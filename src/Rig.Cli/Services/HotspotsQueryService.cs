using Rig.Analysis.Rules;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using static Rig.Cli.Caching.QueryCacheKeys;
using static Rig.Cli.Effects.EffectDerivation;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Services;

// Whole-store hotspot artifact. It intentionally retains generated and lambda rows; those are presentation
// filters, so changing --no-lambdas or the generated-code default never fragments this expensive cache.
public static class HotspotsQueryService
{
    public sealed record HotspotArtifact(IReadOnlyList<FactHotspotReport.Row> Rows, int HiddenIntrinsic);

    public static async Task<HotspotArtifact> BuildAsync(
        string workingDirectory,
        string? storeRef = null,
        bool intrinsic = false,
        IReadOnlyList<string>? extraRules = null
    )
    {
        var rules = RuleSetLoader.Load(
            workingDirectory: workingDirectory,
            extraRules: extraRules ?? [],
            loadedPaths: out var loadedRulePaths
        );
        var workspace = new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: storeRef);
        var (context, rigDirectory) = await OpenReadContextGatedAsync(workspace, withStoreDir: true);
        await using var contextScope = context;

        var storeKey = StoreKey(Path.Combine(rigDirectory, StoreLayout.DbFileName));
        var rulesHash = RulesFingerprint.ComputeFromPaths(loadedRulePaths);
        var key = HotspotsCacheKey(storeKey, rulesHash, intrinsic);
        using var cache = QueryCache.Open(rigDirectory, storeKey);
        if (cache?.Get(key) is { } blob && HotspotsCodec.Decode(blob) is { } hit)
        {
            return hit;
        }

        var graph = await Caching.WarmStore.GraphAsync(context: context, rules: rules, storeDir: rigDirectory, rulesHash: rulesHash);
        var methodMeta = await Reads.LoadHotspotMethodsAsync(context);
        var endLines = await Reads.LoadHotspotEndLinesAsync(context);

        // Detectors see the complete effect set. Intrinsic hiding is applied only to the report's effect
        // metrics, after graph-tier hazard derivation has consumed the unfiltered set.
        var allEffects = await LoadOrDeriveHazardEffectsAsync(
            context: context,
            rigDirectory: rigDirectory,
            storeKey: storeKey,
            rulesHash: rulesHash,
            rules: rules,
            useCache: true
        );
        var graphHazards = await LoadOrDeriveGraphHazardFindingsAsync(
            context: context,
            rigDirectory: rigDirectory,
            storeKey: storeKey,
            rulesHash: rulesHash,
            rules: rules,
            useCache: true,
            shapedGraph: graph,
            unfilteredEffects: allEffects
        );
        var selected = SelectEffects(
            allEffects,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            includeIntrinsic: intrinsic
        );

        var methods = methodMeta
            .Select(m => new FactHotspotReport.Method(
                Id: m.SymbolId,
                Name: m.Name,
                File: m.FilePath,
                Line: m.Line,
                EndLine: endLines.GetValueOrDefault(m.SymbolId, m.Line),
                IsGenerated: m.IsGenerated,
                IsLambda: m.SymbolId.Contains("~λ", StringComparison.Ordinal)
            ))
            .ToList();
        var findingSites = DeriveCommand
            .HazardFindings(selected.Effects)
            .Concat(graphHazards)
            .Select(h => new FactHotspotReport.FindingSite(h.Enclosing, h.Type, h.FilePath, h.Line))
            .ToList();
        var artifact = new HotspotArtifact(
            Rows: FactHotspotReport.Build(graph, methods, selected.Effects, findingSites, rules.Observations.AmplificationOrEmpty),
            HiddenIntrinsic: selected.HiddenIntrinsic
        );
        TryCache(() => cache?.Put(key, HotspotsCodec.Encode(artifact)));
        return artifact;
    }
}
