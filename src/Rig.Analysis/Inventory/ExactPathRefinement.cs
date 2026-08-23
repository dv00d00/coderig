using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Rig.Domain.Data;
using Rig.Domain.Functions;

namespace Rig.Analysis.Inventory;

// Everything required to reproduce PathCommand's demand-shaped forward topology without depending on CLI
// option types. Rules are already shaped (`--raw` applied) and the traversal mode/depth exactly match the
// command that will consume the resulting snapshot.
internal sealed record ExactPathDemand(
    string FromPattern,
    string ToPattern,
    DemandForwardGraphRules Rules,
    int MaxDepth,
    FactPathFinder.TraversalMode Mode
);

internal enum ExactPathRefinementKind
{
    ExactUnchanged,
    ExactPublished,
    Superseded,
    ExactUnavailable,
}

internal sealed record ExactPathRefinementOutcome(ExactPathRefinementKind Kind, FactSnapshot Snapshot, string? Reason = null)
{
    internal static ExactPathRefinementOutcome Unchanged(FactSnapshot snapshot) => new(ExactPathRefinementKind.ExactUnchanged, snapshot);

    internal static ExactPathRefinementOutcome Published(FactSnapshot snapshot) => new(ExactPathRefinementKind.ExactPublished, snapshot);

    internal static ExactPathRefinementOutcome Superseded(FactSnapshot snapshot) => new(ExactPathRefinementKind.Superseded, snapshot);

    internal static ExactPathRefinementOutcome Unavailable(FactSnapshot snapshot, string reason) =>
        new(ExactPathRefinementKind.ExactUnavailable, snapshot, reason);
}

internal sealed record ExactPathPlan(
    ImmutableHashSet<ProjectId> SelectedOrigins,
    ImmutableHashSet<ProjectId> UnknownOrigins,
    bool FromMatched,
    bool ToMatched,
    string? UnavailableReason
);

internal static class ExactPathRefinement
{
    internal static ExactPathPlan Plan(FactSnapshot snapshot, ExactPathDemand demand)
    {
        DemandForwardGraphResult load;
        try
        {
            load = DemandForwardPathGraph.Build(
                snapshot.GraphView,
                demand.Rules,
                new DemandForwardGraphRequest(demand.FromPattern, demand.MaxDepth, demand.Mode)
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable($"demand topology could not be materialized: {exception.Message}");
        }

        var catalog = snapshot.GraphView.MethodSymbolIds.ToArray();
        var fromIds = FactPathFinder.DistinctMatchTargets(catalog, demand.FromPattern);
        var toIds = FactPathFinder.DistinctMatchTargets(catalog, demand.ToPattern);
        var reachable = FactPathFinder.Reaches(
            load.Graph,
            demand.FromPattern,
            maxDepth: demand.MaxDepth,
            maxNodes: int.MaxValue,
            mode: demand.Mode
        );

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

        boundaryProjects.UnionWith(ProjectDependencyClosure(snapshot.Solution, fromProjects, reverse: false));
        boundaryProjects.UnionWith(ProjectDependencyClosure(snapshot.Solution, toProjects, reverse: true));

        var selected = ImmutableHashSet.CreateBuilder<ProjectId>();
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

        // Missing endpoints may be generated declarations retired/introduced by an Unknown origin. Until
        // those generated shards are refreshed, an authoritative no-match would be stale. If ownership
        // cannot narrow the missing declaration, refresh every Unknown dirty origin (origin-conservative).
        if (fromIds.Count == 0 || toIds.Count == 0)
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
        return new ExactPathPlan(selected.ToImmutable(), unknown, fromIds.Count > 0, toIds.Count > 0, null);

        ExactPathPlan Unavailable(string reason) =>
            new(ImmutableHashSet<ProjectId>.Empty, ImmutableHashSet<ProjectId>.Empty, false, false, reason);
    }

    private static ImmutableHashSet<ProjectId> ProjectDependencyClosure(Solution solution, IEnumerable<ProjectId> seeds, bool reverse)
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

    private static bool DependsOnAny(Solution solution, ProjectId projectId, IEnumerable<ProjectId> candidates)
    {
        var candidateSet = candidates.ToImmutableHashSet();
        return ProjectDependencyClosure(solution, [projectId], reverse: false).Overlaps(candidateSet);
    }

    private static bool CanChangeGeneratedSeeds(FactSnapshot snapshot, ProjectId projectId) =>
        snapshot.Solution.GetProject(projectId)?.AnalyzerReferences.Count > 0
        || snapshot.Surfaces.Projects.TryGetValue(projectId, out var partition) && partition.GeneratedShards.Length > 0;
}
