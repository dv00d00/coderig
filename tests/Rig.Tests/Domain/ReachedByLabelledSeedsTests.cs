using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

// FactPathFinder.ReachedByLabelledSeeds — k reverse closures in ONE walk. The Rider file read model needs one
// closure per effect FAMILY, and it used to call ReachedByAny once per family: on MedDBase (442k symbols) the
// first request cost 15.6s with two families, and every family added another full walk.
//
// The whole contract is EQUIVALENCE: per label, the fused walk must return exactly what the separate walk
// returns — same keys, same depths. These tests pin that against ReachedByAny itself rather than against
// hand-written expectations, so the two can never drift.
public sealed class ReachedByLabelledSeedsTests
{
    // Two families that MERGE on the way up (both reachable through Shared.Hub) plus one that stays disjoint,
    // because the interesting case for a mask-carrying walk is labels travelling together and then splitting.
    //   Root.A   -> Shared.Hub -> Db.Write        (label 0)
    //   Root.A   -> Shared.Hub -> Cache.Invalidate(label 1)
    //   Other.B  -> Rpc.Send                      (label 2)
    //   Lonely.C -> Nothing                       (no label)
    private static FactGraphData MergingShape()
    {
        var edges = new[]
        {
            new CallEdge("M:N.Root.A", "M:N.Shared.Hub", "invocation", "f.cs", 1),
            new CallEdge("M:N.Shared.Hub", "M:N.Db.Write", "invocation", "f.cs", 2),
            new CallEdge("M:N.Shared.Hub", "M:N.Cache.Invalidate", "invocation", "f.cs", 3),
            new CallEdge("M:N.Other.B", "M:N.Rpc.Send", "invocation", "f.cs", 4),
            new CallEdge("M:N.Lonely.C", "M:N.Nothing.D", "invocation", "f.cs", 5),
        };
        var methods = new[]
        {
            new MethodRef("M:N.Root.A", "A", "T:N.Root"),
            new MethodRef("M:N.Shared.Hub", "Hub", "T:N.Shared"),
            new MethodRef("M:N.Db.Write", "Write", "T:N.Db"),
            new MethodRef("M:N.Cache.Invalidate", "Invalidate", "T:N.Cache"),
            new MethodRef("M:N.Other.B", "B", "T:N.Other"),
            new MethodRef("M:N.Rpc.Send", "Send", "T:N.Rpc"),
            new MethodRef("M:N.Lonely.C", "C", "T:N.Lonely"),
            new MethodRef("M:N.Nothing.D", "D", "T:N.Nothing"),
        };
        return new FactGraphData(edges, Array.Empty<ImplementsEdge>(), methods);
    }

    // A dispatch-shaped graph, because the reverse walk's edge model includes mined dispatch and the fused
    // version must inherit it rather than re-implement a plain call-edge walk.
    private static FactGraphData DispatchShape()
    {
        var edges = new[]
        {
            new CallEdge("M:N.Caller.Go", "M:N.Base.V", "invocation", "f.cs", 1),
            new CallEdge("M:N.Second.Go", "M:N.Base.V", "invocation", "f.cs", 2),
        };
        var bases = new[] { new BaseEdge("T:N.Impl", "T:N.Base") };
        var methods = new[]
        {
            new MethodRef("M:N.Caller.Go", "Go", "T:N.Caller"),
            new MethodRef("M:N.Second.Go", "Go", "T:N.Second"),
            new MethodRef("M:N.Base.V", "V", "T:N.Base"),
            new MethodRef("M:N.Impl.V", "V", "T:N.Impl", IsOverride: true),
        };
        var mined = new[] { new DispatchFact("M:N.Base.V", "M:N.Impl.V", "override") };
        return new FactGraphData(edges, Array.Empty<ImplementsEdge>(), methods, bases, mined);
    }

    private static void ShouldEqualSeparateWalks(FactGraphData graph, params string[][] seedsByLabel)
    {
        var fused = FactPathFinder.ReachedByLabelledSeeds(
            graph,
            seedsByLabel.Select(seeds => (IReadOnlyCollection<string>)seeds).ToArray(),
            maxDepth: int.MaxValue,
            maxNodes: int.MaxValue
        );

        fused.Count.ShouldBe(seedsByLabel.Length);
        for (var label = 0; label < seedsByLabel.Length; label++)
        {
            var separate = FactPathFinder.ReachedByAny(graph, seedsByLabel[label], maxDepth: int.MaxValue, maxNodes: int.MaxValue);
            fused[label]
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ShouldBe(
                    separate.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                    $"label {label} must match the closure ReachedByAny computes for the same seeds"
                );
        }
    }

    [Test]
    public void Each_label_equals_the_closure_the_separate_walk_computes()
    {
        ShouldEqualSeparateWalks(MergingShape(), ["M:N.Db.Write"], ["M:N.Cache.Invalidate"], ["M:N.Rpc.Send"]);
    }

    [Test]
    public void Labels_that_share_callers_do_not_contaminate_each_others_depths()
    {
        var fused = FactPathFinder.ReachedByLabelledSeeds(
            MergingShape(),
            [new[] { "M:N.Db.Write" }, new[] { "M:N.Rpc.Send" }],
            maxDepth: int.MaxValue,
            maxNodes: int.MaxValue
        );

        // The db label climbs Hub(1) -> Root.A(2); the rpc label never touches either.
        fused[0]["M:N.Shared.Hub"].ShouldBe(1);
        fused[0]["M:N.Root.A"].ShouldBe(2);
        fused[0].Keys.ShouldNotContain("M:N.Other.B");
        fused[1]["M:N.Other.B"].ShouldBe(1);
        fused[1].Keys.ShouldNotContain("M:N.Shared.Hub");
    }

    [Test]
    public void A_multi_seed_label_still_unions_its_seeds()
    {
        ShouldEqualSeparateWalks(MergingShape(), ["M:N.Db.Write", "M:N.Rpc.Send"], ["M:N.Cache.Invalidate"]);
    }

    [Test]
    public void Reverse_dispatch_edges_are_inherited_by_the_fused_walk()
    {
        ShouldEqualSeparateWalks(DispatchShape(), ["M:N.Impl.V"], ["M:N.Base.V"]);
    }

    [Test]
    public void Seeds_absent_from_the_graph_are_skipped_exactly_as_the_separate_walk_skips_them()
    {
        ShouldEqualSeparateWalks(MergingShape(), ["M:N.Db.Write", "M:N.Does.Not.Exist"], ["M:N.Also.Missing"]);
    }

    [Test]
    public void A_max_depth_bound_applies_per_label_exactly_as_the_separate_walk_applies_it()
    {
        var graph = MergingShape();
        var fused = FactPathFinder.ReachedByLabelledSeeds(
            graph,
            [new[] { "M:N.Db.Write" }, new[] { "M:N.Cache.Invalidate" }],
            maxDepth: 1,
            maxNodes: int.MaxValue
        );

        for (var label = 0; label < 2; label++)
        {
            var seeds = label == 0 ? new[] { "M:N.Db.Write" } : ["M:N.Cache.Invalidate"];
            var separate = FactPathFinder.ReachedByAny(graph, seeds, maxDepth: 1, maxNodes: int.MaxValue);
            fused[label]
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ShouldBe(separate.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }
    }

    // The dispatch admission is the reverse mirror of the forward walk's `Fanout > 1` split. Base.V has ONE
    // override, so under null receivers its hop has degree 1: SingleTarget admits it exactly as All does, and
    // None drops it (direct callers only — and the two callers call Base.V, not Impl.V).
    [Test]
    public void Single_target_admission_keeps_a_degree_one_hop_and_none_drops_it()
    {
        var graph = DispatchShape();
        IReadOnlyList<IReadOnlyCollection<string>> seeds = [new[] { "M:N.Impl.V" }];

        var all = FactPathFinder.ReachedByLabelledSeeds(graph, seeds, int.MaxValue, int.MaxValue);
        var singleTarget = FactPathFinder.ReachedByLabelledSeeds(
            graph,
            seeds,
            int.MaxValue,
            int.MaxValue,
            dispatch: FactPathFinder.DispatchAdmission.SingleTarget
        );
        var none = FactPathFinder.ReachedByLabelledSeeds(
            graph,
            seeds,
            int.MaxValue,
            int.MaxValue,
            dispatch: FactPathFinder.DispatchAdmission.None
        );

        all[0].Keys.OrderBy(key => key, StringComparer.Ordinal).ShouldBe(["M:N.Caller.Go", "M:N.Impl.V", "M:N.Second.Go"]);
        singleTarget[0]
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ShouldBe(all[0].OrderBy(pair => pair.Key, StringComparer.Ordinal));
        none[0].Keys.ShouldBe(["M:N.Impl.V"]);
    }

    // With TWO overrides the null-receiver hop has degree 2, so SingleTarget excludes the caller that All admits.
    [Test]
    public void Single_target_admission_excludes_a_hop_that_fans_to_several_overrides()
    {
        var edges = new[] { new CallEdge("M:N.Caller.Go", "M:N.Base.V", "invocation", "f.cs", 1) };
        var bases = new[] { new BaseEdge("T:N.Impl", "T:N.Base"), new BaseEdge("T:N.Other", "T:N.Base") };
        var methods = new[]
        {
            new MethodRef("M:N.Caller.Go", "Go", "T:N.Caller"),
            new MethodRef("M:N.Base.V", "V", "T:N.Base"),
            new MethodRef("M:N.Impl.V", "V", "T:N.Impl", IsOverride: true),
            new MethodRef("M:N.Other.V", "V", "T:N.Other", IsOverride: true),
        };
        var mined = new[]
        {
            new DispatchFact("M:N.Base.V", "M:N.Impl.V", "override"),
            new DispatchFact("M:N.Base.V", "M:N.Other.V", "override"),
        };
        var graph = new FactGraphData(edges, Array.Empty<ImplementsEdge>(), methods, bases, mined);
        IReadOnlyList<IReadOnlyCollection<string>> seeds = [new[] { "M:N.Impl.V" }];

        var all = FactPathFinder.ReachedByLabelledSeeds(graph, seeds, int.MaxValue, int.MaxValue);
        var singleTarget = FactPathFinder.ReachedByLabelledSeeds(
            graph,
            seeds,
            int.MaxValue,
            int.MaxValue,
            dispatch: FactPathFinder.DispatchAdmission.SingleTarget
        );

        all[0].Keys.ShouldContain("M:N.Caller.Go");
        singleTarget[0].Keys.ShouldNotContain("M:N.Caller.Go");
    }

    // A seed stands for its monomorphized instantiations too, because Materialize REPLACES the callee of a
    // fully-concrete call edge with the instantiation node: the base id keeps its body but loses that caller.
    // Effects only ever carry the base id, so without the expansion a materialized effectful generic has no
    // reverse reach at all. Pinned on BOTH walks so the equivalence above stays a real constraint.
    [Test]
    public void A_base_seed_also_admits_the_monomorphized_instantiations_of_that_base()
    {
        const string caller = "M:N.Caller.Go";
        const string generic = "M:N.Ext.Fill``1(N.List{``0})";
        var raw = new FactGraphData(
            [new CallEdge(caller, generic, "invocation", "f.cs", 1, ReceiverType: "N.RowList", MethodTypeArgBinding: """["C:N.Row"]""")],
            Array.Empty<ImplementsEdge>(),
            [new MethodRef(caller, "Go", "T:N.Caller"), new MethodRef(generic, "Fill", "T:N.Ext")]
        );
        var shaped = FactPathFinder.ShapeGraph(
            raw,
            [],
            [],
            [],
            monomorphizeSignatures: new Dictionary<string, string>(StringComparer.Ordinal) { [generic] = "void Fill<T>(List<T> list)" }
        );

        var instantiation = MonomorphizedNodeId.For(generic, [], ["N.Row"]);
        shaped.CallEdges.ShouldContain(edge => edge.Caller == caller && edge.Callee == instantiation);
        shaped.CallEdges.ShouldNotContain(edge => edge.Caller == caller && edge.Callee == generic);

        var separate = FactPathFinder.ReachedByAny(shaped, [generic], maxDepth: int.MaxValue, maxNodes: int.MaxValue);
        separate[generic].ShouldBe(0);
        separate[instantiation].ShouldBe(0);
        separate[caller].ShouldBe(1);

        ShouldEqualSeparateWalks(shaped, [generic]);
    }

    [Test]
    public void No_labels_is_an_empty_result_and_over_sixty_four_is_refused()
    {
        FactPathFinder.ReachedByLabelledSeeds(MergingShape(), []).ShouldBeEmpty();

        var tooMany = Enumerable.Range(0, 65).Select(_ => (IReadOnlyCollection<string>)new[] { "M:N.Db.Write" }).ToArray();
        Should.Throw<ArgumentOutOfRangeException>(() => FactPathFinder.ReachedByLabelledSeeds(MergingShape(), tooMany));
    }
}
