using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class DemandAsyncDeliveryGraphTests
{
    private const string Producer = "M:N.Publisher.Raise";
    private const string RegisterOne = "M:N.Wiring.RegisterOne";
    private const string RegisterTwo = "M:N.Wiring.RegisterTwo";
    private const string HandlerOne = "M:N.Handlers.One";
    private const string HandlerTwo = "M:N.Handlers.Two";
    private const string Sink = "M:N.Sink.Write";
    private const string Event = "E:N.Publisher.Changed";

    [Test]
    public void Forward_exact_delivery_obeys_sync_exact_include_set_inclusion()
    {
        var view = EventGraph(fanout: false);

        var sync = Forward(view, FactPathFinder.TraversalMode.SyncCut);
        var exact = Forward(view, FactPathFinder.TraversalMode.AsyncExact);
        var include = Forward(view, FactPathFinder.TraversalMode.AsyncInclude);

        var syncSet = Reach(sync.Graph, FactPathFinder.TraversalMode.SyncCut);
        var exactSet = Reach(exact.Graph, FactPathFinder.TraversalMode.AsyncExact);
        var includeSet = Reach(include.Graph, FactPathFinder.TraversalMode.AsyncInclude);
        syncSet.ShouldBeSubsetOf(exactSet);
        exactSet.ShouldBeSubsetOf(includeSet);
        exactSet.ShouldContain(HandlerOne);
        exactSet.ShouldContain(Sink);
        exact.Graph.CallEdges.ShouldContain(edge =>
            edge.Caller == Producer && edge.Callee == HandlerOne && edge.DeliveryPrecision == DeliveryPrecisions.Exact
        );
        exact.Diagnostics.Delivery!.ReferencePartitions.Rows.ShouldBeGreaterThan(0);
        exact.Diagnostics.Load.UsedLegacyFallback.ShouldBeFalse();
    }

    [Test]
    public void Forward_fanout_is_cut_by_async_exact_and_walked_by_async_include()
    {
        var view = EventGraph(fanout: true);

        var exact = Forward(view, FactPathFinder.TraversalMode.AsyncExact);
        var include = Forward(view, FactPathFinder.TraversalMode.AsyncInclude);
        var exactSet = Reach(exact.Graph, FactPathFinder.TraversalMode.AsyncExact);
        var includeSet = Reach(include.Graph, FactPathFinder.TraversalMode.AsyncInclude);

        exactSet.ShouldNotContain(HandlerOne);
        exactSet.ShouldNotContain(HandlerTwo);
        includeSet.ShouldContain(HandlerOne);
        includeSet.ShouldContain(HandlerTwo);
        include.Graph.CallEdges.Count(edge => edge.Caller == Producer && edge.DeliveryPrecision == DeliveryPrecisions.Fanout).ShouldBe(2);
    }

    [Test]
    public void Reverse_delivery_preserves_exact_and_fanout_modes_without_whole_graph_reads()
    {
        var exactView = EventGraph(fanout: false);
        var exact = Reverse(exactView, HandlerOne, FactPathFinder.TraversalMode.AsyncExact);
        FactPathFinder.ReachedBy(exact.Graph, HandlerOne, mode: FactPathFinder.TraversalMode.AsyncExact).Keys.ShouldContain(Producer);
        exact.Diagnostics.DeliverySitesSynthesized.ShouldBeTrue();

        var fanoutView = EventGraph(fanout: true);
        var fanoutExact = Reverse(fanoutView, HandlerOne, FactPathFinder.TraversalMode.AsyncExact);
        var fanoutInclude = Reverse(fanoutView, HandlerOne, FactPathFinder.TraversalMode.AsyncInclude);
        FactPathFinder
            .ReachedBy(fanoutExact.Graph, HandlerOne, mode: FactPathFinder.TraversalMode.AsyncExact)
            .Keys.ShouldNotContain(Producer);
        FactPathFinder
            .ReachedBy(fanoutInclude.Graph, HandlerOne, mode: FactPathFinder.TraversalMode.AsyncInclude)
            .Keys.ShouldContain(Producer);
        fanoutView.RequestedTargets.ShouldContain(Event);
    }

    [Test]
    public void Reverse_delivery_discovers_interface_registration_hub_without_chaining_dispatch_hops()
    {
        const string contract = "M:N.IHandler.Handle";
        const string concrete = "M:N.BaseHandler.Handle";
        const string derived = "M:N.DerivedHandler.Handle";
        var view = new DeliveryView()
            .Method(Producer)
            .Method(RegisterOne)
            .Method(contract)
            .Method(concrete)
            .Method(derived)
            .Reference(Producer, Event, RefKinds.Read, line: 10)
            .Reference(RegisterOne, Event, RefKinds.Read, line: 20)
            .Reference(RegisterOne, contract, RefKinds.MethodGroup, line: 20)
            .Dispatch(contract, concrete, "impl")
            .Dispatch(concrete, derived, "override");

        var result = Reverse(view, concrete, FactPathFinder.TraversalMode.AsyncExact);
        var fromProducer = FactPathFinder.Reaches(result.Graph, Producer, mode: FactPathFinder.TraversalMode.AsyncExact).Keys;

        fromProducer.ShouldContain(concrete);
        fromProducer.ShouldNotContain(derived, "one call-site dispatch must not compose impl then override dispatch");
        result.Graph.CallEdges.ShouldContain(edge => edge.Caller == Producer && edge.Callee == contract);
        view.RequestedTargets.ShouldContain(contract);
    }

    [Test]
    public void Classified_method_group_handoff_is_async_reachable_without_delivery_projection()
    {
        const string root = "M:N.Root.Schedule";
        const string handler = "M:N.Work.Run";
        var view = new DeliveryView()
            .Method(root)
            .Method(handler)
            .Reference(root, handler, RefKinds.MethodGroup, line: 4, delegateConsumer: "M:N.Scheduler.Schedule(System.Action)");
        var rules = new DemandForwardGraphRules(
            new ForwardCallProjectionRules(Handoff: [new FactHandoffRule("scheduler", "scheduler", ["Scheduler.Schedule"])]),
            [],
            [],
            []
        );
        var result = DemandForwardPathGraph.Build(
            view,
            rules,
            new DemandForwardGraphRequest(root, int.MaxValue, FactPathFinder.TraversalMode.AsyncExact)
        );

        result.Graph.CallEdges.Single().Kind.ShouldBe(EdgeKinds.Handoff);
        FactPathFinder.Find(result.Graph, root, handler, mode: FactPathFinder.TraversalMode.SyncCut).ShouldBeNull();
        FactPathFinder.Find(result.Graph, root, handler, mode: FactPathFinder.TraversalMode.AsyncExact).ShouldNotBeNull();
    }

    [Test]
    public void Delivery_materialization_cap_fails_closed()
    {
        var view = EventGraph(fanout: true);

        Should.Throw<DemandForwardGraphUnavailableException>(() =>
            DemandForwardPathGraph.Build(
                view,
                Rules(),
                new DemandForwardGraphRequest(Producer, int.MaxValue, FactPathFinder.TraversalMode.AsyncInclude, MaxNodes: 2)
            )
        );
        Should.Throw<DemandReverseCallersGraphUnavailableException>(() =>
            DemandReverseCallersGraph.Build(
                view,
                Rules(),
                new DemandReverseCallersGraphRequest(HandlerOne, int.MaxValue, FactPathFinder.TraversalMode.AsyncInclude, MaxNodes: 2)
            )
        );
    }

    [Test]
    public void Missing_handler_and_unprojectable_delivery_rule_fail_closed()
    {
        var missing = new DeliveryView()
            .Method(Producer)
            .Method(RegisterOne)
            .Reference(Producer, Event, RefKinds.Read, line: 10)
            .Reference(RegisterOne, Event, RefKinds.Read, line: 20)
            .Reference(RegisterOne, HandlerOne, RefKinds.MethodGroup, line: 20);
        Should.Throw<DemandForwardGraphUnavailableException>(() =>
            DemandForwardPathGraph.Build(
                missing,
                Rules(),
                new DemandForwardGraphRequest(Producer, int.MaxValue, FactPathFinder.TraversalMode.AsyncExact)
            )
        );

        var unsupported = Rules() with
        {
            Delivery =
            [
                new DeliveryRule(
                    "unprojectable",
                    "custom",
                    "exact",
                    new DeliveryEndpoint("reflection", "symbol"),
                    new DeliveryEndpoint("reflection", "symbol")
                ),
            ],
        };
        Should.Throw<DemandForwardGraphUnavailableException>(() =>
            DemandForwardPathGraph.Build(
                EventGraph(fanout: false),
                unsupported,
                new DemandForwardGraphRequest(Producer, int.MaxValue, FactPathFinder.TraversalMode.AsyncExact)
            )
        );
    }

    [Test]
    public void Added_removed_and_retargeted_subscriptions_change_only_the_current_delivery_edges()
    {
        var original = Forward(EventGraph(fanout: false), FactPathFinder.TraversalMode.AsyncExact);
        var retargetedView = new DeliveryView()
            .Method(Producer)
            .Method(RegisterOne)
            .Method(HandlerTwo)
            .Reference(Producer, Event, RefKinds.Read, line: 10)
            .Reference(RegisterOne, Event, RefKinds.Read, line: 20)
            .Reference(RegisterOne, HandlerTwo, RefKinds.MethodGroup, line: 20);
        var retargeted = Forward(retargetedView, FactPathFinder.TraversalMode.AsyncExact);
        var removedView = new DeliveryView().Method(Producer).Reference(Producer, Event, RefKinds.Read, line: 10);
        var removed = Forward(removedView, FactPathFinder.TraversalMode.AsyncExact);

        original.Graph.CallEdges.ShouldContain(edge => edge.Caller == Producer && edge.Callee == HandlerOne);
        retargeted.Graph.CallEdges.ShouldContain(edge => edge.Caller == Producer && edge.Callee == HandlerTwo);
        retargeted.Graph.CallEdges.ShouldNotContain(edge => edge.Caller == Producer && edge.Callee == HandlerOne);
        removed.Graph.CallEdges.ShouldNotContain(edge => edge.Caller == Producer && edge.DeliveryPrecision != null);
    }

    private static DemandForwardGraphResult Forward(DeliveryView view, FactPathFinder.TraversalMode mode) =>
        DemandForwardPathGraph.Build(view, Rules(), new DemandForwardGraphRequest(Producer, int.MaxValue, mode));

    private static DemandReverseCallersGraphResult Reverse(DeliveryView view, string target, FactPathFinder.TraversalMode mode) =>
        DemandReverseCallersGraph.Build(view, Rules(), new DemandReverseCallersGraphRequest(target, int.MaxValue, mode));

    private static HashSet<string> Reach(FactGraphData graph, FactPathFinder.TraversalMode mode) =>
        FactPathFinder.Reaches(graph, Producer, mode: mode).Keys.ToHashSet(StringComparer.Ordinal);

    private static DemandForwardGraphRules Rules() =>
        new(
            new ForwardCallProjectionRules(ClassifyEventSubscriptions: true),
            [],
            [],
            [
                new DeliveryRule(
                    "events",
                    "event_raise",
                    "exact",
                    new DeliveryEndpoint("event-symbol", "symbol"),
                    new DeliveryEndpoint("event-symbol", "symbol")
                ),
            ]
        );

    private static DeliveryView EventGraph(bool fanout)
    {
        var view = new DeliveryView()
            .Method(Producer)
            .Method(RegisterOne)
            .Method(HandlerOne)
            .Method(Sink)
            .Reference(Producer, Event, RefKinds.Read, line: 10)
            .Reference(RegisterOne, Event, RefKinds.Read, line: 20)
            .Reference(RegisterOne, HandlerOne, RefKinds.MethodGroup, line: 20)
            .Reference(HandlerOne, Sink, RefKinds.Invocation, line: 30);
        if (fanout)
        {
            view.Method(RegisterTwo)
                .Method(HandlerTwo)
                .Reference(RegisterTwo, Event, RefKinds.Read, line: 40)
                .Reference(RegisterTwo, HandlerTwo, RefKinds.MethodGroup, line: 40);
        }
        return view;
    }

    private sealed class DeliveryView : IFactGraphView
    {
        private readonly List<ReferenceFact> references = [];
        private readonly List<SymbolFact> symbols = [];
        private readonly List<DispatchFact> dispatch = [];

        internal List<string> RequestedTargets { get; } = [];

        internal DeliveryView Method(string id)
        {
            var name = id[(id.LastIndexOf('.') + 1)..];
            symbols.Add(
                new SymbolFact(id, SymbolKinds.Method, name, "N", "T:N.Type", "public", "", name + "()", "/code.cs", 1, 1, "App", false)
            );
            return this;
        }

        internal DeliveryView Reference(string caller, string target, string kind, int line, string? delegateConsumer = null)
        {
            references.Add(new ReferenceFact(target, kind, caller, "App", true, "/code.cs", line, DelegateConsumer: delegateConsumer));
            return this;
        }

        internal DeliveryView Dispatch(string source, string target, string kind)
        {
            dispatch.Add(new DispatchFact(source, target, kind, "/code.cs"));
            return this;
        }

        public IReadOnlyList<ReferenceFact> ReferencesFrom(string enclosingSymbolId) =>
            references.Where(row => row.EnclosingSymbolId == enclosingSymbolId).ToArray();

        public IReadOnlyList<ReferenceFact> ReferencesTo(string targetSymbolId)
        {
            RequestedTargets.Add(targetSymbolId);
            return references.Where(row => row.TargetSymbolId == targetSymbolId).ToArray();
        }

        public IReadOnlyList<ReferenceFact> ReferencesToMethodKey(string methodKey) =>
            references.Where(row => ReferenceTargetMethodKey.Normalize(row.TargetSymbolId) == methodKey).ToArray();

        public IReadOnlyList<SymbolFact> SymbolsById(string symbolId) => symbols.Where(row => row.SymbolId == symbolId).ToArray();

        public IReadOnlyList<SymbolFact> SymbolsByContainingSymbol(string containingSymbolId) =>
            symbols.Where(row => row.ContainingSymbolId == containingSymbolId).ToArray();

        public IReadOnlyCollection<string> MethodSymbolIds => symbols.Select(row => row.SymbolId).ToArray();

        public IReadOnlyList<SymbolFact> MethodsById(string symbolId) => SymbolsById(symbolId);

        public IReadOnlyList<SymbolFact> MethodsByContainingSymbol(string containingSymbolId) =>
            SymbolsByContainingSymbol(containingSymbolId);

        public IReadOnlyList<TypeRelationFact> TypeRelationsFrom(string typeSymbolId) => [];

        public IReadOnlyList<TypeRelationFact> TypeRelationsTo(string relatedSymbolId) => [];

        public IReadOnlyList<TypeRelationFact> DispatchRelationsTo(string declaringTypeId) => [];

        public IReadOnlyList<DispatchFact> DispatchFrom(string sourceMember) =>
            dispatch.Where(row => row.SourceMember == sourceMember).ToArray();

        public IReadOnlyList<DispatchFact> DispatchTo(string targetMember) =>
            dispatch.Where(row => row.TargetMember == targetMember).ToArray();
    }
}
