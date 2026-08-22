using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class DemandForwardPathGraphTests
{
    [Test]
    public void Disconnected_generic_corpus_does_not_change_the_demand_closure_or_counters()
    {
        var sparse = GenericDispatchGraph();
        var crowded = GenericDispatchGraph();
        for (var i = 0; i < 55; i++)
        {
            crowded
                .Method($"M:N.Disconnected{i}.Run", "Run()", $"T:N.Disconnected{i}")
                .Call($"M:N.Disconnected{i}.Run", "M:N.Repo.G", methodBinding: $"[\"C:N.T{i}\"]");
        }

        var sparseResult = Build(sparse, "M:N.Root.Run");
        var crowdedResult = Build(crowded, "M:N.Root.Run");

        crowdedResult.Graph.CallEdges.ShouldBe(sparseResult.Graph.CallEdges);
        crowdedResult.Graph.Methods.ShouldBe(sparseResult.Graph.Methods);
        crowdedResult.Graph.ImplementsEdges.ShouldBe(sparseResult.Graph.ImplementsEdges, ignoreOrder: true);
        crowdedResult.Graph.BaseEdges.ShouldBe(sparseResult.Graph.BaseEdges, ignoreOrder: true);
        crowdedResult.Diagnostics.ShouldBe(sparseResult.Diagnostics);
        crowdedResult.Diagnostics.Load.Mode.ShouldBe(DemandForwardLoadMode.KeyedDemand);
        crowdedResult.Diagnostics.Load.UsedLegacyFallback.ShouldBeFalse();
        crowded.RequestedCallers.ShouldBe(sparse.RequestedCallers);
        crowded.RequestedCallers.ShouldNotContain(caller => caller.Contains("Disconnected", StringComparison.Ordinal));
        crowdedResult.Graph.CallEdges.Count.ShouldBeLessThan(crowded.ReferenceCount);
        crowdedResult.Graph.CallEdges.ShouldNotBeEmpty();
    }

    [Test]
    public void Mono_interface_path_uses_current_one_hop_dispatch_and_collapses_before_render()
    {
        const string root = "M:N.Root.Run";
        const string generic = "M:N.Repo.Start";
        const string contract = "M:N.ILogger.Startup";
        const string inherited = "M:N.ServiceBase.Startup";
        var view = new DemandPathTestView()
            .Method(root, "Run()", "T:N.Root")
            .Method(generic, "Start<T>()", "T:N.Repo")
            .Method(contract, "Startup()", "T:N.ILogger")
            .Method(inherited, "Startup()", "T:N.ServiceBase")
            .Method("M:N.SvcA.Startup", "Startup()", "T:N.SvcA")
            .Method("M:N.SvcB.Startup", "Startup()", "T:N.SvcB")
            .Call(root, generic, methodBinding: "[\"C:N.Impl\"]")
            .Call(generic, contract, receiver: "T")
            .Relation("T:N.Impl", "T:N.ILogger", RelationKinds.Interface)
            .Relation("T:N.Impl", "T:N.ServiceBase", RelationKinds.Base)
            .Relation("T:N.SvcA", "T:N.ServiceBase", RelationKinds.Base)
            .Relation("T:N.SvcB", "T:N.ServiceBase", RelationKinds.Base)
            .Dispatch(contract, inherited, DispatchKinds.Impl)
            .Dispatch(inherited, "M:N.SvcA.Startup", DispatchKinds.Override)
            .Dispatch(inherited, "M:N.SvcB.Startup", DispatchKinds.Override);

        var result = Build(view, root);
        var path = FactPathFinder.Find(result.Graph, root, inherited)!;
        var collapsed = MonomorphCollapse.CollapsePath(path);
        var reach = FactPathFinder.Reaches(result.Graph, root);

        collapsed.Select(step => step.SymbolId).ShouldContain(generic);
        collapsed.ShouldNotContain(step => MonomorphizedNodeId.IsMonomorphized(step.SymbolId));
        reach.Keys.ShouldContain(inherited);
        reach.Keys.ShouldNotContain("M:N.SvcA.Startup");
        reach.Keys.ShouldNotContain("M:N.SvcB.Startup");
    }

    [Test]
    public void Open_generic_declaring_type_discovers_constructed_base_relation_and_override()
    {
        const string root = "M:N.Root.Run";
        const string openBase = "M:N.Base`1.Run";
        const string concreteOverride = "M:N.Sub.Run";
        var view = new DemandPathTestView()
            .Method(root, "Run()", "T:N.Root")
            .Method(openBase, "Run()", "T:N.Base`1")
            .Method(concreteOverride, "Run()", "T:N.Sub", isOverride: true)
            .Call(root, openBase)
            .Relation("T:N.Sub", "T:N.Base`1{T:N.Concrete}", RelationKinds.Base);

        var result = Build(view, root);

        result.Graph.BaseEdges.ShouldNotBeNull();
        result.Graph.BaseEdges!.ShouldContain(new BaseEdge("T:N.Sub", "T:N.Base`1{T:N.Concrete}"));
        FactPathFinder.Find(result.Graph, root, concreteOverride).ShouldNotBeNull();
    }

    [Test]
    public void Resolved_interface_discovers_matching_error_relation_even_when_mined_facts_coexist()
    {
        const string root = "M:N.Root.Run";
        const string contract = "M:N.IFoo.Run";
        const string recovered = "M:N.PartialImpl.Run";
        const string mined = "M:N.MinedImpl.Run";
        var view = new DemandPathTestView()
            .Method(root, "Run()", "T:N.Root")
            .Method(contract, "Run()", "T:N.IFoo")
            .Method(recovered, "Run()", "T:N.PartialImpl")
            .Method(mined, "Run()", "T:N.MinedImpl")
            .Call(root, contract)
            .Relation("T:N.PartialImpl", "!:IFoo", RelationKinds.Interface)
            .Relation("T:N.Unrelated", "!:IBar", RelationKinds.Interface)
            .Dispatch(contract, mined, DispatchKinds.Impl);

        var result = Build(view, root);

        result.Graph.ImplementsEdges.ShouldContain(new ImplementsEdge("T:N.PartialImpl", "!:IFoo"));
        result.Graph.ImplementsEdges.ShouldNotContain(new ImplementsEdge("T:N.Unrelated", "!:IBar"));
        FactPathFinder.Find(result.Graph, root, recovered).ShouldNotBeNull();
        FactPathFinder.Find(result.Graph, root, mined).ShouldNotBeNull();
    }

    [Test]
    public void Caller_local_event_subscription_is_sync_cut_and_async_included()
    {
        const string root = "M:N.Root.Subscribe";
        const string handler = "M:N.Root.Handle";
        var view = new DemandPathTestView()
            .Method(root, "Subscribe()", "T:N.Root")
            .Method(handler, "Handle()", "T:N.Root")
            .Reference(root, "E:N.Root.Changed", RefKinds.Read, line: 7)
            .Call(root, handler, kind: RefKinds.MethodGroup, line: 7);

        var sync = Build(view, root, FactPathFinder.TraversalMode.SyncCut, classifyEvents: true);
        var async = Build(view, root, FactPathFinder.TraversalMode.AsyncExact, classifyEvents: true);

        sync.EventSubscriptionsClassified.ShouldBeTrue();
        sync.Graph.CallEdges.Single().Kind.ShouldBe(EdgeKinds.Handoff);
        FactPathFinder.Find(sync.Graph, root, handler, mode: FactPathFinder.TraversalMode.SyncCut).ShouldBeNull();
        FactPathFinder.Find(async.Graph, root, handler, mode: FactPathFinder.TraversalMode.AsyncExact).ShouldNotBeNull();
        view.RequestedCallers.Distinct(StringComparer.Ordinal).ShouldBe([root, handler], ignoreOrder: true);
    }

    [Test]
    public void Delegate_slot_dispatch_is_loaded_and_walked_as_one_hop()
    {
        const string root = "M:N.Root.Run";
        const string slot = "F:N.Root.Callback";
        const string handler = "M:N.Root.Handle";
        var view = new DemandPathTestView()
            .Method(root, "Run()", "T:N.Root")
            .Method(handler, "Handle()", "T:N.Root")
            .Call(root, slot)
            .Dispatch(slot, handler, DispatchKinds.DelegateBind);

        var result = Build(view, root);
        var path = FactPathFinder.Find(result.Graph, root, handler);

        path.ShouldNotBeNull();
        path!.Last().Kind.ShouldBe("delegate-dispatch");
        result.Graph.MinedDispatch.ShouldNotBeNull();
        result.Graph.MinedDispatch!.ShouldContain(fact =>
            fact.SourceMember == slot && fact.TargetMember == handler && fact.Kind == DispatchKinds.DelegateBind
        );
    }

    [Test]
    public void No_seed_unreachable_target_and_max_depth_match_the_existing_engine()
    {
        var view = new DemandPathTestView()
            .Method("M:N.Root.A", "A()", "T:N.Root")
            .Method("M:N.Root.B", "B()", "T:N.Root")
            .Method("M:N.Root.C", "C()", "T:N.Root")
            .Method("M:N.Other.D", "D()", "T:N.Other")
            .Call("M:N.Root.A", "M:N.Root.B")
            .Call("M:N.Root.B", "M:N.Root.C");

        var missing = Build(view, "NoSuchSeed");
        missing.Graph.Methods.ShouldBeEmpty();
        missing.Diagnostics.Closure.MatchedSeeds.ShouldBe(0);

        var bounded = Build(view, "M:N.Root.A", maxDepth: 1);
        FactPathFinder.Find(bounded.Graph, "M:N.Root.A", "M:N.Root.C", maxDepth: 1).ShouldBeNull();
        FactPathFinder.Find(bounded.Graph, "M:N.Root.A", "M:N.Other.D").ShouldBeNull();
        bounded.Graph.Methods.ShouldNotContain(method => method.SymbolId == "M:N.Other.D");
    }

    private static DemandForwardGraphResult Build(
        DemandPathTestView view,
        string from,
        FactPathFinder.TraversalMode mode = FactPathFinder.TraversalMode.SyncCut,
        bool classifyEvents = false,
        int maxDepth = int.MaxValue
    ) =>
        DemandForwardPathGraph.Build(
            view,
            new DemandForwardGraphRules(new ForwardCallProjectionRules(ClassifyEventSubscriptions: classifyEvents), [], []),
            new DemandForwardGraphRequest(from, maxDepth, mode)
        );

    private static DemandPathTestView GenericDispatchGraph() =>
        new DemandPathTestView()
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method("M:N.Repo.G", "G<T>()", "T:N.Repo")
            .Method("M:N.IAnimal.Act", "Act()", "T:N.IAnimal")
            .Method("M:N.Cat.Act", "Act()", "T:N.Cat")
            .Method("M:N.Dog.Act", "Act()", "T:N.Dog")
            .Call("M:N.Root.Run", "M:N.Repo.G", methodBinding: "[\"C:N.Cat\"]")
            .Call("M:N.Repo.G", "M:N.IAnimal.Act", receiver: "T")
            .Relation("T:N.Cat", "T:N.IAnimal", RelationKinds.Interface)
            .Relation("T:N.Dog", "T:N.IAnimal", RelationKinds.Interface)
            .Dispatch("M:N.IAnimal.Act", "M:N.Cat.Act", DispatchKinds.Impl)
            .Dispatch("M:N.IAnimal.Act", "M:N.Dog.Act", DispatchKinds.Impl);
}

internal sealed class DemandPathTestView : IFactGraphView
{
    private readonly List<ReferenceFact> references = [];
    private readonly List<SymbolFact> symbols = [];
    private readonly List<TypeRelationFact> relations = [];
    private readonly List<DispatchFact> dispatch = [];

    internal List<string> RequestedCallers { get; } = [];
    internal int ReferenceCount => references.Count;

    internal DemandPathTestView Method(string id, string signature, string containing, bool isOverride = false)
    {
        var head = id.Split('(')[0];
        var name = head[(head.LastIndexOf('.') + 1)..].Split('`')[0];
        symbols.Add(
            new SymbolFact(id, SymbolKinds.Method, name, "N", containing, "public", "", signature, "/methods.cs", 1, 1, "App", isOverride)
        );
        return this;
    }

    internal DemandPathTestView Call(
        string caller,
        string callee,
        string kind = RefKinds.Invocation,
        string? receiver = null,
        string? methodBinding = null,
        int line = 1
    ) => Reference(caller, callee, kind, line, receiver, methodBinding);

    internal DemandPathTestView Reference(
        string caller,
        string target,
        string kind,
        int line,
        string? receiver = null,
        string? methodBinding = null
    )
    {
        references.Add(
            new ReferenceFact(
                target,
                kind,
                caller,
                "App",
                true,
                "/calls.cs",
                line,
                ReceiverType: receiver,
                MethodTypeArgBinding: methodBinding
            )
        );
        return this;
    }

    internal DemandPathTestView Relation(string type, string related, string kind)
    {
        relations.Add(new TypeRelationFact(type, related, kind));
        return this;
    }

    internal DemandPathTestView Dispatch(string source, string target, string kind)
    {
        dispatch.Add(new DispatchFact(source, target, kind));
        return this;
    }

    public IReadOnlyList<ReferenceFact> ReferencesFrom(string enclosingSymbolId)
    {
        RequestedCallers.Add(enclosingSymbolId);
        return references.Where(reference => reference.EnclosingSymbolId == enclosingSymbolId).ToArray();
    }

    public IReadOnlyList<ReferenceFact> ReferencesTo(string targetSymbolId) =>
        references.Where(reference => reference.TargetSymbolId == targetSymbolId).ToArray();

    public IReadOnlyList<SymbolFact> SymbolsById(string symbolId) => symbols.Where(symbol => symbol.SymbolId == symbolId).ToArray();

    public IReadOnlyList<SymbolFact> SymbolsByContainingSymbol(string containingSymbolId) =>
        symbols.Where(symbol => symbol.ContainingSymbolId == containingSymbolId).ToArray();

    public IReadOnlyCollection<string> MethodSymbolIds =>
        symbols.Select(symbol => symbol.SymbolId).Distinct(StringComparer.Ordinal).ToArray();

    public IReadOnlyList<SymbolFact> MethodsById(string symbolId) => SymbolsById(symbolId);

    public IReadOnlyList<SymbolFact> MethodsByContainingSymbol(string containingSymbolId) => SymbolsByContainingSymbol(containingSymbolId);

    public IReadOnlyList<TypeRelationFact> TypeRelationsFrom(string typeSymbolId) =>
        relations.Where(relation => relation.TypeSymbolId == typeSymbolId).ToArray();

    public IReadOnlyList<TypeRelationFact> TypeRelationsTo(string relatedSymbolId) =>
        relations.Where(relation => relation.RelatedSymbolId == relatedSymbolId).ToArray();

    public IReadOnlyList<TypeRelationFact> DispatchRelationsTo(string declaringTypeId)
    {
        var family = DispatchRelationKeys.RelatedFamily(declaringTypeId);
        var simpleName = DispatchRelationKeys.SimpleTypeName(declaringTypeId);
        return relations
            .Where(relation =>
                DispatchRelationKeys.RelatedFamily(relation.RelatedSymbolId) == family
                || DispatchRelationKeys.UnresolvedInterfaceName(relation) == simpleName
            )
            .Distinct()
            .ToArray();
    }

    public IReadOnlyList<DispatchFact> DispatchFrom(string sourceMember) =>
        dispatch.Where(fact => fact.SourceMember == sourceMember).ToArray();

    public IReadOnlyList<DispatchFact> DispatchTo(string targetMember) =>
        dispatch.Where(fact => fact.TargetMember == targetMember).ToArray();
}
