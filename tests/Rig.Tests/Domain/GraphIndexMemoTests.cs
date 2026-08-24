using System.Reflection;
using System.Runtime.CompilerServices;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

// The derived traversal structures (GraphIndex, ReverseMaps) are memoised on GRAPH IDENTITY — they used
// to be rebuilt from scratch inside every traversal. These tests pin the three things that make that
// sound: (1) the memo is keyed by everything that changes the result, (2) it is keyed by IDENTITY so a
// different graph object never reads another graph's entry, and (3) the answers a warm memo serves are
// identical to the ones a cold build produced.
//
// The memo sits behind private statics (GraphIndex is internal to Rig.Domain and Rig.Domain grants no
// InternalsVisibleTo), so the probes below go through reflection rather than widening the production
// surface for a test.
public sealed class GraphIndexMemoTests
{
    private static readonly MethodInfo BuildIndexMethod = typeof(FactPathFinder).GetMethod(
        "BuildIndex",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static readonly MethodInfo BuildReverseMapsMethod = typeof(FactPathFinder).GetMethod(
        "BuildReverseMaps",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static readonly MethodInfo BuildCountsMethod = typeof(FactPathFinder).GetMethod(
        "DerivedBuildCounts",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static object Index(FactGraphData graph, bool narrowDispatch) => BuildIndexMethod.Invoke(null, [graph, narrowDispatch])!;

    private static object Reverse(FactGraphData graph, bool narrowDispatch, FactPathFinder.TraversalMode mode) =>
        BuildReverseMapsMethod.Invoke(null, [graph, narrowDispatch, mode])!;

    // (full index builds, full reverse-map builds) this graph's memo has performed. Graph-scoped, so a
    // parallel test class hammering FactPathFinder over its own graphs cannot move these numbers.
    private static (long Indexes, long ReverseMaps) BuildCounts(FactGraphData graph) =>
        ((long, long))BuildCountsMethod.Invoke(null, [graph])!;

    // ── Keying ────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void The_same_graph_object_reuses_one_index()
    {
        var graph = Graph();

        var first = Index(graph, narrowDispatch: true);
        var second = Index(graph, narrowDispatch: true);

        second.ShouldBeSameAs(first);
        BuildCounts(graph).Indexes.ShouldBe(1);
    }

    [Test]
    public void A_different_graph_object_does_not_share_an_index()
    {
        var one = Graph();
        // Structurally IDENTICAL content, different object — identity keying must still give it its own
        // entry (records compare by value, so a value-keyed cache would wrongly collapse these two).
        var two = Graph();

        var indexOne = Index(one, narrowDispatch: true);
        var indexTwo = Index(two, narrowDispatch: true);

        indexTwo.ShouldNotBeSameAs(indexOne);
        BuildCounts(one).Indexes.ShouldBe(1);
        BuildCounts(two).Indexes.ShouldBe(1);
    }

    [Test]
    public void Narrow_dispatch_variants_get_their_own_index()
    {
        var graph = Graph();

        var narrowed = Index(graph, narrowDispatch: true);
        var blind = Index(graph, narrowDispatch: false);

        blind.ShouldNotBeSameAs(narrowed);
        NarrowDispatchFlag(narrowed).ShouldBeTrue();
        NarrowDispatchFlag(blind).ShouldBeFalse();
        // ...and each variant is itself reused, so the two-entry memo is exactly two builds.
        Index(graph, narrowDispatch: true).ShouldBeSameAs(narrowed);
        Index(graph, narrowDispatch: false).ShouldBeSameAs(blind);
        BuildCounts(graph).Indexes.ShouldBe(2);
    }

    [Test]
    public void Reverse_maps_are_keyed_by_narrow_dispatch_and_traversal_mode()
    {
        var graph = Graph();
        var modes = new[]
        {
            FactPathFinder.TraversalMode.SyncCut,
            FactPathFinder.TraversalMode.AsyncExact,
            FactPathFinder.TraversalMode.AsyncInclude,
        };

        var built = new List<object>();
        foreach (var narrow in new[] { true, false })
        foreach (var mode in modes)
        {
            var maps = Reverse(graph, narrow, mode);
            // Every (narrowDispatch, mode) pair is a distinct entry — none of them may be handed a
            // sibling's maps.
            built.ShouldNotContain(x => ReferenceEquals(x, maps));
            built.Add(maps);
            Reverse(graph, narrow, mode).ShouldBeSameAs(maps);
        }

        built.Count.ShouldBe(6);
        BuildCounts(graph).ReverseMaps.ShouldBe(6);
    }

    // ── The point of the change: a traversal no longer rebuilds ───────────────────────────────────

    [Test]
    public void Repeated_traversals_over_one_graph_build_the_derived_structures_once()
    {
        var graph = Graph();

        for (var i = 0; i < 5; i++)
        {
            FactPathFinder.Find(graph, "Entry.Run", "Sink.Write");
            FactPathFinder.ReachesWithFanout(graph, "Entry.Run");
            FactPathFinder.BuildTree(graph, "Entry.Run");
            FactPathFinder.ReachedBy(graph, "Sink.Write");
        }

        var counts = BuildCounts(graph);
        // narrowDispatch:true (the forward/reverse traversals) + narrowDispatch:false (the internal
        // dispatch-resolution index BuildReverseMapsCore uses) = 2 index builds total, and ONE reverse-map
        // build for the single (narrowDispatch:true, SyncCut) key these commands use. Pre-memo this was
        // 20 index builds and 5 reverse-map builds for the same loop.
        counts.Indexes.ShouldBe(2);
        counts.ReverseMaps.ShouldBe(1);
    }

    // ── Byte-identical answers ────────────────────────────────────────────────────────────────────

    [Test]
    public void A_warm_memo_answers_identically_to_a_cold_build()
    {
        var warm = Graph();
        // Warm `warm` up with a full round of traversals, then compare EVERY answer against a freshly
        // constructed (cold, never-memoised) twin — which is exactly the pre-memo code path.
        Render(warm);

        var coldAnswers = Render(Graph());
        var warmAnswers = Render(warm);

        warmAnswers.ShouldBe(coldAnswers);
        // And a second warm round is identical to the first (the memo is not accumulating state).
        Render(warm).ShouldBe(coldAnswers);
        BuildCounts(warm).Indexes.ShouldBe(2);
    }

    [Test]
    public void Narrowed_and_receiver_blind_answers_stay_distinct_under_the_memo()
    {
        var graph = Graph();

        var narrowed = string.Join(
            "|",
            FactPathFinder.ReachedBy(graph, "Sink.Write", narrowDispatch: true).Keys.Order(StringComparer.Ordinal)
        );
        var blind = string.Join(
            "|",
            FactPathFinder.ReachedBy(graph, "Sink.Write", narrowDispatch: false).Keys.Order(StringComparer.Ordinal)
        );

        // The receiver-blind superset must still be a strict superset here (the narrowed walk drops the
        // sibling override's caller). If the memo handed one variant's maps to the other these collapse.
        blind.ShouldNotBe(narrowed);
        blind.Length.ShouldBeGreaterThan(narrowed.Length);
        // Repeating both against the warm memo reproduces both answers exactly.
        string.Join("|", FactPathFinder.ReachedBy(graph, "Sink.Write", narrowDispatch: true).Keys.Order(StringComparer.Ordinal))
            .ShouldBe(narrowed);
        string.Join("|", FactPathFinder.ReachedBy(graph, "Sink.Write", narrowDispatch: false).Keys.Order(StringComparer.Ordinal))
            .ShouldBe(blind);
    }

    // ── Lifetime: the memo must not pin a retired generation ──────────────────────────────────────

    [Test]
    public void The_memo_does_not_keep_a_retired_graph_alive()
    {
        var handle = SeedMemoAndDropTheGraph();

        // ConditionalWeakTable ephemerons can need more than one pass to clear.
        for (var i = 0; i < 10 && handle.IsAlive; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        // If the memo were a strong-keyed Dictionary<FactGraphData, …> this graph — and its whole derived
        // index — would be pinned for the life of the process.
        handle.IsAlive.ShouldBeFalse();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SeedMemoAndDropTheGraph()
    {
        var graph = Graph();
        Index(graph, narrowDispatch: true);
        Index(graph, narrowDispatch: false);
        Reverse(graph, narrowDispatch: true, FactPathFinder.TraversalMode.SyncCut);
        BuildCounts(graph).Indexes.ShouldBe(2);
        return new WeakReference(graph);
    }

    // ── Thread safety ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Concurrent_first_use_builds_exactly_one_index_and_hands_out_no_half_built_one()
    {
        var graph = Graph();
        const int threads = 16;
        using var gate = new Barrier(threads);
        var seen = new object[threads];

        Parallel.For(
            0,
            threads,
            i =>
            {
                gate.SignalAndWait();
                seen[i] = Index(graph, narrowDispatch: true);
            }
        );

        foreach (var index in seen)
        {
            index.ShouldBeSameAs(seen[0]);
            // A half-built index would be missing adjacency/nodes; every racer must see the finished one.
            NodeCount(index).ShouldBe(NodeCount(seen[0]));
            AdjacencyCount(index).ShouldBe(AdjacencyCount(seen[0]));
        }

        BuildCounts(graph).Indexes.ShouldBe(1);
    }

    [Test]
    public void Concurrent_traversals_over_one_graph_agree_with_the_serial_answer()
    {
        var graph = Graph();
        var expected = Render(Graph());
        var results = new string[24];

        Parallel.For(0, results.Length, i => results[i] = Render(graph));

        foreach (var result in results)
        {
            result.ShouldBe(expected);
        }
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────────

    // A small graph that still exercises every input BuildIndex reads: direct calls, interface impls,
    // a base/override hierarchy (so the strict-descendant closure runs), mined dispatch facts, and
    // receiver-typed virtual call sites (so narrowed and receiver-blind dispatch differ).
    private static FactGraphData Graph()
    {
        var edges = new List<CallEdge>
        {
            new("M:App.Entry.Run", "M:App.Service.Handle", "invocation", "Entry.cs", 10),
            new("M:App.Entry.Run", "M:App.IWriter.Write", "invocation", "Entry.cs", 11, ReceiverType: "T:App.FileWriter"),
            new("M:App.Service.Handle", "M:App.BaseWorker.Work", "invocation", "Service.cs", 20, ReceiverType: "T:App.DerivedWorker"),
            new("M:App.FileWriter.Write", "M:App.Sink.Write", "invocation", "FileWriter.cs", 30),
            new("M:App.NetWriter.Write", "M:App.Sink.Write", "invocation", "NetWriter.cs", 40),
            new("M:App.DerivedWorker.Work", "M:App.Sink.Write", "invocation", "DerivedWorker.cs", 50),
            new("M:App.OtherWorker.Work", "M:App.Sink.Write", "invocation", "OtherWorker.cs", 60),
            new("M:App.Detached.Ping", "M:App.NetWriter.Write", "invocation", "Detached.cs", 70),
        };

        var methods = new List<MethodRef>
        {
            new("M:App.Entry.Run", "Run", "T:App.Entry"),
            new("M:App.Service.Handle", "Handle", "T:App.Service"),
            new("M:App.IWriter.Write", "Write", "T:App.IWriter"),
            new("M:App.FileWriter.Write", "Write", "T:App.FileWriter"),
            new("M:App.NetWriter.Write", "Write", "T:App.NetWriter"),
            new("M:App.BaseWorker.Work", "Work", "T:App.BaseWorker"),
            new("M:App.DerivedWorker.Work", "Work", "T:App.DerivedWorker", IsOverride: true),
            new("M:App.OtherWorker.Work", "Work", "T:App.OtherWorker", IsOverride: true),
            new("M:App.Sink.Write", "Write", "T:App.Sink"),
            new("M:App.Detached.Ping", "Ping", "T:App.Detached"),
        };

        var impls = new List<ImplementsEdge> { new("T:App.FileWriter", "T:App.IWriter"), new("T:App.NetWriter", "T:App.IWriter") };

        var bases = new List<BaseEdge> { new("T:App.DerivedWorker", "T:App.BaseWorker"), new("T:App.OtherWorker", "T:App.BaseWorker") };

        var mined = new List<DispatchFact>
        {
            new("M:App.IWriter.Write", "M:App.FileWriter.Write", "impl"),
            new("M:App.IWriter.Write", "M:App.NetWriter.Write", "impl"),
            new("M:App.BaseWorker.Work", "M:App.DerivedWorker.Work", "override"),
            new("M:App.BaseWorker.Work", "M:App.OtherWorker.Work", "override"),
        };

        return new FactGraphData(edges, impls, methods, bases, mined);
    }

    // Every traversal surface this change touches, rendered to one deterministic string so answers can be
    // compared verbatim (the "byte-identical" gate).
    private static string Render(FactGraphData graph)
    {
        var lines = new List<string>();

        var path = FactPathFinder.Find(graph, "Entry.Run", "Sink.Write");
        lines.Add("path=" + (path is null ? "<none>" : string.Join(">", path.Select(s => $"{s.SymbolId}:{s.Kind}:{s.Line}:{s.Fanout}"))));

        foreach (var (node, info) in FactPathFinder.ReachesWithFanout(graph, "Entry.Run").OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            lines.Add($"reach={node}:{info.Depth}");
        }

        foreach (var (node, depth) in FactPathFinder.ReachedBy(graph, "Sink.Write").OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            lines.Add($"callers={node}:{depth}");
        }

        foreach (var mode in new[] { FactPathFinder.TraversalMode.SyncCut, FactPathFinder.TraversalMode.AsyncInclude })
        foreach (
            var (node, depth) in FactPathFinder.ReachedBy(graph, "Sink.Write", mode: mode).OrderBy(kv => kv.Key, StringComparer.Ordinal)
        )
        {
            lines.Add($"callers[{mode}]={node}:{depth}");
        }

        foreach (var root in FactPathFinder.BuildTree(graph, "Entry.Run"))
        {
            RenderTree(root, 0, lines);
        }

        foreach (var node in FactPathFinder.MatchedNodes(graph, "Write").Order(StringComparer.Ordinal))
        {
            lines.Add("match=" + node);
        }

        foreach (
            var edge in FactPathFinder
                .AllDispatchEdges(graph)
                .Select(e => $"{e.From}->{e.To}:{e.Kind}:{e.Basis}")
                .Order(StringComparer.Ordinal)
        )
        {
            lines.Add("dispatch=" + edge);
        }

        return string.Join("\n", lines);
    }

    private static void RenderTree(TraceNode node, int depth, List<string> lines)
    {
        lines.Add($"tree={new string(' ', depth)}{node.SymbolId}:{node.EdgeKind}:{node.Fanout}:{node.DispatchBasis}");
        foreach (var child in node.Children)
        {
            RenderTree(child, depth + 1, lines);
        }
    }

    // ── Reflection probes into the internal GraphIndex ────────────────────────────────────────────

    private static bool NarrowDispatchFlag(object index) =>
        (bool)index.GetType().GetField("NarrowDispatch", BindingFlags.Public | BindingFlags.Instance)!.GetValue(index)!;

    private static int NodeCount(object index) => CountOf(index, "Nodes");

    private static int AdjacencyCount(object index) => CountOf(index, "Adjacency");

    private static int CountOf(object index, string field) =>
        ((System.Collections.IEnumerable)index.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance)!.GetValue(index)!)
            .Cast<object>()
            .Count();
}
