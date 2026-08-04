using Rig.Domain.Data;

namespace Rig.Analysis.Rules;

// Projects the merged `crossMethodAmplification` rule section to the domain FactCrossMethodAmplificationRule
// the cross_method_amplification PRESENCE correlation instance consumes. A SINGLE object, not a list: null
// when the section is absent (detector off). PRESENCE of the section turns the detector on — an empty
// `witnesses` list is the all-IO mode (every effect except `excludeWitnessProviders`), not "off", which is
// why this projection no longer requires a non-empty provider list. Mirrors FactCacheCoherenceRuleProvider.
internal static class FactCrossMethodAmplificationRuleProvider
{
    // The all-IO mode's default exclusions: `alloc`/`throw` scale with code volume (the CLI's intrinsic
    // pair), `shared_state`/`config` are in-memory reads — none is an IO round trip, so none can be the
    // amplified cost a looped call site pays per element. Overridable per rules file.
    private static readonly string[] DefaultExcludedWitnessProviders = ["alloc", "throw", "shared_state", "config"];

    internal static FactCrossMethodAmplificationRule? Project(AnalysisRulesDocument doc)
    {
        var rule = doc.CrossMethodAmplification;
        if (rule is null)
        {
            return null;
        }

        return new FactCrossMethodAmplificationRule(
            Witnesses: (rule.Witnesses ?? [])
                .Select(w => new FactAmplificationRule(Providers: w.Providers ?? [], Operations: w.Operations ?? []))
                .ToList(),
            ExcludeWitnessProviders: rule.ExcludeWitnessProviders ?? DefaultExcludedWitnessProviders,
            MaxDepth: rule.MaxDepth ?? 6,
            MaxWitnessesPerAnchor: rule.MaxWitnessesPerAnchor ?? 0,
            ExcludeEnclosingNamespaceSuffix: rule.ExcludeEnclosingNamespaceSuffix
        );
    }
}
