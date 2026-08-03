using Rig.Cli.Impact;
using Shouldly;

namespace Rig.Tests.Cli;

// The AMPLIFICATION tier in `rig impact`: the per-EP delta of looped_effect, at the TERSE grain.
//
// Why it is wired at all: wrapping a loop around an EXISTING call leaves the effect SET identical on both stores,
// so the Added/Removed lists say nothing — yet the effect now costs xN. That is exactly what a reviewer needs
// told. The volume objection does not apply, because impact reports a DELTA and a newly-introduced loop is rare
// and small (unlike the ~1.5k-site whole-store inventory `derive` shows).
//
// Why TERSE: one entry per (EP x provider:operation) with a site COUNT, never one row per site. EpAmplification's
// identity is the pair alone (Sites is outside equality), so "this EP's http:POST became looped" surfaces and "its
// site count ticked 3 -> 4" deliberately does not. These pin the pure diff over synthetic per-EP sets — no store.
public sealed class ImpactAmplificationDeltaTests
{
    private static EntryPointRef Ep(string kind, string route) => new(kind, route, $"/{route}.cs", 1, null);

    private static IReadOnlyDictionary<(string Kind, string Route), EntryPointRef> EpByKey(string kind, string route) =>
        new Dictionary<(string Kind, string Route), EntryPointRef> { [(kind, route)] = Ep(kind, route) };

    // An EP footprint with ONE effect key, IDENTICAL on both sides — the whole point: the effect set does not move
    // when a loop is introduced, so only the amplification delta can report it.
    private static Dictionary<(string Kind, string Route), Dictionary<(string, string, string, string), EffectReach>> Footprint(
        string kind,
        string route
    ) =>
        new()
        {
            [(kind, route)] = new Dictionary<(string, string, string, string), EffectReach>
            {
                [("http", "POST", "hook", "M:App.Svc.Push")] = new EffectReach(Count: 1, InLoop: false),
            },
        };

    private static Dictionary<(string Kind, string Route), HashSet<EpAmplification>> Amp(
        string kind,
        string route,
        params EpAmplification[] entries
    ) => new() { [(kind, route)] = [.. entries] };

    [Test]
    public void A_loop_introduced_around_an_existing_effect_produces_exactly_one_terse_added_row()
    {
        var footprint = Footprint("action", "Notify");

        var deltas = ImpactEngine.DiffFootprints(
            branch: footprint,
            baseStore: Footprint("action", "Notify"),
            epByKey: EpByKey("action", "Notify"),
            branchAmplifications: Amp("action", "Notify", new EpAmplification("http", "POST", Sites: 1)),
            baseAmplifications: Amp("action", "Notify")
        );

        // The EP surfaces even though its effect set is UNCHANGED — a pure finding gain must not be dropped.
        deltas.Count.ShouldBe(1);
        deltas[0].Added.ShouldBeEmpty();
        deltas[0].Removed.ShouldBeEmpty();

        // Exactly ONE terse row, at (provider, operation) grain, with the site count riding along.
        var added = deltas[0].AmplificationsAddedOrEmpty;
        added.Count.ShouldBe(1);
        added[0].Provider.ShouldBe("http");
        added[0].Operation.ShouldBe("POST");
        added[0].Sites.ShouldBe(1);
        added[0].ProviderOperation.ShouldBe("http:POST");
        deltas[0].AmplificationsRemovedOrEmpty.ShouldBeEmpty();
    }

    [Test]
    public void Removing_the_loop_produces_exactly_one_terse_removed_row()
    {
        var deltas = ImpactEngine.DiffFootprints(
            branch: Footprint("action", "Notify"),
            baseStore: Footprint("action", "Notify"),
            epByKey: EpByKey("action", "Notify"),
            branchAmplifications: Amp("action", "Notify"),
            baseAmplifications: Amp("action", "Notify", new EpAmplification("http", "POST", Sites: 4))
        );

        deltas.Count.ShouldBe(1);
        deltas[0].AmplificationsAddedOrEmpty.ShouldBeEmpty();
        var removed = deltas[0].AmplificationsRemovedOrEmpty;
        removed.Count.ShouldBe(1);
        removed[0].ProviderOperation.ShouldBe("http:POST");
        // The count reported for a REMOVED row is the BASE side's (it no longer exists on head).
        removed[0].Sites.ShouldBe(4);
    }

    // The terse grain, stated as a test: many looped SITES of one provider:operation collapse to ONE row, and a
    // pure site-count move is NOT a delta at all.
    [Test]
    public void Many_looped_sites_collapse_to_one_row_and_a_count_move_alone_is_not_a_delta()
    {
        var manySites = ImpactEngine.DiffFootprints(
            branch: Footprint("action", "Notify"),
            baseStore: Footprint("action", "Notify"),
            epByKey: EpByKey("action", "Notify"),
            branchAmplifications: Amp("action", "Notify", new EpAmplification("llblgen", "write", Sites: 37)),
            baseAmplifications: Amp("action", "Notify")
        );
        manySites[0].AmplificationsAddedOrEmpty.Count.ShouldBe(1);
        manySites[0].AmplificationsAddedOrEmpty[0].Sites.ShouldBe(37);

        // Same pair on both sides, different site counts => identity unchanged => NO row, and (with no other
        // change) the EP is not listed at all. This is what keeps the reviewer-facing noise near zero.
        var countMove = ImpactEngine.DiffFootprints(
            branch: Footprint("action", "Notify"),
            baseStore: Footprint("action", "Notify"),
            epByKey: EpByKey("action", "Notify"),
            branchAmplifications: Amp("action", "Notify", new EpAmplification("llblgen", "write", Sites: 4)),
            baseAmplifications: Amp("action", "Notify", new EpAmplification("llblgen", "write", Sites: 3))
        );
        countMove.ShouldBeEmpty();
    }

    [Test]
    public void No_amplification_maps_means_no_amplification_rows()
    {
        // The --no-amplification path (and every pre-existing effect-only caller): both maps null => no rows, and
        // an otherwise-unchanged EP is not listed. Byte-identical to the pre-tier behaviour.
        var deltas = ImpactEngine.DiffFootprints(
            branch: Footprint("action", "Notify"),
            baseStore: Footprint("action", "Notify"),
            epByKey: EpByKey("action", "Notify")
        );
        deltas.ShouldBeEmpty();
    }

    [Test]
    public void Multiple_provider_operations_are_ordered_stably()
    {
        var deltas = ImpactEngine.DiffFootprints(
            branch: Footprint("action", "Notify"),
            baseStore: Footprint("action", "Notify"),
            epByKey: EpByKey("action", "Notify"),
            branchAmplifications: Amp(
                "action",
                "Notify",
                new EpAmplification("llblgen", "write", Sites: 2),
                new EpAmplification("http", "POST", Sites: 1),
                new EpAmplification("http", "GET", Sites: 1)
            ),
            baseAmplifications: Amp("action", "Notify")
        );

        deltas[0].AmplificationsAddedOrEmpty.Select(a => a.ProviderOperation).ShouldBe(["http:GET", "http:POST", "llblgen:write"]);
    }
}
