using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rig.Analysis;
using Rig.Analysis.Extraction;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Analysis;

// Guard POLARITY under negation. Companion to
// FactExtractorCaptureTests.Guard_predicate_is_the_full_source_condition_not_the_lowered_branch_operands,
// which pins the guard TEXT for &&/||/parens but contains no `!` case — the gap this file closes.
//
// The defect (2026-07-27, found reviewing MedDBase MR !11025): Roslyn folds a leading `!` OUT of the CFG
// branch value and inverts ConditionKind instead, so `if (!flag)` branches on `flag` with the polarity
// flipped. EncodedGuardsFor widens the guard TEXT back up through that `!` to recover the full source
// condition — and used to leave WhenTrue untouched, applying the negation twice. A call in the THEN arm of
// `if (!IsPersonMerge)` rendered `!!IsPersonMerge`: the condition for the arm that does NOT run. A reviewer
// reading the ⎇ column got the branch exactly backwards.
public sealed class GuardPolarityTests
{
    private static FactExtractionResult Extract(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Snippet.cs");
        var compilation = CSharpCompilation.Create(
            "Snippet",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var model = compilation.GetSemanticModel(tree);
        return FactExtractor.Extract(new SourceModel("Snippet", "Snippet.cs", tree, tree.GetRoot(), model), new SymbolStringCache());
    }

    // The extracted guard set for the `Foo()` call in `body`, as "<predicate>=<T|F>" joined by commas.
    private static string Guards(string body)
    {
        var src = "namespace App { class Svc { void H(bool a, bool b, bool c) { " + body + " } void Foo(){} } }";
        var r = Extract(src);
        var foo = r.References.Single(x => x.RefKind == "invocation" && x.TargetSymbolId.Contains("Foo"));
        return string.Join(
            ",",
            FactStructuralContext.DecodeGuards(foo.EnclosingGuards).Select(x => $"{x.Predicate}={(x.WhenTrue ? "T" : "F")}")
        );
    }

    [Test]
    public void A_negated_condition_in_the_then_arm_keeps_its_polarity()
    {
        // THE REGRESSION. `Foo()` runs when `a` is FALSE, i.e. when the source condition `!a` is TRUE.
        // Pre-fix this was "!a=F" — rendered `!!a`, the condition for the arm that does not run.
        Guards("if (!a) Foo();").ShouldBe("!a=T");

        // The MedDBase MR !11025 shape: a negated first operand in a conjunction. Pre-fix: "!a && b=F".
        Guards("if (!a && b) Foo();").ShouldBe("!a && b=T");

        // Negated operand in a disjunction. Pre-fix this produced TWO contradictory guards —
        // "!a || b=F,!a || b=T" (`!(!a||b) && (!a||b)`) — because the two lowered operands cross a
        // DIFFERENT number of `!` on the way up, so the flip is what makes them dedup to one.
        Guards("if (!a || b) Foo();").ShouldBe("!a || b=T");

        // Whole-condition negation, and the double negation that must cancel.
        Guards("if (!(a || b)) Foo();").ShouldBe("!(a || b)=T");
        Guards("if (!!a) Foo();").ShouldBe("!!a=T");
    }

    [Test]
    public void A_negated_condition_in_the_else_arm_is_negated_once_not_twice()
    {
        // `Foo()` runs when `!a` is FALSE. One negation from the else-arm, none doubled up.
        Guards("if (!a) {} else Foo();").ShouldBe("!a=F");
        Guards("if (!a && b) {} else Foo();").ShouldBe("!a && b=F");
    }

    [Test]
    public void Unnegated_conditions_are_unaffected_by_the_polarity_fix()
    {
        // Regression fence: the cases that were already CORRECT on the real store (verified against
        // MedDBase source — `if (FkDocument.HasValue)` and an early-return `if (… == null) return;`)
        // must not move. These mirror the pre-existing FactExtractorCaptureTests expectations.
        Guards("if (a) Foo();").ShouldBe("a=T");
        Guards("if (a || b) Foo();").ShouldBe("a || b=T");
        Guards("if (a && b) Foo();").ShouldBe("a && b=T");
        Guards("if ((a || b) && c) Foo();").ShouldBe("(a || b) && c=T");
        Guards("if (a || b) {} else Foo();").ShouldBe("a || b=F");
        Guards("if (a) { if (b) Foo(); }").ShouldBe("b=T");

        // The early-return shape: the call is control-dependent on the guard being FALSE.
        Guards("if (a) return; Foo();").ShouldBe("a=F");
    }
}
