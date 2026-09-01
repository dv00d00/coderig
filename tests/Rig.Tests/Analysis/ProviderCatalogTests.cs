using Rig.Analysis.Rules;
using Rig.Cli.Effects;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Analysis;

// The `providers` rule section: provider -> family, the first DECLARATION of the noun ten rule sections
// already select on by bare string. What these tests pin, in order of what breaks if it regresses:
//
//  1. absent section = identity, never a built-in literal (core purity: no effect name in rig core C#);
//  2. per-key merge, so an overlay ADDS providers without restating the builtin's;
//  3. the CASCADE TRAP — RuleSetLoader.Merge enumerates sections explicitly, so a section it forgets is
//     silently dropped the moment any overlay declares its parent key. Four features have hit this;
//  4. family as the third --only/--exclude token tier, expanded into providers at the option boundary;
//  5. the Rider selector set derived from the families rather than hardcoded in core C#.
public sealed class ProviderCatalogTests
{
    private static RuleSet Rules(params (string Provider, string Family)[] families) =>
        new()
        {
            ProviderFamilies = families.ToDictionary(pair => pair.Provider, pair => pair.Family, StringComparer.OrdinalIgnoreCase),
            Effects = families
                .Select(pair => new FactEffectRule(
                    Provider: pair.Provider,
                    Operation: "read",
                    Methods: ["Read"],
                    DeclaringTypes: [],
                    ReceiverTypes: []
                ))
                .ToArray(),
        };

    [Test]
    public void An_undeclared_provider_is_its_own_family()
    {
        var rules = Rules(("llblgen", "db"));

        ProviderCatalog.FamilyOf(rules, "llblgen").ShouldBe("db");
        ProviderCatalog.FamilyOf(rules, "twilio").ShouldBe("twilio");
        ProviderCatalog.FamilyOf(new RuleSet(), "llblgen").ShouldBe("llblgen");
        ProviderCatalog.DeclaredFamilies(new RuleSet()).ShouldBeEmpty();
    }

    [Test]
    public void Families_group_their_providers_and_only_count_providers_that_have_effect_rules()
    {
        var rules = Rules(("llblgen", "db"), ("db_command", "db"), ("redis", "cache")) with
        {
            // Declared but ruleless: it would cost a whole reverse traversal per generation to project nothing.
            ProviderFamilies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["llblgen"] = "db",
                ["db_command"] = "db",
                ["redis"] = "cache",
                ["ghost"] = "spooky",
            },
        };

        ProviderCatalog.DeclaredFamilies(rules).ShouldBe(["cache", "db", "spooky"]);
        ProviderCatalog
            .EffectfulFamilies(rules)
            .Select(family => (family.Family, string.Join(",", family.Providers)))
            .ShouldBe([("cache", "redis"), ("db", "db_command,llblgen")]);
    }

    [Test]
    public void The_rider_selector_set_comes_from_the_families_not_from_core_csharp()
    {
        var selectors = RiderFileEffectResponder.SelectorsFor(Rules(("llblgen", "db"), ("db_command", "db"), ("io", "io")));

        selectors.Select(selector => selector.Family).ShouldBe(["db", "io"]);
        selectors[0].Predicates.Select(predicate => predicate.Provider).ShouldBe(["db_command", "llblgen"]);
        // No families declared = no selectors. The responder must not invent a family for a vocabulary it
        // cannot know; the empty answer is disclosed rather than guessed.
        RiderFileEffectResponder.SelectorsFor(new RuleSet()).ShouldBeEmpty();
    }

    [Test]
    public void A_family_token_filters_as_the_union_of_its_providers()
    {
        var rules = Rules(("llblgen", "db"), ("db_command", "db"), ("redis", "cache"));
        var only = new HashSet<string>(["db"], StringComparer.OrdinalIgnoreCase);
        var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var error = new StringWriter();

        EffectDerivation.PrepareFilterTokens(only, exclude, rules, error);

        only.ShouldBe(new HashSet<string>(["db", "llblgen", "db_command"], StringComparer.OrdinalIgnoreCase), ignoreOrder: true);
        // A family is a KNOWN token, so naming one must not produce the unknown-token warning.
        error.ToString().ShouldBeEmpty();

        var unknown = new HashSet<string>(["nonsense"], StringComparer.OrdinalIgnoreCase);
        EffectDerivation.PrepareFilterTokens(unknown, exclude, rules, error);
        error.ToString().ShouldContain("nonsense");
    }

    [Test]
    public void An_effect_is_kept_or_dropped_by_a_family_token_through_the_ordinary_matcher()
    {
        var rules = Rules(("llblgen", "db"), ("redis", "cache"));
        var effects = new[]
        {
            new DerivedEffect("llblgen", "read", "db", "M:A", "a.cs", 1),
            new DerivedEffect("redis", "read", "cache", "M:B", "b.cs", 2),
        };
        var only = new HashSet<string>(["db"], StringComparer.OrdinalIgnoreCase);
        var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EffectDerivation.PrepareFilterTokens(only, exclude, rules, new StringWriter());

        EffectDerivation
            .SelectEffects(effects, only, exclude, includeIntrinsic: false)
            .Effects.Select(effect => effect.Provider)
            .ShouldBe(["llblgen"]);
    }
}
