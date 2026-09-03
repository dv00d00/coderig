using System.CommandLine;
using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Effects;
using Rig.Cli.Rendering;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using static Rig.Cli.Caching.QueryCacheKeys;
using static Rig.Cli.Effects.EffectDerivation;
using static Rig.Cli.EntryPoints.EntryPointContext;
using static Rig.Cli.Graph.TraversalGraphLoader;
using static Rig.Cli.Rendering.EntryPointListRenderer;
using static Rig.Cli.Rendering.SymbolNameFormatter;

namespace Rig.Cli.Commands;

// `rig derive` — the stage-2 pass over facts (no Roslyn): re-derives effects, page/action entry points, and
// delegate/method-group handoff entry points from the reference index in a single command, one DB open, one
// rule load. Effects and entry points are matched against the same rig.rules.json the Roslyn pass uses
// (detectors are data, not code). `--format tsv` emits full-fidelity rows for tooling.
internal static class DeriveCommand
{
    internal static Command Build(TextWriter output, TextWriter error, string workingDirectory)
    {
        var rules = CommonOptions.Rules();
        var limit = CommonOptions.Limit(40);
        var only = CommonOptions.Only();
        var exclude = CommonOptions.Exclude();
        var intrinsic = CommonOptions.Intrinsic();
        var excludeNamespace = CommonOptions.ExcludeNamespace();
        var format = CommonOptions.Format();
        var store = CommonOptions.Store();
        var noGate = CommonOptions.NoGate();
        var noAmplification = CommonOptions.NoAmplification();
        var listProviders = new Option<bool>("--list-providers")
        {
            Description = "Print the known effect providers and provider:operation pairs from the effective rule set, then exit.",
        };
        var cmd = new Command(name: "derive", description: "Re-derive effects + entry points from facts (no Roslyn).")
        {
            rules,
            limit,
            only,
            exclude,
            intrinsic,
            excludeNamespace,
            format,
            store,
            noGate,
            noAmplification,
            listProviders,
        };
        cmd.SetAction(pr =>
            CommandGuard.RunGuardedAsync(
                workingDirectory,
                error,
                () =>
                    RunAsync(
                        new Options(
                            ExtraRules: CommonOptions.RulesOf(pr.GetValue(rules)),
                            Limit: pr.GetValue(limit),
                            Only: CommonOptions.FilterSet(pr.GetValue(only)),
                            Exclude: CommonOptions.FilterSet(pr.GetValue(exclude)),
                            Intrinsic: pr.GetValue(intrinsic),
                            ExcludeNamespaces: CommonOptions.NamespacePrefixes(pr.GetValue(excludeNamespace)),
                            Format: pr.GetValue(format),
                            Gate: !pr.GetValue(noGate),
                            Amplification: !pr.GetValue(noAmplification),
                            ListProviders: pr.GetValue(listProviders)
                        ),
                        new CommandIo(
                            new TextOutput(Output: output, Error: error),
                            new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: pr.GetValue(store))
                        )
                    )
            )
        );
        return cmd;
    }

    private sealed record Options(
        IReadOnlyList<string> ExtraRules,
        int Limit,
        HashSet<string> Only,
        HashSet<string> Exclude,
        bool Intrinsic,
        IReadOnlyList<string> ExcludeNamespaces,
        string? Format,
        bool Gate = true,
        // Amplification finding tier (looped_effect) — ON by default, `--no-amplification` flips it off and
        // reproduces the pre-tier output exactly (the type falls back to an anonymous count in the generic
        // Observations block). See CommonOptions.NoAmplification / HazardKinds for the fact-vs-judgment rationale.
        bool Amplification = true,
        bool ListProviders = false
    );

    private static async Task<int> RunAsync(Options opts, CommandIo io)
    {
        var tsv = CommonOptions.IsTsv(opts.Format);
        // #4: capture the resolved rule paths from the load, so the fingerprint below reuses them instead of
        // re-running the cascade merge (RulesFingerprint.ComputeFromPaths) just to re-discover the same paths.
        var rules = RuleSetLoader.Load(
            workingDirectory: io.WorkspaceLocation.WorkingDirectory,
            extraRules: opts.ExtraRules,
            loadedPaths: out var loadedRulePaths
        );

        // --list-providers: print the known provider / provider:operation tokens from the effective rule
        // set and exit. No store access required — the rule set is available after Load().
        if (opts.ListProviders)
        {
            var knownProviders = KnownProviders(rules);
            var knownProviderOps = KnownProviderOps(rules);
            var families = ProviderCatalog.EffectfulFamilies(rules);
            if (families.Count > 0)
            {
                // Families first: they are the coarsest token and the one a reader reaches for. Each line
                // names the providers it expands to, because that expansion IS the filter semantics.
                io.TextOutput.Output.WriteLine("Known effect families (use with --only / --exclude):");
                foreach (var family in families)
                {
                    io.TextOutput.Output.WriteLine($"  {family.Family} -> {string.Join(", ", family.Providers)}");
                }

                io.TextOutput.Output.WriteLine();
            }

            io.TextOutput.Output.WriteLine("Known effect providers (use with --only / --exclude):");
            foreach (var provider in knownProviders.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                io.TextOutput.Output.WriteLine($"  {provider}");
            }

            io.TextOutput.Output.WriteLine();
            io.TextOutput.Output.WriteLine("Known provider:operation pairs:");
            foreach (var op in knownProviderOps.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                io.TextOutput.Output.WriteLine($"  {op}");
            }

            return 0;
        }

        PrepareFilterTokens(only: opts.Only, exclude: opts.Exclude, rules: rules, errorWriter: io.TextOutput.Error);
        // F7: use the out-param overload so the resolved store dir is available for the StoreKey computation
        // below without a second ResolveReadStoreDir call (io:read ×7). Gated: schema fail-fast at open.
        var (context, rigDir) = await OpenReadContextGatedAsync(io.WorkspaceLocation, withStoreDir: true);
        await using var contextScope = context;

        // Deployment attribution (opt-in: only when deployments.json sits next to .rig). Empty (no-op) when
        // the config is absent; `error` is the log sink so config problems surface.
        var deployments = await LoadDeploymentsAsync(context, io.WorkspaceLocation.WorkingDirectory, io.TextOutput.Error);

        // Cache keys are computed HERE (moved up from below the graph load) because the warm-graph cache
        // keys on the same two axes the disk cache does — see WarmStore.
        // rigDir resolved above via the out-param OpenReadContext overload (F7).
        var storeKey = StoreKey(Path.Combine(rigDir, StoreLayout.DbFileName));
        var rulesHash = RulesFingerprint.ComputeFromPaths(loadedRulePaths); // #4: reuse the paths Load resolved.

        // Shaped graph: built once here and reused for both the handoff-EP classifier (F1 fix: avoids the
        // double-load in DeriveHandoffEntryPointsAsync's fallback path) and the event-cycle deriver below.
        // PoC: routed through the process-lifetime warm cache, so a RESIDENT host (`rig serve`) pays this
        // ~4.5s whole-store load once rather than once per request. A one-shot CLI run is unaffected.
        var shapedGraph = await Caching.WarmStore.GraphAsync(context: context, rules: rules, storeDir: rigDir, rulesHash: rulesHash);

        // Classified handoffs (background/timer/actor/event) shared by the listing, the origin-EP promotion,
        // and the TSV output — derived once. The total count yields the unclassified residual (a count, not a
        // listing), which is why this is loaded here rather than via DeriveEntryPointsAsync (which drops it).
        var handoffs = await Reads.DeriveHandoffEntryPointsAsync(
            context: context,
            limit: int.MaxValue,
            handoffRules: rules.Handoff,
            graph: shapedGraph
        );
        var classifiedHandoffs = handoffs.Where(h => h.Dispatcher is not null).ToList();
        var unclassifiedHandoffCount = handoffs.Count - classifiedHandoffs.Count;

        // Entry-point fact data is loaded up front: its base edges also feed the effect deriver's base-type
        // gates (e.g. clientpage_proxy = declaring type derives MedDBase.Pages.ProxyBase).
        var epData = await Reads.LoadFactEntryPointDataAsync(context);

        // (file,line) -> handler DocID, so each entry-point line/row can carry the queryable FQN beside its
        // slash route. Built once here for both the tsv and the human entry-point listings below.
        var docIdBySite = MethodDocIdBySite(epData);

        // --- Effects (whole-store, hazard-augmented): the field-fed shared_state arms + the race_window/
        //     dual_write/thread_local_context post-pass, cached store+rules-keyed and SHARED with `tree
        //     --hazards` (an effect is a per-method fact, EP- and mode-independent). A reindex or rule edit
        //     misses the cache and recomputes, so hazards stay query-side data. ---
        // storeKey / rulesHash computed above (moved up for the warm-graph cache key).
        var effects = await LoadOrDeriveHazardEffectsAsync(
            context: context,
            rigDirectory: rigDir,
            storeKey: storeKey,
            rulesHash: rulesHash,
            rules: rules,
            useCache: true,
            epData: epData, // #1b: reuse the EP data loaded above; skip the redundant load on a cache miss.
            gate: opts.Gate // shared_state:read write-pairing gate (on by default; --no-gate flips off).
        );
        // cache_coherence (below) MUST see the PRE-filter effects: --exclude cache would otherwise hide the
        // cache:invalidate companions and manufacture false missing-invalidation findings. Capture the
        // unfiltered list before --only/--exclude is applied, and feed THAT to the correlation deriver.
        // The same reasoning covers the DEFAULT hiding of intrinsic providers (alloc/throw): it is applied
        // here, on the presentation side only, so no detector's input is narrowed by it.
        var unfilteredEffects = effects;
        var selection = SelectEffects(effects, only: opts.Only, exclude: opts.Exclude, includeIntrinsic: opts.Intrinsic);
        effects = selection.Effects;
        WriteIntrinsicNote(selection.HiddenIntrinsic, io.TextOutput.Error);

        // --- The GRAPH-TIER hazard sources (event_cycle + cache_coherence + static_init_capture): the three
        //     hazards that are NOT effect-attached observations — each is a property of the SHAPED call graph
        //     (event_cycle's delivery-edge cycle detection, cache_coherence's forward-closure correlation) or
        //     the static-field universe, so they are derived OVER the graph, not folded into
        //     HazardFindings(effects). Extracted into LoadOrDeriveGraphHazardFindingsAsync so `tree --hazards`
        //     shares the SAME derivation (and the same store+rules-keyed cache) — derive once, reuse per EP.
        //     The shapedGraph + unfilteredEffects this command already holds are threaded through so the
        //     cache-miss path neither reloads the graph nor recomputes the effects. The union ORDER (event_cycle,
        //     cache_coherence, static_init_capture) and the opt-in gates are preserved inside the helper, so
        //     derive's output is byte-identical. cache_coherence sees the PRE-`--only/--exclude` effects. ---
        var graphHazardFindings = await LoadOrDeriveGraphHazardFindingsAsync(
            context: context,
            rigDirectory: rigDir,
            storeKey: storeKey,
            rulesHash: rulesHash,
            rules: rules,
            useCache: true,
            shapedGraph: shapedGraph,
            unfilteredEffects: unfilteredEffects
        );

        // Both hazard sources unioned: the over-effects pattern findings + the graph-tier findings (event_cycle,
        // cache_coherence, static_init_capture, in that order). Fed to BOTH the tsv `hazard` emission AND the
        // rendered Hazards section so neither view misses a source.
        var allHazards = new List<HazardFinding>(HazardFindings(effects));
        allHazards.AddRange(graphHazardFindings);

        // --exclude-namespace: drop hazard findings whose enclosing DocID namespace starts with any of the
        // given prefixes (case-insensitive). Applied BEFORE both tsv and human output for consistency.
        // Effects are unaffected — this filter touches only the hazard surface.
        if (opts.ExcludeNamespaces.Count > 0)
        {
            allHazards.RemoveAll(h => CommonOptions.MatchesExcludedNamespace(h.Enclosing, opts.ExcludeNamespaces));
        }

        // --- AMPLIFICATION (looped_effect): the second finding tier — a structural FACT (this effect is inside
        //     an iteration context), rendered in its own section broken down by provider:operation. Scoped by the
        //     rules-declared network-crossing provider set (AmplificationScope) and suppressed wholesale by
        //     --no-amplification, in which case the type falls back into the generic Observations block below and
        //     the output is byte-identical to the pre-tier behaviour. Built from the SAME filtered effect list the
        //     Observations block reads, so --only/--exclude/--intrinsic apply consistently. ---
        var amplificationFindings = opts.Amplification ? AmplificationFindings(effects, rules.Observations.AmplificationOrEmpty) : [];
        if (opts.ExcludeNamespaces.Count > 0)
        {
            amplificationFindings = amplificationFindings
                .Where(f => !CommonOptions.MatchesExcludedNamespace(f.Enclosing, opts.ExcludeNamespaces))
                .ToList();
        }

        // --- cross_method_amplification: the PRESENCE correlation over iteration-fanout pseudo-events (a read
        //     reachable at or beneath a call issued once per element). Opt-in — only when the
        //     `crossMethodAmplification` rule section is present, mirroring cacheCoherence. Emitted as its OWN tsv
        //     row type, never as a HazardFinding: step 1 is a dataset at (anchor x witness) grain, and folding
        //     it into the Hazards view would swamp it and re-tune `rig impact`'s hazard deltas. Sees the
        //     UNFILTERED effects for the same reason cache_coherence does — --only/--exclude is presentation.
        var crossMethodPairs = rules.CrossMethodAmplification is { } xm
            ? CrossMethodAmplificationDataset.Pairs(
                invocations: await Reads.LoadInvocationRefsAsync(context),
                graph: shapedGraph,
                effects: unfilteredEffects,
                observationRules: rules.Observations,
                rule: xm
            )
            : [];
        var crossMethodRows = CrossMethodAmplificationDataset.TsvRows(crossMethodPairs);
        // The DISPLAYED grain (tier 3, HazardKinds.CrossMethodAmplification): one finding per anchor call site, CHA
        // fan-out collapsed to the nearest-depth witness. The human view prints these; the pair rows above stay
        // the machine dataset.
        var crossMethodAnchors = CrossMethodAmplificationDataset.AnchorFindings(crossMethodPairs);

        // Machine-readable mode: emit full-fidelity rows (full DocIDs/paths) for tooling that joins
        // effects/entry points against the call graph. `rig derive --format tsv`.
        if (tsv)
        {
            // TSV column reference (tab-separated; one row per record):
            //   effect      \t provider \t operation \t resource \t enclosing \t file \t line \t observations(csv of Type)
            //               \t mechanism \t cardinality \t shallowSizeBytes \t sizeConfidence \t sizeBasis
            //   hazard      \t type \t confidence \t reason \t cell/context \t enclosing \t file \t line \t detail
            //   amplification \t type \t confidence \t reason \t iterationContext \t enclosing \t file \t line \t detail \t provider \t operation
            //   entrypoint  \t kind \t method \t route \t file \t line \t services(csv) \t activeServices(csv) \t fqn
            // The `effect` row's observations column keeps the comma-joined Type list (back-compat: existing
            // consumers); the `hazard` row carries the full per-hazard evidence (confidence / reason / detail)
            // the effect row can't — one row per hazard observation on an effect (see HazardKinds for the set).
            foreach (var e in effects)
            {
                var observations = string.Join(',', (e.Observations ?? []).Select(o => o.Type));
                io.TextOutput.Output.WriteLine(
                    $"effect\t{e.Provider}\t{e.Operation}\t{e.ResourceType}\t{e.EnclosingSymbolId}\t{e.FilePath}\t{e.Line}\t{observations}\t{e.Mechanism}\t{e.Cardinality}\t{e.ShallowSizeBytes}\t{e.SizeConfidence}\t{e.SizeBasis}"
                );
            }

            foreach (var h in allHazards)
            {
                io.TextOutput.Output.WriteLine(HazardTsvRow(h));
            }

            // The AMPLIFICATION rows (own row type, never `hazard` — downstream consumers and the skill doc
            // treat `hazard` as exactly the hazard set, and an amplification row additionally carries the
            // effect's provider/operation, which a hazard row has no column for). Empty under
            // --no-amplification, so a consumer's `hazard`-filtered pipeline is bit-for-bit unaffected either way.
            foreach (var a in amplificationFindings)
            {
                io.TextOutput.Output.WriteLine(AmplificationTsvRow(a));
            }

            // See CrossMethodAmplificationDataset for the column reference. Empty unless the rule section is present.
            foreach (var row in crossMethodRows)
            {
                io.TextOutput.Output.WriteLine(row);
            }

            var tsvEps = FactEntryPointDeriver.Derive(epData, rules.EntryPoints, rules.ClassInheritance);
            // Trailing columns (comma-joined, empty when no deployments.json): `service` = the hosts that
            // LOAD the EP (link its code); `activeService` = the subset it is ACTIVE-IN after the capability
            // gate (== service when the EP is ungated). `service` is kept for back-compat; tooling that wants
            // runs-here filters on the new `activeService` column.
            foreach (var ep in tsvEps.Concat(PromoteHandoffOrigins(classifiedHandoffs, tsvEps)))
            {
                var loaded = deployments.ServicesForFile(ep.FilePath);
                var active = deployments.ActiveServices(loadedServices: loaded, requires: ep.Requires);
                io.TextOutput.Output.WriteLine(
                    $"entrypoint\t{ep.Kind}\t{ep.Method}\t{ep.Route}\t{ep.FilePath}\t{ep.Line}\t{string.Join(',', loaded)}\t{string.Join(',', active)}\t{FqnOrRoute(route: ep.Route, filePath: ep.FilePath, line: ep.Line, docIdBySite: docIdBySite)}"
                );
            }
            return 0;
        }

        io.TextOutput.Output.WriteLine($"Effects re-derived from facts: {effects.Count}");
        foreach (var group in effects.GroupBy(e => (e.Provider, e.Operation)).OrderByDescending(g => g.Count()))
        {
            io.TextOutput.Output.WriteLine($"{Indent.L1}{group.Key.Provider} {group.Key.Operation}: {group.Count()}");
            foreach (var e in group.Take(opts.Limit / 8 + 1))
            {
                io.TextOutput.Output.WriteLine(
                    $"{Indent.L3}{ShortName(e.ResourceType)}{AllocationEvidenceFormatter.Suffix(e)}  <- {ShortName(e.EnclosingSymbolId)}  {ShortenPath(e.FilePath)}:{e.Line}"
                );
            }
        }

        // --- Hazards: the higher-order findings that match PATTERNS over effects (race_window / lazy_init_race /
        //     n_plus_1 / unserializable_payload — see HazardKinds). Promoted out of the generic observations
        //     block into their own section with per-type, per-confidence counts + sampled sites. ---
        WriteHazards(io.TextOutput.Output, allHazards, opts.Limit);

        // --- Amplification: the FACT tier, rendered AFTER Hazards (judgments first — a hazard is what a reviewer
        //     must decide about; amplification is inventory to scan). Empty-safe: a no-op under
        //     --no-amplification or when nothing in scope is looped. ---
        WriteAmplification(io.TextOutput.Output, amplificationFindings, opts.Limit);

        // --- Cross-method N+1 (tier 3): the ANCHOR-grain finding view — one row per looped call site, nearest
        //     witness as evidence, depth-tiered confidence. Terse by design (grouped by witness provider:op with
        //     per-anchor lines capped by --limit); the full (anchor x witness) dataset stays a tsv artifact. ---
        if (crossMethodAnchors.Count > 0)
        {
            var output = io.TextOutput.Output;
            output.WriteLine();
            // Ubiquitous language: the finding is AMPLIFICATION of whatever effects the rules gate names —
            // network calls by default, occasionally allocs — never just "reads"/"writes" (misleading).
            output.WriteLine($"Cross-method amplification ({crossMethodAnchors.Count} looped call site(s) with a gated effect beneath):");
            foreach (
                var group in crossMethodAnchors
                    .GroupBy(a => $"{a.WitnessProvider}:{a.WitnessOperation}")
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
            )
            {
                output.WriteLine($"{Indent.L1}{group.Key}  ({group.Count()} anchor(s))");
                foreach (var a in group.OrderBy(x => x.WitnessDepth).ThenBy(x => x.FilePath, StringComparer.Ordinal).Take(opts.Limit))
                {
                    output.WriteLine(
                        // Confidence AND Evidence, because depth alone undersold the strong rows: a d0
                        // unconditional call reached with no dispatch inference printed exactly like a d0
                        // witness found through a name-guessed virtual hop. Evidence names which one.
                        $"{Indent.L2}{ShortenPath(a.FilePath)}:{a.Line}  🔁[{a.IterationKind}] -> {ShortName(a.WitnessResource)}  d{a.WitnessDepth} [{a.Confidence}·{a.Evidence}]"
                    );
                }

                if (group.Count() > opts.Limit)
                {
                    output.WriteLine(
                        $"{Indent.L2}… {group.Count() - opts.Limit} more (raise --limit, or --format tsv for the full dataset)"
                    );
                }
            }

            output.WriteLine($"{Indent.L1}({crossMethodRows.Count} (anchor x witness) pair(s) in the tsv dataset)");
        }

        // --- STRUCTURAL observations attached to effects (parallel_fanout / lock_held_across_effect /
        //     transaction_spans_effect, P2b) — context facts with no section of their own, reported as bare
        //     counts. Anything DISPLAYED as a finding is excluded here so nothing is double-counted: the hazard
        //     kinds (Hazards section) always, and an in-scope looped_effect (Amplification section) when the tier
        //     is on. An OUT-of-scope looped_effect still lands here — that is what keeps the narrowed default
        //     lossless rather than hiding effects. ---
        var observationGroups = GenericObservationGroups(
            effects,
            amplificationScope: rules.Observations.AmplificationOrEmpty,
            amplification: opts.Amplification
        );

        if (observationGroups.Count > 0)
        {
            io.TextOutput.Output.WriteLine();
            io.TextOutput.Output.WriteLine($"Observations on effects: {observationGroups.Sum(g => g.Count)}");
            foreach (var group in observationGroups)
            {
                io.TextOutput.Output.WriteLine($"{Indent.L1}{group.Type}: {group.Count}");
            }
        }

        // --- Page + action entry points (fact-based BFS + attribute-ref detection) ---
        // epData was loaded above (shared with the effect deriver's base-type gates).
        var derivedEps = FactEntryPointDeriver.Derive(epData, rules.EntryPoints, rules.ClassInheritance);

        io.TextOutput.Output.WriteLine();
        io.TextOutput.Output.WriteLine($"Entry points re-derived from facts: {derivedEps.Count}");
        var perKindSample = opts.Limit / 4 + 1;
        foreach (var kindGroup in derivedEps.GroupBy(e => e.Kind, StringComparer.Ordinal).OrderByDescending(g => g.Count()))
        {
            io.TextOutput.Output.WriteLine($"{Indent.L1}{kindGroup.Key}: {kindGroup.Count()}");
            foreach (var e in kindGroup.Take(perKindSample))
            {
                WriteEntryPointLine(
                    io.TextOutput.Output,
                    deployments,
                    route: e.Route,
                    filePath: e.FilePath,
                    line: e.Line,
                    requires: e.Requires,
                    fqn: FqnOrRoute(route: e.Route, filePath: e.FilePath, line: e.Line, docIdBySite: docIdBySite)
                );
            }

            WriteSampleTruncationNote(io.TextOutput.Output, total: kindGroup.Count(), shown: perKindSample, kind: kindGroup.Key);
        }

        // --- Classified handoff entry points (Phase 1/3): dispatcher-consumed delegates, promoted to
        //     execution origins by kind (background/timer/actor/event), with the dispatcher + the
        //     registration site. The unclassified-methodGroup residual is collapsed to a count (it was a
        //     4,503-entry firehose). Each emits an `async_handoff` observation at its registration.
        var origins = PromoteHandoffOrigins(classifiedHandoffs, derivedEps);
        io.TextOutput.Output.WriteLine();
        io.TextOutput.Output.WriteLine(
            $"Handoff entry points (classified): {classifiedHandoffs.Count}  "
                + $"(promoted origins after dedup: {origins.Count}; unclassified methodGroup residual: {unclassifiedHandoffCount})"
        );
        foreach (var kindGroup in classifiedHandoffs.GroupBy(h => h.Kind, StringComparer.Ordinal).OrderByDescending(g => g.Count()))
        {
            io.TextOutput.Output.WriteLine($"{Indent.L1}{kindGroup.Key}: {kindGroup.Count()}");
            foreach (var h in kindGroup.Take(perKindSample))
            {
                var tag = deployments.IsEmpty ? "" : $"  {EntryPointRenderer.DeployTag(deployments, h.FilePath, h.Requires)}";
                io.TextOutput.Output.WriteLine(
                    $"{Indent.L3}{ShortName(h.Target)}  ⤳ via {h.Dispatcher}{tag}\n{Indent.L5}registered in {ShortName(h.RegisteredIn)}  {ShortenPath(h.FilePath)}:{h.Line}  [async_handoff]"
                );
            }
            WriteSampleTruncationNote(io.TextOutput.Output, total: kindGroup.Count(), shown: perKindSample, kind: kindGroup.Key ?? "");
        }

        // The headline: entry points per deployed service (the summary table). An EP counts in every service
        // whose process loads it (shared libraries fan out to many hosts — see the chip counts).
        if (!deployments.IsEmpty)
        {
            WriteServiceSummary(
                derivedEps.Concat(origins).Select(e => (e.Kind, (string?)e.FilePath, e.Requires)),
                deployments,
                io.TextOutput.Output
            );
        }

        return 0;
    }

    // One hazard finding flattened from an effect: the hazard observation (Type/Confidence/Reason/Context/
    // Detail) joined to the effect's location (enclosing method + file:line) so it can be rendered or emitted
    // without re-walking the effect. Context is the hazard's own context (the cell for race_window, the loop
    // identifier for n_plus_1, the unsupported type for unserializable_payload); Detail is the hazard's detail
    // (the paired-read site for race_window, the loop detail for n_plus_1, the payload args for serialization).
    internal sealed record HazardFinding(
        string Type,
        string Confidence,
        string Reason,
        string Context,
        string Detail,
        string Enclosing,
        string FilePath,
        int Line,
        // The producing effect's provider / operation. Carried ONLY for the AMPLIFICATION tier, whose whole
        // point is provider-agnostic visibility: a flat "looped_effect: 1531" is useless, so the section and the
        // tsv row group by `provider:operation`. Defaulted empty and ABSENT from HazardTsvRow, so every hazard
        // construction site and every `hazard` row stays byte-identical (a hazard's identity is its Type + cell,
        // not the provider — see the record's use in the Hazards view).
        string Provider = "",
        string Operation = ""
    )
    {
        // The amplification grouping key: "provider:operation", or just the provider when the operation is
        // absent (defensive — every derived effect carries both today).
        public string ProviderOperation => Operation.Length > 0 ? $"{Provider}:{Operation}" : Provider;
    }

    // Flatten every HAZARD observation (HazardKinds) across the effects into one finding per (effect,
    // observation), carrying the effect's site. STRUCTURAL observations are excluded — they stay in the
    // generic Observations block. Pure + internal so the Hazards view + tsv can be unit-tested without a store.
    internal static IReadOnlyList<HazardFinding> HazardFindings(IReadOnlyList<DerivedEffect> effects)
    {
        var findings = new List<HazardFinding>();
        foreach (var e in effects)
        {
            foreach (var o in e.Observations ?? [])
            {
                if (!HazardKinds.IsHazard(o.Type))
                {
                    continue;
                }

                findings.Add(
                    new HazardFinding(
                        Type: o.Type,
                        Confidence: o.Confidence,
                        Reason: o.Reason,
                        Context: o.Context,
                        Detail: o.Detail,
                        Enclosing: e.EnclosingSymbolId ?? "",
                        FilePath: e.FilePath,
                        Line: e.Line
                    )
                );
            }
        }

        return findings;
    }

    // Flatten every in-scope AMPLIFICATION observation (HazardKinds.Amplification — looped_effect) across the
    // effects into one finding per (effect, observation), carrying BOTH the effect's site and its
    // provider/operation. Two gates, in this order:
    //   * the TYPE must be in the amplification tier (so a hazard observation can never leak in here and be
    //     counted twice — the tiers are disjoint by construction, and this is where that is enforced);
    //   * the EFFECT's provider:operation must be in the rules-declared display scope (AmplificationScope) —
    //     the shipped default is network-crossing providers only.
    // Pure + internal so the tier, the scope gate, and the rendering are unit-testable without a store.
    internal static IReadOnlyList<HazardFinding> AmplificationFindings(
        IReadOnlyList<DerivedEffect> effects,
        IReadOnlyList<FactAmplificationRule> scope
    )
    {
        var findings = new List<HazardFinding>();
        foreach (var e in effects)
        {
            if (!AmplificationScope.Includes(scope, e.Provider, e.Operation))
            {
                continue;
            }

            foreach (var o in e.Observations ?? [])
            {
                if (!HazardKinds.IsAmplification(o.Type))
                {
                    continue;
                }

                findings.Add(
                    new HazardFinding(
                        Type: o.Type,
                        Confidence: o.Confidence,
                        Reason: o.Reason,
                        Context: o.Context, // the iteration context (foreach / for / a rule-declared enumerating call)
                        Detail: o.Detail,
                        Enclosing: e.EnclosingSymbolId ?? "",
                        FilePath: e.FilePath,
                        Line: e.Line,
                        Provider: e.Provider,
                        Operation: e.Operation
                    )
                );
            }
        }

        return findings;
    }

    // The generic "Observations on effects" groups: every effect observation that gets NO section of its own,
    // as (Type, Count) ordered by count desc. Excluded are the hazard kinds (always — they are in the Hazards
    // section) and, when the amplification tier is ON, the amplification kinds on IN-SCOPE effects (they are in
    // the Amplification section). An out-of-scope looped effect deliberately remains counted here, so narrowing
    // the display scope never makes a looped effect disappear from the output entirely. With `amplification:
    // false` the predicate collapses to the original `!IsHazard`, which is what makes --no-amplification
    // byte-identical to the pre-tier output. Pure + internal so both branches are unit-testable.
    internal static IReadOnlyList<(string Type, int Count)> GenericObservationGroups(
        IReadOnlyList<DerivedEffect> effects,
        IReadOnlyList<FactAmplificationRule> amplificationScope,
        bool amplification
    ) =>
        effects
            .SelectMany(e => (e.Observations ?? []).Select(o => (Effect: e, Observation: o)))
            .Where(x =>
                !HazardKinds.IsHazard(x.Observation.Type)
                && !(
                    amplification
                    && HazardKinds.IsAmplification(x.Observation.Type)
                    && AmplificationScope.Includes(amplificationScope, x.Effect.Provider, x.Effect.Operation)
                )
            )
            .GroupBy(x => x.Observation.Type, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .Select(g => (Type: g.Key, Count: g.Count()))
            .ToList();

    // Map each graph-tier event_cycle into a HazardFinding so it flows through the SAME Hazards view + tsv
    // split as the over-effects findings. event_cycle is NOT effect-attached, so it has no single owning
    // effect: the REPRESENTATIVE site is the first delivery edge's Caller / FilePath / Line (the raise that
    // closes the loop — the most actionable anchor). Context is the cycle size; Detail enumerates every
    // delivery edge with its dispatcher and site so the full evidence survives into the tsv `hazard` row.
    // Pure + internal so the mapping is unit-testable without a store.
    internal static IReadOnlyList<HazardFinding> EventCycleFindings(IReadOnlyList<EventCycle> cycles)
    {
        var findings = new List<HazardFinding>(cycles.Count);
        foreach (var cycle in cycles)
        {
            var representative = cycle.DeliveryEdges[0];
            var detail = string.Join(
                ", ",
                cycle.DeliveryEdges.Select(e => $"{e.Caller}->{e.Callee}@{e.FilePath}:{e.Line}[{e.HandoffDispatcher}]")
            );
            findings.Add(
                new HazardFinding(
                    Type: FactCycleDeriver.EventCycleType,
                    Confidence: cycle.Confidence,
                    Reason: "feedback_cycle_over_delivery_edges",
                    Context: $"{cycle.Members.Count} methods",
                    Detail: detail,
                    Enclosing: representative.Caller,
                    FilePath: representative.FilePath,
                    Line: representative.Line
                )
            );
        }

        return findings;
    }

    // Map each cache_coherence correlation finding (FR-7) into a HazardFinding so it flows through the SAME
    // Hazards view + tsv split as the other sources. Like event_cycle it is NOT effect-attached, so it has no
    // owning effect: the SITE is the anchor (bulk-write) enclosing Method / FilePath / Line. Context is the
    // normalized resource key (the cached entity whose cache may go stale); Detail is empty (the entity is the
    // whole signal). Confidence rides from the finding's Certainty token: a DECLARED-contract entity is "high",
    // an entity merely inferred from cache reads is "medium", and an untiered finding defaults to "medium" (it
    // reads as "verify", like the other inferred hazards). Pure + internal so the mapping is unit-testable
    // without a store.
    internal static IReadOnlyList<HazardFinding> CacheCoherenceFindings(IReadOnlyList<CorrelationFinding> findings)
    {
        var mapped = new List<HazardFinding>(findings.Count);
        foreach (var f in findings)
        {
            mapped.Add(
                new HazardFinding(
                    Type: HazardKinds.CacheCoherence,
                    Confidence: f.Certainty ?? "medium",
                    Reason: "bulk_write_without_cache_invalidation",
                    Context: f.ResourceKey,
                    Detail: "",
                    Enclosing: f.Method,
                    FilePath: f.FilePath,
                    Line: f.Line
                )
            );
        }

        return mapped;
    }

    // Map each static_init_capture finding into a HazardFinding so it flows through the SAME Hazards view +
    // tsv split as the other sources. Like cache_coherence it is NOT effect-attached: the SITE is the
    // enclosing STATIC field (Method) + the read's FilePath/Line. Context is the mutable source read (the
    // config/Settings value frozen at type-init); Detail is empty (the source is the whole signal).
    // Confidence is "medium" — the shape is grounded (a static-field-init read of a mutable source) but
    // whether the staleness is operationally harmful depends on whether the value changes at runtime, so it
    // reads as "verify". Pure + internal so the mapping is unit-testable without a store.
    internal static IReadOnlyList<HazardFinding> StaticInitCaptureFindings(IReadOnlyList<StaticInitCaptureFinding> findings)
    {
        var mapped = new List<HazardFinding>(findings.Count);
        foreach (var f in findings)
        {
            mapped.Add(
                new HazardFinding(
                    Type: HazardKinds.StaticInitCapture,
                    Confidence: "medium",
                    Reason: "config_read_frozen_in_static_field_init",
                    Context: f.ResourceKey,
                    Detail: "",
                    Enclosing: f.Method,
                    FilePath: f.FilePath,
                    Line: f.Line
                )
            );
        }

        return mapped;
    }

    // Build the tiered in-scope key map for the cache_coherence correlation instance: resource key -> certainty
    // token. Two sources, declared wins on overlap:
    //   * MEDIUM tier (DISCOVERY): every entity the code READS from its cache — the effect named by the rule's
    //     `discoveryRead` — normalized by that rule's `stripSuffix` to the bare entity name. Keyed off READS —
    //     NOT off the companion (invalidation) — on purpose: if an accidental merge deletes every cache bust,
    //     the reads remain, so the entity stays in scope and the missing-invalidation still flags. Keying off
    //     the companion would self-silence the detector exactly when the bug is worst. A rule with no
    //     `discoveryRead` runs the DECLARED tier alone (no discovery), rather than falling back to a built-in
    //     provider name — the cache-read effect is one project's vocabulary, so it lives in the rule.
    //   * HIGH tier (DECLARED CONTRACT): every entity named in the rule's `cachedEntities`. This is the
    //     intentional invariant ("X is cached and MUST be invalidated"); declared overwrites a discovered key.
    //
    // The stripped suffixes matter because one cache read resolves to a MIX of resource forms (a `*Cache` type
    // via resource:receiver_type and the bare entity via a generic factory's resource:type_argument); stripping
    // both lands them on the same key as the anchor (from a `*EntityCollection`) and the companion.
    internal static IReadOnlyDictionary<string, string> BuildCacheInScopeKeys(
        FactCacheCoherenceRule rule,
        IReadOnlyList<DerivedEffect> effects
    )
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        if (rule.DiscoveryRead is { } read)
        {
            var readNormalize = new NormalizeSpec(SimpleTypeName: true, StripSuffix: read.StripSuffix);
            foreach (var e in effects)
            {
                if (
                    !string.Equals(e.Provider, read.Provider, StringComparison.Ordinal)
                    || !string.Equals(e.Operation, read.Operation, StringComparison.Ordinal)
                )
                {
                    continue;
                }

                var key = ResourceKey.Of(e.ResourceType, readNormalize);
                if (key is not null && !keys.ContainsKey(key))
                {
                    keys[key] = "medium";
                }
            }
        }

        foreach (var entity in rule.CachedEntities)
        {
            keys[entity] = "high"; // declared wins on overlap
        }

        return keys;
    }

    // The tsv `hazard` row for one finding (see the column reference in RunAsync): the full per-hazard
    // evidence — type, confidence, reason, cell/context, enclosing, file, line, detail — the comma-joined
    // `effect`-row observation Type list can't carry. Pure + internal so the column contract is unit-testable.
    internal static string HazardTsvRow(HazardFinding h) =>
        $"hazard\t{TsvCell.Clean(h.Type)}\t{TsvCell.Clean(h.Confidence)}\t{TsvCell.Clean(h.Reason)}\t{TsvCell.Clean(h.Context)}\t{TsvCell.Clean(h.Enclosing)}\t{TsvCell.Clean(h.FilePath)}\t{h.Line}\t{TsvCell.Clean(h.Detail)}";

    // The tsv `amplification` row: the `hazard` column contract verbatim (so one parser handles both), plus the
    // two columns amplification exists for — the effect's PROVIDER and OPERATION. A DISTINCT row type on purpose:
    // `hazard` is the closed hazard set for every downstream consumer and the skill doc, and column-1 filtering
    // (`awk -F'\t' '$1=="amplification"'`) is the documented idiom. Pure + internal so the contract is pinned.
    internal static string AmplificationTsvRow(HazardFinding a) =>
        $"amplification\t{TsvCell.Clean(a.Type)}\t{TsvCell.Clean(a.Confidence)}\t{TsvCell.Clean(a.Reason)}\t{TsvCell.Clean(a.Context)}\t{TsvCell.Clean(a.Enclosing)}\t{TsvCell.Clean(a.FilePath)}\t{a.Line}\t{TsvCell.Clean(a.Detail)}\t{TsvCell.Clean(a.Provider)}\t{TsvCell.Clean(a.Operation)}";

    // Confidence tiers in disclosure order (high first), so a per-type breakdown reads worst-first.
    private static readonly string[] ConfidenceOrder = ["high", "medium", "low"];

    // The Hazards section: per hazard type, findings are DEDUPED by ENCLOSING METHOD and rendered as ONE line
    // per method with a ×N site count (N = sites for that method + type), ordered worst-first (highest
    // confidence among the method's findings, then by site count desc). The per-type header reports BOTH
    // counts, e.g. "race_window: 88 site(s) across 31 method(s)". The confidence-tier breakdown uses SITE
    // counts (consistent with the pre-dedup behavior). NOTE: dedup is HUMAN-ONLY — the `--format tsv` `hazard`
    // rows (HazardTsvRow, one per finding/site) stay per-site for tooling. Types are ordered by total site
    // count desc (busiest hazard first), ties broken by type name. Pure render over a finding list — internal so
    // it can be exercised against a synthetic finding set without wiring a full DeriveCommand run.
    internal static void WriteHazards(TextWriter output, IReadOnlyList<HazardFinding> findings, int limit)
    {
        if (findings.Count == 0)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine($"Hazards (pattern findings): {findings.Count}");
        WriteFindingGroups(output, findings.GroupBy(f => f.Type, StringComparer.Ordinal), limit);
    }

    // The AMPLIFICATION section: the same per-group rendering as Hazards (so the two read consistently), grouped
    // by the effect's `provider:operation` instead of by finding TYPE — because the type is always looped_effect,
    // and a flat "looped_effect: 1531" line answers nothing. The provider:operation breakdown is the whole point
    // of promoting this tier: a looped `http:POST` must be as visible as a looped `llblgen:read`.
    //
    // Deliberately NOT called "hazards": looped_effect is a structural FACT (the effect is inside an iteration
    // context, soundly) and not a judgment that anything is wrong — the header says "inventory" so a reader
    // triages it as such. See HazardKinds for the tier rationale. Empty-safe.
    internal static void WriteAmplification(TextWriter output, IReadOnlyList<HazardFinding> findings, int limit)
    {
        if (findings.Count == 0)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine($"Amplification (looped effects — structural inventory): {findings.Count}");
        WriteFindingGroups(output, findings.GroupBy(f => f.ProviderOperation, StringComparer.Ordinal), limit);
    }

    // The shared per-group body of the Hazards + Amplification sections, factored so the two sections cannot
    // drift in shape: for each group (ordered by site count desc, ties by key) a header line with site + method
    // counts and the confidence-tier breakdown, then one sampled row per ENCLOSING METHOD (deduped, worst-first,
    // ×N when a method holds several sites), then the truncation note. The only thing that varies between the
    // sections is the GROUPING KEY the caller chose — hazard type vs the effect's provider:operation.
    private static void WriteFindingGroups(TextWriter output, IEnumerable<IGrouping<string, HazardFinding>> groups, int limit)
    {
        // perTypeSample caps the number of METHOD rows shown (not sites).
        var perTypeSample = limit / 8 + 1;
        var byType = groups.OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal);
        foreach (var typeGroup in byType)
        {
            // Site-level tier counts (for the per-type breakdown parenthetical — unchanged from pre-dedup).
            var tierCounts = typeGroup
                .GroupBy(f => f.Confidence, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var onlyHigh = tierCounts.Count == 1 && tierCounts.ContainsKey("high");
            var breakdown = onlyHigh
                ? ""
                : " ("
                    + string.Join(
                        ", ",
                        ConfidenceOrder
                            .Where(tierCounts.ContainsKey)
                            .Select(t => $"{t} {tierCounts[t]}")
                            // Any non-standard tier (shouldn't happen) is appended after the known ones.
                            .Concat(
                                tierCounts
                                    .Keys.Where(k => !ConfidenceOrder.Contains(k, StringComparer.Ordinal))
                                    .OrderBy(k => k, StringComparer.Ordinal)
                                    .Select(k => $"{k} {tierCounts[k]}")
                            )
                    )
                    + ")";

            // Dedup by enclosing method: one row per method, showing ×N when the method has multiple sites.
            // Ordered worst-first: highest confidence tier first (high < medium < low), then site count desc.
            var byMethod = typeGroup
                .GroupBy(f => f.Enclosing, StringComparer.Ordinal)
                .Select(g =>
                {
                    var worstConfidence = g.Select(f => f.Confidence).OrderBy(HazardConfidenceRank).First();
                    // Representative site: the one with the worst confidence (or first alphabetically on tie).
                    var representative = g.OrderBy(f => HazardConfidenceRank(f.Confidence))
                        .ThenBy(f => f.FilePath, StringComparer.Ordinal)
                        .ThenBy(f => f.Line)
                        .First();
                    return (Representative: representative, SiteCount: g.Count(), WorstConfidence: worstConfidence);
                })
                .OrderBy(m => HazardConfidenceRank(m.WorstConfidence))
                .ThenByDescending(m => m.SiteCount)
                .ThenBy(m => m.Representative.Enclosing, StringComparer.Ordinal)
                .ToList();

            var siteCount = typeGroup.Count();
            var methodCount = byMethod.Count;
            // When all sites are in a single method the "across N method(s)" is noise — suppress it for clarity.
            var methodSuffix = methodCount > 1 ? $" across {methodCount} method(s)" : "";
            output.WriteLine($"{Indent.L1}{typeGroup.Key}: {siteCount} site(s){methodSuffix}{breakdown}");

            foreach (var (representative, siteCountForMethod, _) in byMethod.Take(perTypeSample))
            {
                var siteTag = siteCountForMethod > 1 ? $" ×{siteCountForMethod}" : "";
                output.WriteLine(
                    $"{Indent.L3}{ShortName(representative.Context)}  <- {ShortName(representative.Enclosing)}  {ShortenPath(representative.FilePath)}:{representative.Line}  [{representative.Reason}]{siteTag}"
                );
            }

            WriteSampleTruncationNote(output, total: methodCount, shown: perTypeSample, kind: typeGroup.Key);
        }
    }

    // Confidence sort key for hazard rollup ordering: high=0, medium=1, low=2. OrderBy picks worst (most
    // urgent) first — so a "high" finding sorts before "medium" and "low". Unknown tiers sort last.
    private static int HazardConfidenceRank(string confidence) =>
        confidence switch
        {
            "high" => 0,
            "medium" => 1,
            "low" => 2,
            _ => 3,
        };
}
