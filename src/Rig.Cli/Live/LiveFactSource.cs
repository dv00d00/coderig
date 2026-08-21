using System.Diagnostics;
using System.Globalization;
using Rig.Cli.Effects;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;

namespace Rig.Cli.Live;

// The query-ready bundle over ONE generation of live facts — the in-memory counterpart of what a query
// command loads out of a .rig store before it can answer anything. Given the AnalysisResult a resident index
// (`rig watch` / ResidentIndex) currently holds plus the effective RuleSet, it exposes the three expensive
// derived artifacts every query path needs: the fully shaped call graph, the entry-point fact bundle, and the
// whole-store hazard-augmented effect set.
//
// Every artifact is projected through LiveReads, the in-memory twin of the query-side `Reads` surface, so a
// live-served answer is by construction the same answer a cold index would give. LiveFactSourceParityTests
// asserts that against a real saved store, artifact by artifact.
//
// It is a plain IMMUTABLE value object over one fact generation: no threads, no timers, no file watching, no
// invalidation. A new generation of facts means a NEW LiveFactSource — that is the whole invalidation model,
// and it is what makes each `Lazy<T>` below safe to memoize forever. Wiring this into `rig watch` (swapping
// the current generation in on each rebuild, and routing the query commands at it) is a separate slice.
//
// Thread-safety: the Lazy<T>s use the default ExecutionAndPublication mode, so a single-writer/multi-reader
// host can hand the same instance to concurrent readers and each artifact is still computed at most once.
internal sealed class LiveFactSource
{
    private readonly object _buildTimeLock = new();
    private readonly List<(string Artifact, TimeSpan Elapsed)> _buildTimes = [];

    private readonly Lazy<FactGraphData> _shapedGraph;
    private readonly Lazy<FactGraphData> _traversalGraph;
    private readonly Lazy<FactEntryPointDeriver.FactEntryPointData> _epData;
    private readonly Lazy<IReadOnlyList<FactInvocation>> _invocations;
    private readonly Lazy<IReadOnlyList<SymbolRef>> _throwRefs;
    private readonly Lazy<ISet<EventSubscriptionSite>> _eventSubscriptionSites;
    private readonly Lazy<IReadOnlyList<DerivedEffect>> _effects;
    private readonly Lazy<IReadOnlyList<DerivedEffect>> _hazardEffects;

    public LiveFactSource(AnalysisResult facts, RuleSet rules)
    {
        Facts = facts;
        Rules = rules;
        _shapedGraph = Memo("shapedGraph", () => LiveReads.ShapedGraph(facts, rules));
        _traversalGraph = Memo("traversalGraph", () => TraversalGraphOf(facts, rules));
        _epData = Memo("epData", () => LiveReads.FactEntryPointData(facts));
        _invocations = Memo("invocations", () => LiveReads.InvocationRefs(facts));
        _throwRefs = Memo("throwRefs", () => LiveReads.ThrowRefs(facts));
        _eventSubscriptionSites = Memo("eventSites", () => LiveReads.EventSubscriptionSites(facts));
        _effects = Memo("effects", () => QueryEffectDerivation.ForReach(rules, ReachInputs, TraversalGraph));
        _hazardEffects = Memo("hazardEffects", () => DeriveHazardEffects(facts, rules, EpData));
    }

    // The fact generation this source serves. Immutable — a rebuild produces a new LiveFactSource.
    public AnalysisResult Facts { get; }

    public RuleSet Rules { get; }

    // Mirrors Reads.LoadShapedGraphAsync.
    public FactGraphData ShapedGraph => _shapedGraph.Value;

    // Mirrors the graph half of TraversalGraphLoader.LoadEffectReachInputsAsync — NOT LoadShapedGraphAsync.
    // The distinction is load-bearing and was measured, not guessed: the traversal commands
    // (reaches/tree/path/callers) shape the raw fact graph and STOP there, while LoadShapedGraphAsync (what
    // `derive` uses, and what ShapedGraph above mirrors) additionally runs AddDeliveryEdges, which CREATES
    // producer→handler handoff edges. Serving `reaches` off ShapedGraph would therefore have answered with
    // extra delivery reach the store path never walks — a live/store divergence that has nothing to do with
    // liveness. MarkEventSubscriptionHandoffs is deliberately NOT applied here either: the command applies it
    // (gated on `--raw`), exactly as it does on the store path.
    public FactGraphData TraversalGraph => _traversalGraph.Value;

    // Mirrors Reads.LoadFactEntryPointDataAsync.
    public FactEntryPointDeriver.FactEntryPointData EpData => _epData.Value;

    // Mirrors Reads.LoadInvocationRefsAsync.
    public IReadOnlyList<FactInvocation> Invocations => _invocations.Value;

    // Mirrors Reads.LoadThrowRefsAsync.
    public IReadOnlyList<SymbolRef> ThrowRefs => _throwRefs.Value;

    // Mirrors Reads.EventSubscriptionSitesAsync. Memoized like every other artifact: the traversal commands
    // apply MarkEventSubscriptionHandoffs on EVERY non---raw query, so an unmemoized projection over the whole
    // reference-fact set would be re-run per query for the life of a generation — a real per-query cost that
    // was invisible precisely because it was not in BuildTimes.
    public ISet<EventSubscriptionSite> EventSubscriptionSites => _eventSubscriptionSites.Value;

    // The in-memory twin of what TraversalGraphLoader.LoadEffectReachInputsAsync hands a traversal command —
    // same record, same fields. The one structural difference from the store path is DISCLOSED rather than
    // hidden: there is no SQL, so nothing BOUNDS these inputs to the pattern's closure. `pattern`/`direction`
    // have no live analogue and are simply absent. Effects derived from the wider inputs are still filtered by
    // `reachable.ContainsKey(EnclosingSymbolId)` in the command, which is what keeps the answer equal — see
    // LiveReachesTests for the measured live-vs-store comparison this rests on.
    public SqlReachability.ReachInputs ReachInputs =>
        new SqlReachability.ReachInputs(
            Graph: TraversalGraph,
            Invocations: Invocations,
            CtorRefs: EpData.CtorRefs,
            ThrowRefs: ThrowRefs,
            // AllocationFacts needs no LiveReads twin: Reads.LoadAllocationFactsAsync (whole-store) applies no
            // filter and no dedup, so the extracted list already IS its return value.
            AllocationFacts: Facts.AllocationFacts ?? [],
            EpData: EpData
        );

    // The NON-hazard effect set `reaches`/`tree` derive: EffectDerivation.DeriveEffects over the reach inputs,
    // argument-for-argument the call ReachesCommand makes on the store path (no static-field feeds, no hazard
    // post-pass — those are `derive`'s whole-store arms and HazardEffects covers them). Memoized because it is
    // the single most expensive derived artifact per generation and every query in that generation wants the
    // same one.
    public IReadOnlyList<DerivedEffect> Effects => _effects.Value;

    // Mirrors EffectDerivation.DeriveHazardEffectsAsync — the whole-store hazard-augmented effect set, i.e.
    // exactly what `derive` computes.
    public IReadOnlyList<DerivedEffect> HazardEffects => _hazardEffects.Value;

    // Per-artifact FIRST-ACCESS build cost for this generation, in construction-independent access order.
    // The program's headline latency ("edit → facts servable in ~0.75s") measures FACTS; a QUERY additionally
    // needs this derived layer, and nothing measured what that costs until it was instrumented here. Surfaced
    // on the live answer itself (WatchHost.AnswerQueryAsync) rather than via Console, which TUnit swallows.
    public IReadOnlyList<(string Artifact, TimeSpan Elapsed)> BuildTimes
    {
        get
        {
            lock (_buildTimeLock)
            {
                return _buildTimes.ToArray();
            }
        }
    }

    // "traversalGraph 412.3ms | epData 8.1ms | effects 263.0ms" — the one-line rendering of BuildTimes.
    // MILLISECONDS, not seconds: on a playground-scale tree the whole derived layer lands in single-digit
    // milliseconds, and a seconds-with-3-decimals rendering reported every artifact as "0.000s" — an
    // instrument whose resolution hides the thing it measures is not an instrument.
    // Empty when nothing was built this generation (every artifact already memoized).
    public string BuildTimeLine() =>
        string.Join(" | ", BuildTimes.Select(t => $"{t.Artifact} {t.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}ms"));

    // Force the artifacts a QUERY needs, so the first query of a generation does not pay for them. Called by
    // WatchHost on a background task right after an eager apply; see the note there for the scheduling rules.
    //
    // WHICH artifacts, and why not the others: reaches/path/callers touch exactly TraversalGraph, Invocations,
    // EpData, ThrowRefs (all four via ReachInputs) and Effects — measured on MedDBase at
    // `traversalGraph 2198ms | invocations 242ms | epData 371ms | throwRefs 147ms | effects 1038ms` ≈ 4.0s.
    // ShapedGraph and HazardEffects are deliberately NOT warmed: they are `derive`-shaped (ShapedGraph adds the
    // delivery edges a traversal must NOT walk, HazardEffects is the whole-store hazard pass) and no live query
    // path reads either today — warming them would burn tens of seconds per edit for nothing.
    //
    // Forced ONE AT A TIME with a cancellation check between, because a Lazy factory cannot be interrupted
    // once entered: the granularity of "stop warming" is therefore one artifact, and the ORDER below is the
    // access order the query path itself uses, so BuildTimes still reads as disjoint per-artifact rows.
    // Everything here is idempotent and side-effect-free — a cancelled warm leaves whatever it finished
    // memoized and correct, and a query that arrives mid-warm either finds the artifact already built or
    // joins the in-flight build (Lazy's default ExecutionAndPublication mode) rather than duplicating it.
    public void WarmQueryArtifacts(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = TraversalGraph;
        cancellationToken.ThrowIfCancellationRequested();
        _ = Invocations;
        cancellationToken.ThrowIfCancellationRequested();
        _ = EpData;
        cancellationToken.ThrowIfCancellationRequested();
        _ = ThrowRefs;
        cancellationToken.ThrowIfCancellationRequested();
        _ = Effects;
        cancellationToken.ThrowIfCancellationRequested();
        _ = EventSubscriptionSites;
    }

    // The traversal-graph projection, factored out so an uncached caller (a `--raw`-style rule variant that
    // this generation's memo does not cover) can build one without disturbing the memo. Mirrors
    // LoadEffectReachInputsAsync's shaping pass — LoadFactGraphAsync's twin (FactGraphProjection.FromAnalysis)
    // followed by the SINGLE FactPathFinder.ShapeGraph call, with the same monomorphization signatures.
    public static FactGraphData TraversalGraphOf(AnalysisResult facts, RuleSet rules) =>
        FactPathFinder.ShapeGraph(
            graph: FactGraphProjection.FromAnalysis(facts, handoffRules: rules.Handoff, redirectRules: rules.Redirect),
            factoryRules: rules.Factory,
            cutRules: rules.Cut,
            contextRules: rules.Context,
            monomorphizeSignatures: LiveReads.MonomorphizationSignatures(facts)
        );

    // A Lazy whose factory ALSO records its wall time under `artifact`. Recorded inside the factory, so it is
    // the FIRST-access cost and is recorded exactly once (Lazy's default ExecutionAndPublication mode).
    // Caveat on reading the rows: a composite's row INCLUDES any dependency it happens to force. On the
    // `reaches` path the rows are disjoint in practice (the command loads the reach inputs first, forcing
    // traversalGraph/invocations/epData/throwRefs, and only then derives effects), but a caller that touches
    // `effects` first would see one fat row instead of five — read them in order, not as guaranteed slices.
    private Lazy<T> Memo<T>(string artifact, Func<T> build) =>
        new Lazy<T>(() =>
        {
            var watch = Stopwatch.StartNew();
            var value = build();
            watch.Stop();
            lock (_buildTimeLock)
            {
                _buildTimes.Add((artifact, watch.Elapsed));
            }

            return value;
        });

    // The in-memory equivalent of EffectDerivation.DeriveHazardEffectsAsync: the SAME EffectDerivation.
    // DeriveEffects call with the SAME arguments, each feed sourced from LiveReads instead of Reads. Kept
    // argument-for-argument aligned with that method — including `deriveHazards: true` and the `async`-modifier
    // filter that feeds sync_over_async — so the live effect set cannot drift from the store one. The `gate`
    // stays at its default (the FR-1 read-arm write-pairing gate ON), matching `derive`'s default; a
    // `--no-gate` live path would thread it through here.
    private static IReadOnlyList<DerivedEffect> DeriveHazardEffects(
        AnalysisResult facts,
        RuleSet rules,
        FactEntryPointDeriver.FactEntryPointData epData
    )
    {
        var (staticFieldWriteRefs, staticFieldReadRefs) = LiveReads.StaticFieldAccessRefsByKind(facts);
        var asyncMethodIds = LiveReads
            .DeadCodeMethods(facts)
            .Where(m => m.Modifiers.Split(' ').Contains("async"))
            .Select(m => m.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
        return EffectDerivation.DeriveEffects(
            effectRules: rules.Effects,
            observationRules: rules.Observations,
            invocations: LiveReads.InvocationRefs(facts),
            baseEdges: epData.BaseEdges,
            ctorRefs: epData.CtorRefs,
            throwRefs: LiveReads.ThrowRefs(facts),
            staticFieldWriteRefs: staticFieldWriteRefs,
            staticFieldReadRefs: staticFieldReadRefs,
            deriveHazards: true,
            threadStaticCells: LiveReads.ThreadStaticFieldIds(facts),
            volatileCells: LiveReads.VolatileFieldIds(facts),
            asyncMethodIds: asyncMethodIds,
            // AllocationFacts needs no LiveReads twin: Reads.LoadAllocationFactsAsync (whole-store) applies no
            // filter and no dedup, so the extracted list already IS its return value.
            allocationFacts: facts.AllocationFacts ?? []
        );
    }
}
