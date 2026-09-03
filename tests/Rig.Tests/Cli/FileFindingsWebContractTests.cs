using Rig.Cli.Commands;
using Rig.Cli.Effects;
using Rig.Cli.Services;
using Rig.Cli.Web;
using Rig.Domain.Data;
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
                    WitnessDepth: 6,
                    Guards: null,
                    DispatchBasis: "roslyn",
                    DispatchDegree: 1,
                    IterationDetail: "order in orders"
                ),
            ],
            CrossMethodDerived: true
        );

        var anchor = FileEffectsEndpoint.ToFindingsResponse(File, findings).Anchors.ShouldHaveSingleItem();

        anchor.Line.ShouldBe(232);
        anchor.Caller.ShouldBe("Orders.Load");
        anchor.WitnessDepth.ShouldBe(6);
        anchor.Confidence.ShouldBe("low"); // depth 6 -> a LEAD, and the overlay labels it as one
        anchor.DispatchBasis.ShouldBe("roslyn");
        anchor.DispatchDegree.ShouldBe(1);
    }

    // The evidence tier the note under the finding list is allowed to claim. Pinned by a test because the
    // four inputs are independent and the failure is SILENT: get it wrong and a guessed virtual hop reads as
    // a confirmed per-iteration call, which is the exact overclaim the tier exists to prevent.
    //
    // `guardPredicate` is the raw branch text; the test ENCODES it, because the tier decodes and an unencoded
    // string decodes to zero guards — a fixture that skipped the encoding would assert nothing.
    [Test]
    [Arguments(0, null, "order in orders", null, 0, "direct")] // unconditional in the loop, witness in the callee's body
    [Arguments(1, null, "order in orders", null, 0, "direct")] // one hop down, still no dispatch on the path
    [Arguments(0, "order.IsActive", "order in orders", null, 0, "candidate")] // the CALL may not run every iteration
    // THE 40% CASE: `foreach` contributes a control dependence about itself, whose predicate IS the iterated
    // collection. It does not make the call conditional, and 619 of 1,530 guarded MedDBase rows have no other
    // guard — so grading on the raw guard string downgraded two fifths of the guarded evidence.
    [Arguments(0, "orders", "order in orders", null, 0, "direct")]
    [Arguments(0, "orders", "order in orders.Where(o => o.Live)", null, 0, "candidate")] // a DIFFERENT collection is a real guard
    [Arguments(0, "count > 0", "count > 0", null, 0, "candidate")] // a `while` carries no " in ", so nothing is filtered
    [Arguments(0, null, "order in orders", "roslyn", 0, "candidate")] // exact hops, but dispatch was crossed
    [Arguments(4, null, "order in orders", null, 0, "candidate")] // real calls all the way, just further out
    [Arguments(0, null, "order in orders", "heuristic", 0, "inferred")] // a name/arity guess beats every other strength
    [Arguments(0, null, "order in orders", null, 3, "inferred")] // one source method fanned out to 3 targets
    [Arguments(9, "x", "order in orders", "heuristic", 12, "inferred")] // every doubt at once
    public void The_anchor_evidence_tier_grades_guards_dispatch_and_depth_together(
        int depth,
        string? guardPredicate,
        string iterationDetail,
        string? dispatchBasis,
        int dispatchDegree,
        string expected
    )
    {
        var guards = guardPredicate is null ? null : FactStructuralContext.EncodeGuards([(guardPredicate, true)]);

        var anchor = new CrossMethodAmplificationDataset.AnchorFinding(
            Caller: Enclosing,
            FilePath: File,
            Line: 232,
            IterationKind: "foreach",
            WitnessProvider: "efcore",
            WitnessOperation: "read",
            WitnessResource: "Profile",
            WitnessDepth: depth,
            Guards: guards,
            DispatchBasis: dispatchBasis,
            DispatchDegree: dispatchDegree,
            IterationDetail: iterationDetail
        );

        anchor.Evidence.ShouldBe(expected);

        // Evidence is a strict REFINEMENT of the calibrated depth tier, not a second independent axis: the
        // two can never disagree about which anchors are the strong ones.
        if (anchor.Evidence == "direct")
        {
            anchor.Confidence.ShouldBe("high");
        }

        // An empty guard string is the same fact as null (no control dependence), and a store that writes one
        // must not silently downgrade the row.
        (anchor with { Guards = guards is null ? "" : guards }).Evidence.ShouldBe(expected);
    }

    // The wire carries the tier AND the raw fields it came from, so the client can say WHY a row is not
    // direct without re-deriving the tier and drifting from this definition.
    [Test]
    public void An_inferred_anchor_reaches_the_wire_with_the_fields_that_caused_it()
    {
        var anchor = FileEffectsEndpoint
            .ToFindingsResponse(
                File,
                new FileFindingsQueryService.Findings(
                    [],
                    [],
                    [
                        new CrossMethodAmplificationDataset.AnchorFinding(
                            Caller: Enclosing,
                            FilePath: File,
                            Line: 377,
                            IterationKind: "foreach",
                            WitnessProvider: "db_command",
                            WitnessOperation: "execute",
                            WitnessResource: "Layout",
                            WitnessDepth: 2,
                            // Two guards, ENCODED as the store holds them, one of them the redundant foreach
                            // guard and one a real else-arm — so this pins the whole render, not just decoding.
                            Guards: FactStructuralContext.EncodeGuards([("nodes", true), ("node.HasKey", false)]),
                            DispatchBasis: "heuristic",
                            DispatchDegree: 5,
                            IterationDetail: "node in nodes"
                        ),
                    ],
                    CrossMethodDerived: true
                )
            )
            .Anchors.ShouldHaveSingleItem();

        anchor.Evidence.ShouldBe("inferred");
        anchor.DispatchBasis.ShouldBe("heuristic");
        anchor.DispatchDegree.ShouldBe(5);

        // DISPLAY text on the wire, never the encoded fact: the separators and the polarity flag are internal,
        // the client cannot decode them, and rendering the raw string leaked "\x1f1" into the review UI. The
        // loop-redundant `nodes` guard is dropped and the false-polarity arm is negated.
        anchor.Guards.ShouldBe("!node.HasKey");
        anchor.Guards.ShouldNotContain("");
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
