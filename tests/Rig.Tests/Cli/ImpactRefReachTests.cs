using Rig.Cli.Commands;
using Rig.Cli.Impact;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// Task 4 (Phase 3) — degenerate field/property-access nodes in the per-EP reach. The call graph has only
// method→method edges, so a changed readonly-field/property ACCESS inside a reachable method is invisible to
// the structural reach-set diff. Phase 3 unions the first-party read/write reference TARGETS of each reachable
// method into the reach set as degenerate leaf nodes, keyed with an `R:` prefix so they're distinct from
// method DocIDs and so a changed access surfaces in DiffReachSets. RefTargetsFor is the pure, store-free union
// step (per the perf constraint: a prebuilt enclosing→targets lookup, looked up per reachable method).
public sealed class ImpactRefReachTests
{
    [Test]
    public void Ref_targets_of_reachable_methods_are_unioned_as_degenerate_R_prefixed_nodes()
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal) { "M:N.T.Method()", "M:N.T.Other()" };
        // enclosing method -> the first-party field/property read/write target DocIDs it touches.
        var refsByEnclosing = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["M:N.T.Method()"] = new[] { "F:N.T.Field", "P:N.T.Prop" },
            ["M:N.NotReached.X()"] = new[] { "F:N.T.Unrelated" }, // not reachable => not unioned
        };

        var union = ImpactEngine.RefTargetsFor(reachable, refsByEnclosing);

        union.ShouldBe(new[] { "R:F:N.T.Field", "R:P:N.T.Prop" }, ignoreOrder: true);
    }

    [Test]
    public void No_refs_for_a_reachable_method_contributes_nothing()
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal) { "M:N.T.Method()" };
        var refsByEnclosing = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        ImpactEngine.RefTargetsFor(reachable, refsByEnclosing).ShouldBeEmpty();
    }

    [Test]
    public void A_changed_field_access_surfaces_through_DiffReachSets()
    {
        // Both stores reach the same method, but the branch's method now reads a field the base's didn't
        // (e.g. the access was added). The method-only reach is identical, so the +R: degenerate node is the
        // ONLY thing that makes this EP show as affected — exactly the Phase 3 goal.
        var branch = new Dictionary<(string Kind, string Route), HashSet<string>>
        {
            [("http", "x")] = new(StringComparer.Ordinal) { "M:N.T.M()", "R:F:N.T.NewField" },
        };
        var baseStore = new Dictionary<(string Kind, string Route), HashSet<string>>
        {
            [("http", "x")] = new(StringComparer.Ordinal) { "M:N.T.M()" },
        };
        var epByKey = new Dictionary<(string Kind, string Route), EntryPointRef>
        {
            [("http", "x")] = new(Kind: "http", Route: "x", FilePath: "x.cs", Line: 1, Requires: null),
        };

        var deltas = ImpactEngine.DiffReachSets(branch: branch, baseStore: baseStore, epByKey: epByKey);

        deltas.Count.ShouldBe(1);
        // The degenerate node has no `(` so StripParams leaves it intact => it's a genuine added stem.
        deltas[0].AddedStems.ShouldBe(new[] { "R:F:N.T.NewField" });
    }
}
