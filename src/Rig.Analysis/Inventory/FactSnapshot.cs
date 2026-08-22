using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Rig.Domain.Data;

namespace Rig.Analysis.Inventory;

internal readonly record struct FactRevision(long Value)
{
    public FactRevision Next() => new(checked(Value + 1));
}

internal enum SurfaceState
{
    Unknown,
    BodyOnly,
    Changed,
}

internal sealed record DirtySet(
    ImmutableHashSet<DocumentId> PendingDocuments,
    ImmutableHashSet<string> PendingFiles,
    ImmutableHashSet<ProjectId> PendingProjects
)
{
    internal static DirtySet Empty { get; } =
        new(
            ImmutableHashSet<DocumentId>.Empty,
            ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase),
            ImmutableHashSet<ProjectId>.Empty
        );

    internal static DirtySet From(Solution solution, IEnumerable<DocumentId> documents)
    {
        var pendingDocuments = documents.ToImmutableHashSet();
        if (pendingDocuments.Count == 0)
        {
            return Empty;
        }

        var files = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        var projects = ImmutableHashSet.CreateBuilder<ProjectId>();
        foreach (var documentId in pendingDocuments)
        {
            projects.Add(documentId.ProjectId);
            var filePath = solution.GetDocument(documentId)?.FilePath;
            if (filePath is not null)
            {
                files.Add(filePath);
            }
        }

        return new DirtySet(pendingDocuments, files.ToImmutable(), projects.ToImmutable());
    }
}

internal sealed record SnapshotDelta(
    ImmutableHashSet<string> ReplacedFiles,
    ImmutableHashSet<ProjectId> ConservativelyAffectedProjects,
    ImmutableHashSet<string> KnownChangedSymbols,
    SurfaceState SurfaceState
)
{
    internal static SnapshotDelta Empty { get; } =
        new(
            ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase),
            ImmutableHashSet<ProjectId>.Empty,
            ImmutableHashSet<string>.Empty,
            SurfaceState.Unknown
        );
}

// One immutable, atomically publishable generation of the resident fact index. A snapshot deliberately
// has no predecessor link: readers pin the exact generation they captured, while generations with no
// readers become collectible. Live consumers stream this segmented view; the legacy flattened
// AnalysisResult remains lazy as an explicit compatibility/oracle boundary.
internal sealed class FactSnapshot : IFactSnapshotView
{
    private readonly Lazy<AnalysisResult> _flattenedFacts;
    private readonly Lazy<CompilationHealth?> _compilationHealth;

    internal FactSnapshot(
        FactRevision revision,
        Solution solution,
        AnalysisResult baseFacts,
        ImmutableDictionary<string, FileFacts> overlay,
        DirtySet dirty,
        SnapshotDelta delta
    )
    {
        Revision = revision;
        Solution = solution ?? throw new ArgumentNullException(nameof(solution));
        BaseFacts = baseFacts ?? throw new ArgumentNullException(nameof(baseFacts));
        Overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        Dirty = dirty ?? throw new ArgumentNullException(nameof(dirty));
        Delta = delta ?? throw new ArgumentNullException(nameof(delta));
        _compilationHealth = new Lazy<CompilationHealth?>(
            () => MergeCompilationHealth(BaseFacts, Overlay),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        _flattenedFacts = new Lazy<AnalysisResult>(
            () => Overlay.Count == 0 ? BaseFacts : this.Materialize(BaseFacts.ProjectIdentity, BaseFacts.SourceProjectPath),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
    }

    internal FactRevision Revision { get; }
    internal Solution Solution { get; }
    internal AnalysisResult BaseFacts { get; }
    internal ImmutableDictionary<string, FileFacts> Overlay { get; }
    internal DirtySet Dirty { get; }
    internal SnapshotDelta Delta { get; }

    public string SolutionPath => BaseFacts.SolutionPath;

    public IEnumerable<SourceFileInfo> EnumerateSourceFiles() =>
        EnumerateReplaced(BaseFacts.SourceFiles, f => f.FilePath, slice => slice.SourceFiles);

    public IEnumerable<DiRegistrationInfo> EnumerateDiRegistrations() =>
        EnumerateReplaced(
            BaseFacts.DiRegistrations,
            r => r.FilePath,
            slice => slice.DiRegistrations,
            r => r.FilePath.Length == 0 || !string.Equals(Path.GetExtension(r.FilePath), ".cs", StringComparison.OrdinalIgnoreCase)
        );

    public IEnumerable<SymbolFact> EnumerateSymbols() =>
        EnumerateReplaced(BaseFacts.Symbols ?? [], s => s.FilePath, slice => slice.Symbols);

    public IEnumerable<ReferenceFact> EnumerateReferences() =>
        EnumerateReplaced(BaseFacts.References ?? [], r => r.FilePath, slice => slice.References);

    public IEnumerable<TypeRelationFact> EnumerateTypeRelations() =>
        EnumerateReplaced(BaseFacts.TypeRelations ?? [], r => r.FilePath, slice => slice.TypeRelations);

    public IEnumerable<DispatchFact> EnumerateDispatchFacts() =>
        EnumerateReplaced(BaseFacts.DispatchFacts ?? [], d => d.FilePath, slice => slice.Dispatch);

    public IEnumerable<AllocationFact> EnumerateAllocationFacts() =>
        EnumerateReplaced(BaseFacts.AllocationFacts ?? [], a => a.FilePath, slice => slice.Allocations);

    public CompilationHealth? GetCompilationHealth() => _compilationHealth.Value;

    // Compatibility/oracle boundary. Live query and status paths consume this snapshot as an
    // IFactSnapshotView and therefore leave this lazy uncreated.
    internal AnalysisResult FlattenedFacts => _flattenedFacts.Value;
    internal int FullMaterializationCount => _flattenedFacts.IsValueCreated ? 1 : 0;

    // ResidentIndex.MergeFacts remains as a compatibility entry point for older tests/callers, but
    // delegates here so replacement semantics exist in only this view.
    internal static AnalysisResult MaterializeFacts(AnalysisResult baseFacts, ImmutableDictionary<string, FileFacts> overlay)
    {
        if (overlay.Count == 0)
        {
            return baseFacts;
        }

        return new CompositeView(baseFacts, overlay).Materialize(baseFacts.ProjectIdentity, baseFacts.SourceProjectPath);
    }

    private IEnumerable<T> EnumerateReplaced<T>(
        IEnumerable<T> baseRows,
        Func<T, string> filePath,
        Func<FileFacts, IEnumerable<T>> sliceRows,
        Func<T, bool>? baseOnly = null
    ) => EnumerateReplaced(Overlay, baseRows, filePath, sliceRows, baseOnly);

    private static IEnumerable<T> EnumerateReplaced<T>(
        ImmutableDictionary<string, FileFacts> overlay,
        IEnumerable<T> baseRows,
        Func<T, string> filePath,
        Func<FileFacts, IEnumerable<T>> sliceRows,
        Func<T, bool>? baseOnly = null
    )
    {
        foreach (var row in baseRows)
        {
            if (baseOnly?.Invoke(row) == true || !overlay.ContainsKey(filePath(row)))
            {
                yield return row;
            }
        }

        foreach (var slice in overlay.Values)
        {
            foreach (var row in sliceRows(slice))
            {
                yield return row;
            }
        }
    }

    private static CompilationHealth? MergeCompilationHealth(AnalysisResult baseFacts, ImmutableDictionary<string, FileFacts> overlay)
    {
        if (overlay.Count == 0)
        {
            return baseFacts.CompilationHealth;
        }

        var baseHealth = baseFacts.CompilationHealth ?? CompilationHealth.Empty;
        return baseHealth with
        {
            Files = baseHealth
                .Files.Where(f => !overlay.ContainsKey(f.FilePath))
                .Concat(overlay.Values.SelectMany(slice => slice.CompileHealth))
                .OrderBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    private sealed class CompositeView(AnalysisResult baseFacts, ImmutableDictionary<string, FileFacts> overlay) : IFactSnapshotView
    {
        public string SolutionPath => baseFacts.SolutionPath;

        public IEnumerable<SourceFileInfo> EnumerateSourceFiles() =>
            EnumerateReplaced(overlay, baseFacts.SourceFiles, f => f.FilePath, slice => slice.SourceFiles);

        public IEnumerable<DiRegistrationInfo> EnumerateDiRegistrations() =>
            EnumerateReplaced(
                overlay,
                baseFacts.DiRegistrations,
                r => r.FilePath,
                slice => slice.DiRegistrations,
                r => r.FilePath.Length == 0 || !string.Equals(Path.GetExtension(r.FilePath), ".cs", StringComparison.OrdinalIgnoreCase)
            );

        public IEnumerable<SymbolFact> EnumerateSymbols() =>
            EnumerateReplaced(overlay, baseFacts.Symbols ?? [], s => s.FilePath, slice => slice.Symbols);

        public IEnumerable<ReferenceFact> EnumerateReferences() =>
            EnumerateReplaced(overlay, baseFacts.References ?? [], r => r.FilePath, slice => slice.References);

        public IEnumerable<TypeRelationFact> EnumerateTypeRelations() =>
            EnumerateReplaced(overlay, baseFacts.TypeRelations ?? [], r => r.FilePath, slice => slice.TypeRelations);

        public IEnumerable<DispatchFact> EnumerateDispatchFacts() =>
            EnumerateReplaced(overlay, baseFacts.DispatchFacts ?? [], d => d.FilePath, slice => slice.Dispatch);

        public IEnumerable<AllocationFact> EnumerateAllocationFacts() =>
            EnumerateReplaced(overlay, baseFacts.AllocationFacts ?? [], a => a.FilePath, slice => slice.Allocations);

        public CompilationHealth? GetCompilationHealth() => MergeCompilationHealth(baseFacts, overlay);
    }
}
