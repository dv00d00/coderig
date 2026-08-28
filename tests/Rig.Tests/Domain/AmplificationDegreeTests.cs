using Rig.Cli.Commands;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

// `rig amplify` — the AMPLIFICATION DEGREE of an effect: how many INDEPENDENT iteration contexts are stacked
// between a caller and the effect site. Degree 1 is linear (the shipped n_plus_1 / cross_method tiers already
// cover it); degree >= 2 is the super-linear class this tier exists to find, and a chain that enters a call
// cycle has no finite degree at all.
//
// The two failure modes this suite pins are opposite in sign, and both are silent:
//   * MISSING a real product — the loop and the effect are separated by a call, or by a virtual dispatch hop,
//     or the loop is an enumerating lambda with no loop STATEMENT anywhere in the facts;
//   * MANUFACTURING one — two SIBLING loops over the same collection in one method are ADDITIVE (2N) and must
//     never read as quadratic. That is the anti-overcount test, and it is the reason intra-method nesting is
//     recovered by line-span CONTAINMENT rather than by counting a method's loops.
public sealed class AmplificationDegreeTests
{
    private static readonly FactObservationRules Rules = new(
        ResilienceRetry: [],
        ConcurrencyHandled: [],
        ParallelFanout: [],
        ResourceSpan: [],
        SerializationHazard: [],
        NPlusOne: [],
        EnumeratingMethods: [new FactEnumeratingMethodRule(Methods: ["Select", "ForEach"], DeclaringTypes: ["System.Linq.Enumerable"])]
    );

    // The rules-declared DISPLAY scope. Everything here is data — no provider is named in the deriver.
    private static readonly IReadOnlyList<FactAmplificationRule> Scope =
    [
        new FactAmplificationRule(Providers: ["llblgen"], Operations: []),
    ];

    private static FactInvocation Call(
        string caller,
        string callee,
        string file,
        int line,
        string? loopKind = null,
        string? loopDetail = null,
        string? enclosingInvocations = null
    ) =>
        new(
            Target: callee,
            Enclosing: caller,
            FilePath: file,
            Line: line,
            Args: new FactCallArguments(Names: """["x"]"""),
            Loop: new FactLoopContext(Kind: loopKind, Detail: loopDetail),
            Nesting: new FactCallSiteNesting(Invocations: enclosingInvocations)
        );

    private static DerivedEffect Read(string enclosing, string file = "Repo.cs", int line = 99) =>
        new(Provider: "llblgen", Operation: "read", ResourceType: "N.Thing", EnclosingSymbolId: enclosing, FilePath: file, Line: line);

    private static MethodRef M(string id) => new(id, id, null);

    // The one finding matching a predicate — Shouldly's ShouldHaveSingleItem takes a message, not a filter,
    // so the filter has to happen first or the lambda binds to the message parameter.
    private static FactAmplificationDegreeDeriver.Finding Only(
        IReadOnlyList<FactAmplificationDegreeDeriver.Finding> findings,
        Func<FactAmplificationDegreeDeriver.Finding, bool> predicate
    ) => findings.Where(predicate).ToList().ShouldHaveSingleItem();

    private static FactGraphData Graph(params CallEdge[] edges)
    {
        var nodes = edges.SelectMany(e => new[] { e.Caller, e.Callee }).Distinct(StringComparer.Ordinal).Select(M).ToArray();
        return new FactGraphData(edges, Array.Empty<ImplementsEdge>(), nodes);
    }

    private static IReadOnlyList<FactAmplificationDegreeDeriver.Finding> Derive(
        IReadOnlyList<FactInvocation> invocations,
        FactGraphData graph,
        IReadOnlyList<DerivedEffect> effects
    ) =>
        FactAmplificationDegreeDeriver.Derive(
            invocations: invocations,
            graph: graph,
            effects: effects,
            observationRules: Rules,
            scope: Scope
        );

    // The enumerating-lambda structural context (`rows.Select(r => …)`), the shape that has no loop STATEMENT
    // in the facts at all and on MedDBase outnumbers query syntax ~4:1.
    private static string? Enumerating(string receiver, string method, string parameter) =>
        FactStructuralContext.EncodeInvocations([
            new FactStructuralContext.EnclosingInvocation(
                ReceiverText: receiver,
                ReceiverType: "System.Collections.Generic.IEnumerable`1",
                MethodName: method,
                DeclaringType: "System.Linq.Enumerable",
                LambdaParameter: parameter
            ),
        ]);

    // ---- 1. the product across two methods --------------------------------------------------------------

    // The canonical quadratic: a caller loops calling a callee that itself loops down to a read. Neither the
    // lexical n_plus_1 (the loop and the read are in different frames) nor the k=1 cross-method tier (which
    // correlates ONE anchor with the read and stops) can express the ×N².
    [Test]
    public void Two_loops_in_two_methods_compose_to_degree_two()
    {
        var findings = Derive(
            [
                Call("M:N.A.Run", "M:N.B.Each", "A.cs", 10, loopKind: "foreach", loopDetail: "a in items"),
                Call("M:N.B.Each", "M:N.C.Fetch", "B.cs", 20, loopKind: "foreach", loopDetail: "b in rows"),
            ],
            Graph(
                new CallEdge("M:N.A.Run", "M:N.B.Each", "invocation", "A.cs", 10, "foreach", "a in items"),
                new CallEdge("M:N.B.Each", "M:N.C.Fetch", "invocation", "B.cs", 20, "foreach", "b in rows")
            ),
            [Read("M:N.C.Fetch")]
        );

        var quadratic = Only(findings, f => f.Head.Caller == "M:N.A.Run");
        quadratic.Degree.ShouldBe(2);
        quadratic.Recursion.ShouldBeFalse();
        // Both contributions are cross-method anchor->anchor edges — a call-graph fact, not a line-span guess.
        quadratic.Confidence.ShouldBe("✔");
        quadratic.EffectKind.ShouldBe("llblgen:read");
        quadratic.Chain.Count.ShouldBe(2);
        quadratic.Chain[0].IterationDetail.ShouldBe("a in items");
        quadratic.Chain[0].Callee.ShouldBe("M:N.B.Each");
        quadratic.Chain[1].Caller.ShouldBe("M:N.B.Each");
        quadratic.Chain[1].IterationDetail.ShouldBe("b in rows");
        quadratic.EffectEnclosing.ShouldBe("M:N.C.Fetch");

        // The inner anchor is still reported on its own — at degree 1, i.e. linear, which --min-degree drops.
        Only(findings, f => f.Head.Caller == "M:N.B.Each").Degree.ShouldBe(1);
    }

    // ---- 2. across a virtual dispatch hop ---------------------------------------------------------------

    // The class that was invisible before the anchor seeds moved onto the CALLEE: the second loop calls a
    // BASE virtual and the read lives in the override. The chain must compose across the devirtualization hop
    // exactly as it does across a direct call.
    [Test]
    public void A_loop_reaching_an_effect_through_a_virtual_override_still_composes()
    {
        var edges = new[]
        {
            new CallEdge("M:N.A.Run", "M:N.B.Each", "invocation", "A.cs", 10, "foreach", "a in items"),
            new CallEdge("M:N.B.Each", "M:N.RepoBase.Fetch", "invocation", "B.cs", 20, "foreach", "b in rows"),
        };
        var bases = new[] { new BaseEdge("T:N.SqlRepo", "T:N.RepoBase") };
        var methods = new[]
        {
            new MethodRef("M:N.A.Run", "Run", "T:N.A"),
            new MethodRef("M:N.B.Each", "Each", "T:N.B"),
            new MethodRef("M:N.RepoBase.Fetch", "Fetch", "T:N.RepoBase"),
            new MethodRef("M:N.SqlRepo.Fetch", "Fetch", "T:N.SqlRepo", IsOverride: true),
        };
        var graph = new FactGraphData(edges, Array.Empty<ImplementsEdge>(), methods, bases);

        var findings = Derive(
            [
                Call("M:N.A.Run", "M:N.B.Each", "A.cs", 10, loopKind: "foreach", loopDetail: "a in items"),
                Call("M:N.B.Each", "M:N.RepoBase.Fetch", "B.cs", 20, loopKind: "foreach", loopDetail: "b in rows"),
            ],
            graph,
            // The read is in the OVERRIDE, which is reached only by resolving the base-virtual call.
            [Read("M:N.SqlRepo.Fetch")]
        );

        var quadratic = Only(findings, f => f.Head.Caller == "M:N.A.Run");
        quadratic.Degree.ShouldBe(2);
        quadratic.EffectEnclosing.ShouldBe("M:N.SqlRepo.Fetch");
    }

    // ---- 3. the anti-overcount test ---------------------------------------------------------------------

    // TWO SIBLING LOOPS over ONE collection in ONE method are ADDITIVE (2N), not quadratic. A naive
    // "count this method's loops" rule reads them as degree 2; span containment does not, because disjoint
    // spans never contain one another. This is the single most important assertion in the suite — on the real
    // store, 81% of the methods with >=2 distinct loops have no nested pair at all.
    [Test]
    public void Sibling_loops_over_one_collection_in_one_method_stay_degree_one()
    {
        var findings = Derive(
            [
                Call("M:N.A.Run", "M:N.R.Fetch", "A.cs", 10, loopKind: "foreach", loopDetail: "x in xs"),
                Call("M:N.A.Run", "M:N.R.Fetch", "A.cs", 20, loopKind: "foreach", loopDetail: "x in xs"),
            ],
            Graph(new CallEdge("M:N.A.Run", "M:N.R.Fetch", "invocation", "A.cs", 10, "foreach", "x in xs")),
            [Read("M:N.R.Fetch")]
        );

        findings.ShouldNotBeEmpty();
        findings.ShouldAllBe(f => f.Degree == 1);
        findings.ShouldAllBe(f => f.Chain[0].IntraDepth == 1);
    }

    // The other half of the same heuristic: a loop whose span is STRICTLY inside another loop's span in the
    // same method IS nested, and contributes a second degree — tagged `~`, never `✔`, because line-range
    // containment is an inference over facts that record only the innermost loop per call site.
    [Test]
    public void A_loop_nested_inside_another_loop_of_the_same_method_contributes_a_degree_and_is_tagged_approximate()
    {
        var findings = Derive(
            [
                Call("M:N.A.Run", "M:N.R.Open", "A.cs", 10, loopKind: "foreach", loopDetail: "x in xs"),
                Call("M:N.A.Run", "M:N.R.Fetch", "A.cs", 20, loopKind: "foreach", loopDetail: "y in ys"),
                Call("M:N.A.Run", "M:N.R.Close", "A.cs", 30, loopKind: "foreach", loopDetail: "x in xs"),
            ],
            Graph(
                new CallEdge("M:N.A.Run", "M:N.R.Open", "invocation", "A.cs", 10),
                new CallEdge("M:N.A.Run", "M:N.R.Fetch", "invocation", "A.cs", 20),
                new CallEdge("M:N.A.Run", "M:N.R.Close", "invocation", "A.cs", 30)
            ),
            [Read("M:N.R.Fetch")]
        );

        // The inner loop's span [20,20] sits strictly inside the outer loop's [10,30].
        var inner = Only(findings, f => f.Chain[0].Line == 20);
        inner.Degree.ShouldBe(2);
        inner.Chain.Count.ShouldBe(1); // one call site, two stacked loops around it
        inner.Chain[0].IntraDepth.ShouldBe(2);
        inner.Confidence.ShouldBe("~");
    }

    // ONE query expression is ONE loop context, however many details it emits. A `query` loop detail carries
    // the CUMULATIVE comma-joined bind set, so every clause of a single query emits its own detail and their
    // spans nest by construction. Details modelled on the real store's
    // MedDBase.DataServer.Default.Servlet.Register.GetRegisterByInvoiceDate, which read as intra-depth 5
    // before the identifier-set fold — five stacked loops for one query expression.
    [Test]
    public void The_cumulative_bind_sets_of_one_query_expression_are_one_loop_context_not_a_nest()
    {
        var findings = Derive(
            [
                Call("M:N.Register.ByDate", "M:N.R.Open", "R.cs", 515, loopKind: "query", loopDetail: "invoice, billingitem in invoices"),
                Call(
                    "M:N.Register.ByDate",
                    "M:N.R.Fetch",
                    "R.cs",
                    531,
                    loopKind: "query",
                    loopDetail: "invoice, billingitem, account in invoices"
                ),
                Call(
                    "M:N.Register.ByDate",
                    "M:N.R.Close",
                    "R.cs",
                    559,
                    loopKind: "query",
                    loopDetail: "invoice, billingitem, account in invoices"
                ),
                Call("M:N.Register.ByDate", "M:N.R.Done", "R.cs", 687, loopKind: "query", loopDetail: "invoice, billingitem in invoices"),
            ],
            Graph(
                new CallEdge("M:N.Register.ByDate", "M:N.R.Open", "invocation", "R.cs", 515),
                new CallEdge("M:N.Register.ByDate", "M:N.R.Fetch", "invocation", "R.cs", 531),
                new CallEdge("M:N.Register.ByDate", "M:N.R.Close", "invocation", "R.cs", 559),
                new CallEdge("M:N.Register.ByDate", "M:N.R.Done", "invocation", "R.cs", 687)
            ),
            [Read("M:N.R.Fetch")]
        );

        // `invoice, billingitem, account` [531,559] sits strictly inside `invoice, billingitem` [515,687] —
        // span containment alone would call that a nest. The identifier sets are subset-related, so it is one
        // query, one degree.
        findings.ShouldNotBeEmpty();
        findings.ShouldAllBe(f => f.Degree == 1);
        findings.ShouldAllBe(f => f.Chain[0].IntraDepth == 1);
    }

    // ---- 4. recursion -----------------------------------------------------------------------------------

    // A per-element call that re-enters its own method multiplies by a runtime-only bound. No finite degree is
    // honest, so the finding is flagged and given its own section rather than a number.
    [Test]
    public void A_loop_inside_a_call_cycle_is_flagged_recursion_not_given_a_finite_degree()
    {
        var findings = Derive(
            [Call("M:N.Tree.Walk", "M:N.Tree.Walk", "Tree.cs", 10, loopKind: "foreach", loopDetail: "child in node.Children")],
            Graph(new CallEdge("M:N.Tree.Walk", "M:N.Tree.Walk", "invocation", "Tree.cs", 10, "foreach", "child in node.Children")),
            [Read("M:N.Tree.Walk", file: "Tree.cs", line: 12)]
        );

        var recursive = findings.ShouldHaveSingleItem();
        recursive.Recursion.ShouldBeTrue();
        recursive.Degree.ShouldBe(FactAmplificationDegreeDeriver.Unbounded);
        recursive.Chain.ShouldHaveSingleItem().Caller.ShouldBe("M:N.Tree.Walk");

        // A recursive finding is never mixed into the ranked super-linear list, at any --min-degree.
        var (main, _, recursion) = AmplifyCommand.Sections(findings, minDegree: 2, top: 50);
        main.ShouldBeEmpty();
        recursion.ShouldHaveSingleItem();
    }

    // Mutual recursion is the same conclusion reached through an SCC of size 2 rather than a self-edge.
    [Test]
    public void Mutual_recursion_between_two_looped_methods_is_also_unbounded()
    {
        var findings = Derive(
            [
                Call("M:N.P.Left", "M:N.Q.Right", "P.cs", 10, loopKind: "foreach", loopDetail: "a in aa"),
                Call("M:N.Q.Right", "M:N.P.Left", "Q.cs", 20, loopKind: "foreach", loopDetail: "b in bb"),
            ],
            Graph(
                new CallEdge("M:N.P.Left", "M:N.Q.Right", "invocation", "P.cs", 10, "foreach", "a in aa"),
                new CallEdge("M:N.Q.Right", "M:N.P.Left", "invocation", "Q.cs", 20, "foreach", "b in bb")
            ),
            [Read("M:N.Q.Right", file: "Q.cs", line: 22)]
        );

        findings.ShouldNotBeEmpty();
        findings.ShouldAllBe(f => f.Recursion);
        findings.ShouldAllBe(f => f.Degree == FactAmplificationDegreeDeriver.Unbounded);
    }

    // ---- 5. the enumerating lambda ----------------------------------------------------------------------

    // `rows.Select(r => Repo.Fetch(r))` has no loop STATEMENT anywhere in the facts, yet it iterates. The
    // rules-declared enumerating methods make it a first-class iteration context, so it composes with an
    // outer foreach exactly as a nested foreach would.
    [Test]
    public void An_enumerating_lambda_counts_as_a_loop_context_and_composes()
    {
        var findings = Derive(
            [
                Call("M:N.A.Run", "M:N.B.Each", "A.cs", 10, loopKind: "foreach", loopDetail: "a in items"),
                Call("M:N.B.Each", "M:N.C.Fetch", "B.cs", 20, enclosingInvocations: Enumerating("rows", "Select", "r")),
            ],
            Graph(
                new CallEdge("M:N.A.Run", "M:N.B.Each", "invocation", "A.cs", 10, "foreach", "a in items"),
                new CallEdge("M:N.B.Each", "M:N.C.Fetch", "invocation", "B.cs", 20)
            ),
            [Read("M:N.C.Fetch")]
        );

        var quadratic = Only(findings, f => f.Head.Caller == "M:N.A.Run");
        quadratic.Degree.ShouldBe(2);
        quadratic.Chain[1].IterationKind.ShouldBe("lambda");
        quadratic.Chain[1].IterationDetail.ShouldBe("r in Select");
    }

    // ---- 6. the degree 0 / 1 controls -------------------------------------------------------------------

    [Test]
    public void An_effect_under_no_loop_at_all_produces_no_finding() =>
        Derive(
                [Call("M:N.A.Run", "M:N.C.Fetch", "A.cs", 10)],
                Graph(new CallEdge("M:N.A.Run", "M:N.C.Fetch", "invocation", "A.cs", 10)),
                [Read("M:N.C.Fetch")]
            )
            .ShouldBeEmpty();

    // A single loop over a read is LINEAR — the shipped tiers already own it, so `amplify` derives it (degree
    // 1, for composition) but --min-degree 2 must not report it.
    [Test]
    public void A_single_loop_is_degree_one_and_is_not_reported_at_min_degree_two()
    {
        var findings = Derive(
            [Call("M:N.A.Run", "M:N.C.Fetch", "A.cs", 10, loopKind: "foreach", loopDetail: "a in items")],
            Graph(new CallEdge("M:N.A.Run", "M:N.C.Fetch", "invocation", "A.cs", 10, "foreach", "a in items")),
            [Read("M:N.C.Fetch")]
        );

        findings.ShouldHaveSingleItem().Degree.ShouldBe(1);

        var (main, fireAndForget, recursion) = AmplifyCommand.Sections(findings, minDegree: 2, top: 50);
        main.ShouldBeEmpty();
        fireAndForget.ShouldBeEmpty();
        recursion.ShouldBeEmpty();
    }

    // The DISPLAY scope is rules data, and out-of-scope providers terminate no chain — the ×N of an in-process
    // cache hit is CPU, a different conversation from N² round trips.
    [Test]
    public void An_effect_outside_the_declared_amplification_scope_terminates_no_chain() =>
        FactAmplificationDegreeDeriver
            .Derive(
                invocations:
                [
                    Call("M:N.A.Run", "M:N.B.Each", "A.cs", 10, loopKind: "foreach", loopDetail: "a in items"),
                    Call("M:N.B.Each", "M:N.C.Fetch", "B.cs", 20, loopKind: "foreach", loopDetail: "b in rows"),
                ],
                graph: Graph(
                    new CallEdge("M:N.A.Run", "M:N.B.Each", "invocation", "A.cs", 10, "foreach", "a in items"),
                    new CallEdge("M:N.B.Each", "M:N.C.Fetch", "invocation", "B.cs", 20, "foreach", "b in rows")
                ),
                effects: [Read("M:N.C.Fetch") with { Provider = "entity_cache" }],
                observationRules: Rules,
                scope: Scope
            )
            .ShouldBeEmpty();

    // ---- ranking + tsv ----------------------------------------------------------------------------------

    // actor:tell is fire-and-forget queueing — a mailbox absorbs it, so ×N² is a throughput question rather
    // than N² blocking round trips. It gets its own section and never dilutes the main ranking.
    [Test]
    public void Fire_and_forget_queueing_is_sectioned_away_from_the_main_ranking()
    {
        var invocations = new[]
        {
            Call("M:N.A.Run", "M:N.B.Each", "A.cs", 10, loopKind: "foreach", loopDetail: "a in items"),
            Call("M:N.B.Each", "M:N.C.Tell", "B.cs", 20, loopKind: "foreach", loopDetail: "b in rows"),
        };
        var graph = Graph(
            new CallEdge("M:N.A.Run", "M:N.B.Each", "invocation", "A.cs", 10, "foreach", "a in items"),
            new CallEdge("M:N.B.Each", "M:N.C.Tell", "invocation", "B.cs", 20, "foreach", "b in rows")
        );
        var findings = FactAmplificationDegreeDeriver.Derive(
            invocations: invocations,
            graph: graph,
            effects: [Read("M:N.C.Tell") with { Provider = "actor", Operation = "tell" }],
            observationRules: Rules,
            scope: [new FactAmplificationRule(Providers: ["actor"], Operations: ["tell"])]
        );

        var (main, fireAndForget, _) = AmplifyCommand.Sections(findings, minDegree: 2, top: 50);
        main.ShouldBeEmpty();
        fireAndForget.ShouldHaveSingleItem().Degree.ShouldBe(2);
    }

    // A multi-line LINQ query detail carries raw newlines. Emitting one into a tsv row SPLITS the row —
    // `derive --format tsv` still does this; these rows must not.
    [Test]
    [Arguments("from p\r\n  in profiles", "from p in profiles")]
    [Arguments("a\tin\tb", "a in b")]
    [Arguments("  padded  ", "padded")]
    public void Tsv_text_collapses_every_whitespace_run(string raw, string expected) => AmplifyCommand.Clean(raw).ShouldBe(expected);
}
