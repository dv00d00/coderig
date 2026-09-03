using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Live;
using Rig.Domain.Data;

namespace Rig.Cli.Commands;

// `rig watch <solution>` — the resident host for the live background index (the integration slice of
// docs/backlog/progress/live-background-index.md). Cold-analyzes the solution ONCE retaining the Roslyn
// workspace, then watches the source tree: each debounced .cs save burst is re-extracted and published
// atomically while the dependent cascade remains explicitly unreconciled. ResidentIndex deliberately
// owns no threads/timers — this command owns the watcher, debounce, publication and status lines.
//
// Queries are served three ways, all off the same fact generation: `--query` (one answer at boot), the stdin
// loop, and — in watch mode — a named-pipe endpoint named after this working directory, so a plain one-shot
// `rig reaches/path/callers/tree` in the same directory answers from these live facts instead of the store.
// See LiveQueryTransport for why the endpoint is a derived pipe name and not a published port.
internal static class WatchCommand
{
    internal static Command Build(TextWriter output, TextWriter error, string workingDirectory)
    {
        var target = CommonOptions.Pattern(name: "solution", description: "Solution (.slnx/.sln) to keep live-indexed.");
        var rules = CommonOptions.Rules();
        var once = new Option<bool>("--once")
        {
            Description = "Cold-boot the resident index, print the status line, and exit (no watching).",
        };
        var noServe = new Option<bool>("--no-serve")
        {
            Description =
                "Maintain live facts for this process's own stdin only — do not publish the query endpoint, and do not "
                + "refuse to start when another resident index already owns this directory. For deliberately running a "
                + "second host; a resident index costs GBs of RAM, so this is not the default.",
        };
        var query = new Option<string?>("--query")
        {
            Description =
                "Answer one query against the booted resident facts (e.g. --query \"reaches Program.Main\"). "
                + "Composes with --once: boot, answer, exit.",
        };
        // Same flag, same default, same wording as `rig index --restore` (opt-in since eb6480ff): the
        // Restore target is the dominant cost of the build phase and rig normally indexes a tree someone
        // already built. It was reachable through AnalyzeRetainingWorkspaceAsync but had no flag here, so
        // an unrestored checkout could not be booted at all — every compilation came out effectively
        // empty (1.79M "Predefined type 'System.Object' is not defined") and every answer was 0.
        var restore = new Option<bool>("--restore")
        {
            Description =
                "Run the MSBuild Restore target before each design-time build (off by default; needed only "
                + "when the tree has not been restored/built yet — an unrestored checkout resolves no "
                + "references, so the compilation is effectively EMPTY and every answer is 0).",
        };
        var verifyCascadeGate = new Option<bool>("--verify-cascade-gate")
        {
            Description =
                "Verify each would-be body-only surface classification by re-extracting the cascade it would skip. "
                + "A mismatch publishes the fresh coarse facts and permanently disables the gate for that project in this process.",
        };
        // The scheduler for the cascade debt ApplyEdits deliberately OWES. Without it the first .cs save of a
        // session leaves `k project(s) unreconciled` set forever — nothing in production ever called
        // ReconcileAllAsync — so every `file-effects` request fails closed as stale and a Rider session goes
        // dark after one keystroke-save. Quiet period, not interval: a reconcile costs the cascade, so it must
        // fire once after a burst settles, not once per save.
        var reconcileQuietPeriod = new Option<int>("--reconcile-quiet-period")
        {
            Description =
                "Milliseconds of save quiet before the owed dependent cascade is reconciled in the background "
                + "(default 750). 0 disables the loop: the eager facts stay published but the debt is never paid, "
                + "so from the FIRST save onwards every served answer that requires reconciled facts — Rider's "
                + "file effects above all — is declined as stale until something explicitly reconciles.",
            DefaultValueFactory = _ => 750,
        };
        var cmd = new Command(
            name: "watch",
            description: "Live background index: cold-analyze once retaining the workspace, then atomically publish each "
                + "debounced .cs save batch (dependent reconciliation is explicit; no store is written)."
        )
        {
            target,
            rules,
            once,
            query,
            noServe,
            restore,
            verifyCascadeGate,
            reconcileQuietPeriod,
        };
        cmd.SetAction(pr =>
            CommandGuard.RunGuardedAsync(
                workingDirectory,
                error,
                () =>
                    RunAsync(
                        target: pr.GetValue(target)!,
                        extraRules: CommonOptions.RulesOf(pr.GetValue(rules)),
                        once: pr.GetValue(once),
                        query: pr.GetValue(query),
                        noServe: pr.GetValue(noServe),
                        restore: pr.GetValue(restore),
                        verifyCascadeGate: pr.GetValue(verifyCascadeGate),
                        reconcileQuietPeriod: TimeSpan.FromMilliseconds(Math.Max(0, pr.GetValue(reconcileQuietPeriod))),
                        output: output,
                        error: error,
                        workingDirectory: workingDirectory
                    )
            )
        );
        return cmd;
    }

    private static async Task<int> RunAsync(
        string target,
        IReadOnlyList<string> extraRules,
        bool once,
        string? query,
        bool noServe,
        bool restore,
        bool verifyCascadeGate,
        TimeSpan reconcileQuietPeriod,
        TextWriter output,
        TextWriter error,
        string workingDirectory
    )
    {
        var solutionPath = IndexCommands.ResolveSolutionPath(target: target, workingDirectory: workingDirectory);
        // Rules anchored at the working directory, exactly like `index` — the resident facts must be
        // extracted with the same rule set a cold index of this directory would use.
        var ruleSet = RuleSetLoader.Load(workingDirectory, extraRules);
        // The dtb cache is NOT optional garnish: without it the cold boot pays a full MSBuild pass
        // (see AnalyzeRetainingWorkspaceAsync's note). Same location `index` uses, so they share hits.
        var buildCacheDir = Path.Combine(StoreLayout.RigDir(workingDirectory), "dtb-cache");

        // OWNERSHIP, CHECKED BEFORE THE COLD BOOT — and the ordering is the whole point. A resident index costs
        // 10.8 GB at boot and ~19 GB after an edit+reconcile on a 227-project solution, so discovering the clash
        // AFTER analysing would already have spent the thing the check exists to protect. Refusing here costs
        // nothing.
        //
        // Two hosts on one directory is not a supported configuration: both would bind (the endpoint allows
        // several listener instances so one stays armed while another is served), whichever accepts first would
        // answer, and if they booted with different rules their answers would differ with no way for a client to
        // tell. `--no-serve` is the deliberate escape hatch. `--once` never publishes an endpoint, so it never
        // clashes.
        if (!once && !noServe && LiveQueryTransport.ServerExists(LiveQueryTransport.PipeNameFor(workingDirectory)))
        {
            error.WriteLine($"rig: a resident index is already watching {workingDirectory}");
            error.WriteLine($"     (endpoint {LiveQueryTransport.EndpointPath(LiveQueryTransport.PipeNameFor(workingDirectory))})");
            error.WriteLine("");
            error.WriteLine(
                "     Stop it first, or pass --no-serve to run a second host that maintains facts but does not answer queries."
            );
            return 2;
        }

        output.WriteLine($"watch: {solutionPath}");
        var bootWatch = Stopwatch.StartNew();
        WatchHost host;
        try
        {
            host = await WatchHost.StartAsync(
                solutionPath: solutionPath,
                rules: ruleSet,
                buildCacheDir: buildCacheDir,
                output: output,
                error: error,
                watch: !once,
                workingDirectory: workingDirectory,
                restore: restore,
                verifyCascadeGate: verifyCascadeGate,
                reconcileQuietPeriod: reconcileQuietPeriod
            );
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // Same clean failure `index` gives for a missing/unloadable target.
            error.WriteLine("Failed to load solution/project for analysis.");
            error.WriteLine(exception.ToString());
            error.WriteLine("Ensure the target solution has been restored and builds successfully, then retry.");
            return 2;
        }

        await using (host)
        {
            bootWatch.Stop();
            output.WriteLine(
                FormattableString.Invariant(
                    $"watch: cold boot in {bootWatch.Elapsed.TotalSeconds:F1}s — {host.ProjectCount} project(s), workspace retained"
                )
            );
            // --query: one answer off the freshly booted facts. Printed BEFORE the watch loop starts so
            // `--once --query …` is a complete boot-answer-exit, and a plain `--query …` seeds the session
            // with an answer before the first edit lands. Every answer is already prefixed with the same
            // staleness disclosure the boot status line carries, so printing both would just duplicate it.
            if (query is null)
            {
                output.WriteLine(await host.GetStatusLineAsync());
            }
            else
            {
                output.WriteLine(await host.AnswerQueryAsync(query));
            }

            if (once)
            {
                return 0;
            }

            // THE ONE-SHOT TRANSPORT — what makes the resident index reachable by anything other than this
            // process's own stdin. Published only in WATCH mode: `--once` exits within milliseconds, so an
            // endpoint there would be created and torn down before any client could find it, and a client that
            // did catch the window would be racing a shutdown.
            //
            // The endpoint's NAME is derived from `workingDirectory`, which is also what a one-shot `rig`
            // invocation hashes — so there is nothing to publish, nothing to keep fresh, and nothing to go
            // stale. See LiveQueryTransport.
            // TryStart, not Start: a query endpoint that cannot be published must not cost the resident loop.
            // The host still maintains live facts and still answers its own stdin; it just says so.
            //
            // Re-checked here as well as pre-boot: two hosts started within the same boot window would both
            // have passed the earlier probe. This narrows that race to the width of a single probe rather than
            // the width of a cold boot. It cannot close it entirely, and a host that loses the race declines to
            // serve rather than binding alongside.
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var owned = !noServe && !LiveQueryTransport.ServerExists(LiveQueryTransport.PipeNameFor(workingDirectory));
            await using var server = owned
                ? LiveQueryServer.TryStart(
                    workingDirectory,
                    host.ServeAsync,
                    output,
                    serveFileEffects: host.ServeFileEffectsAsync,
                    serveWatchControl: host.ServeWatchControlAsync,
                    requestWatchRestart: () =>
                    {
                        stopped.TrySetResult();
                        output.WriteLine("watch: restart requested by a local client.");
                    }
                )
                : null;
            if (!owned)
            {
                output.WriteLine(
                    noServe
                        ? "watch: NOT serving (--no-serve) — one-shot `rig reaches/path/callers/tree` will read the .rig store. This host answers its own stdin only."
                        : "watch: NOT serving — another resident index claimed this directory's endpoint while this one was booting. This host answers its own stdin only."
                );
            }

            if (server is not null)
            {
                output.WriteLine(
                    $"watch: serving `reaches`, `path`, `callers` and `tree` for {workingDirectory} over {LiveQueryTransport.EndpointPath(server.PipeName)} "
                        + "— those commands now answer from these live facts; pass --no-live (or set RIG_NO_LIVE=1) to read the .rig store instead."
                );
            }
            output.WriteLine(
                "watch: watching for .cs saves (obj/ and bin/ excluded). Type a query "
                    + $"({LiveQueryRunner.Usage}) or press Ctrl+C to stop."
            );
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                e.Cancel = true; // we own the shutdown: dispose the host cleanly, exit 0
                stopped.TrySetResult();
            };
            Console.CancelKeyPress += onCancel;
            try
            {
                // Three ways out: Ctrl+C, a local control request, or `quit` on stdin. Cancel the pending
                // console read after either external stop: on Unix an abandoned ReadLineAsync can retain a
                // blocking runtime thread and keep the otherwise-disposed process alive.
                var stopReading = new CancellationTokenSource();
                // Console's Unix reader can block before ReadLineAsync returns its task. Enter it on the
                // pool so constructing the WhenAny inputs cannot pin this command before `stopped` is armed.
                var readQueries = Task.Run(() => ReadQueriesAsync(host, output, stopped.Task, stopReading.Token));
                await Task.WhenAny(stopped.Task, readQueries);
                // CancelAsync waits for Console's cancellation callback; on Unix that callback can itself
                // wait for the terminal read. Let the timer deliver cancellation while shutdown continues.
                stopReading.CancelAfter(TimeSpan.FromMilliseconds(1));
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }

            output.WriteLine("watch: stopped.");
            return 0;
        }
    }

    // Read query lines from stdin and answer each against the CURRENT fact generation. Blank lines are ignored
    // rather than answered, so a stray Enter at a terminal doesn't print the usage banner.
    //
    // EOF does NOT stop the watcher — it stops READING. That distinction is load-bearing: ReadLineAsync returns
    // null immediately when stdin is closed or attached to the null device, which is exactly how a daemon or a
    // background launcher starts a process, so treating EOF as "exit" would make `rig watch` terminate
    // instantly and silently for anyone not sitting at a terminal. `quit`/`exit` and Ctrl+C are the ways out;
    // a piped caller that wants the process to end appends `quit`.
    private static async Task ReadQueriesAsync(WatchHost host, TextWriter output, Task stopped, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var line = await Console.In.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    await stopped.WaitAsync(cancellationToken); // stdin is not a query source here; hand exit to an external stop
                    return;
                }

                var trimmed = line.Trim();
                if (
                    string.Equals(trimmed, "quit", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trimmed, "exit", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return;
                }

                if (trimmed.Length == 0)
                {
                    continue;
                }

                output.WriteLine(await host.AnswerQueryAsync(line));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}

// The resident loop, split from the command action so a test can drive it end-to-end (boot → disk
// edit → poll) without a console. Owns the FileSystemWatcher, debounce, one publication worker and the
// debounced background reconcile loop. `_gate` keeps status/query capture coherent with publication.
// Reconciliation stays a plain awaitable primitive (ResidentIndex owns no threads); what schedules it is
// the quiet-period loop below, which is what stops the debt from a save being owed forever. The opt-in
// cascade verifier still reconciles inline, before the status line, on its own edit path.
internal sealed class WatchHost : IAsyncDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    private readonly ResidentIndex _index;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly RuleSet _rules;
    private readonly string _workingDirectory;
    private readonly TimeSpan _debounce;
    private readonly bool _verifyCascadeGate;
    private readonly Channel<string> _changes = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Serializes every edit publication with foreground exact refinement. Both paths take this BEFORE
    // _gate, so a query cannot win ResidentIndex's CAS after an edit captured its basis and kill the
    // single watcher worker. FileSystemWatcher callbacks remain lock-free and keep filling _changes.
    private readonly SemaphoreSlim _publicationGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly FileSystemWatcher? _watcher;
    private readonly Task _loop;

    // The reconcile SCHEDULER. ResidentIndex owns no threads by design, so the debt ApplyEdits owes has to be
    // paid by something in the host — and until this loop existed nothing paid it: `ReconcileAllAsync` had no
    // production caller at all, so the first save of a session pinned `k project(s) unreconciled` for the
    // process lifetime and every file-effects request was declined as stale from then on.
    //
    // An unbounded channel, not a flag+event: a signal written while a reconcile is RUNNING survives in the
    // channel and the next ReadAsync returns immediately, so an edit that lands mid-reconcile schedules the
    // next one instead of being lost.
    private readonly Channel<byte> _reconcileSignals = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TimeSpan _reconcileQuietPeriod;
    private readonly Func<CancellationToken, Task<bool>> _reconcile;
    private readonly Task _reconcileLoop;

    private int _appliedFiles;
    private int _reconcileAttempts;

    // STICKY once set, for the process lifetime, and deliberately not clearable. A FileSystemWatcher
    // overflow means events were dropped before we saw them, so we do not know WHICH files went stale —
    // and a background reconcile cannot recover them, because it only re-extracts the cascade of files it
    // knows are dirty. Only a cold boot re-reads everything. So this is a permanent "some edits may be
    // missing" caveat on every subsequent answer, not a transient condition.
    private int _watcherOverflowed;
    private int _topologyChanged;
    private double _lastEditSeconds = -1;

    // The query-ready derived layer for the CURRENT immutable snapshot, built on first query and thrown away
    // the moment publication moves. Snapshot reference identity is the correctness token; the revision is
    // diagnostic only.
    private LiveFactSource? _liveFacts;
    private FactSnapshot? _liveFactsFor;

    // The indexed-path set for the CURRENT fact generation, and the snapshot it belongs to.
    private IReadOnlySet<string>? _indexedFiles;
    private FactSnapshot? _indexedFilesFor;

    private WatchHost(
        ResidentIndex index,
        string solutionPath,
        RuleSet rules,
        string workingDirectory,
        TextWriter output,
        TextWriter error,
        bool watch,
        TimeSpan debounce,
        bool verifyCascadeGate,
        TimeSpan reconcileQuietPeriod,
        Func<CancellationToken, Task<bool>>? reconcile
    )
    {
        _index = index;
        _output = output;
        _error = error;
        _rules = rules;
        _workingDirectory = workingDirectory;
        _debounce = debounce;
        _verifyCascadeGate = verifyCascadeGate;
        _reconcileQuietPeriod = reconcileQuietPeriod;
        _reconcile = reconcile ?? ReconcileAllAsync;
        ProjectCount = index.CurrentSolution.ProjectIds.Count;
        _loop = Task.CompletedTask;
        _reconcileLoop = Task.CompletedTask;

        if (!watch)
        {
            return;
        }

        // Watch the solution's whole source tree. obj/ and bin/ churn constantly during builds
        // (generated AssemblyInfo.cs, compiler artifacts) and would thrash the index — excluded in
        // Enqueue, path-segment-wise, so nested project obj/ dirs are caught too.
        var root = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        _watcher = new FileSystemWatcher(root, "*.cs")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            // 64 KB (the documented max for network paths, 8x the default): a branch switch touches
            // hundreds of files in a burst, and an overflowed buffer DROPS events silently.
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Changed += (_, e) => Enqueue(e.FullPath);
        _watcher.Created += (_, e) => Enqueue(e.FullPath);
        _watcher.Deleted += (_, e) => Enqueue(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Enqueue(e.OldFullPath);
            Enqueue(e.FullPath);
        };
        // Overflow is a staleness hazard, not a crash: disclose it — rig's contract is never to serve
        // silently-stale facts as current.
        _watcher.Error += (_, e) =>
        {
            // Record it BEFORE printing: the console line only reaches whoever is watching the terminal,
            // while the status segment reaches every answer, including one served over the transport to a
            // client that cannot see this process at all.
            Interlocked.Exchange(ref _watcherOverflowed, 1);
            _output.WriteLine(
                $"live: WATCHER OVERFLOW — changes may have been missed; edits since may not be reflected ({e.GetException().Message})"
            );
        };
        _watcher.EnableRaisingEvents = true;
        _loop = Task.Run(() => RunLoopAsync(_shutdown.Token));
        // Zero disables the scheduler entirely — the pre-loop behaviour, kept reachable because the debt
        // disclosure is a deliberate product surface and a caller may want to pay it explicitly.
        if (_reconcileQuietPeriod > TimeSpan.Zero)
        {
            _reconcileLoop = Task.Run(() => RunReconcileLoopAsync(_shutdown.Token));
        }
    }

    public int ProjectCount { get; }

    // Total files applied through the eager arm since boot — the `n` of the status line, and the
    // signal a test polls to know the watcher fired.
    public int AppliedFileCount => Volatile.Read(ref _appliedFiles);

    // Reconciles ATTEMPTED by the background loop since boot, counted before the call so a failed one still
    // shows. The observable seam for "a burst coalesces into ONE reconcile" and "a failure does not kill the
    // loop" — both of which are otherwise only visible as console prose a test would have to re-format.
    internal int ReconcileAttemptCount => Volatile.Read(ref _reconcileAttempts);

    public static async Task<WatchHost> StartAsync(
        string solutionPath,
        RuleSet rules,
        string? buildCacheDir,
        TextWriter output,
        bool watch,
        // Where the compile-health footer note goes. STDERR by contract (the existing stderr-notice
        // family), so the answer on stdout stays greppable. Null = discard, for a test that only reads
        // the status line.
        TextWriter? error = null,
        TimeSpan? debounce = null,
        // Where a live query resolves rules and deployments.json from — the directory `rig` was invoked in,
        // exactly as a store-backed query command uses it. Defaults to the solution's own directory, which is
        // what a test driving the host directly wants.
        string? workingDirectory = null,
        // `rig watch --restore`: run the MSBuild Restore target before each design-time build. Off by
        // default, exactly as `rig index --restore` is.
        bool restore = false,
        // Opt-in safety oracle: after each edit publication, reconcile through the private cascade
        // verifier before the status line is emitted. Default watch remains lazy.
        bool verifyCascadeGate = false,
        // How long the saves must stay quiet before the owed dependent cascade is reconciled in the
        // background. DEFAULTS TO ZERO (disabled) here while the `watch` command defaults to 750 ms: a host
        // constructed directly is being driven by a test that asserts on the debt itself, and a scheduler
        // that fires on its own would race every one of those assertions. The CLI passes its value explicitly.
        TimeSpan? reconcileQuietPeriod = null,
        // Test seam: what the background loop calls instead of ReconcileAllAsync. Lets a test count
        // reconciles and make one fail without racing a real cascade.
        Func<CancellationToken, Task<bool>>? reconcile = null,
        CancellationToken cancellationToken = default
    )
    {
        // ONE host-lifetime interner shared by the cold boot AND every ResidentIndex re-extraction, so a
        // reconcile generation's retained strings alias the base facts' instead of duplicating the whole
        // string set per edit (see StringInterner).
        var interner = Rig.Analysis.Extraction.StringInterner.CreateDefault();
        var (baseFacts, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(
            solutionPath: solutionPath,
            rules: rules,
            cancellationToken: cancellationToken,
            // Match `rig index` defaults: tests excluded, dtb cache on (the caller passes the dir).
            excludeTests: true,
            buildCacheDir: buildCacheDir,
            restore: restore,
            interner: interner
        );
        var index = new ResidentIndex(workspace, baseFacts, solutionPath, rules, interner: interner, verifyCascadeGate: verifyCascadeGate);
        return new WatchHost(
            index,
            solutionPath,
            rules,
            workingDirectory ?? Path.GetDirectoryName(Path.GetFullPath(solutionPath))!,
            output,
            error ?? TextWriter.Null,
            watch,
            debounce ?? DefaultDebounce,
            verifyCascadeGate,
            reconcileQuietPeriod ?? TimeSpan.Zero,
            reconcile
        );
    }

    public async Task<string> GetStatusLineAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        string status;
        try
        {
            var snapshot = _index.CaptureSnapshot();
            status = ComposeStatus(snapshot);
            WriteCompilationHealthNote(snapshot);
        }
        finally
        {
            _gate.Release();
        }

        return status;
    }

    public async Task<IReadOnlyCollection<string>> GetUnreconciledProjectsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return UnreconciledProjects(_index.CaptureSnapshot());
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<long> GetCurrentRevisionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _index.CaptureSnapshot().Revision.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Answer one query against the CURRENT fact generation, returning the rendered text.
    //
    // The answer is always PREFIXED with the staleness disclosure (the same line ComposeStatus feeds the
    // status output): serving an answer about a partially-reconciled tree without saying so is precisely the
    // failure this program exists to remove, and an answer is the moment it matters most — a status line
    // printed thirty seconds ago is not a disclosure attached to THIS answer. The prefix is captured under the
    // same lock as the fact generation, so the disclosure and the facts it describes cannot disagree.
    //
    // The derived layer (traversal graph, EP facts, effects) is built ONCE per generation and reused: the
    // per-artifact first-access cost is appended as a trailing measurement line, which is the only
    // instrumentation of what a live QUERY costs on top of the ~0.75s fact latency.
    //
    // The gate is released BEFORE the query runs. LiveFactSource is an immutable value over one generation, so
    // a concurrent edit cannot corrupt this answer — at worst it makes it a (correctly disclosed) answer about
    // the generation that was current when it started. Holding the gate for the whole traversal would instead
    // block the worker's next apply behind it, which is the one thing the resident loop must never do.
    public async Task<string> AnswerQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        // Parse/validate before capturing or refining. A malformed/unsupported query follows the normal
        // rejection path and pays zero resident work.
        var demand = LiveQueryRunner.PrepareTextExactDemand(query, _rules, DeploymentsConfigured());
        var capture = await CaptureForQueryAsync(demand, sourceDisclosure: false, cancellationToken);
        foreach (var line in capture.Health)
        {
            _error.WriteLine(line);
        }
        if (capture.UnavailableReason is not null)
        {
            var refused = LiveQueryRunner.ExactUnavailable(demand!.Verb, capture.Snapshot.Revision.Value, capture.UnavailableReason);
            return $"{capture.Disclosure}\n{refused.Text.TrimEnd('\r', '\n')}";
        }

        var facts = capture.Facts!;
        var built = facts.BuildTimes.Count; // artifacts already memoized before this query
        var answer = await LiveQueryRunner.AnswerAsync(query, facts, _workingDirectory);
        var costLine = facts.BuildTimes.Count == built ? "" : $"\nlive: derived layer built this generation: {facts.BuildTimeLine()}";
        return $"{capture.Disclosure}\n{answer.Text.TrimEnd('\r', '\n')}{costLine}";
    }

    // Answer one TRANSPORT request — a one-shot `rig reaches/path/callers/tree` in this working directory,
    // arriving over the named pipe. Same fact generation, same command bodies and same disclosure discipline
    // as AnswerQueryAsync; what differs is where the three parts of the answer go.
    //
    // The split is deliberate and it is the reason the response record has three text fields rather than one.
    // stdout carries ONLY what the command wrote, byte for byte, so `--format tsv` survives the trip and a
    // routed answer is directly comparable to an in-process one. Everything the HOST has to say — the
    // source/staleness line, the compile-health note, the derived-layer cost — goes to stderr, which is where
    // rig already puts every disclosure precisely so stdout stays parseable.
    public async Task<LiveServeResult> ServeAsync(LiveQueryRequest request, CancellationToken cancellationToken = default)
    {
        var demand = LiveQueryRunner.PrepareTransportExactDemand(request, _rules, DeploymentsConfigured());
        var capture = await CaptureForQueryAsync(demand, sourceDisclosure: true, cancellationToken);
        // PREPARATION-time unavailability DECLINES, exactly as an execution-time demand failure does below.
        // Both are "the resident index cannot answer this question"; rendering one as an exit-2 answer and
        // the other as a decline gave the same failure two opposite outcomes — a store answer for one, a
        // terminal error and no answer at all for the other. Every producer of UnavailableReason routes here:
        // a failed demand refinement, the sticky watcher-overflow and topology-changed flags, and the
        // repeated-supersession case. The client discloses the reason and reads the store (LiveRoute), which
        // then discloses which snapshot answered (StoreAnswerDisclosure) — so the fallback names both halves.
        //
        // The compile-health note is deliberately dropped on this path: it describes the RESIDENT facts, and
        // the answer the user is about to get comes from the store, whose own provenance line describes it.
        if (capture.UnavailableReason is not null)
        {
            return LiveServeResult.Declined(
                LiveQueryRunner.ExactUnavailableDecline(demand!.Verb, capture.Snapshot.Revision.Value, capture.UnavailableReason)
            );
        }

        var facts = capture.Facts!;
        var built = facts.BuildTimes.Count; // artifacts already memoized before this query
        var result = await LiveQueryRunner.RunRequestAsync(request, facts, _workingDirectory);
        if (result.DeclineReason is not null)
        {
            return LiveServeResult.Declined(result.DeclineReason);
        }

        var answer = result.Answer!;
        var notes = new StringBuilder();
        foreach (var line in capture.Health)
        {
            notes.Append(line).Append('\n');
        }

        if (answer.Err.Length > 0)
        {
            notes.Append(answer.Err);
            if (!answer.Err.EndsWith('\n'))
            {
                notes.Append('\n');
            }
        }

        if (facts.BuildTimes.Count != built)
        {
            notes.Append($"live: derived layer built this generation: {facts.BuildTimeLine()}").Append('\n');
        }

        return LiveServeResult.Answered(
            exit: answer.Exit,
            standardOut: answer.Out,
            standardError: notes.ToString(),
            disclosure: capture.Disclosure
        );
    }

    // Rider's whole-file semantic read. Capture is deliberately the ONLY region under `_gate`: the immutable
    // snapshot, its generation, the generation-owned LiveFactSource and exactness inputs move together. The
    // reverse closure is forced by RiderFileEffectResponder only AFTER the gate has been released.
    internal async Task<RiderFileEffectResponse> ServeFileEffectsAsync(
        RiderFileEffectRequest request,
        CancellationToken cancellationToken = default
    )
    {
        RiderFileEffectCapture capture;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = _index.CaptureSnapshot();
            var facts = CurrentLiveFacts(snapshot);
            var scope = RiderFileEffectResponder.IndexedProjectScope(snapshot, request.FilePath);
            capture = new RiderFileEffectCapture(
                snapshot.Revision.Value,
                scope.Contexts,
                FileEffectUnavailableReason(snapshot, request.FilePath, scope.ProjectNames),
                () => facts.FileEffects(RiderFileEffectResponder.SelectorsFor(_rules))
            );
        }
        finally
        {
            _gate.Release();
        }

        return RiderFileEffectResponder.Respond(request, capture);
    }

    internal async Task<RiderWatchControlResponse> ServeWatchControlAsync(
        RiderWatchControlRequest request,
        CancellationToken cancellationToken = default
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = _index.CaptureSnapshot();
            return RiderWatchControlResponder.Answer(request, snapshot.Revision.Value, HostUnavailableReason(snapshot)?.Text);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Caller holds `_gate`. The causes that are NOT attributable to any one file: a topology change and a
    // watcher overflow invalidate the whole resident generation, and an error diagnostic with no source
    // location cannot be pinned to a file or a project, so it keeps failing closed for everyone.
    private FileEffectUnavailable? HostUnavailableReason(FactSnapshot snapshot)
    {
        if (Volatile.Read(ref _topologyChanged) != 0)
        {
            return new FileEffectUnavailable(
                RiderFileEffectResponder.ReasonTopologyChanged,
                RiderFileEffectResponder.ScopeHost,
                TopologyStatusSegment
            );
        }

        if (Volatile.Read(ref _watcherOverflowed) != 0)
        {
            return new FileEffectUnavailable(
                RiderFileEffectResponder.ReasonWatcherOverflow,
                RiderFileEffectResponder.ScopeHost,
                "file-watcher overflowed — exact file effects cannot be established; restart required"
            );
        }

        if (snapshot.GetCompilationHealth() is { UnlocatedErrorCount: > 0 } unlocated)
        {
            return new FileEffectUnavailable(
                RiderFileEffectResponder.ReasonUnlocatedCompileErrors,
                RiderFileEffectResponder.ScopeHost,
                $"{unlocated.UnlocatedErrorCount} error diagnostic(s) carry no source location, so no file can be cleared"
            );
        }

        return null;
    }

    // Caller holds `_gate`. The host-wide causes first, then the file-scoped decision (a pure function in the
    // responder, so it is testable without a resident host).
    private FileEffectUnavailable? FileEffectUnavailableReason(FactSnapshot snapshot, string filePath, IReadOnlySet<string> projectNames) =>
        HostUnavailableReason(snapshot)
        ?? RiderFileEffectResponder.UnavailableForFile(
            filePath,
            projectNames,
            snapshot
                .Dirty.PendingProjects.Select(id => snapshot.Solution.GetProject(id)?.Name)
                .Where(name => name is not null)
                .Select(name => name!)
                .ToArray(),
            snapshot.GetCompilationHealth()
        );

    // Commands with keyed demand topology capture their planning basis briefly under the
    // host gate, do all Roslyn work outside that gate, then accept only the exact snapshot reference returned
    // by ResidentIndex. The publication mutex prevents duplicate refinements and edit-CAS races without
    // blocking lock-free watcher event capture.
    private async Task<QueryCapture> CaptureForQueryAsync(
        IExactQueryDemand? demand,
        bool sourceDisclosure,
        CancellationToken cancellationToken
    )
    {
        if (demand is null)
        {
            return await CaptureCurrentAsync(sourceDisclosure, cancellationToken);
        }

        await _publicationGate.WaitAsync(cancellationToken);
        try
        {
            const int MaxCaptureAttempts = 4;
            for (var attempt = 0; attempt < MaxCaptureAttempts; attempt++)
            {
                FactSnapshot basis;
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    basis = _index.CaptureSnapshot();
                }
                finally
                {
                    _gate.Release();
                }

                ExactForwardRefinementOutcome outcome;
                if (Volatile.Read(ref _topologyChanged) != 0)
                {
                    outcome = ExactForwardRefinementOutcome.Unavailable(basis, TopologyStatusSegment);
                }
                else if (Volatile.Read(ref _watcherOverflowed) != 0)
                {
                    outcome = ExactForwardRefinementOutcome.Unavailable(
                        basis,
                        "file-watcher overflowed — exactness cannot be established; restart required"
                    );
                }
                else
                {
                    outcome = demand switch
                    {
                        ExactForwardDemand forward => await _index.EnsureExactForwardAsync(basis, forward, cancellationToken),
                        ExactCallersDemand callers => await _index.EnsureExactCallersAsync(basis, callers, cancellationToken),
                        _ => ExactForwardRefinementOutcome.Unavailable(basis, $"exact {demand.Verb} refinement is not implemented"),
                    };
                }

                await _gate.WaitAsync(cancellationToken);
                try
                {
                    var current = _index.CaptureSnapshot();
                    // Watcher callbacks are lock-free and can turn either sticky flag on WHILE Roslyn work
                    // is in flight. Recheck under final capture; snapshot identity alone cannot prove the
                    // retained workspace still covers the filesystem topology.
                    if (Volatile.Read(ref _topologyChanged) != 0)
                    {
                        return CaptureSnapshot(current, sourceDisclosure, TopologyStatusSegment);
                    }
                    if (Volatile.Read(ref _watcherOverflowed) != 0)
                    {
                        return CaptureSnapshot(
                            current,
                            sourceDisclosure,
                            "file-watcher overflowed — exactness cannot be established; restart required"
                        );
                    }
                    if (outcome.Kind == ExactForwardRefinementKind.Superseded || !ReferenceEquals(current, outcome.Snapshot))
                    {
                        continue;
                    }

                    if (outcome.Kind == ExactForwardRefinementKind.ExactPublished)
                    {
                        ReleasePublishedSnapshotCaches();
                    }

                    return CaptureSnapshot(
                        current,
                        sourceDisclosure,
                        outcome.Kind == ExactForwardRefinementKind.ExactUnavailable ? outcome.Reason : null
                    );
                }
                finally
                {
                    _gate.Release();
                }
            }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                return CaptureSnapshot(
                    _index.CaptureSnapshot(),
                    sourceDisclosure,
                    $"resident snapshot changed repeatedly while exact {demand.Verb} refinement was running"
                );
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            _publicationGate.Release();
        }
    }

    private bool DeploymentsConfigured() => File.Exists(Path.Combine(_workingDirectory, "deployments.json"));

    private async Task<QueryCapture> CaptureCurrentAsync(bool sourceDisclosure, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return CaptureSnapshot(_index.CaptureSnapshot(), sourceDisclosure, unavailableReason: null);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Caller holds _gate. Every field derives from the same immutable reference.
    private QueryCapture CaptureSnapshot(FactSnapshot snapshot, bool sourceDisclosure, string? unavailableReason) =>
        new(
            snapshot,
            unavailableReason is null ? CurrentLiveFacts(snapshot) : null,
            sourceDisclosure ? ComposeSourceDisclosure(snapshot) : ComposeStatus(snapshot),
            ComposeCompilationHealthNote(snapshot),
            unavailableReason
        );

    private sealed record QueryCapture(
        FactSnapshot Snapshot,
        LiveFactSource? Facts,
        string Disclosure,
        IReadOnlyList<string> Health,
        string? UnavailableReason
    );

    // Explicit compatibility/oracle boundary for tests and callers that still require AnalysisResult.
    // The resident query/status path captures and consumes FactSnapshot directly.
    public async Task<AnalysisResult> GetCurrentFactsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _index.CaptureSnapshot().FlattenedFacts;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }

        _changes.Writer.TryComplete();
        _reconcileSignals.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException) { }

        // Awaited too: a reconcile in flight holds `_gate` and the index, both of which are disposed below.
        try
        {
            await _reconcileLoop;
        }
        catch (OperationCanceledException) { }

        _shutdown.Dispose();
        _publicationGate.Dispose();
        _gate.Dispose();
        _index.Dispose();
    }

    private void Enqueue(string fullPath)
    {
        if (!fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || IsBuildArtifactPath(fullPath))
        {
            return;
        }

        _changes.Writer.TryWrite(fullPath);
    }

    // Internal, not private: the sticky topology flag is one of the four producers of an unavailable
    // preparation, and LiveDeclineFallbackTests drives it directly rather than racing a FileSystemWatcher.
    internal void RecordTopologyChange(string fullPath, string change)
    {
        if (!fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || IsBuildArtifactPath(fullPath))
        {
            return;
        }

        Interlocked.Exchange(ref _topologyChanged, 1);
        _output.WriteLine($"live: source file {change}: {fullPath} — retained solution topology is stale; restart required");
    }

    private static bool IsBuildArtifactPath(string fullPath)
    {
        foreach (var segment in fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }

    // The single worker: debounce a burst of watcher events into one atomic eager publication, then
    // print the status with its persistent dirty disclosure. Default mode does not reconcile or warm;
    // verify mode reconciles under the same host gate before that status is visible.
    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _changes.Reader;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string first;
                try
                {
                    first = await reader.ReadAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ChannelClosedException)
                {
                    break;
                }

                // Debounce: one save fires several watcher events (and editors write in bursts) —
                // absorb until the channel has been quiet for the debounce window, so one save is one
                // apply.
                var batch = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { first };
                while (true)
                {
                    while (reader.TryRead(out var more))
                    {
                        batch.Add(more);
                    }

                    var quiet = Task.Delay(_debounce, cancellationToken);
                    var next = reader.WaitToReadAsync(cancellationToken).AsTask();
                    if (await Task.WhenAny(quiet, next) == quiet)
                    {
                        break;
                    }

                    if (!await next)
                    {
                        break; // channel completed (shutdown) — apply what we have
                    }
                }

                var applied = await ApplyBatchAsync(batch, cancellationToken);
                if (applied == 0)
                {
                    continue;
                }

                _output.WriteLine(await GetStatusLineAsync(cancellationToken));
                // Signalled HERE — after ApplyBatchAsync returned, so both `_publicationGate` and `_gate` are
                // released — and never from inside them: ReconcileAllAsync takes `_gate` itself, so scheduling
                // while holding it would deadlock the moment the loop ran promptly.
                RequestReconcile();
            }
        }
        catch (OperationCanceledException) { }
    }

    // Ask the background loop for a reconcile once the saves go quiet. Non-blocking and lock-free, callable
    // from anywhere; a no-op when the loop is disabled so the channel cannot grow unread.
    internal void RequestReconcile()
    {
        if (_reconcileQuietPeriod > TimeSpan.Zero)
        {
            _reconcileSignals.Writer.TryWrite(0);
        }
    }

    // The debt scheduler: wait for a signal, wait out the quiet period (RESET by any further applied batch,
    // so N rapid saves cost ONE cascade rather than N), then pay. It holds NEITHER gate while waiting — the
    // reconcile primitive takes `_gate` for itself and a query path takes `_publicationGate` before it, so a
    // scheduler that waited under either would stall every answer for the whole quiet window.
    private async Task RunReconcileLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _reconcileSignals.Reader;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await reader.ReadAsync(cancellationToken);
                }
                catch (ChannelClosedException)
                {
                    break;
                }

                while (true)
                {
                    while (reader.TryRead(out _)) { }

                    var quiet = Task.Delay(_reconcileQuietPeriod, cancellationToken);
                    var next = reader.WaitToReadAsync(cancellationToken).AsTask();
                    if (await Task.WhenAny(quiet, next) == quiet)
                    {
                        break;
                    }

                    if (!await next)
                    {
                        break; // channel completed (shutdown) — pay what is owed and let the loop exit
                    }
                }

                // Drain BEFORE reconciling, never after: a signal that arrives while the cascade below is
                // running describes an edit this generation may not cover, so it must survive to schedule the
                // NEXT reconcile. Draining afterwards would swallow exactly that wakeup.
                while (reader.TryRead(out _)) { }

                await ReconcileOwedDebtAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    // One scheduled payment of the outstanding cascade. A failure is DISCLOSED and the loop lives on: the
    // eager facts are already sound and the debt is still recorded, so the next save simply schedules another
    // attempt — letting one exception kill this task would silently restore the permanent-staleness bug.
    private async Task ReconcileOwedDebtAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reconcileAttempts);
        try
        {
            // Read the owed count outside the reconcile call; `n` is a status number, not a decision, so an
            // apply landing between the two only makes it a slight undercount of what got paid.
            var owed = await PendingProjectCountAsync(cancellationToken);
            var watch = Stopwatch.StartNew();
            if (await _reconcile(cancellationToken))
            {
                _output.WriteLine(FormattableString.Invariant($"live: reconciled {owed} project(s) in {watch.Elapsed.TotalSeconds:F2}s"));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _output.WriteLine($"live: reconcile FAILED: {exception.Message} — debt retained");
        }
    }

    private async Task<int> PendingProjectCountAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _index.CaptureSnapshot().Dirty.PendingProjects.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> ApplyBatchAsync(IReadOnlyCollection<string> batch, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var edits = new Dictionary<string, SourceText>(StringComparer.OrdinalIgnoreCase);
        var solution = _index.CurrentSolution;
        foreach (var path in batch)
        {
            var fullPath = Path.GetFullPath(path);
            if (solution.GetDocumentIdsWithFilePath(fullPath).IsEmpty)
            {
                // Classify topology from the FINAL state after the whole watcher burst, not from event order.
                // A temporary *.cs created during atomic save has disappeared by now and is harmless; a
                // persistent unretained source really is a new document the retained Solution cannot index.
                if (File.Exists(fullPath))
                {
                    RecordTopologyChange(fullPath, "created");
                }
                continue;
            }

            var text = await ReadAllTextWithRetryAsync(fullPath, cancellationToken);
            if (text is null)
            {
                if (!File.Exists(fullPath))
                {
                    RecordTopologyChange(fullPath, "deleted");
                    continue;
                }
                _output.WriteLine($"live: FAILED to apply {batch.Count}-file batch: {fullPath} could not be read; no edits published");
                return 0;
            }

            edits[fullPath] = SourceText.From(text, Encoding.UTF8);
        }

        if (edits.Count == 0)
        {
            return 0;
        }

        await _publicationGate.WaitAsync(cancellationToken);
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                try
                {
                    await _index.ApplyEditsAsync(edits, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _output.WriteLine($"live: FAILED to apply {edits.Count}-file batch: {exception.Message}");
                    return 0;
                }

                if (_verifyCascadeGate)
                {
                    try
                    {
                        await _index.ReconcileAsync(cancellationToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // ApplyEdits already published a sound eager generation with explicit debt. A
                        // verifier failure must not rewrite that as "no edit" or suppress its status;
                        // retain the outstanding debt and disclose the failed optional oracle.
                        _output.WriteLine(
                            $"live: cascade verification FAILED after applying {edits.Count}-file batch: {exception.Message} — eager facts published; debt retained"
                        );
                    }
                }
                ReleasePublishedSnapshotCaches();

                Interlocked.Add(ref _appliedFiles, edits.Count);
                _lastEditSeconds = watch.Elapsed.TotalSeconds;
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            _publicationGate.Release();
        }

        return edits.Count;
    }

    // The scheduler's payment primitive, and still callable explicitly. The batch apply path never calls it
    // inline — the debt is published first and paid after the saves go quiet (RunReconcileLoopAsync) — so an
    // edit is never blocked behind a cascade. Verify mode invokes the same index primitive directly under the
    // host gate so its classification and oracle remain one gated operation.
    public async Task<bool> ReconcileAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var published = await _index.ReconcileAsync(cancellationToken);
            if (published)
            {
                ReleasePublishedSnapshotCaches();
            }

            return published;
        }
        finally
        {
            _gate.Release();
        }
    }

    // The live derived layer for whatever generation of facts is current. Caller must hold `_gate` (it reads
    // the published snapshot). Rebuilding on a snapshot-reference change is the WHOLE invalidation model —
    // no versions to bump, no cache keys, no staleness window.
    private LiveFactSource CurrentLiveFacts(FactSnapshot snapshot)
    {
        if (_liveFacts is null || !ReferenceEquals(_liveFactsFor, snapshot))
        {
            _liveFacts = new LiveFactSource(snapshot, _rules);
            _liveFactsFor = snapshot;
        }

        return _liveFacts;
    }

    // The product surface (program doc, slice 5): `k project(s) unreconciled` IS the staleness
    // disclosure while the cascade is owed; when it is clear, say so plainly.
    //
    // COMPILE HEALTH shares this line, and for the same reason staleness does: a boot banner that
    // scrolled past thirty seconds ago is not a disclosure on THIS answer. The rule that matters is that
    // "all projects reconciled" — a health claim — is NOT emitted when the tree did not compile. It is
    // replaced by what is true, quantified: how many indexed files carried compile errors, and how many
    // projects contributed nothing at all. On a tree that compiles this adds NOTHING, which is the point:
    // a disclosure that fires on a healthy tree is one the reader learns to skip.
    private string ComposeStatus(FactSnapshot snapshot) =>
        snapshot.Dirty.PendingProjects.Count == 0
            ? $"live: facts current as of {StatusBody(snapshot)}"
            : $"live: revision {snapshot.Revision.Value}: affected facts STALE — {StatusBody(snapshot)}";

    // The same facts, led with the SOURCE rather than with currency — what a routed one-shot answer carries.
    // A `rig watch` session already knows where its answers come from; a plain `rig reaches` in another
    // process does not, and "which of the two possible sources answered me" is the first thing its reader
    // needs. The em-dash form is the one the program doc specified for this line.
    private string ComposeSourceDisclosure(FactSnapshot snapshot) =>
        snapshot.Dirty.PendingProjects.Count == 0
            ? $"live: facts from resident index — {StatusBody(snapshot)}"
            : $"live: facts from resident index revision {snapshot.Revision.Value} — affected facts STALE — {StatusBody(snapshot)}";

    // Caller must hold `_gate`.
    private string StatusBody(FactSnapshot snapshot)
    {
        var applied = Volatile.Read(ref _appliedFiles);
        var unreconciled = snapshot.Dirty.PendingProjects.Count;
        var segments = new List<string>();
        if (unreconciled > 0)
        {
            segments.Add($"{unreconciled} project(s) unreconciled");
        }

        var cascadeGateStatus = CascadeGateStatusSegment(snapshot);
        if (cascadeGateStatus is not null)
        {
            segments.Add(cascadeGateStatus);
        }

        segments.AddRange(CompilationHealthNotice.StatusSegments(snapshot.GetCompilationHealth(), IndexedFiles(snapshot)));
        if (Volatile.Read(ref _watcherOverflowed) != 0)
        {
            // Must be a segment rather than a one-off console line: otherwise an answer computed from a
            // generation that silently missed edits still claims "all projects reconciled" — exactly the
            // shape of the broken-compilation defect this host already discloses.
            segments.Add("file-watcher overflowed — some edits may be MISSING; restart to be certain");
        }
        if (Volatile.Read(ref _topologyChanged) != 0)
        {
            segments.Add(TopologyStatusSegment);
        }

        if (segments.Count == 0)
        {
            segments.Add("all projects reconciled");
        }

        var body = $"{applied} file(s) applied | {string.Join(" | ", segments)}";
        return _lastEditSeconds < 0 ? body : FormattableString.Invariant($"{body} | last edit {_lastEditSeconds:F2}s");
    }

    internal static string? CascadeGateStatusSegment(FactSnapshot snapshot) =>
        snapshot.Surfaces.GateDisabledCount == 0
            ? null
            : $"cascade gate disabled for {snapshot.Surfaces.GateDisabledCount} project(s) after verification mismatch — coarse fallback active";

    internal const string TopologyStatusSegment = "source topology changed (create/delete/rename) — facts may be MISSING; restart required";

    // The population the file count is quoted against: the distinct paths this analysis actually
    // indexed. Memoized per immutable snapshot, because it is rebuilt on every status line and answer and MedDBase has ~10.5k
    // rows. Caller must hold `_gate`.
    private IReadOnlySet<string> IndexedFiles(FactSnapshot snapshot)
    {
        if (_indexedFiles is null || !ReferenceEquals(_indexedFilesFor, snapshot))
        {
            _indexedFiles = CompilationHealthNotice.IndexedFileSet(snapshot);
            _indexedFilesFor = snapshot;
        }

        return _indexedFiles;
    }

    // The compile-health footer note for the CURRENT generation, or an empty list when the tree
    // compiled. Gated like every other read accessor so it captures one coherent snapshot.
    public async Task<IReadOnlyList<string>> GetCompilationHealthNoteAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return ComposeCompilationHealthNote(_index.CaptureSnapshot());
        }
        finally
        {
            _gate.Release();
        }
    }

    // Caller must hold `_gate`.
    private IReadOnlyList<string> ComposeCompilationHealthNote(FactSnapshot snapshot)
    {
        return CompilationHealthNotice.Note(snapshot.GetCompilationHealth(), IndexedFiles(snapshot));
    }

    // Emit the footer note on STDERR. Called wherever an answer or a status line is served, because the
    // note is a property of the FACTS, not of one query. Caller must hold `_gate`.
    private void WriteCompilationHealthNote(FactSnapshot snapshot)
    {
        foreach (var line in ComposeCompilationHealthNote(snapshot))
        {
            _error.WriteLine(line);
        }
    }

    private static IReadOnlyCollection<string> UnreconciledProjects(FactSnapshot snapshot) =>
        snapshot
            .Dirty.PendingProjects.Select(id => snapshot.Solution.GetProject(id)?.Name)
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    // Host-owned caches must not pin the generation that publication just retired. Active queries keep
    // their own local reference until they finish; after that, no host history remains.
    private void ReleasePublishedSnapshotCaches()
    {
        _liveFacts = null;
        _liveFactsFor = null;
        _indexedFiles = null;
        _indexedFilesFor = null;
    }

    private static async Task<string?> ReadAllTextWithRetryAsync(string fullPath, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await File.ReadAllTextAsync(fullPath, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (IOException) when (attempt < 10)
            {
                // The editor may still hold the file right after the save event; bursts settle fast.
                await Task.Delay(50, cancellationToken);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
