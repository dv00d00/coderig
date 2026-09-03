using System.Threading;
using Rig.Cli.Git;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;

namespace Rig.Cli.CommandLine;

// One invocation-scoped disclosure for the immutable store(s) that actually answered a CLI command. The
// ambient session is established by CommandGuard and flows through async service calls; web/internal callers
// do not run under CommandGuard, so their store opens remain silent. stdout is never involved.
internal sealed class StoreAnswerDisclosure
{
    private static readonly AsyncLocal<StoreAnswerDisclosure?> Ambient = new();

    private readonly string _workingDirectory;
    private readonly TextWriter _error;
    private readonly object _gate = new();
    private readonly HashSet<string> _emittedStoreDirectories = new(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );
    private readonly Dictionary<string, (string StoreId, CompilationHealthNotice.StoreSnapshot Snapshot)> _compilationSnapshots = new(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );
    private readonly HashSet<string> _emittedCompilationDirectories = new(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );

    private StoreAnswerDisclosure(string workingDirectory, TextWriter error)
    {
        _workingDirectory = workingDirectory;
        _error = error;
    }

    internal static bool IsActive => Ambient.Value is not null;

    internal static IDisposable BeginInvocation(string workingDirectory, TextWriter error, bool enabled)
    {
        var previous = Ambient.Value;
        Ambient.Value = enabled ? new StoreAnswerDisclosure(workingDirectory, error) : null;
        return new AmbientScope(previous);
    }

    // Called only after the schema gate succeeds. An explicit path overload also covers the impact engine's
    // direct base-store contexts; dedupe makes its provenance/base-compute opens one logical disclosure.
    internal static Task DiscloseCurrentAsync(RigDbContext context, string storeDirectoryOrDbPath, string? explicitStoreRef = null) =>
        Ambient.Value is { } current
            ? current.DiscloseAsync(context, StoreDirectory(storeDirectoryOrDbPath), explicitStoreRef)
            : Task.CompletedTask;

    internal static bool HasCompileError(string? filePath) =>
        !string.IsNullOrEmpty(filePath) && Ambient.Value?.HasCompileErrorCore(filePath) == true;

    internal static string BindingHealth(string? filePath) => HasCompileError(filePath) ? "compile_error" : "ok";

    internal static IReadOnlySet<string> CompileErrorFiles =>
        Ambient.Value?.CompileErrorFilesCore() ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    internal static void WriteCompilationHealth() => Ambient.Value?.WriteCompilationHealthCore();

    internal static void WriteCompilationHealth(string role, string storeDirectoryOrDbPath) =>
        Ambient.Value?.WriteCompilationHealthCore(role, StoreDirectory(storeDirectoryOrDbPath));

    private async Task DiscloseAsync(RigDbContext context, string storeDirectory, string? explicitStoreRef)
    {
        var canonicalDirectory = Path.GetFullPath(storeDirectory);
        lock (_gate)
        {
            if (!_emittedStoreDirectories.Add(canonicalDirectory))
            {
                return;
            }
        }

        CompilationHealthNotice.StoreSnapshot? compilationSnapshot = null;
        try
        {
            compilationSnapshot = await CompilationHealthNotice.LoadStoreAsync(context);
        }
        catch
        {
            // A disclosure-only read must not replace the schema gate's ownership of store failures.
        }

        string line;
        try
        {
            var runs = await Reads.ListRunsAsync(context);
            line = BuildLine(canonicalDirectory, explicitStoreRef, runs);
        }
        catch
        {
            // Disclosure must never make an otherwise-readable query fail. The schema gate already owns
            // store-read failures; a provenance-only failure means freshness is unknown, nothing stronger.
            line = Prefix(canonicalDirectory, explicitStoreRef, indexedCommit: null) + "freshness unknown: provenance unavailable";
        }

        lock (_gate)
        {
            if (compilationSnapshot is not null)
            {
                _compilationSnapshots[canonicalDirectory] = (StoreId(canonicalDirectory), compilationSnapshot);
            }
            _error.WriteLine(line);
        }
    }

    private bool HasCompileErrorCore(string filePath)
    {
        lock (_gate)
        {
            return _compilationSnapshots.Values.Any(value => value.Snapshot.HasCompileError(filePath));
        }
    }

    private IReadOnlySet<string> CompileErrorFilesCore()
    {
        lock (_gate)
        {
            return _compilationSnapshots
                .Values.SelectMany(value => value.Snapshot.CompileErrorFiles)
                .ToHashSet(CompilationFilePath.Comparer);
        }
    }

    private void WriteCompilationHealthCore()
    {
        lock (_gate)
        {
            foreach (var (directory, value) in _compilationSnapshots.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!_emittedCompilationDirectories.Add(directory))
                {
                    continue;
                }

                foreach (var line in CompilationHealthNotice.Note(value.Snapshot.Health, value.Snapshot.IndexedFiles))
                {
                    _error.WriteLine($"{line} [store {value.StoreId}] Run `rig files --compile-errors` for details.");
                }
            }
        }
    }

    private void WriteCompilationHealthCore(string role, string storeDirectory)
    {
        var canonicalDirectory = Path.GetFullPath(storeDirectory);
        lock (_gate)
        {
            var emissionKey = $"{role}\u001f{canonicalDirectory}";
            if (!_emittedCompilationDirectories.Add(emissionKey) || !_compilationSnapshots.TryGetValue(canonicalDirectory, out var value))
            {
                return;
            }

            foreach (var line in CompilationHealthNotice.Note(value.Snapshot.Health, value.Snapshot.IndexedFiles))
            {
                _error.WriteLine($"{line} [{role} store {value.StoreId}] Run `rig files --compile-errors` for details.");
            }
        }
    }

    private string BuildLine(string storeDirectory, string? explicitStoreRef, IReadOnlyList<RunSummary> runs)
    {
        if (runs.Count == 0)
        {
            return Prefix(storeDirectory, explicitStoreRef, indexedCommit: null) + "freshness unknown: no run provenance";
        }

        var provenance = runs.Select(run => new RunProvenance(NormalizeCommit(run.SourceCommit), run.SourceDirty)).Distinct().ToList();
        if (provenance.Count != 1)
        {
            return Prefix(storeDirectory, explicitStoreRef, indexedCommit: null) + "freshness unknown: mixed run provenance";
        }

        var indexed = provenance[0];
        if (indexed.Commit is null)
        {
            return Prefix(storeDirectory, explicitStoreRef, indexedCommit: null) + "freshness unknown: no source commit";
        }

        if (indexed.Dirty)
        {
            return Prefix(storeDirectory, explicitStoreRef, indexed.Commit) + "UNVERIFIABLE: indexed from a dirty tree";
        }

        var repositoryRoots = new HashSet<string>(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
        );
        foreach (var solutionPath in runs.Select(run => run.SolutionPath))
        {
            var repositoryRoot = GitProvenanceProbe.ResolveRepositoryRoot(solutionPath);
            if (repositoryRoot is null)
            {
                return Prefix(storeDirectory, explicitStoreRef, indexed.Commit) + "freshness unknown: source checkout unavailable";
            }

            repositoryRoots.Add(repositoryRoot);
        }

        if (repositoryRoots.Count == 0)
        {
            return Prefix(storeDirectory, explicitStoreRef, indexed.Commit) + "freshness unknown: source checkout unavailable";
        }

        // A merged store commonly has one run per solution/project identity in the SAME monorepo. Resolve
        // those paths first, then issue one freshness status probe per distinct repository, not three git
        // processes per run row.
        var rigDirectory = StoreLayout.RigDir(_workingDirectory);
        var checkouts = repositoryRoots.Select(root => GitProvenanceProbe.CaptureFreshness(root, rigDirectory)).ToList();
        if (checkouts.Any(checkout => checkout.Commit is null))
        {
            return Prefix(storeDirectory, explicitStoreRef, indexed.Commit) + "freshness unknown: source checkout unavailable";
        }

        var checkoutCommits = checkouts
            .Select(checkout => NormalizeCommit(checkout.Commit)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (checkoutCommits.Count != 1)
        {
            return Prefix(storeDirectory, explicitStoreRef, indexed.Commit) + "freshness unknown: source checkouts are at mixed HEADs";
        }

        var checkoutCommit = checkoutCommits[0];
        if (!string.Equals(checkoutCommit, indexed.Commit, StringComparison.OrdinalIgnoreCase))
        {
            return Prefix(storeDirectory, explicitStoreRef, indexed.Commit) + $"STALE vs checkout HEAD {Short(checkoutCommit)}";
        }

        if (checkouts.Any(checkout => checkout.Dirty))
        {
            return Prefix(storeDirectory, explicitStoreRef, indexed.Commit) + "STALE: working tree has unindexed changes";
        }

        return Prefix(storeDirectory, explicitStoreRef, indexed.Commit) + "current";
    }

    private string Prefix(string storeDirectory, string? explicitStoreRef, string? indexedCommit)
    {
        var storeId = StoreId(storeDirectory);
        var selection =
            explicitStoreRef is not null ? " (pinned)"
            : string.Equals(storeId, StoreLayout.LatestStoreId(_workingDirectory), StringComparison.OrdinalIgnoreCase) ? " (LATEST)"
            : " (default)";
        return $"store: {storeId}{selection} @ {(indexedCommit is null ? "unknown" : Short(indexedCommit))} — ";
    }

    private static string StoreId(string storeDirectory)
    {
        var directoryName = Path.GetFileName(storeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(directoryName, StoreLayout.RigDirName, StringComparison.Ordinal) ? "legacy" : directoryName;
    }

    private static string? NormalizeCommit(string? commit) => string.IsNullOrWhiteSpace(commit) ? null : commit.Trim().ToLowerInvariant();

    private static string Short(string commit) => commit.Length > 12 ? commit[..12] : commit;

    private static string StoreDirectory(string directoryOrDbPath) =>
        string.Equals(Path.GetFileName(directoryOrDbPath), StoreLayout.DbFileName, StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(directoryOrDbPath) ?? directoryOrDbPath
            : directoryOrDbPath;

    private sealed record RunProvenance(string? Commit, bool Dirty);

    private sealed class AmbientScope(StoreAnswerDisclosure? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Ambient.Value = previous;
            _disposed = true;
        }
    }
}
