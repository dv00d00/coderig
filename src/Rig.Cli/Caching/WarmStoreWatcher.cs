using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Caching;

// PROOF OF CONCEPT — the "warm on commit / on disk write" half of WarmStore, for a RESIDENT host.
//
// WarmStore's key already makes a stale entry unservable (store identity = rig.db size+mtime), so
// correctness needs no watcher. What a watcher buys is the LATENCY of the first request after a reindex:
// without it, whoever asks the next question pays the full cold load; with it, the reload happens off the
// request path while nobody is waiting.
//
// Two triggers, both on the .rig directory:
//   * rig.db changed  -> the store was rewritten in place (`rig graph`, an in-place reindex).
//   * LATEST changed  -> a NEW COMMIT was indexed and is now the default store; the resolved store dir
//     moves, so this is a different WarmStore key and needs its own load.
// Either way the response is the same: re-resolve the store and pre-warm it in the background.
//
// Debounced, because an index publish touches the directory many times in a burst and each touch would
// otherwise start a multi-second load. Fire-and-forget by design: a failed warm just means the next real
// request pays what it would have paid anyway.
internal static class WarmStoreWatcher
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
    private static FileSystemWatcher? watcher;
    private static CancellationTokenSource? pending;
    private static readonly object PendingLock = new();

    // Pre-warm now, then keep warming on store changes for the life of the process. `errorWriter` gets the
    // one-line progress notes; everything here is best-effort and never throws to the caller.
    internal static void Start(string workingDirectory, TextWriter errorWriter)
    {
        _ = WarmAsync(workingDirectory, errorWriter);

        var rigDirectory = StoreLayout.RigDir(workingDirectory);
        if (!Directory.Exists(rigDirectory))
        {
            return;
        }

        try
        {
            watcher = new FileSystemWatcher(rigDirectory)
            {
                // Size+mtime are what the cache key is made of, so those are the changes worth reacting to.
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                IncludeSubdirectories = true, // a per-commit store lives in .rig/<short-sha>/
                EnableRaisingEvents = true,
            };
            watcher.Changed += (_, e) => OnStoreTouched(e.Name, workingDirectory, errorWriter);
            watcher.Created += (_, e) => OnStoreTouched(e.Name, workingDirectory, errorWriter);
            watcher.Renamed += (_, e) => OnStoreTouched(e.Name, workingDirectory, errorWriter);
        }
        catch (Exception ex)
        {
            // No watcher (permissions, an exotic filesystem) is a degraded but correct mode: entries still
            // invalidate on key mismatch, the first post-reindex request just pays the reload.
            errorWriter.WriteLine($"  warm-store watcher unavailable (non-fatal): {ex.Message}");
        }
    }

    private static void OnStoreTouched(string? name, string workingDirectory, TextWriter errorWriter)
    {
        // `rig index` publishes rig.db by atomic rename and rewrites LATEST; ignore everything else so the
        // cache.db writes our OWN queries make can't trigger an endless re-warm loop.
        if (name is null)
        {
            return;
        }

        var leaf = Path.GetFileName(name);
        var interesting =
            string.Equals(leaf, StoreLayout.DbFileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(leaf, StoreLayout.LatestPointerName, StringComparison.OrdinalIgnoreCase);
        if (!interesting)
        {
            return;
        }

        CancellationToken token;
        lock (PendingLock)
        {
            pending?.Cancel();
            pending = new CancellationTokenSource();
            token = pending.Token;
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    // Coalesce the publish burst into one warm.
                    await Task.Delay(Debounce, token);
                }
                catch (OperationCanceledException)
                {
                    return; // superseded by a later touch
                }

                errorWriter.WriteLine($"  store changed ({leaf}) — re-warming");
                await WarmAsync(workingDirectory, errorWriter);
            },
            CancellationToken.None
        );
    }

    private static async Task WarmAsync(string workingDirectory, TextWriter errorWriter)
    {
        try
        {
            var workspace = new WorkspaceLocation(WorkingDirectory: workingDirectory);
            var (context, storeDirectory) = await OpenReadContextGatedAsync(workspace, withStoreDir: true);
            await using var scope = context;

            var rules = RuleSetLoader.Load(workingDirectory, extraRules: [], loadedPaths: out var loadedPaths);
            var rulesHash = RulesFingerprint.ComputeFromPaths(loadedPaths);

            var started = Environment.TickCount64;
            await WarmStore.PrewarmAsync(context: context, rules: rules, storeDir: storeDirectory, rulesHash: rulesHash);
            errorWriter.WriteLine($"  warm: graph + invocations ready in {(Environment.TickCount64 - started) / 1000.0:F1}s");
        }
        catch (Exception ex)
        {
            errorWriter.WriteLine($"  warm failed (non-fatal, queries still work): {ex.Message}");
        }
    }
}
