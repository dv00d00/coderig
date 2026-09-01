using Rig.Cli.Commands;
using Rig.Cli.Effects;
using Rig.Cli.Services;
using Rig.Cli.Web;
using Shouldly;

namespace Rig.Tests.Cli;

// The /api/file-findings wire contract. Worth pinning for one reason above all: the finding records name two
// fields `Reason` and `Context`, and on the wire they are the hazard SUBTYPE and the KEY. Swapping them would
// be invisible in review (both are short lowercase strings) and would make every tooltip in the overlay wrong.
public sealed class FileFindingsWebContractTests
{
    private const string File = "/repo/src/Demo/Orders.cs";
    private const string Enclosing = "M:Demo.Orders.Load(System.Int32)";

    [Test]
    public void Hazard_rows_carry_subtype_and_key_in_the_named_fields()
    {
        var findings = new FileFindingsQueryService.Findings(
            [
                new DeriveCommand.HazardFinding(
                    Type: "n_plus_1",
                    Confidence: "high",
                    Reason: "looped_read_with_varying_key",
                    Context: "reviewer",
                    Detail: "reviewer in newReviewers",
                    Enclosing: Enclosing,
                    FilePath: File,
                    Line: 671
                ),
            ],
            [],
            [],
            CrossMethodDerived: true
        );

        var hazard = FileEffectsEndpoint.ToFindingsResponse(File, findings).Hazards.ShouldHaveSingleItem();

        hazard.Type.ShouldBe("n_plus_1");
        hazard.Confidence.ShouldBe("high");
        hazard.Subtype.ShouldBe("looped_read_with_varying_key"); // Reason
        hazard.Key.ShouldBe("reviewer"); // Context
        hazard.Detail.ShouldBe("reviewer in newReviewers");
        hazard.Line.ShouldBe(671);
        // Short display name, not the DocID: the overlay has ~200px for it.
        hazard.Enclosing.ShouldBe("Orders.Load");
    }

    [Test]
    public void Amplification_rows_carry_the_provider_operation_the_tier_exists_for()
    {
        var findings = new FileFindingsQueryService.Findings(
            [],
            [
                new DeriveCommand.HazardFinding(
                    Type: "looped_effect",
                    Confidence: "high",
                    Reason: "effect_inside_loop",
                    Context: "foreach",
                    Detail: "reviewer in newReviewers",
                    Enclosing: Enclosing,
                    FilePath: File,
                    Line: 671,
                    Provider: "entity_cache",
                    Operation: "read"
                ),
            ],
            [],
            CrossMethodDerived: true
        );

        var amplification = FileEffectsEndpoint.ToFindingsResponse(File, findings).Amplifications.ShouldHaveSingleItem();

        amplification.Provider.ShouldBe("entity_cache");
        amplification.Operation.ShouldBe("read");
        amplification.Iteration.ShouldBe("reviewer in newReviewers"); // Detail — the loop a reader can see
        amplification.Key.ShouldBe("foreach"); // Context — the iteration kind
    }

    // The anchor's confidence IS its witness depth, and the server sends both so the two cannot disagree.
    [Test]
    public void Anchor_rows_send_the_confidence_the_domain_derived()
    {
        var findings = new FileFindingsQueryService.Findings(
            [],
            [],
            [
                new CrossMethodAmplificationDataset.AnchorFinding(
                    Caller: Enclosing,
                    FilePath: File,
                    Line: 232,
                    IterationKind: "query",
                    WitnessProvider: "entity_cache",
                    WitnessOperation: "read",
                    WitnessResource: "Profile",
                    WitnessDepth: 6
                ),
            ],
            CrossMethodDerived: true
        );

        var anchor = FileEffectsEndpoint.ToFindingsResponse(File, findings).Anchors.ShouldHaveSingleItem();

        anchor.Line.ShouldBe(232);
        anchor.Caller.ShouldBe("Orders.Load");
        anchor.WitnessDepth.ShouldBe(6);
        anchor.Confidence.ShouldBe("low"); // depth 6 -> a LEAD, and the overlay labels it as one
    }

    // The distinction the overlay's disclosure rests on: no anchors because there are none, versus no anchors
    // because the rule set never looked. A count cannot express that; the flag must.
    [Test]
    public void An_undeclared_cross_method_section_is_reported_as_off_not_empty()
    {
        FileEffectsEndpoint
            .ToFindingsResponse(File, new FileFindingsQueryService.Findings([], [], [], CrossMethodDerived: false))
            .CrossMethodAvailable.ShouldBeFalse();

        FileEffectsEndpoint
            .ToFindingsResponse(File, new FileFindingsQueryService.Findings([], [], [], CrossMethodDerived: true))
            .CrossMethodAvailable.ShouldBeTrue();
    }
}
