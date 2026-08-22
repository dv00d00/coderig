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
                restore: restore
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
                $"watch: cold boot in {bootWatch.Elapsed.TotalSeconds:F1}s — {host.ProjectCount} project(s), workspace retained"
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
            var owned = !noServe && !LiveQueryTransport.ServerExists(LiveQueryTransport.PipeNameFor(workingDirectory));
            await using var server = owned ? LiveQueryServer.TryStart(workingDirectory, host.ServeAsync, output) : null;
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
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                e.Cancel = true; // we own the shutdown: dispose the host cleanly, exit 0
                stopped.TrySetResult();
            };
            Console.CancelKeyPress += onCancel;
            try
            {
                // Two ways out: Ctrl+C, or `quit` on stdin. The stdin reader IS the query transport for
                // this slice — enough for a human at a terminal or a piped agent to drive the resident index,
                // deliberately not a protocol (that is a later slice).
                await Task.WhenAny(stopped.Task, ReadQueriesAsync(host, output, stopped.Task));
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
    private static async Task ReadQueriesAsync(WatchHost host, TextWriter output, Task stopped)
    {
        while (true)
        {
            var line = await Console.In.ReadLineAsync();
            if (line is null)
            {
                await stopped; // stdin is not a query source here; hand the exit back to Ctrl+C
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
}

// The resident loop, split from the command action so a test can drive it end-to-end (boot → disk
// edit → poll) without a console. Owns the FileSystemWatcher, debounce and one publication worker.
// `_gate` keeps status/query capture coherent with publication. Dependent reconciliation and derived-
// artifact warming are explicit primitives: the watch loop does neither automatically, so a save burst
// publishes exactly one eager generation and keeps its dirty disclosure until an explicit reconcile.
internal sealed class WatchHost : IAsyncDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    private readonly ResidentIndex _index;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly RuleSet _rules;
    private readonly string _workingDirectory;
    private readonly TimeSpan _debounce;
    private readonly Channel<string> _changes = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly FileSystemWatcher? _watcher;
    private readonly Task _loop;

    private int _appliedFiles;

    // STICKY once set, for the process lifetime, and deliberately not clearable. A FileSystemWatcher
    // overflow means events were dropped before we saw them, so we do not know WHICH files went stale —
    // and a background reconcile cannot recover them, because it only re-extracts the cascade of files it
    // knows are dirty. Only a cold boot re-reads everything. So this is a permanent "some edits may be
    // missing" caveat on every subsequent answer, not a transient condition.
    private int _watcherOverflowed;
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
        TimeSpan debounce
    )
    {
        _index = index;
        _output = output;
        _error = error;
        _rules = rules;
        _workingDirectory = workingDirectory;
        _debounce = debounce;
        ProjectCount = index.CurrentSolution.ProjectIds.Count;

        if (!watch)
        {
            _loop = Task.CompletedTask;
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
        _watcher.Renamed += (_, e) => Enqueue(e.FullPath);
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
    }

    public int ProjectCount { get; }

    // Total files applied through the eager arm since boot — the `n` of the status line, and the
    // signal a test polls to know the watcher fired.
    public int AppliedFileCount => Volatile.Read(ref _appliedFiles);

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
        var index = new ResidentIndex(workspace, baseFacts, solutionPath, rules, interner: interner);
        return new WatchHost(
            index,
            solutionPath,
            rules,
            workingDirectory ?? Path.GetDirectoryName(Path.GetFullPath(solutionPath))!,
            output,
            error ?? TextWriter.Null,
            watch,
            debounce ?? DefaultDebounce
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
        LiveFactSource facts;
        string disclosure;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = _index.CaptureSnapshot();
            facts = CurrentLiveFacts(snapshot);
            disclosure = ComposeStatus(snapshot);
            // The footer rides with the answer, not only with the boot log. Stderr, so the answer on
            // stdout stays parseable.
            WriteCompilationHealthNote(snapshot);
        }
        finally
        {
            _gate.Release();
        }

        var built = facts.BuildTimes.Count; // artifacts already memoized before this query
        var answer = await LiveQueryRunner.AnswerAsync(query, facts, _workingDirectory);
        var costLine =
            facts.BuildTimes.Count == built
                ? ""
                : $"{Environment.NewLine}live: derived layer built this generation: {facts.BuildTimeLine()}";
        return $"{disclosure}{Environment.NewLine}{answer.Text.TrimEnd('\r', '\n')}{costLine}";
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
        LiveFactSource facts;
        string disclosure;
        IReadOnlyList<string> health;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = _index.CaptureSnapshot();
            facts = CurrentLiveFacts(snapshot);
            disclosure = ComposeSourceDisclosure(snapshot);
            // Captured, NOT written to the host's own stderr: this note belongs to the answer being sent
            // back, and writing it locally as well would leave the note in the wrong terminal.
            health = ComposeCompilationHealthNote(snapshot);
        }
        finally
        {
            _gate.Release();
        }

        var built = facts.BuildTimes.Count; // artifacts already memoized before this query
        var result = await LiveQueryRunner.RunRequestAsync(request, facts, _workingDirectory);
        if (result.DeclineReason is not null)
        {
            return LiveServeResult.Declined(result.DeclineReason);
        }

        var answer = result.Answer!;
        var notes = new StringBuilder();
        foreach (var line in health)
        {
            notes.AppendLine(line);
        }

        if (answer.Err.Length > 0)
        {
            notes.Append(answer.Err);
            if (!answer.Err.EndsWith('\n'))
            {
                notes.AppendLine();
            }
        }

        if (facts.BuildTimes.Count != built)
        {
            notes.AppendLine($"live: derived layer built this generation: {facts.BuildTimeLine()}");
        }

        return LiveServeResult.Answered(
            exit: answer.Exit,
            standardOut: answer.Out,
            standardError: notes.ToString(),
            disclosure: disclosure
        );
    }

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
        _shutdown.Cancel();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException) { }

        _shutdown.Dispose();
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
    // print the status with its persistent dirty disclosure. It deliberately does not reconcile or warm.
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
            }
        }
        catch (OperationCanceledException) { }
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
                // A brand-new file is not in the retained workspace; ResidentIndex has no add-file
                // path yet. Disclose rather than silently skip.
                _output.WriteLine($"live: {fullPath} is not a workspace document (new file?) — not indexed until the next cold boot");
                continue;
            }

            var text = await ReadAllTextWithRetryAsync(fullPath, cancellationToken);
            if (text is null)
            {
                _output.WriteLine($"live: FAILED to apply {batch.Count}-file batch: {fullPath} could not be read; no edits published");
                return 0;
            }

            edits[fullPath] = SourceText.From(text, Encoding.UTF8);
        }

        if (edits.Count == 0)
        {
            return 0;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await _index.ApplyEditsAsync(edits, cancellationToken);
                ReleasePublishedSnapshotCaches();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _output.WriteLine($"live: FAILED to apply {edits.Count}-file batch: {exception.Message}");
                return 0;
            }

            Interlocked.Add(ref _appliedFiles, edits.Count);
            _lastEditSeconds = watch.Elapsed.TotalSeconds;
        }
        finally
        {
            _gate.Release();
        }

        return edits.Count;
    }

    // Explicit scheduler/test seam. Watcher publication never calls it automatically: dirty debt stays
    // visible until a caller deliberately pays the cascade cost.
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

        segments.AddRange(CompilationHealthNotice.StatusSegments(snapshot.GetCompilationHealth(), IndexedFiles(snapshot)));
        if (Volatile.Read(ref _watcherOverflowed) != 0)
        {
            // Must be a segment rather than a one-off console line: otherwise an answer computed from a
            // generation that silently missed edits still claims "all projects reconciled" — exactly the
            // shape of the broken-compilation defect this host already discloses.
            segments.Add("file-watcher overflowed — some edits may be MISSING; restart to be certain");
        }

        if (segments.Count == 0)
        {
            segments.Add("all projects reconciled");
        }

        var body = $"{applied} file(s) applied | {string.Join(" | ", segments)}";
        return _lastEditSeconds < 0 ? body : $"{body} | last edit {_lastEditSeconds:F2}s";
    }

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
