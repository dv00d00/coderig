using Rig.Cli.Caching;
using Rig.Cli.Commands;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using static Rig.Cli.Caching.QueryCacheKeys;

namespace Rig.Cli.Effects;

// The stage-2 effect derivation + the --only/--exclude filter, shared by reaches/tree/derive/impact. Runs
// FactEffectDeriver.Derive over the caller's already-loaded effect + observation rules (from its RuleSet)
// and its (bounded or whole-store) invocation/ctor/throw inputs.
internal static class EffectDerivation
{
    internal static IReadOnlyList<DerivedEffect> DeriveEffects(
        IReadOnlyList<FactEffectRule> effectRules,
        FactObservationRules observationRules,
        IReadOnlyList<FactInvocation> invocations,
        IReadOnlyList<(string, string)> baseEdges,
        IReadOnlyList<SymbolRef> ctorRefs,
        IReadOnlyList<SymbolRef> throwRefs,
        // FR-1(b): static-field/auto-property write refs (whole-store; supplied by `derive`). The bounded
        // reaches/tree/impact closures do not yet bound these, so they default to none there (a follow-up).
        IReadOnlyList<FactFieldAccess>? staticFieldWriteRefs = null,
        // FR-1 read arm: static-field/auto-property read refs (whole-store; supplied by `derive`), threaded
        // symmetrically to the write refs. Defaults to none for the bounded closures.
        IReadOnlyList<FactFieldAccess>? staticFieldReadRefs = null,
        // Hazard post-pass (race_window read-before-write matcher). Default OFF — like the other field-fed
        // signals it runs only on the whole-store `derive` path, not the bounded tree/reaches/impact closures
        // (which don't bound the static-field refs, so a read+write pair would be incomplete there anyway).
        bool deriveHazards = false,
        // [ThreadStatic] cell DocIDs (Reads.LoadThreadStaticFieldIdsAsync). A read→write on one is rerouted
        // from race_window to thread_local_context (thread-confined ⇒ not a race, but the FR-2 surface).
        // Null/empty leaves the legacy race_window classification unchanged.
        IReadOnlySet<string>? threadStaticCells = null,
        // `volatile` field DocIDs (Reads.LoadVolatileFieldIdsAsync). Corroborates the safe-DCL suppression
        // in the lazy_init_race lock-enclosed tier (FactHazardDeriver). Null/empty = never suppress.
        IReadOnlySet<string>? volatileCells = null,
        // Symbol ids of methods declared `async` (Reads.LoadDeadCodeMethodsAsync, filtered on the "async"
        // token in Modifiers). Feeds the sync_over_async hazard (an `async_block` effect — Task.Wait /
        // .GetAwaiter().GetResult() — whose enclosing method is itself async). Null/empty = no findings
        // (FactHazardDeriver.DeriveSyncOverAsync's null-safety convention, same as threadStaticCells/volatileCells).
        IReadOnlySet<string>? asyncMethodIds = null,
        // FR-1 read-arm WRITE-PAIRING GATE (on by default). When true, a shared_state:read effect is emitted
        // only for a static-field read whose cell ALSO appears as a static-field write target somewhere in the
        // same input set — so a read of a never-written cell (const/enum/other immutable static) is dropped as
        // pure inventory noise (it can never pair with a write for the race_window TOCTOU hazard). Pure
        // presentation/inventory filtering: race_window is unaffected (its matcher already only pairs same-cell
        // read+write, so an unpaired read contributes nothing). `--no-gate` flips this off (emit every read).
        bool gate = true,
        IReadOnlyList<AllocationFact>? allocationFacts = null
    )
    {
        // Pre-filter the static-field READ refs to cells that are also WRITTEN somewhere (the gate). An unpaired
        // read can never form a race_window pair, so it is inventory-only — drop it unless --no-gate opts in.
        if (gate && staticFieldReadRefs is { Count: > 0 })
        {
            var writtenCells = (staticFieldWriteRefs ?? []).Select(w => w.Target).ToHashSet(StringComparer.Ordinal);
            staticFieldReadRefs = staticFieldReadRefs.Where(r => writtenCells.Contains(r.Target)).ToList();
        }

        var effects = FactEffectDeriver.Derive(
            invocations,
            effectRules,
            providerFilter: null,
            baseEdges: baseEdges,
            ctorRefs: ctorRefs,
            observationRules: observationRules,
            throwRefs: throwRefs,
            staticFieldWriteRefs: staticFieldWriteRefs,
            staticFieldReadRefs: staticFieldReadRefs
        );
        if (allocationFacts is { Count: > 0 })
        {
            effects = effects.Concat(CoreAllocationEffectDeriver.Derive(allocationFacts, observationRules)).ToList();
        }

        // Annotate qualifying effects with hazard observations — pure post-passes over the derived effects
        // that add observations and drop nothing:
        //   - race_window: a read-before-write of the same shared cell in one method (RMW / TOCTOU);
        //   - dual_write: durable writes to ≥2 distinct system classes in one method (FR-8, distributed
        //     consistency — DB + queue / search / cache / external HTTP with no atomicity).
        //   - sync_over_async: a blocking Task.Wait()/.GetAwaiter().GetResult() whose enclosing method is
        //     itself declared async (threadpool starvation / deadlock risk — "just await instead").
        if (!deriveHazards)
        {
            return effects;
        }

        effects = FactHazardDeriver.DeriveRaceWindows(effects, threadStaticCells, volatileCells);
        effects = FactHazardDeriver.DeriveDualWrites(effects);
        effects = FactHazardDeriver.DeriveSyncOverAsync(effects, asyncMethodIds);
        return effects;
    }

    // The WHOLE-STORE hazard-augmented effect set: every indexed symbol's effects + the field-fed
    // shared_state arms + the race_window/dual_write/thread_local_context post-pass — i.e. exactly what
    // `derive` computes. EP-independent and traversal-mode-independent (an effect is a per-method fact), so
    // `tree --hazards` filters this to its reachable methods instead of re-deriving a bounded slice per EP.
    // Uncached; the cached wrapper is LoadOrDeriveHazardEffectsAsync.
    internal static async Task<IReadOnlyList<DerivedEffect>> DeriveHazardEffectsAsync(
        RigDbContext context,
        RuleSet rules,
        // Perf (#1b): the caller (DeriveCommand) has already loaded the EP data for its own base-type gates;
        // thread it through to skip the redundant LoadFactEntryPointDataAsync here. Null = load it ourselves
        // (back-compat for callers that don't have it — e.g. the uncached cache-miss path triggered elsewhere).
        FactEntryPointDeriver.FactEntryPointData? epData = null,
        // FR-1 read-arm write-pairing gate (on by default) — threaded into DeriveEffects. `--no-gate` flips off.
        bool gate = true
    )
    {
        epData ??= await Reads.LoadFactEntryPointDataAsync(context);
        var invocations = await Reads.LoadInvocationRefsAsync(context);
        var throwRefs = await Reads.LoadThrowRefsAsync(context);
        var allocationFacts = await Reads.LoadAllocationFactsAsync(context);
        // Perf (#3): one reference_facts scan for both the write and read static-field arms (was two).
        var (staticFieldWriteRefs, staticFieldReadRefs) = await Reads.LoadStaticFieldAccessRefsByKindAsync(context);
        var threadStaticCells = await Reads.LoadThreadStaticFieldIdsAsync(context);
        var volatileCells = await Reads.LoadVolatileFieldIdsAsync(context);
        // sync_over_async feed: the method universe's `async` modifier bit (already mined by FactExtractor;
        // not loaded elsewhere on this path, so loaded fresh here).
        var methods = await Reads.LoadDeadCodeMethodsAsync(context);
        var asyncMethodIds = methods
            .Where(m => m.Modifiers.Split(' ').Contains("async"))
            .Select(m => m.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
        return DeriveEffects(
            effectRules: rules.Effects,
            observationRules: rules.Observations,
            invocations: invocations,
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
            allocationFacts: allocationFacts
        );
    }

    // Cached over the .rig/cache.db query cache, keyed by (store + rules) — see HazardEffectsCacheKey. The
    // store-correct rigDirectory/storeKey/rulesHash are passed in (so `--store <ref>` caches against the right
    // commit's store, not the default). Computed once then memoized; `derive` and `tree --hazards` share the
    // entry, and a reindex (storeKey) or rule edit (rulesHash) misses → recompute, keeping hazards query-side.
    internal static async Task<IReadOnlyList<DerivedEffect>> LoadOrDeriveHazardEffectsAsync(
        RigDbContext context,
        string rigDirectory,
        string storeKey,
        string rulesHash,
        RuleSet rules,
        bool useCache,
        // Perf (#1b): the already-loaded EP data, threaded into DeriveHazardEffectsAsync on a cache MISS so it
        // need not reload it. Unused on a cache HIT (derivation is skipped). Null = derivation loads its own.
        FactEntryPointDeriver.FactEntryPointData? epData = null,
        // FR-1 read-arm write-pairing gate (on by default). CRITICAL: it is folded into the cache key below —
        // a gated and an ungated (--no-gate) run produce DIFFERENT effect sets, so they must not share an entry
        // (else --no-gate would return cached gated results, or vice-versa).
        bool gate = true
    )
    {
        if (!useCache)
        {
            return await DeriveHazardEffectsAsync(context, rules, epData, gate: gate);
        }

        using var cache = QueryCache.Open(rigDirectory: rigDirectory, storeKey: storeKey);
        // Fold the gate state into the rule-fingerprint slot of the key so the gated and ungated (--no-gate)
        // effect sets never share a cache entry. BOTH carry an explicit token (not the bare rulesHash) so a
        // blob written by a PRE-gate binary under the bare fingerprint can never be served as a gated result —
        // the gate is a derivation-logic change the rulesHash alone doesn't capture (a one-time recompute on
        // upgrade; correctness over a warm-cache hit — a fact tool must not silently return stale ungated counts).
        var keyRulesHash = gate ? $"{rulesHash}|gate" : $"{rulesHash}|nogate";
        var key = cache is null ? null : HazardEffectsCacheKey(storeKey: storeKey, rulesHash: keyRulesHash);
        if (key is not null && cache!.Get(key) is { } blob && HazardEffectsCodec.Decode(blob) is { } hit)
        {
            return hit;
        }

        var derived = await DeriveHazardEffectsAsync(context, rules, epData, gate: gate);
        if (key is not null)
        {
            TryCache(() => cache!.Put(key, HazardEffectsCodec.Encode(derived)));
        }

        return derived;
    }

    // The WHOLE-STORE GRAPH-TIER hazard findings: cache_coherence + event_cycle + static_init_capture — the
    // three hazard sources that are NOT effect-attached (they have no owning DerivedEffect) and so are NOT in
    // HazardFindings(effects). Each is a property of the SHAPED call graph (cache_coherence's forward-closure
    // correlation, event_cycle's delivery-edge cycle detection) or the static-field universe — EP-independent,
    // whole-store facts. Mirrors DeriveCommand's inline derivation (the union order is event_cycle,
    // cache_coherence, static_init_capture) so derive's output is unchanged. Opt-in arms (cache_coherence /
    // static_init_capture) fire only when their rule section is present, exactly as `derive` gates them.
    // Uncached; the cached wrapper is LoadOrDeriveGraphHazardFindingsAsync.
    internal static async Task<IReadOnlyList<DeriveCommand.HazardFinding>> DeriveGraphHazardFindingsAsync(
        RigDbContext context,
        RuleSet rules,
        // The shaped graph (cache_coherence + event_cycle derive over it). The caller (DeriveCommand) already
        // built it; thread it through to skip the redundant LoadShapedGraphAsync here. Null = load it ourselves.
        FactGraphData? shapedGraph = null,
        // The UNFILTERED whole-store effect set — cache_coherence MUST see PRE-`--only/--exclude` effects (the
        // companion cache:invalidate effects would otherwise be hidden and manufacture false missing-invalidation
        // findings). Null = load it ourselves (uncached). Callers that already hold it thread it through.
        IReadOnlyList<DerivedEffect>? unfilteredEffects = null
    )
    {
        shapedGraph ??= await Reads.LoadShapedGraphAsync(context: context, rules: rules);
        // The effect set is loaded UNCACHED here when not supplied — this method is the cache-miss derive path,
        // and the caller-supplied set (DeriveCommand / the cached effect wrapper) is the warm reuse.
        unfilteredEffects ??= await DeriveHazardEffectsAsync(context, rules);

        return await GraphHazardFindingsAsync(
            rules: rules,
            shapedGraph: shapedGraph,
            unfilteredEffects: unfilteredEffects,
            // Deferred, not awaited eagerly: the static-field universe is loaded ONLY inside the
            // static_init_capture arm on the store path, and a live caller must not pay a whole-symbol scan
            // for a rule section that is absent.
            staticFieldIds: () => Reads.LoadStaticFieldIdsAsync(context)
        );
    }

    // The graph-tier hazard derivation itself, with EVERY store touch hoisted into its three FEEDS (the shaped
    // graph, the unfiltered whole-store effect set, the static-field universe). Factored out so the LIVE path
    // runs the SAME classification over in-memory feeds instead of a second, drifting copy of it — the same
    // single-sourcing the FactInvocation / CallEdge projections already got. The union ORDER (event_cycle,
    // cache_coherence, static_init_capture) is load-bearing: it is the order `derive` renders findings in.
    internal static async Task<IReadOnlyList<DeriveCommand.HazardFinding>> GraphHazardFindingsAsync(
        RuleSet rules,
        FactGraphData shapedGraph,
        IReadOnlyList<DerivedEffect> unfilteredEffects,
        Func<Task<IReadOnlySet<string>>> staticFieldIds
    )
    {
        var findings = new List<DeriveCommand.HazardFinding>();

        // event_cycle: a feedback cycle that closes through ≥1 publish→consumer delivery edge. Always derived
        // (no rule gate) — exactly as DeriveCommand does.
        findings.AddRange(DeriveCommand.EventCycleFindings(FactCycleDeriver.DeriveEventCycles(shapedGraph)));

        // cache_coherence (FR-7): an anchor bulk_write whose forward closure lacks a same-key cache:invalidate.
        // Opt-in: only when the `cacheCoherence` rule section is present. Spec replicated from DeriveCommand.
        if (rules.CacheCoherence is { } cc)
        {
            findings.AddRange(
                DeriveCommand.CacheCoherenceFindings(
                    FactCorrelationDeriver.Derive(
                        graph: shapedGraph,
                        effects: unfilteredEffects,
                        spec: new CorrelationSpec(
                            Anchor: new EffectPredicate(Provider: "llblgen", Operation: "bulk_write"),
                            Companion: new EffectPredicate(Provider: "cache", Operation: "invalidate"),
                            AnchorNormalize: new NormalizeSpec(
                                SimpleTypeName: true,
                                StripSuffix: ["EntityCollection", "Collection", "DAO"]
                            ),
                            CompanionNormalize: new NormalizeSpec(SimpleTypeName: true, StripSuffix: ["Cache"]),
                            ExcludeEnclosingNamespaceSuffix: cc.ExcludeEnclosingNamespaceSuffix ?? ["CollectionClasses", "DaoClasses"],
                            InScopeKeys: DeriveCommand.BuildCacheInScopeKeys(cachedEntities: cc.CachedEntities, effects: unfilteredEffects)
                        )
                    )
                )
            );
        }

        // static_init_capture: a config/Settings read frozen in a static field initializer. Opt-in: only when
        // the `staticInitCapture` rule section is present. Spec replicated from DeriveCommand.
        if (rules.StaticInitCapture is { } sic)
        {
            findings.AddRange(
                DeriveCommand.StaticInitCaptureFindings(
                    FactStaticInitCaptureDeriver.Derive(
                        effects: unfilteredEffects,
                        spec: new StaticInitCaptureSpec(MutableSourcePatterns: sic.MutableSources),
                        staticFieldIds: await staticFieldIds()
                    )
                )
            );
        }

        return findings;
    }

    // Cached over the .rig/cache.db query cache, keyed by (store + rules) — see GraphHazardFindingsCacheKey,
    // a DISTINCT namespace from the effect-attached HazardEffectsCacheKey. Computed once then memoized; `derive`
    // and `tree --hazards` share the entry, and a reindex (storeKey) or rule edit (rulesHash) misses → recompute,
    // keeping hazards query-side. Same shape as LoadOrDeriveHazardEffectsAsync (the effect-attached twin).
    internal static async Task<IReadOnlyList<DeriveCommand.HazardFinding>> LoadOrDeriveGraphHazardFindingsAsync(
        RigDbContext context,
        string rigDirectory,
        string storeKey,
        string rulesHash,
        RuleSet rules,
        bool useCache,
        // The already-shaped graph + the unfiltered effect set, threaded into the cache-MISS derive path so it
        // need not reload/recompute them. Unused on a cache HIT (derivation is skipped). Null = derive loads its own.
        FactGraphData? shapedGraph = null,
        IReadOnlyList<DerivedEffect>? unfilteredEffects = null
    )
    {
        if (!useCache)
        {
            return await DeriveGraphHazardFindingsAsync(context, rules, shapedGraph, unfilteredEffects);
        }

        using var cache = QueryCache.Open(rigDirectory: rigDirectory, storeKey: storeKey);
        var key = cache is null ? null : GraphHazardFindingsCacheKey(storeKey: storeKey, rulesHash: rulesHash);
        if (key is not null && cache!.Get(key) is { } blob && GraphHazardFindingsCodec.Decode(blob) is { } hit)
        {
            return hit;
        }

        var derived = await DeriveGraphHazardFindingsAsync(context, rules, shapedGraph, unfilteredEffects);
        if (key is not null)
        {
            TryCache(() => cache!.Put(key, GraphHazardFindingsCodec.Encode(derived)));
        }

        return derived;
    }

    // LANGUAGE-INTRINSIC effect providers, hidden by DEFAULT and restored by --intrinsic.
    //
    // These two scale with CODE VOLUME — every `new`, every `throw` — whereas all ~49 other providers scale
    // with what the code actually TALKS TO (a db, a cache, an http endpoint). On the MedDBase store that is
    // 243,391 alloc + 79,508 throw against ~30,619 of everything else: 91.3% of all effects, a ~1:10
    // signal-to-noise ratio on exactly the commands whose job is "show me what this reaches". Hiding them by
    // default is the single biggest usability lever in the effect surface.
    //
    // The membership is a DELIBERATE, CLOSED set of two — NOT a computed category. There is no clean
    // predicate: "derived from syntax rather than from a rule" would also capture `shared_state`, which is
    // load-bearing for the concurrency hazard detectors and must always stay visible. Resist growing this
    // set; a second default-hidden group is how a CLI grows a profile system, which was explicitly rejected
    // (a named tier's contents drift silently and every consumer has to re-learn them).
    //
    // NEVER silent: callers MUST disclose the suppressed count (EffectSelection.HiddenIntrinsic) so the
    // hidden state is impossible to encounter without also being told the flag that undoes it. That
    // disclosure is what pays for the terse flag name.
    internal static readonly HashSet<string> IntrinsicProviders = new(["alloc", "throw"], StringComparer.OrdinalIgnoreCase);

    // The result of effect selection: the surviving effects plus how many were withheld SOLELY because they
    // were intrinsic (i.e. they passed --only/--exclude and were dropped only by the default hiding). Zero
    // when --intrinsic was passed, when --only named an intrinsic provider, or when there were none.
    internal readonly record struct EffectSelection(IReadOnlyList<DerivedEffect> Effects, int HiddenIntrinsic);

    internal static bool IsIntrinsic(DerivedEffect e) => IntrinsicProviders.Contains(e.Provider);

    // True when a filter set explicitly names an intrinsic provider ("throw") or one of its operations
    // ("alloc:boxing"). Naming one in --only is an unambiguous request FOR it, so it implies --intrinsic —
    // `rig reaches X --only alloc` must do the obvious thing rather than return an empty list.
    internal static bool NamesIntrinsic(HashSet<string> tokens)
    {
        foreach (var token in tokens)
        {
            var provider = token.Contains(':', StringComparison.Ordinal) ? token[..token.IndexOf(':', StringComparison.Ordinal)] : token;
            if (IntrinsicProviders.Contains(provider))
            {
                return true;
            }
        }

        return false;
    }

    // Effect selection for reaches/tree/derive/impact: --only keeps just the listed effects, --exclude drops
    // them (exclude wins on overlap). Tokens match an effect's `provider` (e.g. "throw") or the precise
    // `provider:operation` (e.g. "llblgen:read").
    //
    // On top of that, language-intrinsic providers (see IntrinsicProviders) are withheld unless
    // includeIntrinsic is set OR --only explicitly named one. HiddenIntrinsic reports what that withholding
    // cost so the caller can disclose it.
    internal static EffectSelection SelectEffects(
        IReadOnlyList<DerivedEffect> effects,
        HashSet<string> only,
        HashSet<string> exclude,
        bool includeIntrinsic
    )
    {
        var hideIntrinsic = !includeIntrinsic && !NamesIntrinsic(only);
        if (only.Count == 0 && exclude.Count == 0 && !hideIntrinsic)
        {
            return new EffectSelection(effects, HiddenIntrinsic: 0);
        }

        var kept = new List<DerivedEffect>(effects.Count);
        var hidden = 0;
        foreach (var e in effects)
        {
            if ((only.Count > 0 && !InSet(e, only)) || InSet(e, exclude))
            {
                continue; // dropped by an EXPLICIT filter — not an intrinsic suppression, so not disclosed
            }

            if (hideIntrinsic && IsIntrinsic(e))
            {
                hidden++;
                continue;
            }

            kept.Add(e);
        }

        return new EffectSelection(kept, hidden);

        static bool InSet(DerivedEffect e, HashSet<string> set) => set.Contains(e.Provider) || set.Contains($"{e.Provider}:{e.Operation}");
    }

    // The one-line disclosure for withheld intrinsic effects, or "" when nothing was withheld.
    // Names the providers AND the flag, so the hidden state always teaches its own escape hatch.
    //
    // DELIBERATELY UNQUANTIFIED. It once carried the withheld COUNT, but that count is taken over the derived
    // effect set while reaches/tree display only the effects surviving a REACHABILITY JOIN — on a real seed it
    // read "57,513 hidden" next to a visible drop of 6,654, an 8x overstatement. Since this note is the entire
    // justification for the terse flag name, an inflated number is a defect in the safety mechanism itself.
    // An exact figure would need per-command join logic in four places; the machine-readable count lives in
    // `impact_summary`'s intrinsic_hidden instead, where it is exact (per-EP delta entries, no join involved).
    internal static string IntrinsicNote(int hiddenIntrinsic) =>
        hiddenIntrinsic <= 0
            ? ""
            : $"note: intrinsic effects ({string.Join(", ", IntrinsicProviders.OrderBy(p => p, StringComparer.Ordinal))}) are hidden by default — pass --intrinsic to include.";

    // Emit the disclosure to STDERR. Stderr (not stdout) so it never corrupts tsv/llm parsing while staying
    // visible to a human on every format — the same channel WarnUnknownFilterTokens uses. No-op when nothing
    // was withheld. Callers MUST call this wherever effects are rendered; see IntrinsicProviders on why.
    internal static void WriteIntrinsicNote(int hiddenIntrinsic, TextWriter errorWriter)
    {
        var note = IntrinsicNote(hiddenIntrinsic);
        if (note.Length > 0)
        {
            errorWriter.WriteLine(note);
        }
    }

    // The distinct provider strings (e.g. "http", "throw") known from the effective rule set.
    // A bare-provider --only/--exclude token is valid iff it appears here.
    internal static HashSet<string> KnownProviders(RuleSet rules) =>
        new(rules.Effects.Select(r => r.Provider).Append("alloc"), StringComparer.OrdinalIgnoreCase);

    // The distinct provider:operation strings (e.g. "http:GET", "throw:access_denied") known from
    // the effective rule set. A provider:operation token is valid iff it appears here.
    internal static HashSet<string> KnownProviderOps(RuleSet rules) =>
        new(
            rules.Effects.Select(r => $"{r.Provider}:{r.Operation}").Concat(["alloc:object", "alloc:array", "alloc:boxing"]),
            StringComparer.OrdinalIgnoreCase
        );

    // Warn to STDERR for any --only/--exclude token that cannot match any known provider or
    // provider:operation from the effective rule set. Non-fatal: the command still runs. A token is
    // "unknown" only when it matches neither a bare provider NOR any provider:op — so "http" is valid
    // when ANY http:* rule exists, and "http:GET" is valid iff that exact pair exists. Token-matching
    // mirrors ApplyEffectFilters exactly (case-insensitive, bare provider matches any op of that provider).
    internal static void WarnUnknownFilterTokens(HashSet<string> only, HashSet<string> exclude, RuleSet rules, TextWriter errorWriter)
    {
        if (only.Count == 0 && exclude.Count == 0)
        {
            return;
        }

        var knownProviders = KnownProviders(rules);
        var knownProviderOps = KnownProviderOps(rules);

        // Compute the sorted provider list once — only consumed when at least one unknown token is found.
        string? sortedProvidersLabel = null;

        foreach (var token in only.Concat(exclude))
        {
            if (!TokenIsKnown(token, knownProviders, knownProviderOps))
            {
                sortedProvidersLabel ??= string.Join(", ", knownProviders.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
                errorWriter.WriteLine(
                    $"warning: --only/--exclude token '{token}' matched no known effect (providers: {sortedProvidersLabel}). Run 'rig derive --list-providers' to see the full set."
                );
            }
        }
    }

    // A token is known when it is a bare provider ("http") present in the known-provider set, OR a
    // provider:operation pair ("http:GET") present in the known-provider-op set. Mirrors InSet above.
    private static bool TokenIsKnown(string token, HashSet<string> knownProviders, HashSet<string> knownProviderOps) =>
        token.Contains(':') ? knownProviderOps.Contains(token) : knownProviders.Contains(token);
}
