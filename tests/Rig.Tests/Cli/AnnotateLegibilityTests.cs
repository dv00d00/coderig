using Rig.Cli.Commands;
using Rig.Cli.Rendering;
using Rig.Cli.Services;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class AnnotateLegibilityTests
{
    [Test]
    public void Human_header_leads_with_present_families_and_explains_both_counts()
    {
        var lens = Lens();
        var output = new StringWriter();

        AnnotateCommand.RenderHeader(output, lens);

        var nl = Environment.NewLine;
        output
            .ToString()
            .ShouldBe(
                $"Work.cs  1 effectful method declaration(s), 1 distinct marked source line(s){nl}"
                    + $"  /repo/src/Work.cs{nl}"
                    + $"  families present: db io  (requested but absent: cache rpc){nl}"
                    + nl
            );
    }

    [Test]
    public void Human_footer_explains_method_and_line_distance_grains()
    {
        var lens = Lens();
        var output = new StringWriter();

        AnnotateCommand.RenderFooter(output, lens);

        var nl = Environment.NewLine;
        output
            .ToString()
            .ShouldBe(
                nl
                    + $"  line precision only — several calls on one line share that line's badges{nl}"
                    + $"  distances: method badges start at the method; line badges start at the target/direct site — a visible in-solution call normally adds 1 at method grain{nl}"
                    + $"  no column facts — a badge marks the LINE, not the expression{nl}"
            );
    }

    [Test]
    public void Tsv_fact_rows_remain_the_existing_wire_shape()
    {
        var lens = Lens();
        var output = new StringWriter();

        AnnotateCommand.WriteFactRows(output, lens);

        var nl = Environment.NewLine;
        output
            .ToString()
            .ShouldBe(
                $"method\t7\t14\tRun\tdb:1 io!\tM:Demo.Work.Run{nl}"
                    + $"site\t10\tio!\tFile.WriteAllText{nl}"
            );
    }

    private static FileEffectLens.LensModel Lens()
    {
        const string methodId = "M:Demo.Work.Run";
        var artifact = new FileEffectsQueryService.Artifact(
            new FileEffectReadModel(
                "/repo/src/Work.cs",
                ["rpc", "io", "db", "cache"],
                [new FileEffectMethod(methodId, [new FileEffectAggregate("io", 0), new FileEffectAggregate("db", 1)])],
                [
                    new FileEffectCallSite(
                        methodId,
                        "M:System.IO.File.WriteAllText(System.String,System.String)",
                        10,
                        [new FileEffectAggregate("io", 0)]
                    ),
                ]
            ),
            new Dictionary<string, FileEffectsQueryService.MethodLocation>(StringComparer.Ordinal)
            {
                [methodId] = new(methodId, "Run", "void Run()", 7, 14),
            }
        );
        return FileEffectLens.Project(artifact);
    }
}
