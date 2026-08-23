using Rig.Analysis.Rules;
using Rig.Cli.Commands;
using Rig.Cli.Rendering;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Cli;

// Mirrors the real LiveQueryRunner.RunRequestAsync store case: six method-group lambdas share the same
// parameterful enclosing method, so ordinary ShortName used to collapse every row to one visual identity.
public sealed class TreeLambdaIdentityTests
{
    private const string MethodId =
        "M:Rig.Cli.Live.LiveQueryRunner.RunRequestAsync(Rig.Cli.Live.LiveQueryRequest,Rig.Cli.Live.LiveFactSource,System.String)";

    private static readonly IReadOnlyDictionary<string, List<string>> NoEffects = new Dictionary<string, List<string>>(
        StringComparer.Ordinal
    );

    private static TraceNode Root() =>
        new(
            MethodId,
            "entry",
            null,
            null,
            Enumerable.Range(0, 6).Select(i => new TraceNode($"{MethodId}~λ{i}", "methodGroup", null, null, [])).ToArray()
        );

    private static IReadOnlyList<string> Lines(StringWriter output) =>
        output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();

    [Test]
    public void Pretty_llm_and_llm_ids_preserve_the_same_six_distinct_lambda_suffixes()
    {
        var root = Root();

        var pretty = new StringWriter();
        TreeRenderer.RenderTreeNode(root, new TreeRenderContext(pretty, NoEffects, FactRenderRules.Empty, NoEffects));

        var llm = new StringWriter();
        LlmSummaryRenderer.Render(
            [root],
            NoEffects,
            LlmSummaryRenderer.LlmProjection.Full,
            llm,
            suppress: LlmSummaryRenderer.SuppressSet.None
        );

        var llmIds = new StringWriter();
        LlmSummaryRenderer.RenderWithIds(
            [root],
            NoEffects,
            LlmSummaryRenderer.LlmProjection.Full,
            llmIds,
            suppress: LlmSummaryRenderer.SuppressSet.None
        );

        for (var i = 0; i < 6; i++)
        {
            var expected = $"LiveQueryRunner.RunRequestAsync~λ{i}";
            Lines(pretty).Count(line => line.Contains(expected, StringComparison.Ordinal)).ShouldBe(1);
            Lines(llm).Count(line => line.Split('\t').Contains(expected)).ShouldBe(1);
            Lines(llmIds).Count(line => line.Split('\t').Contains(expected)).ShouldBe(1);
        }
    }

    [Test]
    public void Tsv_keeps_the_six_exact_lambda_doc_ids_unchanged()
    {
        var output = new StringWriter();
        TreeCommand.EmitTsvNode(
            Root(),
            depth: 0,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, (string? File, int Line)>(StringComparer.Ordinal),
            output
        );

        var ids = Lines(output).Select(line => line.Split('\t')[1]).ToArray();
        ids.ShouldBe([MethodId, .. Enumerable.Range(0, 6).Select(i => $"{MethodId}~λ{i}")]);
    }

    [Test]
    public void Shared_tree_identity_does_not_duplicate_a_parameterless_lambda_suffix()
    {
        SymbolNameFormatter.ShortNamePreservingLambda("M:App.Worker.Run~λ3").ShouldBe("Worker.Run~λ3");
        SymbolNameFormatter.ShortNamePreservingLambda("M:App.Worker.Run(System.String)~λ3").ShouldBe("Worker.Run~λ3");
    }
}
