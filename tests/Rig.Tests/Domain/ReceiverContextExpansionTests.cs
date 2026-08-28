using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Domain;

// DEVIRTUALIZATION GAP: a virtual method's dispatch fan-out is a function of the CALL-SITE RECEIVER, but
// both forward traversals memoized "already expanded / already reached" on the SYMBOL ALONE. So the FIRST
// occurrence of a shared virtual hub (e.g. the external `EntityBase.Save`) won the expansion with ITS
// receiver's fan, and every LATER occurrence — with a DIFFERENT receiver, resolving to a DIFFERENT override
// — was silently collapsed: a "⋯elided" leaf in `tree`, and nothing at all in `reaches`.
//
// Observed on MedDBase: 62 `EntityBase.Save` nodes in one `rig tree` forest, exactly ONE expanded (to
// `CommonEntityBase.Save`, the inherited impl for the first receiver). The 61 others — including the
// in-loop `module.Save()` / `personCreditUsed.Save()` sites whose receivers are locally-constructed
// `AppointmentServiceModuleEntity` / `PersonCreditUsedEntity` — rendered opaque, so the child overrides'
// effects never reached the caller and the quadratic booking chain produced no looped-effect finding.
//
// The receiver is NOT in doubt at those sites (a local `new T{...}` pinned it); the memo threw it away.
public sealed class ReceiverContextExpansionTests
{
    // Mirrors the MedDBase shape:
    //   Ext.EntityBase.Save            — EXTERNAL virtual base (opaque, no body in the index)
    //     CommonEntityBase.Save        — first-party override; Alpha INHERITS it
    //       Beta.Save                  — a more-derived override, with its own effect-bearing body
    // Entry calls both sites: Alpha's (inherits) first, Beta's (overrides) second. Both receivers are
    // exactly known. Case (a)/(b)/(c)/(d) of the receiver taxonomy collapse to the same fact at query time
    // — extraction mines the site's STATIC receiver type regardless of how the value flowed there — so the
    // discriminator that actually matters is WHICH RECEIVER GOT THERE FIRST.
    private static FactGraphData Graph(string? secondSiteLoopKind = null)
    {
        var edges = new[]
        {
            new CallEdge("M:N.Entry.Go", "M:N.AlphaSite.Run", "invocation", "f.cs", 1),
            new CallEdge("M:N.Entry.Go", "M:N.BetaSite.Run", "invocation", "f.cs", 2),
            // Site 1 — receiver Alpha, which has NO override: resolves to the inherited CommonEntityBase.Save.
            new CallEdge("M:N.AlphaSite.Run", "M:Ext.EntityBase.Save", "invocation", "f.cs", 10, ReceiverType: "N.Alpha"),
            // Site 2 — receiver Beta, which DOES override: must resolve to Beta.Save. Optionally in a loop.
            new CallEdge(
                "M:N.BetaSite.Run",
                "M:Ext.EntityBase.Save",
                "invocation",
                "f.cs",
                20,
                LoopKind: secondSiteLoopKind,
                LoopDetail: secondSiteLoopKind is null ? null : "price in service.Prices",
                ReceiverType: "N.Beta"
            ),
            // Distinct effect-bearing bodies, so we can tell WHICH override was resolved.
            new CallEdge("M:N.CommonEntityBase.Save", "M:N.CommonAudit.Write", "invocation", "f.cs", 30),
            new CallEdge("M:N.Beta.Save", "M:N.BetaCache.Rebuild", "invocation", "f.cs", 40),
        };
        var bases = new[]
        {
            new BaseEdge("T:N.CommonEntityBase", "T:Ext.EntityBase"),
            new BaseEdge("T:N.Alpha", "T:N.CommonEntityBase"), // inherits Save
            new BaseEdge("T:N.Beta", "T:N.CommonEntityBase"), // overrides Save
        };
        var methods = new[]
        {
            new MethodRef("M:N.Entry.Go", "Go", "T:N.Entry"),
            new MethodRef("M:N.AlphaSite.Run", "Run", "T:N.AlphaSite"),
            new MethodRef("M:N.BetaSite.Run", "Run", "T:N.BetaSite"),
            new MethodRef("M:Ext.EntityBase.Save", "Save", "T:Ext.EntityBase"),
            new MethodRef("M:N.CommonEntityBase.Save", "Save", "T:N.CommonEntityBase", IsOverride: true),
            new MethodRef("M:N.Beta.Save", "Save", "T:N.Beta", IsOverride: true),
            new MethodRef("M:N.CommonAudit.Write", "Write", "T:N.CommonAudit"),
            new MethodRef("M:N.BetaCache.Rebuild", "Rebuild", "T:N.BetaCache"),
        };
        var mined = new[]
        {
            new DispatchFact("M:Ext.EntityBase.Save", "M:N.CommonEntityBase.Save", "override"),
            new DispatchFact("M:N.CommonEntityBase.Save", "M:N.Beta.Save", "override"),
        };
        return new FactGraphData(edges, Array.Empty<ImplementsEdge>(), methods, bases, mined);
    }

    private static TraceNode Child(TraceNode node, string id) => node.Children.Single(c => c.SymbolId == id);

    private static IEnumerable<TraceNode> Walk(TraceNode n)
    {
        yield return n;
        foreach (var c in n.Children)
        foreach (var d in Walk(c))
        {
            yield return d;
        }
    }

    // The headline gap. Both sites call the SAME external base method with DIFFERENT receivers; each must
    // devirtualize to its own override. Before the fix the second site was a Truncated "⋯elided" leaf.
    [Test]
    public void Each_call_site_devirtualizes_to_its_own_receivers_override()
    {
        var root = FactPathFinder.BuildTree(Graph(), "M:N.Entry.Go").Single();

        var alphaSave = Child(Child(root, "M:N.AlphaSite.Run"), "M:Ext.EntityBase.Save");
        var betaSave = Child(Child(root, "M:N.BetaSite.Run"), "M:Ext.EntityBase.Save");

        alphaSave.Children.Select(c => c.SymbolId).ShouldBe(["M:N.CommonEntityBase.Save"]);
        // Beta's site must NOT be swallowed by Alpha's earlier expansion of the same symbol.
        betaSave.Truncated.ShouldBeFalse();
        betaSave.Children.Select(c => c.SymbolId).ShouldBe(["M:N.Beta.Save"]);
    }

    // The consequence that made this matter: the override's OWN body must land under the caller, so its
    // effects compose with the caller's context. Before the fix `Beta.Save` was absent from the forest.
    [Test]
    public void The_resolved_overrides_body_reaches_the_caller()
    {
        var root = FactPathFinder.BuildTree(Graph(), "M:N.Entry.Go").Single();

        var all = Walk(root).Select(n => n.SymbolId).ToHashSet(StringComparer.Ordinal);
        all.ShouldContain("M:N.Beta.Save");
        all.ShouldContain("M:N.BetaCache.Rebuild");
        // The first site's resolution is unaffected.
        all.ShouldContain("M:N.CommonEntityBase.Save");
        all.ShouldContain("M:N.CommonAudit.Write");
    }

    // Loop context must COMPOSE with the recovered override: the `🔁` sits on the edge into the virtual hub,
    // and the override's effects now hang beneath it — which is what turns a looped call into a hazard.
    [Test]
    public void Loop_context_composes_with_the_recovered_override()
    {
        var root = FactPathFinder.BuildTree(Graph(secondSiteLoopKind: "foreach"), "M:N.Entry.Go").Single();

        var betaSave = Child(Child(root, "M:N.BetaSite.Run"), "M:Ext.EntityBase.Save");

        betaSave.LoopKind.ShouldBe("foreach");
        betaSave.LoopDetail.ShouldBe("price in service.Prices");
        betaSave.Truncated.ShouldBeFalse();
        Walk(betaSave).Select(n => n.SymbolId).ShouldContain("M:N.BetaCache.Rebuild");
    }

    // Same defect in the REACHABILITY engine (the one behind reaches / impact / correlation): `receiverOf`
    // was BFS-first-wins and a node already in `info` was never re-expanded, so the second receiver's
    // override was not merely rendered opaque — it was UNREACHABLE.
    [Test]
    public void Reachability_finds_the_override_of_every_receiver_not_just_the_first()
    {
        var reach = FactPathFinder.Reaches(Graph(), "M:N.Entry.Go");

        reach.Keys.ShouldContain("M:N.CommonEntityBase.Save");
        reach.Keys.ShouldContain("M:N.Beta.Save");
        reach.Keys.ShouldContain("M:N.BetaCache.Rebuild");
    }

    // Order independence: the SAME two sites reached in the opposite order must give the same answer.
    // (Before the fix the result flipped with source order — Beta first meant Alpha's inherited impl was
    // the one that vanished, which is why the gap looked intermittent across queries.)
    [Test]
    public void The_answer_does_not_depend_on_which_receiver_is_seen_first()
    {
        var reach = FactPathFinder.Reaches(Graph(), "M:N.BetaSite.Run");
        reach.Keys.ShouldContain("M:N.Beta.Save");

        var both = FactPathFinder.Reaches(Graph(), "M:N.Entry.Go");
        var alphaOnly = FactPathFinder.Reaches(Graph(), "M:N.AlphaSite.Run");
        var betaOnly = FactPathFinder.Reaches(Graph(), "M:N.BetaSite.Run");

        // The union of the two sites reached separately == what the shared entry point reaches.
        foreach (var id in alphaOnly.Keys.Concat(betaOnly.Keys))
        {
            both.Keys.ShouldContain(id);
        }
    }

    // RECALL GUARD (must hold before and after): a receiver-less CHA site still fans to every override, and
    // an inheriting receiver still does NOT drag in its sibling's override. The fix must not widen dispatch.
    [Test]
    public void Narrowing_is_unchanged_for_a_single_site()
    {
        var alphaOnly = FactPathFinder.Reaches(Graph(), "M:N.AlphaSite.Run");

        alphaOnly.Keys.ShouldContain("M:N.CommonEntityBase.Save");
        alphaOnly.Keys.ShouldNotContain("M:N.Beta.Save"); // Alpha is not a Beta — no sibling fan
    }
}
