using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
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

internal static class ExactCallersRefinement
{
    internal static ExactCallersPlan Plan(FactSnapshot snapshot, ExactCallersDemand demand)
    {
        DemandReverseCallersGraphResult load;
        try
        {
            load = DemandReverseCallersGraph.Build(
                snapshot.GraphView,
                demand.Rules,
                new DemandReverseCallersGraphRequest(demand.ToPattern, demand.MaxDepth, demand.DiscoveryMode, demand.MaxNodes)
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable($"demand topology could not be materialized: {exception.Message}");
        }

        var boundaryProjects = ImmutableHashSet.CreateBuilder<ProjectId>();
        var boundaryFiles = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbolId in load.Ownership.SymbolIds)
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

        foreach (var emitterPath in load.Ownership.EmitterFilePaths)
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

        if (load.TargetIds.Length == 0)
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
        return new ExactCallersPlan(selected.ToImmutable(), unknown, load.TargetIds.Length > 0, null);

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
}
