using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Cli.Effects;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using static Rig.Cli.Caching.QueryCacheKeys;
using static Rig.Cli.Effects.EffectDerivation;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Services;

// The FINDINGS half of the file lens: tiers 1-3 for ONE source file.
//
// The effect badges (`db!`, `cache:5?`) say what a line touches. These say what is WRONG with it, and they are
// a different derivation with a different cost, which is why they are a separate service and a separate
// endpoint rather than more fields on /api/file-effects:
//
//   tier 1  HAZARD        — n_plus_1, race_window, sync_over_async, dual_write, … (HazardKinds.All), plus the
//                           graph-tier ones (cache_coherence / event_cycle / static_init_capture).
//   tier 2  AMPLIFICATION — `looped_effect`: the effect runs once per iteration. Scoped by the rules'
//                           amplification section, exactly as `rig derive` scopes it.
//   tier 3  CROSS-METHOD  — a read reachable at or beneath a call issued once per element. Anchor grain (one
//                           row per looped call site, nearest witness), the grain a human reviews.
//
// This is the same whole-store derivation `rig derive` and /api/hazards run, filtered to a FILE instead of to a
// tree — cheaper than /api/hazards, which must compute a call tree first to know which methods to keep. The
// two expensive inputs are both cached: the hazard-augmented effect set on disk (HazardEffectsSchema) and the
// graph + invocation table in the resident WarmStore. A cold call still pays them once.
//
// Line-anchored by construction: every row carries the FilePath and Line of the effect or call site that
// produced it, so the lens can put a mark on a line without re-resolving anything.
internal static class FileFindingsQueryService
{
    internal sealed record Findings(
        IReadOnlyList<DeriveCommand.HazardFinding> Hazards,
        IReadOnlyList<DeriveCommand.HazardFinding> Amplifications,
        IReadOnlyList<CrossMethodAmplificationDataset.AnchorFinding> Anchors,
        // Whether tier 3 was DERIVED at all. An empty Anchors list means two different things — this file has
        // no looped call site that reaches a read, or the rule set declares no `crossMethodAmplification`
        // section and nothing was ever looked for — and a reader who cannot tell them apart reads silence as
        // safety. The flag is the only place that difference survives.
        bool CrossMethodDerived
    )
    {
        internal static Findings Empty { get; } = new([], [], [], false);
    }

    private static readonly StringComparer FilePathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static async Task<Findings> ForFileAsync(string workingDirectory, string filePath, string? storeRef = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var rules = RuleSetLoader.Load(workingDirectory: workingDirectory, extraRules: [], loadedPaths: out var loadedPaths);
        var ws = new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: storeRef);
        await using var context = await OpenReadContextGatedAsync(ws);

        var rigDir = StoreLayout.ResolveReadStoreDir(ws);
        var storeKey = StoreKey(Path.Combine(rigDir, StoreLayout.DbFileName));
        var rulesHash = RulesFingerprint.ComputeFromPaths(loadedPaths);

        // The per-FILE result, cached over `.rig/cache.db` exactly as the two whole-store inputs below are
        // (LoadOrDerive*: QueryCache.Open → Get+decode → derive → TryCache Put). Both of those were already
        // warm and this call still cost ~2.1s, because the work on top of them is per-file and was recomputed
        // every request: the tier-3 anchor pairing walks the WHOLE-store effect set (it must — the witness is
        // by definition in another frame), and tiers 1-2 scan it to filter to this path. That is the artifact
        // this entry holds. Best-effort throughout: an unopenable cache misses and a failed write is dropped,
        // so the cache can only change latency, never the findings.
        var key = FileFindingsCacheKey(storeKey: storeKey, rulesHash: rulesHash, filePath: filePath);
        using var cache = QueryCache.Open(rigDirectory: rigDir, storeKey: storeKey);
        if (cache?.Get(key) is { } cachedBlob && Caching.FileFindingsCodec.Decode(cachedBlob) is { } cacheHit)
        {
            return cacheHit;
        }

        // The whole-store hazard-augmented effect set — the same cached artifact `derive` and /api/hazards
        // share, so asking for a second file costs a dictionary scan, not a re-derivation.
        var hazardEffects = await LoadOrDeriveHazardEffectsAsync(
            context: context,
            rigDirectory: rigDir,
            storeKey: storeKey,
            rulesHash: rulesHash,
            rules: rules,
            useCache: true
        );

        // Filter to the FILE first: both finding derivations below are per-effect, so narrowing the input is
        // both the cheapest filter available and the one that keeps the line anchors honest.
        var fileEffects = hazardEffects.Where(effect => FilePathComparer.Equals(effect.FilePath, filePath)).ToArray();

        var hazards = DeriveCommand
            .HazardFindings(fileEffects)
            .Concat(
                // Graph-tier hazards are NOT effect-attached, so they cannot be found by filtering effects —
                // they carry their own site and are filtered on it.
                (
                    await LoadOrDeriveGraphHazardFindingsAsync(
                        context: context,
                        rigDirectory: rigDir,
                        storeKey: storeKey,
                        rulesHash: rulesHash,
                        rules: rules,
                        useCache: true
                    )
                ).Where(finding => FilePathComparer.Equals(finding.FilePath, filePath))
            )
            .OrderBy(finding => finding.Line)
            .ThenBy(finding => finding.Type, StringComparer.Ordinal)
            .ToArray();

        var amplifications = DeriveCommand
            .AmplificationFindings(fileEffects, rules.Observations.AmplificationOrEmpty)
            .OrderBy(finding => finding.Line)
            .ToArray();

        // Tier 3 needs the WHOLE effect set, not the file's: the anchor is in this file but its WITNESS is by
        // definition in another frame, usually another file. Filtering the input would delete exactly the
        // evidence the tier exists to find, so the filter is applied to the ANCHOR after derivation.
        var anchors = Array.Empty<CrossMethodAmplificationDataset.AnchorFinding>();
        var crossMethodDerived = rules.CrossMethodAmplification is not null;
        if (rules.CrossMethodAmplification is { } crossMethodRule)
        {
            anchors = CrossMethodAmplificationDataset
                .AnchorFindings(
                    CrossMethodAmplificationDataset.Pairs(
                        invocations: await Caching.WarmStore.InvocationsAsync(context: context, storeDir: rigDir),
                        graph: await Caching.WarmStore.GraphAsync(context: context, rules: rules, storeDir: rigDir, rulesHash: rulesHash),
                        effects: hazardEffects,
                        observationRules: rules.Observations,
                        rule: crossMethodRule
                    )
                )
                .Where(anchor => FilePathComparer.Equals(anchor.FilePath, filePath))
                .OrderBy(anchor => anchor.Line)
                .ToArray();
        }

        var findings = new Findings(hazards, amplifications, anchors, crossMethodDerived);
        if (cache is not null)
        {
            TryCache(() => cache.Put(key, Caching.FileFindingsCodec.Encode(findings)));
        }

        return findings;
    }
}
