using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class DemandMonomorphizedLocalityTests
{
    [Test]
    public void Fifty_disconnected_instantiations_cannot_cap_the_reachable_cat_instantiation()
    {
        var sparse = BaseGraph();
        var crowded = BaseGraph();
        for (var i = 0; i < 50; i++)
        {
            crowded
                .Method($"M:N.Disconnected{i}.Run", "Run()", $"T:N.Disconnected{i}")
                .Call($"M:N.Disconnected{i}.Run", "M:N.G", methodBinding: $"[\"C:N.T{i}\"]");
        }

        var sparseSource = sparse.Source();
        var crowdedSource = crowded.Source();
        var sparseMono = sparseSource.CallsFrom("M:N.Root.Run").Single().Callee;
        var crowdedMono = crowdedSource.CallsFrom("M:N.Root.Run").Single().Callee;
        var sparseBody = sparseSource.CallsFrom(sparseMono);
        var crowdedBody = crowdedSource.CallsFrom(crowdedMono);

        crowdedMono.ShouldBe(sparseMono);
        crowdedBody.ShouldBe(sparseBody);
        crowdedBody.Single().ReceiverType.ShouldBe("N.Cat");
        crowded.RequestedCallers.ShouldBe(["M:N.Root.Run", "M:N.G"]);
        crowded.RequestedCallers.ShouldNotContain(caller => caller.Contains("Disconnected", StringComparison.Ordinal));
        var crowdedDiagnostics = crowdedSource.DiagnosticsSnapshot();
        var sparseDiagnostics = sparseSource.DiagnosticsSnapshot();
        crowdedDiagnostics.ShouldBe(sparseDiagnostics);
        crowdedDiagnostics.Reads.ForwardCallers.ShouldBe(new DemandReadMetric(2, 2));
        crowdedDiagnostics.Adjacency.ShouldBe(new DemandAdjacencyDiagnostics(CacheHits: 0, CacheMisses: 2, ProjectedBaseEdges: 2));
        crowdedDiagnostics.Precision.DistinctInstantiations.ShouldBe(1);
    }

    [Test]
    public void Fifty_one_reachable_instantiations_keep_the_51st_base_edge_as_a_recall_safe_superset()
    {
        var graph = new DemandTestGraph()
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method("M:N.G", "G<T>()", "T:N.Repo")
            .Call("M:N.G", "M:N.IAnimal.Act", receiver: "T");
        for (var i = 0; i < 51; i++)
        {
            graph.Call("M:N.Root.Run", "M:N.G", methodBinding: $"[\"C:N.T{i:00}\"]", line: i + 1);
        }
        var source = graph.Source();

        var edges = source.CallsFrom("M:N.Root.Run");
        var collapsed = edges.Select(edge => MonomorphizedNodeId.BaseOf(edge.Callee)).Distinct(StringComparer.Ordinal).ToArray();

        edges.Count(edge => MonomorphizedNodeId.IsMonomorphized(edge.Callee)).ShouldBe(50);
        edges.Count(edge => edge.Callee == "M:N.G").ShouldBe(1);
        collapsed.ShouldBe(["M:N.G"]);
        var diagnostics = source.DiagnosticsSnapshot();
        diagnostics.Precision.CappedMethodIds.ShouldBe(["M:N.G"]);
        diagnostics.Precision.PerMethodFallbackEdges.ShouldBe(1);
        diagnostics.Precision.DistinctInstantiations.ShouldBe(50);

        // The rejected base node still exposes the unspecialized receiver, so the existing dispatch engine
        // may conservatively fan out rather than losing any implementation.
        source.CallsFrom("M:N.G").Single().ReceiverType.ShouldBe("T");
    }

    private static DemandTestGraph BaseGraph() =>
        new DemandTestGraph()
            .Method("M:N.Root.Run", "Run()", "T:N.Root")
            .Method("M:N.G", "G<T>()", "T:N.Repo")
            .Call("M:N.Root.Run", "M:N.G", methodBinding: "[\"C:N.Cat\"]")
            .Call("M:N.G", "M:N.IAnimal.Act", receiver: "T");
}
