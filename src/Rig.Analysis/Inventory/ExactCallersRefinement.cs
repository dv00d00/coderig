using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Rig.Domain.Data;
using Rig.Domain.Functions;

namespace Rig.Analysis.Inventory;

// Discovery may deliberately be wider than execution. In particular, sync `callers --entrypoints`
// renders an async-reachable hint, so its debt boundary must be discovered with AsyncExact while the
// command still executes its ordinary SyncCut reverse traversal over the resulting graph.
internal sealed record ExactCallersDemand(
    string ToPattern,
    DemandForwardGraphRules Rules,
    int MaxDepth,
    FactPathFinder.TraversalMode ExecutionMode,
    FactPathFinder.TraversalMode DiscoveryMode,
    ExactForwardDebtScope DebtScope = ExactForwardDebtScope.DemandBoundary,
    int MaxNodes = 250_000
) : IExactQueryDemand
{
    public string Verb => "callers";
}

internal sealed record ExactCallersPlan(
    ImmutableHashSet<ProjectId> SelectedOrigins,
    ImmutableHashSet<ProjectId> UnknownOrigins,
    bool ToMatched,
    string? UnavailableReason
) : IExactDebtPlan;

// WHERE THE PLANNER'S THREE BOUNDARY INPUTS COME FROM.
//
// The demand builder was replaced on the ROUTED path (2026-08-24) because its fixed point re-derived the
// whole graph index once per pass — O(passes x graph). Measured on a 22,000-node corpus: 2,003 passes /
// 67,523 ms; on the real 227-project monorepo a routed live `callers` never finished and only the client's
// 30s timeout rescued it. It is retained here as the DIFFERENTIAL ORACLE the new derivation is tested
// against (PlannerMaterializedGraphTests), and for nothing else.
internal enum ExactCallersBoundarySource
{
    // The whole projected call graph, materialized ONCE per fact generation and shared with the query that
    // follows the plan (FactSnapshot.ProjectedCallGraph). The routed path.
    MaterializedGraph,

    // The per-query keyed demand projection. Oracle only — do NOT route this.
    KeyedDemand,
}

// The three things the planner's policy consumes, decoupled from how they were obtained.
internal readonly record struct ExactCallersBoundaryInputs(
    int MatchedTargetCount,
    IReadOnlyCollection<string> ClosureSymbolIds,
    IReadOnlyCollection<string> EmitterFilePaths
);

internal static class ExactCallersRefinement
{
    internal static ExactCallersPlan Plan(FactSnapshot snapshot, ExactCallersDemand demand) =>
        Plan(snapshot, demand, ExactCallersBoundarySource.MaterializedGraph);

    internal static ExactCallersPlan Plan(FactSnapshot snapshot, ExactCallersDemand demand, ExactCallersBoundarySource source)
    {
        ExactCallersBoundaryInputs load;
        try
        {
            load =
                source == ExactCallersBoundarySource.KeyedDemand
                    ? FromKeyedDemand(snapshot, demand)
                    : FromMaterializedGraph(snapshot, demand);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable($"demand topology could not be materialized: {exception.Message}");
        }

        var boundaryProjects = ImmutableHashSet.CreateBuilder<ProjectId>();
        var boundaryFiles = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbolId in load.ClosureSymbolIds)
        {
            // External targets and structural ids legitimately have no retained declaration row. They do
            // not confer ownership; every row that is represented must, however, resolve exactly.
            foreach (var row in snapshot.GraphView.SymbolsById(symbolId))
            {
                if (!TryAddOwnership(row.FilePath, row.DefiningAssembly, out var reason))
                {
                    return Unavailable(reason!);
                }
            }
        }

        foreach (var emitterPath in load.EmitterFilePaths)
        {
            if (!TryAddOwnership(emitterPath, assemblyName: null, out var reason))
            {
                return Unavailable(reason!);
            }
        }

        // A structural hub can be owned by a contracts project even when the final implementation target
        // lives elsewhere. Any project depending on any admitted owner can rebind an incoming edge, so the
        // reverse project-reference closure is part of the exact reverse boundary.
        boundaryProjects.UnionWith(
            ExactForwardRefinement.ProjectDependencyClosure(snapshot.Solution, boundaryProjects.ToImmutable(), reverse: true)
        );

        var selected = ImmutableHashSet.CreateBuilder<ProjectId>();
        if (demand.DebtScope == ExactForwardDebtScope.WholeResident)
        {
            selected.UnionWith(snapshot.Dirty.PendingByOrigin.Keys);
        }
        else
        {
            foreach (var (origin, contribution) in snapshot.Dirty.PendingByOrigin)
            {
                if (
                    contribution.Any(documentId =>
                        boundaryProjects.Contains(documentId.ProjectId)
                        || snapshot.Solution.GetDocument(documentId)?.FilePath is { } file && boundaryFiles.Contains(Path.GetFullPath(file))
                    )
                )
                {
                    selected.Add(origin);
                }
            }
        }

        if (load.MatchedTargetCount == 0)
        {
            selected.UnionWith(
                snapshot.Dirty.PendingByOrigin.Keys.Where(id => snapshot.Delta.SurfaceStates.GetValueOrDefault(id) == SurfaceState.Unknown)
            );
        }

        foreach (var origin in snapshot.Dirty.PendingByOrigin.Keys)
        {
            if (
                snapshot.Delta.SurfaceStates.GetValueOrDefault(origin) == SurfaceState.Unknown
                && (
                    ExactForwardRefinement.CanChangeGeneratedSeeds(snapshot, origin)
                    || ExactForwardRefinement.DependsOnAny(snapshot.Solution, origin, boundaryProjects)
                )
            )
            {
                selected.Add(origin);
            }
        }

        var unknown = selected.Where(id => snapshot.Delta.SurfaceStates.GetValueOrDefault(id) == SurfaceState.Unknown).ToImmutableHashSet();
        return new ExactCallersPlan(selected.ToImmutable(), unknown, load.MatchedTargetCount > 0, null);

        bool TryAddOwnership(string? emitterPath, string? assemblyName, out string? unavailableReason)
        {
            EmitterOwnership ownership;
            try
            {
                if (!string.IsNullOrWhiteSpace(emitterPath))
                {
                    boundaryFiles.Add(Path.GetFullPath(emitterPath));
                }
                ownership = snapshot.Surfaces.ResolveEmitterOwnership(snapshot.Solution, emitterPath, assemblyName);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                unavailableReason = $"emitter ownership could not be normalized: {exception.Message}";
                return false;
            }

            if (!ownership.IsExact)
            {
                unavailableReason = ownership.UnavailableReason;
                return false;
            }

            boundaryProjects.UnionWith(ownership.ProjectIds);
            unavailableReason = null;
            return true;
        }

        ExactCallersPlan Unavailable(string reason) =>
            new(ImmutableHashSet<ProjectId>.Empty, ImmutableHashSet<ProjectId>.Empty, false, reason);
    }

    // THE ROUTED DERIVATION. Everything the planner needs is already in the generation's materialized graph;
    // nothing here projects a keyed partition or runs a fixed point.
    //
    //  * MATCHED TARGETS are the depth-0 entries of the closure. ReachedByCore seeds exactly
    //    MatchNodes(index.Nodes, pattern) at 0 and never re-depths an existing key, so the depth-0 keys ARE
    //    FactPathFinder.MatchedNodes over the same node universe — obtained here for free rather than by a
    //    second BuildIndex over the whole graph.
    //  * CLOSURE SYMBOLS are the reverse closure itself, under the demand's DISCOVERY mode (which is
    //    deliberately wider than execution for sync `callers --entrypoints`). Monomorphized ids collapse to
    //    their base, because that is the key SymbolsById is partitioned by.
    //  * EMITTER FILES are the files that emit an edge INTO the closure. NOT "every file in the solution":
    //    a whole-graph answer read everything, but claiming the whole solution as the boundary would select
    //    every dirty origin and turn the precision win into a refinement-cost regression. Callee-side is
    //    sufficient rather than Caller-or-Callee: an edge's FilePath is its CALL SITE, so a caller admitted
    //    to the closure necessarily has an into-the-closure edge carrying that same file.
    //
    // WHY THIS IS NOT AN UNDER-APPROXIMATION of the demand builder's Ownership, which additionally named
    // every symbol it PROBED (dispatch families, MethodsByContainingType candidates, name+arity heuristics):
    // those were discovery scaffolding for a builder that could not see the whole graph, and the whole-graph
    // closure already proves which of them can reach the target. The residual worry — a sibling implementer
    // in a project the closure never enters growing a NEW call into the target — is caught project-side, not
    // symbol-side: any such caller must reference the target's or its hub's project, and
    // ProjectDependencyClosure(reverse: true) over the boundary projects (unchanged, above) selects it.
    private static ExactCallersBoundaryInputs FromMaterializedGraph(FactSnapshot snapshot, ExactCallersDemand demand)
    {
        var graph = snapshot.ProjectedCallGraph(demand.Rules);
        var closure = FactPathFinder.ReachedBy(
            graph,
            demand.ToPattern,
            maxDepth: demand.MaxDepth,
            maxNodes: int.MaxValue,
            narrowDispatch: true,
            mode: demand.DiscoveryMode
        );

        // The demand builder's own closure cap, kept: MaxNodes is the caller's `--max-nodes` budget for how
        // much reverse closure a refinement may consider, and it still bounds honestly here. (The QUERY arm
        // deliberately drops it — there the whole graph IS the answer and nothing is partial — but a plan
        // over an unbounded closure is a debt boundary the user never asked to pay for. An exceeded budget
        // degrades to a disclosure, not a failed query.)
        if (closure.Count > demand.MaxNodes)
        {
            throw new DemandReverseCallersGraphUnavailableException(
                $"Reverse closure reached {closure.Count} nodes, past the {demand.MaxNodes} node cap. Raise it with --max-nodes <n> (0 = uncapped), or narrow the query with --depth <n>."
            );
        }

        var matchedTargets = 0;
        var boundaryNodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (node, depth) in closure)
        {
            if (depth == 0)
            {
                matchedTargets++;
            }
            boundaryNodes.Add(MonomorphizedNodeId.BaseOf(node));
        }

        AddDispatchHubs(graph, boundaryNodes);

        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in graph.CallEdges)
        {
            if (
                !string.IsNullOrWhiteSpace(edge.FilePath)
                && (closure.ContainsKey(edge.Callee) || boundaryNodes.Contains(MonomorphizedNodeId.BaseOf(edge.Callee)))
            )
            {
                files.Add(edge.FilePath);
            }
        }

        return new ExactCallersBoundaryInputs(matchedTargets, boundaryNodes, files);
    }

    // THE DISPATCH HUBS the closure routes through — the interface/base members a closure node implements or
    // overrides, transitively, and the delegate slots bound to it.
    //
    // NOT an optional widening: FactPathFinder's NARROWED reverse dispatch maps `impl -> callers of the hub`
    // and never yields the HUB ITSELF as a closure node (BuildReverseMaps inverts forward's per-edge
    // DispatchTargets, so the interface member is jumped over, not stepped on). The hub's DECLARATION is
    // nonetheless load-bearing debt: edit `IWork.Run`'s signature and Impl.Work.Run's binding — hence its
    // caller set — changes. The demand builder collected these explicitly (ReverseDispatchSources → methods
    // → ownership); without them the contracts project silently drops out of the boundary.
    //
    // DELIBERATELY WITHOUT the impl↔override kind constraint that governs TRAVERSAL. That rule exists so a
    // mined-fact closure cannot compose two resolutions into a call that never happens — it is about
    // REACHABILITY. Ownership is a different question: `Derived.Run` overrides `Work.Run` which implements
    // `IWork.Run`, and editing `IWork.Run` changes what `Derived.Run` binds to and therefore who reaches it,
    // whether or not one dispatch resolution may cross those two hops. Measured on the differential corpus:
    // with the constraint applied, a `callers Impl.Derived.Run` plan dropped the contracts project (and, with
    // it, the caller project whose only route in is the interface) that the demand builder admitted. The
    // over-approximate walk is the correct one here — under-selecting a debt boundary is silently wrong,
    // over-selecting is only slow.
    private static void AddDispatchHubs(FactGraphData graph, HashSet<string> boundaryNodes)
    {
        if (graph.MinedDispatch is not { Count: > 0 } mined)
        {
            return;
        }

        var sourcesByTarget = new Dictionary<string, List<DispatchFact>>(StringComparer.Ordinal);
        foreach (var fact in mined)
        {
            if (fact.Kind is not (DispatchKinds.Impl or DispatchKinds.Override or DispatchKinds.DelegateBind))
            {
                continue;
            }
            if (!sourcesByTarget.TryGetValue(fact.TargetMember, out var list))
            {
                sourcesByTarget[fact.TargetMember] = list = [];
            }
            list.Add(fact);
        }
        if (sourcesByTarget.Count == 0)
        {
            return;
        }

        var pending = new Queue<string>(boundaryNodes);
        var visited = new HashSet<string>(boundaryNodes, StringComparer.Ordinal);
        var hubs = new List<string>();
        while (pending.Count > 0)
        {
            if (!sourcesByTarget.TryGetValue(pending.Dequeue(), out var facts))
            {
                continue;
            }

            foreach (var fact in facts)
            {
                if (!visited.Add(fact.SourceMember))
                {
                    continue;
                }

                hubs.Add(fact.SourceMember);
                // A delegate slot is a terminal owner, not a member other members dispatch to.
                if (fact.Kind != DispatchKinds.DelegateBind)
                {
                    pending.Enqueue(fact.SourceMember);
                }
            }
        }

        boundaryNodes.UnionWith(hubs);
    }

    // The oracle. See ExactCallersBoundarySource.KeyedDemand — this is what the routed path used to do, kept
    // so a differential test can assert the two produce the SAME plan on a corpus small enough to build both.
    private static ExactCallersBoundaryInputs FromKeyedDemand(FactSnapshot snapshot, ExactCallersDemand demand)
    {
        var load = DemandReverseCallersGraph.Build(
            snapshot.GraphView,
            demand.Rules,
            new DemandReverseCallersGraphRequest(demand.ToPattern, demand.MaxDepth, demand.DiscoveryMode, demand.MaxNodes)
        );
        return new ExactCallersBoundaryInputs(load.TargetIds.Length, load.Ownership.SymbolIds, load.Ownership.EmitterFilePaths);
    }
}
