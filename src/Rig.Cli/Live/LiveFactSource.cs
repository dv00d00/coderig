using System.Diagnostics;
using System.Globalization;
using Rig.Analysis.Inventory;
using Rig.Cli.Effects;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;

namespace Rig.Cli.Live;

// The query-ready bundle over ONE generation of live facts — the in-memory counterpart of what a query
// command loads out of a .rig store before it can answer anything. Given the segmented fact view a resident index
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
    // The first Rider surface has one active semantic family (SQL). Keep one transition slot for a selector
    // change, but do not retain eight full reverse closures before their real-store footprint is measured.
    private const int FileEffectSelectorCapacity = 2;

    private readonly object _buildTimeLock = new();
    private readonly List<(string Artifact, TimeSpan Elapsed)> _buildTimes = [];
    private readonly object _fileEffectLock = new();
    private readonly Dictionary<string, Lazy<FileEffectReadModelIndex>> _fileEffectIndexes = new(StringComparer.Ordinal);
    private readonly Queue<string> _fileEffectOrder = new();

    private readonly Lazy<FactGraphData> _shapedGraph;
    private readonly Lazy<FactGraphData> _traversalGraph;
    private readonly Lazy<FactEntryPointDeriver.FactEntryPointData> _epData;
    private readonly Lazy<IReadOnlyList<FactInvocation>> _invocations;
    private readonly Lazy<IReadOnlyList<SymbolRef>> _throwRefs;
    private readonly Lazy<IReadOnlyList<AllocationFact>> _allocationFacts;
    private readonly Lazy<ISet<EventSubscriptionSite>> _eventSubscriptionSites;
    private readonly Lazy<IReadOnlyList<DerivedEffect>> _effects;
    private readonly Lazy<IReadOnlyList<DerivedEffect>> _hazardEffects;
    private readonly Lazy<Task<IReadOnlyList<Commands.DeriveCommand.HazardFinding>>> _graphHazardFindings;

    public LiveFactSource(IFactSnapshotView facts, RuleSet rules)
    {
        Facts = facts;
        Rules = rules;
        _shapedGraph = Memo("shapedGraph", () => LiveReads.ShapedGraph(facts, rules));
        _traversalGraph = Memo("traversalGraph", () => TraversalGraphOf(facts, rules));
        _epData = Memo("epData", () => LiveReads.FactEntryPointData(facts));
        _invocations = Memo("invocations", () => LiveReads.InvocationRefs(facts));
        _throwRefs = Memo("throwRefs", () => LiveReads.ThrowRefs(facts));
        // The store loader returns a list and downstream derivation consumes it more than once. Keep one
        // undeduplicated materialization per generation without adding a new rendered cost label.
        _allocationFacts = new Lazy<IReadOnlyList<AllocationFact>>(() => facts.EnumerateAllocationFacts().ToArray());
        _eventSubscriptionSites = Memo("eventSites", () => LiveReads.EventSubscriptionSites(facts));
        _effects = Memo("effects", () => QueryEffectDerivation.ForReach(rules, ReachInputs, TraversalGraph));
        _hazardEffects = Memo("hazardEffects", () => DeriveHazardEffects(facts, rules, EpData, AllocationFacts, gate: true));
        _graphHazardFindings = MemoAsync(
            "graphHazardFindings",
            () =>
                EffectDerivation.GraphHazardFindingsAsync(
                    rules: rules,
                    // The `derive`-shaped graph, NOT the traversal graph: the graph-tier hazards are exactly the
                    // ones that read delivery edges (event_cycle's cycles close through a publish->consumer hop),
                    // which is what ShapedGraph adds and a traversal must not walk.
                    shapedGraph: ShapedGraph,
                    unfilteredEffects: HazardEffects,
                    staticFieldIds: () => Task.FromResult(LiveReads.StaticFieldIds(facts))
                )
        );
    }

    // The fact generation this source serves. Immutable — a rebuild produces a new LiveFactSource.
    public IFactSnapshotView Facts { get; }

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
            AllocationFacts: AllocationFacts,
            EpData: EpData
        );

    // The NON-hazard effect set `reaches`/`tree` derive: EffectDerivation.DeriveEffects over the reach inputs,
    // argument-for-argument the call ReachesCommand makes on the store path (no static-field feeds, no hazard
    // post-pass — those are `derive`'s whole-store arms and HazardEffects covers them). Memoized because it is
    // the single most expensive derived artifact per generation and every query in that generation wants the
    // same one.
    public IReadOnlyList<DerivedEffect> Effects => _effects.Value;

    // Rider's semantic file read model, memoized per fact GENERATION and per normalized selector. Unlike tree
    // artifacts this has its own deliberately small bound: arbitrary requests must not make a resident
    // generation's memory grow without limit. Equivalent predicate sets share one Lazy even when reordered.
    internal FileEffectReadModelIndex FileEffects(FileEffectSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var key = NormalizeFileEffectSelector(selector);
        Lazy<FileEffectReadModelIndex> index;
        lock (_fileEffectLock)
        {
            if (!_fileEffectIndexes.TryGetValue(key, out index!))
            {
                index = Memo(
                    $"fileEffects[{selector.Family}]",
                    () =>
                        FileEffectReadModelIndex.Build(
                            TraversalGraph,
                            Facts.EnumerateSymbols(),
                            Effects,
                            selector,
                            indexedFilePaths: Facts.EnumerateSourceFiles().Select(file => file.FilePath)
                        )
                );
                _fileEffectIndexes.Add(key, index);
                _fileEffectOrder.Enqueue(key);
                while (_fileEffectOrder.Count > FileEffectSelectorCapacity && _fileEffectOrder.TryDequeue(out var evicted))
                {
                    _fileEffectIndexes.Remove(evicted);
                }
            }
        }

        return index.Value;
    }

    // Mirrors EffectDerivation.DeriveHazardEffectsAsync — the whole-store hazard-augmented effect set, i.e.
    // exactly what `derive` computes. The FR-1 write-pairing gate is ON, matching `derive`'s (and
    // `tree --view hazards`') default; HazardEffectsFor is the gate-parameterized entry.
    public IReadOnlyList<DerivedEffect> HazardEffects => _hazardEffects.Value;

    // The gate-aware entry point for the hazard-augmented effect set. The DEFAULT (gate on) is the memo above —
    // one derivation per generation, shared by every hazard query. `--no-gate` is a DIFFERENT effect set, so it
    // derives fresh and uncached rather than being served the gated memo: the store path keeps the two in
    // separate cache slots for exactly this reason, and silently returning gated counts for an ungated question
    // is the failure a fact tool cannot have. Unreachable from today's live surface (no --no-gate there), and
    // implemented honestly anyway — a member that lies when a flag is added is worse than one that is slow.
    public IReadOnlyList<DerivedEffect> HazardEffectsFor(bool gate) =>
        gate ? HazardEffects : DeriveHazardEffects(Facts, Rules, EpData, AllocationFacts, gate: false);

    // Mirrors EffectDerivation.DeriveGraphHazardFindingsAsync — the whole-store GRAPH-TIER findings
    // (event_cycle / cache_coherence / static_init_capture), through the SAME
    // EffectDerivation.GraphHazardFindingsAsync classification the store path runs, with the shaped graph, the
    // unfiltered hazard effect set and the static-field universe supplied from memory. Memoized per generation
    // like everything else here; `derive`'s own opt-in rule gates (cacheCoherence / staticInitCapture) still
    // decide which arms fire, so a repo without those sections pays only the event-cycle pass.
    public Task<IReadOnlyList<Commands.DeriveCommand.HazardFinding>> GraphHazardFindingsAsync() => _graphHazardFindings.Value;

    // The per-GENERATION artifact memo the live query-cache arm writes into (LiveQueryArtifactCache). It lives
    // here, not on LiveQueryFactSource, because that adapter is constructed PER QUERY — a memo on it would be
    // discarded before the second question, which is the entire point of memoizing a tree. Bounded (see
    // BoundedArtifactMemo): tree artifacts are O(queries), unlike every other artifact here.
    public BoundedArtifactMemo ArtifactMemo { get; } = new BoundedArtifactMemo();

    // Per-artifact FIRST-ACCESS build cost for this generation, in construction-independent access order.
    // The program's headline latency ("edit → facts servable in ~0.75s") measures FACTS; a QUERY additionally
    // needs this derived layer, and nothing measured what that costs until it was instrumented here. Surfaced
    // on the live answer itself (WatchHost.AnswerQueryAsync) rather than via Console, which TUnit swallows.
    //
    // MERGED IN (2026-08-24): the SNAPSHOT's projected-call-graph build. That graph is this generation's
    // traversal graph — the same artifact `_traversalGraph` memoizes — but the exact-query PLANNER now
    // materializes it on the snapshot before the query runs, so the memo is never forced and its row went
    // missing. On a large solution that row was the biggest number in the line (~12s), and dropping it turned
    // the "derived layer built this generation" note into a disclosure that omits what the user actually
    // waited for. The snapshot carries `(artifact, elapsed)`; this getter folds it back in.
    //
    // FIRST, not appended: the graph is the first thing any traversal needs and, on the routed path, it was
    // built before this LiveFactSource even existed — so leading with it keeps the line in access order.
    //
    // EXACTLY ONE traversalGraph row, ever. If the memo ran (the un-routed path, or a flattened-fixture
    // source with no FactSnapshot behind it) its row is already here, and the snapshot's build STRICTLY
    // CONTAINS that memo's — LiveQueryFactSource.BuildMaterializedGraph calls TraversalGraph and then adds
    // delivery edges — so merging both would report the same milliseconds twice. The memo's row wins because
    // it is already in true access order; the delivery-edge remainder it understates is small next to the
    // shaping pass, and an understated row beats a double-counted one in a disclosure.
    public IReadOnlyList<(string Artifact, TimeSpan Elapsed)> BuildTimes
    {
        get
        {
            // Read the snapshot BEFORE taking _buildTimeLock: FactSnapshot takes its own gate, and no lock
            // order between the two is worth establishing for a diagnostic read.
            var projected = Facts is FactSnapshot snapshot ? snapshot.ProjectedCallGraphBuild : null;
            lock (_buildTimeLock)
            {
                if (projected is not { } graphBuild || _buildTimes.Any(t => t.Artifact == FactSnapshot.ProjectedCallGraphArtifact))
                {
                    return _buildTimes.ToArray();
                }

                return [graphBuild, .. _buildTimes];
            }
        }
    }

    // "traversalGraph 412.3ms | epData 8.1ms | effects 263.0ms" — the one-line rendering of BuildTimes.
    // MILLISECONDS, not seconds: on a playground-scale tree the whole derived layer lands in single-digit
    // milliseconds, and a seconds-with-3-decimals rendering reported every artifact as "0.000s" — an
    // instrument whose resolution hides the thing it measures is not an instrument.
    // Empty when nothing was built this generation (every artifact already memoized).
    public string BuildTimeLine() =>
        string.Join(
            " | ",
            BuildTimes.Select(t => $"{t.Artifact} {t.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}ms")
        );

    // Force the artifacts a QUERY needs, so the first query of a generation does not pay for them. Called by
    // WatchHost on a background task right after an eager apply; see the note there for the scheduling rules.
    //
    // WHICH artifacts, and why not the others: reaches/path/callers/tree touch exactly TraversalGraph,
    // Invocations, EpData, ThrowRefs (all four via ReachInputs) and Effects — measured on MedDBase at
    // `traversalGraph 2198ms | invocations 242ms | epData 371ms | throwRefs 147ms | effects 1038ms` ≈ 4.0s
    // (re-measured 2026-08-21 on the -2 clone: 3258/432/550/343/1424ms + eventSites 213ms ≈ 6.2s).
    //
    // ShapedGraph, HazardEffects and GraphHazardFindings are deliberately still NOT warmed, and as of the
    // `tree` slice that is a MEASURED call rather than an assumption — `tree --view hazards` reads all three,
    // so "no live query path touches them" stopped being the reason. On MedDBase, in memory:
    //
    //     hazardEffects        3.4s  (362,368 effects)   <- the store path's ~18s SQL-cold artifact
    //     shapedGraph          3.9s
    //     graphHazardFindings  5.0s  (incl. the shapedGraph it forces; ~1.1s of classification on top)
    //
    // i.e. ~8.4s of marginal cost, which would MORE THAN DOUBLE the warm window (6.2s -> ~14.6s) to serve an
    // arm most queries never ask for. Warming is bounded by what the worker's next apply must not wait behind,
    // and a Lazy factory cannot be interrupted once entered — so warming these would extend the
    // uninterruptible window by up to 8.4s per edit for the benefit of the occasional hazards query. Left LAZY,
    // with the cost DISCLOSED on the answer that pays it: the "derived layer built this generation" line names
    // each artifact and its milliseconds, so the one query that pays 8.4s is told it did, and every later
    // hazards query in the generation pays nothing. Revisit if a hazards view becomes a default surface.
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
    public static FactGraphData TraversalGraphOf(IFactSnapshotView facts, RuleSet rules) =>
        FactPathFinder.ShapeGraph(
            graph: FactGraphProjection.FromView(
                facts,
                handoffRules: rules.Handoff,
                redirectRules: rules.Redirect,
                externalNodes: ExternalNodeAdmission.FromRules(rules)
            ),
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

    // The async twin of Memo, for an artifact whose derivation is shared with an ASYNC store-side function
    // (the graph-tier hazard findings: its store arm awaits a SQL read for the static-field universe, so the
    // one shared classification is a Task-returning method on both paths).
    //
    // Lazy<Task<T>> is the correct shape here, not Lazy<T> over a blocking wait: the FACTORY runs once
    // (ExecutionAndPublication) and produces the ONE Task every caller awaits, so the derivation happens once
    // per generation without a thread ever blocking on it. `.GetAwaiter().GetResult()` inside a Lazy<T> would
    // memoize the same value and be sync-over-async — the hazard this tool ships a detector for.
    private Lazy<Task<T>> MemoAsync<T>(string artifact, Func<Task<T>> build) => new Lazy<Task<T>>(() => TimedAsync(artifact, build));

    private static string NormalizeFileEffectSelector(FileEffectSelector selector)
    {
        var predicates = selector
            .Predicates.Distinct()
            .OrderBy(predicate => predicate.Provider, StringComparer.Ordinal)
            .ThenBy(predicate => predicate.Operation, StringComparer.Ordinal)
            .Select(predicate =>
                $"{predicate.Provider.Length}:{predicate.Provider}{predicate.Operation?.Length ?? -1}:{predicate.Operation}"
            );
        return $"{selector.Family.Length}:{selector.Family}|{string.Join('|', predicates)}";
    }

    // Records the artifact's wall time around the AWAIT, not around task creation — timing the factory call
    // alone would report ~0ms for the whole derivation and be an instrument that hides what it measures.
    private async Task<T> TimedAsync<T>(string artifact, Func<Task<T>> build)
    {
        var watch = Stopwatch.StartNew();
        var value = await build();
        watch.Stop();
        lock (_buildTimeLock)
        {
            _buildTimes.Add((artifact, watch.Elapsed));
        }

        return value;
    }

    // The in-memory equivalent of EffectDerivation.DeriveHazardEffectsAsync: the SAME EffectDerivation.
    // DeriveEffects call with the SAME arguments, each feed sourced from LiveReads instead of Reads. Kept
    // argument-for-argument aligned with that method — including `deriveHazards: true` and the `async`-modifier
    // filter that feeds sync_over_async — so the live effect set cannot drift from the store one. `gate` is the
    // FR-1 read-arm write-pairing gate, threaded through exactly as the store path threads `--no-gate`; the
    // memoized artifact is the gated (default) one and HazardEffectsFor is what routes the other.
    private static IReadOnlyList<DerivedEffect> DeriveHazardEffects(
        IFactSnapshotView facts,
        RuleSet rules,
        FactEntryPointDeriver.FactEntryPointData epData,
        IReadOnlyList<AllocationFact> allocationFacts,
        bool gate
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
            gate: gate,
            // AllocationFacts needs no LiveReads twin: Reads.LoadAllocationFactsAsync (whole-store) applies no
            // filter and no dedup, so the extracted list already IS its return value.
            allocationFacts: allocationFacts,
            dualWriteSystemClassMap: rules.DualWrite?.SystemClassMap
        );
    }

    private IReadOnlyList<AllocationFact> AllocationFacts => _allocationFacts.Value;
}
