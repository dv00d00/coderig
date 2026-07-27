using Rig.Cli.Commands;
using Rig.Cli.Impact;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Cli;

// The pure classification behind `impact`'s guard_condition_delta rows: how a control-dependence condition is
// normalized, split into conjuncts, and compared across two stores.
//
// Why this is conjunct-set containment rather than string containment: the guard text stored at index is RAW
// SOURCE — newlines, original indentation, and any comment sitting inside the condition's span. MedDBase MR
// !11025's guard is 230 characters and carries `// no auditing for documents anymore, …` BETWEEN its two
// conjuncts. Substring/prefix matching on that is fragile, and a comment-only edit would read as a behavioural
// change. See docs/backlog/todo/impact-guard-delta-for-predicate-only-changes.md.
public sealed class GuardConditionDiffTests
{
    // The real MR !11025 head condition, verbatim from the store (`sqlite3 … select EnclosingGuards`) —
    // interleaved comment, embedded newlines and source indentation all included on purpose.
    private const string MrHeadCondition = """
        !IsPersonMerge &&
                        // no auditing for documents anymore, we plan to stop using PERSON_EVENT for documents completely
                        (!FkDocument.HasValue || !Settings.DocumentEventCommentsStopPersonEventAudits)
        """;

    private const string MrBaseCondition = "!IsPersonMerge";

    // Encode a guard set the way the extractor does, so these tests exercise the real decode path.
    private static string Encode(params (string Predicate, bool WhenTrue)[] guards) => FactStructuralContext.EncodeGuards(guards)!;

    [Test]
    public void The_motivating_MR_shape_classifies_as_NARROWED()
    {
        // THE CASE THE FEATURE EXISTS FOR. Base gates on one clause; head ANDs a second one on. No call and no
        // effect changed, so ep_effect_* is empty and --expect-no-effect-change passes — this verdict is the
        // only signal that the audit stopped firing for documents.
        var baseConjuncts = GuardConditionDiff.Conjuncts(Encode((MrBaseCondition, true)));
        var headConjuncts = GuardConditionDiff.Conjuncts(Encode((MrHeadCondition, true)));

        GuardConditionDiff.Classify(baseConjuncts, headConjuncts).ShouldBe(GuardVerdict.Narrowed);
        // ...and the reverse direction is WIDENED, not "changed" — the relation is symmetric.
        GuardConditionDiff.Classify(headConjuncts, baseConjuncts).ShouldBe(GuardVerdict.Widened);

        // The head splits into exactly the two source clauses, comment stripped and whitespace collapsed, so
        // the rendered row is single-line and greppable rather than a 230-char multi-line blob.
        headConjuncts.Count.ShouldBe(2);
        headConjuncts.ShouldContain("!IsPersonMerge");
        headConjuncts.ShouldContain("(!FkDocument.HasValue || !Settings.DocumentEventCommentsStopPersonEventAudits)");
        string.Join(" && ", headConjuncts).ShouldNotContain("no auditing for documents");
        string.Join(" && ", headConjuncts).ShouldNotContain("\n");
    }

    [Test]
    public void A_comment_or_reformat_only_edit_is_NOT_a_condition_change()
    {
        // The robustness property that makes this safe to gate CI on. All three spellings of the same predicate
        // must compare equal, or every reformat would trip --expect-no-guard-narrowing.
        var a = GuardConditionDiff.Conjuncts(Encode(("a && b", true)));
        var b = GuardConditionDiff.Conjuncts(Encode(("a &&\n    // explain why\n    b", true)));
        var c = GuardConditionDiff.Conjuncts(Encode(("a   &&\t/* block */ b", true)));

        GuardConditionDiff.Classify(a, b).ShouldBeNull(); // null = unchanged, emits no row
        GuardConditionDiff.Classify(a, c).ShouldBeNull();
        GuardConditionDiff.Classify(b, c).ShouldBeNull();
    }

    [Test]
    public void Verdicts_cover_appearing_vanishing_and_incomparable_conditions()
    {
        var none = GuardConditionDiff.Conjuncts(null); // unguarded edge == empty conjunct set
        var one = GuardConditionDiff.Conjuncts(Encode(("a", true)));
        var other = GuardConditionDiff.Conjuncts(Encode(("z", true)));

        none.ShouldBeEmpty();
        // A guard APPEARING on a previously must-run edge is a narrowing, and vanishing is a widening — both
        // fall out of the same subset rule, which is why unguarded is modelled as the empty set rather than a
        // special case.
        GuardConditionDiff.Classify(none, one).ShouldBe(GuardVerdict.Narrowed);
        GuardConditionDiff.Classify(one, none).ShouldBe(GuardVerdict.Widened);
        // Neither contains the other: honest fallback, deliberately not sub-classified.
        GuardConditionDiff.Classify(one, other).ShouldBe(GuardVerdict.Changed);
        GuardConditionDiff.Classify(one, one).ShouldBeNull();
    }

    [Test]
    public void A_negated_guard_set_entry_stays_one_opaque_clause_because_De_Morgan_makes_it_a_disjunction()
    {
        // WhenTrue=false means the effect runs when the predicate is FALSE, i.e. `!(a && b)` — which is
        // `!a || !b`, a DISJUNCTION. Splitting it into conjuncts would let a false containment be derived
        // (e.g. concluding `!(a && b)` ⊂ something), so it is kept whole.
        var negated = GuardConditionDiff.Conjuncts(Encode(("a && b", false)));

        negated.Count.ShouldBe(1);
        negated.ShouldContain("!(a && b)");

        // And it is NOT comparable to the positive form — the polarity flip must not read as "unchanged".
        var positive = GuardConditionDiff.Conjuncts(Encode(("a && b", true)));
        GuardConditionDiff.Classify(negated, positive).ShouldBe(GuardVerdict.Changed);
    }

    [Test]
    public void Distinct_decisions_in_one_guard_set_are_all_conjuncts()
    {
        // A guard SET already means "these distinct decisions AND-join" (nested ifs, a loop plus an inner if),
        // so every entry contributes, and an added outer decision is a narrowing.
        var inner = GuardConditionDiff.Conjuncts(Encode(("b", true)));
        var both = GuardConditionDiff.Conjuncts(Encode(("a", true), ("b", true)));

        both.Count.ShouldBe(2);
        GuardConditionDiff.Classify(inner, both).ShouldBe(GuardVerdict.Narrowed);
    }

    [Test]
    public void Top_level_and_splitting_respects_parens_strings_and_comments()
    {
        static IReadOnlyList<string> Split(string s) => GuardConditionDiff.SplitTopLevelAnd(s);

        // A disjunction is ONE conjunct; parenthesised `&&` belongs to the sub-expression.
        Split("a || b").Count.ShouldBe(1);
        Split("(a && b) || c").Count.ShouldBe(1);
        Split("a && (b || c)").Count.ShouldBe(2);
        Split("a && b && c").Count.ShouldBe(3);

        // A single `&` is bitwise, not a split point.
        Split("a & b").Count.ShouldBe(1);

        // `&&` inside a string literal, a char literal, or a comment must not split. A LINQ-ish condition with
        // an embedded string is exactly the shape MedDBase produces.
        Split("""name == "x && y" """).Count.ShouldBe(1);
        Split("""s.Contains("a && b") && flag""").Count.ShouldBe(2);
        Split("a /* && */ && b").Count.ShouldBe(2);
        Split("a // && b\n && c").Count.ShouldBe(2);

        // Indexers/brackets are depth too, so an indexed comparison stays intact.
        Split("map[\"k && v\"] == 1 && flag").Count.ShouldBe(2);
    }

    [Test]
    public void A_version_skewed_store_pair_is_detected_so_its_guard_rows_are_not_believed()
    {
        // Guards on lambda edges did not exist before 2026-07-27, so a pre-fix store has EXACTLY zero of them.
        // Diffing pre-fix against post-fix makes thousands of lambda edges look freshly guarded — a flood of
        // NARROWED rows indistinguishable from real audit suppression. This fingerprint is what makes that
        // detectable instead of a false report.
        new GuardCoverage(BaseLambdaGuards: 0, HeadLambdaGuards: 7091).SkewSuspected.ShouldBeTrue();
        new GuardCoverage(BaseLambdaGuards: 7091, HeadLambdaGuards: 0).SkewSuspected.ShouldBeTrue();

        // Two post-fix stores: no skew, even though the counts differ by a normal amount.
        new GuardCoverage(BaseLambdaGuards: 7091, HeadLambdaGuards: 7085).SkewSuspected.ShouldBeFalse();
        // Two stores that genuinely have no argument lambdas (a small solution) must NOT warn — zero on BOTH
        // sides is consistent, not skewed.
        new GuardCoverage(BaseLambdaGuards: 0, HeadLambdaGuards: 0).SkewSuspected.ShouldBeFalse();
        // A handful appearing is a plausible real change, not a version difference, so it stays quiet.
        new GuardCoverage(BaseLambdaGuards: 0, HeadLambdaGuards: 3).SkewSuspected.ShouldBeFalse();
    }

    [Test]
    public void The_skew_warning_names_the_counts_and_the_fix()
    {
        var error = new StringWriter();
        ImpactCommand.WriteGuardSkewWarning(
            new ImpactDiff(Ep: null, AffectedEps: [], PerEp: [], GuardConditions: [], GuardCoverage: new GuardCoverage(0, 7091)),
            error
        );

        var text = error.ToString();
        text.ShouldContain("WARNING");
        text.ShouldContain("0 base vs 7091 head");
        text.ShouldContain("Re-index BOTH commits");

        // Silent when the pair is consistent — this must not fire on every ordinary diff.
        var quiet = new StringWriter();
        ImpactCommand.WriteGuardSkewWarning(
            new ImpactDiff(Ep: null, AffectedEps: [], PerEp: [], GuardConditions: [], GuardCoverage: new GuardCoverage(7091, 7085)),
            quiet
        );
        quiet.ToString().ShouldBeEmpty();
    }

    [Test]
    public void Normalization_strips_comments_but_never_touches_string_content()
    {
        // A `//` INSIDE a literal is data, not a comment — truncating there would silently rewrite the
        // condition (and a URL in a config comparison is a realistic way to hit it).
        GuardConditionDiff.NormalizeConjunct("""url == "https://x/y" """).ShouldBe("""url == "https://x/y" """.Trim());
        GuardConditionDiff.NormalizeConjunct("a  //  trailing").ShouldBe("a");
        GuardConditionDiff.NormalizeConjunct("a /* mid */ b").ShouldBe("a b");
        GuardConditionDiff.NormalizeConjunct("  a\n\t  b  ").ShouldBe("a b");
        // An escaped quote must not end the literal early.
        GuardConditionDiff.NormalizeConjunct("""s == "a\"//b" """).ShouldBe("""s == "a\"//b" """.Trim());
    }
}
