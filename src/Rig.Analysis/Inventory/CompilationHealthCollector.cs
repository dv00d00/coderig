using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Rig.Domain.Data;

namespace Rig.Analysis.Inventory;

// Buckets Roslyn's own ERROR diagnostics into the per-FILE / per-PROJECT shape CompilationHealth
// records. Thread-safe: the loader's compile+read+extract pass runs Parallel over projects, and a file
// linked into several projects is reported against from more than one of them.
//
// What this deliberately does NOT do:
//   - it does not retain the diagnostic STRINGS. The pre-existing code appended every error to a
//     ConcurrentBag<string> and Console.WriteLine'd each one; on the unrestored MedDBase clone that was
//     2,387,334 lines / 528 MB, retained AND printed to stdout. Here the retained state is O(files
//     with errors), and printing is capped by the caller.
//   - it does not look at warnings. `Severity == Error` is the filter, unchanged — a warning does not
//     degrade binding, and loosening it would flag nearly every file on a real index.
internal sealed class CompilationHealthCollector
{
    // Deduped diagnostic ids kept per file, capped before the "+N" summary kicks in.
    private const int MaxCodesListed = 8;
    private const int MaxMessageLength = 200;

    private readonly ConcurrentDictionary<string, FileBucket> _files = new(CompilationFilePath.Comparer);
    private readonly ConcurrentDictionary<(string Project, string Reason), byte> _projects = new();
    private int _unlocated;

    // Record one error diagnostic. `filePath`, when supplied, is the path the CALLER knows the
    // diagnostic's tree by (document.FilePath / SyntaxTree.FilePath) rather than the diagnostic's own
    // reported path — the overlay's replacement key is the workspace's path string, so bucketing under
    // anything else would leave a stale flag that no re-extraction can clear.
    public void AddError(Diagnostic diagnostic, string? filePath = null)
    {
        var path = filePath ?? diagnostic.Location.SourceTree?.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            var span = diagnostic.Location.GetLineSpan();
            path = span.IsValid ? span.Path : null;
        }

        if (string.IsNullOrEmpty(path))
        {
            // Compilation-level, or reported against a tree with no path: no file can carry it, so it
            // is counted (the total must stay truthful) and otherwise invisible to the per-file channel.
            Interlocked.Increment(ref _unlocated);
            return;
        }

        _files.GetOrAdd(CompilationFilePath.Key(path), _ => new FileBucket()).Add(diagnostic);
    }

    public void AddUnlocatedError() => Interlocked.Increment(ref _unlocated);

    // Record a location-less project failure (ProjectCompileFailure.NoCompilation / GeneratorEmit /
    // GeneratorRun). Idempotent per (project, reason) — the generator wiring pass can hit the same
    // project from several referencing projects.
    public void AddProjectFailure(string projectName, string reason) => _projects.TryAdd((projectName, reason), value: 0);

    public CompilationHealth Build()
    {
        var files = _files
            .Select(entry => entry.Value.ToRecord(entry.Key))
            .OrderBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var projects = _projects
            .Keys.Select(k => new ProjectCompileFailure(k.Project, k.Reason))
            .OrderBy(p => p.ProjectName, StringComparer.Ordinal)
            .ThenBy(p => p.Reason, StringComparer.Ordinal)
            .ToArray();
        return new CompilationHealth(files, projects, Volatile.Read(ref _unlocated));
    }

    // One file's accumulator. `first` is the LOWEST-POSITIONED diagnostic, not the first one observed:
    // projects are visited in parallel, so "first observed" would make the recorded message
    // non-deterministic for a file linked into more than one project.
    private sealed class FileBucket
    {
        private readonly Lock _lock = new();
        private readonly SortedSet<string> _codes = new(StringComparer.Ordinal);
        private int _count;
        private (int Line, int Character) _firstPosition = (int.MaxValue, int.MaxValue);
        private string _firstMessage = "";

        public void Add(Diagnostic diagnostic)
        {
            var span = diagnostic.Location.GetLineSpan();
            var position = span.IsValid ? (span.StartLinePosition.Line, span.StartLinePosition.Character) : (int.MaxValue, int.MaxValue);
            var message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            lock (_lock)
            {
                _count++;
                _codes.Add(diagnostic.Id);
                if (
                    position.CompareTo(_firstPosition) < 0
                    || (position == _firstPosition && string.CompareOrdinal(message, _firstMessage) < 0)
                )
                {
                    _firstPosition = position;
                    _firstMessage = message;
                }
            }
        }

        public FileCompileHealth ToRecord(string filePath)
        {
            lock (_lock)
            {
                var listed = _codes.Take(MaxCodesListed).ToArray();
                var overflow = _codes.Count - listed.Length;
                var codes = overflow > 0 ? $"{string.Join(",", listed)},+{overflow}" : string.Join(",", listed);
                var message = _firstMessage.Length <= MaxMessageLength ? _firstMessage : _firstMessage[..(MaxMessageLength - 3)] + "...";
                return new FileCompileHealth(filePath, _count, codes, message);
            }
        }
    }
}
