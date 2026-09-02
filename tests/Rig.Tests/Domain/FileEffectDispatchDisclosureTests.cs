using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

// A file badge may rest entirely on whole-program devirtualization: CHA says the call CAN land on an effectful
// override, while no real call path proves it does. `reaches` has always disclosed that (its "NOT a real call"
// fan-out bucket); the file model used to launder it into a plain badge, which is how
// PathwayTreeNode.get_Task showed `cache:18` for a reach `rig reaches` scored as ZERO real effects.
//
// The disclosure fires on the same split the forward walk draws: only a hop whose receiver-narrowed candidate
// set has MORE THAN ONE target is polymorphic (`Fanout > 1`). A hop with exactly one candidate is deterministic
// and reads as an ordinary reach — `tree` folds it away as `«via IFoo»`, and hedging it here made "possible
// dispatch" fire on nearly every service call in a codebase where most interfaces have one implementation.
// These tests pin the DISCLOSURE, not the reach: the badge stays, it just says what it rests on.
public sealed class FileEffectDispatchDisclosureTests
{
    private const string File = "/repo/Caller.cs";
    private const string Caller = "M:Fixture.Caller.Run()";
    private const string Iface = "M:Fixture.IStore.Put()";
    private const string Impl = "M:Fixture.RedisStore.Put()";
    private const string OtherImpl = "M:Fixture.MemoryStore.Put()";
    private const string Hop = "M:Fixture.Middle.Go()";
    private const string Direct = "M:Fixture.Direct.Write()";

    [Test]
    public void A_single_target_dispatch_reach_is_an_ordinary_effect()
    {
        // Caller -> IStore.Put is a real edge; IStore.Put -> RedisStore.Put is DISPATCH with exactly ONE
        // candidate. The call can land nowhere else, so this is a deterministic reach, not a guess.
        var model = Build(SingleTargetDispatch(), [Method(Caller, 10)], [Effect(Impl, 30)]).Find(File).ShouldNotBeNull();

        var badge = model.Methods.Single(method => method.SymbolId == Caller).Effects.ShouldHaveSingleItem();
        badge.Family.ShouldBe("cache");
        badge.NearestDepth.ShouldBe(1);
        badge.ViaDispatchOnly.ShouldBeFalse();
    }

    [Test]
    public void A_reach_that_exists_only_through_a_polymorphic_dispatch_is_flagged()
    {
        // IStore.Put fans to TWO implementations and only one of them performs the effect. Nothing proves the
        // call lands on that one.
        var model = Build(PolymorphicDispatch(), [Method(Caller, 10)], [Effect(Impl, 30)]).Find(File).ShouldNotBeNull();

        var badge = model.Methods.Single(method => method.SymbolId == Caller).Effects.ShouldHaveSingleItem();
        badge.Family.ShouldBe("cache");
        badge.NearestDepth.ShouldBe(1);
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

    // The important one: a SHORT polymorphic guess must not hide a longer DETERMINISTIC path, and must not lend
    // the deterministic badge its own smaller number. Basis wins over distance, then distance decides within the
    // basis.
    [Test]
    public void A_deterministic_path_wins_over_a_shorter_polymorphic_route()
    {
        var graph = Graph(
            [
                new CallEdge(Caller, Iface, EdgeKinds.Invocation, File, 12),
                new CallEdge(Caller, Hop, EdgeKinds.Invocation, File, 13),
                new CallEdge(Hop, Direct, EdgeKinds.Invocation, "/repo/Middle.cs", 5),
            ],
            dispatch: [new DispatchFact(Iface, Impl, "impl"), new DispatchFact(Iface, OtherImpl, "impl")]
        );

        var model = Build(graph, [Method(Caller, 10)], [Effect(Impl, 30), Effect(Direct, 31)]).Find(File).ShouldNotBeNull();

        var badge = model.Methods.Single(method => method.SymbolId == Caller).Effects.ShouldHaveSingleItem();
        badge.ViaDispatchOnly.ShouldBeFalse();
        // 2 = Caller -> Middle.Go -> Direct.Write. The polymorphic route scored 1; reporting that would have
        // been a number no deterministic route supports.
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

    // The degree is the NARROWED, PER-EDGE count the reverse walk actually computes, not the receiver-blind
    // fan of the hub. Animal.Speak has two overrides (blind fan 2); a caller whose static receiver is Dog
    // narrows to Dog.Speak alone (fan 1) and reads as deterministic, while a caller with no resolved receiver
    // falls back to full CHA (fan 2) and is flagged — the same graph, two callers, both states.
    [Test]
    public void The_degree_is_the_receiver_narrowed_count_of_the_callers_own_edge()
    {
        const string callerA = "M:N.CallerA.Go";
        const string callerB = "M:N.CallerB.Go";
        const string animalSpeak = "M:N.Animal.Speak";
        const string dogSpeak = "M:N.Dog.Speak";
        const string catSpeak = "M:N.Cat.Speak";
        var graph = new FactGraphData(
            [
                new CallEdge(callerA, animalSpeak, EdgeKinds.Invocation, File, 12, ReceiverType: "N.Dog"),
                new CallEdge(callerB, animalSpeak, EdgeKinds.Invocation, File, 22, ReceiverType: null),
            ],
            [],
            [
                new MethodRef(callerA, "Go", "T:N.CallerA"),
                new MethodRef(callerB, "Go", "T:N.CallerB"),
                new MethodRef(animalSpeak, "Speak", "T:N.Animal"),
                new MethodRef(dogSpeak, "Speak", "T:N.Dog", IsOverride: true),
                new MethodRef(catSpeak, "Speak", "T:N.Cat", IsOverride: true),
            ],
            [new BaseEdge("T:N.Dog", "T:N.Animal"), new BaseEdge("T:N.Cat", "T:N.Animal")],
            [new DispatchFact(animalSpeak, dogSpeak, "override"), new DispatchFact(animalSpeak, catSpeak, "override")]
        );

        var model = Build(graph, [Method(callerA, 10), Method(callerB, 20)], [Effect(dogSpeak, 30)]).Find(File).ShouldNotBeNull();

        var resolved = model.Methods.Single(method => method.SymbolId == callerA).Effects.ShouldHaveSingleItem();
        resolved.NearestDepth.ShouldBe(1);
        resolved.ViaDispatchOnly.ShouldBeFalse();

        var unresolved = model.Methods.Single(method => method.SymbolId == callerB).Effects.ShouldHaveSingleItem();
        unresolved.NearestDepth.ShouldBe(1);
        unresolved.ViaDispatchOnly.ShouldBeTrue();
    }

    private static FactGraphData SingleTargetDispatch() =>
        Graph([new CallEdge(Caller, Iface, EdgeKinds.Invocation, File, 12)], dispatch: [new DispatchFact(Iface, Impl, "impl")]);

    private static FactGraphData PolymorphicDispatch() =>
        Graph(
            [new CallEdge(Caller, Iface, EdgeKinds.Invocation, File, 12)],
            dispatch: [new DispatchFact(Iface, Impl, "impl"), new DispatchFact(Iface, OtherImpl, "impl")]
        );

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
