using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Cli.Impact;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// ImpactEngine.Select — the ONE selected view `rig impact` (render + both CI gates) and the web /api/impact
// consume. Two things are pinned here.
//
// (1) WHAT PerEp RETAINS. Selection used to drop any EP whose Added/Removed/Amplified went empty, which
// silently deleted the EPs whose only delta is a HAZARD gain/loss or an AMPLIFICATION-TIER (looped_effect)
// gain/loss — even though DiffFootprints puts them in PerEp precisely so they surface per-EP. They are
// retained now, and the effect-only number is reported separately as BehavioralEpCount, so the two counts are
// allowed to differ instead of agreeing by accident. A guard delta needs no arm of its own — it IS a
// lock/async_lock effect entry, so the effect arm covers it whenever the reader has not filtered locks away.
//
// (2) ONE BEHAVIORAL NUMBER ON EVERY SURFACE. The human header and impact_summary's behavioral_eps used to be
// computed two different ways (EffectChangedEpCount vs PerEp.Count); both now read the view's
// BehavioralEpCount, asserted end-to-end below under the default filter AND --intrinsic.
[ClassDataSource<AnalyzedPlaygrounds>(Shared = SharedType.PerTestSession)]
public sealed class ImpactSelectViewTests(AnalyzedPlaygrounds playgrounds)
{
    private static HashSet<string> Set(params string[] tokens) => new(tokens, StringComparer.OrdinalIgnoreCase);

    // An EP footprint delta with only the fields selection reads. Every list defaults EMPTY, so each test
    // below supplies exactly ONE kind of delta and nothing else — the independence is the point.
    private static EpFootprintDelta Delta(
        string route,
        (string Provider, string Operation, string Resource, string Enclosing)[]? added = null,
        (string Provider, string Operation, string Resource, string Enclosing)[]? removed = null,
        HazardFinding[]? hazardsAdded = null,
        HazardFinding[]? hazardsRemoved = null,
        EpAmplification[]? amplificationsAdded = null,
        EpAmplification[]? amplificationsRemoved = null,
        bool sharedMutationOnPath = false
    ) =>
        new(
            Kind: "http",
            Route: route,
            FilePath: $"/{route}.cs",
            Line: 1,
            BranchEffects: 3,
            BaseEffects: 3,
            Added: added ?? [],
            Removed: removed ?? [],
            Amplified: [],
            SharedMutationOnPath: sharedMutationOnPath,
            HazardsAdded: hazardsAdded ?? [],
            HazardsRemoved: hazardsRemoved ?? [],
            AmplificationsAdded: amplificationsAdded ?? [],
            AmplificationsRemoved: amplificationsRemoved ?? []
        );

    private static ImpactDiff Diff(params EpFootprintDelta[] perEp) => new(Ep: null, AffectedEps: [], PerEp: perEp);

    // A hazard-only EP: no effect entry at all, one gained race_window. Retained (that is what PerEp has always
    // claimed to contain) and counted in NEITHER behavioral count — a hazard is not an effect-set change, and
    // --expect-no-effect-change must stay a deterministic effect gate.
    [Test]
    public void A_hazard_only_ep_is_retained_and_is_not_counted_as_a_behavioral_change()
    {
        var diff = Diff(Delta("hazard-only", hazardsAdded: [new HazardFinding("race_window", "_cache", "App.Warm.Fill", "medium")]));

        var view = ImpactEngine.Select(diff, only: Set(), exclude: Set(), includeIntrinsic: false);

        view.PerEp.Select(d => d.Route).ShouldBe(["hazard-only"]);
        view.BehavioralEpCount.ShouldBe(0);
    }

    // The same for a LOST hazard finding (a fix closing a race) — the drop was symmetric, so the retention is.
    [Test]
    public void A_hazard_removed_only_ep_is_retained_and_is_not_counted_as_a_behavioral_change()
    {
        var diff = Diff(Delta("hazard-fixed", hazardsRemoved: [new HazardFinding("n_plus_1", "loop@42", "App.List.Load", "high")]));

        var view = ImpactEngine.Select(diff, only: Set(), exclude: Set(), includeIntrinsic: false);

        view.PerEp.Select(d => d.Route).ShouldBe(["hazard-fixed"]);
        view.BehavioralEpCount.ShouldBe(0);
    }

    // The AMPLIFICATION TIER (looped_effect: this provider:operation is now reached inside an iteration
    // context) is a different signal from the `Amplified` effect list, and it is the one delta that leaves the
    // effect SET identical while the cost goes x N — so an EP carrying only that must survive selection.
    [Test]
    public void An_amplification_tier_only_ep_is_retained_and_is_not_counted_as_a_behavioral_change()
    {
        var diff = Diff(Delta("looped-only", amplificationsAdded: [new EpAmplification("http", "GET", Sites: 4)]));

        var view = ImpactEngine.Select(diff, only: Set(), exclude: Set(), includeIntrinsic: false);

        view.PerEp.Select(d => d.Route).ShouldBe(["looped-only"]);
        view.PerEp.Single().AmplificationsAddedOrEmpty.Single().ProviderOperation.ShouldBe("http:GET");
        view.BehavioralEpCount.ShouldBe(0);
    }

    [Test]
    public void An_amplification_tier_removal_only_ep_is_retained_and_is_not_counted_as_a_behavioral_change()
    {
        var diff = Diff(Delta("unlooped-only", amplificationsRemoved: [new EpAmplification("llblgen", "read", Sites: 2)]));

        var view = ImpactEngine.Select(diff, only: Set(), exclude: Set(), includeIntrinsic: false);

        view.PerEp.Select(d => d.Route).ShouldBe(["unlooped-only"]);
        view.BehavioralEpCount.ShouldBe(0);
    }

    // D4 names a guard delta as a retention reason, but it needs no ARM of its own, and must not have one
    // (decided 2026-09-03). A guard delta IS a lock/async_lock entry in Added/Removed, so `--only audit`
    // strips the only evidence of it — and BOTH renderers evaluate HasGuardDeltaOnSharedMutation on the
    // FILTERED delta, so retaining this EP would print `ep_delta … +0 -0 ~0` with no ep_guard_delta row and
    // no ⚠ line: an information-free husk. The reader explicitly asked not to be shown locks; dropping it is
    // the answer. (The alternative — evaluating the arm on the filtered delta — makes it strictly subsumed by
    // the effect arm, i.e. dead code implying a rule that never fires.)
    [Test]
    public void A_guard_delta_only_ep_is_dropped_by_a_filter_that_strips_its_lock_effects_rather_than_left_as_a_husk()
    {
        var diff = Diff(Delta("guard-only", added: [("lock", "acquire", "_cache", "App.Warm.Fill")], sharedMutationOnPath: true));

        var view = ImpactEngine.Select(diff, only: Set("audit"), exclude: Set(), includeIntrinsic: false);

        view.PerEp.ShouldBeEmpty();
        view.BehavioralEpCount.ShouldBe(0);
    }

    // The same EP under the DEFAULT filter: lock is not intrinsic, so the guard effect survives, the EP is
    // retained by the effect arm, and it IS an effect-set change. The two cases together fix the arm's meaning.
    [Test]
    public void The_same_guard_delta_ep_is_an_effect_change_under_the_default_filter()
    {
        var diff = Diff(Delta("guard-only", added: [("lock", "acquire", "_cache", "App.Warm.Fill")], sharedMutationOnPath: true));

        var view = ImpactEngine.Select(diff, only: Set(), exclude: Set(), includeIntrinsic: false);

        view.PerEp.Single().Added.Select(x => $"{x.Provider}:{x.Operation}").ShouldBe(["lock:acquire"]);
        view.BehavioralEpCount.ShouldBe(1);
        ImpactEngine.HasGuardDeltaOnSharedMutation(view.PerEp.Single()).ShouldBeTrue();
    }

    // The husk rule survives D4: an EP whose every effect is filtered away and that carries NO other delta is
    // dropped entirely, so no `ep_delta … +0 -0 ~0` row is left behind by filtering.
    [Test]
    public void An_ep_with_no_other_delta_is_dropped_when_every_effect_is_filtered_out()
    {
        var diff = Diff(Delta("alloc-only", added: [("alloc", "object", "Dictionary", "App.A.B")]));

        var view = ImpactEngine.Select(diff, only: Set(), exclude: Set(), includeIntrinsic: false);

        view.PerEp.ShouldBeEmpty();
        view.BehavioralEpCount.ShouldBe(0);
        view.HiddenIntrinsic.ShouldBe(1); // ...but never silently: the withheld entry is still disclosed
    }

    // HiddenIntrinsic still counts every WITHHELD entry, including the ones on an EP that is dropped and the
    // ones on an EP that survives — it is the audit trail for the gate's default loosening.
    [Test]
    public void Hidden_intrinsic_counts_every_withheld_entry_across_dropped_and_surviving_eps()
    {
        var diff = Diff(
            Delta("noisy", added: [("alloc", "object", "Dictionary", "App.A.B"), ("throw", "raise", "InvalidOp", "App.A.B")]),
            Delta("real", added: [("llblgen", "write", "DocumentHistoryEntity", "App.C.D"), ("alloc", "array", "byte[]", "App.C.D")])
        );

        var view = ImpactEngine.Select(diff, only: Set(), exclude: Set(), includeIntrinsic: false);

        view.PerEp.Select(d => d.Route).ShouldBe(["real"]);
        view.PerEp.Single().Added.Select(x => x.Provider).ShouldBe(["llblgen"]);
        view.BehavioralEpCount.ShouldBe(1);
        view.HiddenIntrinsic.ShouldBe(3);
    }

    // The structural section PARTITIONS the affected set against the SELECTED PerEp: an affected EP the per-EP
    // deltas carry (here, for a hazard alone) is not repeated as structural-only, and one they don't carry is
    // listed with its cause. This is what keeps the two sections from double-counting an entry point.
    [Test]
    public void Structural_only_excludes_the_eps_the_selected_per_ep_deltas_carry_and_classifies_the_rest()
    {
        var hazardOnly = Delta("hazard-only", hazardsAdded: [new HazardFinding("race_window", "_cache", "App.Warm.Fill", "low")]);
        var diff = new ImpactDiff(
            Ep: null,
            AffectedEps:
            [
                Reach("hazard-only"),
                Reach("record-shape", added: ["App.Company.get_HealthcodeInsurerCode", "R:P:App.Company.HealthcodeInsurerCode"]),
            ],
            PerEp: [hazardOnly]
        );

        var view = ImpactEngine.Select(diff, only: Set(), exclude: Set(), includeIntrinsic: false);

        view.AffectedEpCount.ShouldBe(2);
        view.StructuralOnly.Select(s => s.Ep.Route).ShouldBe(["record-shape"]);
        view.StructuralOnly.Single().Cause.ShouldBe(StructuralCause.RecordShape);
    }

    private static EpReachDelta Reach(string route, string[]? added = null) =>
        new(
            Kind: "http",
            Route: route,
            FilePath: $"/{route}.cs",
            Line: 1,
            Requires: null,
            Added: [],
            Removed: [],
            AddedStems: added ?? [],
            RemovedStems: [],
            ChangedStems: [],
            DistinctStemDelta: added?.Length ?? 0,
            InPlaceCount: added is null ? 1 : 0
        );

    // END-TO-END, the number this slice exists to collapse: the human header's "N entry point(s) with a changed
    // behavior" and `--format tsv`'s `impact_summary behavioral_eps` are the SAME number, under the default
    // filter and under --intrinsic. They were computed two different ways and agreed only because filtering
    // dropped the EPs where they differ. The two renderings, from a real MedDBase two-store run:
    //   Diff vs 'a1d65d423431': +1/-0 entry point(s); 38 entry point(s) with a changed behavior, 40 with a …
    //   impact_summary<TAB>eps=40<TAB>behavioral_eps=38<TAB>effect_added=108<TAB>…<TAB>intrinsic_hidden=11782
    // Counts are playground-dependent here, so the assertion is the AGREEMENT, plus a non-zero behavioral
    // count so the equality is not trivially 0 == 0.
    [Test]
    public async Task The_human_header_and_the_tsv_summary_report_one_behavioral_count_under_both_filters()
    {
        var headPg = await playgrounds.EntryPointEffectsAsync();
        // The BASE is the same analysis MINUS the HttpClient.GetStringAsync call sites, so the EP set is
        // identical on both sides while the EPs that reach BillingClient (TeamsController.Get ->
        // TeamWorkflow.LoadTeamSummaryAsync -> BillingClient.LoadInvoiceAsync) GAIN an http effect in head.
        // That is a genuine per-EP effect delta on a SHARED entry point — the only shape that makes the
        // behavioral count non-zero, and therefore the only one that makes the agreement assertion mean anything.
        var baseResult = headPg.Result with
        {
            References =
            [
                .. (headPg.Result.References ?? []).Where(r => !r.TargetSymbolId.Contains("GetStringAsync", StringComparison.Ordinal)),
            ],
        };
        var wd = NewWorkingDirectory();
        try
        {
            var baseId = await MaterializeStoreAsync(wd, baseResult, storeId: "selectviewbase");
            var headId = await MaterializeStoreAsync(wd, headPg.Result, storeId: "selectviewhead");

            var defaultRun = await BehavioralCountsAsync(wd, baseId, headId, extra: []);
            var intrinsicRun = await BehavioralCountsAsync(wd, baseId, headId, extra: ["--intrinsic"]);

            defaultRun.Header.ShouldBe(defaultRun.Summary);
            intrinsicRun.Header.ShouldBe(intrinsicRun.Summary);
            defaultRun.Header.ShouldBeGreaterThan(0); // the two playgrounds differ, so this is not 0 == 0
        }
        finally
        {
            TryDelete(wd);
        }
    }

    // Run `rig impact` twice (human + tsv) and read the behavioral count out of each rendering.
    private static async Task<(int Header, int Summary)> BehavioralCountsAsync(string wd, string baseId, string headId, string[] extra)
    {
        string[] args = ["impact", "--base", baseId, "--head", headId, .. extra];
        var human = new StringWriter();
        var error = new StringWriter();
        (await CliApplication.RunAsync(args, human, error, wd)).ShouldBe(0);

        var tsv = new StringWriter();
        (await CliApplication.RunAsync([.. args, "--format", "tsv"], tsv, error, wd)).ShouldBe(0);

        var header = Number(human.ToString(), " entry point(s) with a changed behavior", before: true);
        var summary = Number(tsv.ToString(), "behavioral_eps=", before: false);
        return (header, summary);
    }

    // Pull the integer that sits immediately before (human header) or after (tsv summary) a marker.
    private static int Number(string text, string marker, bool before)
    {
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"marker '{marker}' missing from:\n{text}");
        if (before)
        {
            var end = at;
            var start = end;
            while (start > 0 && char.IsAsciiDigit(text[start - 1]))
            {
                start--;
            }

            return int.Parse(text[start..end]);
        }

        var from = at + marker.Length;
        var to = from;
        while (to < text.Length && char.IsAsciiDigit(text[to]))
        {
            to++;
        }

        return int.Parse(text[from..to]);
    }

    private static string NewWorkingDirectory()
    {
        var wd = Path.Combine(Path.GetTempPath(), $"rig-impact-select-{Guid.NewGuid():n}");
        Directory.CreateDirectory(wd);
        return wd;
    }

    private static async Task<string> MaterializeStoreAsync(string workingDirectory, AnalysisResult result, string storeId)
    {
        var dir = StoreLayout.NewStoreDir(workingDirectory, storeId);
        await using var ctx = new RigDbContext(Path.Combine(dir, StoreLayout.DbFileName), pooling: false);
        await Writes.SaveAsync(ctx, result, provenance: null);
        return storeId;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup; a held SQLite handle must not fail the test.
        }
    }
}
