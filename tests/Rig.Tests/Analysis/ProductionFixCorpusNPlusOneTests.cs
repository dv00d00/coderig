using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Analysis;

// FR-3 (RCA #2892), the n_plus_1 layer over the §#2892 looped_effect test in ProductionFixCorpusTests.
// rig already flags ANY effect inside a loop as looped_effect; FR-3 refines that for READS: a read inside a
// loop whose KEY ARGUMENT VARIES per iteration (the loop variable appears in the read's key) is an
// n+1 / read amplification (the Pathways 4000 queries/min defect — variable definitions read from the source
// PER ITERATION because they were missing from the cache). A read in the SAME loop with a CONSTANT key is
// hoistable and is NOT an n+1, so it must NOT fire — that contrast is the whole discriminator. The detector
// is data-driven (the read provider/operation set + the n_plus_1 rule live in builtin-rules.json); this test
// proves the shipped rules fire on the varying-key bug and stay silent on the constant-key fix.
public sealed class ProductionFixCorpusNPlusOneTests
{
    // System.Net.Http.HttpClient.GetStringAsync is a builtin http:GET read present in the framework refs —
    // a reliable read surface. The bug interpolates the loop variable into the URL ($"/var/{id}"), so the
    // key varies per iteration; the fix reads a single constant URL ("/vars/all"), hoistable out of the loop.
    [Test]
    public void _2892_looped_read_with_a_varying_key_fires_n_plus_1_the_constant_key_fix_does_not()
    {
        var result = ProductionFixCorpus.Analyze(
            """
            namespace Pathways
            {
                public sealed class Interpreter
                {
                    // BUG (#2892): the key (id) varies per iteration -> a read per iteration -> N+1.
                    public async System.Threading.Tasks.Task ReadVars_Bug(
                        System.Net.Http.HttpClient client,
                        System.Collections.Generic.IEnumerable<string> ids)
                    {
                        foreach (var id in ids)
                        {
                            await client.GetStringAsync($"/var/{id}");
                        }
                    }

                    // FIX (#2892): same loop, but the key is CONSTANT -> hoistable -> NOT an N+1.
                    public async System.Threading.Tasks.Task ReadVars_Fix(
                        System.Net.Http.HttpClient client,
                        System.Collections.Generic.IEnumerable<string> ids)
                    {
                        foreach (var id in ids)
                        {
                            await client.GetStringAsync("/vars/all");
                        }
                    }
                }
            }
            """
        );

        // BUG: the http read is inside a loop (looped_effect) AND its key varies per iteration (n_plus_1).
        var bug = result.EffectsIn("ReadVars_Bug").Single(e => e.Provider == "http");
        bug.Observations.ShouldNotBeNull();
        bug.Observations!.ShouldContain(o => o.Type == "looped_effect");
        bug.Observations!.ShouldContain(o => o.Type == "n_plus_1");

        // FIX: still in a loop (looped_effect) — but the constant key is hoistable, so NO n_plus_1. This
        // contrast is the point: n_plus_1 is the read-amplification discriminator over plain looped_effect.
        var fix = result.EffectsIn("ReadVars_Fix").Single(e => e.Provider == "http");
        fix.Observations.ShouldNotBeNull();
        fix.Observations!.ShouldContain(o => o.Type == "looped_effect");
        result.ObservationsIn("ReadVars_Fix", "n_plus_1").ShouldBeEmpty();
    }

    // The iteration context is not always a loop STATEMENT. MedDBase Admin/Profile/Home2 (trace
    // 35bcafca0907910d3106c460f5d0afc7, 11,461 single-row PROFILE selects / 35s of DB time in one request)
    // amplifies inside a LINQ QUERY EXPRESSION: `let profile = ProfileCache.New(p.PkProfile)` runs once per
    // element of the source sequence, so the read is per-iteration with a key that varies — the identical
    // defect shape to the #2892 foreach, expressed in query syntax. A `let` clause has no
    // ForEach/For/WhileStatementSyntax ancestor, so the loop-statement-only ancestor walk saw no iteration
    // context at all and NEITHER looped_effect NOR n_plus_1 fired. The constant-key variant must stay silent
    // for the same hoistability reason as the foreach fix above.
    [Test]
    public void Query_expression_let_clause_is_an_iteration_context_varying_key_fires_n_plus_1()
    {
        var result = ProductionFixCorpus.Analyze(
            """
            namespace Admin
            {
                public sealed class ProfileHome
                {
                    // BUG (Home2): the key (id) varies per element -> one read per element -> N+1.
                    public static System.Collections.Generic.List<string> Load_Bug(
                        System.Net.Http.HttpClient client,
                        System.Collections.Generic.IEnumerable<int> ids)
                    {
                        var q = from id in ids
                                let body = client.GetStringAsync($"/profile/{id}").Result
                                select body;
                        return System.Linq.Enumerable.ToList(q);
                    }

                    // FIX: same query shape, CONSTANT key -> hoistable -> NOT an N+1.
                    public static System.Collections.Generic.List<string> Load_Fix(
                        System.Net.Http.HttpClient client,
                        System.Collections.Generic.IEnumerable<int> ids)
                    {
                        var q = from id in ids
                                let body = client.GetStringAsync("/profiles/all").Result
                                select body;
                        return System.Linq.Enumerable.ToList(q);
                    }
                }
            }
            """
        );

        var bug = result.EffectsIn("Load_Bug").Single(e => e.Provider == "http");
        bug.Observations.ShouldNotBeNull();
        bug.Observations!.ShouldContain(o => o.Type == "looped_effect");
        bug.Observations!.ShouldContain(o => o.Type == "n_plus_1");

        var fix = result.EffectsIn("Load_Fix").Single(e => e.Provider == "http");
        fix.Observations.ShouldNotBeNull();
        fix.Observations!.ShouldContain(o => o.Type == "looped_effect");
        result.ObservationsIn("Load_Fix", "n_plus_1").ShouldBeEmpty();
    }

    // The PRIMARY `from` source is the one position in a query expression that is evaluated ONCE, so a read
    // there is not amplified and must report NEITHER looped_effect NOR n_plus_1. Home2's real source
    // expression is exactly this shape (`from p in profiles.ToList().DistinctOn(..)`) — treating a query as
    // a blanket iteration context would misreport that single batched fetch as the per-element one, which
    // would be precisely backwards: it is the FIX shape, not the bug.
    [Test]
    public void A_read_in_the_primary_from_source_runs_once_and_is_not_reported_as_looped()
    {
        var result = ProductionFixCorpus.Analyze(
            """
            namespace Admin
            {
                public sealed class BatchedLoad
                {
                    public static System.Collections.Generic.List<string> Load_Once(System.Net.Http.HttpClient client)
                    {
                        var q = from row in client.GetStringAsync("/profiles/all").Result.Split(',')
                                select row.Trim();
                        return System.Linq.Enumerable.ToList(q);
                    }
                }
            }
            """
        );

        result.EffectsIn("Load_Once").ShouldContain(e => e.Provider == "http");
        result.ObservationsIn("Load_Once", "looped_effect").ShouldBeEmpty();
        result.ObservationsIn("Load_Once", "n_plus_1").ShouldBeEmpty();
    }

    // A query rebinds EVERY variable it introduces, not just the `from` one — so a key built from a `let`
    // is amplified exactly as much as one built from the range variable. This is why the iteration
    // identifier is a SET rather than the single foreach variable; the finding should name `key`, the
    // variable that actually varies, not the `from` variable that happens to be first.
    [Test]
    public void A_key_derived_from_a_let_variable_also_varies_per_element()
    {
        var result = ProductionFixCorpus.Analyze(
            """
            namespace Admin
            {
                public sealed class LetKeyed
                {
                    public static System.Collections.Generic.List<string> Load_Bug(
                        System.Net.Http.HttpClient client,
                        System.Collections.Generic.IEnumerable<int> ids)
                    {
                        var q = from id in ids
                                let key = id + 1
                                let body = client.GetStringAsync($"/profile/{key}").Result
                                select body;
                        return System.Linq.Enumerable.ToList(q);
                    }
                }
            }
            """
        );

        var nPlusOne = result.ObservationsIn("Load_Bug", "n_plus_1").ShouldHaveSingleItem();
        nPlusOne.Context.ShouldBe("key");
    }

    // for/while/do amplify just as much, but carry no iteration identifier, so the varying-key
    // discriminator has nothing to match. Deliberate: they stay covered by looped_effect alone rather than
    // producing a keyless n_plus_1 guess. `do` is included because the ancestor walk originally omitted
    // DoStatementSyntax entirely — a read in a do/while body reported no loop context at all.
    [Test]
    public void A_do_while_body_is_a_loop_context_but_yields_no_keyless_n_plus_1()
    {
        var result = ProductionFixCorpus.Analyze(
            """
            namespace Admin
            {
                public sealed class Paged
                {
                    public static void Drain(System.Net.Http.HttpClient client, int pages)
                    {
                        var page = 0;
                        do
                        {
                            var body = client.GetStringAsync($"/page/{page}").Result;
                            page++;
                        }
                        while (page < pages);
                    }
                }
            }
            """
        );

        result.ObservationsIn("Drain", "looped_effect").ShouldNotBeEmpty();
        result.ObservationsIn("Drain", "n_plus_1").ShouldBeEmpty();
    }

    // Method-syntax LINQ is the same amplification with no loop STATEMENT and no query clause: the lambda
    // handed to Select runs once per element. On MedDBase this shape outnumbers the query-syntax one ~4:1
    // (193 vs 46 `Cache.New` call sites), so it is where most of the real read amplification lives.
    [Test]
    public void An_enumerating_lambda_is_an_iteration_context_varying_key_fires_n_plus_1()
    {
        var result = ProductionFixCorpus.Analyze(
            """
            namespace Admin
            {
                public sealed class Projected
                {
                    public static System.Collections.Generic.List<string> Load_Bug(
                        System.Net.Http.HttpClient client,
                        System.Collections.Generic.IEnumerable<int> ids)
                    {
                        return System.Linq.Enumerable.ToList(
                            System.Linq.Enumerable.Select(ids, id => client.GetStringAsync($"/profile/{id}").Result));
                    }

                    public static System.Collections.Generic.List<string> Load_Fix(
                        System.Net.Http.HttpClient client,
                        System.Collections.Generic.IEnumerable<int> ids)
                    {
                        return System.Linq.Enumerable.ToList(
                            System.Linq.Enumerable.Select(ids, id => client.GetStringAsync("/profiles/all").Result));
                    }
                }
            }
            """
        );

        var nPlusOne = result.ObservationsIn("Load_Bug", "n_plus_1").ShouldHaveSingleItem();
        nPlusOne.Context.ShouldBe("id");

        result.ObservationsIn("Load_Fix", "looped_effect").ShouldNotBeEmpty();
        result.ObservationsIn("Load_Fix", "n_plus_1").ShouldBeEmpty();
    }

    // THE false-positive guard, and the reason the gate is the resolved DECLARING type rather than the
    // method name: a single-shot lambda taker applies its function at most once, so an effect inside it is
    // not amplified. LanguageExt's Option.Map is the case that matters here — MedDBase calls Map/Match/Bind
    // pervasively, so a name-only gate ("Select"/"Map" anywhere) would bury the real findings. The custom
    // `Select` proves the same point from the other side: the right NAME on the wrong declaring type must
    // still not fire.
    [Test]
    public void Single_shot_lambda_takers_are_not_iteration_contexts()
    {
        var result = ProductionFixCorpus.Analyze(
            ProductionFixCorpus.LanguageExtStub
                + """
                namespace Admin
                {
                    public sealed class SingleShot
                    {
                        public static void Via_Option_Map(System.Net.Http.HttpClient client, LanguageExt.Option<int> id)
                        {
                            id.Map(x => client.GetStringAsync($"/profile/{x}").Result);
                        }
                    }

                    public sealed class Box<A>
                    {
                        public A Value;
                        // Right name, wrong declaring type — not System.Linq.Enumerable, so not enumerating.
                        public B Select<B>(System.Func<A, B> f) => f(Value);
                    }

                    public sealed class CustomSelect
                    {
                        public static void Via_Custom_Select(System.Net.Http.HttpClient client, Box<int> box)
                        {
                            box.Select(x => client.GetStringAsync($"/profile/{x}").Result);
                        }
                    }
                }
                """
        );

        result.EffectsIn("Via_Option_Map").ShouldContain(e => e.Provider == "http");
        result.ObservationsIn("Via_Option_Map", "looped_effect").ShouldBeEmpty();
        result.ObservationsIn("Via_Option_Map", "n_plus_1").ShouldBeEmpty();

        result.EffectsIn("Via_Custom_Select").ShouldContain(e => e.Provider == "http");
        result.ObservationsIn("Via_Custom_Select", "looped_effect").ShouldBeEmpty();
        result.ObservationsIn("Via_Custom_Select", "n_plus_1").ShouldBeEmpty();
    }

    // The KEY-CAPTURE base case. The argument surface used to record a name only when the argument expression
    // was ITSELF an identifier or member access, so a key nested inside a composite expression — an LLBLGen
    // predicate, a concatenation, a cast, a ternary — was invisible and n_plus_1 could not fire. That is the
    // exact shape of all 14 looped `TypedListBase.Fill(1, null, true, (Fields.Name == s.Trim()))` sites
    // (InvoicesByNominalCode.cs:96 and 10 more): provider, operation and iteration identifier all pass, and
    // the key match alone rejected them. Reduced to the same http read surface the rest of this file uses.
    //
    // The CONTRAST is the whole point and is why this is one test: the composite argument of the hoistable
    // variant is captured too (it is no longer null), and it still must not fire — a captured surface that
    // does not name the loop variable is a NEGATIVE, not a free pass.
    [Test]
    public void A_key_nested_in_a_composite_argument_fires_n_plus_1_a_composite_constant_key_does_not()
    {
        var result = ProductionFixCorpus.Analyze(
            """
            namespace Billing
            {
                public sealed class NominalCodes
                {
                    private const string BasePath = "/nominal";

                    // BUG (InvoicesByNominalCode.cs:96 shape): the key varies per iteration but sits INSIDE a
                    // composite expression, so the whole argument is neither an identifier nor a member access.
                    public static void Fill_Bug(
                        System.Net.Http.HttpClient client,
                        System.Collections.Generic.IEnumerable<string> nomCs)
                    {
                        foreach (string s in nomCs)
                        {
                            var body = client.GetStringAsync(BasePath + "/" + s.Trim()).Result;
                        }
                    }

                    // FIX: same composite SHAPE (so the surface is captured), constant key -> hoistable.
                    public static void Fill_Fix(
                        System.Net.Http.HttpClient client,
                        System.Collections.Generic.IEnumerable<string> nomCs)
                    {
                        foreach (string s in nomCs)
                        {
                            var body = client.GetStringAsync(BasePath + "/" + Total(nomCs)).Result;
                        }
                    }

                    private static string Total(System.Collections.Generic.IEnumerable<string> all) => "all";
                }
            }
            """
        );

        var fired = result.ObservationsIn("Fill_Bug", "n_plus_1").ShouldHaveSingleItem();
        fired.Context.ShouldBe("s");

        result.ObservationsIn("Fill_Fix", "looped_effect").ShouldNotBeEmpty();
        result.ObservationsIn("Fill_Fix", "n_plus_1").ShouldBeEmpty();
    }

    // THE DISCLOSED FALSE POSITIVE of the composite-argument surface above, pinned so it cannot be discovered
    // by surprise in a review. The surface says "these names appear somewhere in this expression", never "this
    // name is the key": a loop variable mentioned in a NON-key position of a composite argument matches all the
    // same. Here the read's key is a constant and `code` appears only as a FIELD NAME on the other side of the
    // predicate — the read is hoistable, and n_plus_1 fires anyway.
    //
    // Not fixable by narrowing the surface: separating "appears as the key" from "appears anywhere" needs
    // intra-method dataflow rig does not have, and the alternative — dropping composite arguments, as before —
    // costs the 11 confirmed true findings the test above pins. Recorded per the house rule that clears are
    // unsound and the ceiling is disclosed, never quietly assumed away.
    [Test]
    public void A_loop_variable_named_like_a_field_in_a_composite_key_is_a_KNOWN_false_positive()
    {
        var result = ProductionFixCorpus.Analyze(
            """
            namespace Billing
            {
                public static class Fields
                {
                    public static string code = "code";
                }

                public sealed class Shadowed
                {
                    // The key is CONSTANT ("/all"); `code` occurs only as the member name of Fields.code, not
                    // as a value. Hoistable, and flagged regardless — the over-approximation, pinned.
                    public static void Hoistable(
                        System.Net.Http.HttpClient client,
                        System.Collections.Generic.IEnumerable<string> codes)
                    {
                        foreach (string code in codes)
                        {
                            var body = client.GetStringAsync(Fields.code + "/all").Result;
                        }
                    }
                }
            }
            """
        );

        result.ObservationsIn("Hoistable", "n_plus_1").ShouldNotBeEmpty();
    }
}
