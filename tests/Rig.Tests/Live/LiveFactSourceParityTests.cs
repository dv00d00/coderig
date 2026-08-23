using System.Globalization;
using System.Text;
using Rig.Analysis.Rules;
using Rig.Cli.Effects;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Live;

// THE PARITY GATE for live query serving. `LiveReads` + `LiveFactSource` project the query-side artifacts
// (shaped graph, entry-point facts, whole-store hazard effects and every loader feeding them) straight off an
// in-memory AnalysisResult, so the resident live index can be QUERIED without a SQLite round-trip. That is
// only sound while each projection is field-for-field identical to the `Reads` method it mirrors — this test
// is what enforces that: analyze a real playground, save those facts to a temp rig.db, read them back through
// `Reads`, and assert every live projection is SET-EQUAL to the store one.
//
// Four measurement lessons are baked into the assertions and must not be softened:
//   1. SETS, not counts. A count-only comparison once produced a confident-but-false "24.5% of facts LOST"
//      here (the store holds duplicate fact rows). Every assertion below is a symmetric difference and prints
//      the ACTUAL differing elements on failure — never just a number. (Total counts are checked too, but
//      only ON TOP of set equality, so a multiplicity drift can't hide either.)
//   2. ANTI-VACUITY. A parity check over two empty lists passes and proves nothing, so every collection that
//      should be populated carries an explicit non-empty guard. In particular the event-bearing playground
//      (EntryPointEffects declares `public event Action? Saved;`) must produce non-empty delivery sites and
//      event-subscription sites, or the `csharp-event` delivery rule would be firing vacuously — and each
//      playground additionally asserts one ARM of the shared DeliverySiteProjection core is populated
//      (event_raise here, the Echo actor_tell arg arm on LegacyNet48Web).
//      KNOWN BLIND SPOTS — fact kinds no available playground produces, so their parity passes vacuously and
//      the guard is deliberately off: ThreadStaticFieldIds and VolatileFieldIds (no `[ThreadStatic]` or
//      `volatile` declaration exists in playgrounds/) and StaticFieldAccessRefsByKind.Writes (no static-field
//      WRITE; the READ arm IS populated). Add a fixture that declares them to close these.
//   3. HazardEffects is the DEEPEST assertion — it composes ~8 of the twins (invocations, ctor/base edges from
//      the EP data, throws, both static-field arms, [ThreadStatic]/volatile cells, the async-method set,
//      allocations). It is compared as canonical strings carrying provider/op/resource/enclosing/file/line/
//      atomic/guards/mechanism/cardinality/size AND every observation, so no field-level divergence can hide.
//   4. AllocationFacts is asserted to need no twin rather than assumed: Reads.LoadAllocationFactsAsync
//      (whole-store) applies no filter and no dedup, so `result.AllocationFacts` should already BE its return
//      value — the test proves that instead of LiveReads carrying a pass-through wrapper.
[ClassDataSource<AnalyzedPlaygrounds>(Shared = SharedType.PerTestSession)]
public sealed class LiveFactSourceParityTests(AnalyzedPlaygrounds playgrounds)
{
    private static readonly object ReportLock = new();

    // EntryPointEffects declares a real C# event (`AuditNotifications.Saved`), so the `csharp-event` delivery
    // rule fires non-vacuously here — the delivery-site + event-subscription-site parity is meaningful.
    [Test]
    public async Task Live_projections_match_the_store_on_the_event_bearing_playground()
    {
        var playground = await playgrounds.EntryPointEffectsAsync();
        await AssertParityAsync(playground, "EntryPointEffects", eventBearing: true);
    }

    [Test]
    public async Task Live_projections_match_the_store_on_the_legacy_web_playground()
    {
        var playground = await playgrounds.LegacyNet48Async();
        await AssertParityAsync(playground, "LegacyNet48Web", eventBearing: false);
    }

    private static async Task AssertParityAsync(AnalyzedPlayground playground, string label, bool eventBearing)
    {
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);
        var facts = playground.Result;
        var live = new LiveFactSource(facts, rules);

        var directory = Path.Combine(Path.GetTempPath(), "rig-liveparity-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "rig.db");
        try
        {
            await using (var write = new RigDbContext(databasePath, pooling: false))
            {
                await Writes.SaveAsync(write, facts);
            }

            await using var read = new RigDbContext(databasePath, pooling: false);

            // --- the individual LiveReads twins -------------------------------------------------------
            var storeSignatures = await Reads.LoadMonomorphizationSignaturesAsync(read);
            AssertSetEqual(
                label,
                "MonomorphizationSignatures",
                LiveReads.MonomorphizationSignatures(facts).ToList(),
                (storeSignatures ?? new Dictionary<string, string>()).ToList()
            );

            AssertSetEqual(
                label,
                "EventSubscriptionSites",
                LiveReads.EventSubscriptionSites(facts),
                await Reads.EventSubscriptionSitesAsync(read),
                requireNonEmpty: eventBearing
            );

            var liveDelivery = LiveReads.DeliverySites(facts, rules.Delivery);
            AssertSetEqual(label, "DeliverySites", liveDelivery, await Reads.LoadDeliverySitesAsync(read, rules.Delivery));

            // Anti-vacuity for BOTH arms of the shared DeliverySiteProjection core, one per playground: the
            // event-symbol arm on the event-bearing playground (`csharp-event`, tag event_raise) and the arg
            // arm on the legacy one (`echo-actor` spawn/tell/ask, tag actor_tell). Without these a rule could
            // stop matching entirely and the set-equality above would still pass on two empty lists.
            var expectedTag = eventBearing ? "event_raise" : "actor_tell";
            liveDelivery
                .Count(site => string.Equals(site.Tag, expectedTag, StringComparison.Ordinal))
                .ShouldBeGreaterThan(
                    0,
                    $"[{label}] no delivery site tagged '{expectedTag}' — that arm of DeliverySiteProjection is not being exercised."
                );

            var liveEp = LiveReads.FactEntryPointData(facts);
            var storeEp = await Reads.LoadFactEntryPointDataAsync(read);
            AssertSetEqual(label, "FactEntryPointData.BaseEdges", liveEp.BaseEdges, storeEp.BaseEdges);
            AssertSetEqual(label, "FactEntryPointData.InterfaceEdges", liveEp.InterfaceEdges ?? [], storeEp.InterfaceEdges ?? []);
            AssertSetEqual(label, "FactEntryPointData.Methods", liveEp.Methods, storeEp.Methods);
            AssertSetEqual(label, "FactEntryPointData.Types", liveEp.Types, storeEp.Types);
            AssertSetEqual(label, "FactEntryPointData.CtorRefs", liveEp.CtorRefs, storeEp.CtorRefs);

            AssertSetEqual(label, "InvocationRefs", LiveReads.InvocationRefs(facts), await Reads.LoadInvocationRefsAsync(read));
            AssertSetEqual(label, "ThrowRefs", LiveReads.ThrowRefs(facts), await Reads.LoadThrowRefsAsync(read));

            var (liveWrites, liveReads) = LiveReads.StaticFieldAccessRefsByKind(facts);
            var (storeWrites, storeReads) = await Reads.LoadStaticFieldAccessRefsByKindAsync(read);
            AssertSetEqual(label, "StaticFieldAccessRefsByKind.Writes", liveWrites, storeWrites, requireNonEmpty: false);
            AssertSetEqual(label, "StaticFieldAccessRefsByKind.Reads", liveReads, storeReads, requireNonEmpty: false);

            AssertSetEqual(
                label,
                "ThreadStaticFieldIds",
                LiveReads.ThreadStaticFieldIds(facts),
                await Reads.LoadThreadStaticFieldIdsAsync(read),
                requireNonEmpty: false
            );
            AssertSetEqual(
                label,
                "VolatileFieldIds",
                LiveReads.VolatileFieldIds(facts),
                await Reads.LoadVolatileFieldIdsAsync(read),
                requireNonEmpty: false
            );
            AssertSetEqual(label, "DeadCodeMethods", LiveReads.DeadCodeMethods(facts), await Reads.LoadDeadCodeMethodsAsync(read));

            // AllocationFacts: asserted to already be Reads.LoadAllocationFactsAsync's shape, which is why
            // LiveReads carries no twin for it.
            AssertSetEqual(
                label,
                "AllocationFacts (no twin — result.AllocationFacts IS the shape)",
                facts.AllocationFacts ?? [],
                await Reads.LoadAllocationFactsAsync(read),
                requireNonEmpty: false
            );

            // --- LiveFactSource's three composed artifacts --------------------------------------------
            var storeGraph = await Reads.LoadShapedGraphAsync(read, rules);
            var liveGraph = live.ShapedGraph;
            AssertSetEqual(label, "ShapedGraph.CallEdges", liveGraph.CallEdges, storeGraph.CallEdges);
            AssertSetEqual(
                label,
                "ShapedGraph.ImplementsEdges",
                liveGraph.ImplementsEdges,
                storeGraph.ImplementsEdges,
                requireNonEmpty: false
            );
            AssertSetEqual(label, "ShapedGraph.BaseEdges", liveGraph.BaseEdges ?? [], storeGraph.BaseEdges ?? [], requireNonEmpty: false);
            AssertSetEqual(label, "ShapedGraph.Methods", liveGraph.Methods, storeGraph.Methods);
            AssertSetEqual(
                label,
                "ShapedGraph.MinedDispatch",
                liveGraph.MinedDispatch ?? [],
                storeGraph.MinedDispatch ?? [],
                requireNonEmpty: false
            );

            var storeEpData = await Reads.LoadFactEntryPointDataAsync(read);
            AssertSetEqual(label, "LiveFactSource.EpData.Methods", live.EpData.Methods, storeEpData.Methods);
            AssertSetEqual(label, "LiveFactSource.EpData.Types", live.EpData.Types, storeEpData.Types);
            AssertSetEqual(label, "LiveFactSource.EpData.CtorRefs", live.EpData.CtorRefs, storeEpData.CtorRefs);
            AssertSetEqual(label, "LiveFactSource.EpData.BaseEdges", live.EpData.BaseEdges, storeEpData.BaseEdges);

            // The deepest assertion: the whole-store hazard-augmented effect set, compared field-by-field
            // (including every observation) as canonical strings.
            var storeEffects = await EffectDerivation.DeriveHazardEffectsAsync(read, rules);
            AssertSetEqual(
                label,
                "LiveFactSource.HazardEffects",
                live.HazardEffects.Select(Canonical).ToList(),
                storeEffects.Select(Canonical).ToList()
            );
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            { /* best-effort cleanup */
            }
        }
    }

    // A DerivedEffect flattened to a single canonical string: every field plus every observation, so a
    // field-level divergence between the live and store derivations cannot hide behind a matching identity.
    private static string Canonical(DerivedEffect e)
    {
        var observations = string.Join(
            ";",
            (e.Observations ?? [])
                .Select(o => $"{o.Type}/{o.Context}/{o.Detail}/{o.Confidence}/{o.Basis}/{o.Reason}")
                .OrderBy(s => s, StringComparer.Ordinal)
        );
        return string.Join(
            "|",
            e.Provider,
            e.Operation,
            e.ResourceType,
            e.EnclosingSymbolId ?? "<null>",
            e.FilePath,
            e.Line.ToString(CultureInfo.InvariantCulture),
            e.Atomic ? "atomic" : "",
            e.EnclosingGuards ?? "",
            e.Mechanism ?? "",
            e.Cardinality ?? "",
            e.ShallowSizeBytes?.ToString(CultureInfo.InvariantCulture) ?? "",
            e.SizeConfidence ?? "",
            e.SizeBasis ?? "",
            observations
        );
    }

    // Asserts the live projection and the store projection are SET-EQUAL, reporting the actual differing
    // ELEMENTS (not just counts) on failure, and guarding against a vacuous pass over two empty collections.
    // Total counts are also compared, but strictly on top of set equality — never as a substitute for it.
    private static void AssertSetEqual<T>(
        string label,
        string artifact,
        IEnumerable<T> liveItems,
        IEnumerable<T> storeItems,
        bool requireNonEmpty = true
    )
    {
        var live = liveItems.ToList();
        var store = storeItems.ToList();
        var liveSet = live.ToHashSet();
        var storeSet = store.ToHashSet();
        var missing = storeSet.Except(liveSet).ToList();
        var extra = liveSet.Except(storeSet).ToList();

        Report(
            $"[parity/{label}] {artifact}: live={live.Count} store={store.Count} "
                + $"(distinct live={liveSet.Count} store={storeSet.Count}) missing={missing.Count} extra={extra.Count}"
        );

        var detail = Describe(label, artifact, missing, extra);
        missing.ShouldBeEmpty(detail);
        extra.ShouldBeEmpty(detail);
        live.Count.ShouldBe(
            store.Count,
            $"[{label}] {artifact}: sets agree but MULTIPLICITY differs (live={live.Count}, store={store.Count}) — "
                + "one side is deduping differently."
        );

        if (requireNonEmpty)
        {
            liveSet.Count.ShouldBeGreaterThan(
                0,
                $"[{label}] {artifact} is EMPTY — the parity assertion would pass vacuously. Either the "
                    + "playground stopped producing this fact kind or the projection broke."
            );
        }
    }

    private static string Describe<T>(string label, string artifact, IReadOnlyList<T> missing, IReadOnlyList<T> extra)
    {
        const int show = 10;
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"[{label}] {artifact} parity FAILED.");
        if (missing.Count > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}  MISSING from the live projection ({missing.Count}):");
            foreach (var item in missing.Take(show))
            {
                builder.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}    {item}");
            }

            if (missing.Count > show)
            {
                builder.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}    … and {missing.Count - show} more");
            }
        }

        if (extra.Count > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}  EXTRA in the live projection ({extra.Count}):");
            foreach (var item in extra.Take(show))
            {
                builder.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}    {item}");
            }

            if (extra.Count > show)
            {
                builder.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}    … and {extra.Count - show} more");
            }
        }

        return builder.ToString();
    }

    // The per-artifact parity numbers, for when someone wants to SEE the counts rather than just a green
    // tick. Deliberately NOT Console.WriteLine: TUnit on Microsoft.Testing.Platform does not surface console
    // output in its default mode, so a Console line here is a dead instrument that reads like observability
    // and provides none (the measurement lesson this program paid for twice). Set RIG_PARITY_REPORT to a file
    // path to collect them. Failure DETAIL never depends on this — the differing elements go in the assertion
    // message itself, which always surfaces.
    private static void Report(string line)
    {
        var path = Environment.GetEnvironmentVariable("RIG_PARITY_REPORT");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // The two parity tests run CONCURRENTLY, so an unguarded AppendAllText loses lines to file
        // contention — a diagnostic that silently drops rows is worse than none, because a missing
        // artifact reads as an artifact that was never compared. Serialize on a process-wide lock, and
        // retry briefly in case another PROCESS holds the file.
        lock (ReportLock)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.AppendAllText(path, line + Environment.NewLine);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(10);
                }
            }
        }
    }
}
