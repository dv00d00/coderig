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
// workspace, then watches the source tree: each .cs save is re-extracted eagerly (facts servable in
// ~a second on MedDBase) while the sound cascade reconciles on a background task, with the disclosure
// (`N project(s) unreconciled`) printed until it clears. ResidentIndex deliberately owns no
// threads/timers — this command is the loop's owner: watcher, debounce, scheduling, status lines.
//
// Query SERVING from the resident facts is explicitly the NEXT slice; this one proves the loop.
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
        var query = new Option<string?>("--query")
        {
            Description = "Answer one query against the booted resident facts (e.g. --query \"reaches Program.Main\"). "
                + "Composes with --once: boot, answer, exit.",
        };
        var cmd = new Command(
            name: "watch",
            description: "Live background index: cold-analyze once retaining the workspace, then re-extract each saved .cs file "
                + "in ~a second and reconcile the cascade in the background (facts stay in memory; no store is written)."
        )
        {
            target,
            rules,
            once,
            query,
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
                watch: !once,
                workingDirectory: workingDirectory
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
            if (string.Equals(trimmed, "quit", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "exit", StringComparison.OrdinalIgnoreCase))
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
// edit → poll) without a console. Owns the FileSystemWatcher, the debounce, and the one WORKER that
// touches the ResidentIndex; `_gate` makes the read accessors (status/facts/disclosure) safe against
// the worker, and the worker is the only writer. Reconcile runs as a background task the worker
// starts after an apply and CANCELS when the next edit arrives — an edit never queues behind the
// cascade, which is the whole point of the converging overlay.
//
// WARMING runs on a SECOND background task started at the same point and cancelled by the same next edit,
// because facts being current is not the same as a query being fast: the per-generation derived layer costs
// ~4.0s on MedDBase on first access and nothing thereafter, so before this the first query after EVERY edit
// paid it. Warming builds it off the worker's path so that query pays nothing. The two tasks differ in one
// respect and it is deliberate: the worker AWAITS the cancelled reconcile before touching the index (the
// ResidentIndex is single-writer, so it must), but it does NOT await the cancelled warm — warming only forces
// Lazy fields on an IMMUTABLE LiveFactSource and touches no index state, so it is safe to abandon, and
// awaiting it would be exactly the "an edit waits for warming" trade that warming must always lose.
internal sealed class WatchHost : IAsyncDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    private readonly ResidentIndex _index;
    private readonly TextWriter _output;
    private readonly RuleSet _rules;
    private readonly string _workingDirectory;
    private readonly TimeSpan _debounce;
    private readonly Channel<string> _changes = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly FileSystemWatcher? _watcher;
    private readonly Task _loop;

    private int _appliedFiles;
    private double _lastEditSeconds = -1;

    // The in-flight background warm (and its cancellation), owned by the worker loop. Kept as FIELDS rather
    // than locals of RunLoopAsync — unlike the reconcile, this task outlives the iteration that started it
    // (it is cancelled, not awaited, when the next edit arrives), so DisposeAsync must still be able to
    // cancel and observe it instead of leaving a fire-and-forget task running past the host's lifetime.
    private Task? _warm;
    private CancellationTokenSource? _warmCts;

    // The query-ready derived layer for the CURRENT fact generation, built on first query and thrown away the
    // moment the facts move. Generation identity is the AnalysisResult INSTANCE: ResidentIndex nulls its
    // merged-facts field on every apply/reconcile, so a reference change is exactly "the facts moved".
    private LiveFactSource? _liveFacts;

    private WatchHost(ResidentIndex index, string solutionPath, RuleSet rules, string workingDirectory, TextWriter output, bool watch, TimeSpan debounce)
    {
        _index = index;
        _output = output;
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
            _output.WriteLine($"live: WATCHER OVERFLOW — changes may have been missed; edits since may not be reflected ({e.GetException().Message})");
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
        TimeSpan? debounce = null,
        // Where a live query resolves rules and deployments.json from — the directory `rig` was invoked in,
        // exactly as a store-backed query command uses it. Defaults to the solution's own directory, which is
        // what a test driving the host directly wants.
        string? workingDirectory = null,
        CancellationToken cancellationToken = default
    )
    {
        var (baseFacts, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(
            solutionPath: solutionPath,
            rules: rules,
            cancellationToken: cancellationToken,
            // Match `rig index` defaults: tests excluded, dtb cache on (the caller passes the dir).
            excludeTests: true,
            buildCacheDir: buildCacheDir
        );
        var index = new ResidentIndex(workspace, baseFacts, solutionPath, rules);
        return new WatchHost(
            index,
            solutionPath,
            rules,
            workingDirectory ?? Path.GetDirectoryName(Path.GetFullPath(solutionPath))!,
            output,
            watch,
            debounce ?? DefaultDebounce
        );
    }

    public async Task<string> GetStatusLineAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return ComposeStatus();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<string>> GetUnreconciledProjectsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _index.UnreconciledProjects;
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
            facts = CurrentLiveFacts();
            disclosure = ComposeStatus();
        }
        finally
        {
            _gate.Release();
        }

        var built = facts.BuildTimes.Count; // artifacts already memoized before this query
        var answer = await LiveQueryRunner.AnswerAsync(query, facts, _workingDirectory);
        var costLine =
            facts.BuildTimes.Count == built ? "" : $"{Environment.NewLine}live: derived layer built this generation: {facts.BuildTimeLine()}";
        return $"{disclosure}{Environment.NewLine}{answer.Text.TrimEnd('\r', '\n')}{costLine}";
    }

    public async Task<AnalysisResult> GetCurrentFactsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _index.CurrentFacts;
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

        // Warming is linked to the shutdown token, so it is already cancelled; await the most recent one so a
        // disposed host leaves no thread still touching a fact generation (the `_gate` dispose below would
        // otherwise race a warm that had not yet noticed).
        if (_warm is not null)
        {
            try
            {
                await _warm;
            }
            catch (OperationCanceledException) { }
        }

        _warmCts?.Dispose();
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
            if (string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) || string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // The single worker: debounce a burst of watcher events into one batch, apply it eagerly, print
    // the status (with the disclosure), then start the background reconcile. A NEW edit cancels an
    // in-flight reconcile before touching the index — ResidentIndex is single-writer by design, and a
    // cancelled reconcile is safe (its pending set and overlay are only committed on completion).
    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _changes.Reader;
        Task? reconcile = null;
        CancellationTokenSource? reconcileCts = null;

        async Task StopReconcileAsync()
        {
            if (reconcile is null)
            {
                return;
            }

            reconcileCts!.Cancel();
            try
            {
                await reconcile;
            }
            catch (OperationCanceledException) { }
            reconcileCts.Dispose();
            reconcile = null;
            reconcileCts = null;
        }

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

                await StopReconcileAsync();
                // Cancel — but deliberately do NOT await — any in-flight warm before touching the index. It
                // holds no index state, so abandoning it is safe, and awaiting it would make this edit wait on
                // work whose only purpose is to make a LATER query fast. Warming always loses that trade.
                CancelWarming();

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
                    // Nothing applied (an obj/ escapee, a non-workspace file, a file deleted mid-burst), so the
                    // fact generation did NOT move — but the warm was already cancelled above. Restart it so a
                    // stray save cannot leave the current generation permanently half-warmed. Cheap and silent:
                    // whatever it already built is memoized, so this only finishes the remainder, and the
                    // completion line is suppressed when there was no remainder to build.
                    await StartWarmingAsync(cancellationToken);
                    continue;
                }

                _output.WriteLine(await GetStatusLineAsync(cancellationToken));

                reconcileCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                reconcile = ReconcileInBackgroundAsync(reconcileCts.Token);
                await StartWarmingAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            await StopReconcileAsync();
            CancelWarming();
        }
    }

    // Start warming the CURRENT generation's derived layer. The LiveFactSource is resolved under `_gate` (it
    // reads _index.CurrentFacts, which the worker mutates) but the BUILD runs outside it — holding the gate for
    // seconds would block the next apply, which is the one thing this loop must never do. Nothing here is
    // ordered against a query either: LiveFactSource is immutable per generation, so a query arriving mid-warm
    // either finds its artifact already memoized or joins the in-flight build.
    private async Task StartWarmingAsync(CancellationToken cancellationToken)
    {
        LiveFactSource facts;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            facts = CurrentLiveFacts();
        }
        finally
        {
            _gate.Release();
        }

        // The previous CTS is already cancelled (CancelWarming ran at the top of this iteration); disposing it
        // here is safe even if its abandoned task is still running, because that task only ever reads the
        // token's IsCancellationRequested flag.
        _warmCts?.Dispose();
        _warmCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _warm = WarmInBackgroundAsync(facts, _warmCts.Token);
    }

    // Cancel the in-flight warm without waiting for it. Every warm CTS is LINKED to the shutdown token, so a
    // task abandoned here is also cancelled by DisposeAsync — it cannot outlive the host even though only the
    // most recent one is awaited there.
    private void CancelWarming() => _warmCts?.Cancel();

    private Task WarmInBackgroundAsync(LiveFactSource facts, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                // Artifacts already memoized before this warm — the same before/after count AnswerQueryAsync
                // uses, and for the same reason: it is the only way to tell "I built this" from "it was
                // already there", and reporting the generation's accumulated costs as if THIS task had just
                // paid them would be a false disclosure.
                var built = facts.BuildTimes.Count;
                var watch = Stopwatch.StartNew();
                try
                {
                    facts.WarmQueryArtifacts(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return; // a newer edit (or shutdown) superseded this generation — nothing to say
                }
                catch (Exception exception)
                {
                    // A warm failure must never take the host down and must never pass silently: the layer
                    // stays unbuilt, so the next query builds it and would hit the same fault where the user
                    // can see it. Disclose and move on.
                    _output.WriteLine($"live: background warm of the derived layer FAILED: {exception.Message}");
                    return;
                }

                watch.Stop();
                if (facts.BuildTimes.Count == built)
                {
                    return; // nothing left to build (a restarted warm on an unchanged generation) — say nothing
                }

                // The counterpart of AnswerQueryAsync's cost line: this says the cost was paid HERE, off the
                // query path, so the absence of a cost line on the next answer is explained rather than
                // mysterious. The artifact list is the GENERATION's, which on the normal path is exactly what
                // this task built.
                _output.WriteLine($"live: derived layer warmed in {watch.Elapsed.TotalSeconds:F2}s: {facts.BuildTimeLine()}");
            },
            cancellationToken
        );

    private async Task<int> ApplyBatchAsync(IReadOnlyCollection<string> batch, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var applied = 0;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var path in batch)
            {
                var fullPath = Path.GetFullPath(path);
                if (_index.CurrentSolution.GetDocumentIdsWithFilePath(fullPath).IsEmpty)
                {
                    // A brand-new file is not in the retained workspace; ResidentIndex has no add-file
                    // path yet. Disclose rather than silently skip.
                    _output.WriteLine($"live: {fullPath} is not a workspace document (new file?) — not indexed until the next cold boot");
                    continue;
                }

                var text = await ReadAllTextWithRetryAsync(fullPath, cancellationToken);
                if (text is null)
                {
                    continue; // deleted mid-burst, or still locked after retries — the next save will catch it
                }

                try
                {
                    await _index.ApplyEditAsync(fullPath, SourceText.From(text, Encoding.UTF8), cancellationToken);
                    applied++;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _output.WriteLine($"live: FAILED to apply {fullPath}: {exception.Message}");
                }
            }

            if (applied > 0)
            {
                Interlocked.Add(ref _appliedFiles, applied);
                _lastEditSeconds = watch.Elapsed.TotalSeconds;
            }
        }
        finally
        {
            _gate.Release();
        }

        return applied;
    }

    private async Task ReconcileInBackgroundAsync(CancellationToken cancellationToken)
    {
        await Task.Yield(); // never run synchronously on the worker's apply path
        var watch = Stopwatch.StartNew();
        string status;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _index.ReconcileAsync(cancellationToken);
            status = ComposeStatus();
        }
        finally
        {
            _gate.Release();
        }

        _output.WriteLine($"{status} | reconcile {watch.Elapsed.TotalSeconds:F2}s");
    }

    // The live derived layer for whatever generation of facts is current. Caller must hold `_gate` (it reads
    // _index.CurrentFacts, which the worker mutates). Rebuilding on a reference change is the WHOLE
    // invalidation model — no versions to bump, no cache keys, no staleness window: an edited fact set is a
    // different object, so it gets a different LiveFactSource.
    private LiveFactSource CurrentLiveFacts()
    {
        var facts = _index.CurrentFacts;
        if (_liveFacts is null || !ReferenceEquals(_liveFacts.Facts, facts))
        {
            _liveFacts = new LiveFactSource(facts, _rules);
        }

        return _liveFacts;
    }

    // The product surface (program doc, slice 5): `k project(s) unreconciled` IS the staleness
    // disclosure while the cascade is owed; when it is clear, say so plainly.
    private string ComposeStatus()
    {
        var applied = Volatile.Read(ref _appliedFiles);
        var unreconciled = _index.UnreconciledProjects.Count;
        var disclosure = unreconciled > 0 ? $"{unreconciled} project(s) unreconciled" : "all projects reconciled";
        var line = $"live: facts current as of {applied} file(s) applied | {disclosure}";
        return _lastEditSeconds < 0 ? line : $"{line} | last edit {_lastEditSeconds:F2}s";
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
