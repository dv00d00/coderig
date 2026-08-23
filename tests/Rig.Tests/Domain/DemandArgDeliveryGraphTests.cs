using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class DemandArgDeliveryGraphTests
{
    private const string Producer = "M:N.Actor.Send";
    private const string Register = "M:N.Actor.Register";
    private const string RegisterTwo = "M:N.Actor.RegisterTwo";
    private const string Handler = "M:N.Actor.Handle";
    private const string HandlerTwo = "M:N.Actor.HandleTwo";
    private const string Tell = "M:Echo.Process.tell(Echo.ProcessId,System.Object)";
    private const string Spawn = "M:Echo.Process.spawn(Echo.ProcessId,System.Action)";
    private const string Wrong = "M:Echo.Process.ask(Echo.ProcessId,System.Object)";

    [Test]
    public void Arg_delivery_path_and_leaf_identity_are_bounded_to_configured_endpoints()
    {
        var path = Graph("ProcessDns.Account", "ProcessNames.Account", fanout: false);
        var leaf = Graph("ProcessDns.Account", "ProcessNames.Account", fanout: false);

        var pathResult = Forward(path, Resolve: "path", FactPathFinder.TraversalMode.AsyncExact);
        var leafResult = Forward(leaf, Resolve: "leaf", FactPathFinder.TraversalMode.AsyncExact);

        Reach(pathResult.Graph, FactPathFinder.TraversalMode.AsyncExact).ShouldNotContain(Handler);
        Reach(leafResult.Graph, FactPathFinder.TraversalMode.AsyncExact).ShouldContain(Handler);
        leafResult.Graph.CallEdges.ShouldContain(edge =>
            edge.Caller == Producer
            && edge.Callee == Handler
            && edge.HandoffDispatcher == "actor.tell"
            && edge.DeliveryPrecision == DeliveryPrecisions.Exact
        );
        leaf.RequestedMethodKeys.Order(StringComparer.Ordinal)
            .ToArray()
            .ShouldBe(
                new[] { ReferenceTargetMethodKey.Normalize(Spawn), ReferenceTargetMethodKey.Normalize(Tell) }
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            );
        leaf.RequestedMethodKeys.ShouldNotContain(ReferenceTargetMethodKey.Normalize(Wrong));
    }

    [Test]
    public void Arg_delivery_rejects_bare_variables_and_filters_wrong_endpoints()
    {
        var bare = Graph("pid", "pid", fanout: false);
        bare.Reference(Producer, Wrong, RefKinds.Invocation, 12, firstArgumentName: "ProcessDns.Account");

        var result = Forward(bare, Resolve: "leaf", FactPathFinder.TraversalMode.AsyncExact);

        Reach(result.Graph, FactPathFinder.TraversalMode.AsyncExact).ShouldNotContain(Handler);
        result.Graph.CallEdges.ShouldNotContain(edge => edge.DeliveryPrecision != null);
        bare.RequestedMethodKeys.ShouldNotContain(ReferenceTargetMethodKey.Normalize(Wrong));
    }

    [Test]
    public void Handler_dispatcher_selects_the_classified_spawn_delegate_and_exact_cuts_fanout()
    {
        var view = Graph("ProcessDns.Account", "ProcessNames.Account", fanout: true);

        var exact = Forward(view, Resolve: "leaf", FactPathFinder.TraversalMode.AsyncExact);
        var include = Forward(view, Resolve: "leaf", FactPathFinder.TraversalMode.AsyncInclude);

        exact
            .Graph.CallEdges.Single(edge => edge.Caller == Register && edge.Callee == Handler)
            .HandoffDispatcher.ShouldBe("spawn.dispatch");
        Reach(exact.Graph, FactPathFinder.TraversalMode.AsyncExact).ShouldNotContain(Handler);
        var includeReach = Reach(include.Graph, FactPathFinder.TraversalMode.AsyncInclude);
        includeReach.ShouldContain(Handler);
        includeReach.ShouldContain(HandlerTwo);
        include.Graph.CallEdges.Count(edge => edge.Caller == Producer && edge.DeliveryPrecision == DeliveryPrecisions.Fanout).ShouldBe(2);
    }

    [Test]
    public void Unsupported_arg_resolve_fails_closed()
    {
        Should.Throw<DemandForwardGraphUnavailableException>(() =>
            Forward(
                Graph("ProcessDns.Account", "ProcessNames.Account", fanout: false),
                Resolve: "symbol",
                FactPathFinder.TraversalMode.AsyncExact
            )
        );
    }

    private static DemandForwardGraphResult Forward(ArgView view, string Resolve, FactPathFinder.TraversalMode mode) =>
        DemandForwardPathGraph.Build(view, Rules(Resolve), new DemandForwardGraphRequest(Producer, int.MaxValue, mode));

    private static HashSet<string> Reach(FactGraphData graph, FactPathFinder.TraversalMode mode) =>
        FactPathFinder.Reaches(graph, Producer, mode: mode).Keys.ToHashSet(StringComparer.Ordinal);

    private static DemandForwardGraphRules Rules(string resolve) =>
        new(
            new ForwardCallProjectionRules(Handoff: [new FactHandoffRule("spawn.dispatch", "actor", ["Echo.Process.spawn"])]),
            [],
            [],
            [
                new DeliveryRule(
                    "actors",
                    "actor.tell",
                    "heuristic",
                    new DeliveryEndpoint("arg", resolve, Methods: ["tell"], DeclaringTypes: ["Echo.Process"]),
                    new DeliveryEndpoint(
                        "arg",
                        resolve,
                        Methods: ["spawn"],
                        DeclaringTypes: ["Echo.Process"],
                        HandlerDispatcher: "spawn.dispatch"
                    )
                ),
            ]
        );

    private static ArgView Graph(string producerIdentity, string registrationIdentity, bool fanout)
    {
        var view = new ArgView()
            .Method(Producer)
            .Method(Register)
            .Method(Handler)
            .Reference(Producer, Tell, RefKinds.Invocation, 10, firstArgumentName: producerIdentity, targetInSource: false)
            .Reference(Register, Spawn, RefKinds.Invocation, 20, firstArgumentName: registrationIdentity, targetInSource: false)
            .Reference(Register, Handler, RefKinds.MethodGroup, 20, delegateConsumer: Spawn);
        if (fanout)
        {
            view.Method(RegisterTwo)
                .Method(HandlerTwo)
                .Reference(RegisterTwo, Spawn, RefKinds.Invocation, 30, firstArgumentName: registrationIdentity, targetInSource: false)
                .Reference(RegisterTwo, HandlerTwo, RefKinds.MethodGroup, 30, delegateConsumer: Spawn);
        }
        return view;
    }

    private sealed class ArgView : IFactGraphView
    {
        private readonly List<ReferenceFact> references = [];
        private readonly List<SymbolFact> symbols = [];

        internal HashSet<string> RequestedMethodKeys { get; } = new(StringComparer.Ordinal);

        internal ArgView Method(string id)
        {
            var name = id[(id.LastIndexOf('.') + 1)..];
            symbols.Add(
                new SymbolFact(id, SymbolKinds.Method, name, "N", "T:N.Actor", "public", "", name + "()", "/code.cs", 1, 1, "App", false)
            );
            return this;
        }

        internal ArgView Reference(
            string caller,
            string target,
            string kind,
            int line,
            string? firstArgumentName = null,
            string? delegateConsumer = null,
            bool targetInSource = true
        )
        {
            references.Add(
                new ReferenceFact(
                    target,
                    kind,
                    caller,
                    "App",
                    targetInSource,
                    "/code.cs",
                    line,
                    FirstArgumentName: firstArgumentName,
                    DelegateConsumer: delegateConsumer
                )
            );
            return this;
        }

        public IReadOnlyList<ReferenceFact> ReferencesFrom(string enclosingSymbolId) =>
            references.Where(row => row.EnclosingSymbolId == enclosingSymbolId).ToArray();

        public IReadOnlyList<ReferenceFact> ReferencesTo(string targetSymbolId) =>
            references.Where(row => row.TargetSymbolId == targetSymbolId).ToArray();

        public IReadOnlyList<ReferenceFact> ReferencesToMethodKey(string methodKey)
        {
            RequestedMethodKeys.Add(methodKey);
            return references.Where(row => ReferenceTargetMethodKey.Normalize(row.TargetSymbolId) == methodKey).ToArray();
        }

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

        public IReadOnlyList<DispatchFact> DispatchFrom(string sourceMember) => [];

        public IReadOnlyList<DispatchFact> DispatchTo(string targetMember) => [];
    }
}
