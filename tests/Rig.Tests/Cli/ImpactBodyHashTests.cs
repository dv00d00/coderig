using Rig.Cli.Commands;
using Rig.Cli.Impact;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// Task 3 (Phase 2) — the in-place body-edit signal. A reachable method whose BODY hash differs base↔branch is
// an in-place change the structural reach-set diff can't see (it stayed in the reach). These pin (a) the pure
// body-changed-set computation, (b) its per-EP attribution in DiffReachSets, and (c) the backward-compatible
// silent skip when either store lacks the BodyHash fact.
public sealed class ImpactBodyHashLogicTests
{
    private static IReadOnlyDictionary<string, string> Hashes(params (string Id, string Hash)[] pairs) =>
        pairs.ToDictionary(p => p.Id, p => p.Hash, StringComparer.Ordinal);

    [Test]
    public void Body_changed_set_is_the_differing_and_one_sided_symbols()
    {
        var branch = Hashes(("M:N.A.M()", "aaaa"), ("M:N.A.Changed()", "bbbb"), ("M:N.A.OnlyBranch()", "cccc"));
        var @base = Hashes(("M:N.A.M()", "aaaa"), ("M:N.A.Changed()", "ZZZZ"), ("M:N.A.OnlyBase()", "dddd"));

        var changed = ImpactEngine.BodyChangedSymbols(branch, @base);

        changed.ShouldBe(new[] { "M:N.A.Changed()", "M:N.A.OnlyBranch()", "M:N.A.OnlyBase()" }, ignoreOrder: true);
        changed.Contains("M:N.A.M()").ShouldBeFalse(); // identical hash => not changed
    }

    [Test]
    public void Empty_on_either_side_yields_no_signal_pre_fact_store()
    {
        var branch = Hashes(("M:N.A.M()", "aaaa"));

        // base has no BodyHash fact (pre-Phase-2 store) => guarded read returned empty => skip silently.
        ImpactEngine.BodyChangedSymbols(branch, Hashes()).ShouldBeEmpty();
        ImpactEngine.BodyChangedSymbols(Hashes(), branch).ShouldBeEmpty();
    }

    [Test]
    public void An_ep_with_no_structural_change_but_a_changed_reached_body_is_affected_in_place()
    {
        // The reach set is IDENTICAL (M:N.A.M present both sides), so the structural diff is empty — but the
        // method's body changed in place, so the EP must still surface, attributed via InPlace.
        var shared = new[] { "M:N.A.M()" };
        var branch = new Dictionary<(string Kind, string Route), HashSet<string>> { [("http", "x")] = new(shared, StringComparer.Ordinal) };
        var baseStore = new Dictionary<(string Kind, string Route), HashSet<string>>
        {
            [("http", "x")] = new(shared, StringComparer.Ordinal),
        };
        var epByKey = new Dictionary<(string Kind, string Route), EntryPointRef> { [("http", "x")] = new("http", "x", "/x.cs", 1, null) };
        var bodyChanged = new HashSet<string>(StringComparer.Ordinal) { "M:N.A.M()" };

        var deltas = ImpactEngine.DiffReachSets(branch, baseStore, epByKey, bodyChanged);

        deltas.Count.ShouldBe(1);
        deltas[0].AddedStems.ShouldBeEmpty();
        deltas[0].RemovedStems.ShouldBeEmpty();
        deltas[0].ChangedStems.ShouldBeEmpty();
        deltas[0].InPlaceCount.ShouldBe(1);
        deltas[0].InPlace.ShouldBe(new[] { "M:N.A.M()" });
        deltas[0].DistinctStemDelta.ShouldBe(1); // the in-place body change carries the magnitude
    }

    [Test]
    public void A_genuinely_added_method_is_not_double_counted_as_in_place()
    {
        // M:N.A.New is only in the branch reach => it's a structural ADD, not in-place (in-place is the SHARED
        // reach whose body changed). Even though its DocID is in the body-changed set, it must not appear under
        // InPlace (it's already attributed by the structural diff).
        var branch = new Dictionary<(string Kind, string Route), HashSet<string>>
        {
            [("http", "x")] = new(new[] { "M:N.A.M()", "M:N.A.New()" }, StringComparer.Ordinal),
        };
        var baseStore = new Dictionary<(string Kind, string Route), HashSet<string>>
        {
            [("http", "x")] = new(new[] { "M:N.A.M()" }, StringComparer.Ordinal),
        };
        var epByKey = new Dictionary<(string Kind, string Route), EntryPointRef> { [("http", "x")] = new("http", "x", "/x.cs", 1, null) };
        var bodyChanged = new HashSet<string>(StringComparer.Ordinal) { "M:N.A.New()" };

        var deltas = ImpactEngine.DiffReachSets(branch, baseStore, epByKey, bodyChanged);

        deltas.Count.ShouldBe(1);
        deltas[0].AddedStems.ShouldBe(new[] { "N.A.New" });
        deltas[0].InPlaceCount.ShouldBe(0); // M:N.A.New is an add, not a shared-reach body change
    }
}
