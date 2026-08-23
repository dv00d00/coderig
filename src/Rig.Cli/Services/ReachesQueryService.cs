using System.Diagnostics;
using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using static Rig.Cli.Effects.EffectDerivation;

namespace Rig.Cli.Services;

// The reusable REACHABLE-EFFECTS computation shared by the CLI, resident live queries, and web host.
public static class ReachesQueryService
{
    public sealed record EffectSummary(string Provider, string Operation, string Glyph, int Sites);

    public sealed record ReachesQueryResult(
        string FromPattern,
        bool Matched,
        int ReachableCount,
        IReadOnlyList<EffectSummary> Effects,
        bool IntrinsicHidden
    );

    internal sealed record ReachesComputation(
        IReadOnlyDictionary<string, FactPathFinder.ReachInfo> Reachable,
        IReadOnlyList<DerivedEffect> Effects,
        int HiddenIntrinsic,
        FactGraphData Graph,
        FactEntryPointDeriver.FactEntryPointData? EpData,
        TimeSpan GraphLoadElapsed,
        TimeSpan TraversalElapsed
    );

    public static async Task<ReachesQueryResult> BuildAsync(
        string workingDirectory,
        string fromPattern,
        string? storeRef = null,
        bool async = false,
        bool intrinsic = false
    )
    {
        var rules = RuleSetLoader.Load(workingDirectory: workingDirectory, extraRules: [], loadedPaths: out _);
        await using var source = await StoreQueryFactSource.OpenAsync(
            new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: storeRef)
        );
        var computation = await ComputeAsync(
            source: source,
            rules: rules,
            shaped: rules,
            fromPattern: fromPattern,
            maxDepth: CommonOptions.DepthOrUnbounded(null),
            mode: CommonOptions.Mode(async: async),
            raw: false,
            only: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            exclude: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            includeIntrinsic: intrinsic
        );

        var summaries = computation
            .Effects.GroupBy(e => (e.Provider, e.Operation))
            .Select(g => new EffectSummary(
                Provider: g.Key.Provider,
                Operation: g.Key.Operation,
                Glyph: EmojiLookup.For(rules.EffectEmoji, provider: g.Key.Provider, operation: g.Key.Operation),
                Sites: g.Count()
            ))
            .OrderByDescending(s => s.Sites)
            .ThenBy(s => s.Provider, StringComparer.Ordinal)
            .ThenBy(s => s.Operation, StringComparer.Ordinal)
            .ToList();

        return new ReachesQueryResult(
            FromPattern: fromPattern,
            Matched: computation.Reachable.Count > 0,
            ReachableCount: computation.Reachable.Count,
            Effects: summaries,
            IntrinsicHidden: computation.HiddenIntrinsic > 0
        );
    }

    // Rich computation used by BOTH ReachesCommand and BuildAsync. It deliberately accepts an already-open
    // fact source so resident live generations retain their demand-forward loading and effect memoization.
    internal static async Task<ReachesComputation> ComputeAsync(
        IQueryFactSource source,
        RuleSet rules,
        RuleSet shaped,
        string fromPattern,
        int maxDepth,
        FactPathFinder.TraversalMode mode,
        bool raw,
        HashSet<string> only,
        HashSet<string> exclude,
        bool includeIntrinsic
    )
    {
        var graphWatch = Stopwatch.StartNew();
        DemandForwardReachInputs? demandInputs = null;
        SqlReachability.ReachInputs inputs;
        FactGraphData graph;
        if (mode != FactPathFinder.TraversalMode.SyncCut && source is IDemandForwardPathFactSource demand)
        {
            demandInputs = await demand.LoadDemandForwardReachInputsAsync(
                fromPattern,
                shaped,
                maxDepth,
                mode,
                classifyEventSubscriptions: !raw
            );
            inputs = demandInputs.Inputs;
            graph = demandInputs.Demand.Graph;
        }
        else
        {
            inputs = await source.LoadEffectReachInputsAsync(fromPattern, SqlReachability.Direction.Forward, shaped);
            graph = inputs.Graph;
        }
        if (!raw && demandInputs?.Demand.EventSubscriptionsClassified != true)
        {
            graph = FactPathFinder.MarkEventSubscriptionHandoffs(graph, await source.EventSubscriptionSitesAsync());
        }
        graphWatch.Stop();

        var traversalWatch = Stopwatch.StartNew();
        var reachable = MonomorphCollapse.CollapseReachInfo(
            FactPathFinder.ReachesWithFanout(graph, fromPattern, maxDepth, mode: mode)
        );
        if (reachable.Count == 0)
        {
            traversalWatch.Stop();
            return new ReachesComputation(
                reachable,
                [],
                HiddenIntrinsic: 0,
                graph,
                inputs.EpData,
                graphWatch.Elapsed,
                traversalWatch.Elapsed
            );
        }

        var effects = demandInputs is null
            ? await source.DeriveEffectsAsync(inputs, graph, rules)
            : QueryEffectDerivation.ForReach(rules, inputs, graph);
        var selection = SelectEffectsForMethods(effects, reachable.Keys, only, exclude, includeIntrinsic);
        traversalWatch.Stop();
        return new ReachesComputation(
            reachable,
            selection.Effects,
            selection.HiddenIntrinsic,
            graph,
            inputs.EpData,
            graphWatch.Elapsed,
            traversalWatch.Elapsed
        );
    }
}
