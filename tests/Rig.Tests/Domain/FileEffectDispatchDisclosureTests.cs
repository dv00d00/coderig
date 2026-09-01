using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

// A file badge may rest entirely on whole-program devirtualization: CHA says the call CAN land on an effectful
// override, while no real call path proves it does. `reaches` has always disclosed that (its "NOT a real call"
// fan-out bucket); the file model used to launder it into a plain badge, which is how
// PathwayTreeNode.get_Task showed `cache:18` for a reach `rig reaches` scored as ZERO real effects.
// These tests pin the DISCLOSURE, not the reach: the badge stays, it just says what it rests on.
public sealed class FileEffectDispatchDisclosureTests
{
    private const string File = "/repo/Caller.cs";
    private const string Caller = "M:Fixture.Caller.Run()";
    private const string Iface = "M:Fixture.IStore.Put()";
    private const string Impl = "M:Fixture.RedisStore.Put()";
    private const string Hop = "M:Fixture.Middle.Go()";
    private const string Direct = "M:Fixture.Direct.Write()";

    [Test]
    public void A_reach_that_exists_only_through_dispatch_is_flagged()
    {
        // Caller -> IStore.Put is a real edge; IStore.Put -> RedisStore.Put is DISPATCH, and only the impl
        // performs the effect. Nothing proves the call lands there.
        var model = Build(DispatchOnly(), [Method(Caller, 10)], [Effect(Impl, 30)]).Find(File).ShouldNotBeNull();

        var badge = model.Methods.Single(method => method.SymbolId == Caller).Effects.ShouldHaveSingleItem();
        badge.Family.ShouldBe("cache");
        badge.ViaDispatchOnly.ShouldBeTrue();
    }

    [Test]
    public void A_real_call_path_is_not_flagged()
    {
        var graph = Graph([new CallEdge(Caller, Direct, EdgeKinds.Invocation, File, 12)]);

        var model = Build(graph, [Method(Caller, 10)], [Effect(Direct, 30)]).Find(File).ShouldNotBeNull();

        var badge = model.Methods.Single(method => method.SymbolId == Caller).Effects.ShouldHaveSingleItem();
        badge.NearestDepth.ShouldBe(1);
        badge.ViaDispatchOnly.ShouldBeFalse();
    }

    // The important one: a SHORT dispatch guess must not hide a longer REAL path, and must not lend the real
    // badge its own smaller number. Basis wins over distance, then distance decides within the basis.
    [Test]
    public void A_real_path_wins_over_a_shorter_dispatch_route()
    {
        var graph = Graph(
            [
                new CallEdge(Caller, Iface, EdgeKinds.Invocation, File, 12),
                new CallEdge(Caller, Hop, EdgeKinds.Invocation, File, 13),
                new CallEdge(Hop, Direct, EdgeKinds.Invocation, "/repo/Middle.cs", 5),
            ],
            dispatch: [new DispatchFact(Iface, Impl, "impl")]
        );

        var model = Build(graph, [Method(Caller, 10)], [Effect(Impl, 30), Effect(Direct, 31)]).Find(File).ShouldNotBeNull();

        var badge = model.Methods.Single(method => method.SymbolId == Caller).Effects.ShouldHaveSingleItem();
        badge.ViaDispatchOnly.ShouldBeFalse();
        // 2 = Caller -> Middle.Go -> Direct.Write. The dispatch route scored 1; reporting that would have been
        // a number no real call path supports.
        badge.NearestDepth.ShouldBe(2);
    }

    [Test]
    public void An_effect_in_the_bodys_own_line_is_never_flagged()
    {
        var model = Build(Graph([]), [Method(Caller, 10)], [Effect(Caller, 11)]).Find(File).ShouldNotBeNull();

        var badge = model.Methods.Single(method => method.SymbolId == Caller).Effects.ShouldHaveSingleItem();
        badge.NearestDepth.ShouldBe(0);
        badge.ViaDispatchOnly.ShouldBeFalse();
    }

    private static FactGraphData DispatchOnly() =>
        Graph([new CallEdge(Caller, Iface, EdgeKinds.Invocation, File, 12)], dispatch: [new DispatchFact(Iface, Impl, "impl")]);

    private static FileEffectReadModelIndex Build(
        FactGraphData graph,
        IEnumerable<SymbolFact> symbols,
        IEnumerable<DerivedEffect> effects
    ) => FileEffectReadModelIndex.Build(graph, symbols, effects, [new FileEffectSelector("cache", [new EffectPredicate("redis")])], [File]);

    private static DerivedEffect Effect(string owner, int line) => new("redis", "write", "redis", owner, File, line);

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

    private static FactGraphData Graph(IReadOnlyList<CallEdge> edges, IReadOnlyList<DispatchFact>? dispatch = null)
    {
        var methods = edges
            .SelectMany(edge => new[] { edge.Caller, edge.Callee })
            .Concat((dispatch ?? []).SelectMany(fact => new[] { fact.SourceMember, fact.TargetMember }))
            .Concat([Caller])
            .Distinct(StringComparer.Ordinal)
            .Select(id => new MethodRef(id, id, "T:Fixture"))
            .ToArray();
        return new FactGraphData(edges, [], methods, [], dispatch);
    }
}
