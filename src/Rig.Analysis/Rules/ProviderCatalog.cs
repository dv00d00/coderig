using Rig.Domain.Data;

namespace Rig.Analysis.Rules;

// The FAMILY lookup over the provider catalog (carried on RuleSet.ProviderFamilies). No loader — the map
// comes from the merged rule set; this is purely the lookup plus the two derived vocabularies the filter and
// the Rider read model need.
//
// The rule this type exists to respect is the one AmplificationCategories states: no effect name may appear
// in rig core C#. So there is no built-in family table here, and an undeclared provider is its OWN family
// rather than being folded into some default — identity is neutral, a literal would make one codebase's stack
// the silent default for every other.
public static class ProviderCatalog
{
    // The family a provider belongs to, or the provider's own name when the rule set declares none.
    public static string FamilyOf(RuleSet rules, string provider)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.ProviderFamilies.TryGetValue(provider, out var family) ? family : provider;
    }

    // The DECLARED families, ordinal-sorted. Only declared ones: an undeclared provider's identity family is
    // already reachable as the bare provider token, so listing it as a family too would double the vocabulary
    // without adding a single new selection.
    public static IReadOnlyList<string> DeclaredFamilies(RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules
            .ProviderFamilies.Values.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(family => family, StringComparer.Ordinal)
            .ToArray();
    }

    // family -> the providers declared in it, over exactly the families DeclaredFamilies lists — a family
    // whose providers have no effect rules included. This is the GROUPING a reader's vocabulary needs (the
    // web legend, the grain toggle), so it must cover every declared family: one named in `families` with no
    // entry here would render as a family whose providers are missing rather than empty. EffectfulFamilies
    // stays separate because it answers a different question — which families are worth a traversal.
    public static IReadOnlyList<(string Family, IReadOnlyList<string> Providers)> DeclaredFamilyProviders(RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules
            .ProviderFamilies.GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
                (
                    group.Key,
                    (IReadOnlyList<string>)group.Select(pair => pair.Key).OrderBy(provider => provider, StringComparer.Ordinal).ToArray()
                )
            )
            .ToArray();
    }

    // family -> the providers declared in it, restricted to providers that actually have EFFECT RULES. A
    // family whose providers never produce an effect would otherwise cost a whole reverse traversal per
    // generation to project nothing (the Rider read model builds one closure per family).
    public static IReadOnlyList<(string Family, IReadOnlyList<string> Providers)> EffectfulFamilies(RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var withRules = rules.Effects.Select(rule => rule.Provider).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return rules
            .ProviderFamilies.Where(pair => withRules.Contains(pair.Key))
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
                (
                    group.Key,
                    (IReadOnlyList<string>)group.Select(pair => pair.Key).OrderBy(provider => provider, StringComparer.Ordinal).ToArray()
                )
            )
            .ToArray();
    }
}
