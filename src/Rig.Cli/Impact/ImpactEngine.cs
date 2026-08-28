using Rig.Analysis.Rules;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using static Rig.Cli.Caching.QueryCacheKeys;
using static Rig.Cli.Effects.EffectDerivation;
using static Rig.Cli.EntryPoints.EntryPointContext;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Impact;

// The reusable store-vs-store DIFF ENGINE behind `rig impact` — the compute half split out of ImpactCommand
// so the command file is thin wiring + rendering. It adds no graph code: it runs `derive`'s
// DeriveEntryPoints/DeriveEffects on each store and diffs the per-EP forward-reach footprints. Shared by the
// CLI (ImpactCommand.RunAsync) and the web /api/impact endpoint (ImpactQueryService) so the two cannot diverge.
internal static class ImpactEngine
{
    // Resolve BOTH per-commit stores up front (sha / short-sha / store-id → store dir), load the rule set,
    // and open the query cache. ResolveReadStoreDir throws StoreRefNotFoundException for an unmatched ref —
    // CommandGuard lists what's indexed, so past this point both stores exist and are addressable. The HEAD
    // store dir hosts the result cache; the cache KEY folds in BOTH store identities so reindexing either
    // side misses. Render-only flags (--structural/--format/--limit) are absent — they re-present the SAME
    // diff and must not fragment it. F6: LoadedRulePaths are passed to ComputeFromPaths so the fingerprint
    // reuses the already-resolved paths instead of re-running the cascade merge.
    private static (RuleSet Rules, string BaseDbPath, QueryCache? Cache, string? CacheKey) ResolveStoresAndCache(
        WorkspaceLocation ws,
        string baseRef,
        string headRef,
        IReadOnlyList<string> extraRules,
        bool gate,
        bool noCache,
        FactPathFinder.TraversalMode mode,
        bool amplification = true
    )
    {
        var rules = RuleSetLoader.Load(workingDirectory: ws.WorkingDirectory, extraRules: extraRules, loadedPaths: out var loadedRulePaths);

        var headDir = StoreLayout.ResolveReadStoreDir(ws with { StoreRef = headRef });
        var baseDir = StoreLayout.ResolveReadStoreDir(ws with { StoreRef = baseRef });
        var baseDbPath = Path.Combine(baseDir, StoreLayout.DbFileName);
        var headStoreKey = StoreKey(Path.Combine(headDir, StoreLayout.DbFileName));
        var baseStoreKey = StoreKey(baseDbPath);
        var cache = noCache ? null : QueryCache.Open(rigDirectory: headDir, storeKey: headStoreKey);
        // Fold the shared_state:read write-pairing gate state into the rule-fingerprint slot so the gated and
        // ungated (--no-gate) diffs never share a cache entry. BOTH carry an explicit token (not the bare
        // rulesHash) so a diff cached by a PRE-gate binary can never be served as a gated result (a one-time
        // recompute on upgrade; correctness over a warm-cache hit).
        var rulesHash = RulesFingerprint.ComputeFromPaths(loadedRulePaths); // F6: reuse paths Load resolved.
        var keyRulesHash = gate ? $"{rulesHash}|gate" : $"{rulesHash}|nogate";
        // Same reasoning for the amplification tier: it changes the PAYLOAD (extra per-EP rows), so a suppressed
        // and an amplified diff must not share a slot. Only the OFF state is tokenized, so the default (on) key is
        // byte-identical to the pre-tier key shape — one less forced recompute on upgrade. (The tier's rules scope
        // itself already rides in rulesHash: `observations.amplification` lives in builtin-rules.json.)
        keyRulesHash = amplification ? keyRulesHash : $"{keyRulesHash}|noamp";
        var cacheKey = cache is null
            ? null
            : ImpactCacheKey(baseStoreKey: baseStoreKey, headStoreKey: headStoreKey, rulesHash: keyRulesHash, mode: mode);
        return (rules, baseDbPath, cache, cacheKey);
    }

    // The reusable store-vs-store DIFF, shared by `rig impact` (RunAsync) and the web /api/impact endpoint (via
    // ImpactQueryService). Produces the full ImpactCacheArtifact (diff + both provenances + the diff-site FQN
    // map) — everything a renderer needs — with NO rendering/deployment concerns. Warm path returns the cached
    // artifact without loading either graph; cold path loads + derives + caches. The caller owns `headContext`
    // (opened at StoreRef=headRef) so it can also read deployments off it without a second open.
    internal static async Task<ImpactCacheArtifact> DiffAsync(
        RigDbContext headContext,
        WorkspaceLocation ws,
        string baseRef,
        string headRef,
        FactPathFinder.TraversalMode mode,
        bool gate,
        bool noCache,
        IReadOnlyList<string> extraRules,
        // Optional coarse progress callback (phase name, ms since previous phase) — awaited between the
        // top-level phases so a caller (the SSE endpoint) can stream live progress on a cold diff. Null (the
        // CLI) makes this a no-op; the diff RESULT is unchanged either way.
        Func<string, long, Task>? onPhase = null,
        // --no-amplification: drop the amplification tier from the per-EP delta (no ep_amplification_* rows).
        // Folded into the CACHE KEY — an amplified and a suppressed diff are DIFFERENT artifacts, and sharing one
        // slot would let a --no-amplification run poison the default view (the mistake --no-gate already avoids).
        bool amplification = true
    )
    {
        var (rules, baseDbPath, cacheRaw, cacheKey) = ResolveStoresAndCache(
            ws,
            baseRef,
            headRef,
            extraRules,
            gate,
            noCache,
            mode,
            amplification
        );
        using var cache = cacheRaw;

        // WARM PATH: a fully-materialized diff + provenance + per-EP FQN subset → return WITHOUT loading the
        // base graph or shaping/walking either graph.
        if (cacheKey is not null && cache!.Get(cacheKey) is { } cachedBlob && ImpactCacheCodec.Decode(cachedBlob) is { } art)
        {
            // The cached answer is still backed by BASE as well as the caller-owned HEAD context. This
            // lightweight open exists only on a cache hit; the cold path discloses during its existing
            // provenance/base-compute opens.
            if (StoreAnswerDisclosure.IsActive)
            {
                await using var baseContext = new RigDbContext(baseDbPath, readOnly: true);
                await SchemaGate.AssertReadableAsync(baseContext);
                await StoreAnswerDisclosure.DiscloseCurrentAsync(baseContext, baseDbPath, baseRef);
            }

            if (onPhase is not null)
            {
                await onPhase("cache hit", 0);
            }

            return art;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        async Task Tick(string name)
        {
            if (onPhase is not null)
            {
                await onPhase(name, sw.ElapsedMilliseconds);
            }

            sw.Restart();
        }

        var headProv = await ReadProvenanceAsync(headContext, headRef);
        var baseProv = await ResolveBaseProvenanceAsync(baseDbPath: baseDbPath, baseRef: baseRef);
        await Tick("provenance");
        var headData = await LoadHeadSideDataAsync(headContext, rules, gate: gate);
        await Tick("head: load graph + derive effects");
        var branchSide = ComputeBranchSide(mode: mode, headData: headData, rules: rules, amplification: amplification);
        await Tick("head: reach sets + footprints + hazards");
        var impactDiff = await AssembleImpactDiffAsync(
            baseDbPath: baseDbPath,
            baseRef: baseRef,
            rules: rules,
            mode: mode,
            headData: headData,
            branchSide: branchSide,
            gate: gate,
            amplification: amplification
        );
        await Tick("base: load + derive + diff");
        var fqnSites = branchSide.IdBySite;
        TrySaveDiffToCache(cache, cacheKey, impactDiff, baseProv, headProv, fqnSites);
        return new ImpactCacheArtifact(Diff: impactDiff, BaseProvenance: baseProv, HeadProvenance: headProv, FqnSites: fqnSites);
    }

    // The HEAD (branch) store data needed by the per-EP computations: the shaped graph, the entry points,
    // the derived effects, the method-id-by-site index, the body hashes, and the field-access ref targets.
    // Loaded once and threaded into ComputeBranchSideAsync and AssembleImpactDiffAsync so no second open is
    // needed for the branch store. Hazard delta: F8 combined single-kind scan mirrors `derive` / the base side.
    private sealed record HeadSideData(
        FactGraphData Graph,
        IReadOnlyList<DerivedEntryPoint> DerivedEps,
        IReadOnlyList<DerivedEntryPoint> PromotedEps,
        IReadOnlyList<DerivedEffect> Effects,
        Dictionary<(string, int), string> IdBySite,
        IReadOnlyDictionary<string, string> BodyHashes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> RefTargets
    );

    private static async Task<HeadSideData> LoadHeadSideDataAsync(RigDbContext context, RuleSet rules, bool gate = true)
    {
        var methods = await Reads.LoadDeadCodeMethodsAsync(context);
        // The branch's per-symbol declaration body hashes (guarded — empty on a pre-fact store). Diffed against
        // the base's (loaded once in ComputeBaseSideAsync) to find in-place body edits the reach-set diff misses.
        var branchBodyHashes = await Reads.LoadSymbolBodyHashesAsync(context);
        // Fully shaped graph: handoff-classified load → ShapeGraph → MarkEventSubscriptionHandoffs →
        // AddDeliveryEdges. Impact walks the delivery-edge-bearing graph, so per-EP --async reach includes
        // event/actor delivery paths.
        var graph = await Reads.LoadShapedGraphAsync(context: context, rules: rules);
        var epData = await Reads.LoadFactEntryPointDataAsync(context);
        var epSet = await DeriveEntryPointsAsync(context, epData, rules);
        var invocations = await Reads.LoadInvocationRefsAsync(context);
        var throwRefs = await Reads.LoadThrowRefsAsync(context);
        var allocationFacts = await Reads.LoadAllocationFactsAsync(context);
        // Hazard delta: impact loads the static-field read/write refs and runs the hazard post-pass on BOTH
        // stores (mirroring `derive`), so the derived effects carry hazard observations (race_window /
        // lazy_init_race; n_plus_1 / unserializable_payload ride along via the observation rules). Scoped to
        // impact — tree/reaches are untouched.
        // F8: one combined scan (RefKind in {read,write}) instead of two back-to-back single-kind queries.
        var (staticFieldWriteRefs, staticFieldReadRefs) = await Reads.LoadStaticFieldAccessRefsByKindAsync(context);
        var threadStaticCells = await Reads.LoadThreadStaticFieldIdsAsync(context);
        var volatileCells = await Reads.LoadVolatileFieldIdsAsync(context);
        // sync_over_async feed: `methods` (loaded above for IdBySite) already carries the `async` modifier bit.
        var asyncMethodIds = methods
            .Where(m => m.Modifiers.Split(' ').Contains("async"))
            .Select(m => m.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
        var effects = DeriveEffects(
            rules.Effects,
            rules.Observations,
            invocations,
            baseEdges: epData.BaseEdges,
            ctorRefs: epData.CtorRefs,
            throwRefs: throwRefs,
            staticFieldWriteRefs: staticFieldWriteRefs,
            staticFieldReadRefs: staticFieldReadRefs,
            deriveHazards: true,
            threadStaticCells: threadStaticCells,
            volatileCells: volatileCells,
            asyncMethodIds: asyncMethodIds,
            gate: gate,
            allocationFacts: allocationFacts,
            dualWriteSystemClassMap: rules.DualWrite?.SystemClassMap
        );
        // The branch's enclosing→field/property-access-targets lookup, built ONCE so ComputeReachSets can union
        // each reachable method's read/write targets as degenerate `R:` nodes at O(reach) cost.
        var refTargets = RefTargetsByEnclosing(await Reads.LoadFieldAccessRefsAsync(context));
        return new HeadSideData(
            Graph: graph,
            DerivedEps: epSet.Derived,
            PromotedEps: epSet.PromotedOrigins,
            Effects: effects,
            IdBySite: MethodIdBySite(methods),
            BodyHashes: branchBodyHashes,
            RefTargets: refTargets
        );
    }

    // The branch-side per-EP outputs computed over the already-loaded HEAD data: the entry-point set diff
    // vs the base, the branch reach sets, the (Kind, Route) → EP-ref site map, the effect footprints, and
    // the hazard sets. Bundled so AssembleImpactDiffAsync can consume them without re-opening the HEAD store.
    private sealed record BranchSideData(
        // The branch EP set, carried so the base side can compute the EP set-diff from its single load.
        IReadOnlyList<DerivedEntryPoint> BranchEps,
        // The head-side traversal session, carried so the guard-condition walk reuses this index rather than
        // building a fourth one over the same graph.
        FactPathFinder.TraversalSession Session,
        Dictionary<(string Kind, string Route), HashSet<string>> ReachSets,
        Dictionary<(string Kind, string Route), EntryPointRef> EpByKey,
        Dictionary<(string Kind, string Route), Dictionary<(string, string, string, string), EffectReach>> Footprints,
        Dictionary<(string Kind, string Route), HashSet<HazardFinding>> Hazards,
        Dictionary<(string Kind, string Route), HashSet<EpAmplification>> Amplifications,
        Dictionary<(string, int), string> IdBySite
    );

    // `rules` is threaded in for the amplification DISPLAY SCOPE (rules.Observations.Amplification — data, not a
    // C# list); `amplification` is the --no-amplification opt-out. Both only affect the amplification tier.
    private static BranchSideData ComputeBranchSide(
        FactPathFinder.TraversalMode mode,
        HeadSideData headData,
        RuleSet rules,
        bool amplification
    )
    {
        // The branch entry-point set. The two-store EP DIFF is computed on the base side (ComputeBaseSideAsync),
        // which already derives the base EP set — doing it here meant a second full base-store open + EP load.
        var branchEps = headData.DerivedEps.Concat(headData.PromotedEps).ToList();

        // --- Per-EP store-vs-store diff. The AFFECTED ENTRY POINTS are computed STRUCTURALLY: per EP, diff
        // its full reachable symbol set branch vs base ("two trees, diffed") — an EP is affected iff WHAT IT
        // REACHES changed, regardless of whether an effect rule fired. This catches the obj→sql kind of
        // migration the effect-set diff collapses (same key, different symbols), and excludes false positives.
        // ONE traversal session for the whole head side: reach sets, footprints and hazards (plus the
        // guard-condition walk in AssembleImpactDiffAsync) all traverse the same graph, and each used to
        // rebuild the GraphIndex from scratch.
        var headSession = FactPathFinder.OpenSession(headData.Graph);
        var branchReachSets = ComputeReachSets(headSession, branchEps, headData.IdBySite, mode, refsByEnclosing: headData.RefTargets);
        var epByKey = branchEps
            .GroupBy(e => (e.Kind, e.Route))
            .ToDictionary(
                g => g.Key,
                g => new EntryPointRef(
                    Kind: g.Key.Kind,
                    Route: g.Key.Route,
                    FilePath: g.First().FilePath,
                    Line: g.First().Line,
                    Requires: g.First().Requires
                )
            );
        var branchFootprints = ComputeFootprints(headSession, branchEps, headData.IdBySite, EffectKeysByEnclosing(headData.Effects), mode);
        // The branch's per-EP reachable-hazard + reachable-amplification sets (the finding mirror of the
        // footprint), diffed against the base's in DiffFootprints so each per-EP delta carries what was
        // gained/lost. ONE walk for both tiers.
        var (branchHazards, branchAmplifications) = ComputeFindingSets(
            headSession,
            branchEps,
            headData.IdBySite,
            HazardsByEnclosing(headData.Effects),
            amplification ? AmplificationsByEnclosing(headData.Effects, rules.Observations.AmplificationOrEmpty) : [],
            mode
        );
        return new BranchSideData(
            BranchEps: branchEps,
            Session: headSession,
            ReachSets: branchReachSets,
            EpByKey: epByKey,
            Footprints: branchFootprints,
            Hazards: branchHazards,
            Amplifications: branchAmplifications,
            IdBySite: headData.IdBySite
        );
    }

    // Load the base store ONCE (via ComputeBaseSideAsync), diff the branch reach sets and footprints against
    // it, and assemble the three signals (EP-set diff, structural affected EPs, per-EP behavioral deltas) into
    // one ImpactDiff. The branch store is represented by headData + branchSide — no second HEAD open needed.
    private static async Task<ImpactDiff> AssembleImpactDiffAsync(
        string baseDbPath,
        string baseRef,
        RuleSet rules,
        FactPathFinder.TraversalMode mode,
        HeadSideData headData,
        BranchSideData branchSide,
        bool gate = true,
        bool amplification = true
    )
    {
        var headGuarded = GuardConditionDiff.GuardedEdges(headData.Graph);
        var baseSide = await ComputeBaseSideAsync(
            baseDbPath: baseDbPath,
            baseRef: baseRef,
            rules: rules,
            mode: mode,
            branchEps: branchSide.BranchEps,
            headGuardedKeys: headGuarded.Keys,
            gate: gate,
            amplification: amplification
        );

        // The symbols whose declaration BODY changed base↔branch (differing/one-sided hash). An EP whose reach
        // intersects this set is affected IN-PLACE even when its structural reach-set diff is empty. Both maps
        // empty (pre-fact store on either side) => BodyChangedSymbols returns empty and the signal degrades
        // silently. branchBodyHashes is loaded once from the branch context above (headData.BodyHashes).
        var bodyChanged = BodyChangedSymbols(branchHashes: headData.BodyHashes, baseHashes: baseSide.BodyHashes);
        var affectedEntryPoints = DiffReachSets(
            branch: branchSide.ReachSets,
            baseStore: baseSide.ReachSets,
            epByKey: branchSide.EpByKey,
            bodyChanged: bodyChanged
        );
        var perEpDeltas = DiffFootprints(
            branch: branchSide.Footprints,
            baseStore: baseSide.Footprints,
            epByKey: branchSide.EpByKey,
            branchHazards: branchSide.Hazards,
            baseHazards: baseSide.Hazards,
            branchAmplifications: branchSide.Amplifications,
            baseAmplifications: baseSide.Amplifications
        );
        var guardConditions = ComputeGuardConditionDeltas(
            headData: headData,
            branchSide: branchSide,
            headGuarded: headGuarded,
            baseGuarded: baseSide.Guarded,
            basePresent: baseSide.PairsPresent,
            mode: mode
        );
        return new ImpactDiff(
            Ep: baseSide.EpDiff,
            AffectedEps: affectedEntryPoints,
            PerEp: perEpDeltas,
            GuardConditions: guardConditions,
            GuardCoverage: new GuardCoverage(
                BaseLambdaGuards: GuardConditionDiff.LambdaGuardCount(baseSide.Guarded),
                HeadLambdaGuards: GuardConditionDiff.LambdaGuardCount(headGuarded)
            )
        );
    }

    // The guard-condition diff: edges whose gating predicate moved, what they gate, and which EPs reach them.
    //
    // Cost is bounded by the CHANGED-edge count, not the graph: classification is a set comparison over
    // already-decoded conjuncts, and only the survivors seed a forward walk. The walk is what answers "what
    // does this condition gate" — the effect keys reachable FROM the callee. This is deliberately NOT the
    // effect's own guard set: post-fix the audit in MR !11025 is reached through a guarded lambda edge but is
    // itself unconditional within that lambda's body, so an effect-keyed diff still reports UNCHANGED. Keying
    // on the EDGE is what makes the predicate-only change visible without full transitive guard composition.
    private static IReadOnlyList<GuardConditionDelta> ComputeGuardConditionDeltas(
        HeadSideData headData,
        BranchSideData branchSide,
        Dictionary<(string Caller, string Callee), SortedSet<string>> headGuarded,
        Dictionary<(string Caller, string Callee), SortedSet<string>> baseGuarded,
        HashSet<(string Caller, string Callee)> basePresent,
        FactPathFinder.TraversalMode mode
    )
    {
        var candidates = new HashSet<(string Caller, string Callee)>(headGuarded.Keys);
        candidates.UnionWith(baseGuarded.Keys);
        var headPresent = GuardConditionDiff.PairsPresent(headData.Graph, candidates);

        // Pre-classify to find the callees actually worth a reach walk: the edges present on both sides whose
        // conjunct sets differ. Without this the walk would run for every guarded edge in the graph.
        var changedCallees = new List<string>();
        foreach (var key in candidates)
        {
            if (!basePresent.Contains(key) || !headPresent.Contains(key))
            {
                continue;
            }

            var b = baseGuarded.TryGetValue(key, out var bs) ? bs : [];
            var h = headGuarded.TryGetValue(key, out var hs) ? hs : [];
            if (GuardConditionDiff.Classify(b, h) is not null)
            {
                changedCallees.Add(key.Callee);
            }
        }

        var effectsByCallee = EffectsReachableFrom(
            session: branchSide.Session,
            seeds: changedCallees.Distinct(StringComparer.Ordinal).ToList(),
            effectsByEnclosing: EffectKeysByEnclosing(headData.Effects),
            mode: mode
        );

        // EP attribution: an EP is attributed an edge when its reach contains the CALLER (the frame whose
        // branch moved). A count plus a few sample routes, not a row per EP — one changed edge in a shared
        // utility is reachable from hundreds of EPs.
        var epsByCaller = new Dictionary<string, (int Count, IReadOnlyList<string> Samples)>(StringComparer.Ordinal);
        (int, IReadOnlyList<string>) EpsReaching(string caller)
        {
            if (epsByCaller.TryGetValue(caller, out var cached))
            {
                return cached;
            }

            var routes = new List<string>();
            var count = 0;
            foreach (var (key, reach) in branchSide.ReachSets)
            {
                if (!reach.Contains(caller))
                {
                    continue;
                }

                count++;
                if (routes.Count < 3)
                {
                    routes.Add($"{key.Kind} {key.Route}");
                }
            }

            routes.Sort(StringComparer.Ordinal);
            var result = (count, (IReadOnlyList<string>)routes);
            epsByCaller[caller] = result;
            return result;
        }

        return GuardConditionDiff.Diff(
            baseGuarded: baseGuarded,
            headGuarded: headGuarded,
            basePresent: basePresent,
            headPresent: headPresent,
            effectsFromCallee: callee => effectsByCallee.TryGetValue(callee, out var e) ? e : [],
            epsReaching: EpsReaching
        );
    }

    // `provider:operation` labels reachable from each seed node. One BFS per seed over the already-loaded
    // graph — the seeds are the callees of CHANGED guarded edges, so this is a handful of walks on a real MR,
    // not a per-edge cost. Unbounded depth/nodes to match ComputeFootprints: a truncated walk would silently
    // drop the very effect that makes a guard change reviewable, reading as "this gates nothing".
    private static Dictionary<string, IReadOnlyList<string>> EffectsReachableFrom(
        FactPathFinder.TraversalSession session,
        IReadOnlyList<string> seeds,
        Dictionary<string, List<(string, string, string, string)>> effectsByEnclosing,
        FactPathFinder.TraversalMode mode
    )
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (seeds.Count == 0)
        {
            return result;
        }

        var reached = session.ReachesInfoFromEachSeed(seeds, maxDepth: int.MaxValue, maxNodes: int.MaxValue, mode: mode);
        for (var i = 0; i < seeds.Count; i++)
        {
            var labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (node, _) in reached[i])
            {
                if (!effectsByEnclosing.TryGetValue(node, out var keys))
                {
                    continue;
                }

                foreach (var (provider, operation, _, _) in keys)
                {
                    labels.Add($"{provider}:{operation}");
                }
            }

            result[seeds[i]] = [.. labels.Order(StringComparer.Ordinal)];
        }

        return result;
    }

    // Write the proven diff + both sides' provenance + the diff-site FQN subset to the cache (best-effort).
    // Stored UNTRUNCATED (--limit is a render concern), so every --limit value renders correctly from one blob.
    // No-ops when caching is disabled (cacheKey is null).
    private static void TrySaveDiffToCache(
        QueryCache? cache,
        string? cacheKey,
        ImpactDiff impactDiff,
        StoreProvenance baseProv,
        StoreProvenance headProv,
        Dictionary<(string, int), string> fqnSites
    )
    {
        if (cacheKey is null)
        {
            return;
        }

        TryCache(() =>
            cache!.Put(
                key: cacheKey,
                payload: ImpactCacheCodec.Encode(diff: impactDiff, baseProvenance: baseProv, headProvenance: headProv, idBySite: fqnSites)
            )
        );
    }

    // The count of EPs whose reachable EFFECT SET changed (added / removed / amplified) — the FR-4
    // behavioral count, and the headline "changed behavior" number. PerEp ALSO contains EPs whose only
    // delta is a HAZARD gain/loss (race_window / n+1 / …) so they surface in the per-EP section; those must
    // NOT count here. --expect-no-effect-change is a deterministic effect-set gate — gating CI on a
    // (often heuristic) hazard belongs to a separate opt-in (e.g. a future --expect-no-hazard-gain), not
    // this flag. So a behavior-preserving refactor that merely trips a hazard heuristic stays green here.
    internal static int EffectChangedEpCount(ImpactDiff diff) =>
        diff.PerEp.Count(d => d.Added.Count > 0 || d.Removed.Count > 0 || d.Amplified.Count > 0);

    // Effect-filter the per-EP behavioral deltas for `impact`, mirroring reaches/tree/derive: --only keeps
    // just the listed effects, --exclude drops them, and the language-intrinsic providers (alloc/throw) are
    // withheld unless --intrinsic is set or --only names one. See IntrinsicProviders — on a
    // real 33-file MR, alloc:* alone was ~80% of impact's 68k rows and never changed a review verdict.
    //
    // An EP whose Added/Removed/Amplified lists ALL become empty is dropped entirely, so the output has no
    // `ep_delta … +0 -0 ~0` rows left behind by filtering. HiddenIntrinsic is the total number of withheld
    // effect entries across every EP, for the mandatory disclosure.
    //
    // The gate (--expect-no-effect-change) counts the FILTERED set, so what you READ and what CI DECIDES can
    // never disagree — the deliberate consequence is that with the default filter an alloc-only MR no longer
    // trips the gate, which is why `impact_summary` always reports intrinsic_hidden.
    internal static (IReadOnlyList<EpFootprintDelta> PerEp, int HiddenIntrinsic) FilterPerEpEffects(
        IReadOnlyList<EpFootprintDelta> perEp,
        HashSet<string> only,
        HashSet<string> exclude,
        bool includeIntrinsic
    )
    {
        var hideIntrinsic = !includeIntrinsic && !NamesIntrinsic(only);
        if (only.Count == 0 && exclude.Count == 0 && !hideIntrinsic)
        {
            return (perEp, 0);
        }

        var hidden = 0;
        var kept = new List<EpFootprintDelta>(perEp.Count);
        foreach (var d in perEp)
        {
            var added = d.Added.Where(x => Keep(x.Provider, x.Operation)).ToList();
            var removed = d.Removed.Where(x => Keep(x.Provider, x.Operation)).ToList();
            var amplified = d.Amplified.Where(x => Keep(x.Provider, x.Operation)).ToList();

            if (added.Count == 0 && removed.Count == 0 && amplified.Count == 0)
            {
                continue; // nothing behavioral survives the filter for this EP
            }

            kept.Add(d with { Added = added, Removed = removed, Amplified = amplified });
        }

        return (kept, hidden);

        bool Keep(string provider, string operation)
        {
            if ((only.Count > 0 && !InSet(provider, operation, only)) || InSet(provider, operation, exclude))
            {
                return false; // dropped by an EXPLICIT filter — not an intrinsic suppression, so not disclosed
            }

            if (hideIntrinsic && IntrinsicProviders.Contains(provider))
            {
                hidden++;
                return false;
            }

            return true;
        }

        static bool InSet(string provider, string operation, HashSet<string> set) =>
            set.Contains(provider) || set.Contains($"{provider}:{operation}");
    }

    // Effect-filter the guard-condition deltas with the SAME --only/--exclude grammar as the effect rows, so
    // `--only audit` narrows this signal to "guard changes that gate an audit" instead of leaving it as an
    // unfiltered wall. A row whose whole effect list is filtered out is dropped: the condition still moved,
    // but not around anything the reviewer asked about.
    //
    // Intrinsic providers are already excluded when the deltas are built (a guard moved around a `new` is not
    // review material), so there is nothing extra to disclose here — hence no HiddenIntrinsic counterpart.
    internal static IReadOnlyList<GuardConditionDelta> FilterGuardConditions(
        IReadOnlyList<GuardConditionDelta> deltas,
        HashSet<string> only,
        HashSet<string> exclude
    )
    {
        if (only.Count == 0 && exclude.Count == 0)
        {
            return deltas;
        }

        var kept = new List<GuardConditionDelta>(deltas.Count);
        foreach (var d in deltas)
        {
            var effects = d.Effects.Where(Keep).ToList();
            if (effects.Count == 0)
            {
                continue;
            }

            kept.Add(d with { Effects = effects });
        }

        return kept;

        bool Keep(string label)
        {
            // Labels are already `provider:operation`; match either the bare provider or the full pair.
            var provider = label.Split(':')[0];
            var inOnly = only.Contains(provider) || only.Contains(label);
            var inExclude = exclude.Contains(provider) || exclude.Contains(label);
            return (only.Count == 0 || inOnly) && !inExclude;
        }
    }

    // The number of edges whose guard NARROWED — the `--expect-no-guard-narrowing` gate count. Widened and
    // Changed are reported but do NOT gate: only narrowing is the "an effect silently stopped firing" shape,
    // and a gate that also tripped on relaxations would be unusable on ordinary feature work.
    internal static int NarrowedGuardCount(IReadOnlyList<GuardConditionDelta> deltas) =>
        deltas.Count(d => d.Verdict == GuardVerdict.Narrowed);

    // Read a store's provenance from its own run row (the run with the most symbols — the primary index).
    // Short sha = first 12 chars, matching `rig runs`. Fallback is the store-ref the user passed.
    private static async Task<StoreProvenance> ReadProvenanceAsync(RigDbContext context, string storeRef)
    {
        var runs = await Reads.ListRunsAsync(context);
        var primary = runs.OrderByDescending(r => r.SymbolCount).FirstOrDefault();
        var commit = primary?.SourceCommit;
        var shortSha = commit is { Length: > 0 } ? (commit.Length >= 12 ? commit[..12] : commit) : null;
        return new StoreProvenance(Branch: primary?.SourceBranch, ShortCommit: shortSha, Fallback: storeRef);
    }

    // The base store's provenance — opened read-only for just its run row.
    private static async Task<StoreProvenance> ResolveBaseProvenanceAsync(string baseDbPath, string baseRef)
    {
        await using var baseContext = new RigDbContext(baseDbPath, readOnly: true);
        await SchemaGate.AssertReadableAsync(baseContext);
        await StoreAnswerDisclosure.DiscloseCurrentAsync(baseContext, baseDbPath, baseRef);
        return await ReadProvenanceAsync(baseContext, baseRef);
    }

    // Derive entry points on the base store and set-diff them against the branch's, keyed on (Kind, Route).
    // DeriveEntryPointsAsync derives straight from the passed context with rules loaded from the (shared)
    // working dir — no query cache — so running it on a second store is correct. Internal for testing.
    // PURE set-diff of two already-derived entry-point sets, paired on (Kind, Route) so line/param moves and
    // formatting edits don't churn it.
    //
    // This used to open the BASE STORE itself and re-run LoadFactEntryPointDataAsync + DeriveEntryPointsAsync
    // — a second full base-side EP load (all base-type edges, all interface edges, ~217k method symbols, all
    // type symbols, all ctor refs) on top of the one ComputeBaseSideAsync already does, in a separate
    // uncoordinated RigDbContext. ComputeBaseSideAsync now calls this with the base EP set it has already
    // derived, so the base store is opened ONCE per run. Keeping it pure is what made that shareable.
    internal static EpDiff DiffEntryPointSets(IReadOnlyList<DerivedEntryPoint> branchEps, IReadOnlyList<DerivedEntryPoint> baseEps)
    {
        var branchKeys = branchEps.Select(e => (e.Kind, e.Route)).ToHashSet();
        var baseKeys = baseEps.Select(e => (e.Kind, e.Route)).ToHashSet();

        var added = branchKeys
            .Where(k => !baseKeys.Contains(k))
            .OrderBy(k => k.Kind, StringComparer.Ordinal)
            .ThenBy(k => k.Route, StringComparer.Ordinal)
            .ToList();
        var removed = baseKeys
            .Where(k => !branchKeys.Contains(k))
            .OrderBy(k => k.Kind, StringComparer.Ordinal)
            .ThenBy(k => k.Route, StringComparer.Ordinal)
            .ToList();
        return new EpDiff(Added: added, Removed: removed);
    }

    // Strip a DocID's parameter list (and leading `M:`) to a param-free `Namespace.Type.Method` key.
    // Delegates to the shared SymbolNameFormatter.FqnFromDocId so the impact EP card and the derive/
    // entrypoints/callers EP listings render the identical FQN form from one implementation.
    internal static string StripParams(string? docId) => FqnFromDocId(docId);

    // The copy-pasteable label for an EP card: the method's fully-qualified dotted name (namespace.Type.Member),
    // resolved from the EP's (FilePath, Line) against the in-RAM method index (no extra store I/O — idBySite is
    // already built for reach computation). This is the exact suffix `rig tree <from>` matches on, so a card
    // label round-trips straight into a tree query. Falls back to the path-style Route when the site maps to no
    // indexed method symbol (synthesized/promoted handoff EPs, lambdas) — those keep their derived route.
    // Internal for testing — the route↔FQN resolution is the contract behind "the card always shows a dotted
    // name when the site resolves, else the route".
    internal static string FqnForCard(string route, string filePath, int line, Dictionary<(string, int), string> idBySite) =>
        !string.IsNullOrEmpty(filePath) && idBySite.TryGetValue((filePath, line), out var docId) ? StripParams(docId) : route;

    // The providers that count as a concurrency GUARD (a lock/atomic acquired or released on a path). Used
    // by the FR-1e guard-delta callout: a guard added/removed on a path that still mutates shared state.
    private static readonly HashSet<string> GuardProviders = new(StringComparer.Ordinal) { "lock", "async_lock" };

    // FR-1e — the guard delta on a shared-mutation path. From one EP's footprint delta, the lock/async_lock
    // effects it GAINED (Added) and LOST (Removed), as "provider:operation" labels. Pure + derivable from the
    // already-computed Added/Removed sets (the guard effects ARE effects), so it needs nothing the diff didn't
    // already carry. Empty lists when no guard moved. Internal for unit-testing the classification.
    internal static (IReadOnlyList<string> Added, IReadOnlyList<string> Removed) GuardEffectDelta(EpFootprintDelta d)
    {
        static List<string> Guards(IReadOnlyList<(string Provider, string Operation, string Resource, string Enclosing)> keys) =>
            keys.Where(k => GuardProviders.Contains(k.Provider))
                .Select(k => $"{k.Provider}:{k.Operation}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

        return (Guards(d.Added), Guards(d.Removed));
    }

    // FR-1e fires for an EP when a guard (lock/async_lock) was added OR removed on its path AND the branch
    // path STILL carries a shared_state mutation — i.e. the concurrency protection around an inherently-shared
    // mutation changed. Both the lost-guard case (mutation now unguarded) and the gained-guard case (a fix) are
    // flagged for review; the static signal asserts the delta, not a verdict on correctness.
    internal static bool HasGuardDeltaOnSharedMutation(EpFootprintDelta d)
    {
        if (!d.SharedMutationOnPath)
        {
            return false;
        }

        var (added, removed) = GuardEffectDelta(d);
        return added.Count > 0 || removed.Count > 0;
    }

    // enclosing-method-id -> the distinct effect keys (provider, op, resource, param-free enclosing) declared
    // there, so a per-EP footprint is assembled by unioning the effects of every reachable enclosing node.
    private static Dictionary<string, List<(string, string, string, string)>> EffectKeysByEnclosing(IReadOnlyList<DerivedEffect> effects) =>
        effects
            .Where(e => e.EnclosingSymbolId is not null)
            .GroupBy(e => e.EnclosingSymbolId!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => (e.Provider, e.Operation, e.ResourceType, StripParams(e.EnclosingSymbolId))).Distinct().ToList(),
                StringComparer.Ordinal
            );

    // enclosing-method-id -> the distinct HAZARD findings (Type, Cell, param-free enclosing, Confidence)
    // declared there, so a per-EP hazard set is assembled by unioning the hazards of every reachable enclosing
    // node — exactly as EffectKeysByEnclosing does for effects. A hazard finding is an EffectObservationInfo
    // whose Type is in HazardKinds (race_window / lazy_init_race / n_plus_1 / unserializable_payload), found on
    // an effect's Observations. Cell = the observation's Context (the shared cell / loop identifier / payload
    // type — the same field the cli renders); Confidence rides along (not part of the diff identity, see the
    // record). An effect with no hazard observation contributes nothing. Distinct so two effects in one method
    // bearing the same finding count once.
    // NOTE ON THE TIER SPLIT: this stays on IsHazard, NOT IsFinding. The amplification tier is diffed separately
    // (AmplificationsByEnclosing below) into its OWN terse per-EP rows, because (a) the `hazard` delta rows and
    // the --expect-no-hazard-* gates are keyed to exactly the hazard set, and (b) a HazardFinding has no
    // provider/operation columns, which is the only thing an amplification row is worth reporting at.
    private static Dictionary<string, List<HazardFinding>> HazardsByEnclosing(IReadOnlyList<DerivedEffect> effects) =>
        effects
            .Where(e => e.EnclosingSymbolId is not null && e.Observations is not null)
            .SelectMany(e =>
                e.Observations!.Where(o => HazardKinds.IsHazard(o.Type))
                    .Select(o =>
                        (
                            Enclosing: e.EnclosingSymbolId!,
                            Finding: new HazardFinding(
                                Type: o.Type,
                                Cell: o.Context,
                                Enclosing: StripParams(e.EnclosingSymbolId),
                                Confidence: o.Confidence
                            )
                        )
                    )
            )
            .GroupBy(x => x.Enclosing, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Finding).Distinct().ToList(), StringComparer.Ordinal);

    // enclosing-method-id -> the distinct AMPLIFICATION (provider, operation) pairs declared there: an effect
    // carrying a looped_effect observation whose provider:operation is in the rules-declared display scope. The
    // amplification mirror of HazardsByEnclosing, at the TERSE grain — the pair, not the site: one loop wrapped
    // around three calls to the same provider:operation is ONE thing a reviewer needs told. Empty dictionary when
    // the tier is off (--no-amplification) or the rule scope is empty, which makes the whole delta a no-op.
    private static Dictionary<string, List<(string Provider, string Operation)>> AmplificationsByEnclosing(
        IReadOnlyList<DerivedEffect> effects,
        IReadOnlyList<FactAmplificationRule> scope
    ) =>
        effects
            .Where(e =>
                e.EnclosingSymbolId is not null
                && e.Observations is not null
                && e.Observations.Any(o => HazardKinds.IsAmplification(o.Type))
                && AmplificationScope.Includes(scope, e.Provider, e.Operation)
            )
            .GroupBy(e => e.EnclosingSymbolId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => (e.Provider, e.Operation)).Distinct().ToList(), StringComparer.Ordinal);

    // The reachable-HAZARD and reachable-AMPLIFICATION sets of each entry point, keyed on (Kind, Route), over an
    // ALREADY-LOADED graph — the finding mirror of ComputeFootprints. Forward-reaches every EP and unions the
    // findings of each reachable enclosing node. Same seed/depth/mode contract as ComputeFootprints so all of them
    // are computed over the identical reach. An EP whose reach bears no finding maps to an empty set.
    //
    // BOTH tiers come out of ONE walk on purpose: the reach walk is the expensive part of an impact run (minutes
    // over two stores), so the amplification tier must ride the hazard traversal rather than add a second one.
    // Amplification is accumulated as a per-(provider, operation) SITE COUNT over the reachable producing nodes,
    // then materialized into EpAmplification (whose identity is the pair alone — the count rides along).
    private static (
        Dictionary<(string Kind, string Route), HashSet<HazardFinding>> Hazards,
        Dictionary<(string Kind, string Route), HashSet<EpAmplification>> Amplifications
    ) ComputeFindingSets(
        FactPathFinder.TraversalSession session,
        IReadOnlyList<DerivedEntryPoint> eps,
        Dictionary<(string, int), string> idBySite,
        Dictionary<string, List<HazardFinding>> hazardsByEnclosing,
        Dictionary<string, List<(string Provider, string Operation)>> amplificationsByEnclosing,
        FactPathFinder.TraversalMode mode
    )
    {
        var distinct = eps.GroupBy(e => (e.Kind, e.Route, e.FilePath, e.Line)).Select(g => g.Key).ToList();
        var seedIds = distinct.Select(e => idBySite.TryGetValue((e.FilePath, e.Line), out var id) ? id : "").ToList();
        var reached = session.ReachesFromEachSeed(seedIds, maxDepth: int.MaxValue, maxNodes: int.MaxValue, mode: mode);

        var sets = new Dictionary<(string, string), HashSet<HazardFinding>>();
        var ampCounts = new Dictionary<(string, string), Dictionary<(string Provider, string Operation), int>>();
        for (var i = 0; i < distinct.Count; i++)
        {
            var key = (distinct[i].Kind, distinct[i].Route);
            if (!sets.TryGetValue(key, out var set))
            {
                sets[key] = set = new HashSet<HazardFinding>();
            }

            if (!ampCounts.TryGetValue(key, out var counts))
            {
                ampCounts[key] = counts = new Dictionary<(string, string), int>();
            }

            foreach (var node in reached[i])
            {
                if (hazardsByEnclosing.TryGetValue(node, out var findings))
                {
                    set.UnionWith(findings);
                }

                if (amplificationsByEnclosing.TryGetValue(node, out var pairs))
                {
                    foreach (var pair in pairs)
                    {
                        counts[pair] = counts.GetValueOrDefault(pair) + 1;
                    }
                }
            }
        }

        var amplifications = ampCounts.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(p => new EpAmplification(p.Key.Provider, p.Key.Operation, p.Value)).ToHashSet()
        );
        return (sets, amplifications);
    }

    // (FilePath, Line) -> the method declared there, so an EP (which carries a declaration site, not an id)
    // can seed a forward reach from its method node.
    private static Dictionary<(string, int), string> MethodIdBySite(IReadOnlyList<DeadCodeFinder.MethodMeta> methods)
    {
        var idBySite = new Dictionary<(string, int), string>();
        foreach (var m in methods)
        {
            if (!string.IsNullOrEmpty(m.FilePath))
            {
                idBySite[(m.FilePath, m.Line)] = m.SymbolId;
            }
        }

        return idBySite;
    }

    // The reachable-effect footprint of each entry point, keyed on (Kind, Route), over an ALREADY-LOADED graph
    // + effects (no store I/O — the caller loaded the store once). Forward-reaches every EP in parallel
    // (ReachesInfoFromEachSeed: one shared index, all cores). An EP whose (FilePath, Line) maps to no method
    // node seeds empty; duplicate (Kind, Route) sites union their footprints.
    //
    // Feature 1 (amplification): the inner value is no longer a bare effect-key SET but a per-key EffectReach
    // carrying CARDINALITY + a LOOP flag. Count = number of distinct reachable effect-bearing enclosing nodes
    // that produce the key (a derivable multiplicity — produced from more sites ⇒ higher count). InLoop ORs
    // the NearestLoopKind of each producing node over the EP's forward reach (the same BFS loop context the
    // tree's 🔁 marker uses; available identically on both stores). The set-diff over the key SET is recovered
    // by DiffFootprints reading the dictionary keys, so Added/Removed semantics are unchanged.
    private static Dictionary<(string Kind, string Route), Dictionary<(string, string, string, string), EffectReach>> ComputeFootprints(
        FactPathFinder.TraversalSession session,
        IReadOnlyList<DerivedEntryPoint> eps,
        Dictionary<(string, int), string> idBySite,
        Dictionary<string, List<(string, string, string, string)>> effectsByEnclosing,
        FactPathFinder.TraversalMode mode
    )
    {
        var distinct = eps.GroupBy(e => (e.Kind, e.Route, e.FilePath, e.Line)).Select(g => g.Key).ToList();
        var seedIds = distinct.Select(e => idBySite.TryGetValue((e.FilePath, e.Line), out var id) ? id : "").ToList();
        // Unbounded depth (matching `reaches`/`tree`): the default maxDepth=20 truncates effects whose shortest
        // reach is deeper than 20 hops, which made impact emit spurious per-EP +/- deltas when a change merely
        // shifted an effect's shortest depth across the 20 boundary. maxNodes is ALSO unbounded here: the default
        // 20000-node budget silently truncated the reach BFS with no signal (unlike BuildTree's BudgetCapped) —
        // so a >20k-node EP dropped its tail effect/hazard deltas as a false "unchanged". The reach is bounded by
        // the finite graph (+ the MaxBinding re-enqueue cap) and cycle dedup, so the walk still terminates.
        var reached = session.ReachesInfoFromEachSeed(seedIds, maxDepth: int.MaxValue, maxNodes: int.MaxValue, mode: mode);

        var footprints = new Dictionary<(string, string), Dictionary<(string, string, string, string), EffectReach>>();
        for (var i = 0; i < distinct.Count; i++)
        {
            var key = (distinct[i].Kind, distinct[i].Route);
            if (!footprints.TryGetValue(key, out var perKey))
            {
                footprints[key] = perKey = new Dictionary<(string, string, string, string), EffectReach>();
            }

            // Walk the EP's reachable nodes; for each effect-bearing one, accumulate its effect keys' count
            // (one per distinct producing node) and OR-in whether that node is reached under a loop.
            foreach (var (node, info) in reached[i])
            {
                if (!effectsByEnclosing.TryGetValue(node, out var keys))
                {
                    continue;
                }

                var nodeInLoop = info.NearestLoopKind is not null;
                foreach (var ek in keys)
                {
                    var prev = perKey.TryGetValue(ek, out var r) ? r : new EffectReach(Count: 0, InLoop: false);
                    perKey[ek] = new EffectReach(Count: prev.Count + 1, InLoop: prev.InLoop || nodeInLoop);
                }
            }
        }

        return footprints;
    }

    // Phase 3: the degenerate field/property-access nodes contributed by a set of reachable methods. For each
    // reachable enclosing method, union in its first-party read/write reference TARGETS, `R:`-prefixed. Pure +
    // internal so the union step is unit-testable WITHOUT a store. The caller passes a PREBUILT
    // enclosing→targets lookup (built once per store), so this is O(reachable) lookups, not a per-EP ref scan.
    internal static IReadOnlyCollection<string> RefTargetsFor(
        IReadOnlySet<string> reachableMethods,
        IReadOnlyDictionary<string, IReadOnlyList<string>> refsByEnclosing
    )
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in reachableMethods)
        {
            if (refsByEnclosing.TryGetValue(method, out var targets))
            {
                foreach (var t in targets)
                {
                    union.Add(RefNodePrefix + t);
                }
            }
        }

        return union;
    }

    // Per-EP REACHABLE SYMBOL SET over an already-loaded graph (no store I/O). Mirrors ComputeFootprints but
    // collects the raw reachable method DocIDs instead of mapping them to effect keys — so a structural diff
    // sees every reachable-set change, not just effect-classified ones. Duplicate (Kind, Route) sites union.
    // refsByEnclosing (Phase 3, optional) unions each reachable method's first-party field/property read/write
    // TARGETS into the set as `R:`-prefixed degenerate leaf nodes, so a changed access surfaces in the diff.
    private static Dictionary<(string Kind, string Route), HashSet<string>> ComputeReachSets(
        FactPathFinder.TraversalSession session,
        IReadOnlyList<DerivedEntryPoint> eps,
        Dictionary<(string, int), string> idBySite,
        FactPathFinder.TraversalMode mode,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? refsByEnclosing = null
    )
    {
        var distinct = eps.GroupBy(e => (e.Kind, e.Route, e.FilePath, e.Line)).Select(g => g.Key).ToList();
        var seedIds = distinct.Select(e => idBySite.TryGetValue((e.FilePath, e.Line), out var id) ? id : "").ToList();
        // Unbounded depth (matching `reaches`/`tree`): the default maxDepth=20 truncates effects whose shortest
        // reach is deeper than 20 hops, which made impact emit spurious per-EP +/- deltas when a change merely
        // shifted an effect's shortest depth across the 20 boundary. maxNodes is ALSO unbounded here: the default
        // 20000-node budget silently truncated the reach BFS with no signal (unlike BuildTree's BudgetCapped) —
        // so a >20k-node EP dropped its tail effect/hazard deltas as a false "unchanged". The reach is bounded by
        // the finite graph (+ the MaxBinding re-enqueue cap) and cycle dedup, so the walk still terminates.
        var reached = session.ReachesFromEachSeed(seedIds, maxDepth: int.MaxValue, maxNodes: int.MaxValue, mode: mode);

        var sets = new Dictionary<(string, string), HashSet<string>>();
        for (var i = 0; i < distinct.Count; i++)
        {
            var key = (distinct[i].Kind, distinct[i].Route);
            if (!sets.TryGetValue(key, out var set))
            {
                sets[key] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            set.UnionWith(reached[i]);
            if (refsByEnclosing is not null)
            {
                set.UnionWith(RefTargetsFor(reached[i], refsByEnclosing));
            }
        }

        return sets;
    }

    // Build the enclosing-method → first-party field/property read/write TARGET-DocIDs lookup ONCE per store
    // (Phase 3). Looked up per reachable method in ComputeReachSets, so the per-EP cost stays O(reach); without
    // this prebuild it would be an O(EPs × all-refs) re-scan. Distinct targets per enclosing method.
    private static Dictionary<string, IReadOnlyList<string>> RefTargetsByEnclosing(IReadOnlyList<SymbolRef> fieldAccessRefs) =>
        fieldAccessRefs
            .Where(r => r.Enclosing is not null)
            .GroupBy(r => r.Enclosing!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(r => r.Target).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal
            );

    // Diff two stores' per-EP reachable symbol sets: for every EP present in BOTH (paired on Kind+Route), the
    // methods its reach gained/lost. Returns only EPs whose reach changed, busiest-delta first. EPs added/
    // removed wholesale are the entry-point-diff section's job. epByKey supplies the EP's site for rendering.
    internal static IReadOnlyList<EpReachDelta> DiffReachSets(
        Dictionary<(string Kind, string Route), HashSet<string>> branch,
        Dictionary<(string Kind, string Route), HashSet<string>> baseStore,
        Dictionary<(string Kind, string Route), EntryPointRef> epByKey,
        IReadOnlySet<string>? bodyChanged = null
    )
    {
        bodyChanged ??= new HashSet<string>(StringComparer.Ordinal);
        var deltas = new List<EpReachDelta>();
        foreach (var (key, branchSet) in branch)
        {
            if (!baseStore.TryGetValue(key, out var baseSet))
            {
                continue;
            }

            var added = branchSet.Where(s => !baseSet.Contains(s)).ToList();
            var removed = baseSet.Where(s => !branchSet.Contains(s)).ToList();

            // Phase 2 (in-place): reachable methods PRESENT IN BOTH stores whose body hash differs — a changed
            // constant/literal the structural set-diff can't see (the method stayed in the reach). Intersect
            // the body-changed set with the SHARED reach (branch ∩ base) so a genuinely added/removed method is
            // attributed by the structural diff above, not double-counted here.
            var inPlace =
                bodyChanged.Count == 0
                    ? []
                    : branchSet.Where(s => baseSet.Contains(s) && bodyChanged.Contains(s)).OrderBy(s => s, StringComparer.Ordinal).ToList();

            // An EP is affected if its reach STRUCTURE changed (added/removed) OR a reachable body changed in
            // place. With none of those, it's untouched.
            if (added.Count == 0 && removed.Count == 0 && inPlace.Count == 0)
            {
                continue;
            }

            // Collapse signature/overload churn: bucket by param-free stem so a ctor whose params moved reads
            // as ONE `~` change, not an add+remove pair. The magnitude that RANKS the list is the count of
            // DISTINCT meaningful stems (added ∪ removed ∪ changed) PLUS the in-place body-changed methods, so
            // a 30-overload swap counts as 1 (Task 2) and a pure in-place edit still has a non-zero magnitude.
            var b = BucketStems(added, removed);
            var distinctStemDelta = b.AddedStems.Count + b.RemovedStems.Count + b.ChangedStems.Count + inPlace.Count;
            var ep = epByKey.TryGetValue(key, out var r)
                ? r
                : new EntryPointRef(Kind: key.Kind, Route: key.Route, FilePath: "", Line: 0, Requires: null);
            deltas.Add(
                new EpReachDelta(
                    Kind: key.Kind,
                    Route: key.Route,
                    FilePath: ep.FilePath,
                    Line: ep.Line,
                    Requires: ep.Requires,
                    Added: b.Added,
                    Removed: b.Removed,
                    AddedStems: b.AddedStems,
                    RemovedStems: b.RemovedStems,
                    ChangedStems: b.ChangedStems,
                    DistinctStemDelta: distinctStemDelta,
                    InPlaceCount: inPlace.Count,
                    InPlace: inPlace
                )
            );
        }

        // Stable order: by distinct meaningful (stem) delta desc, then Kind, then Route (Task 2).
        return deltas
            .OrderByDescending(d => d.DistinctStemDelta)
            .ThenBy(d => d.Kind, StringComparer.Ordinal)
            .ThenBy(d => d.Route, StringComparer.Ordinal)
            .ToList();
    }

    // The set of symbol DocIDs whose declaration BODY changed base↔branch: a DocID whose hash differs between
    // the two hash maps, OR is present on exactly one side (added/removed declarations also count). Empty when
    // either store lacks the BodyHash fact (pre-fact store) — the in-place signal degrades silently then.
    internal static IReadOnlySet<string> BodyChangedSymbols(
        IReadOnlyDictionary<string, string> branchHashes,
        IReadOnlyDictionary<string, string> baseHashes
    )
    {
        // Either side empty => the fact is absent on at least one store; no reliable signal, skip silently.
        if (branchHashes.Count == 0 || baseHashes.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, hash) in branchHashes)
        {
            if (!baseHashes.TryGetValue(id, out var baseHash) || !string.Equals(hash, baseHash, StringComparison.Ordinal))
            {
                changed.Add(id);
            }
        }

        foreach (var id in baseHashes.Keys)
        {
            if (!branchHashes.ContainsKey(id))
            {
                changed.Add(id);
            }
        }

        return changed;
    }

    // Load the BASE store ONCE and produce, from that single load: the base per-EP REACHABLE SYMBOL SETS (for
    // the structural affected-EP diff), the base per-EP effect FOOTPRINTS (for the behavioral per-EP diff), and
    // the base body-hash map (for the in-place signal). The branch side reuses the graph/effects RunAsync
    // already built, so the whole impact run is 2 store loads total.
    private static async Task<(
        Dictionary<(string Kind, string Route), HashSet<string>> ReachSets,
        Dictionary<(string Kind, string Route), Dictionary<(string, string, string, string), EffectReach>> Footprints,
        Dictionary<(string Kind, string Route), HashSet<HazardFinding>> Hazards,
        Dictionary<(string Kind, string Route), HashSet<EpAmplification>> Amplifications,
        IReadOnlyDictionary<string, string> BodyHashes,
        Dictionary<(string Caller, string Callee), SortedSet<string>> Guarded,
        HashSet<(string Caller, string Callee)> PairsPresent,
        EpDiff EpDiff
    )> ComputeBaseSideAsync(
        string baseDbPath,
        string baseRef,
        RuleSet rules,
        FactPathFinder.TraversalMode mode,
        // The branch's entry points, so the EP set-diff is computed HERE from the base EP set this method
        // already derives. Previously ComputeEpDiffAsync opened the base store a SECOND time to re-derive it.
        IReadOnlyList<DerivedEntryPoint> branchEps,
        // The HEAD store's guarded-edge keys. Passed IN because the guard-condition diff needs, for every
        // candidate edge, whether it exists on the BASE side — and the base graph only exists inside this
        // method. Union with the base's own guarded keys here so one pass over the base edges answers
        // presence for the whole candidate set; the HEAD half is answered by the caller, which holds that graph.
        IReadOnlyCollection<(string Caller, string Callee)> headGuardedKeys,
        bool gate = true,
        // --no-amplification: skip the base-side amplification set too, so the diff sees empty on BOTH sides and
        // emits no amplification rows (rather than reading every pair as "removed").
        bool amplification = true
    )
    {
        await using var context = new RigDbContext(baseDbPath, readOnly: true);
        await SchemaGate.AssertReadableAsync(context);
        await StoreAnswerDisclosure.DiscloseCurrentAsync(context, baseDbPath, baseRef);
        var graph = await Reads.LoadShapedGraphAsync(context: context, rules: rules);

        var methods = await Reads.LoadDeadCodeMethodsAsync(context);
        var epData = await Reads.LoadFactEntryPointDataAsync(context);
        var epSet = await DeriveEntryPointsAsync(context, epData, rules);
        var baseEps = epSet.Derived.Concat(epSet.PromotedOrigins).ToList();
        var idBySite = MethodIdBySite(methods);
        var invocations = await Reads.LoadInvocationRefsAsync(context);
        var throwRefs = await Reads.LoadThrowRefsAsync(context);
        var allocationFacts = await Reads.LoadAllocationFactsAsync(context);
        // Hazard delta: derive hazards on the base side too (mirror RunAsync / DeriveCommand) so the base
        // per-EP hazard set is computed over hazard-bearing effects and the diff compares like-for-like.
        // F8: one combined scan instead of two back-to-back single-kind queries (mirrors the HEAD side).
        var (staticFieldWriteRefs, staticFieldReadRefs) = await Reads.LoadStaticFieldAccessRefsByKindAsync(context);
        var threadStaticCells = await Reads.LoadThreadStaticFieldIdsAsync(context);
        var volatileCells = await Reads.LoadVolatileFieldIdsAsync(context);
        // sync_over_async feed: `methods` (loaded above for idBySite) already carries the `async` modifier bit.
        var asyncMethodIds = methods
            .Where(m => m.Modifiers.Split(' ').Contains("async"))
            .Select(m => m.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
        var effects = DeriveEffects(
            rules.Effects,
            rules.Observations,
            invocations,
            baseEdges: epData.BaseEdges,
            ctorRefs: epData.CtorRefs,
            throwRefs: throwRefs,
            staticFieldWriteRefs: staticFieldWriteRefs,
            staticFieldReadRefs: staticFieldReadRefs,
            deriveHazards: true,
            threadStaticCells: threadStaticCells,
            volatileCells: volatileCells,
            asyncMethodIds: asyncMethodIds,
            gate: gate,
            allocationFacts: allocationFacts,
            dualWriteSystemClassMap: rules.DualWrite?.SystemClassMap
        );

        // Phase 3: union the base's field/property-access targets into its reach sets too, so the per-EP
        // structural diff compares like-for-like (degenerate `R:` nodes on BOTH sides). Built once per store.
        var baseRefTargets = RefTargetsByEnclosing(await Reads.LoadFieldAccessRefsAsync(context));
        // One session for the base side too — same three traversals over one index.
        var baseSession = FactPathFinder.OpenSession(graph);
        var reachSets = ComputeReachSets(baseSession, baseEps, idBySite, mode, refsByEnclosing: baseRefTargets);
        var footprints = ComputeFootprints(baseSession, baseEps, idBySite, EffectKeysByEnclosing(effects), mode);
        var (hazards, amplifications) = ComputeFindingSets(
            baseSession,
            baseEps,
            idBySite,
            HazardsByEnclosing(effects),
            amplification ? AmplificationsByEnclosing(effects, rules.Observations.AmplificationOrEmpty) : [],
            mode
        );

        // Phase 2: the base body-hash map (guarded — empty on a pre-fact store), so RunAsync can diff it
        // against the branch's WITHOUT a second base load.
        var bodyHashes = await Reads.LoadSymbolBodyHashesAsync(context);

        // Guard-condition diff inputs, computed while the base graph is still open.
        var baseGuarded = GuardConditionDiff.GuardedEdges(graph);
        var candidates = new HashSet<(string Caller, string Callee)>(headGuardedKeys);
        candidates.UnionWith(baseGuarded.Keys);
        var basePairs = GuardConditionDiff.PairsPresent(graph, candidates);

        return (reachSets, footprints, hazards, amplifications, bodyHashes, baseGuarded, basePairs, DiffEntryPointSets(branchEps, baseEps));
    }

    // Diff two stores' per-EP footprints: for every EP present in BOTH (paired on Kind+Route), the effects its
    // reach gained/lost (set membership) AND the effects that are AMPLIFIED — same key on both sides but now
    // produced MORE (higher reach multiplicity) or MOVED INTO A LOOP (Feature 1). Returns only EPs whose
    // footprint changed in EITHER way, busiest-delta first. EPs added/removed wholesale are the EP-diff
    // section's job, not this. Internal for unit-testing the pure diff (ImpactAmplificationTests).
    //
    // Hazard delta (additive): branchHazards/baseHazards (optional — null for the existing effect-only callers
    // and tests) are the per-EP reachable-hazard SETS. For each EP present in BOTH stores, the set-diff yields
    // HazardsAdded (head-only) and HazardsRemoved (base-only). An EP whose ONLY change is a hazard delta —
    // empty effect Added/Removed/Amplified — still surfaces in the result (so a pure hazard gain isn't missed).
    internal static IReadOnlyList<EpFootprintDelta> DiffFootprints(
        Dictionary<(string Kind, string Route), Dictionary<(string, string, string, string), EffectReach>> branch,
        Dictionary<(string Kind, string Route), Dictionary<(string, string, string, string), EffectReach>> baseStore,
        // (Kind, Route) -> the EP's site, so each delta carries FilePath/Line for FQN rendering. An EP missing
        // here (shouldn't happen — branch footprints are keyed off the same EPs) falls back to empty site.
        IReadOnlyDictionary<(string Kind, string Route), EntryPointRef> epByKey,
        Dictionary<(string Kind, string Route), HashSet<HazardFinding>>? branchHazards = null,
        Dictionary<(string Kind, string Route), HashSet<HazardFinding>>? baseHazards = null,
        // Amplification delta (additive, same optional-map contract as the hazard maps): the per-EP
        // provider:operation sets that are reached inside an iteration context. Null on the effect-only callers
        // and under --no-amplification ⇒ no amplification rows.
        Dictionary<(string Kind, string Route), HashSet<EpAmplification>>? branchAmplifications = null,
        Dictionary<(string Kind, string Route), HashSet<EpAmplification>>? baseAmplifications = null
    )
    {
        var deltas = new List<EpFootprintDelta>();
        foreach (var (key, branchReach) in branch)
        {
            if (!baseStore.TryGetValue(key, out var baseReach))
            {
                continue;
            }

            var added = branchReach.Keys.Where(k => !baseReach.ContainsKey(k)).OrderBy(k => k).ToList();
            var removed = baseReach.Keys.Where(k => !branchReach.ContainsKey(k)).OrderBy(k => k).ToList();

            // Amplification is a THIRD category over the INTERSECTION: a key present on BOTH sides whose
            // branch reach is produced MORE (BranchCount > BaseCount) and/or has newly entered a loop
            // (BranchInLoop && !BaseInLoop). A count DECREASE or LEAVING a loop is not flagged.
            var amplified = new List<EpEffectAmplified>();
            foreach (var (ek, br) in branchReach)
            {
                if (!baseReach.TryGetValue(ek, out var ba))
                {
                    continue; // added key — handled by the set-diff above, not amplification
                }

                var countUp = br.Count > ba.Count;
                var loopEntry = br.InLoop && !ba.InLoop;
                if (countUp || loopEntry)
                {
                    amplified.Add(
                        new EpEffectAmplified(
                            Provider: ek.Item1,
                            Operation: ek.Item2,
                            Resource: ek.Item3,
                            Enclosing: ek.Item4,
                            BaseCount: ba.Count,
                            BranchCount: br.Count,
                            BaseInLoop: ba.InLoop,
                            BranchInLoop: br.InLoop
                        )
                    );
                }
            }

            // Hazard delta (additive): the set-diff of this EP's reachable hazard findings, head-only = added,
            // base-only = removed. Empty when no hazard maps were supplied (the effect-only callers/tests) or
            // when the EP's hazard set is unchanged.
            var (hazardsAdded, hazardsRemoved) = DiffHazards(key, branchHazards, baseHazards);

            // Amplification delta: the set-diff of this EP's reachable provider:operation amplification pairs.
            // Identity is the PAIR (EpAmplification.Equals ignores Sites), so "this EP's http:POST is now looped"
            // surfaces and "its site count went 3→4" deliberately does not.
            var (amplificationsAdded, amplificationsRemoved) = DiffAmplifications(key, branchAmplifications, baseAmplifications);

            // An EP is listed when its effect footprint changed (set membership or amplification) OR a hazard or
            // amplification finding was gained/lost — so a PURE finding gain (no effect-set change, which is
            // exactly what wrapping a loop around an existing call looks like) still surfaces in PerEp.
            if (
                added.Count > 0
                || removed.Count > 0
                || amplified.Count > 0
                || hazardsAdded.Count > 0
                || hazardsRemoved.Count > 0
                || amplificationsAdded.Count > 0
                || amplificationsRemoved.Count > 0
            )
            {
                var site = epByKey.GetValueOrDefault(key);
                // FR-1e: does the branch path still mutate shared state? (provider == shared_state — an
                // inherently-concurrent cell). Carried on the delta because an unchanged mutation is absent
                // from Added/Removed yet is what makes a co-occurring lock/guard delta race-relevant.
                var sharedMutationOnPath = branchReach.Keys.Any(k => string.Equals(k.Item1, "shared_state", StringComparison.Ordinal));
                deltas.Add(
                    new EpFootprintDelta(
                        Kind: key.Item1,
                        Route: key.Item2,
                        FilePath: site?.FilePath ?? "",
                        Line: site?.Line ?? 0,
                        BranchEffects: branchReach.Count,
                        BaseEffects: baseReach.Count,
                        Added: added,
                        Removed: removed,
                        Amplified: amplified
                            .OrderBy(a => a.Provider, StringComparer.Ordinal)
                            .ThenBy(a => a.Operation, StringComparer.Ordinal)
                            .ThenBy(a => a.Resource, StringComparer.Ordinal)
                            .ThenBy(a => a.Enclosing, StringComparer.Ordinal)
                            .ToList(),
                        SharedMutationOnPath: sharedMutationOnPath,
                        HazardsAdded: hazardsAdded,
                        HazardsRemoved: hazardsRemoved,
                        AmplificationsAdded: amplificationsAdded,
                        AmplificationsRemoved: amplificationsRemoved
                    )
                );
            }
        }

        return deltas
            .OrderByDescending(d =>
                d.Added.Count
                + d.Removed.Count
                + d.Amplified.Count
                + d.HazardsAddedOrEmpty.Count
                + d.HazardsRemovedOrEmpty.Count
                + d.AmplificationsAddedOrEmpty.Count
                + d.AmplificationsRemovedOrEmpty.Count
            )
            .ThenBy(d => d.Route, StringComparer.Ordinal)
            .ToList();
    }

    // The per-EP hazard set-diff: head-only findings = added, base-only = removed, both ordered stably. Returns
    // empty lists when no hazard maps were supplied or the EP is absent on either side (so the diff degrades
    // silently on a pre-hazard store / the effect-only callers). Pure + ordered so the cache round-trip and the
    // unit tests see a deterministic list.
    private static (IReadOnlyList<HazardFinding> Added, IReadOnlyList<HazardFinding> Removed) DiffHazards(
        (string Kind, string Route) key,
        Dictionary<(string Kind, string Route), HashSet<HazardFinding>>? branchHazards,
        Dictionary<(string Kind, string Route), HashSet<HazardFinding>>? baseHazards
    )
    {
        if (branchHazards is null || baseHazards is null)
        {
            return ([], []);
        }

        var branchSet = branchHazards.GetValueOrDefault(key) ?? [];
        var baseSet = baseHazards.GetValueOrDefault(key) ?? [];
        if (branchSet.Count == 0 && baseSet.Count == 0)
        {
            return ([], []);
        }

        static List<HazardFinding> Order(IEnumerable<HazardFinding> hs) =>
            hs.OrderBy(h => h.Type, StringComparer.Ordinal)
                .ThenBy(h => h.Cell, StringComparer.Ordinal)
                .ThenBy(h => h.Enclosing, StringComparer.Ordinal)
                .ThenBy(h => h.Confidence, StringComparer.Ordinal)
                .ToList();

        var added = Order(branchSet.Where(h => !baseSet.Contains(h)));
        var removed = Order(baseSet.Where(h => !branchSet.Contains(h)));
        return (added, removed);
    }

    // The per-EP AMPLIFICATION set-diff, mirroring DiffHazards: head-only pairs = added, base-only = removed,
    // stably ordered by provider then operation. Empty when no maps were supplied (--no-amplification / the
    // effect-only callers) or the EP is unchanged. Site counts come from the side the row is reported on (the head
    // count for an added pair, the base count for a removed one) — Sites is outside the diff identity.
    private static (IReadOnlyList<EpAmplification> Added, IReadOnlyList<EpAmplification> Removed) DiffAmplifications(
        (string Kind, string Route) key,
        Dictionary<(string Kind, string Route), HashSet<EpAmplification>>? branchAmplifications,
        Dictionary<(string Kind, string Route), HashSet<EpAmplification>>? baseAmplifications
    )
    {
        if (branchAmplifications is null || baseAmplifications is null)
        {
            return ([], []);
        }

        var branchSet = branchAmplifications.GetValueOrDefault(key) ?? [];
        var baseSet = baseAmplifications.GetValueOrDefault(key) ?? [];
        if (branchSet.Count == 0 && baseSet.Count == 0)
        {
            return ([], []);
        }

        static List<EpAmplification> Order(IEnumerable<EpAmplification> xs) =>
            xs.OrderBy(a => a.Provider, StringComparer.Ordinal).ThenBy(a => a.Operation, StringComparer.Ordinal).ToList();

        return (Order(branchSet.Where(a => !baseSet.Contains(a))), Order(baseSet.Where(a => !branchSet.Contains(a))));
    }

    // Partition an EP's added/removed reachable DocIDs by param-free stem (StripParams). A stem present on
    // BOTH sides is a signature change (ChangedStems) and its raw ids drop out of Added/Removed — collapsing
    // the `- #ctor(old)` / `+ #ctor(new)` churn to one `~` line. A stem on one side only stays a genuine
    // add/remove; its raw ids are preserved (ordered) for tsv tooling. All three stem lists are ordered.
    internal static StemBuckets BucketStems(IReadOnlyList<string> added, IReadOnlyList<string> removed)
    {
        var addedStems = added.Select(StripParams).ToHashSet(StringComparer.Ordinal);
        var removedStems = removed.Select(StripParams).ToHashSet(StringComparer.Ordinal);
        var changedStems = addedStems.Where(removedStems.Contains).ToHashSet(StringComparer.Ordinal);

        var rawAdded = added.Where(id => !changedStems.Contains(StripParams(id))).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var rawRemoved = removed.Where(id => !changedStems.Contains(StripParams(id))).OrderBy(s => s, StringComparer.Ordinal).ToList();
        return new StemBuckets(
            Added: rawAdded,
            Removed: rawRemoved,
            AddedStems: addedStems.Where(s => !changedStems.Contains(s)).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            RemovedStems: removedStems.Where(s => !changedStems.Contains(s)).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            ChangedStems: changedStems.OrderBy(s => s, StringComparer.Ordinal).ToList()
        );
    }

    // The `R:` prefix marks a DEGENERATE reach node: a field/property access TARGET (Phase 3), not a callable
    // method node. It keeps these distinct from method DocIDs in the reach set so the structural diff sees a
    // changed access, and StripParams leaves it intact (no `(`), so it reads as its own stem.
    internal const string RefNodePrefix = "R:";

    // Data-shape dominance threshold: an EP whose moved+changed members are at least this fraction data-shape
    // (fields/properties/accessors/ctors) is RecordShape — the few non-data-shape moves are incidental to the
    // same record change. Below it, real method churn is significant enough to warrant review (Other). 0.8 keeps
    // pure field-ripple and field-ripple-plus-a-moved-type in RecordShape while routing genuine refactors to
    // Other; validated against the live MR (see the migration's Master workflow EPs landing in Other).
    private const double DataShapeDominance = 0.8;

    // Classify ONE structural-only EP delta (effect set unchanged) into a cause bucket — pure + internal so the
    // bucketing is unit-testable without a store. "Data-shape" = a field/property-access node (`R:` prefix), a
    // property accessor (`.get_`/`.set_`), or a constructor (`.#ctor`) — all three are how a record's field
    // add/remove shows up in the reach graph. Classification is PROPORTIONAL (not all-or-nothing): record-shape
    // when data-shape dominates the moved+changed set, so a migration's field ripple isn't mislabeled "other"
    // just because one real method moved alongside it.
    internal static StructuralCause ClassifyStructuralCause(EpReachDelta d)
    {
        static bool IsDataShape(string stem) =>
            stem.StartsWith(RefNodePrefix, StringComparison.Ordinal)
            || stem.Contains(".get_", StringComparison.Ordinal)
            || stem.Contains(".set_", StringComparison.Ordinal)
            || stem.EndsWith(".#ctor", StringComparison.Ordinal);
        static bool IsCtor(string stem) => stem.EndsWith(".#ctor", StringComparison.Ordinal);

        // Every member that moved or changed signature — the population we classify over.
        var members = d.AddedStems.Concat(d.RemovedStems).Concat(d.ChangedStems).ToList();
        if (members.Count == 0)
        {
            // No structural move at all — the EP is affected only by an in-place body change (Phase 2).
            return d.InPlaceCount > 0 ? StructuralCause.InPlace : StructuralCause.Other;
        }

        // Purely constructor signatures (no fields/methods moved) — a record's ctor params changed and nothing
        // else. Called out separately from the field-add case since there's no new accessor, just a re-signing.
        if (members.All(IsCtor))
        {
            return StructuralCause.CtorSig;
        }

        var dataShape = members.Count(IsDataShape);
        return dataShape >= members.Count * DataShapeDominance ? StructuralCause.RecordShape : StructuralCause.Other;
    }
}
