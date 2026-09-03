using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class EventSubscriptionHandoffMemoTests
{
    private static readonly EventSubscriptionSite FirstSite = new("M:N.R.Register", "f.cs", 10);
    private static readonly EventSubscriptionSite SecondSite = new("M:N.R.Register", "f.cs", 20);

    [Test]
    public void A_stable_graph_and_site_set_pair_reuses_the_same_correctly_marked_graph()
    {
        var graph = Graph();
        var sites = new HashSet<EventSubscriptionSite> { FirstSite };

        var first = FactPathFinder.MarkEventSubscriptionHandoffs(graph, sites);
        var second = FactPathFinder.MarkEventSubscriptionHandoffs(graph, sites);

        second.ShouldBeSameAs(first);
        EdgeAt(first, 10).Kind.ShouldBe(EdgeKinds.Handoff);
        EdgeAt(first, 10).HandoffDispatcher.ShouldBe("event");
        EdgeAt(first, 20).Kind.ShouldBe(EdgeKinds.MethodGroup);
    }

    [Test]
    public void An_equivalent_but_different_site_set_instance_gets_its_own_rewrite()
    {
        var graph = Graph();
        var firstSites = new HashSet<EventSubscriptionSite> { FirstSite };
        var equivalentSites = new HashSet<EventSubscriptionSite> { FirstSite };

        var first = FactPathFinder.MarkEventSubscriptionHandoffs(graph, firstSites);
        var equivalent = FactPathFinder.MarkEventSubscriptionHandoffs(graph, equivalentSites);

        equivalent.ShouldNotBeSameAs(first);
        EdgeAt(equivalent, 10).Kind.ShouldBe(EdgeKinds.Handoff);
        FactPathFinder.MarkEventSubscriptionHandoffs(graph, equivalentSites).ShouldBeSameAs(equivalent);
    }

    [Test]
    public void A_different_site_set_does_not_reuse_a_stale_classification()
    {
        var graph = Graph();
        var first = FactPathFinder.MarkEventSubscriptionHandoffs(graph, new HashSet<EventSubscriptionSite> { FirstSite });
        var secondSites = new HashSet<EventSubscriptionSite> { SecondSite };

        var second = FactPathFinder.MarkEventSubscriptionHandoffs(graph, secondSites);

        second.ShouldNotBeSameAs(first);
        EdgeAt(second, 10).Kind.ShouldBe(EdgeKinds.MethodGroup);
        EdgeAt(second, 20).Kind.ShouldBe(EdgeKinds.Handoff);
        EdgeAt(second, 20).HandoffDispatcher.ShouldBe("event");
    }

    [Test]
    public void An_empty_site_set_returns_the_original_graph()
    {
        var graph = Graph();

        var marked = FactPathFinder.MarkEventSubscriptionHandoffs(graph, new HashSet<EventSubscriptionSite>());

        marked.ShouldBeSameAs(graph);
    }

    private static FactGraphData Graph()
    {
        var edges = new[]
        {
            new CallEdge("M:N.R.Register", "M:N.H.OnFirst", EdgeKinds.MethodGroup, "f.cs", 10),
            new CallEdge("M:N.R.Register", "M:N.H.OnSecond", EdgeKinds.MethodGroup, "f.cs", 20),
        };
        var methods = edges
            .SelectMany(e => new[] { e.Caller, e.Callee })
            .Distinct(StringComparer.Ordinal)
            .Select(id => new MethodRef(id, id, null))
            .ToArray();
        return new FactGraphData(edges, Array.Empty<ImplementsEdge>(), methods);
    }

    private static CallEdge EdgeAt(FactGraphData graph, int line) => graph.CallEdges.Single(e => e.Line == line);
}
