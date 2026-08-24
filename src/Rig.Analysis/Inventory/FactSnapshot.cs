using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Rig.Domain.Data;
using Rig.Domain.Functions;

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

internal sealed record DirtySet
{
    private DirtySet(Solution? solution, ImmutableDictionary<ProjectId, ImmutableHashSet<DocumentId>> pendingByOrigin)
    {
        PendingByOrigin = pendingByOrigin;
        PendingDocuments = pendingByOrigin.Values.SelectMany(d => d).ToImmutableHashSet();
        PendingProjects = PendingDocuments.Select(d => d.ProjectId).ToImmutableHashSet();
        var files = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        if (solution is not null)
        {
            foreach (var documentId in PendingDocuments)
            {
                var filePath = solution.GetDocument(documentId)?.FilePath;
                if (filePath is not null)
                {
                    files.Add(filePath);
                }
            }
        }
        PendingFiles = files.ToImmutable();
    }

    internal ImmutableDictionary<ProjectId, ImmutableHashSet<DocumentId>> PendingByOrigin { get; }
    internal ImmutableHashSet<DocumentId> PendingDocuments { get; }
    internal ImmutableHashSet<string> PendingFiles { get; }
    internal ImmutableHashSet<ProjectId> PendingProjects { get; }

    internal static DirtySet Empty { get; } = new(null, ImmutableDictionary<ProjectId, ImmutableHashSet<DocumentId>>.Empty);

    internal static DirtySet FromContributions(
        Solution solution,
        ImmutableDictionary<ProjectId, ImmutableHashSet<DocumentId>> pendingByOrigin
    ) => pendingByOrigin.Count == 0 ? Empty : new DirtySet(solution, pendingByOrigin);
}

internal sealed record SnapshotDelta(
    ImmutableHashSet<string> ReplacedFiles,
    ImmutableHashSet<ProjectId> ConservativelyAffectedProjects,
    ImmutableHashSet<string> KnownChangedSymbols,
    ImmutableDictionary<ProjectId, SurfaceState> SurfaceStates
)
{
    internal static SnapshotDelta Empty { get; } =
        new(
            ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase),
            ImmutableHashSet<ProjectId>.Empty,
            ImmutableHashSet<string>.Empty,
            ImmutableDictionary<ProjectId, SurfaceState>.Empty
        );
}

// One immutable, atomically publishable generation of the resident fact index. A snapshot deliberately
// has no predecessor link: readers pin the exact generation they captured, while generations with no
// readers become collectible. Live consumers stream this segmented view; the legacy flattened
// AnalysisResult remains lazy as an explicit compatibility/oracle boundary.
internal sealed class FactSnapshot : IIndexedFactSnapshotView
{
    private readonly Lazy<AnalysisResult> _flattenedFacts;
    private readonly Lazy<CompilationHealth?> _compilationHealth;
    private readonly object _projectedGraphGate = new();
    private readonly Dictionary<string, FactGraphData> _projectedGraphs = new(StringComparer.Ordinal);

    // Wall time of the FIRST projected-graph build on this generation, or null if nothing has built one.
    // Guarded by _projectedGraphGate, like the dictionary it describes. See ProjectedCallGraphBuild.
    private TimeSpan? _projectedGraphBuild;

    internal FactSnapshot(
        FactRevision revision,
        Solution solution,
        AnalysisResult baseFacts,
        ImmutableDictionary<string, FileFacts> overlay,
        DirtySet dirty,
        SnapshotDelta delta,
        ProjectSurfaceCatalog? surfaces = null,
        SegmentedFactGraphBase? graphBase = null,
        SegmentedFactGraphOverlay? graphOverlay = null
    )
    {
        Revision = revision;
        Solution = solution ?? throw new ArgumentNullException(nameof(solution));
        BaseFacts = baseFacts ?? throw new ArgumentNullException(nameof(baseFacts));
        Overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        Dirty = dirty ?? throw new ArgumentNullException(nameof(dirty));
        Delta = delta ?? throw new ArgumentNullException(nameof(delta));
        Surfaces = surfaces ?? ProjectSurfaceCatalog.Empty;
        graphBase ??= SegmentedFactGraphBase.Build(BaseFacts);
        graphOverlay ??= SegmentedFactGraphOverlay.Empty.Replace(Overlay);
        GraphView = new SegmentedFactGraphView(graphBase, graphOverlay);
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
    internal ProjectSurfaceCatalog Surfaces { get; }
    public IFactGraphView GraphView { get; }

    internal SegmentedFactGraphOverlay GraphOverlay => ((SegmentedFactGraphView)GraphView).Overlay;

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

    // ---- The materialized projected call graph for THIS generation ----
    //
    // The generation IS the invalidation model: a snapshot is immutable, an applied edit batch publishes a
    // NEW snapshot, and the graph a reader materialized against the old one dies with the last reference to
    // it. There is no version to bump and no staleness window, exactly as for the lazies above.
    //
    // WHY HERE and not on the query-side bundle that builds it: the bundle is rebuilt whenever the host
    // re-wraps a generation, while the SNAPSHOT is the thing a query actually pins. Caching on the snapshot
    // means the whole-graph projection is paid once per generation no matter how many bundles wrap it.
    //
    // KEYED BY SHAPING, not by rule set: the resident ruleset is fixed for the host's lifetime (the live
    // surface declines `--rules`), so the only reachable variation is a command that deliberately zeroes
    // shaping slices — `--raw`. Two shapes are reachable today; the cap is a hard ceiling on how much graph
    // one generation can retain if that ever stops being true, and past it the caller simply builds fresh
    // rather than the cache growing without bound.
    //
    // The build runs UNDER the lock on purpose: two queries racing the same shape should join one build, not
    // each pay for a whole-graph projection and then discard one of them.
    private const int MaxProjectedShapes = 4;

    internal FactGraphData ProjectedCallGraph(string shapingKey, Func<FactGraphData> build)
    {
        lock (_projectedGraphGate)
        {
            if (_projectedGraphs.TryGetValue(shapingKey, out var cached))
            {
                return cached;
            }

            var watch = Stopwatch.StartNew();
            var graph = build();
            watch.Stop();
            // FIRST build only (`??=`), which is what makes the disclosure read "once per GENERATION" rather
            // than once per shaping slot: `--raw` legitimately takes a second slot, and reporting a second
            // multi-second graph row for it would read as the generation having paid twice for one artifact.
            // The first build is also the one a user actually waits on — every later slot is a rarer variant.
            _projectedGraphBuild ??= watch.Elapsed;
            if (_projectedGraphs.Count < MaxProjectedShapes)
            {
                _projectedGraphs[shapingKey] = graph;
            }

            return graph;
        }
    }

    // The artifact NAME this build is disclosed under. Deliberately the same string LiveFactSource's own memo
    // uses, because it is the same artifact: before the exact planner started filling this cache slot, the
    // memo built the graph and the "derived layer built this generation" note said `traversalGraph <ms>`.
    // Reporting the planner's build under a different name would have read as a new cost rather than the same
    // one moving, so the disclosure stays byte-comparable across that change.
    internal const string ProjectedCallGraphArtifact = "traversalGraph";

    // What the generation paid to materialize its projected call graph, for the host's per-query cost
    // disclosure — null until something builds one.
    //
    // WHY THIS EXISTS: the exact-query PLANNER runs before the query and fills the slot above, so on the
    // routed path LiveFactSource's `traversalGraph` memo is never forced and contributed no row. The cost did
    // not go away — on a large solution it is seconds the user waits inside their first query of a generation
    // — it just stopped being disclosed, and an answer-plus-disclosure tool silently dropping the biggest
    // number in the query is the regression. LiveFactSource.BuildTimes merges this back in.
    internal (string Artifact, TimeSpan Elapsed)? ProjectedCallGraphBuild
    {
        get
        {
            lock (_projectedGraphGate)
            {
                return _projectedGraphBuild is { } elapsed ? (ProjectedCallGraphArtifact, elapsed) : null;
            }
        }
    }

    // The SAME materialized graph, addressed by a demand's shaping instead of by a caller-computed key —
    // the form the exact-refinement PLANNERS want. They live in this assembly and never see a RuleSet (a
    // demand carries only the slices that shape edges), so without this overload a planner would have to
    // re-derive both the key and the projection and would then miss the query arm's cache slot entirely.
    //
    // KEY PARITY WITH THE QUERY ARM IS THE POINT. LiveQueryFactSource.ShapingKey formats exactly this
    // string from the RuleSet it resolved the demand against, so a planner and the query it precedes share
    // ONE slot and therefore ONE build per generation: the planner's materialization is not an added cost,
    // it is the query's cost paid earlier. The one reachable divergence is a demand with a NULL Delivery
    // slice — the query arm resolves that against the host's own rules while this shapes with none — so the
    // two take separate slots (correct, just unshared). LiveQueryRunner always supplies the slice; only
    // hand-built test demands leave it null.
    internal FactGraphData ProjectedCallGraph(DemandForwardGraphRules rules) =>
        ProjectedCallGraph(DemandShapingKey(rules), () => BuildProjectedCallGraph(rules));

    // The same two steps LiveQueryFactSource.BuildMaterializedGraph runs, over this generation's segmented
    // view: the traversal-shaped projection (LiveFactSource.TraversalGraphOf's body) followed by
    // AddDeliveryEdges — and, like it, deliberately BEFORE any event-subscription reclassification, which
    // AddDeliveryEdges depends on not having happened yet. Delivery edges are folded in unconditionally
    // because TRAVERSAL, not materialization, decides whether to cross them: one graph serves every mode.
    private FactGraphData BuildProjectedCallGraph(DemandForwardGraphRules rules)
    {
        var traversal = FactPathFinder.ShapeGraph(
            graph: FactGraphProjection.FromView(
                this,
                handoffRules: rules.Projection.Handoff ?? [],
                redirectRules: rules.Projection.Redirect ?? []
            ),
            factoryRules: rules.Projection.Factory ?? [],
            cutRules: rules.Cut,
            contextRules: rules.Context,
            monomorphizeSignatures: LiveReads.MonomorphizationSignatures(this)
        );
        return FactPathFinder.AddDeliveryEdges(traversal, LiveReads.DeliverySites(this, rules.Delivery ?? []));
    }

    // Byte-for-byte the format LiveQueryFactSource.ShapingKey emits, so both arms address one slot. Widen
    // BOTH together if the live surface ever gains a flag that shapes edges some other way.
    private static string DemandShapingKey(DemandForwardGraphRules rules) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"f{rules.Projection.Factory?.Count ?? 0}/c{rules.Cut.Count}/x{rules.Context.Count}/d{rules.Delivery?.Count ?? 0}/h{rules.Projection.Handoff?.Count ?? 0}/r{rules.Projection.Redirect?.Count ?? 0}"
        );

    // Carry another generation's materialized graphs onto this one, when the two are FACT-IDENTICAL.
    //
    // The only caller is ResidentIndex.WithRevision, which rebuilds a snapshot with the same base facts, the
    // same overlay and the same segmented graph — everything the projection is a function of — and changes
    // ONLY the revision stamp, so an exact refinement can publish its fixed point through one CAS. Without
    // this, that publish threw away a graph it had just paid for and the first query on the new generation
    // rebuilt an identical one: after every edit, TWO whole-graph projections (the planner's and the query's)
    // where the facts justify one. The guard is reference identity on both fact halves; anything else copies
    // nothing rather than aliasing a graph to facts it was not projected from.
    internal void InheritProjectedCallGraphsFrom(FactSnapshot source)
    {
        if (!ReferenceEquals(source.BaseFacts, BaseFacts) || !ReferenceEquals(source.Overlay, Overlay))
        {
            return;
        }

        // One lock at a time, never nested: two snapshots can never legitimately inherit from each other,
        // but a lock-order hazard that only "can't happen" is not worth leaving in a publication path.
        KeyValuePair<string, FactGraphData>[] carried;
        TimeSpan? carriedBuild;
        lock (source._projectedGraphGate)
        {
            carried = [.. source._projectedGraphs];
            carriedBuild = source._projectedGraphBuild;
        }

        lock (_projectedGraphGate)
        {
            foreach (var (key, graph) in carried)
            {
                _projectedGraphs[key] = graph;
            }

            // The BUILD COST rides along with the graph, for the same reason the graph does: a revision stamp
            // is not a rebuild, so the generation that inherits the graph is the generation that paid for it
            // and must be able to say so. Carried with `??=`, never overwritten — if this snapshot has already
            // recorded a build of its own, that one is the first and the disclosure must stay at ONE row.
            _projectedGraphBuild ??= carriedBuild;
        }
    }

    // Test/diagnostic seam: how many distinct shapes this generation is holding materialized. A query that
    // does not move this number paid nothing for its graph.
    internal int ProjectedCallGraphCount
    {
        get
        {
            lock (_projectedGraphGate)
            {
                return _projectedGraphs.Count;
            }
        }
    }

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

        // ImmutableDictionary iteration order is an implementation detail. Emitter order is the stable
        // raw-row tie-break shared with SegmentedFactGraphView, so duplicate semantic rows project the
        // same first value regardless of edit chronology.
        foreach (var (_, slice) in overlay.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
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
