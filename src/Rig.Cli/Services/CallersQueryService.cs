using System.Diagnostics;
using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Deployments;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using static Rig.Cli.EntryPoints.EntryPointContext;

namespace Rig.Cli.Services;

// The ONE reverse-reachability engine ("who reaches X") behind BOTH `rig callers` and /api/callers.
// ComputeAsync owns everything that decides the answer — graph load, event-subscription handoff marking, the
// reverse closure, forward verification, the entry-point join, its cache routing and its two disclosures —
// and returns every partition as DATA. CallersCommand and CallersEndpoint only project it.
//
// Before this the command and BuildAsync each carried their own load + marking + traversal + forward verify,
// and they had drifted into THREE policies for one partition: web roots kept the reverse-only rows flagged,
// the web entry-point lens dropped them, and the CLI hid both. The partition is now one field on every row
// and each surface projects it.
//
// Deliberately public + primitives-in (workingDirectory/storeRef, not the internal WorkspaceLocation) so the
// contract survives a later lift to a standalone Rig.Web project — mirrors TreeQueryService/PathQueryService/
// ReachesQueryService's convention.
public static class CallersQueryService
{
    // The three mutually-exclusive lenses as ONE input. CallersCommand carries them as two independent bools
    // (Options.RootsOnly / Options.EntrypointsOnly, kept from combining by a System.CommandLine validator)
    // plus their flag-less default; `/api/callers?mode=` names the latter two directly.
    public enum CallersMode
    {
        Callers,
        Roots,
        EntryPoints,
    }

    // Default-lens row: one node of the reverse closure with its BFS depth. Depth 0 is a matched TARGET node
    // (the subject of the query, never a reverse-dispatch artefact, so always forward-confirmed); depth >= 1
    // is an upstream caller.
    public sealed record CallerReach(string SymbolId, int Depth, bool ForwardConfirmed);

    // mode=roots result row: one no-predecessor origin reaching the target, plus the forward-verification
    // flag (false = reverse-only: in the reverse closure but with no confirmed forward path — a
    // reverse-dispatch over-approximation; always true under --raw).
    public sealed record CallerRoot(string SymbolId, bool ForwardConfirmed);

    // One reverse-reachable entry point + its owning deployed service(s) (loaded-in, from deployments.json;
    // empty when deployments.json is absent) + the same forward-verification flag the other two lenses carry.
    public sealed record CallersEntryPoint(
        EntryPointService.EntryPointView View,
        IReadOnlyList<ServiceRef> Services,
        bool ForwardConfirmed
    );

    // Where the reverse chain TOPS OUT: a reverse-reachable method with no in-solution caller of its own,
    // under the same mode semantics as the walk. This is what makes a 0-entry-point answer attributable —
    // "reached across a boundary this analysis cannot see" rather than "dead".
    public sealed record CallersFrontierNode(string SymbolId, string FilePath, int Line);

    // Exactly one of Callers/Roots/EntryPoints is populated, selected by Mode. Every partition is a FIELD:
    // the forward-confirmed rows and the reverse-only ones arrive together, flagged, so a renderer hides a
    // field it was given instead of asking the engine to select for it.
    public sealed record CallersResult(
        string ToPattern,
        CallersMode Mode,
        bool Matched,
        // FLAT lists, not a nested TraceNode forest — see the note on ComputeAsync for why the reverse walk
        // has no tree to offer.
        IReadOnlyList<CallerReach>? Callers,
        IReadOnlyList<CallerRoot>? Roots,
        // Reuses EntryPointService.EntryPointView (Kind/Route/Fqn/File/Line) verbatim — the same shape
        // `/api/entrypoints` already exposes — rather than inventing a parallel record.
        IReadOnlyList<CallersEntryPoint>? EntryPoints,
        // The two --entrypoints disclosures, computed FROM the answer and therefore part of it: how many
        // rule-detected entry points reach the target on the ASYNC surface (0 when nothing is hidden — the
        // walk already crosses handoffs, or the graph has none), and where the reverse chain tops out.
        int AsyncReachableEpCount,
        IReadOnlyList<CallersFrontierNode> Frontier
    );

    // An entry-point row before the render projection: the cached whole-store record — which carries the
    // capability `Requires` tokens and the pre-resolved handler DocID the CLI's TSV and FQN columns need —
    // plus its verification flag. Internal because EntryPointRecord is the cache's shape, not a wire shape.
    internal sealed record CallersEntryPointHit(EntryPointRecord Record, bool ForwardConfirmed);

    // The rich result of the shared engine. Internal: the web consumes the public CallersResult projection,
    // while CallersCommand consumes this directly so its render stages (TSV columns, deployment chips,
    // --time phase rows) get the data they need without a second load.
    internal sealed record CallersComputation(
        CallersMode Mode,
        FactGraphData Graph,
        DeploymentMap Deployments,
        // Size of the reverse closure for the lenses that compute one (Callers/EntryPoints); 0 for Roots,
        // whose question EntryRootsReaching answers instead. The frontier disclosure reads it to tell
        // "nothing in the solution calls it at all" from "the chain tops out at a boundary".
        int ReachableCount,
        IReadOnlyList<CallerReach> Callers,
        IReadOnlyList<CallerRoot> Roots,
        IReadOnlyList<CallersEntryPointHit> EntryPoints,
        int AsyncReachableEpCount,
        IReadOnlyList<CallersFrontierNode> Frontier,
        // Phase elapsed times, recorded by the caller into its own QueryTiming so the CLI's `--time` table
        // keeps one row per phase in the order it always had. Null = the phase did not run, which is the
        // difference between "the async probe was free" and "no second reverse walk was paid".
        TimeSpan GraphLoadElapsed,
        TimeSpan? DeploymentsElapsed,
        TimeSpan ReverseClosureElapsed,
        TimeSpan? EntryPointsElapsed,
        TimeSpan? ForwardVerifyElapsed,
        TimeSpan? AsyncProbeElapsed
    );

    // The graph-load bound a lens needs: the --entrypoints answer carries an async-hint disclosure that
    // probes the ASYNC reverse set, so its load must reach that superset even when the walk itself is
    // sync-cut. The other two lenses need only their execution lens. CallersCommand.DiscoveryMode is this
    // rule plus the one CLI-only economy on top of it.
    internal static FactPathFinder.TraversalMode DiscoveryModeFor(CallersMode lens, FactPathFinder.TraversalMode executionMode) =>
        executionMode == FactPathFinder.TraversalMode.SyncCut && lens == CallersMode.EntryPoints
            ? FactPathFinder.TraversalMode.AsyncExact
            : executionMode;

    // Build the callers result for `fromPattern` over the store at `workingDirectory` (optionally a specific
    // `storeRef` commit/id). Thin by construction: it resolves the rules, opens the store arm of the fact
    // source and projects ComputeAsync's output — so the web reports exactly what `rig callers` computes,
    // minus the CLI-only render chrome (TSV/pretty rendering, deployment chips, --time).
    public static async Task<CallersResult> BuildAsync(
        string workingDirectory,
        string fromPattern,
        string? storeRef,
        CallersMode mode,
        bool async,
        int? depth = null,
        bool raw = false,
        IReadOnlyList<string>? extraRules = null
    )
    {
        // loadedRulePaths: the cascade RuleSetLoader just resolved, reused for the --entrypoints cache key's
        // rule-fingerprint axis instead of re-running the merge to re-discover the same files.
        var rules = RuleSetLoader.Load(workingDirectory, extraRules ?? [], loadedPaths: out var loadedRulePaths);
        // --raw parity: zero the graph-shaping rules so the reverse walk runs over the exact unfiltered graph.
        var shaped = raw ? rules with { Factory = [], Cut = [], Context = [], MaterializedGraphCompatible = false } : rules;
        await using var source = await StoreQueryFactSource.OpenAsync(
            new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: storeRef)
        );
        var executionMode = CommonOptions.Mode(async: async);
        var computation = await ComputeAsync(
            source: source,
            rules: rules,
            shaped: shaped,
            workingDirectory: workingDirectory,
            toPattern: fromPattern,
            maxDepth: CommonOptions.DepthOrUnbounded(depth),
            mode: executionMode,
            discoveryMode: DiscoveryModeFor(mode, executionMode),
            lens: mode,
            raw: raw,
            loadedRulePaths: loadedRulePaths,
            useCache: true
        );
        return Project(fromPattern, computation);
    }

    // The reverse-reachability engine, shared by `rig callers` (CallersCommand) and BuildAsync. It operates on
    // an ALREADY-OPEN fact source + ALREADY-LOADED/SHAPED rules, so the CLI reuses the source it opened —
    // and, because that source is the IQueryFactSource seam rather than a RigDbContext, the SAME engine
    // answers off a saved store or off the resident live facts.
    //
    // NOT a nested tree. The domain layer has no reverse analog of FactPathFinder.BuildTree: BuildTree only
    // walks FORWARD Successors to materialize a parent-linked TraceNode forest. The reverse walk
    // (FactPathFinder's private Predecessors/BuildReverseMaps, which EntryRootsReaching/ReachedBy are built
    // on) only ever produces a depth-map or a flat root list — it never records WHICH predecessor reached
    // WHICH node, so there is no parent linkage to reconstruct a tree from. Hence the flat results, which is
    // also how `rig callers` itself has always rendered.
    internal static async Task<CallersComputation> ComputeAsync(
        IQueryFactSource source,
        RuleSet rules,
        RuleSet shaped,
        string workingDirectory,
        string toPattern,
        int maxDepth,
        FactPathFinder.TraversalMode mode,
        FactPathFinder.TraversalMode discoveryMode,
        CallersMode lens,
        bool raw,
        // REQUIRED, not defaulted: these two are the EP-record cache key's non-store inputs. A default would
        // let a caller key a --rules query under the DEFAULT rule fingerprint and be served the wrong
        // entry-point set — so the caller must say which cascade it loaded and whether caching is on.
        IReadOnlyList<string> loadedRulePaths,
        bool useCache,
        int? maxNodes = null,
        int? maxGenericWork = null
    )
    {
        // One shaped reverse subgraph (SQL-bounded/full-EF on the store path; keyed fixed-point on an indexed
        // resident source) drives all three lenses. Direction only bounds WHICH nodes/edges get loaded —
        // FactPathFinder's own Predecessors/BuildReverseMaps do the actual reverse walk over whichever edges
        // arrived. A flattened live fixture alone retains the explicit whole-graph compatibility path.
        var graphWatch = Stopwatch.StartNew();
        DemandReverseCallersGraphResult? demandResult = null;
        FactGraphData graph;
        if (source is IDemandReverseCallersFactSource demand)
        {
            demandResult = await demand.LoadDemandReverseCallersGraphAsync(
                new DemandForwardGraphRules(
                    new ForwardCallProjectionRules(
                        Handoff: shaped.Handoff,
                        Redirect: shaped.Redirect,
                        Factory: shaped.Factory,
                        ClassifyEventSubscriptions: !raw
                    ),
                    shaped.Cut,
                    shaped.Context
                ),
                new DemandReverseCallersGraphRequest(
                    toPattern,
                    maxDepth,
                    discoveryMode,
                    Monomorphization: maxGenericWork is null ? null : new DemandMonomorphizationLimits(MaxWorkUnits: maxGenericWork.Value),
                    MaxNodes: maxNodes ?? 250_000,
                    ExecutionMode: mode
                )
            );
            graph = demandResult.Graph;
        }
        else
        {
            graph = await source.LoadShapedTraversalGraphAsync(toPattern, SqlReachability.Direction.Reverse, shaped);
        }

        // Reclassify event-subscription (`+=`) method-group edges to `handoff` — mirroring reaches/tree/path.
        // The handler runs LATER via the event, not synchronously at the `+=` site, so it is sync-cut by
        // default and only crossed under --async. Marks edges by (Caller, FilePath, Line), which is
        // direction-agnostic, so it applies to this REVERSE subgraph the same way. Consequence (intended,
        // matches reaches/tree): a `+=` handler is no longer a synchronous reverse caller, so event handlers
        // surface under --roots/--entrypoints only via --async. `--raw` bypasses shaping.
        if (!raw && demandResult?.EventSubscriptionsClassified != true)
        {
            graph = FactPathFinder.MarkEventSubscriptionHandoffs(graph, await source.EventSubscriptionSitesAsync());
        }

        graphWatch.Stop();

        return lens switch
        {
            CallersMode.Roots => ComputeRoots(graph, toPattern, maxDepth, mode, raw, graphWatch.Elapsed),
            CallersMode.EntryPoints => await ComputeEntryPointsAsync(
                source,
                graph,
                rules,
                workingDirectory,
                toPattern,
                maxDepth,
                mode,
                discoveryMode,
                raw,
                loadedRulePaths,
                useCache,
                graphWatch.Elapsed
            ),
            _ => ComputeCallers(graph, toPattern, maxDepth, mode, raw, graphWatch.Elapsed),
        };
    }

    // The flag-less default lens: every node of the reverse closure, depth-tagged, partitioned by forward
    // verification. The depth-0 entries are the matched TARGET nodes — the subject of the query, not its
    // answer — which is why the renderer's headline counts only depth >= 1.
    private static CallersComputation ComputeCallers(
        FactGraphData graph,
        string toPattern,
        int maxDepth,
        FactPathFinder.TraversalMode mode,
        bool raw,
        TimeSpan graphLoadElapsed
    )
    {
        // SPLIT (2026-08-24) from the single `traversal` bucket into the two hot spots it hid: the reverse
        // closure and the forward verification. They scale differently — the closure with graph size, the
        // verification with candidate COUNT x depth — so one bucket could never say which one to attack.
        var closureWatch = Stopwatch.StartNew();
        var reachable = MonomorphCollapse.CollapseDepthMap(FactPathFinder.ReachedBy(graph, toPattern, maxDepth, mode: mode));
        if (reachable.Count == 0)
        {
            closureWatch.Stop();
            return Empty(CallersMode.Callers, graph, graphLoadElapsed, closureWatch.Elapsed);
        }

        var matched = reachable.Where(k => k.Value == 0).OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
        var callers = reachable.Where(k => k.Value > 0).OrderBy(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).ToList();
        closureWatch.Stop();

        // FORWARD-VERIFY each upstream caller against the SAME graph, unless --raw — which keeps the exact
        // unfiltered reverse superset. Reverse reachability is set-based BFS, so a shared base/interface
        // virtual node pulls in callers whose FORWARD (receiver-narrowed) dispatch resolves to a sibling
        // override that never reaches the target. Each caller forward-reaches a matched (depth-0) target node
        // or is partitioned as reverse-only (recall-safe — a forward reach can legitimately miss an
        // interface-dispatch/lambda-only path, so we partition rather than drop).
        var verifyWatch = Stopwatch.StartNew();
        var targetIds = matched.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        var flags = Verify(graph, callers.Select(c => c.Key), targetIds, maxDepth, mode, raw);
        verifyWatch.Stop();

        var reached = matched
            .Select(kv => new CallerReach(kv.Key, kv.Value, ForwardConfirmed: true))
            .Concat(callers.Select((kv, i) => new CallerReach(kv.Key, kv.Value, flags[i])))
            .ToList();
        return Empty(CallersMode.Callers, graph, graphLoadElapsed, closureWatch.Elapsed) with
        {
            ReachableCount = reached.Count,
            Callers = reached,
            ForwardVerifyElapsed = verifyWatch.Elapsed,
        };
    }

    // `--roots`/`--orphans`: the no-predecessor origins reaching the target (FactPathFinder.EntryRootsReaching),
    // each forward-verified against the depth-0 matched target ids the reverse closure yields.
    private static CallersComputation ComputeRoots(
        FactGraphData graph,
        string toPattern,
        int maxDepth,
        FactPathFinder.TraversalMode mode,
        bool raw,
        TimeSpan graphLoadElapsed
    )
    {
        var closureWatch = Stopwatch.StartNew();
        var roots = FactPathFinder.EntryRootsReaching(graph, toPattern, maxDepth, mode: mode);
        closureWatch.Stop();
        if (roots.Count == 0)
        {
            return Empty(CallersMode.Roots, graph, graphLoadElapsed, closureWatch.Elapsed);
        }

        // The second reverse walk (for the depth-0 target ids) is attributed to the VERIFICATION phase, not
        // the closure: it exists only because the forward check needs targets to aim at.
        var verifyWatch = Stopwatch.StartNew();
        bool[] flags;
        if (raw)
        {
            flags = roots.Select(_ => true).ToArray();
        }
        else
        {
            var targetIds = FactPathFinder
                .ReachedBy(graph, toPattern, maxDepth, mode: mode)
                .Where(kv => kv.Value == 0)
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.Ordinal);
            flags = Verify(graph, roots, targetIds, maxDepth, mode, raw);
        }

        verifyWatch.Stop();

        return Empty(CallersMode.Roots, graph, graphLoadElapsed, closureWatch.Elapsed) with
        {
            Roots = roots.Select((r, i) => new CallerRoot(r, flags[i])).ToList(),
            ForwardVerifyElapsed = verifyWatch.Elapsed,
        };
    }

    // `--entrypoints`: the RULE-DETECTED entry points (the same set `rig derive` emits) whose declaration site
    // is in the reverse closure of the target. The join key is the declaration site (FilePath, Line): a
    // derived EP carries no DocID, but its handler method's symbol fact shares the same site, so an EP is
    // "touching" when some reverse-reachable method is declared at the EP's site.
    private static async Task<CallersComputation> ComputeEntryPointsAsync(
        IQueryFactSource source,
        FactGraphData graph,
        RuleSet rules,
        string workingDirectory,
        string toPattern,
        int maxDepth,
        FactPathFinder.TraversalMode mode,
        FactPathFinder.TraversalMode discoveryMode,
        bool raw,
        IReadOnlyList<string> loadedRulePaths,
        bool useCache,
        TimeSpan graphLoadElapsed
    )
    {
        // Loaded once, before the closure, so each reverse-reachable EP can be annotated with where it runs
        // (file-path attribution from deployments.json; a no-op upper bound when it is absent).
        var deploymentsWatch = Stopwatch.StartNew();
        var deployments = await source.LoadDeploymentsAsync(workingDirectory);
        deploymentsWatch.Stop();

        // Keep the depth-bearing closure: the depth-0 entries are the matched TARGET nodes, which the
        // forward-verify pass below reaches each candidate EP toward.
        var closureWatch = Stopwatch.StartNew();
        var reachedBy = FactPathFinder.ReachedBy(graph, toPattern, maxDepth, mode: mode);
        var reachable = reachedBy.Keys.ToHashSet(StringComparer.Ordinal);
        closureWatch.Stop();
        var empty = Empty(CallersMode.EntryPoints, graph, graphLoadElapsed, closureWatch.Elapsed) with
        {
            Deployments = deployments,
            DeploymentsElapsed = deploymentsWatch.Elapsed,
            ReachableCount = reachable.Count,
        };
        if (reachable.Count == 0)
        {
            return empty;
        }

        // (FilePath, Line) of every reverse-reachable method — the join key against derived EP sites. Sourced
        // from the already-loaded graph's method nodes rather than a second whole-method-table EF scan.
        var epWatch = Stopwatch.StartNew();
        var reachableSites = graph.Methods.Where(m => reachable.Contains(m.SymbolId)).Select(m => (m.FilePath, m.Line)).ToHashSet();

        // The whole-store EP record set, through the SAME artifact-cache entry every other EP surface uses
        // (EntryPointContext.LoadOrDeriveEntryPointRecordsAsync): a pure function of (store + rules) that does
        // not scale with the question, so no invocation may re-derive it. ONE code path and ONE key, and the
        // routing lives HERE rather than in a renderer.
        using var epCache = source.OpenArtifactCache(useCache);
        var epRecords = await LoadOrDeriveEntryPointRecordsAsync(
            source: source,
            cache: epCache,
            rulesHash: RulesFingerprint.ComputeFromPaths(loadedRulePaths),
            rules: rules
        );

        var touching = epRecords
            .Where(e => reachableSites.Contains((e.FilePath, e.Line)))
            .GroupBy(e => (e.Kind, e.Route, e.FilePath, e.Line))
            // The group key IS four of the six fields, and DocId is a function of the other two, so taking the
            // first member is the same row the old projection built field-by-field off the key + First().
            .Select(g => g.First())
            .OrderBy(e => e.Kind, StringComparer.Ordinal)
            .ThenBy(e => e.Route, StringComparer.Ordinal)
            .ToList();
        epWatch.Stop();

        // BUG-rig-missed-entrypoints-healthcode (Defect 2): the sync surface hides the scheduled/actor-handoff
        // paths, so a sync EP answer can UNDER-report — "0 sync" misreads as "unreachable from any entry
        // point" (which de-risks a change wrongly), and a non-zero sync count can still omit EPs that reach
        // the target ONLY via a handoff. Probed with AsyncExact (the semantics we'd suggest), never
        // AsyncInclude, so the hint never leans on imprecise delivery fan-out that --async would itself
        // exclude. Returns 0 when nothing is hidden: the walk already crosses handoffs, the graph has none, or
        // the caller did not ask the loader for the async superset (see DiscoveryModeFor) — in which case a
        // count taken off this graph would be an understatement rather than a disclosure.
        var probeable =
            mode == FactPathFinder.TraversalMode.SyncCut
            && discoveryMode == FactPathFinder.TraversalMode.AsyncExact
            // An O(E) scan that spares handoff-free stores the whole probe.
            && graph.CallEdges.Any(e => e.Kind == EdgeKinds.Handoff);
        var asyncEpCount = 0;
        TimeSpan? probeElapsed = null;
        if (probeable)
        {
            // Timed from HERE, past the early-out: a probe that did not run must not add a 0ms row that reads
            // as "the async hint is free". A row appearing at all means a SECOND whole reverse walk was paid.
            var probeWatch = Stopwatch.StartNew();
            var asyncReachable = FactPathFinder
                .ReachedBy(graph, toPattern, maxDepth, mode: FactPathFinder.TraversalMode.AsyncExact)
                .Keys.ToHashSet(StringComparer.Ordinal);
            var asyncSites = graph.Methods.Where(m => asyncReachable.Contains(m.SymbolId)).Select(m => (m.FilePath, m.Line)).ToHashSet();
            asyncEpCount = epRecords
                .Where(e => asyncSites.Contains((e.FilePath, e.Line)))
                .GroupBy(e => (e.Kind, e.Route, e.FilePath, e.Line))
                .Count();
            probeWatch.Stop();
            probeElapsed = probeWatch.Elapsed;
        }

        var withDisclosures = empty with
        {
            EntryPointsElapsed = epWatch.Elapsed,
            AsyncReachableEpCount = asyncEpCount,
            Frontier = ReverseFrontier(graph, reachable, mode),
            AsyncProbeElapsed = probeElapsed,
        };
        if (touching.Count == 0)
        {
            return withDisclosures;
        }

        // FORWARD-VERIFY each candidate EP against the SAME graph (recall-safe partition, NOT a drop).
        // Reverse reachability is set-based BFS, so once a shared base/interface virtual node enters the
        // reverse closure ALL its callers rejoin — including callers whose FORWARD (receiver-narrowed)
        // dispatch resolves to a DIFFERENT sibling override, never the target's (the documented "reverse
        // narrowing is dispatch-hop-precise, not path-precise" limitation). For each candidate EP we
        // forward-reach its handler-method nodes (the graph.Methods declared at the EP's (file,line)) and mark
        // it CONFIRMED iff one of them forward-reaches a matched target node; the rest carry the flag false
        // rather than disappearing — a forward reach can legitimately miss an interface-dispatch/lambda-only
        // reach, so dropping would risk a false negative.
        //
        // Its own phase, separate from `reverse closure`: SeedsReachTarget is a forward walk PER candidate EP,
        // so it scales with the candidate COUNT x depth while the closure scales with graph size. Folded
        // together they could never say which of the two a slow query was actually paying for.
        var verifyWatch = Stopwatch.StartNew();
        var targetIds = reachedBy.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
        // (FilePath,Line) -> the method symbol ids declared there, inverting the same graph.Methods set
        // reachableSites was built from — the candidate EP's handler nodes to seed the forward reach with.
        var methodsBySite = new Dictionary<(string, int), List<string>>();
        foreach (var m in graph.Methods)
        {
            var key = (m.FilePath!, m.Line);
            if (!methodsBySite.TryGetValue(key, out var ids))
            {
                ids = new List<string>();
                methodsBySite[key] = ids;
            }

            ids.Add(m.SymbolId);
        }

        var seedGroups = touching
            .Select(e => (IReadOnlyList<string>)(methodsBySite.TryGetValue((e.FilePath, e.Line), out var ids) ? ids : []))
            .ToList();
        var flags = raw
            ? touching.Select(_ => true).ToArray()
            : FactPathFinder.SeedsReachTarget(graph, seedGroups, targetIds, maxDepth, mode);
        verifyWatch.Stop();

        return withDisclosures with
        {
            EntryPoints = touching.Select((e, i) => new CallersEntryPointHit(e, flags[i])).ToList(),
            ForwardVerifyElapsed = verifyWatch.Elapsed,
        };
    }

    // One forward reach per candidate, toward the matched (depth-0) target nodes: the arbiter that partitions
    // the reverse closure's candidates into confirmed and reverse-only. --raw keeps the exact unfiltered
    // reverse superset, so everything is confirmed there by definition.
    private static bool[] Verify(
        FactGraphData graph,
        IEnumerable<string> candidates,
        HashSet<string> targetIds,
        int maxDepth,
        FactPathFinder.TraversalMode mode,
        bool raw
    )
    {
        var seedGroups = candidates.Select(c => (IReadOnlyList<string>)new[] { c }).ToList();
        return raw ? seedGroups.Select(_ => true).ToArray() : FactPathFinder.SeedsReachTarget(graph, seedGroups, targetIds, maxDepth, mode);
    }

    // THE FRONTIER: the reverse-reachable methods that have NO caller of their own — where the chain TOPS
    // OUT. On a 0-EP answer this is the difference between "dead code" and "reached across a boundary this
    // analysis cannot see". `callers X --entrypoints` reporting a bare zero while plain `callers X` returned
    // an 18-method chain cost a WRONG CONCLUSION mid-review (the gap was attributed to lambdas; rig models
    // lambdas fine — `~λ0` nodes appear in the chain). The real cause was template/Dom interpolation
    // (`{MedicalRecord.Documents}` resolved reflectively), which is exactly what a frontier list shows.
    //
    // In-edges are computed under the SAME mode semantics as the walk (handoff edges excluded under SyncCut),
    // so the frontier describes the traversal actually performed rather than a different graph.
    private static List<CallersFrontierNode> ReverseFrontier(
        FactGraphData graph,
        HashSet<string> reachable,
        FactPathFinder.TraversalMode mode
    )
    {
        var hasCaller = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in graph.CallEdges)
        {
            if (mode == FactPathFinder.TraversalMode.SyncCut && e.Kind == EdgeKinds.Handoff)
            {
                continue; // not walked in this mode, so it does not count as a caller for this answer
            }

            hasCaller.Add(e.Callee);
        }

        return graph
            .Methods.Where(m => reachable.Contains(m.SymbolId) && !hasCaller.Contains(m.SymbolId))
            .GroupBy(m => m.SymbolId, StringComparer.Ordinal)
            // FilePath is nullable on a method fact (a synthesized/metadata node has no source); the renderer
            // already treats "" as "unlocated", so normalize here rather than widen the record.
            .Select(g => new CallersFrontierNode(g.Key, g.First().FilePath ?? "", g.First().Line))
            .OrderBy(m => m.SymbolId, StringComparer.Ordinal)
            .ToList();
    }

    // The no-answer shape for a lens, and the base every populated result is built from with `with` — so a
    // new field cannot be silently forgotten on one of the four exit paths.
    private static CallersComputation Empty(
        CallersMode lens,
        FactGraphData graph,
        TimeSpan graphLoadElapsed,
        TimeSpan reverseClosureElapsed
    ) =>
        new(
            Mode: lens,
            Graph: graph,
            Deployments: DeploymentMap.Empty,
            ReachableCount: 0,
            Callers: [],
            Roots: [],
            EntryPoints: [],
            AsyncReachableEpCount: 0,
            Frontier: [],
            GraphLoadElapsed: graphLoadElapsed,
            DeploymentsElapsed: null,
            ReverseClosureElapsed: reverseClosureElapsed,
            EntryPointsElapsed: null,
            ForwardVerifyElapsed: null,
            AsyncProbeElapsed: null
        );

    // The web projection. Both lenses keep their reverse-only rows FLAGGED rather than dropped — one policy
    // for one partition — and the flag rides on the row so a client can hide exactly what the CLI hides by
    // default (`--include-reverse-only` is the CLI's escape hatch for the same data).
    private static CallersResult Project(string toPattern, CallersComputation computation)
    {
        var deploy = new DeploymentAttributionLookup(computation.Deployments);
        var entryPoints = computation
            .EntryPoints.Select(hit =>
            {
                var file = string.IsNullOrEmpty(hit.Record.FilePath) ? null : hit.Record.FilePath;
                var view = new EntryPointService.EntryPointView(
                    Kind: hit.Record.Kind,
                    Route: hit.Record.Route,
                    Fqn: FqnOrRoute(hit.Record),
                    File: file,
                    Line: hit.Record.Line
                );
                return new CallersEntryPoint(view, deploy.IsEmpty ? [] : deploy.ServicesWithKindFor(file), hit.ForwardConfirmed);
            })
            .ToList();

        return computation.Mode switch
        {
            CallersMode.EntryPoints => new CallersResult(
                ToPattern: toPattern,
                Mode: computation.Mode,
                Matched: entryPoints.Count > 0,
                Callers: null,
                Roots: null,
                EntryPoints: entryPoints,
                AsyncReachableEpCount: computation.AsyncReachableEpCount,
                Frontier: computation.Frontier
            ),
            CallersMode.Roots => new CallersResult(
                ToPattern: toPattern,
                Mode: computation.Mode,
                Matched: computation.Roots.Count > 0,
                Callers: null,
                Roots: computation.Roots,
                EntryPoints: null,
                AsyncReachableEpCount: computation.AsyncReachableEpCount,
                Frontier: computation.Frontier
            ),
            _ => new CallersResult(
                ToPattern: toPattern,
                Mode: computation.Mode,
                Matched: computation.Callers.Count > 0,
                Callers: computation.Callers,
                Roots: null,
                EntryPoints: null,
                AsyncReachableEpCount: computation.AsyncReachableEpCount,
                Frontier: computation.Frontier
            ),
        };
    }
}
