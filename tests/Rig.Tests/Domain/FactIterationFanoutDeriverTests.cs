using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

// The ANCHOR half of cross-method read amplification: an in-source call site that sits in an iteration context,
// i.e. a call issued once per element. Emitted for BOTH keyed and keyless sites — C# statements are eager, so a
// read beneath a per-element call executes per element regardless of the key, which makes PRESENCE the finding
// and a null key token DATA rather than a disqualifier. These tests pin the matrix that matters to the dataset:
// iteration kind x argument position x keyless.
//
// The pseudo-event's EnclosingSymbolId is the CALLEE (load-bearing: the correlation operator seeds its forward
// reach there, and the reach set includes the seed, so "companion reachable from the anchor's enclosing method"
// becomes "read reachable at or beneath the per-iteration call"), while FilePath/Line are the CALL SITE and
// Caller is the enclosing method a human would fix. Getting those three confused is the one way this whole
// design silently means something else, so every test asserts them.
public sealed class FactIterationFanoutDeriverTests
{
    private static readonly FactObservationRules Rules = new(
        ResilienceRetry: [],
        ConcurrencyHandled: [],
        ParallelFanout: [],
        ResourceSpan: [],
        SerializationHazard: [],
        NPlusOne: [],
        EnumeratingMethods: [new FactEnumeratingMethodRule(Methods: ["Select"], DeclaringTypes: ["System.Linq.Enumerable"])]
    );

    private static FactInvocation Call(
        string? loopKind,
        string? loopDetail,
        string? argumentNames = null,
        string? argumentTemplates = null,
        string? enclosingInvocations = null,
        string? guards = null,
        string callee = "M:N.Helper.Load(System.Int64)",
        string caller = "M:N.Page.Render",
        string? elementType = null
    ) =>
        new(
            Target: callee,
            Enclosing: caller,
            FilePath: "Page.cs",
            Line: 12,
            Args: new FactCallArguments(Names: argumentNames, Templates: argumentTemplates),
            Loop: new FactLoopContext(Kind: loopKind, Detail: loopDetail, ElementType: elementType),
            Nesting: new FactCallSiteNesting(Invocations: enclosingInvocations, Guards: guards)
        );

    [Test]
    public void A_foreach_call_site_anchors_on_the_callee_and_locates_at_the_call_site()
    {
        var fanout = FactIterationFanoutDeriver
            .Derive([Call(loopKind: "foreach", loopDetail: "id in ids", argumentNames: """["id"]""")], Rules)
            .ShouldHaveSingleItem();

        fanout.Event.Provider.ShouldBe("iteration");
        fanout.Event.Operation.ShouldBe("fanout");
        fanout.Event.EnclosingSymbolId.ShouldBe("M:N.Helper.Load(System.Int64)"); // the CALLEE
        fanout.Event.FilePath.ShouldBe("Page.cs");
        fanout.Event.Line.ShouldBe(12);
        fanout.Caller.ShouldBe("M:N.Page.Render"); // the human site
        fanout.IterationKind.ShouldBe("foreach");
        fanout.IteratedSource.ShouldBe("ids");
        fanout.KeyToken.ShouldBe("id");
        fanout.ArgumentIndex.ShouldBe(0);
        fanout.Event.ResourceType.ShouldBe("id");
        fanout.Recursive.ShouldBeFalse();
    }

    // A query expression rebinds every variable it introduces, so a key built from a `let` varies exactly as
    // much as one built from the `from` — the identifier set, not the first identifier.
    [Test]
    public void A_query_range_variable_in_any_argument_position_is_a_key()
    {
        var fanout = FactIterationFanoutDeriver
            .Derive(
                [
                    Call(
                        loopKind: "query",
                        loopDetail: "p, profile in profiles.ToList()",
                        argumentNames: """[null,"txn","profile.PkProfile"]"""
                    ),
                ],
                Rules
            )
            .ShouldHaveSingleItem();

        fanout.IterationKind.ShouldBe("query");
        fanout.IteratedSource.ShouldBe("profiles.ToList()");
        fanout.KeyToken.ShouldBe("profile");
        fanout.ArgumentIndex.ShouldBe(2);
    }

    // The enumerating-lambda context has no loop STATEMENT at all: `ids.Select(id => Helper.Load(id))`
    // amplifies identically, and on MedDBase this shape outnumbers query syntax ~4:1.
    [Test]
    public void An_enumerating_lambda_is_an_iteration_context()
    {
        var enclosing = FactStructuralContext.EncodeInvocations([
            new FactStructuralContext.EnclosingInvocation(
                ReceiverText: "ids",
                ReceiverType: "System.Collections.Generic.IEnumerable`1",
                MethodName: "Select",
                DeclaringType: "System.Linq.Enumerable",
                LambdaParameter: "id"
            ),
        ]);

        var fanout = FactIterationFanoutDeriver
            .Derive([Call(loopKind: null, loopDetail: null, argumentNames: """["id"]""", enclosingInvocations: enclosing)], Rules)
            .ShouldHaveSingleItem();

        fanout.IterationKind.ShouldBe("lambda");
        fanout.KeyToken.ShouldBe("id");
    }

    // for/while/do bind nothing, so there is no key to find — and they are emitted ANYWAY, keyless. This is the
    // single biggest behavioural difference from the intra-method detector (which deliberately stays silent
    // rather than guess a key), and it follows from presence being the finding.
    [Test]
    [Arguments("for")]
    [Arguments("while")]
    [Arguments("do")]
    public void A_keyless_loop_still_anchors(string kind)
    {
        var fanout = FactIterationFanoutDeriver.Derive([Call(loopKind: kind, loopDetail: kind)], Rules).ShouldHaveSingleItem();

        fanout.IterationKind.ShouldBe(kind);
        fanout.KeyToken.ShouldBe("");
        fanout.ArgumentIndex.ShouldBe(-1);
        fanout.Event.ResourceType.ShouldBe("");
        fanout.IteratedSource.ShouldBe("");
    }

    // A foreach whose call passes NOTHING per-element (a constant argument, or no captured surface at all) is
    // still a per-iteration call: the callee runs N times. Keyless, emitted.
    [Test]
    public void A_foreach_call_that_carries_no_per_element_argument_is_keyless_not_absent()
    {
        var fanout = FactIterationFanoutDeriver
            .Derive([Call(loopKind: "foreach", loopDetail: "id in ids", argumentNames: """["Settings.Timeout"]""")], Rules)
            .ShouldHaveSingleItem();

        fanout.KeyToken.ShouldBe("");
        fanout.ArgumentIndex.ShouldBe(-1);
    }

    // An interpolated argument keeps its {token}, so a key reaching the callee inside a string is found —
    // the same surface the intra-method detector matches on.
    [Test]
    public void A_key_inside_an_argument_template_counts()
    {
        var fanout = FactIterationFanoutDeriver
            .Derive([Call(loopKind: "foreach", loopDetail: "id in ids", argumentTemplates: """["/var/{id}"]""")], Rules)
            .ShouldHaveSingleItem();

        fanout.KeyToken.ShouldBe("id");
        fanout.ArgumentIndex.ShouldBe(0);
    }

    [Test]
    public void A_call_outside_any_iteration_context_is_not_an_anchor() =>
        FactIterationFanoutDeriver.Derive([Call(loopKind: null, loopDetail: null, argumentNames: """["id"]""")], Rules).ShouldBeEmpty();

    // Recursion (callee == the enclosing method) is a tree walk, not a fan-out. Flagged as EVIDENCE, not
    // suppressed: the per-node read of a recursive descent is sometimes exactly the hotspot.
    [Test]
    public void Recursion_is_marked_rather_than_dropped()
    {
        var self = "M:N.ObjectStore.GetIndexIdentifiers(System.Int64)";
        var fanout = FactIterationFanoutDeriver
            .Derive([Call(loopKind: "foreach", loopDetail: "index in indexes", callee: self, caller: self)], Rules)
            .ShouldHaveSingleItem();

        fanout.Recursive.ShouldBeTrue();
    }

    // Guards ride the event because they are the suspected real precision lever: a read behind a rarely-true
    // `if` inside the loop body executes ~never whatever its key.
    [Test]
    public void Anchor_guards_ride_the_event()
    {
        var fanout = FactIterationFanoutDeriver
            .Derive([Call(loopKind: "foreach", loopDetail: "id in ids", guards: "row.IsDirty")], Rules)
            .ShouldHaveSingleItem();

        fanout.Event.EnclosingGuards.ShouldBe("row.IsDirty");
    }

    // The KEY PATH is the whole argument surface, not the bare loop variable the token carries. This is the
    // distinction the amortization question turns on: `p.PkProfile` (the element's OWN identity, cardinality N,
    // cannot amortize) and `p.FkDepartmentCode` (a foreign reference into a bounded domain, may amortize) are the
    // SAME token `p` and only differ in the member name. A token-only dataset cannot tell them apart at all.
    [Test]
    [Arguments("""["p.PkProfile"]""", "p.PkProfile")]
    [Arguments("""["p.FkDepartmentCode"]""", "p.FkDepartmentCode")]
    public void The_key_path_carries_the_member_not_just_the_element(string argumentNames, string expected)
    {
        var fanout = FactIterationFanoutDeriver
            .Derive([Call(loopKind: "foreach", loopDetail: "p in profiles", argumentNames: argumentNames)], Rules)
            .ShouldHaveSingleItem();

        fanout.KeyToken.ShouldBe("p");
        fanout.KeyPath.ShouldBe(expected);
    }

    // The leading '~' marking a REDUCED COMPOSITE surface is load-bearing elsewhere (FactEffectDeriver rejects a
    // marked value as a resource identity), so the path must carry it through verbatim rather than clean it up.
    [Test]
    public void A_composite_argument_surface_keeps_its_reduced_mark()
    {
        var fanout = FactIterationFanoutDeriver
            .Derive([Call(loopKind: "foreach", loopDetail: "s in names", argumentNames: """["~NominalCodeFields.Name|s.Trim"]""")], Rules)
            .ShouldHaveSingleItem();

        fanout.KeyToken.ShouldBe("s");
        fanout.KeyPath.ShouldBe("~NominalCodeFields.Name|s.Trim");
    }

    // A key matched only through the TEMPLATE has no member path; the template itself is then the surface the
    // match was made on, and reporting "" would lose the only evidence there is.
    [Test]
    public void A_template_match_reports_the_template_as_the_path()
    {
        var fanout = FactIterationFanoutDeriver
            .Derive([Call(loopKind: "foreach", loopDetail: "id in ids", argumentTemplates: """["/var/{id}"]""")], Rules)
            .ShouldHaveSingleItem();

        fanout.KeyPath.ShouldBe("/var/{id}");
    }

    [Test]
    public void A_keyless_anchor_has_no_key_path() =>
        FactIterationFanoutDeriver
            .Derive([Call(loopKind: "while", loopDetail: "while")], Rules)
            .ShouldHaveSingleItem()
            .KeyPath.ShouldBe("");

    // The loop's element TYPE, which the source-text detail cannot supply: "row in rows" says nothing about what
    // a row is. This is the semantic half of the self-keyed test.
    [Test]
    public void A_loop_carries_its_resolved_element_type()
    {
        var fanout = FactIterationFanoutDeriver
            .Derive(
                [
                    Call(
                        loopKind: "foreach",
                        loopDetail: "p in profiles",
                        argumentNames: """["p.PkProfile"]""",
                        elementType: "N.ProfileEntity"
                    ),
                ],
                Rules
            )
            .ShouldHaveSingleItem();

        fanout.ElementType.ShouldBe("N.ProfileEntity");
    }

    // For a lambda anchor the element type is the ONLY iteration evidence there is: IteratedSource degenerates to
    // the enumerating method name, so a semantic self-keyed test has nothing else to read.
    [Test]
    public void A_lambda_anchor_takes_its_element_type_from_the_lambda_parameter()
    {
        var enclosing = FactStructuralContext.EncodeInvocations([
            new FactStructuralContext.EnclosingInvocation(
                ReceiverText: "profiles",
                ReceiverType: "System.Collections.Generic.IEnumerable`1",
                MethodName: "Select",
                DeclaringType: "System.Linq.Enumerable",
                LambdaParameter: "p",
                LambdaParameterType: "N.ProfileEntity"
            ),
        ]);

        var fanout = FactIterationFanoutDeriver
            .Derive([Call(loopKind: null, loopDetail: null, argumentNames: """["p.PkProfile"]""", enclosingInvocations: enclosing)], Rules)
            .ShouldHaveSingleItem();

        fanout.IterationKind.ShouldBe("lambda");
        fanout.IteratedSource.ShouldBe("Select"); // useless on its own — hence the element type
        fanout.ElementType.ShouldBe("N.ProfileEntity");
        fanout.KeyPath.ShouldBe("p.PkProfile");
    }

    // An ANONYMOUS projection has no nameable element type, and that is the case the lexical key path exists for
    // (Admin/Profile/Home2: `select new { p.PkProfile, l.PkLicense }` then `ProfileCache.New(p.PkProfile)`). The
    // two signals are therefore not redundant — one fires where the other structurally cannot.
    [Test]
    public void An_anonymous_projection_has_no_element_type_but_still_has_a_key_path()
    {
        var fanout = FactIterationFanoutDeriver
            .Derive(
                [
                    Call(
                        loopKind: "query",
                        loopDetail: "p, profile in profiles.ToList().DistinctOn(p => p.PkProfile)",
                        argumentNames: """["p.PkProfile"]""",
                        elementType: null
                    ),
                ],
                Rules
            )
            .ShouldHaveSingleItem();

        fanout.ElementType.ShouldBe("");
        fanout.KeyPath.ShouldBe("p.PkProfile");
    }
}
