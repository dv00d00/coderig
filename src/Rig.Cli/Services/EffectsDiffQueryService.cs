using System.Diagnostics;
using Rig.Analysis.Rules;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Storage;
using static Rig.Cli.Caching.QueryCacheKeys;
using static Rig.Cli.Effects.EffectDerivation;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Services;

// The shared effects-diff computation behind both `rig effects-diff` and /api/effects-diff. Pattern
// resolution deliberately goes through FactPathFinder: exact full DocIDs / rendered FQNs win over
// substring matches, and open-generic identities retain all of their concrete monomorphized seeds.
public static class EffectsDiffQueryService
{
    public enum TargetStatus
    {
        Matched,
        NoMatch,
        Ambiguous,
    }

    public sealed record TargetResolution(string Pattern, TargetStatus Status, IReadOnlyList<string> Matches, string? ResolvedId);

    public sealed record ResourceSetItem(string ResourceKey, IReadOnlyList<string> Categories);

    private sealed record ResolvedTarget(TargetResolution Target, IReadOnlyList<string> SeedIds);

    public sealed record EffectsDiffQueryResult(
        string Label,
        TargetResolution A,
        TargetResolution B,
        IReadOnlyList<ResourceSetItem> Common,
        IReadOnlyList<ResourceSetItem> AOnly,
        IReadOnlyList<ResourceSetItem> BOnly,
        IReadOnlyList<EffectSetDiffFinding> Findings,
        TimeSpan GraphLoadElapsed,
        TimeSpan TraversalElapsed
    )
    {
        public bool Matched => A.Status == TargetStatus.Matched && B.Status == TargetStatus.Matched;
    }

    public static async Task<EffectsDiffQueryResult> BuildAsync(
        string workingDirectory,
        string aPattern,
        string bPattern,
        IReadOnlyList<string>? only = null,
        string? label = null,
        string? storeRef = null
    )
    {
        var rules = RuleSetLoader.Load(workingDirectory: workingDirectory, extraRules: [], loadedPaths: out var loadedRulePaths);
        var (context, storeDirectory) = await OpenReadContextGatedAsync(
            new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: storeRef),
            withStoreDir: true
        );
        await using var contextScope = context;
        var storeKey = StoreKey(Path.Combine(storeDirectory, StoreLayout.DbFileName));
        var rulesHash = RulesFingerprint.ComputeFromPaths(loadedRulePaths);
        var graphWatch = Stopwatch.StartNew();
        var graph = await WarmStore.GraphAsync(context: context, rules: rules, storeDir: storeDirectory, rulesHash: rulesHash);
        graphWatch.Stop();

        var a = Resolve(graph, aPattern);
        var b = Resolve(graph, bPattern);
        if (a.Target.Status != TargetStatus.Matched || b.Target.Status != TargetStatus.Matched)
        {
            return new EffectsDiffQueryResult(
                Label: label ?? "",
                A: a.Target,
                B: b.Target,
                Common: [],
                AOnly: [],
                BOnly: [],
                Findings: [],
                GraphLoadElapsed: graphWatch.Elapsed,
                TraversalElapsed: TimeSpan.Zero
            );
        }

        var traversalWatch = Stopwatch.StartNew();
        var effects = await LoadOrDeriveHazardEffectsAsync(
            context: context,
            rigDirectory: storeDirectory,
            storeKey: storeKey,
            rulesHash: rulesHash,
            rules: rules,
            useCache: true
        );
        var result = ComputeResolved(graph, effects, a.Target, b.Target, a.SeedIds, b.SeedIds, only, label ?? "", graphWatch.Elapsed);
        traversalWatch.Stop();
        return result with { TraversalElapsed = traversalWatch.Elapsed };
    }

    // Pure in-memory entry used by focused tests and future resident callers. It exercises the same resolver,
    // normalization, filter, and FactEffectSetDiffDeriver path as the store-backed CLI/web entry point.
    internal static EffectsDiffQueryResult Compute(
        FactGraphData graph,
        IReadOnlyList<DerivedEffect> effects,
        string aPattern,
        string bPattern,
        IReadOnlyList<string>? only = null,
        string label = ""
    )
    {
        var a = Resolve(graph, aPattern);
        var b = Resolve(graph, bPattern);
        if (a.Target.Status != TargetStatus.Matched || b.Target.Status != TargetStatus.Matched)
        {
            return new EffectsDiffQueryResult(
                Label: label,
                A: a.Target,
                B: b.Target,
                Common: [],
                AOnly: [],
                BOnly: [],
                Findings: [],
                GraphLoadElapsed: TimeSpan.Zero,
                TraversalElapsed: TimeSpan.Zero
            );
        }

        return ComputeResolved(graph, effects, a.Target, b.Target, a.SeedIds, b.SeedIds, only, label, TimeSpan.Zero);
    }

    private static EffectsDiffQueryResult ComputeResolved(
        FactGraphData graph,
        IReadOnlyList<DerivedEffect> effects,
        TargetResolution a,
        TargetResolution b,
        IReadOnlyList<string> aSeeds,
        IReadOnlyList<string> bSeeds,
        IReadOnlyList<string>? only,
        string label,
        TimeSpan graphLoadElapsed
    )
    {
        // ReachesWithFanout exposes the exact-match-wins seed set as the depth-zero nodes. Synthetic roots
        // let the existing pure diff deriver compare the UNION when a conceptual target has overloads or
        // monomorphized executions, without teaching the deriver about user-facing patterns.
        var (comparisonGraph, aRoot, bRoot, emptyRoot) = AddComparisonRoots(graph, aSeeds, bSeeds);
        var filter = ParseFilter(only);
        var normalize = new NormalizeSpec(SimpleTypeName: true, StripSuffix: ["EntityCollection", "Collection", "DAO"]);
        var allFindings = FactEffectSetDiffDeriver.Derive(
            graph: comparisonGraph,
            effects: effects,
            spec: new EffectSetDiffSpec(
                Pairs:
                [
                    new EffectSetDiffPair(Label: "pair", AId: aRoot, BId: bRoot),
                    new EffectSetDiffPair(Label: "a-inventory", AId: aRoot, BId: emptyRoot),
                    new EffectSetDiffPair(Label: "b-inventory", AId: bRoot, BId: emptyRoot),
                ],
                Filter: filter,
                Normalize: normalize,
                // The synthetic root consumes one hop; preserve the command's historical depth-20 reach.
                MaxDepth: 21
            )
        );

        var aResources = Inventory(allFindings, "a-inventory");
        var bResources = Inventory(allFindings, "b-inventory");
        var common = aResources
            .Where(kv => bResources.ContainsKey(kv.Key))
            .Select(kv => new ResourceSetItem(
                kv.Key,
                kv.Value.Concat(bResources[kv.Key]).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList()
            ))
            .OrderBy(x => x.ResourceKey, StringComparer.Ordinal)
            .ToList();

        var findings = allFindings
            .Where(f => f.Label == "pair")
            .Select(f =>
                f with
                {
                    Label = label,
                    PresentEpId = f.Direction == EffectDiffSide.AOnly ? a.ResolvedId! : b.ResolvedId!,
                    AbsentEpId = f.Direction == EffectDiffSide.AOnly ? b.ResolvedId! : a.ResolvedId!,
                }
            )
            .ToList();

        return new EffectsDiffQueryResult(
            Label: label,
            A: a,
            B: b,
            Common: common,
            AOnly: Resources(findings, EffectDiffSide.AOnly),
            BOnly: Resources(findings, EffectDiffSide.BOnly),
            Findings: findings,
            GraphLoadElapsed: graphLoadElapsed,
            TraversalElapsed: TimeSpan.Zero
        );
    }

    private static ResolvedTarget Resolve(FactGraphData graph, string pattern)
    {
        var nodes = graph.Methods.Select(m => m.SymbolId).Distinct(StringComparer.Ordinal).ToList();
        var conceptualMatches = FactPathFinder.DistinctMatchTargets(nodes, pattern);
        if (conceptualMatches.Count == 0)
        {
            return new ResolvedTarget(
                new TargetResolution(pattern, TargetStatus.NoMatch, conceptualMatches, ResolvedId: null),
                SeedIds: []
            );
        }

        // Depth-zero nodes are precisely FactPathFinder's exact-first selected seeds. Reduce only for target
        // IDENTITY: open base + N monomorphs is one source member; two overload base DocIDs are two targets.
        // In the latter case fail closed and return full signatures that can be pasted back verbatim.
        var seeds = SeedIds(graph, pattern);
        var seedSet = seeds.ToHashSet(StringComparer.Ordinal);
        var canonicalTargets = seeds
            .Where(seed => !IsContainedLambdaOfMatched(seed, seedSet))
            .Select(MonomorphizedNodeId.BaseOf)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (canonicalTargets.Count != 1)
        {
            return new ResolvedTarget(
                new TargetResolution(pattern, TargetStatus.Ambiguous, canonicalTargets, ResolvedId: null),
                SeedIds: []
            );
        }

        return new ResolvedTarget(new TargetResolution(pattern, TargetStatus.Matched, conceptualMatches, canonicalTargets[0]), seeds);
    }

    private static bool IsContainedLambdaOfMatched(string nodeId, HashSet<string> matched)
    {
        var marker = nodeId.IndexOf("~λ", StringComparison.Ordinal);
        return marker > 0 && matched.Contains(nodeId[..marker]);
    }

    private static IReadOnlyList<string> SeedIds(FactGraphData graph, string pattern) =>
        FactPathFinder
            .ReachesWithFanout(graph, pattern, maxDepth: 0)
            .Where(kv => kv.Value.Depth == 0)
            .Select(kv => kv.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

    private static (FactGraphData Graph, string ARoot, string BRoot, string EmptyRoot) AddComparisonRoots(
        FactGraphData graph,
        IReadOnlyList<string> aSeeds,
        IReadOnlyList<string> bSeeds
    )
    {
        var existing = graph.Methods.Select(m => m.SymbolId).ToHashSet(StringComparer.Ordinal);
        var aRoot = Unique("M:__RigEffectsDiff.A", existing);
        var bRoot = Unique("M:__RigEffectsDiff.B", existing);
        var emptyRoot = Unique("M:__RigEffectsDiff.Empty", existing);
        var roots = new[] { aRoot, bRoot, emptyRoot };
        var methods = graph.Methods.Concat(roots.Select(id => new MethodRef(id, id, null))).ToList();
        var edges = graph
            .CallEdges.Concat(aSeeds.Select(seed => new CallEdge(aRoot, seed, "invocation", "", 0)))
            .Concat(bSeeds.Select(seed => new CallEdge(bRoot, seed, "invocation", "", 0)))
            .ToList();
        return (graph with { CallEdges = edges, Methods = methods }, aRoot, bRoot, emptyRoot);
    }

    private static string Unique(string candidate, HashSet<string> existing)
    {
        while (!existing.Add(candidate))
        {
            candidate += "_";
        }

        return candidate;
    }

    private static Dictionary<string, IReadOnlyList<string>> Inventory(IReadOnlyList<EffectSetDiffFinding> findings, string label) =>
        findings
            .Where(f => f.Label == label && f.Direction == EffectDiffSide.AOnly)
            .ToDictionary(f => f.ResourceKey, f => f.Categories, StringComparer.Ordinal);

    private static IReadOnlyList<ResourceSetItem> Resources(IReadOnlyList<EffectSetDiffFinding> findings, EffectDiffSide side) =>
        findings
            .Where(f => f.Direction == side)
            .Select(f => new ResourceSetItem(f.ResourceKey, f.Categories))
            .OrderBy(x => x.ResourceKey, StringComparer.Ordinal)
            .ToList();

    // Empty = all effects. A bare provider matches every operation; provider:operation pins both.
    private static IReadOnlyList<EffectPredicate> ParseFilter(IReadOnlyList<string>? tokens)
    {
        if (tokens is null || tokens.Count == 0)
        {
            return [];
        }

        return tokens
            .Select(t =>
            {
                var colon = t.IndexOf(':');
                return colon < 0
                    ? new EffectPredicate(Provider: t, Operation: null)
                    : new EffectPredicate(Provider: t[..colon], Operation: t[(colon + 1)..]);
            })
            .ToList();
    }
}
