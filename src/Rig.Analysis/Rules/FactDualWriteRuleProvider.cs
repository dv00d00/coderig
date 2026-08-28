using Rig.Domain.Data;

namespace Rig.Analysis.Rules;

// Projects the merged `dualWrite` rule section to the domain FactDualWriteRule the FR-8 dual_write matcher
// consumes. A SINGLE object, not a list: null when the section is absent or its map is empty, which leaves the
// detector OFF.
//
// Core carries no default system-class map. Every key in such a map is a `provider:operation` from some
// codebase's ruleset, so a built-in one would classify a stranger's stack — the shipped builtin-rules.json
// keeps the entries for the providers IT declares (efcore / rabbitmq / redis / smtp / …) and a project overlay
// adds its own (the map merges per key across the cascade, so adding never means restating).
internal static class FactDualWriteRuleProvider
{
    internal static FactDualWriteRule? Project(AnalysisRulesDocument doc)
    {
        var map = doc.DualWrite?.SystemClassMap;
        if (map is null || map.Count == 0)
        {
            return null;
        }

        // Ordinal keys: `provider:operation` tokens are matched ordinally against derived effects.
        return new FactDualWriteRule(new Dictionary<string, string>(map, StringComparer.Ordinal));
    }
}
