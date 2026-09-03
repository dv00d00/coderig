using Rig.Cli.Commands;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class DeriveCompilationHealthRowsTests
{
    private static readonly DeriveCommand.HazardFinding Finding = new(
        "race_window",
        "high",
        "unlocked_write",
        "Demo.State",
        "paired read",
        "M:Demo.Service.Run",
        "/src/Broken.cs",
        12,
        "db",
        "write"
    );

    [Test]
    public void Hazard_and_amplification_tsv_rows_append_binding_health_without_splitting_rows()
    {
        var hazard = DeriveCommand.HazardTsvRow(Finding, "compile_error");
        var amplification = DeriveCommand.AmplificationTsvRow(Finding, "compile_error");

        hazard.Split('\n').ShouldHaveSingleItem();
        amplification.Split('\n').ShouldHaveSingleItem();
        hazard.Split('\t').Length.ShouldBe(10);
        amplification.Split('\t').Length.ShouldBe(12);
        hazard.Split('\t')[^1].ShouldBe("compile_error");
        amplification.Split('\t')[^1].ShouldBe("compile_error");
    }

    [Test]
    public void Human_finding_rows_mark_only_compile_error_files()
    {
        var output = new StringWriter();
        DeriveCommand.WriteHazards(output, [Finding], 20, file => file.EndsWith("Broken.cs", StringComparison.Ordinal));

        output.ToString().ShouldContain("~compile-error");
        var clean = new StringWriter();
        DeriveCommand.WriteHazards(clean, [Finding], 20, _ => false);
        clean.ToString().ShouldNotContain("~compile-error");
    }
}
