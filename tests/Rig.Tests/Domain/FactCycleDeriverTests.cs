using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

// FactCycleDeriver.DeriveEventCycles — the `event_cycle` GRAPH hazard: a feedback cycle that closes through
// ≥1 publish→consumer DELIVERY edge (a Kind="handoff" CallEdge whose HandoffDispatcher tag a delivery RULE
// marked `cycleDelivery`). Detected as a strongly-connected component of the Caller→Callee graph that CONTAINS
// such a delivery edge (both endpoints in the SCC). Confidence is "high" when every delivery edge came from an
// exact join, "low" when any came from a rule declaring `joinConfidence: "low"`.
//
// Core-purity F6: the qualifying tags and their join exactness are RULE DATA — core knew `actor_tell` and its
// heuristic-ness by name before this. The dispatcher map below is what CycleDeliveryDispatchers builds from the
// shipped C#-event rule (exact) plus a project's actor rule (heuristic). These synthetic-graph tests mirror
// EventDeliveryEdgeTests' harness.
public sealed class FactCycleDeriverTests
{
    // Tags a ruleset declared as cycle-carrying -> that mechanism's join confidence.
    private static readonly Dictionary<string, string> Dispatchers = new(StringComparer.Ordinal)
    {
        ["event_raise"] = "high",
        ["actor_tell"] = "low",
    };

    private static MethodRef M(string id) => new(id, id, null);

    private static FactGraphData Graph(params CallEdge[] edges)
    {
        var nodes = edges.SelectMany(e => new[] { e.Caller, e.Callee }).Distinct(StringComparer.Ordinal).Select(M).ToArray();
        return new FactGraphData(edges, Array.Empty<ImplementsEdge>(), nodes);
    }

    [Test]
    public void A_feedback_cycle_through_an_event_raise_is_one_high_confidence_cycle()
    {
        // A raises an event delivered to handler H; H calls B; B raises an event delivered back to A — a
        // feedback loop closing through two event_raise delivery edges. {A, H, B} is one SCC.
        var graph = Graph(
            new CallEdge("M:N.A", "M:N.H", "handoff", "f.cs", 10, HandoffDispatcher: "event_raise"),
            new CallEdge("M:N.H", "M:N.B", "invocation", "f.cs", 20),
            new CallEdge("M:N.B", "M:N.A", "handoff", "f.cs", 30, HandoffDispatcher: "event_raise")
        );

        var cycles = FactCycleDeriver.DeriveEventCycles(graph, Dispatchers);

        cycles.Count.ShouldBe(1);
        cycles[0].Confidence.ShouldBe("high");
        cycles[0].Members.ShouldBe(new[] { "M:N.A", "M:N.B", "M:N.H" }); // sorted Ordinal
        cycles[0].DeliveryEdges.Count.ShouldBe(2);
    }

    [Test]
    public void A_cycle_whose_delivery_edge_came_from_a_heuristic_join_is_low_confidence()
    {
        var graph = Graph(
            new CallEdge("M:N.A", "M:N.H", "handoff", "f.cs", 10, HandoffDispatcher: "actor_tell"),
            new CallEdge("M:N.H", "M:N.A", "invocation", "f.cs", 20)
        );

        var cycles = FactCycleDeriver.DeriveEventCycles(graph, Dispatchers);

        cycles.Count.ShouldBe(1);
        cycles[0].Confidence.ShouldBe("low");
    }

    [Test]
    public void A_linear_delivery_chain_with_no_edge_back_is_no_cycle()
    {
        // A raises to H, H calls B — but nothing returns to A. No SCC > size 1, no self-loop: no cycle.
        var graph = Graph(
            new CallEdge("M:N.A", "M:N.H", "handoff", "f.cs", 10, HandoffDispatcher: "event_raise"),
            new CallEdge("M:N.H", "M:N.B", "invocation", "f.cs", 20)
        );

        FactCycleDeriver.DeriveEventCycles(graph, Dispatchers).ShouldBeEmpty();
    }

    [Test]
    public void A_pure_synchronous_recursion_cycle_is_not_an_event_cycle()
    {
        // A -> B -> A is a real SCC and a real cycle, but it traverses NO delivery edge — so it is not an
        // event_cycle (the hazard requires the loop to close through a publish→consumer delivery hop).
        var graph = Graph(
            new CallEdge("M:N.A", "M:N.B", "invocation", "f.cs", 10),
            new CallEdge("M:N.B", "M:N.A", "invocation", "f.cs", 20)
        );

        FactCycleDeriver.DeriveEventCycles(graph, Dispatchers).ShouldBeEmpty();
    }

    [Test]
    public void A_self_delivery_edge_is_a_size_one_cycle()
    {
        // A raises an event it itself handles: A --event_raise--> A is a size-1 SCC with a self delivery edge.
        var graph = Graph(new CallEdge("M:N.A", "M:N.A", "handoff", "f.cs", 10, HandoffDispatcher: "event_raise"));

        var cycles = FactCycleDeriver.DeriveEventCycles(graph, Dispatchers);

        cycles.Count.ShouldBe(1);
        cycles[0].Members.ShouldBe(new[] { "M:N.A" });
        cycles[0].Confidence.ShouldBe("high");
    }

    // Core recognizes NO delivery tag of its own (F6). A ruleset that marks no delivery rule `cycleDelivery`
    // therefore finds NOTHING — the same graph that yields a cycle above yields none with an empty map. That is
    // the honest degradation: without a declared mechanism, rig does not know what a delivery hop is here.
    [Test]
    public void With_no_cycle_carrying_delivery_rule_there_are_no_findings()
    {
        var graph = Graph(
            new CallEdge("M:N.A", "M:N.H", "handoff", "f.cs", 10, HandoffDispatcher: "event_raise"),
            new CallEdge("M:N.H", "M:N.A", "invocation", "f.cs", 20)
        );

        FactCycleDeriver.DeriveEventCycles(graph, new Dictionary<string, string>()).ShouldBeEmpty();
        FactCycleDeriver.DeriveEventCycles(graph, null).ShouldBeEmpty();
    }

    // A delivery edge whose tag no rule declared cycle-carrying is not a cycle hop, even though the SCC is
    // real — the tag vocabulary is the ruleset's, so an undeclared tag is simply unknown to the hunt.
    [Test]
    public void A_delivery_tag_no_rule_declared_is_not_a_cycle_hop()
    {
        var graph = Graph(
            new CallEdge("M:N.A", "M:N.H", "handoff", "f.cs", 10, HandoffDispatcher: "some_other_bus"),
            new CallEdge("M:N.H", "M:N.A", "invocation", "f.cs", 20)
        );

        FactCycleDeriver.DeriveEventCycles(graph, Dispatchers).ShouldBeEmpty();
    }

    // The dispatcher map itself is projected from the delivery RULES: only rules marked cycleDelivery appear,
    // an omitted joinConfidence means an exact join, and where two rules share a tag the more doubtful join
    // wins (disclosure is never upgraded away).
    [Test]
    public void CycleDeliveryDispatchers_takes_the_tags_and_join_confidence_from_the_rules()
    {
        DeliveryEndpoint Endpoint() => new(Source: "event-symbol", Resolve: "symbol");
        DeliveryRule Rule(string id, string tag, bool cycleDelivery, string? joinConfidence) =>
            new(
                Id: id,
                Tag: tag,
                Confidence: "exact",
                Producer: Endpoint(),
                Registration: Endpoint(),
                CycleDelivery: cycleDelivery,
                JoinConfidence: joinConfidence
            );

        var dispatchers = FactCycleDeriver.CycleDeliveryDispatchers([
            Rule("csharp-event", "event_raise", cycleDelivery: true, joinConfidence: "high"),
            Rule("implicit-exact", "bus_publish", cycleDelivery: true, joinConfidence: null),
            Rule("actors", "actor_tell", cycleDelivery: true, joinConfidence: "low"),
            Rule("not-a-cycle-hop", "sms_send", cycleDelivery: false, joinConfidence: "low"),
            Rule("actors-again", "actor_tell", cycleDelivery: true, joinConfidence: "high"),
        ]);

        dispatchers["event_raise"].ShouldBe("high");
        dispatchers["bus_publish"].ShouldBe("high"); // omitted joinConfidence = exact join
        dispatchers["actor_tell"].ShouldBe("low"); // the doubtful join wins the tie
        dispatchers.ShouldNotContainKey("sms_send"); // cycleDelivery:false is not a cycle hop
    }
}
