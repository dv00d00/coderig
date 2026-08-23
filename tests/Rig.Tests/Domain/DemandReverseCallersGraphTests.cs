using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class DemandReverseCallersGraphTests
{
    [Test]
    public void Direct_reverse_closure_matches_full_graph_and_does_not_read_disconnected_callers()
    {
        var view = new SpyView()
            .Method("M:N.Root.A", "A()", "T:N.Root")
            .Method("M:N.Mid.B", "B()", "T:N.Mid")
            .Method("M:N.Target.C", "C()", "T:N.Target")
            .Method("M:N.Noise.D", "D()", "T:N.Noise")
            .Method("M:N.Noise.E", "E()", "T:N.Noise")
            .Call("M:N.Root.A", "M:N.Mid.B")
            .Call("M:N.Mid.B", "M:N.Target.C")
            .Call("M:N.Noise.D", "M:N.Noise.E");
        var rules = Rules();
        var full = view.FullGraph(rules);
        view.ClearReads();

        var result = Build(view, rules, "M:N.Target.C");

        FactPathFinder.ReachedBy(result.Graph, "M:N.Target.C").ShouldBe(FactPathFinder.ReachedBy(full, "M:N.Target.C"));
        FactPathFinder.EntryRootsReaching(result.Graph, "M:N.Target.C").ShouldBe(FactPathFinder.EntryRootsReaching(full, "M:N.Target.C"));
        FactPathFinder
            .SeedsReachTarget(
                result.Graph,
                [
                    ["M:N.Root.A"],
                ],
                ["M:N.Target.C"],
                int.MaxValue,
                FactPathFinder.TraversalMode.SyncCut
            )
            .ShouldBe(
                FactPathFinder.SeedsReachTarget(
                    full,
                    [
                        ["M:N.Root.A"],
                    ],
                    ["M:N.Target.C"],
                    int.MaxValue,
                    FactPathFinder.TraversalMode.SyncCut
                )
            );
        view.RequestedCallers.ShouldNotContain("M:N.Noise.D");
        result.Diagnostics.Reverse.ReferencesTo.Calls.ShouldBeGreaterThan(0);
        result.Diagnostics.Closure.MaterializedCallerPartitions.ShouldBe(2);
    }

    [Test]
    public void Mined_and_heuristic_dispatch_match_and_forward_confirmation_rejects_a_receiver_sibling()
    {
        const string contract = "M:N.IWork.Run";
        const string target = "M:N.WorkA.Run";
        var view = new SpyView()
            .Method(contract, "Run()", "T:N.IWork")
            .Method(target, "Run()", "T:N.WorkA", isOverride: true)
            .Method("M:N.WorkB.Run", "Run()", "T:N.WorkB", isOverride: true)
            .Method("M:N.Good.Start", "Start()", "T:N.Good")
            .Method("M:N.Bad.Start", "Start()", "T:N.Bad")
            .Relation("T:N.WorkA", "T:N.IWork", RelationKinds.Interface)
            .Relation("T:N.WorkB", "T:N.IWork", RelationKinds.Interface)
            .Dispatch(contract, target, DispatchKinds.Impl)
            .Dispatch(contract, "M:N.WorkB.Run", DispatchKinds.Impl)
            .Call("M:N.Good.Start", contract, receiver: "N.WorkA")
            .Call("M:N.Bad.Start", contract, receiver: "N.WorkB");
        var rules = Rules();
        var full = view.FullGraph(rules);
        view.ClearReads();

        var result = Build(view, rules, target);

        FactPathFinder.ReachedBy(result.Graph, target).ShouldBe(FactPathFinder.ReachedBy(full, target));
        FactPathFinder.ReachedBy(result.Graph, target).Keys.ShouldContain("M:N.Good.Start");
        FactPathFinder.ReachedBy(result.Graph, target).Keys.ShouldNotContain("M:N.Bad.Start");
        FactPathFinder
            .SeedsReachTarget(
                result.Graph,
                [
                    ["M:N.Good.Start"],
                    ["M:N.Bad.Start"],
                ],
                [target],
                int.MaxValue,
                FactPathFinder.TraversalMode.SyncCut
            )
            .ShouldBe([true, false]);

        var heuristic = new SpyView()
            .Method(contract, "Run()", "T:N.IWork")
            .Method(target, "Run()", "T:N.WorkA", isOverride: true)
            .Method("M:N.Good.Start", "Start()", "T:N.Good")
            .Relation("T:N.WorkA", "T:N.IWork", RelationKinds.Interface)
            .Call("M:N.Good.Start", contract, receiver: "N.WorkA");
        var heuristicFull = heuristic.FullGraph(rules);
        heuristic.ClearReads();
        var heuristicResult = Build(heuristic, rules, target);
        FactPathFinder.ReachedBy(heuristicResult.Graph, target).ShouldBe(FactPathFinder.ReachedBy(heuristicFull, target));
    }

    [Test]
    public void Delegate_field_redirect_and_factory_reverse_partitions_match_full_projection()
    {
        const string callable = "M:N.Handler.Run";
        const string slot = "F:N.State.Callback";
        const string redirectTarget = "M:N.Base.SaveCore";
        const string factoryTarget = "M:N.Account.New(System.Int32)";
        var rules = Rules(
            redirect: [new FactRedirectRule("M:External.Base.Save", redirectTarget)],
            factory: [new FactGenericFactoryRule("N.Entity.New", 0, "New")]
        );
        var view = new SpyView()
            .Method(callable, "Run()", "T:N.Handler")
            .Method("M:N.Invoke.Go", "Go()", "T:N.Invoke")
            .Method("M:N.Redirect.Go", "Go()", "T:N.Redirect")
            .Method(redirectTarget, "SaveCore()", "T:N.Base")
            .Method("M:N.Factory.Go", "Go()", "T:N.Factory")
            .Method("M:N.Entity.New``3(``1)", "New<T1,T2,T3>(T2)", "T:N.Entity")
            .Method(factoryTarget, "New(int)", "T:N.Account")
            .Dispatch(slot, callable, DispatchKinds.DelegateFieldBind)
            .Dispatch(slot, "M:N.Invoke.Go", DispatchKinds.DelegateFieldInvoke)
            .Call("M:N.Redirect.Go", "M:External.Base.Save(System.Boolean)", targetInSource: false)
            .Call("M:N.Factory.Go", "M:N.Entity.New``3(``1)", typeArguments: "N.Account,System.Int32,N.Other");
        var full = view.FullGraph(rules);
        view.ClearReads();

        foreach (var target in new[] { callable, redirectTarget, factoryTarget })
        {
            var result = Build(view, rules, target);
            FactPathFinder.ReachedBy(result.Graph, target).ShouldBe(FactPathFinder.ReachedBy(full, target));
        }
        FactPathFinder.ReachedBy(Build(view, rules, factoryTarget).Graph, factoryTarget).Keys.ShouldContain("M:N.Factory.Go");
        FactPathFinder.ReachedBy(Build(view, rules, redirectTarget).Graph, redirectTarget).Keys.ShouldContain("M:N.Redirect.Go");
        view.RequestedMethodKeys.ShouldContain(ReferenceTargetMethodKey.Normalize("N.Entity.New"));
        view.RequestedMethodKeys.ShouldContain(ReferenceTargetMethodKey.Normalize("M:External.Base.Save"));

        var raw = Rules(redirect: [new FactRedirectRule("M:External.Base.Save", redirectTarget)]);
        var rawGraph = Build(view, raw, redirectTarget).Graph;
        FactPathFinder.ReachedBy(rawGraph, redirectTarget).Keys.ShouldContain("M:N.Redirect.Go");
        var rawFactoryGraph = Build(view, raw, factoryTarget).Graph;
        FactPathFinder.ReachedBy(rawFactoryGraph, factoryTarget).Keys.ShouldNotContain("M:N.Factory.Go");
    }

    [Test]
    public void Redirect_family_is_inverted_for_an_external_hatch_discovered_through_mined_dispatch()
    {
        const string hatch = "M:External.EntityBase.Save(External.IPredicate,System.Boolean)";
        const string target = "M:N.CommonEntityBase.Save(External.IPredicate,System.Boolean)";
        var rules = Rules(redirect: [new FactRedirectRule("M:External.EntityBase.Save", hatch)]);
        var view = new SpyView()
            .Method(hatch, "Save(IPredicate,bool)", "T:External.EntityBase")
            .Method(target, "Save(IPredicate,bool)", "T:N.CommonEntityBase", isOverride: true)
            .Method("M:N.Caller.Go", "Go()", "T:N.Caller")
            .Dispatch(hatch, target, DispatchKinds.Override)
            .Call("M:N.Caller.Go", "M:External.EntityBase.Save()", targetInSource: false);
        var full = view.FullGraph(rules);

        var result = Build(view, rules, target);

        FactPathFinder.ReachedBy(result.Graph, target).ShouldBe(FactPathFinder.ReachedBy(full, target));
        FactPathFinder.ReachedBy(result.Graph, target).Keys.ShouldContain("M:N.Caller.Go");
        result.Ownership.SymbolIds.ShouldContain("M:External.EntityBase.Save()");
    }

    [Test]
    public void Error_interface_reverse_recovery_finds_the_resolved_hub_even_when_it_has_mined_facts()
    {
        const string hub = "M:N.IFoo.Run";
        const string target = "M:N.PartialFoo.Run";
        var view = new SpyView()
            .Method(hub, "Run()", "T:N.IFoo")
            .Method(target, "Run()", "T:N.PartialFoo", isOverride: true)
            .Method("M:N.OtherFoo.Run", "Run()", "T:N.OtherFoo", isOverride: true)
            .Method("M:N.Root.Go", "Go()", "T:N.Root")
            .Relation("T:N.PartialFoo", "!:IFoo", RelationKinds.Interface)
            .Dispatch(hub, "M:N.OtherFoo.Run", DispatchKinds.Impl)
            .Call("M:N.Root.Go", hub, receiver: "N.PartialFoo");
        var rules = Rules();
        var full = view.FullGraph(rules);
        view.ClearReads();

        var result = Build(view, rules, target);

        FactPathFinder.ReachedBy(result.Graph, target).ShouldBe(FactPathFinder.ReachedBy(full, target));
        FactPathFinder.ReachedBy(result.Graph, target).Keys.ShouldContain("M:N.Root.Go");
    }

    [Test]
    public void Escaped_delegate_field_suppresses_the_synthetic_caller()
    {
        const string slot = "F:N.State.Callback";
        const string callable = "M:N.Handler.Run";
        var view = new SpyView()
            .Method(callable, "Run()", "T:N.Handler")
            .Method("M:N.Invoke.Go", "Go()", "T:N.Invoke")
            .Dispatch(slot, callable, DispatchKinds.DelegateFieldBind)
            .Dispatch(slot, "M:N.Invoke.Go", DispatchKinds.DelegateFieldInvoke)
            .Dispatch(slot, slot, DispatchKinds.DelegateFieldEscape);

        var result = Build(view, Rules(), callable);

        FactPathFinder.ReachedBy(result.Graph, callable).Keys.ShouldNotContain("M:N.Invoke.Go");
        result.Graph.CallEdges.ShouldNotContain(edge => edge.Kind == EdgeKinds.DelegateField);
    }

    [Test]
    public void Nonvirtual_base_call_reaches_the_base_body_but_not_a_sibling_override()
    {
        const string baseMethod = "M:N.Base.Run";
        const string sibling = "M:N.Sibling.Run";
        var view = new SpyView()
            .Method(baseMethod, "Run()", "T:N.Base")
            .Method(sibling, "Run()", "T:N.Sibling", isOverride: true)
            .Method("M:N.Root.Go", "Go()", "T:N.Root")
            .Relation("T:N.Sibling", "T:N.Base", RelationKinds.Base)
            .Call("M:N.Root.Go", baseMethod, receiver: "N.Sibling", nonVirtual: true);

        var siblingResult = Build(view, Rules(), sibling);
        var baseResult = Build(view, Rules(), baseMethod);

        FactPathFinder.ReachedBy(siblingResult.Graph, sibling).Keys.ShouldNotContain("M:N.Root.Go");
        FactPathFinder.ReachedBy(baseResult.Graph, baseMethod).Keys.ShouldContain("M:N.Root.Go");
    }

    [Test]
    public void Same_kind_dispatch_stops_an_inherited_impl_before_unrelated_overrides_under_a_tight_cap()
    {
        const string contract = "M:N.ILogger.Startup";
        const string inherited = "M:N.ServiceBase.Startup";
        var view = new SpyView()
            .Method(contract, "Startup()", "T:N.ILogger")
            .Method(inherited, "Startup()", "T:N.ServiceBase")
            .Method("M:N.Logger.Go", "Go()", "T:N.Logger")
            .Relation("T:N.Logger", "T:N.ILogger", RelationKinds.Interface)
            .Relation("T:N.Logger", "T:N.ServiceBase", RelationKinds.Base)
            .Dispatch(contract, inherited, DispatchKinds.Impl)
            .Call("M:N.Logger.Go", contract, receiver: "N.Logger");
        for (var i = 0; i < 20; i++)
        {
            view.Method($"M:N.Service{i}.Startup", "Startup()", $"T:N.Service{i}", isOverride: true)
                .Relation($"T:N.Service{i}", "T:N.ServiceBase", RelationKinds.Base)
                .Dispatch(inherited, $"M:N.Service{i}.Startup", DispatchKinds.Override);
        }

        var result = DemandReverseCallersGraph.Build(
            view,
            Rules(),
            new DemandReverseCallersGraphRequest(inherited, int.MaxValue, FactPathFinder.TraversalMode.SyncCut, MaxNodes: 8)
        );

        FactPathFinder.ReachedBy(result.Graph, inherited).Keys.ShouldContain("M:N.Logger.Go");
        result.Graph.Methods.ShouldNotContain(method => method.SymbolId.StartsWith("M:N.Service0.", StringComparison.Ordinal));
    }

    [Test]
    public void Admitted_monomorphized_callers_expand_their_cloned_body_and_keep_only_the_matching_instantiation()
    {
        const string generic = "M:N.Generic.Go";
        const string contract = "M:N.IWork.Run";
        const string target = "M:N.WorkA.Run";
        var view = new SpyView()
            .Method("M:N.Root.A", "A()", "T:N.Root")
            .Method("M:N.Root.B", "B()", "T:N.Root")
            .Method(generic, "Go<T>()", "T:N.Generic")
            .Method(contract, "Run()", "T:N.IWork")
            .Method(target, "Run()", "T:N.WorkA", isOverride: true)
            .Method("M:N.WorkB.Run", "Run()", "T:N.WorkB", isOverride: true)
            .Call("M:N.Root.A", generic, methodBinding: "[\"C:N.WorkA\"]")
            .Call("M:N.Root.B", generic, methodBinding: "[\"C:N.WorkB\"]")
            .Call(generic, contract, receiver: "T")
            .Relation("T:N.WorkA", "T:N.IWork", RelationKinds.Interface)
            .Relation("T:N.WorkB", "T:N.IWork", RelationKinds.Interface)
            .Dispatch(contract, target, DispatchKinds.Impl)
            .Dispatch(contract, "M:N.WorkB.Run", DispatchKinds.Impl);
        var rules = Rules();
        var full = view.FullGraph(rules);
        view.ClearReads();

        var result = Build(view, rules, target);
        var reached = FactPathFinder.ReachedBy(result.Graph, target);

        reached.ShouldBe(FactPathFinder.ReachedBy(full, target));
        reached.Keys.ShouldContain("M:N.Root.A");
        reached.Keys.ShouldNotContain("M:N.Root.B");
        result.Graph.CallEdges.ShouldContain(edge => MonomorphizedNodeId.IsMonomorphized(edge.Caller));
        view.RequestedReverseTargets.ShouldNotContain(targetId => MonomorphizedNodeId.IsMonomorphized(targetId));
    }

    [Test]
    public void Cut_depth_boundary_shell_missing_ambiguous_and_async_discovery_are_explicit()
    {
        var view = new SpyView()
            .Method("M:N.A", "A()", "T:N")
            .Method("M:N.B", "B()", "T:N")
            .Method("M:N.C", "C()", "T:N")
            .Method("M:N.Target.One", "One()", "T:N.Target")
            .Method("M:N.Other.One", "One()", "T:N.Other")
            .Call("M:N.A", "M:N.B")
            .Call("M:N.B", "M:N.C")
            .Call("M:N.C", "M:N.Target.One");
        var cutRules = Rules(cut: [new FactTraversalCutRule("M:N.B", "boundary")]);

        var bounded = Build(view, Rules(), "M:N.Target.One", maxDepth: 1);
        bounded.Graph.CallEdges.ShouldContain(edge => edge.Caller == "M:N.B" && edge.Callee == "M:N.C");
        var boundedReached = FactPathFinder.ReachedBy(bounded.Graph, "M:N.Target.One", maxDepth: 1);
        foreach (var boundary in boundedReached.Where(item => item.Value == 1).Select(item => item.Key))
        {
            bounded.Graph.CallEdges.ShouldContain(edge => edge.Callee == boundary, $"{boundary} needs its predecessor shell");
        }
        FactPathFinder
            .EntryRootsReaching(bounded.Graph, "M:N.Target.One", maxDepth: 1)
            .ShouldBe(FactPathFinder.EntryRootsReaching(view.FullGraph(Rules()), "M:N.Target.One", maxDepth: 1));
        var cut = Build(view, cutRules, "M:N.Target.One");
        FactPathFinder
            .ReachedBy(cut.Graph, "M:N.Target.One")
            .ShouldBe(FactPathFinder.ReachedBy(view.FullGraph(cutRules), "M:N.Target.One"));

        var missing = Build(view, Rules(), "Missing");
        missing.TargetIds.ShouldBeEmpty();
        missing.Graph.CallEdges.ShouldBeEmpty();
        view.ClearReads();
        var ambiguous = Build(view, Rules(), ".One");
        ambiguous.TargetIds.Length.ShouldBe(2);
        ambiguous.Diagnostics.Reverse.ReferencesTo.Calls.ShouldBe(view.RequestedReverseTargets.Distinct(StringComparer.Ordinal).Count());

        Should.Throw<DemandReverseCallersGraphUnavailableException>(() =>
            DemandReverseCallersGraph.Build(
                view,
                Rules(),
                new DemandReverseCallersGraphRequest("M:N.Target.One", int.MaxValue, FactPathFinder.TraversalMode.AsyncInclude)
            )
        );
        Should.Throw<DemandReverseCallersGraphUnavailableException>(() =>
            DemandReverseCallersGraph.Build(
                view,
                Rules(),
                new DemandReverseCallersGraphRequest("M:N.Target.One", int.MaxValue, FactPathFinder.TraversalMode.SyncCut, MaxNodes: 1)
            )
        );
        var asyncExact = Build(view, Rules(), "M:N.Target.One", discoveryMode: FactPathFinder.TraversalMode.AsyncExact);
        asyncExact.Diagnostics.DeliverySitesSynthesized.ShouldBeFalse();
    }

    [Test]
    public void Context_dispatch_and_raw_shape_match_the_full_graph()
    {
        const string contract = "M:N.IState.RegisterEvents";
        const string target = "M:N.S1.RegisterEvents";
        var view = new SpyView()
            .Method("M:N.Root.One", "One()", "T:N.Root")
            .Method("M:N.Root.Two", "Two()", "T:N.Root")
            .Method("M:N.C1.Init", "Init()", "T:N.C1")
            .Method("M:N.C2.Init", "Init()", "T:N.C2")
            .Method(contract, "RegisterEvents()", "T:N.IState")
            .Method(target, "RegisterEvents()", "T:N.S1", isOverride: true)
            .Method("M:N.S2.RegisterEvents", "RegisterEvents()", "T:N.S2", isOverride: true)
            .Call("M:N.Root.One", "M:N.C1.Init", receiver: "N.C1")
            .Call("M:N.Root.Two", "M:N.C2.Init", receiver: "N.C2")
            .Call("M:N.C1.Init", contract, receiver: "N.IState")
            .Call("M:N.C2.Init", contract, receiver: "N.IState")
            .Relation("T:N.S1", "T:N.IState", RelationKinds.Interface)
            .Relation("T:N.S2", "T:N.IState", RelationKinds.Interface)
            .Relation("T:N.S1", "T:N.StateBase{N.C1}", RelationKinds.Base)
            .Relation("T:N.S2", "T:N.StateBase{N.C2}", RelationKinds.Base);
        var context = Rules(context: [new FactContextDispatchRule("IState", "StateBase")]);
        var raw = Rules();

        foreach (var rules in new[] { context, raw })
        {
            var full = view.FullGraph(rules);
            view.ClearReads();
            var result = Build(view, rules, target);
            FactPathFinder.ReachedBy(result.Graph, target).ShouldBe(FactPathFinder.ReachedBy(full, target));
        }
        var contextGraph = Build(view, context, target).Graph;
        FactPathFinder
            .SeedsReachTarget(
                contextGraph,
                [
                    ["M:N.Root.One"],
                    ["M:N.Root.Two"],
                ],
                [target],
                int.MaxValue,
                FactPathFinder.TraversalMode.SyncCut
            )
            .ShouldBe([true, false]);
    }

    [Test]
    public void Ownership_hints_are_admitted_only_deterministic_and_carry_structural_emitters()
    {
        const string factoryTarget = "M:N.Account.New(System.Int32)";
        var factoryRules = Rules(factory: [new FactGenericFactoryRule("N.Entity.New", 0, "New")]);
        var factory = new SpyView()
            .Method("M:N.Accepted.Go", "Go()", "T:N.Accepted", filePath: "/accepted-method.cs")
            .Method("M:N.Noise.Go", "Go()", "T:N.Noise", filePath: "/noise-method.cs")
            .Method("M:N.Entity.New``3(``1)", "New<T1,T2,T3>(T2)", "T:N.Entity")
            .Method(factoryTarget, "New(int)", "T:N.Account", filePath: "/account.cs")
            .Method("M:N.Other.New(System.Int32)", "New(int)", "T:N.Other", filePath: "/other.cs")
            .Call("M:N.Accepted.Go", "M:N.Entity.New``3(``1)", typeArguments: "N.Account,System.Int32,N.X", filePath: "/accepted-call.cs")
            .Call("M:N.Noise.Go", "M:N.Entity.New``3(``1)", typeArguments: "N.Other,System.Int32,N.X", filePath: "/noise-call.cs");

        var first = Build(factory, factoryRules, factoryTarget);
        var second = Build(factory, factoryRules, factoryTarget);

        first.Ownership.SymbolIds.ToArray().ShouldBe(second.Ownership.SymbolIds.ToArray());
        first.Ownership.EmitterFilePaths.ToArray().ShouldBe(second.Ownership.EmitterFilePaths.ToArray());
        first
            .Ownership.SymbolIds.ToArray()
            .ShouldBe(first.Ownership.SymbolIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        first
            .Ownership.EmitterFilePaths.ToArray()
            .ShouldBe(
                first.Ownership.EmitterFilePaths.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray()
            );
        first.Ownership.EmitterFilePaths.ShouldContain("/accepted-call.cs");
        first.Ownership.EmitterFilePaths.ShouldContain("/accepted-method.cs");
        first.Ownership.EmitterFilePaths.ShouldNotContain("/noise-call.cs");
        first.Ownership.EmitterFilePaths.ShouldNotContain("/noise-method.cs");
        first.Ownership.SymbolIds.ShouldContain("M:N.Entity.New``3(``1)");

        const string hub = "M:N.IWork.Run";
        const string target = "M:N.Work.Run";
        const string slot = "F:N.State.Callback";
        var structural = new SpyView()
            .Method(hub, "Run()", "T:N.IWork")
            .Method(target, "Run()", "T:N.Work", filePath: "/target.cs")
            .Method("M:N.Root.Go", "Go()", "T:N.Root")
            .Relation("T:N.Work", "T:N.IWork", RelationKinds.Interface, "/relation.cs")
            .Dispatch(hub, target, DispatchKinds.Impl, "/dispatch.cs")
            .Call("M:N.Root.Go", hub, receiver: "N.Work", filePath: "/raw-call.cs")
            .Dispatch(slot, target, DispatchKinds.DelegateFieldBind, "/bind.cs")
            .Dispatch(slot, "M:N.Root.Go", DispatchKinds.DelegateFieldInvoke, "/invoke.cs");

        var ownership = Build(structural, Rules(), target).Ownership;

        ownership.SymbolIds.ShouldContain(slot);
        ownership.SymbolIds.ShouldContain("T:N.Work");
        ownership.SymbolIds.ShouldContain("T:N.IWork");
        ownership.EmitterFilePaths.ShouldContain("/dispatch.cs");
        ownership.EmitterFilePaths.ShouldContain("/relation.cs");
        ownership.EmitterFilePaths.ShouldContain("/bind.cs");
        ownership.EmitterFilePaths.ShouldContain("/invoke.cs");
        ownership.EmitterFilePaths.ShouldContain("/raw-call.cs");
    }

    [Test]
    public void Load_diagnostics_and_event_classification_are_explicit()
    {
        var view = new SpyView().Method("M:N.Target.Run", "Run()", "T:N.Target");
        var classified = DemandReverseCallersGraph.Build(
            view,
            new DemandForwardGraphRules(new ForwardCallProjectionRules(ClassifyEventSubscriptions: true), [], []),
            new DemandReverseCallersGraphRequest("M:N.Target.Run", int.MaxValue, FactPathFinder.TraversalMode.SyncCut)
        );
        var ordinary = Build(view, Rules(), "M:N.Target.Run");
        var legacy = DemandReverseCallersGraphDiagnostics.LegacyFallback();

        classified.EventSubscriptionsClassified.ShouldBeTrue();
        classified.Diagnostics.Load.Mode.ShouldBe(DemandReverseLoadMode.KeyedDemand);
        classified.Diagnostics.Load.UsedLegacyFallback.ShouldBeFalse();
        ordinary.EventSubscriptionsClassified.ShouldBeFalse();
        legacy.Load.Mode.ShouldBe(DemandReverseLoadMode.LegacyWholeGraphFallback);
        legacy.Load.UsedLegacyFallback.ShouldBeTrue();
        legacy.Reverse.ShouldBe(new DemandReverseKeyedReads(default, default, default, default, default));
        legacy.Closure.ShouldBe(new DemandReverseClosureDiagnostics(0, 0, 0, 0));
        legacy.DeliverySitesSynthesized.ShouldBeFalse();
    }

    private static DemandReverseCallersGraphResult Build(
        SpyView view,
        DemandForwardGraphRules rules,
        string target,
        int maxDepth = int.MaxValue,
        FactPathFinder.TraversalMode discoveryMode = FactPathFinder.TraversalMode.SyncCut
    ) => DemandReverseCallersGraph.Build(view, rules, new DemandReverseCallersGraphRequest(target, maxDepth, discoveryMode));

    private static DemandForwardGraphRules Rules(
        IReadOnlyList<FactRedirectRule>? redirect = null,
        IReadOnlyList<FactGenericFactoryRule>? factory = null,
        IReadOnlyList<FactTraversalCutRule>? cut = null,
        IReadOnlyList<FactContextDispatchRule>? context = null
    ) => new(new ForwardCallProjectionRules(Redirect: redirect, Factory: factory), cut ?? [], context ?? []);

    private sealed class SpyView : IFactGraphView
    {
        private readonly List<ReferenceFact> references = [];
        private readonly List<SymbolFact> symbols = [];
        private readonly List<TypeRelationFact> relations = [];
        private readonly List<DispatchFact> dispatch = [];

        internal List<string> RequestedCallers { get; } = [];
        internal List<string> RequestedReverseTargets { get; } = [];
        internal List<string> RequestedMethodKeys { get; } = [];

        internal SpyView Method(string id, string signature, string containing, bool isOverride = false, string filePath = "/methods.cs")
        {
            var head = id.Split('(')[0];
            var name = head[(head.LastIndexOf('.') + 1)..].Split('`')[0];
            symbols.Add(
                new SymbolFact(id, SymbolKinds.Method, name, "N", containing, "public", "", signature, filePath, 1, 1, "App", isOverride)
            );
            return this;
        }

        internal SpyView Call(
            string caller,
            string callee,
            string kind = RefKinds.Invocation,
            string? receiver = null,
            bool targetInSource = true,
            string? methodBinding = null,
            string? typeArguments = null,
            bool nonVirtual = false,
            string filePath = "/calls.cs"
        )
        {
            references.Add(
                new ReferenceFact(
                    callee,
                    kind,
                    caller,
                    "App",
                    targetInSource,
                    filePath,
                    1,
                    ReceiverType: receiver,
                    TypeArguments: typeArguments,
                    MethodTypeArgBinding: methodBinding,
                    NonVirtual: nonVirtual
                )
            );
            return this;
        }

        internal SpyView Relation(string type, string related, string kind, string filePath = "")
        {
            relations.Add(new TypeRelationFact(type, related, kind, filePath));
            return this;
        }

        internal SpyView Dispatch(string source, string target, string kind, string filePath = "")
        {
            dispatch.Add(new DispatchFact(source, target, kind, filePath));
            return this;
        }

        internal void ClearReads()
        {
            RequestedCallers.Clear();
            RequestedReverseTargets.Clear();
            RequestedMethodKeys.Clear();
        }

        internal FactGraphData FullGraph(DemandForwardGraphRules rules)
        {
            var source = new DemandMonomorphizedCallSource(this, rules.Projection);
            var edges = new HashSet<CallEdge>();
            var pending = new Queue<string>(MethodSymbolIds);
            var expanded = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                var caller = pending.Dequeue();
                if (!expanded.Add(caller))
                {
                    continue;
                }
                foreach (var edge in source.CallsFrom(caller))
                {
                    edges.Add(edge);
                    if (MonomorphizedNodeId.IsMonomorphized(edge.Callee))
                    {
                        pending.Enqueue(edge.Callee);
                    }
                }
            }
            return new FactGraphData(
                edges.ToArray(),
                relations
                    .Where(row => row.RelationKind == RelationKinds.Interface)
                    .Select(row => new ImplementsEdge(row.TypeSymbolId, row.RelatedSymbolId))
                    .Distinct()
                    .ToArray(),
                SymbolFactProjections.SelectCanonicalMethodFacts(symbols).Select(SymbolFactProjections.ToMethodRef).ToArray(),
                relations
                    .Where(row => row.RelationKind == RelationKinds.Base)
                    .Select(row => new BaseEdge(row.TypeSymbolId, row.RelatedSymbolId))
                    .Distinct()
                    .ToArray(),
                dispatch,
                CutRules: rules.Cut.Count == 0 ? null : rules.Cut,
                ContextRules: rules.Context.Count == 0 ? null : rules.Context
            );
        }

        public IReadOnlyList<ReferenceFact> ReferencesFrom(string enclosingSymbolId)
        {
            RequestedCallers.Add(enclosingSymbolId);
            return references.Where(reference => reference.EnclosingSymbolId == enclosingSymbolId).ToArray();
        }

        public IReadOnlyList<ReferenceFact> ReferencesTo(string targetSymbolId)
        {
            RequestedReverseTargets.Add(targetSymbolId);
            return references.Where(reference => reference.TargetSymbolId == targetSymbolId).ToArray();
        }

        public IReadOnlyList<ReferenceFact> ReferencesToMethodKey(string methodKey)
        {
            RequestedMethodKeys.Add(methodKey);
            return references.Where(reference => ReferenceTargetMethodKey.Normalize(reference.TargetSymbolId) == methodKey).ToArray();
        }

        public IReadOnlyList<SymbolFact> SymbolsById(string symbolId) => symbols.Where(symbol => symbol.SymbolId == symbolId).ToArray();

        public IReadOnlyList<SymbolFact> SymbolsByContainingSymbol(string containingSymbolId) =>
            symbols.Where(symbol => symbol.ContainingSymbolId == containingSymbolId).ToArray();

        public IReadOnlyCollection<string> MethodSymbolIds =>
            symbols.Select(symbol => symbol.SymbolId).Distinct(StringComparer.Ordinal).ToArray();

        public IReadOnlyList<SymbolFact> MethodsById(string symbolId) => SymbolsById(symbolId);

        public IReadOnlyList<SymbolFact> MethodsByContainingSymbol(string containingSymbolId) =>
            SymbolsByContainingSymbol(containingSymbolId);

        public IReadOnlyList<TypeRelationFact> TypeRelationsFrom(string typeSymbolId) =>
            relations.Where(relation => relation.TypeSymbolId == typeSymbolId).ToArray();

        public IReadOnlyList<TypeRelationFact> TypeRelationsTo(string relatedSymbolId) =>
            relations.Where(relation => relation.RelatedSymbolId == relatedSymbolId).ToArray();

        public IReadOnlyList<TypeRelationFact> DispatchRelationsTo(string declaringTypeId)
        {
            var family = DispatchRelationKeys.RelatedFamily(declaringTypeId);
            var simple = DispatchRelationKeys.SimpleTypeName(declaringTypeId);
            return relations
                .Where(relation =>
                    DispatchRelationKeys.RelatedFamily(relation.RelatedSymbolId) == family
                    || DispatchRelationKeys.UnresolvedInterfaceName(relation) == simple
                )
                .ToArray();
        }

        public IReadOnlyList<DispatchFact> DispatchFrom(string sourceMember) =>
            dispatch.Where(fact => fact.SourceMember == sourceMember).ToArray();

        public IReadOnlyList<DispatchFact> DispatchTo(string targetMember) =>
            dispatch.Where(fact => fact.TargetMember == targetMember).ToArray();
    }
}
