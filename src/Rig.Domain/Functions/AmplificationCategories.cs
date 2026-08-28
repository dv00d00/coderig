using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// GENERIC grouping/ranking/exclusion for amplification findings. Companion to AmplificationScope: that one
// answers "is this effect in scope at all", this one answers "which display category does it fall in, how does
// it rank, and does it get its own section".
//
// The hard rule this type exists to enforce: **no effect name may appear in rig core C#**. Provider and
// operation tokens (`llblgen:read`, `actor:tell`, `lock:acquire`, `reflection:load`) are the vocabulary of a
// particular codebase's RULESET — Echo actors exist in exactly one repo — so a ranking table, a category
// grouping, or a default exclusion list written in C# would bake one project's domain into the tool. Core
// therefore implements only "rank / group / exclude by CONFIGURED category"; the categories themselves are
// authored in the rule cascade (`observations.amplificationCategories`) and projected to
// FactAmplificationCategoryRule.
//
// Matching mirrors AmplificationScope/FactNPlusOneRule: within a rule an EMPTY list means "any" for that
// dimension, and rules are tried IN ORDER so the first match wins — order is the authoring lever for
// specificity (put `{providers:[x], operations:[y]}` before `{providers:[x]}`). Ordinal comparison throughout:
// these are rule-authored identifiers, not prose.
public static class AmplificationCategories
{
    // The neutral category: what every finding gets when the ruleset declares no categories at all. One
    // implicit group, no weighting, no separate section, nothing excluded — so a rig with no project ruleset
    // still ranks correctly by degree and site, just without opinion about which effects cost more.
    public static readonly FactAmplificationCategoryRule Neutral = new(
        Name: "",
        Weight: 0,
        Separate: false,
        Label: "",
        Excluded: false,
        Providers: [],
        Operations: []
    );

    // The category a provider:operation falls in, or Neutral when nothing matches (an effect the ruleset did
    // not categorise is NEVER dropped — it simply carries no opinion and sorts after weighted categories).
    public static FactAmplificationCategoryRule For(
        IReadOnlyList<FactAmplificationCategoryRule> categories,
        string? provider,
        string? operation
    )
    {
        foreach (var category in categories)
        {
            var providerOk = category.Providers.Count == 0 || category.Providers.Contains(provider ?? "", StringComparer.Ordinal);
            var operationOk = category.Operations.Count == 0 || category.Operations.Contains(operation ?? "", StringComparer.Ordinal);
            if (providerOk && operationOk)
            {
                return category;
            }
        }

        return Neutral;
    }

    // Sort key for the effect-kind tiebreak. Uncategorised effects sort AFTER every weighted category (rather
    // than before), so introducing a category for one provider never silently demotes the others.
    public static int Rank(IReadOnlyList<FactAmplificationCategoryRule> categories, string? provider, string? operation)
    {
        var match = For(categories, provider, operation);
        return ReferenceEquals(match, Neutral) ? int.MaxValue : match.Weight;
    }
}
