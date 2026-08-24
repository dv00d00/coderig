using System.Collections.Immutable;
using System.Globalization;
using Rig.Analysis.Inventory;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Cli.Deployments;
using Rig.Cli.EntryPoints;
using Rig.Cli.Rendering;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;

namespace Rig.Cli.Live;

// IQueryFactSource over the RESIDENT in-memory facts — the point of the whole live-background-index program.
// A query answered through this instance touches no SQLite: the graph, the effect inputs and the entry-point
// facts are projected off the immutable segmented view `rig watch` keeps current, through LiveReads (the
// parity-gated twin of the store-side `Reads`).
//
// It is a THIN adapter over one LiveFactSource generation and owns nothing: DisposeAsync is a no-op, because
// the facts belong to the host and outlive any single query. A new fact generation means a new LiveFactSource
// and hence a new adapter — never a mutation of this one.
//
// Two honest asymmetries vs the store path, both disclosed rather than papered over:
//
//  1. MOST INPUTS ARE NOT SQL-BOUNDED. The store path narrows the effect-derivation inputs to the pattern's
//     SQL reach closure (SqlReachability); live reaches/tree still use the whole resident graph and let
//     traversal narrow that sound superset. Forward path and reverse callers are the exceptions: their
//     optional demand capabilities walk keyed partitions to a fixed point and never force TraversalGraph.
//  2. NO QUERY CACHE. The store path memoizes the (expensive, pattern-independent) EP-site map in
//     .rig/cache.db keyed by store identity; live facts change per edit, so a disk cache keyed on them would
//     be a liability. The equivalent here is per-GENERATION memoization: derive once, reuse for every query
//     against the same facts, discard when the facts move.
internal sealed class LiveQueryFactSource(LiveFactSource live)
    : IQueryFactSource,
        IDemandForwardPathFactSource,
        IDemandReverseCallersFactSource
{
    private readonly object _gate = new();

    private IReadOnlyDictionary<(string File, int Line), (string Kind, IReadOnlyList<string>? Requires)>? _epSiteKind;
    private (
        IReadOnlyList<DerivedEntryPoint> Derived,
        IReadOnlyList<HandoffEntryPoint> ClassifiedHandoffs,
        IReadOnlyList<DerivedEntryPoint> PromotedOrigins
    )? _entryPoints;

    public LiveFactSource Source { get; } = live;

    public Task<SqlReachability.ReachInputs> LoadEffectReachInputsAsync(
        string pattern,
        SqlReachability.Direction direction,
        RuleSet shapedRules
    )
    {
        // `pattern`/`direction` have no live analogue (see asymmetry 1 above) and are intentionally unused.
        // `shapedRules` is NOT ignored — see SameShapingAsMemo for why its three gated slice sizes are the
        // discriminator and what would force that check to widen.
        return Task.FromResult(
            SameShapingAsMemo(shapedRules)
                ? Source.ReachInputs
                : Source.ReachInputs with
                {
                    Graph = LiveFactSource.TraversalGraphOf(Source.Facts, shapedRules),
                }
        );
    }

    // `callers`' graph-only load, plus the legacy fallback for a flattened live fixture. Same memo, same shaping discriminator — the only difference from
    // LoadEffectReachInputsAsync is that nothing but the graph is wanted, so nothing but the graph is touched
    // (on the live path that also means the effect derivation is never forced by a `path`/`callers` query).
    //
    // `direction` is intentionally unused here. Indexed forward path and reverse callers use their keyed
    // demand capabilities instead; this remains the store-parity seam and flattened-fixture fallback.
    public Task<FactGraphData> LoadShapedTraversalGraphAsync(string pattern, SqlReachability.Direction direction, RuleSet shapedRules) =>
        Task.FromResult(
            SameShapingAsMemo(shapedRules) ? Source.TraversalGraph : LiveFactSource.TraversalGraphOf(Source.Facts, shapedRules)
        );

    public Task<DemandForwardGraphResult> LoadDemandForwardPathGraphAsync(
        string fromPattern,
        RuleSet shapedRules,
        int maxDepth,
        FactPathFinder.TraversalMode mode,
        bool classifyEventSubscriptions,
        int? maxNodes = null,
        int? maxGenericWork = null
    )
    {
        if (Source.Facts is not IIndexedFactSnapshotView)
        {
            if (mode != FactPathFinder.TraversalMode.SyncCut)
            {
                throw new DemandForwardGraphUnavailableException(
                    "async live traversal requires the resident keyed graph; flattened compatibility facts cannot project delivery exactly"
                );
            }
            // Compatibility for callers/tests that construct LiveFactSource from a flattened AnalysisResult.
            // WatchHost never takes this arm: its resident FactSnapshot implements IIndexedFactSnapshotView.
            // The fallback is deliberately explicit in diagnostics and is not used for demand caps, budget
            // exhaustion, or any exception from the keyed builder.
            var graph = SameShapingAsMemo(shapedRules) ? Source.TraversalGraph : LiveFactSource.TraversalGraphOf(Source.Facts, shapedRules);
            return Task.FromResult(
                new DemandForwardGraphResult(
                    Graph: graph,
                    Diagnostics: DemandForwardGraphDiagnostics.LegacyFallback(),
                    EventSubscriptionsClassified: false
                )
            );
        }

        // MATERIALIZED, not projected per query — see MaterializedGraph. `fromPattern`, `maxDepth`, `mode`,
        // `classifyEventSubscriptions`, `maxNodes` and `maxGenericWork` were the keyed builder's per-query
        // PROJECTION inputs and have no analogue here: a whole graph has no seed to expand from, no closure
        // to bound and no node budget to blow, and the traversal that follows applies depth and mode itself.
        // EventSubscriptionsClassified is FALSE so the command applies MarkEventSubscriptionHandoffs over the
        // generation's memoized event-site set — the same order the store path uses, and the order
        // AddDeliveryEdges requires (it must see the unreclassified `+=` methodGroup edges).
        return Task.FromResult(
            new DemandForwardGraphResult(
                Graph: MaterializedGraph(shapedRules),
                Diagnostics: DemandForwardGraphDiagnostics.Materialized(),
                EventSubscriptionsClassified: false
            )
        );
    }

    public async Task<DemandForwardReachInputs> LoadDemandForwardReachInputsAsync(
        string fromPattern,
        RuleSet shapedRules,
        int maxDepth,
        FactPathFinder.TraversalMode mode,
        bool classifyEventSubscriptions,
        int? maxNodes = null,
        int? maxGenericWork = null
    )
    {
        var demand = await LoadDemandForwardPathGraphAsync(
            fromPattern,
            shapedRules,
            maxDepth,
            mode,
            classifyEventSubscriptions,
            maxNodes,
            maxGenericWork
        );
        if (demand.Diagnostics.Load.UsedLegacyFallback && mode != FactPathFinder.TraversalMode.SyncCut)
        {
            throw new DemandForwardGraphUnavailableException(
                "async live traversal cannot use the flattened whole-graph compatibility fallback"
            );
        }

        var epData = Source.EpData;
        return new DemandForwardReachInputs(
            demand,
            new SqlReachability.ReachInputs(
                Graph: demand.Graph,
                Invocations: Source.Invocations,
                CtorRefs: epData.CtorRefs,
                ThrowRefs: Source.ThrowRefs,
                AllocationFacts: Source.Facts.EnumerateAllocationFacts().ToArray(),
                EpData: epData
            )
        );
    }

    public Task<DemandReverseCallersGraphResult> LoadDemandReverseCallersGraphAsync(
        DemandForwardGraphRules rules,
        DemandReverseCallersGraphRequest request
    )
    {
        var shapedRules = ShapedRulesFor(rules);

        if (Source.Facts is IIndexedFactSnapshotView)
        {
            // MATERIALIZED, not projected per query. The keyed reverse builder ran a fixed point that
            // re-derived the whole graph index once per pass, so its cost was O(passes x graph) and on a
            // 227-project monorepo (300,961 nodes / 630,932 call edges) it did not terminate in usable time.
            // The whole graph fits in memory and the store answers the same question off exactly such a graph
            // in seconds, so materialize it ONCE per generation and let the shared FactPathFinder narrow it —
            // the store's own model, and the one this adapter's LoadShapedTraversalGraphAsync already serves.
            //
            // request.MaxNodes is deliberately NOT applied. It is a DEMAND BUDGET — how much graph a keyed
            // projection may expand before it must fail closed rather than serve a partial answer — and there
            // is nothing partial here: the whole graph IS the answer. Enforcing it would decline every query
            // on any solution larger than the budget, which is precisely the solutions this exists for.
            var materialized = MaterializedGraph(shapedRules);
            return Task.FromResult(
                new DemandReverseCallersGraphResult(
                    materialized,
                    DemandReverseCallersGraphDiagnostics.Materialized(),
                    MatchedTargetIds(materialized, request.ToPattern),
                    // Ownership hints are the keyed projection's account of WHICH fact partitions it read, so
                    // an exact-refinement planner can bound its debt to them. A whole-graph answer read
                    // everything and can honestly claim no narrower boundary; the command itself never reads
                    // this field (only ExactCallersRefinement does, off its own direct builder call).
                    new DemandReverseOwnershipHints([], []),
                    EventSubscriptionsClassified: false
                )
            );
        }

        // Compatibility only for tests/callers that supplied a flattened AnalysisResult. WatchHost publishes
        // an indexed FactSnapshot and never takes this arm. A genuinely async execution needs delivery edges,
        // which this flattened traversal graph does not contain; fail closed rather than serving a plausible
        // sync answer. Sync human --entrypoints may still use AsyncExact as its DISCOVERY lens, so the request
        // carries execution separately and retains this compatibility arm.
        if (request.EffectiveExecutionMode != FactPathFinder.TraversalMode.SyncCut)
        {
            throw new DemandReverseCallersGraphUnavailableException(
                "async live callers requires the resident keyed graph; flattened compatibility facts cannot project delivery exactly"
            );
        }

        var graph = SameShapingAsMemo(shapedRules) ? Source.TraversalGraph : LiveFactSource.TraversalGraphOf(Source.Facts, shapedRules);
        return Task.FromResult(
            new DemandReverseCallersGraphResult(
                graph,
                DemandReverseCallersGraphDiagnostics.LegacyFallback(),
                MatchedTargetIds(graph, request.ToPattern),
                new DemandReverseOwnershipHints([], []),
                EventSubscriptionsClassified: false
            )
        );
    }

    // The RuleSet a DemandForwardGraphRules describes, resolved against the generation's own rules for the
    // slices the demand record does not carry. Lifted out of the flattened arm unchanged so both arms shape
    // (and therefore cache-key) identically.
    private RuleSet ShapedRulesFor(DemandForwardGraphRules rules) =>
        Source.Rules with
        {
            Handoff = rules.Projection.Handoff ?? [],
            Redirect = rules.Projection.Redirect ?? [],
            Factory = rules.Projection.Factory ?? [],
            Cut = rules.Cut,
            Context = rules.Context,
            Delivery = rules.Delivery ?? Source.Rules.Delivery,
        };

    // The depth-0 entries of the reverse closure — the nodes the target pattern actually names. Through
    // FactPathFinder.MatchedNodes, which is the SAME matcher over the same node universe the traversal seeds
    // with; the previous `ReachedBy(maxDepth: 0).Where(v == 0)` returned exactly this set but also built the
    // reverse maps (a whole-graph dispatch scan) to answer a question that reads no reverse edge.
    private static ImmutableArray<string> MatchedTargetIds(FactGraphData graph, string toPattern) =>
        [.. FactPathFinder.MatchedNodes(graph, toPattern).OrderBy(id => id, StringComparer.Ordinal)];

    // THE MATERIALIZED WHOLE GRAPH for this fact generation: the traversal-shaped projection plus its
    // delivery edges, built once and cached on the SNAPSHOT (FactSnapshot.ProjectedCallGraph), so every
    // later query in the generation pays nothing.
    //
    // ONE GRAPH, NOT ONE PER MODE. Delivery edges are folded in unconditionally, exactly as the store folds
    // them into `call_edges` when `rig graph` materializes: they carry Kind=handoff plus a DeliveryPrecision,
    // and TRAVERSAL decides whether to cross them (FactPathFinder.CutsHandoff — SyncCut cuts all of them,
    // AsyncExact cuts only the Fanout ones, AsyncInclude keeps all). So `--async` / `--include-delivery` cost
    // no second materialization, and a sync answer is unaffected because every added edge is one it cuts.
    //
    // `--raw` is the one shaping variation the live surface can still ask for (it zeroes Factory/Cut/Context,
    // and those genuinely change the EDGES: factory monomorphization rewrites them, cut/context ride on the
    // graph and drive BuildIndex). It cannot share the shaped graph, so it gets its own cache slot rather
    // than its own per-query build — two slots is the whole reachable space, and FactSnapshot caps it.
    private FactGraphData MaterializedGraph(RuleSet shapedRules) =>
        Source.Facts is FactSnapshot snapshot
            ? snapshot.ProjectedCallGraph(ShapingKey(shapedRules), () => BuildMaterializedGraph(shapedRules))
            : BuildMaterializedGraph(shapedRules);

    // The projection itself. The shaped half reuses the generation's existing traversalGraph memo when the
    // shaping matches (so a materialized query and a plain LoadShapedTraversalGraphAsync share one build);
    // the delivery half is LiveReads.DeliverySites + FactPathFinder.AddDeliveryEdges, the same pair
    // LiveReads.ShapedGraph runs — and, like it, BEFORE any event-subscription reclassification, which
    // AddDeliveryEdges depends on not having happened yet.
    private FactGraphData BuildMaterializedGraph(RuleSet shapedRules)
    {
        var traversal = SameShapingAsMemo(shapedRules) ? Source.TraversalGraph : LiveFactSource.TraversalGraphOf(Source.Facts, shapedRules);
        return FactPathFinder.AddDeliveryEdges(traversal, LiveReads.DeliverySites(Source.Facts, shapedRules.Delivery));
    }

    // The cache key: the shaping slices that change the EDGES, by count — the same discriminator, and the
    // same standing caveat, as SameShapingAsMemo. Widen both together if the live surface ever gains
    // `--rules`.
    private static string ShapingKey(RuleSet rules) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"f{rules.Factory.Count}/c{rules.Cut.Count}/x{rules.Context.Count}/d{rules.Delivery.Count}/h{rules.Handoff.Count}/r{rules.Redirect.Count}"
        );

    // Does `shapedRules` shape the graph the same way the memo was shaped? The memo was built with the rules
    // the FACTS were extracted under, and a caller shaping differently must not be silently served the wrong
    // graph. Today exactly two such divergences are reachable, both `--raw`: `reaches --raw` zeroes Cut/Context,
    // and `path`/`callers --raw` additionally zero Factory. Everything else is equal by construction — the live
    // query surface has no `--rules` flag, and the commands reload rules from the SAME working directory the
    // host booted with, so the two rule sets differ only where a command deliberately gates them. The three
    // gated slice SIZES are therefore a sufficient discriminator TODAY; adding `--rules` (or any other
    // rule-gating flag) to the live surface means widening this check, not trusting it.
    private bool SameShapingAsMemo(RuleSet shapedRules) =>
        shapedRules.Factory.Count == Source.Rules.Factory.Count
        && shapedRules.Cut.Count == Source.Rules.Cut.Count
        && shapedRules.Context.Count == Source.Rules.Context.Count;

    public Task<ISet<EventSubscriptionSite>> EventSubscriptionSitesAsync() => Task.FromResult(Source.EventSubscriptionSites);

    public Task<IReadOnlyList<DerivedEffect>> DeriveEffectsAsync(SqlReachability.ReachInputs inputs, FactGraphData graph, RuleSet rules)
    {
        // The memo is derived over Source.ReachInputs and Source.TraversalGraph. The graph the command hands
        // back has had MarkEventSubscriptionHandoffs applied, which rewrites only CallEdge KINDS and leaves
        // BaseEdges as the SAME list instance — and BaseEdges is the only part of the graph the derivation
        // reads. So reference-identical BaseEdges is a sufficient (and cheap) proof the memo answers this
        // call; anything else (a `--raw` reshape, a future caller passing a different graph) derives fresh
        // rather than silently returning the wrong set.
        var memoApplies =
            ReferenceEquals(inputs.Invocations, Source.Invocations) && ReferenceEquals(graph.BaseEdges, Source.TraversalGraph.BaseEdges);
        return Task.FromResult(memoApplies ? Source.Effects : QueryEffectDerivation.ForReach(rules, inputs, graph));
    }

    // The live source KNOWS its solution (AnalysisResult.SolutionPath), so unlike the store path it needs no
    // ListRunsAsync probe to pick the primary/max-symbol run — there is exactly one solution in the resident
    // index, and it is the one being watched. Everything after that resolution is the same DeploymentMap.LoadAsync.
    public Task<DeploymentMap> LoadDeploymentsAsync(string workingDirectory) =>
        !File.Exists(Path.Combine(workingDirectory, "deployments.json"))
            ? Task.FromResult(DeploymentMap.Empty)
            : DeploymentMap.LoadAsync(workingDirectory: workingDirectory, solutionPath: Source.Facts.SolutionPath);

    public Task<EpRenderContext?> BuildEpContextAsync(
        FactGraphData graph,
        string workingDirectory,
        IReadOnlyList<string> extraRules,
        RuleSet rules,
        DeploymentMap deployments,
        FactEntryPointDeriver.FactEntryPointData? epData
    )
    {
        // Same short-circuit as EntryPointContext.BuildEpContextAsync: unconfigured deployments cost nothing.
        if (deployments.IsEmpty)
        {
            return Task.FromResult<EpRenderContext?>(null);
        }

        var epSiteKind = EpSiteKind(rules, epData);

        // The cheap half, rebuilt from THIS query's graph exactly as the store path does.
        var siteById = graph
            .Methods.GroupBy(m => m.SymbolId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().FilePath, g.First().Line), StringComparer.Ordinal);

        return Task.FromResult<EpRenderContext?>(new EpRenderContext(deployments, siteById, epSiteKind));
    }

    // The in-memory twin of SeedResolutionNotice.ReportNoNodeMatchAsync's store probe. Mirrors
    // Reads.SearchSymbolsAsync's LIKE arm (case-insensitive substring on Name OR SymbolId, ordered by
    // SymbolId, deduped by SymbolId, capped) — deliberately that arm and not the symbol_fts arm, since FTS is
    // built by `rig graph` and the live index has no materialized graph at all. The MESSAGE is not rebuilt
    // here: both paths call the one SeedResolutionNotice formatter, so the disclosure text cannot drift.
    public Task ReportNoNodeMatchAsync(TextWriter output, string pattern)
    {
        var hits = Source
            .Facts.EnumerateSymbols()
            .Where(s =>
                s.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                || s.SymbolId.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(s => s.SymbolId, StringComparer.Ordinal)
            .GroupBy(s => s.SymbolId, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(s => (s.SymbolId, s.Kind))
            // The store probe passes `limit: MaxListed`, and the "is there a non-node hit at all" decision is
            // made over THAT truncated list — so the cap has to be applied here too, or the live disclosure
            // could name a `P:` the store path would never have looked at.
            .Take(SeedResolutionNotice.MaxListed)
            .ToList();
        SeedResolutionNotice.ReportNoNodeMatch(output, hits, pattern);
        return Task.CompletedTask;
    }

    // The in-memory twin of SeedResolutionNotice.ExistsInStoreAsync. Same conservative rule — ANY indexed
    // symbol of ANY kind counts as "exists", so the no-match claim is only made when the fact set genuinely
    // has nothing by that name — and the same LIKE-arm semantics as ReportNoNodeMatchAsync above (a
    // case-insensitive substring on Name OR SymbolId), for the same reason: symbol_fts is built by `rig graph`
    // and the live index has no materialized graph at all.
    public Task<bool> SymbolExistsAnywhereAsync(string pattern) =>
        Task.FromResult(
            Source
                .Facts.EnumerateSymbols()
                .Any(s =>
                    s.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                    || s.SymbolId.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                )
        );

    public Task<FactEntryPointDeriver.FactEntryPointData> LoadEntryPointDataAsync() => Task.FromResult(Source.EpData);

    public Task<(
        IReadOnlyList<DerivedEntryPoint> Derived,
        IReadOnlyList<HandoffEntryPoint> ClassifiedHandoffs,
        IReadOnlyList<DerivedEntryPoint> PromotedOrigins
    )> DeriveEntryPointsAsync(FactEntryPointDeriver.FactEntryPointData epData, RuleSet rules) =>
        Task.FromResult(EntryPointSets(rules, epData));

    // ---- `tree` only, below. ----

    // The per-GENERATION artifact memo, in place of `.rig/cache.db` (asymmetry 2 above, now load-bearing rather
    // than theoretical: `tree` is the first cached command on this path). The memo lives on the LiveFactSource,
    // not on this adapter — this adapter is built PER QUERY, so a memo here would never survive to serve the
    // second question. `useCache:false` yields a memo-less cache that behaves exactly like the store's disabled
    // one (every Get a miss, every Put dropped).
    public IQueryArtifactCache OpenArtifactCache(bool useCache) => new LiveQueryArtifactCache(useCache ? Source.ArtifactMemo : null);

    // `rulesHash` and `useCache` are the STORE's cache-key inputs and have no live analogue: the generation is
    // the cache, and a rules edit that the command has reloaded cannot silently alias a generation's memo (the
    // hazard artifacts are memoized on the LiveFactSource under the rules the FACTS were extracted with — the
    // same construction, and the same caveat, as SameShapingAsMemo above; widen this when the live surface
    // gains `--rules`). `gate` IS honoured: an ungated set is a different set, so it derives fresh.
    public Task<IReadOnlyList<DerivedEffect>> HazardEffectsAsync(string rulesHash, RuleSet rules, bool useCache, bool gate) =>
        Task.FromResult(Source.HazardEffectsFor(gate));

    public Task<IReadOnlyList<DeriveCommand.HazardFinding>> GraphHazardFindingsAsync(string rulesHash, RuleSet rules, bool useCache) =>
        Source.GraphHazardFindingsAsync();

    // Tier 3 of EntryPointContext.LoadOrDeriveEpSiteKindAsync, in memory — the SAME map BuildEpContextAsync
    // above flattens for its chip, through the same EpSiteKind memo, so the tree's ▶ chip and the `callers
    // --entrypoints` listing cannot disagree. Tiers 1 (the materialized entry_point_sites table) and 2 (the
    // cache.db query cache) do not exist here by construction; `workingDirectory`/`extraRules`/`useCache` are
    // their inputs and are unused for that reason.
    public Task<IReadOnlyDictionary<(string File, int Line), (string Kind, IReadOnlyList<string>? Requires)>> EpSiteKindAsync(
        string workingDirectory,
        IReadOnlyList<string> extraRules,
        RuleSet rules,
        bool useCache,
        FactEntryPointDeriver.FactEntryPointData? epData
    ) => Task.FromResult(EpSiteKind(rules, epData));

    public Task<IReadOnlyList<SymbolRef>> LibraryCallSitesAsync(IReadOnlyCollection<string> enclosingIds) =>
        Task.FromResult(LiveReads.LibraryCallSites(Source.Facts, enclosingIds));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // EntryPointContext.DeriveEntryPointsAsync, in memory: the SAME FactEntryPointDeriver.Derive + handoff
    // classification + PromoteHandoffOrigins, with the graph supplied by FactGraphProjection instead of the
    // store. Memoized because BOTH live consumers (`callers --entrypoints` and the EP-site map below) want the
    // same answer within one query, and the handoff arm re-projects the whole call graph.
    //
    // Mirrors Reads.DeriveHandoffEntryPointsAsync's FALLBACK arm (its fast arm reads the `call_edges` table
    // `rig graph` materialized, which does not exist here): classify the UNSHAPED, handoff-classified graph's
    // edges. Unshaped is the store fallback's own choice (LoadFactGraphAsync, not LoadShapedGraphAsync) —
    // mirrored, not re-decided.
    private (
        IReadOnlyList<DerivedEntryPoint> Derived,
        IReadOnlyList<HandoffEntryPoint> ClassifiedHandoffs,
        IReadOnlyList<DerivedEntryPoint> PromotedOrigins
    ) EntryPointSets(RuleSet rules, FactEntryPointDeriver.FactEntryPointData? epData)
    {
        lock (_gate)
        {
            if (_entryPoints is { } memo)
            {
                return memo;
            }

            var derived = FactEntryPointDeriver.Derive(epData ?? Source.EpData, rules.EntryPoints, rules.ClassInheritance);
            var edges = FactGraphProjection.FromView(Source.Facts, handoffRules: rules.Handoff, redirectRules: rules.Redirect).CallEdges;
            var classifiedHandoffs = HandoffClassifier
                .HandoffEntryPoints(edges, rules.Handoff)
                .Where(h => h.Dispatcher is not null)
                .ToList();
            var promoted = EntryPointContext.PromoteHandoffOrigins(classifiedHandoffs, derived);
            var sets = (
                (IReadOnlyList<DerivedEntryPoint>)derived,
                (IReadOnlyList<HandoffEntryPoint>)classifiedHandoffs,
                (IReadOnlyList<DerivedEntryPoint>)promoted
            );
            _entryPoints = sets;
            return sets;
        }
    }

    // EntryPointContext.DeriveEpSiteKindAsync's tier 3 (the live derive), in memory and memoized for this fact
    // generation. Tier 1 (the entry_point_sites table `rig graph` materializes) and tier 2 (the cache.db query
    // cache) do not exist on the live path by construction — there is no store.
    private IReadOnlyDictionary<(string File, int Line), (string Kind, IReadOnlyList<string>? Requires)> EpSiteKind(
        RuleSet rules,
        FactEntryPointDeriver.FactEntryPointData? epData
    )
    {
        lock (_gate)
        {
            if (_epSiteKind is not null)
            {
                return _epSiteKind;
            }

            // The SAME live EP derivation `callers --entrypoints` gets (EntryPointSets), flattened to the
            // site->kind map EpRenderContext wants — so the chip and the EP listing can never disagree.
            var (derived, _, promoted) = EntryPointSets(rules, epData);

            var map = new Dictionary<(string File, int Line), (string Kind, IReadOnlyList<string>? Requires)>();
            foreach (var e in derived.Concat(promoted))
            {
                map[(e.FilePath, e.Line)] = (e.Kind, e.Requires);
            }

            _epSiteKind = map;
            return map;
        }
    }
}
