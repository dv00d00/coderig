using Rig.Analysis.Rules;
using Rig.Cli.Commands;
using Rig.Cli.Rendering;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Cli;

// --limit bounds TraceNode data rows, not format headers or pretty-mode effect provenance leaves. These
// tests stay Roslyn/storage-free and drive the shared forest through every tree data-node renderer.
public sealed class TreeBudgetContractTests
{
    private static readonly IReadOnlyDictionary<string, List<string>> NoEffects = new Dictionary<string, List<string>>(
        StringComparer.Ordinal
    );

    private static MethodRef Method(string id) => new(id, id, null);

    private static CallEdge Edge(string caller, string callee, int line) => new(caller, callee, "invocation", "f.cs", line);

    private static FactGraphData Graph(params CallEdge[] edges)
    {
        var methods = edges.SelectMany(e => new[] { e.Caller, e.Callee }).Distinct(StringComparer.Ordinal).Select(Method).ToArray();
        return new FactGraphData(edges, [], methods);
    }

    private static IReadOnlyList<string> Lines(StringWriter output) =>
        output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();

    private static IReadOnlyList<TraceNode> HighFanoutForest(int limit) =>
        FactPathFinder.BuildTree(
            Graph(
                Edge("M:App.Root.Run", "M:App.Child.A", 1),
                Edge("M:App.Root.Run", "M:App.Child.B", 2),
                Edge("M:App.Root.Run", "M:App.Child.C", 3),
                Edge("M:App.Root.Run", "M:App.Child.D", 4),
                Edge("M:App.Root.Run", "M:App.Child.E", 5),
                Edge("M:App.Root.Run", "M:App.Child.F", 6),
                Edge("M:App.Root.Run", "M:App.Child.G", 7),
                Edge("M:App.Root.Run", "M:App.Child.H", 8)
            ),
            "M:App.Root.Run",
            maxNodes: limit
        );

    [Test]
    public void High_fanout_never_materializes_or_renders_more_than_the_limit_in_any_format()
    {
        var roots = HighFanoutForest(limit: 5);
        var nodes = Flatten(roots).ToArray();

        nodes.Length.ShouldBe(5);
        nodes.Select(n => n.SymbolId).ShouldBe(["M:App.Root.Run", "M:App.Child.A", "M:App.Child.B", "M:App.Child.C", "M:App.Child.D"]);
        nodes[^1].TruncationCause.ShouldBe(TruncationCause.BudgetCapped);
        nodes.ShouldNotContain(n =>
            n.SymbolId == "M:App.Child.E" || n.SymbolId == "M:App.Child.F" || n.SymbolId == "M:App.Child.G" || n.SymbolId == "M:App.Child.H"
        );

        var pretty = new StringWriter();
        TreeRenderer.RenderTreeNode(roots.Single(), new TreeRenderContext(pretty, NoEffects, FactRenderRules.Empty, NoEffects));
        Lines(pretty).Count.ShouldBe(5);
        Lines(pretty)[^1].ShouldContain("⋯elided");

        var tsv = new StringWriter();
        TreeCommand.EmitTsvNode(
            roots.Single(),
            depth: 0,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, (string? File, int Line)>(StringComparer.Ordinal),
            tsv
        );
        Lines(tsv).Count.ShouldBe(5);

        var llm = new StringWriter();
        LlmSummaryRenderer.Render(
            roots,
            NoEffects,
            LlmSummaryRenderer.LlmProjection.Full,
            llm,
            suppress: LlmSummaryRenderer.SuppressSet.None
        );
        Lines(llm).Skip(1).Count().ShouldBe(5, "the header is not a tree data node");
        Lines(llm)[^1].ShouldEndWith("budget-capped");

        var llmIds = new StringWriter();
        LlmSummaryRenderer.RenderWithIds(
            roots,
            NoEffects,
            LlmSummaryRenderer.LlmProjection.Full,
            llmIds,
            suppress: LlmSummaryRenderer.SuppressSet.None
        );
        Lines(llmIds).Skip(1).Count().ShouldBe(5, "the header is not a tree data node");
        Lines(llmIds)[^1].ShouldEndWith("budget-capped");
    }

    [Test]
    public void Limit_one_emits_only_the_budget_capped_root()
    {
        var roots = HighFanoutForest(limit: 1);
        var root = roots.ShouldHaveSingleItem();

        root.SymbolId.ShouldBe("M:App.Root.Run");
        root.TruncationCause.ShouldBe(TruncationCause.BudgetCapped);
        root.Children.ShouldBeEmpty();

        var pretty = new StringWriter();
        TreeRenderer.RenderTreeNode(root, new TreeRenderContext(pretty, NoEffects, FactRenderRules.Empty, NoEffects));
        Lines(pretty).ShouldHaveSingleItem().ShouldContain("⋯elided");

        var llm = new StringWriter();
        LlmSummaryRenderer.Render(roots, NoEffects, LlmSummaryRenderer.LlmProjection.Full, llm, LlmSummaryRenderer.SuppressSet.None);
        Lines(llm).Count.ShouldBe(2);

        var llmIds = new StringWriter();
        LlmSummaryRenderer.RenderWithIds(
            roots,
            NoEffects,
            LlmSummaryRenderer.LlmProjection.Full,
            llmIds,
            LlmSummaryRenderer.SuppressSet.None
        );
        Lines(llmIds).Count.ShouldBe(2);
    }

    [Test]
    public void Depth_cap_and_seen_reentry_keep_their_specific_causes_when_budget_is_available()
    {
        var chain = Graph(Edge("M:A", "M:B", 1), Edge("M:B", "M:C", 2));
        var depthCapped = FactPathFinder.BuildTree(chain, "M:A", maxDepth: 1, maxNodes: 10).Single().Children.Single();
        depthCapped.TruncationCause.ShouldBe(TruncationCause.DepthCapped);

        var diamond = Graph(Edge("M:A", "M:B", 1), Edge("M:A", "M:C", 2), Edge("M:B", "M:D", 3), Edge("M:C", "M:D", 4));
        var repeated = FactPathFinder
            .BuildTree(diamond, "M:A", maxNodes: 20)
            .Single()
            .Children.Single(n => n.SymbolId == "M:C")
            .Children.Single();
        repeated.SymbolId.ShouldBe("M:D");
        repeated.TruncationCause.ShouldBe(TruncationCause.AlreadyExpanded);
    }

    private static IEnumerable<TraceNode> Flatten(IEnumerable<TraceNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Flatten(root.Children))
            {
                yield return child;
            }
        }
    }
}
