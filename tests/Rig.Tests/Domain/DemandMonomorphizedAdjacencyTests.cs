using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class DemandMonomorphizedAdjacencyTests
{
    [Test]
    public void Method_generic_adjacency_substitutes_the_receiver_and_matches_shape_graph()
    {
        var graph = new DemandTestGraph()
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method("M:N.Repo.G", "G<T>()", "T:N.Repo")
            .Call("M:N.Root.Run", "M:N.Repo.G", methodBinding: "[\"C:N.Cat\"]")
            .Call("M:N.Repo.G", "M:N.IAnimal.Act", receiver: "T");
        var source = graph.Source();

        var rootEdge = source.CallsFrom("M:N.Root.Run").Single();
        MonomorphizedNodeId.IsMonomorphized(rootEdge.Callee).ShouldBeTrue();
        var body = source.CallsFrom(rootEdge.Callee).Single();
        body.ReceiverType.ShouldBe("N.Cat");

        var full = FactPathFinder.ShapeGraph(graph.AsFactGraph(), [], [], [], graph.Signatures);
        full.CallEdges.Single(edge => edge.Caller == rootEdge.Callee).ReceiverType.ShouldBe(body.ReceiverType);
    }

    [Test]
    public void Declaring_type_generic_adjacency_substitutes_the_receiver()
    {
        var graph = new DemandTestGraph()
            .Type("T:N.Repo", "Repo<T>")
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method("M:N.Repo.G", "G()", "T:N.Repo")
            .Call("M:N.Root.Run", "M:N.Repo.G", declaringBinding: "[\"C:N.Cat\"]")
            .Call("M:N.Repo.G", "M:N.IAnimal.Act", receiver: "T");
        var source = graph.Source();

        var mono = source.CallsFrom("M:N.Root.Run").Single().Callee;

        source.CallsFrom(mono).Single().ReceiverType.ShouldBe("N.Cat");
    }

    [Test]
    public void Forwarded_binding_and_lambda_closure_are_redirected_on_demand()
    {
        const string outer = "M:N.Repo.Outer";
        const string inner = "M:N.Repo.Inner";
        const string lambda = outer + "~λ0";
        var graph = new DemandTestGraph()
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method(outer, "Outer<T>()", "T:N.Repo")
            .Method(inner, "Inner<U>()", "T:N.Repo")
            .Method(lambda, "lambda", outer)
            .Call("M:N.Root.Run", outer, methodBinding: "[\"C:N.Cat\"]")
            .Call(outer, lambda, kind: RefKinds.MethodGroup)
            .Call(lambda, inner, methodBinding: "[\"M:0\"]")
            .Call(inner, "M:N.IAnimal.Act", receiver: "U");
        var source = graph.Source();

        var monoOuter = source.CallsFrom("M:N.Root.Run").Single().Callee;
        var monoLambda = source.CallsFrom(monoOuter).Single().Callee;
        var monoInner = source.CallsFrom(monoLambda).Single().Callee;

        MonomorphizedNodeId.BaseOf(monoLambda).ShouldBe(lambda);
        MonomorphizedNodeId.BaseOf(monoInner).ShouldBe(inner);
        source.CallsFrom(monoInner).Single().ReceiverType.ShouldBe("N.Cat");
    }

    [Test]
    public void Generic_factory_rewrite_runs_before_monomorphization_and_uses_canonical_overload()
    {
        var graph = new DemandTestGraph()
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method("M:N.Entity.New``2(``1)", "New<T, P>(P)", "T:N.Entity")
            // Same SymbolId, conflicting raw metadata: deterministic /a.cs must win. A first-row (/z.cs)
            // projection names it Other and therefore cannot include it as the factory's New candidate.
            .MethodNamed("M:N.Cat.New(System.Int32)", "Other", "New(int)", "T:N.Cat", file: "/z.cs")
            .MethodNamed("M:N.Cat.New(System.Int32)", "New", "New(int)", "T:N.Cat", file: "/a.cs")
            .Call("M:N.Root.Run", "M:N.Entity.New``2(``1)", typeArguments: "N.Cat,int", methodBinding: "[\"C:N.Cat\",\"C:int\"]");
        var rules = new ForwardCallProjectionRules(
            Factory: [new FactGenericFactoryRule("N.Entity.New", ConstructArgIndex: 0, TargetMethod: "New")]
        );
        var source = graph.Source(rules);

        var edge = source.CallsFrom("M:N.Root.Run").Single();

        edge.Callee.ShouldBe("M:N.Cat.New(System.Int32)");
        MonomorphizedNodeId.IsMonomorphized(edge.Callee).ShouldBeFalse();
        source.DiagnosticsSnapshot().Reads.ContainingMethods.Rows.ShouldBe(2);
    }

    [Test]
    public void Missing_signature_or_unresolved_binding_keeps_the_base_edge()
    {
        var graph = new DemandTestGraph()
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method("M:N.Repo.Missing", "", "T:N.Repo")
            .Method("M:N.Repo.Forwarded", "Forwarded<T>()", "T:N.Repo")
            .Call("M:N.Root.Run", "M:N.Repo.Missing", methodBinding: "[\"C:N.Cat\"]", line: 1)
            .Call("M:N.Root.Run", "M:N.Repo.Forwarded", methodBinding: "[\"M:0\"]", line: 2);
        var source = graph.Source();

        var callees = source.CallsFrom("M:N.Root.Run").Select(edge => edge.Callee).ToArray();

        callees.ShouldBe(["M:N.Repo.Missing", "M:N.Repo.Forwarded"]);
    }

    [Test]
    public void Repeated_caller_is_cached_and_uncharged()
    {
        var graph = new DemandTestGraph().Method("M:N.Root.Run", "Run()", "T:N.Root").Call("M:N.Root.Run", "M:N.Leaf.Run");
        var source = graph.Source();

        var first = source.CallsFrom("M:N.Root.Run");
        var before = source.DiagnosticsSnapshot();
        var second = source.CallsFrom("M:N.Root.Run");
        var after = source.DiagnosticsSnapshot();

        second.ShouldBeSameAs(first);
        after.ShouldBe(before);
    }

    [Test]
    public void Per_method_cap_keeps_the_rejected_base_edge_and_discloses_exactly()
    {
        var graph = new DemandTestGraph().Method("M:N.G", "G<T>()", "T:N.Repo");
        for (var i = 0; i < 3; i++)
        {
            graph.Method($"M:N.Root{i}", "Run()", $"T:N.Root{i}").Call($"M:N.Root{i}", "M:N.G", methodBinding: $"[\"C:N.T{i}\"]");
        }
        var source = graph.Source(limits: new DemandMonomorphizationLimits(MaxInstantiationsPerMethod: 2, MaxWorkUnits: 1000));

        MonomorphizedNodeId.IsMonomorphized(source.CallsFrom("M:N.Root0").Single().Callee).ShouldBeTrue();
        MonomorphizedNodeId.IsMonomorphized(source.CallsFrom("M:N.Root1").Single().Callee).ShouldBeTrue();
        source.CallsFrom("M:N.Root2").Single().Callee.ShouldBe("M:N.G");
        var diagnostics = source.DiagnosticsSnapshot();
        diagnostics.Precision.PerMethodFallbackEdges.ShouldBe(1);
        diagnostics.Precision.CappedMethodIds.ShouldBe(["M:N.G"]);
        diagnostics.Precision.DistinctInstantiations.ShouldBe(2);
    }

    [Test]
    public void Work_budget_rejection_keeps_base_edges_and_reports_atomic_overshoot()
    {
        var graph = new DemandTestGraph()
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method("M:N.G", "G<T>()", "T:N.Repo")
            .Call("M:N.Root.Run", "M:N.G", methodBinding: "[\"C:N.Cat\"]")
            .Call("M:N.Root.Run", "M:N.Leaf1", line: 2)
            .Call("M:N.Root.Run", "M:N.Leaf2", line: 3);
        var source = graph.Source(limits: new DemandMonomorphizationLimits(MaxInstantiationsPerMethod: 50, MaxWorkUnits: 2));

        source.CallsFrom("M:N.Root.Run").Select(edge => edge.Callee).ShouldContain("M:N.G");
        var diagnostics = source.DiagnosticsSnapshot();
        diagnostics.Budget.Exceeded.ShouldBeTrue();
        diagnostics.Budget.AtomicOvershoot.ShouldBe(1);
        diagnostics.Precision.BudgetFallbackEdges.ShouldBe(1);
    }

    [Test]
    public void Budget_exhaustion_never_skips_the_semantic_factory_bridge()
    {
        var graph = new DemandTestGraph()
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method("M:N.Entity.New``2(``1)", "New<T, P>(P)", "T:N.Entity")
            .Method("M:N.Cat.New(System.Int32)", "New(int)", "T:N.Cat")
            .Call("M:N.Root.Run", "M:N.Entity.New``2(``1)", typeArguments: "N.Cat,int");
        var source = graph.Source(
            new ForwardCallProjectionRules(
                Factory: [new FactGenericFactoryRule("N.Entity.New", ConstructArgIndex: 0, TargetMethod: "New")]
            ),
            new DemandMonomorphizationLimits(MaxInstantiationsPerMethod: 50, MaxWorkUnits: 1)
        );

        source.CallsFrom("M:N.Root.Run").Single().Callee.ShouldBe("M:N.Cat.New(System.Int32)");
        var diagnostics = source.DiagnosticsSnapshot();
        diagnostics.Budget.Exceeded.ShouldBeTrue();
        diagnostics.Budget.AtomicOvershoot.ShouldBeGreaterThan(0);
        diagnostics.Reads.ContainingMethods.ShouldBe(new DemandReadMetric(1, 1));
    }

    [Test]
    public void Generic_construct_type_resolves_its_open_arity_partition_with_full_graph_parity()
    {
        var graph = new DemandTestGraph()
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method("M:N.Entity.New``1", "New<T>()", "T:N.Entity")
            .Method("M:N.Widget`1.New", "New()", "T:N.Widget`1")
            .Call("M:N.Root.Run", "M:N.Entity.New``1", typeArguments: "N.Widget<N.Cat>");
        var rule = new FactGenericFactoryRule("N.Entity.New", ConstructArgIndex: 0, TargetMethod: "New");
        var source = graph.Source(new ForwardCallProjectionRules(Factory: [rule]));

        var demand = source.CallsFrom("M:N.Root.Run").Single();
        var whole = FactPathFinder.RewriteGenericFactories(graph.AsFactGraph(), [rule]).CallEdges.Single();

        demand.Callee.ShouldBe("M:N.Widget`1.New");
        demand.ShouldBe(whole);
        source.DiagnosticsSnapshot().Reads.ContainingMethods.ShouldBe(new DemandReadMetric(1, 1));
    }

    [Test]
    [Arguments("N.Outer<N.A>.Inner<N.B>", "T:N.Outer`1.Inner`1")]
    [Arguments("N.Widget<(int,string)>", "T:N.Widget`1")]
    [Arguments("N.Outer`1{N.A}.Inner`1{N.B}", "T:N.Outer`1.Inner`1")]
    public void Construct_type_parser_is_segment_and_delimiter_aware_with_demand_whole_parity(string construct, string openType)
    {
        var target = "M:" + openType[2..] + ".New";
        var graph = new DemandTestGraph()
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method("M:N.Entity.New``1", "New<T>()", "T:N.Entity")
            .Method(target, "New()", openType)
            .Call("M:N.Root.Run", "M:N.Entity.New``1", typeArguments: construct);
        var rule = new FactGenericFactoryRule("N.Entity.New", ConstructArgIndex: 0, TargetMethod: "New");

        var demand = graph.Source(new ForwardCallProjectionRules(Factory: [rule])).CallsFrom("M:N.Root.Run").Single();
        var whole = FactPathFinder.RewriteGenericFactories(graph.AsFactGraph(), [rule]).CallEdges.Single();

        FactPathFinder.FactoryConstructTypeId(construct).ShouldBe(openType);
        demand.Callee.ShouldBe(target);
        demand.ShouldBe(whole);
    }

    [Test]
    [Arguments("N.Widget<N.A")]
    [Arguments("N.Widget<N.A>>")]
    [Arguments("N.Widget<,>")]
    [Arguments("N.Outer<N.A}.Inner<N.B>")]
    public void Malformed_construct_type_fails_closed_to_the_factory_plumbing(string construct)
    {
        var graph = new DemandTestGraph()
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method("M:N.Entity.New``1", "New<T>()", "T:N.Entity")
            .Method("M:N.Widget`1.New", "New()", "T:N.Widget`1")
            .Call("M:N.Root.Run", "M:N.Entity.New``1", typeArguments: construct);
        var rule = new FactGenericFactoryRule("N.Entity.New", ConstructArgIndex: 0, TargetMethod: "New");

        var demand = graph.Source(new ForwardCallProjectionRules(Factory: [rule])).CallsFrom("M:N.Root.Run").Single();
        var whole = FactPathFinder.RewriteGenericFactories(graph.AsFactGraph(), [rule]).CallEdges.Single();

        FactPathFinder.FactoryConstructTypeId(construct).ShouldBeNull();
        demand.Callee.ShouldBe("M:N.Entity.New``1");
        demand.ShouldBe(whole);
    }

    [Test]
    public void Monomorphized_id_parser_round_trips_complex_bindings_and_rejects_malformed_ids()
    {
        var id = MonomorphizedNodeId.For("M:N.Repo.G", ["N.Dictionary{System.String,N.Tuple{N.A,N.B}}", "N.Outer+Inner[]"], []);

        MonomorphizedNodeId.TryParse(id, out var parsed).ShouldBeTrue();
        parsed.BaseMethodId.ShouldBe("M:N.Repo.G");
        parsed.DeclaringBinding.ShouldBe(["N.Dictionary{System.String,N.Tuple{N.A,N.B}}", "N.Outer+Inner[]"]);
        parsed.MethodBinding.ShouldBeEmpty();

        var separator = '\u001f';
        var malformed = new[]
        {
            "~mono⟨;N.A⟩",
            "M:N.G~mono⟨;⟩",
            $"M:N.G~mono⟨N.A{separator};N.B⟩",
            $"M:N.G~mono⟨N.A{separator}{separator}N.B;⟩",
            "M:N.G~mono⟨N.A;;N.B⟩",
            "M:N.G~mono⟨N.A;N.B",
            "M:N.G~mono[N.A;N.B]",
        };
        foreach (var candidate in malformed)
        {
            MonomorphizedNodeId.TryParse(candidate, out _).ShouldBeFalse(candidate);
        }
    }

    [Test]
    public void Demand_closure_composes_with_one_hop_dispatch_and_collapses_to_base_graph_parity()
    {
        const string root = "M:N.Root.Run";
        const string generic = "M:N.Repo.Start";
        const string contract = "M:N.ILogger.Startup";
        const string inherited = "M:N.ServiceBase.Startup";
        var facts = new DemandTestGraph()
            .Method(root, "Run()", "T:N.Root")
            .Method(generic, "Start<T>()", "T:N.Repo")
            .Method(contract, "Startup()", "T:N.ILogger")
            .Method(inherited, "Startup()", "T:N.ServiceBase")
            .Method("M:N.SvcA.Startup", "Startup()", "T:N.SvcA")
            .Method("M:N.SvcB.Startup", "Startup()", "T:N.SvcB")
            .Call(root, generic, methodBinding: "[\"C:N.Impl\"]")
            .Call(generic, contract, receiver: "T")
            .Implements("T:N.Impl", "T:N.ILogger")
            .Base("T:N.Impl", "T:N.ServiceBase")
            .Base("T:N.SvcA", "T:N.ServiceBase")
            .Base("T:N.SvcB", "T:N.ServiceBase")
            .Dispatch(contract, inherited, DispatchKinds.Impl)
            .Dispatch(inherited, "M:N.SvcA.Startup", DispatchKinds.Override)
            .Dispatch(inherited, "M:N.SvcB.Startup", DispatchKinds.Override);
        var source = facts.Source();
        var closure = DemandClosure(source, root);
        var demandGraph = facts.AsFactGraph() with { CallEdges = closure };

        var demandReach = FactPathFinder.Reaches(demandGraph, root);
        demandReach.Keys.ShouldContain(inherited);
        demandReach.Keys.ShouldNotContain("M:N.SvcA.Startup");
        demandReach.Keys.ShouldNotContain("M:N.SvcB.Startup");

        var collapsedReach = MonomorphCollapse.CollapseDepthMap(demandReach);
        collapsedReach.Keys.ShouldContain(generic);
        collapsedReach.Keys.ShouldNotContain(key => MonomorphizedNodeId.IsMonomorphized(key));
        var collapsedEdges = closure
            .Select(edge =>
                edge with
                {
                    Caller = MonomorphizedNodeId.BaseOf(edge.Caller),
                    Callee = MonomorphizedNodeId.BaseOf(edge.Callee),
                }
            )
            .Distinct()
            .ToArray();
        var baseReach = FactPathFinder.Reaches(demandGraph with { CallEdges = collapsedEdges }, root);
        collapsedReach.Keys.ShouldBe(baseReach.Keys, ignoreOrder: true);

        var path = FactPathFinder.Find(demandGraph, root, inherited)!;
        MonomorphCollapse.CollapsePath(path).Select(step => step.SymbolId).ShouldContain(generic);
    }

    private static IReadOnlyList<CallEdge> DemandClosure(IForwardCallSource source, string root)
    {
        var edges = new List<CallEdge>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var caller = pending.Dequeue();
            if (!seen.Add(caller))
            {
                continue;
            }

            foreach (var edge in source.CallsFrom(caller))
            {
                edges.Add(edge);
                pending.Enqueue(edge.Callee);
            }
        }

        return edges;
    }
}

internal sealed class DemandTestGraph : IFactGraphView
{
    private readonly List<ReferenceFact> references = [];
    private readonly List<SymbolFact> symbols = [];
    private readonly List<ImplementsEdge> implementations = [];
    private readonly List<BaseEdge> bases = [];
    private readonly List<DispatchFact> dispatch = [];

    public List<string> RequestedCallers { get; } = [];
    public IReadOnlyDictionary<string, string> Signatures =>
        symbols
            .GroupBy(symbol => symbol.SymbolId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(symbol => symbol.FilePath, StringComparer.Ordinal).First().Signature);

    public DemandTestGraph Type(string id, string signature, string file = "/types.cs")
    {
        symbols.Add(Symbol(id, SymbolKinds.Type, id[(id.LastIndexOf('.') + 1)..], null, signature, file));
        return this;
    }

    public DemandTestGraph Method(string id, string signature, string containing, string file = "/methods.cs")
    {
        return MethodNamed(id, MethodName(id), signature, containing, file);
    }

    public DemandTestGraph MethodNamed(string id, string name, string signature, string containing, string file = "/methods.cs")
    {
        symbols.Add(Symbol(id, SymbolKinds.Method, name, containing, signature, file));
        return this;
    }

    public DemandTestGraph Call(
        string caller,
        string callee,
        string kind = RefKinds.Invocation,
        string? receiver = null,
        string? typeArguments = null,
        string? declaringBinding = null,
        string? methodBinding = null,
        int line = 1
    )
    {
        references.Add(
            new ReferenceFact(
                callee,
                kind,
                caller,
                "App",
                true,
                "/calls.cs",
                line,
                ReceiverType: receiver,
                TypeArguments: typeArguments,
                DeclaringTypeArgBinding: declaringBinding,
                MethodTypeArgBinding: methodBinding
            )
        );
        return this;
    }

    public DemandTestGraph Implements(string type, string contract)
    {
        implementations.Add(new ImplementsEdge(type, contract));
        return this;
    }

    public DemandTestGraph Base(string type, string baseType)
    {
        bases.Add(new BaseEdge(type, baseType));
        return this;
    }

    public DemandTestGraph Dispatch(string source, string target, string kind)
    {
        dispatch.Add(new DispatchFact(source, target, kind));
        return this;
    }

    public DemandMonomorphizedCallSource Source(ForwardCallProjectionRules? rules = null, DemandMonomorphizationLimits? limits = null) =>
        new(this, rules, limits);

    public FactGraphData AsFactGraph() =>
        new(
            references.Select(reference => CallEdgeProjection.Project(reference)).ToArray(),
            implementations,
            SymbolFactProjections.SelectCanonicalMethodFacts(symbols).Select(SymbolFactProjections.ToMethodRef).ToArray(),
            bases,
            dispatch
        );

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
        symbols
            .Where(symbol => symbol.Kind == SymbolKinds.Method)
            .Select(symbol => symbol.SymbolId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<SymbolFact> MethodsById(string symbolId) =>
        symbols.Where(symbol => symbol.Kind == SymbolKinds.Method && symbol.SymbolId == symbolId).ToArray();

    public IReadOnlyList<SymbolFact> MethodsByContainingSymbol(string containingSymbolId) =>
        symbols.Where(symbol => symbol.Kind == SymbolKinds.Method && symbol.ContainingSymbolId == containingSymbolId).ToArray();

    public IReadOnlyList<TypeRelationFact> TypeRelationsFrom(string typeSymbolId) => [];

    public IReadOnlyList<TypeRelationFact> TypeRelationsTo(string relatedSymbolId) => [];

    public IReadOnlyList<DispatchFact> DispatchFrom(string sourceMember) =>
        dispatch.Where(fact => fact.SourceMember == sourceMember).ToArray();

    public IReadOnlyList<DispatchFact> DispatchTo(string targetMember) =>
        dispatch.Where(fact => fact.TargetMember == targetMember).ToArray();

    private static SymbolFact Symbol(string id, string kind, string name, string? containing, string signature, string file) =>
        new(id, kind, name, "N", containing, "public", kind == SymbolKinds.Type ? "class" : "", signature, file, 1, 1, "App", false);

    private static string MethodName(string id)
    {
        var parameters = id.IndexOf('(');
        var head = parameters < 0 ? id : id[..parameters];
        var start = head.LastIndexOf('.') + 1;
        var end = id.IndexOfAny(['`', '('], start);
        return end < 0 ? id[start..] : id.Substring(start, end - start);
    }
}
