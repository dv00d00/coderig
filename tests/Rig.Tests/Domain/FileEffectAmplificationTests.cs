using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

// The AMPLIFICATION tier reaching the file lens: `looped_effect` (HazardKinds.Amplification) already rides on
// DerivedEffect.Observations, and the read model used to drop it — so a db write executed once per element of
// a collection rendered identically to one executed once. These tests pin BOTH halves of that: the mark
// appears where the loop actually is, and it is never inferred anywhere else.
//
// The limit is deliberate and is what most of these tests defend: `looped_effect` is LEXICAL, a fact about the
// effect's own body, so it is knowable exactly at depth 0. Propagating it up the reverse closure would answer
// a weaker question ("something looped exists somewhere below this call") that the tier-3 cross-method anchor
// answers properly, with a witness and a confidence.
public sealed class FileEffectAmplificationTests
{
    private const string File = "/repo/Caller.cs";
    private const string Caller = "M:Fixture.Caller.Run()";
    private const string Callee = "M:Fixture.Store.Write()";

    [Test]
    public void An_effect_inside_a_loop_marks_its_own_method_and_line()
    {
        var model = Build(Graph([]), [Method(Caller, 10)], [Looped(Caller, 12)]).Find(File).ShouldNotBeNull();

        var badge = model.Methods.Single(method => method.SymbolId == Caller).Effects.ShouldHaveSingleItem();
        badge.NearestDepth.ShouldBe(0);
        badge.Looped.ShouldBeTrue();
    }

    [Test]
    public void An_effect_outside_a_loop_is_not_marked()
    {
        var model = Build(Graph([]), [Method(Caller, 10)], [Effect(Caller, 12)]).Find(File).ShouldNotBeNull();

        model.Methods.Single(method => method.SymbolId == Caller).Effects.ShouldHaveSingleItem().Looped.ShouldBeFalse();
    }

    // The load-bearing negative: the callee loops, the caller does not. A mark on the caller would claim the
    // reader's line repeats when it runs once — and the loop is not even in the file being read.
    [Test]
    public void A_distant_looped_effect_never_marks_the_calling_method()
    {
        var graph = Graph([new CallEdge(Caller, Callee, EdgeKinds.Invocation, File, 12)]);

        var model = Build(graph, [Method(Caller, 10)], [Looped(Callee, 30, "/repo/Store.cs")]).Find(File).ShouldNotBeNull();

        var badge = model.Methods.Single(method => method.SymbolId == Caller).Effects.ShouldHaveSingleItem();
        badge.NearestDepth.ShouldBe(1);
        badge.Looped.ShouldBeFalse();
    }

    // Two effects of one family in one body, one looped and one not: the badge shows the nearest distance, and
    // repetition is OR-ed only across the rows AT that distance — so a body that does both reads as looped,
    // because at least one of the things on it repeats.
    [Test]
    public void Repetition_is_or_ed_across_effects_at_the_rendered_distance()
    {
        var model = Build(Graph([]), [Method(Caller, 10)], [Effect(Caller, 12), Looped(Caller, 14)]).Find(File).ShouldNotBeNull();

        model.Methods.Single(method => method.SymbolId == Caller).Effects.ShouldHaveSingleItem().Looped.ShouldBeTrue();
    }

    // Line grain is keyed on (enclosing method, line), not on the family: the unlooped line must stay
    // unmarked even though its sibling line in the same body is looped.
    [Test]
    public void Line_rows_carry_repetition_only_on_the_looped_line()
    {
        var graph = Graph([new CallEdge(Caller, Callee, EdgeKinds.Invocation, File, 14)]);

        var model = Build(graph, [Method(Caller, 10), Method(Callee, 40)], [Effect(Caller, 12), Looped(Caller, 14)])
            .Find(File)
            .ShouldNotBeNull();

        var looped = model.CallSites.Where(site => site.Line == 14).SelectMany(site => site.Effects).ToArray();
        looped.ShouldNotBeEmpty();
        looped.ShouldAllBe(effect => effect.Looped);
        model.CallSites.Where(site => site.Line == 12).SelectMany(site => site.Effects).ShouldAllBe(effect => !effect.Looped);
    }

    private static FileEffectReadModelIndex Build(
        FactGraphData graph,
        IEnumerable<SymbolFact> symbols,
        IEnumerable<DerivedEffect> effects
    ) => FileEffectReadModelIndex.Build(graph, symbols, effects, [new FileEffectSelector("cache", [new EffectPredicate("redis")])], [File]);

    private static DerivedEffect Effect(string owner, int line, string? file = null) =>
        new("redis", "write", "redis", owner, file ?? File, line);

    // The same effect carrying the observation the AMPLIFICATION tier is defined by. Type string comes from
    // the emitter's own constant so this fixture cannot drift from the catalog.
    private static DerivedEffect Looped(string owner, int line, string? file = null) =>
        Effect(owner, line, file) with
        {
            Observations =
            [
                new EffectObservationInfo(
                    FactObservationDeriver.LoopedEffectType,
                    "foreach",
                    "foreach (var id in ids)",
                    "high",
                    "compilation",
                    "effect_inside_loop"
                ),
            ],
        };

    private static SymbolFact Method(string id, int line) =>
        new(
            id,
            SymbolKinds.Method,
            id,
            "Fixture",
            "T:Fixture",
            "",
            "",
            $"void {id}()",
            File,
            line,
            line + 20,
            "Fixture",
            false,
            BodyHash: id
        );

    private static FactGraphData Graph(IReadOnlyList<CallEdge> edges)
    {
        var methods = edges
            .SelectMany(edge => new[] { edge.Caller, edge.Callee })
            .Concat([Caller])
            .Distinct(StringComparer.Ordinal)
            .Select(id => new MethodRef(id, id, "T:Fixture"))
            .ToArray();
        return new FactGraphData(edges, [], methods, [], null);
    }
}
