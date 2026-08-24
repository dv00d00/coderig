using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Rig.Domain.Data;
using Rig.Domain.Functions;

namespace Rig.Analysis.Inventory;

// Everything required to reproduce a command's demand-shaped forward topology without depending on CLI
// option types. Rules are already shaped (`--raw` applied) and the traversal mode/depth exactly match the
// command that will consume the resulting snapshot. Path additionally supplies TO so its reverse endpoint
// ownership can widen the boundary; reaches/tree deliberately do not substitute a project-reference cone
// for the keyed forward graph they actually consume.
internal enum ExactForwardQueryKind
{
    Path,
    Reaches,
    Tree,
}

internal enum ExactForwardDebtScope
{
    DemandBoundary,
    WholeResident,
}

internal interface IExactQueryDemand
{
    string Verb { get; }
}

internal interface IExactDebtPlan
{
    ImmutableHashSet<ProjectId> SelectedOrigins { get; }
    ImmutableHashSet<ProjectId> UnknownOrigins { get; }
    string? UnavailableReason { get; }
}

internal sealed record ExactForwardDemand(
    ExactForwardQueryKind QueryKind,
    string FromPattern,
    string? ToPattern,
    DemandForwardGraphRules Rules,
    int MaxDepth,
    FactPathFinder.TraversalMode Mode,
    ExactForwardDebtScope DebtScope = ExactForwardDebtScope.DemandBoundary
) : IExactQueryDemand
{
    internal string Verb =>
        QueryKind switch
        {
            ExactForwardQueryKind.Path => "path",
            ExactForwardQueryKind.Reaches => "reaches",
            ExactForwardQueryKind.Tree => "tree",
            _ => throw new InvalidOperationException($"Unknown exact forward query kind: {QueryKind}"),
        };

    string IExactQueryDemand.Verb => Verb;
}

internal enum ExactForwardRefinementKind
{
    ExactUnchanged,
    ExactPublished,
    Superseded,
    ExactUnavailable,
}

internal sealed record ExactForwardRefinementOutcome(ExactForwardRefinementKind Kind, FactSnapshot Snapshot, string? Reason = null)
{
    internal static ExactForwardRefinementOutcome Unchanged(FactSnapshot snapshot) =>
        new(ExactForwardRefinementKind.ExactUnchanged, snapshot);

    internal static ExactForwardRefinementOutcome Published(FactSnapshot snapshot) =>
        new(ExactForwardRefinementKind.ExactPublished, snapshot);

    internal static ExactForwardRefinementOutcome Superseded(FactSnapshot snapshot) => new(ExactForwardRefinementKind.Superseded, snapshot);

    internal static ExactForwardRefinementOutcome Unavailable(FactSnapshot snapshot, string reason) =>
        new(ExactForwardRefinementKind.ExactUnavailable, snapshot, reason);
}

internal sealed record ExactForwardPlan(
    ImmutableHashSet<ProjectId> SelectedOrigins,
    ImmutableHashSet<ProjectId> UnknownOrigins,
    bool FromMatched,
    bool ToMatched,
    string? UnavailableReason
) : IExactDebtPlan;

// See ExactCallersBoundarySource — the same seam, for the forward planner. The keyed builder is retained
// ONLY as the differential oracle (PlannerMaterializedGraphTests); the routed path never takes it.
internal enum ExactForwardBoundarySource
{
    MaterializedGraph,
    KeyedDemand,
}

internal static class ExactForwardRefinement
{
    internal static ExactForwardPlan Plan(FactSnapshot snapshot, ExactForwardDemand demand) =>
        Plan(snapshot, demand, ExactForwardBoundarySource.MaterializedGraph);

    internal static ExactForwardPlan Plan(FactSnapshot snapshot, ExactForwardDemand demand, ExactForwardBoundarySource source)
    {
        // MATERIALIZED, not projected per query — the same fix ExactCallersRefinement carries, and for the
        // same measured reason. DemandForwardPathGraph.Build ran a fixed point whose every pass called
        // FactPathFinder.Reaches over the partial snapshot, and Reaches rebuilds the whole graph index from
        // scratch on each call: cost O(passes x graph), with passes ~ closure depth. ResidentIndex loops this
        // planner up to 12 times before the query even starts. The whole projected call graph is materialized
        // ONCE per fact generation and shared with the query that follows (FactSnapshot.ProjectedCallGraph),
        // so the planner's traversal costs one BFS instead of thousands of index rebuilds.
        FactGraphData graph;
        IReadOnlyDictionary<string, int> reachable;
        (IReadOnlyCollection<string> SymbolIds, IReadOnlyCollection<string> EmitterFilePaths) delivery;
        try
        {
            if (source == ExactForwardBoundarySource.KeyedDemand)
            {
                var load = DemandForwardPathGraph.Build(
                    snapshot.GraphView,
                    demand.Rules,
                    new DemandForwardGraphRequest(demand.FromPattern, demand.MaxDepth, demand.Mode)
                );
                graph = load.Graph;
                delivery = (load.Ownership?.SymbolIds ?? [], load.Ownership?.EmitterFilePaths ?? []);
            }
            else
            {
                graph = snapshot.ProjectedCallGraph(demand.Rules);
                delivery = ([], []);
            }

            reachable = FactPathFinder.Reaches(
                graph,
                demand.FromPattern,
                maxDepth: demand.MaxDepth,
                maxNodes: int.MaxValue,
                mode: demand.Mode
            );
            if (source == ExactForwardBoundarySource.MaterializedGraph)
            {
                delivery = DeliveryBoundary(graph, reachable, demand);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable($"demand topology could not be materialized: {exception.Message}");
        }

        // Endpoint matching is UNCHANGED: it never came from the demand load, it matches the pattern against
        // the generation's whole method catalog. Left exactly as it was.
        var catalog = snapshot.GraphView.MethodSymbolIds.ToArray();
        var fromIds = FactPathFinder.DistinctMatchTargets(catalog, demand.FromPattern);
        IReadOnlyList<string> toIds = demand.ToPattern is null ? [] : FactPathFinder.DistinctMatchTargets(catalog, demand.ToPattern);

        var boundaryProjects = ImmutableHashSet.CreateBuilder<ProjectId>();
        var boundaryFiles = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        var fromProjects = ImmutableHashSet.CreateBuilder<ProjectId>();
        var toProjects = ImmutableHashSet.CreateBuilder<ProjectId>();

        var relevantIds = reachable.Keys.Select(MonomorphizedNodeId.BaseOf).Concat(fromIds).Concat(toIds).Distinct(StringComparer.Ordinal);
        foreach (var symbolId in relevantIds)
        {
            foreach (var row in snapshot.GraphView.MethodsById(symbolId))
            {
                EmitterOwnership ownership;
                try
                {
                    if (!string.IsNullOrWhiteSpace(row.FilePath))
                    {
                        boundaryFiles.Add(Path.GetFullPath(row.FilePath));
                    }
                    ownership = snapshot.Surfaces.ResolveEmitterOwnership(snapshot.Solution, row.FilePath, row.DefiningAssembly);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return Unavailable($"emitter ownership could not be normalized: {exception.Message}");
                }
                if (!ownership.IsExact)
                {
                    return Unavailable(ownership.UnavailableReason!);
                }

                boundaryProjects.UnionWith(ownership.ProjectIds);
                // A pattern may name both endpoints (`path X X`); populate the two topology sides
                // independently rather than letting FROM win a ternary.
                if (fromIds.Contains(symbolId, StringComparer.Ordinal))
                {
                    fromProjects.UnionWith(ownership.ProjectIds);
                }
                if (toIds.Contains(symbolId, StringComparer.Ordinal))
                {
                    toProjects.UnionWith(ownership.ProjectIds);
                }
            }
        }

        // Delivery joins read subscription/registration and producer partitions that can sit outside the
        // ordinary call closure. They are query inputs, so their emitters participate in the same exact
        // ownership boundary; otherwise an edited handler/channel could leave a plausible stale answer.
        foreach (var symbolId in delivery.SymbolIds)
        {
            foreach (var row in snapshot.GraphView.SymbolsById(MonomorphizedNodeId.BaseOf(symbolId)))
            {
                if (!TryAddDeliveryOwnership(row.FilePath, row.DefiningAssembly, out var reason))
                {
                    return Unavailable(reason!);
                }
            }
        }
        foreach (var emitterPath in delivery.EmitterFilePaths)
        {
            if (!TryAddDeliveryOwnership(emitterPath, assemblyName: null, out var reason))
            {
                return Unavailable(reason!);
            }
        }

        boundaryProjects.UnionWith(ProjectDependencyClosure(snapshot.Solution, fromProjects, reverse: false));
        if (demand.ToPattern is not null)
        {
            boundaryProjects.UnionWith(ProjectDependencyClosure(snapshot.Solution, toProjects, reverse: true));
        }

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

        // Missing endpoints may be generated declarations retired/introduced by an Unknown origin. Until
        // those generated shards are refreshed, an authoritative no-match would be stale. If ownership
        // cannot narrow the missing declaration, refresh every Unknown dirty origin (origin-conservative).
        if (fromIds.Count == 0 || demand.ToPattern is not null && toIds.Count == 0)
        {
            selected.UnionWith(
                snapshot.Dirty.PendingByOrigin.Keys.Where(id => snapshot.Delta.SurfaceStates.GetValueOrDefault(id) == SurfaceState.Unknown)
            );
        }

        // Any generator-capable Unknown origin can add a NEW seed that matches an already-successful broad
        // pattern, so old endpoint matches cannot narrow it safely. Analyzer references are the independent
        // zero->one capability signal; existing generated shards cover retained solutions where that signal
        // is unavailable. Ordinary source declarations are already current through eager file extraction.
        foreach (var origin in snapshot.Dirty.PendingByOrigin.Keys)
        {
            if (
                snapshot.Delta.SurfaceStates.GetValueOrDefault(origin) == SurfaceState.Unknown
                && (CanChangeGeneratedSeeds(snapshot, origin) || DependsOnAny(snapshot.Solution, origin, boundaryProjects))
            )
            {
                selected.Add(origin);
            }
        }

        var unknown = selected.Where(id => snapshot.Delta.SurfaceStates.GetValueOrDefault(id) == SurfaceState.Unknown).ToImmutableHashSet();
        return new ExactForwardPlan(selected.ToImmutable(), unknown, fromIds.Count > 0, demand.ToPattern is null || toIds.Count > 0, null);

        bool TryAddDeliveryOwnership(string? emitterPath, string? assemblyName, out string? unavailableReason)
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
                unavailableReason = $"delivery emitter ownership could not be normalized: {exception.Message}";
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

        ExactForwardPlan Unavailable(string reason) =>
            new(ImmutableHashSet<ProjectId>.Empty, ImmutableHashSet<ProjectId>.Empty, false, false, reason);
    }

    // THE DELIVERY HALF of the boundary — what the keyed builder reported as `Ownership`, and the ONE input
    // here that is not a straight translation. Its Ownership was a READ LOG of DemandDeliverySiteSource: every
    // row that source touched while projecting the channels the forward closure produces on, which includes
    // registration/producer sites the call closure itself never enters. A whole-graph answer has no read log,
    // so the analogue is reconstructed from the graph's own edges:
    //
    //   * a DELIVERY edge (DeliveryPrecision is non-null — AddDeliveryEdges is the only writer) whose PRODUCER
    //     is in the closure: its FilePath is the publish site, its Callee the handler. Under AsyncExact the
    //     Fanout half of a channel is cut from the traversal, so the handler is admitted here even when it is
    //     not reachable — the same over-approximation the keyed read log made by reading the whole channel.
    //   * a METHODGROUP edge that BINDS a node in the closure: its FilePath is the registration site
    //     (`someEvent += H`, `Subscribe(H)`), which is exactly what AddDeliveryEdges joins on and is where a
    //     handler is added or removed. Editing it changes what the producer reaches.
    //
    // GATED IDENTICALLY to the keyed builder (ExpandDeliveryChannels returns immediately on SyncCut or with no
    // delivery rules), so the DEFAULT sync `path`/`reaches`/`tree` plan is a provably empty contribution —
    // there, the conversion is a pure graph swap with nothing to reconstruct.
    private static (IReadOnlyCollection<string> SymbolIds, IReadOnlyCollection<string> EmitterFilePaths) DeliveryBoundary(
        FactGraphData graph,
        IReadOnlyDictionary<string, int> reachable,
        ExactForwardDemand demand
    )
    {
        if (demand.Mode == FactPathFinder.TraversalMode.SyncCut || demand.Rules.Delivery is not { Count: > 0 })
        {
            return ([], []);
        }

        var symbols = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in graph.CallEdges)
        {
            var publishesFromClosure = edge.DeliveryPrecision is not null && reachable.ContainsKey(edge.Caller);
            var bindsIntoClosure =
                string.Equals(edge.Kind, EdgeKinds.MethodGroup, StringComparison.Ordinal) && reachable.ContainsKey(edge.Callee);
            if (!publishesFromClosure && !bindsIntoClosure)
            {
                continue;
            }

            symbols.Add(MonomorphizedNodeId.BaseOf(edge.Caller));
            symbols.Add(MonomorphizedNodeId.BaseOf(edge.Callee));
            if (!string.IsNullOrWhiteSpace(edge.FilePath))
            {
                files.Add(edge.FilePath);
            }
        }

        return (symbols, files);
    }

    internal static ImmutableHashSet<ProjectId> ProjectDependencyClosure(Solution solution, IEnumerable<ProjectId> seeds, bool reverse)
    {
        var seen = ImmutableHashSet.CreateBuilder<ProjectId>();
        var queue = new Queue<ProjectId>(seeds);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            IEnumerable<ProjectId> adjacent = reverse
                ? solution
                    .Projects.Where(project => project.ProjectReferences.Any(reference => reference.ProjectId == current))
                    .Select(p => p.Id)
                : solution.GetProject(current)?.ProjectReferences.Select(reference => reference.ProjectId) ?? Enumerable.Empty<ProjectId>();
            foreach (var projectId in adjacent)
            {
                queue.Enqueue(projectId);
            }
        }

        return seen.ToImmutable();
    }

    internal static bool DependsOnAny(Solution solution, ProjectId projectId, IEnumerable<ProjectId> candidates)
    {
        var candidateSet = candidates.ToImmutableHashSet();
        return ProjectDependencyClosure(solution, [projectId], reverse: false).Overlaps(candidateSet);
    }

    internal static bool CanChangeGeneratedSeeds(FactSnapshot snapshot, ProjectId projectId) =>
        snapshot.Solution.GetProject(projectId)?.AnalyzerReferences.Count > 0
        || snapshot.Surfaces.Projects.TryGetValue(projectId, out var partition) && partition.GeneratedShards.Length > 0;
}
