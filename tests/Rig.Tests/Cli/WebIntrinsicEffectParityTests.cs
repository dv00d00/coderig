using Rig.Cli.Effects;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Cli;

// The three browsable forward-query surfaces share the same method-scoped selection contract. These tests
// pin the web default/opt-in parity without standing up ASP.NET: the endpoints delegate their effect view to
// this exact selector after their tree/reach/path method sets have been computed.
public sealed class WebIntrinsicEffectParityTests
{
    private static readonly HashSet<string> Empty = new(StringComparer.OrdinalIgnoreCase);

    private static DerivedEffect Effect(string provider, string method) =>
        new(provider, "op", $"{provider}.Resource", method, "Fixture.cs", 1);

    private static IReadOnlyList<DerivedEffect> Effects() =>
        [
            Effect("db", "M:App.Entry"),
            Effect("alloc", "M:App.Entry"),
            Effect("throw", "M:App.Work"),
            Effect("http", "M:App.Work"),
            Effect("alloc", "M:App.Unrelated"),
        ];

    [Test]
    [Arguments("tree")]
    [Arguments("reaches")]
    [Arguments("path")]
    public void Forward_web_surfaces_hide_and_restore_intrinsic_effects(string surface)
    {
        // Each surface supplies its own computed method scope; selection semantics are intentionally shared.
        var scope = surface == "path" ? new[] { "M:App.Entry", "M:App.Work" } : new[] { "M:App.Entry", "M:App.Work" };

        var hidden = EffectDerivation.SelectEffectsForMethods(Effects(), scope, Empty, Empty, includeIntrinsic: false);
        hidden.Effects.Select(e => e.Provider).ShouldBe(["db", "http"]);
        hidden.HiddenIntrinsic.ShouldBe(2);

        var restored = EffectDerivation.SelectEffectsForMethods(Effects(), scope, Empty, Empty, includeIntrinsic: true);
        restored.Effects.Select(e => e.Provider).ShouldBe(["db", "alloc", "throw", "http"]);
        restored.HiddenIntrinsic.ShouldBe(0);
    }

    [Test]
    public void Shared_selection_scopes_before_counting_hidden_intrinsics()
    {
        var selection = EffectDerivation.SelectEffectsForMethods(Effects(), new[] { "M:App.Entry" }, Empty, Empty, includeIntrinsic: false);

        selection.Effects.Select(e => e.Provider).ShouldBe(["db"]);
        selection.HiddenIntrinsic.ShouldBe(1); // unrelated alloc + Work's throw are outside this answer
    }

    [Test]
    public void Explicit_intrinsic_only_filter_implies_intrinsic_view()
    {
        var only = new HashSet<string>(["alloc"], StringComparer.OrdinalIgnoreCase);
        var selection = EffectDerivation.SelectEffectsForMethods(Effects(), new[] { "M:App.Entry" }, only, Empty, includeIntrinsic: false);

        selection.Effects.Select(e => e.Provider).ShouldBe(["alloc"]);
        selection.HiddenIntrinsic.ShouldBe(0);
    }
}
