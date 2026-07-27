using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rig.Analysis;
using Rig.Analysis.Extraction;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Analysis;

// Control-dependence guards on the call-graph edges that are NOT invocations: an argument LAMBDA
// (`Call(() => …)`) and a METHOD-GROUP conversion (`Call(Helper.Do)`). Both are real edges the
// tree/reaches/path walks traverse, so both need the guard of their CREATION SITE.
//
// The defect (2026-07-27, quantified on the MedDBase store): 0 of 65,450 argument-lambda edges and 0 of
// 71,690 methodGroup edges carried ANY guard, against 10.8% of invocation edges. Two independent causes —
// ProcessLambda never set EnclosingGuards, and the main ref loop derived the guard root from
// structuralRoot, which is deliberately null for a method group ("no effect consumes it" — true of
// structural context, false of guards).
//
// Consequence, and why this is a soundness bug rather than a missing feature: a `() => …` literal inside an
// `if` makes EVERYTHING its body reaches conditional. With the edge unguarded, every effect under it read as
// MUST-RUN — rig asserting "this always happens" where the truth is "only when the branch is taken". It is
// also why `impact` reported no delta for MedDBase MR !11025, which suppressed an audit purely by tightening
// the predicate around a `TransactionDependency.Call(() => … AuditLog … .Log())`.
//
// The guard belongs on the EDGE, not on the effect: the audit call inside the lambda is unconditional
// *within the lambda's own body*, and that stays true here (see
// An_effect_inside_the_lambda_body_stays_unguarded_the_guard_is_on_the_edge). Any consumer wanting "under
// what condition does this effect fire" must compose along the path.
public sealed class GuardedCallGraphEdgeTests
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

    // A custom delegate keeps the snippet independent of which BCL assembly is referenced; `Call`/`Inner`
    // give the lambda an ARGUMENT position, which is what earns it a synthetic identity + a methodGroup edge.
    private static FactExtractionResult ExtractBody(string body) =>
        Extract(
            "namespace App { delegate void Act(); class Svc { "
                + "void H(bool a, bool b) { "
                + body
                + " } "
                + "static void Call(Act f){} static void Inner(Act f){} void Foo(){} static void Do(){} } }"
        );

    private static string Render(string? encoded) =>
        string.Join(",", FactStructuralContext.DecodeGuards(encoded).Select(x => $"{x.Predicate}={(x.WhenTrue ? "T" : "F")}"));

    // The guard set on the edge into the Nth argument-lambda declared in `body`.
    private static string LambdaEdgeGuards(string body, int ordinal = 0)
    {
        var r = ExtractBody(body);
        var edge = r.References.Single(x =>
            x.RefKind == "methodGroup" && x.TargetSymbolId.EndsWith($"~λ{ordinal}", StringComparison.Ordinal)
        );
        return Render(edge.EnclosingGuards);
    }

    [Test]
    public void An_argument_lambda_created_inside_a_branch_carries_that_branch_as_its_edge_guard()
    {
        // THE REGRESSION. Pre-fix every one of these was "" (must-run).
        LambdaEdgeGuards("if (a) Call(() => Foo());").ShouldBe("a=T");
        LambdaEdgeGuards("if (a) {} else Call(() => Foo());").ShouldBe("a=F");

        // The MedDBase MR !11025 shape: a negated conjunction gating the lambda's creation. This is the
        // condition whose tightening `impact` could not see, composed with the polarity fix from
        // GuardPolarityTests (the widened text keeps its `!`, and WhenTrue is not double-negated).
        LambdaEdgeGuards("if (!a && b) Call(() => Foo());").ShouldBe("!a && b=T");

        // Sugar the CFG lowers the same way — no `if` node to find syntactically, which is the whole reason
        // guards are CFG-derived rather than an ancestor walk.
        LambdaEdgeGuards("if (a) return; Call(() => Foo());").ShouldBe("a=F");
    }

    [Test]
    public void An_unconditionally_created_lambda_stays_must_run()
    {
        // Fence: the fix must not manufacture guards. A must-run edge stays null/empty, so `--guards` output
        // does not gain noise on the ~89% of sites that genuinely always run.
        LambdaEdgeGuards("Call(() => Foo());").ShouldBe("");

        var r = ExtractBody("Call(() => Foo());");
        r.References.Single(x => x.RefKind == "methodGroup" && x.TargetSymbolId.Contains("λ", StringComparison.Ordinal))
            .EnclosingGuards.ShouldBeNull();
    }

    [Test]
    public void A_lambda_nested_in_a_lambda_is_guarded_relative_to_its_OWN_enclosing_body()
    {
        // The nesting case that makes the CFG choice load-bearing. The OUTER lambda is created
        // unconditionally; the INNER one is created inside `if (b)` *within the outer lambda's body*. So the
        // inner edge's guard must be `b` — read from the outer lambda's sub-CFG, not the method's top-level
        // CFG (where `b` does not gate anything) and not from the outer lambda's own creation site.
        const string body = "Call(() => { if (b) Inner(() => Foo()); });";

        LambdaEdgeGuards(body, ordinal: 0).ShouldBe(""); // outer: unconditional
        LambdaEdgeGuards(body, ordinal: 1).ShouldBe("b=T"); // inner: gated by the branch inside the outer body
    }

    [Test]
    public void A_guarded_outer_lambda_does_not_leak_its_guard_onto_an_unconditional_inner_lambda()
    {
        // The converse, and the reason this is EDGE-local rather than cumulative: the outer lambda is created
        // under `if (a)`, but inside the outer body the inner lambda is unconditional. Stamping `a` onto the
        // inner edge too would double-count the same condition when a consumer composes along the path.
        const string body = "if (a) Call(() => { Inner(() => Foo()); });";

        LambdaEdgeGuards(body, ordinal: 0).ShouldBe("a=T");
        LambdaEdgeGuards(body, ordinal: 1).ShouldBe("");
    }

    [Test]
    public void A_method_group_conversion_inside_a_branch_carries_the_branch_as_its_edge_guard()
    {
        // Arm 2: `Call(Do)` / `Call(Svc.Do)` is an edge too. BlockOf needs an exact operation-syntax match,
        // so the guard root is widened to the member access — the bare `Do` identifier is not itself an
        // operation node. Both spellings must work.
        static string MethodGroupGuards(string body)
        {
            var r = ExtractBody(body);
            var edge = r.References.Single(x => x.RefKind == "methodGroup" && x.TargetSymbolId.Contains(".Do", StringComparison.Ordinal));
            return Render(edge.EnclosingGuards);
        }

        MethodGroupGuards("if (a) Call(Svc.Do);").ShouldBe("a=T");
        MethodGroupGuards("if (a) Call(Do);").ShouldBe("a=T");
        MethodGroupGuards("Call(Svc.Do);").ShouldBe("");
    }

    [Test]
    public void An_effect_inside_the_lambda_body_stays_unguarded_the_guard_is_on_the_edge()
    {
        // Pins the DESIGN, not just the fix. `Foo()` is unconditional within the lambda body, and remains so
        // — the `if (a)` is recorded once, on the edge into the lambda. This is why a guard delta keyed on an
        // effect's OWN guard set still reports UNCHANGED for MR !11025: the condition is an ancestor edge's,
        // and any "under what condition does this fire" answer has to compose along the path.
        var r = ExtractBody("if (a) Call(() => Foo());");

        var lambdaEdge = r.References.Single(x => x.RefKind == "methodGroup" && x.TargetSymbolId.Contains("λ", StringComparison.Ordinal));
        Render(lambdaEdge.EnclosingGuards).ShouldBe("a=T");

        var fooCall = r.References.Single(x => x.RefKind == "invocation" && x.TargetSymbolId.Contains("Foo", StringComparison.Ordinal));
        fooCall.EnclosingGuards.ShouldBeNull();
        // ...and it is owned by the lambda, so the edge above is genuinely on its path.
        fooCall.EnclosingSymbolId.ShouldNotBeNull().ShouldContain("λ");
    }
}
