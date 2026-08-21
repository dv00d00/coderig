using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
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
        var cmd = new Command(
            name: "watch",
            description: "Live background index: cold-analyze once retaining the workspace, then re-extract each saved .cs file "
                + "in ~a second and reconcile the cascade in the background (facts stay in memory; no store is written)."
        )
        {
            target,
            rules,
            once,
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
                watch: !once
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
            output.WriteLine(await host.GetStatusLineAsync());
            if (once)
            {
                return 0;
            }

            output.WriteLine("watch: watching for .cs saves (obj/ and bin/ excluded) — press Ctrl+C to stop.");
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                e.Cancel = true; // we own the shutdown: dispose the host cleanly, exit 0
                stopped.TrySetResult();
            };
            Console.CancelKeyPress += onCancel;
            try
            {
                await stopped.Task;
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }

            output.WriteLine("watch: stopped.");
            return 0;
        }
    }
}

// The resident loop, split from the command action so a test can drive it end-to-end (boot → disk
// edit → poll) without a console. Owns the FileSystemWatcher, the debounce, and the one WORKER that
// touches the ResidentIndex; `_gate` makes the read accessors (status/facts/disclosure) safe against
// the worker, and the worker is the only writer. Reconcile runs as a background task the worker
// starts after an apply and CANCELS when the next edit arrives — an edit never queues behind the
// cascade, which is the whole point of the converging overlay.
internal sealed class WatchHost : IAsyncDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    private readonly ResidentIndex _index;
    private readonly TextWriter _output;
    private readonly TimeSpan _debounce;
    private readonly Channel<string> _changes = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly FileSystemWatcher? _watcher;
    private readonly Task _loop;

    private int _appliedFiles;
    private double _lastEditSeconds = -1;

    private WatchHost(ResidentIndex index, string solutionPath, TextWriter output, bool watch, TimeSpan debounce)
    {
        _index = index;
        _output = output;
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
        return new WatchHost(index, solutionPath, output, watch, debounce ?? DefaultDebounce);
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

                reconcileCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                reconcile = ReconcileInBackgroundAsync(reconcileCts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            await StopReconcileAsync();
        }
    }

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
