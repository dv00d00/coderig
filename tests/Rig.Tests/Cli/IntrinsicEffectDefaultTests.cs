using Rig.Cli.Caching;
using Rig.Cli.Effects;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Cli;

// The language-INTRINSIC effect axis: `alloc` and `throw` are withheld by default and restored by
// --intrinsic. They scale with code VOLUME (every `new`, every `throw`) rather than with what the code
// talks to, and on the MedDBase store they are 243,391 + 79,508 effects against ~30,619 of everything
// else — 91.3% of the corpus. Hiding them by default is the core usability lever; the invariant that makes
// it SAFE is that the hiding is presentational (detectors always see the unfiltered set) and never silent.
public sealed class IntrinsicEffectDefaultTests
{
    private static DerivedEffect Effect(string provider, string operation) =>
        new(provider, operation, ResourceType: $"{provider}.Res", EnclosingSymbolId: "M:App.Svc.Go", FilePath: "Svc.cs", Line: 1);

    // A representative mix: two intrinsic providers, three ordinary ones.
    private static IReadOnlyList<DerivedEffect> Corpus() =>
        [
            Effect("alloc", "object"),
            Effect("alloc", "boxing"),
            Effect("throw", "raise"),
            Effect("llblgen", "write"),
            Effect("audit", "write"),
            Effect("shared_state", "mutate"),
        ];

    private static HashSet<string> Set(params string[] tokens) => new(tokens, StringComparer.OrdinalIgnoreCase);

    private static List<string> Providers(EffectDerivation.EffectSelection s) =>
        s.Effects.Select(e => e.Provider).Distinct().Order().ToList();

    [Test]
    public void By_default_intrinsic_providers_are_withheld_and_the_count_is_disclosed()
    {
        var s = EffectDerivation.SelectEffects(Corpus(), only: Set(), exclude: Set(), includeIntrinsic: false);

        Providers(s).ShouldBe(["audit", "llblgen", "shared_state"]);
        s.HiddenIntrinsic.ShouldBe(3); // 2 alloc + 1 throw

        // Never silent: the disclosure names both the providers AND the flag that undoes the hiding.
        var note = EffectDerivation.IntrinsicNote(s.HiddenIntrinsic);
        note.ShouldContain("alloc");
        note.ShouldContain("throw");
        note.ShouldContain("--intrinsic");
    }

    [Test]
    public void The_intrinsic_flag_restores_them_and_reports_nothing_hidden()
    {
        var s = EffectDerivation.SelectEffects(Corpus(), only: Set(), exclude: Set(), includeIntrinsic: true);

        Providers(s).ShouldBe(["alloc", "audit", "llblgen", "shared_state", "throw"]);
        s.HiddenIntrinsic.ShouldBe(0);
        EffectDerivation.IntrinsicNote(0).ShouldBe(""); // nothing withheld -> no note at all
    }

    [Test]
    public void Naming_an_intrinsic_provider_in_only_implies_the_flag()
    {
        // `rig reaches X --only alloc` must do the obvious thing rather than return an empty list — an
        // explicit --only naming an intrinsic provider IS a request for it.
        var bare = EffectDerivation.SelectEffects(Corpus(), only: Set("alloc"), exclude: Set(), includeIntrinsic: false);
        Providers(bare).ShouldBe(["alloc"]);
        bare.HiddenIntrinsic.ShouldBe(0);

        // Also via a precise provider:operation token, and for `throw`.
        Providers(EffectDerivation.SelectEffects(Corpus(), only: Set("alloc:boxing"), exclude: Set(), includeIntrinsic: false))
            .ShouldBe(["alloc"]);
        Providers(EffectDerivation.SelectEffects(Corpus(), only: Set("throw"), exclude: Set(), includeIntrinsic: false))
            .ShouldBe(["throw"]);

        // Mixing an intrinsic token with an ordinary one keeps BOTH — naming one lifts the default hiding,
        // it does not narrow the request to only the intrinsic half.
        Providers(EffectDerivation.SelectEffects(Corpus(), only: Set("alloc", "audit"), exclude: Set(), includeIntrinsic: false))
            .ShouldBe(["alloc", "audit"]);
    }

    [Test]
    public void An_only_filter_naming_no_intrinsic_provider_still_hides_them()
    {
        var s = EffectDerivation.SelectEffects(Corpus(), only: Set("audit"), exclude: Set(), includeIntrinsic: false);

        Providers(s).ShouldBe(["audit"]);
        // The alloc/throw effects were dropped by the EXPLICIT --only, not withheld by the intrinsic default,
        // so they must NOT be double-reported as a suppression the user can undo with --intrinsic.
        s.HiddenIntrinsic.ShouldBe(0);
    }

    [Test]
    public void Explicitly_excluding_an_intrinsic_provider_is_not_reported_as_a_hidden_suppression()
    {
        // --exclude throw is the user's own decision; only the DEFAULT hiding is disclosed. Here `throw` is
        // excluded explicitly and the two allocs are withheld by default -> HiddenIntrinsic counts 2, not 3.
        var s = EffectDerivation.SelectEffects(Corpus(), only: Set(), exclude: Set("throw"), includeIntrinsic: false);

        Providers(s).ShouldBe(["audit", "llblgen", "shared_state"]);
        s.HiddenIntrinsic.ShouldBe(2);
    }

    [Test]
    public void Exclude_still_wins_over_intrinsic_inclusion()
    {
        // --intrinsic lifts the DEFAULT hiding; it does not override an explicit --exclude.
        var s = EffectDerivation.SelectEffects(Corpus(), only: Set(), exclude: Set("alloc"), includeIntrinsic: true);

        Providers(s).ShouldBe(["audit", "llblgen", "shared_state", "throw"]);
        s.HiddenIntrinsic.ShouldBe(0);
    }

    [Test]
    public void Shared_state_is_not_intrinsic_because_the_hazard_detectors_depend_on_it()
    {
        // Guard against the tempting "intrinsic == derived from syntax rather than from a rule" generalisation:
        // shared_state would qualify under it, and silently hiding it would blind the concurrency detectors.
        // The set is deliberately CLOSED at two.
        EffectDerivation.IntrinsicProviders.ShouldBe(["alloc", "throw"], ignoreOrder: true);
        EffectDerivation.IsIntrinsic(Effect("shared_state", "mutate")).ShouldBeFalse();
        EffectDerivation.IsIntrinsic(Effect("lock", "acquire")).ShouldBeFalse();
    }

    [Test]
    public void The_cache_signature_distinguishes_the_intrinsic_flag()
    {
        // The render sidecar's seam summaries are a function of the FILTERED effects, so a default run and an
        // --intrinsic run must not share a key — otherwise one serves the other's payload.
        var off = QueryCacheKeys.EffectFilterSignature([], [], intrinsic: false);
        var on = QueryCacheKeys.EffectFilterSignature([], [], intrinsic: true);

        off.ShouldNotBe(on);
    }
}
