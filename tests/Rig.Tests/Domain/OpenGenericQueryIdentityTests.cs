using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

public sealed class OpenGenericQueryIdentityTests
{
    private const string Caller = "M:N.Caller.Start";
    private const string GenericWithParameter = "M:N.Repo.Run``1(``0)";
    private const string GenericWithParameterFqn = "N.Repo.Run``1";
    private const string ParameterlessGeneric = "M:N.Repo.Flush``1";
    private const string ParameterlessGenericFqn = "N.Repo.Flush``1";
    private const string ConcreteBinding = "N.Work";

    [Test]
    public void Exact_open_generic_DocID_finds_the_concrete_forward_path()
    {
        var graph = MaterializedGraph(GenericWithParameter, "Run<T>(T value)");

        var raw = FactPathFinder.Find(graph, Caller, GenericWithParameter);

        raw.ShouldNotBeNull();
        raw!.Count.ShouldBe(2);
        raw[0].SymbolId.ShouldBe(Caller);
        MonomorphizedNodeId.IsMonomorphized(raw[1].SymbolId).ShouldBeTrue();
        MonomorphizedNodeId.BaseOf(raw[1].SymbolId).ShouldBe(GenericWithParameter);
        MonomorphCollapse.CollapsePath(raw).Select(step => step.SymbolId).ShouldBe([Caller, GenericWithParameter]);
    }

    [Test]
    public void Exact_open_generic_DocID_reverse_reaches_the_concrete_caller()
    {
        var graph = MaterializedGraph(GenericWithParameter, "Run<T>(T value)");

        var reachedBy = FactPathFinder.ReachedBy(graph, GenericWithParameter);

        reachedBy.Keys.ShouldContain(Caller);
        reachedBy[Caller].ShouldBe(1);
    }

    [Test]
    public void Full_DocID_and_param_free_FQN_select_the_same_base_and_concrete_executions()
    {
        var graph = MaterializedGraph(GenericWithParameter, "Run<T>(T value)");
        var nodes = Nodes(graph);

        var byDocId = FactPathFinder.Reaches(graph, GenericWithParameter).Keys.ToArray();
        var byFqn = FactPathFinder.Reaches(graph, GenericWithParameterFqn).Keys.ToArray();

        byDocId.ShouldBe(byFqn, ignoreOrder: true);
        byDocId.ShouldContain(GenericWithParameter);
        byDocId.ShouldContain(id => MonomorphizedNodeId.IsMonomorphized(id));
        FactPathFinder.DistinctMatchTargets(nodes, GenericWithParameter).ShouldBe([GenericWithParameterFqn]);
    }

    [Test]
    public void Parameterless_open_generic_FQN_includes_its_concrete_execution()
    {
        var graph = MaterializedGraph(ParameterlessGeneric, "Flush<T>()");
        var nodes = Nodes(graph);

        var byDocId = FactPathFinder.Reaches(graph, ParameterlessGeneric).Keys.ToArray();
        var byFqn = FactPathFinder.Reaches(graph, ParameterlessGenericFqn).Keys.ToArray();

        byDocId.ShouldBe(byFqn, ignoreOrder: true);
        byFqn.ShouldContain(ParameterlessGeneric);
        byFqn.ShouldContain(id => MonomorphizedNodeId.IsMonomorphized(id));
        FactPathFinder.DistinctMatchTargets(nodes, ParameterlessGenericFqn).ShouldBe([ParameterlessGenericFqn]);
    }

    [Test]
    public void Keyed_reverse_exact_open_generic_admits_and_forward_confirms_the_concrete_caller()
    {
        var view = Facts(GenericWithParameter, "Run<T>(T value)");
        var rules = new DemandForwardGraphRules(new ForwardCallProjectionRules(), [], []);

        var result = DemandReverseCallersGraph.Build(
            view,
            rules,
            new DemandReverseCallersGraphRequest(GenericWithParameter, int.MaxValue, FactPathFinder.TraversalMode.SyncCut)
        );
        var reachedBy = FactPathFinder.ReachedBy(result.Graph, GenericWithParameter);
        var exactTargets = reachedBy.Where(item => item.Value == 0).Select(item => item.Key).ToHashSet(StringComparer.Ordinal);

        reachedBy.Keys.ShouldContain(Caller);
        result.Graph.CallEdges.ShouldContain(edge => edge.Caller == Caller && MonomorphizedNodeId.IsMonomorphized(edge.Callee));
        FactPathFinder
            .SeedsReachTarget(
                result.Graph,
                [
                    [Caller],
                ],
                exactTargets,
                int.MaxValue,
                FactPathFinder.TraversalMode.SyncCut
            )
            .ShouldBe([true]);
    }

    private static DemandTestGraph Facts(string genericId, string signature) =>
        new DemandTestGraph()
            .Method(Caller, "Start()", "T:N.Caller")
            .Method(genericId, signature, "T:N.Repo")
            .Call(Caller, genericId, methodBinding: $"[\"C:{ConcreteBinding}\"]");

    private static Rig.Domain.Data.FactGraphData MaterializedGraph(string genericId, string signature)
    {
        var facts = Facts(genericId, signature);
        return FactPathFinder.ShapeGraph(facts.AsFactGraph(), [], [], [], facts.Signatures);
    }

    private static IReadOnlyCollection<string> Nodes(Rig.Domain.Data.FactGraphData graph) =>
        graph
            .Methods.Select(method => method.SymbolId)
            .Concat(graph.CallEdges.SelectMany(edge => new[] { edge.Caller, edge.Callee }))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
