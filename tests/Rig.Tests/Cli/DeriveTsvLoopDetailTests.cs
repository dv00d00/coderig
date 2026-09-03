using Rig.Cli.Commands;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class DeriveTsvLoopDetailTests
{
    private const string MultilineLoopDetail = "from p\r\n  in\tprofiles";

    [Test]
    public void Hazard_and_amplification_rows_collapse_loop_detail_without_changing_their_column_contracts()
    {
        var hazard = Finding(HazardKinds.NPlusOne);
        var amplification = Finding(HazardKinds.LoopedEffect, provider: "llblgen", operation: "read");

        var hazardRow = DeriveCommand.HazardTsvRow(hazard);
        var amplificationRow = DeriveCommand.AmplificationTsvRow(amplification);

        hazardRow.Split('\t').Length.ShouldBe(9);
        hazardRow.Split('\t')[8].ShouldBe("from p in profiles");
        amplificationRow.Split('\t').Length.ShouldBe(11);
        amplificationRow.Split('\t')[8].ShouldBe("from p in profiles");

        var output = string.Join('\n', hazardRow, amplificationRow);
        output.Split('\n').Length.ShouldBe(2);
        output.ShouldNotContain("\r");
    }

    [Test]
    public void Tsv_rendering_sanitizes_each_copy_without_mutating_the_finding_used_by_human_rendering()
    {
        var finding = Finding(HazardKinds.LoopedEffect, provider: "http", operation: "POST");

        DeriveCommand.AmplificationTsvRow(finding).ShouldContain("from p in profiles");

        finding.Detail.ShouldBe(MultilineLoopDetail);
        var human = new StringWriter();
        DeriveCommand.WriteAmplification(human, [finding], limit: 40);
        human.ToString().ShouldContain("Amplification (looped effects — structural inventory): 1");
        finding.Detail.ShouldBe(MultilineLoopDetail);
    }

    private static DeriveCommand.HazardFinding Finding(string type, string provider = "", string operation = "") =>
        new(
            Type: type,
            Confidence: "high",
            Reason: "effect_inside_loop",
            Context: "foreach",
            Detail: MultilineLoopDetail,
            Enclosing: "M:App.Profiles.Load",
            FilePath: "C:/repo/App/Profiles.cs",
            Line: 17,
            Provider: provider,
            Operation: operation
        );
}
