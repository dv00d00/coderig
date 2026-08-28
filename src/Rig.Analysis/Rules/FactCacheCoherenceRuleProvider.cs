using Rig.Domain.Data;

namespace Rig.Analysis.Rules;

// Projects the merged `cacheCoherence` rule section to the domain FactCacheCoherenceRule the cache-coherence
// correlation INSTANCE consumes (FR-7). A SINGLE object, not a list.
//
// The GATE: null (detector OFF) unless the section names BOTH an `anchor` and a `companion` provider. Core
// carries no fallback pair on purpose — a built-in anchor would be one project's ORM shipped as the default
// for every codebase, matching nothing while the detector reports "no findings" as if the code were clean.
// An OFF detector is honest; a silently-mistargeted one is not.
internal static class FactCacheCoherenceRuleProvider
{
    internal static FactCacheCoherenceRule? Project(AnalysisRulesDocument doc)
    {
        var rule = doc.CacheCoherence;
        if (rule is null)
        {
            return null;
        }

        var anchor = Selector(rule.Anchor);
        var companion = Selector(rule.Companion);
        if (anchor is null || companion is null)
        {
            return null;
        }

        return new FactCacheCoherenceRule(
            Anchor: anchor,
            Companion: companion,
            CachedEntities: rule.CachedEntities ?? [],
            AnchorStripSuffix: rule.AnchorStripSuffix ?? [],
            CompanionStripSuffix: rule.CompanionStripSuffix ?? [],
            ExcludeEnclosingNamespaceSuffix: rule.ExcludeEnclosingNamespaceSuffix,
            DiscoveryRead: DiscoveryRead(rule.DiscoveryRead)
        );
    }

    private static FactEffectSelector? Selector(EffectSelectorDocument? selector) =>
        string.IsNullOrWhiteSpace(selector?.Provider)
            ? null
            : new FactEffectSelector(
                Provider: selector.Provider,
                Operation: string.IsNullOrWhiteSpace(selector.Operation) ? null : selector.Operation
            );

    // The discovery tier is independently optional: an absent (or provider/operation-less) `discoveryRead`
    // leaves the in-scope key set to the DECLARED `cachedEntities` alone, rather than defaulting to some
    // project's cache-read effect.
    private static FactCacheDiscoveryRead? DiscoveryRead(DiscoveryReadDocument? read) =>
        string.IsNullOrWhiteSpace(read?.Provider) || string.IsNullOrWhiteSpace(read.Operation)
            ? null
            : new FactCacheDiscoveryRead(Provider: read.Provider, Operation: read.Operation, StripSuffix: read.StripSuffix ?? []);
}
