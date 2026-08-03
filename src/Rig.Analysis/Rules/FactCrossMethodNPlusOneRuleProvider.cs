using Rig.Domain.Data;

namespace Rig.Analysis.Rules;

// Projects the merged `crossMethodNPlusOne` rule section to the domain FactCrossMethodNPlusOneRule the
// n_plus_1_cross_method PRESENCE correlation instance consumes. A SINGLE object, not a list: null when the
// section is absent or declares no read provider (a gate with nothing in it is off, not "everything"). Mirrors
// FactCacheCoherenceRuleProvider.
internal static class FactCrossMethodNPlusOneRuleProvider
{
    internal static FactCrossMethodNPlusOneRule? Project(AnalysisRulesDocument doc)
    {
        var rule = doc.CrossMethodNPlusOne;
        if (rule is null || rule.ReadProviders.Count == 0)
        {
            return null;
        }

        return new FactCrossMethodNPlusOneRule(
            ReadProviders: rule.ReadProviders,
            ReadOperations: rule.ReadOperations ?? [],
            MaxDepth: rule.MaxDepth ?? 6,
            MaxWitnessesPerAnchor: rule.MaxWitnessesPerAnchor ?? 0,
            ExcludeEnclosingNamespaceSuffix: rule.ExcludeEnclosingNamespaceSuffix
        );
    }
}
