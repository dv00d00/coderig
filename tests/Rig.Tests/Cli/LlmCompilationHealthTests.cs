using Rig.Cli.Rendering;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class LlmCompilationHealthTests
{
    private const string Broken = "M:App.Service.Broken()";
    private const string Clean = "M:App.Service.Clean()";

    [Test]
    public void Llm_flags_only_the_affected_node_without_changing_columns()
    {
        var output = Render(ids: false);
        var lines = Lines(output);

        lines[0].Split('\t').Length.ShouldBe(6);
        lines[1].Split('\t').Length.ShouldBe(6);
        lines[2].Split('\t').Length.ShouldBe(6);
        lines[1].Split('\t')[5].ShouldBe("compile-error");
        lines[2].Split('\t')[5].ShouldBe("");
    }

    [Test]
    public void Llm_ids_flags_only_the_affected_node_without_changing_columns()
    {
        var output = Render(ids: true);
        var lines = Lines(output);

        lines[0].Split('\t').Length.ShouldBe(8);
        lines[1].Split('\t').Length.ShouldBe(8);
        lines[2].Split('\t').Length.ShouldBe(8);
        lines[1].Split('\t')[7].ShouldBe("compile-error");
        lines[2].Split('\t')[7].ShouldBe("");
    }

    [Arguments(false)]
    [Arguments(true)]
    [Test]
    public void Clean_parent_inherits_compile_error_from_a_suppressed_child_effect(bool ids)
    {
        const string parent = "M:App.Service.Run()";
        const string suppressedChild = "M:App.Service.Run~λ1()";
        var roots = new[]
        {
            new TraceNode(
                parent,
                "invocation",
                null,
                null,
                [new TraceNode(suppressedChild, "invocation", null, null, [], CallSites: 1)],
                CallSites: 1
            ),
        };
        var effects = new Dictionary<string, List<string>>(StringComparer.Ordinal) { [suppressedChild] = ["db:read"] };
        var output = new StringWriter();
        var brokenEffects = new HashSet<string>([suppressedChild], StringComparer.Ordinal);

        if (ids)
        {
            LlmSummaryRenderer.RenderWithIds(
                roots,
                effects,
                LlmSummaryRenderer.LlmProjection.Full,
                output,
                compileErrorSymbols: new HashSet<string>(StringComparer.Ordinal),
                compileErrorEffectSymbols: brokenEffects
            );
        }
        else
        {
            LlmSummaryRenderer.Render(
                roots,
                effects,
                LlmSummaryRenderer.LlmProjection.Full,
                output,
                compileErrorSymbols: new HashSet<string>(StringComparer.Ordinal),
                compileErrorEffectSymbols: brokenEffects
            );
        }

        var row = Lines(output.ToString())[1].Split('\t');
        row[^2].ShouldBe("db:read");
        row[^1].ShouldBe("compile-error");
    }

    private static string Render(bool ids)
    {
        var roots = new[]
        {
            new TraceNode(Broken, "invocation", null, null, [], CallSites: 1),
            new TraceNode(Clean, "invocation", null, null, [], CallSites: 1),
        };
        var output = new StringWriter();
        var effects = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var compileErrors = new HashSet<string>([Broken], StringComparer.Ordinal);
        if (ids)
        {
            LlmSummaryRenderer.RenderWithIds(
                roots,
                effects,
                LlmSummaryRenderer.LlmProjection.Full,
                output,
                LlmSummaryRenderer.SuppressSet.None,
                compileErrorSymbols: compileErrors
            );
        }
        else
        {
            LlmSummaryRenderer.Render(
                roots,
                effects,
                LlmSummaryRenderer.LlmProjection.Full,
                output,
                LlmSummaryRenderer.SuppressSet.None,
                compileErrorSymbols: compileErrors
            );
        }

        return output.ToString();
    }

    private static string[] Lines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();
}
