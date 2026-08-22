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
// readers become collectible. The legacy flattened AnalysisResult is lazy so Slice 2 changes publication
// semantics without yet changing the query engine's input shape.
internal sealed class FactSnapshot
{
    private readonly Lazy<AnalysisResult> _flattenedFacts;

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
        _flattenedFacts = new Lazy<AnalysisResult>(
            () => ResidentIndex.MergeFacts(BaseFacts, Overlay),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
    }

    internal FactRevision Revision { get; }
    internal Solution Solution { get; }
    internal AnalysisResult BaseFacts { get; }
    internal ImmutableDictionary<string, FileFacts> Overlay { get; }
    internal DirtySet Dirty { get; }
    internal SnapshotDelta Delta { get; }
    internal AnalysisResult FlattenedFacts => _flattenedFacts.Value;
}
