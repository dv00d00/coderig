using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// Stage-2-over-facts path finding: BFS the fact-derived call graph from any symbol matching
// `fromPattern` to any symbol matching `toPattern`, cross-project, with no entry-point anchoring.
// Includes the interface->concrete DI hop (the single-impl dispatch from Q5) reconstructed from
// type-relation facts + DocID member-name matching — no Roslyn, no SemanticModel.
// (Rig.Domain targets netstandard2.0, so this avoids TryAdd / ranges / Contains(string,cmp).)
//
// Dispatch is resolved EXACT-FIRST: the member-level interface→impl / base→override correspondence
// comes from the Roslyn-MINED dispatch facts when present (FactGraphData.MinedDispatch, Basis=
// "roslyn" — signature-exact, generic-correct), with the name/arity CHA scan kept only as a FLAGGED
// fallback (Basis="heuristic") for members Roslyn couldn't bind (`!:` error-typed interfaces, unmined
// stores). And EDGE-AWARE (receiver-type narrowing): the in-memory traversal narrows a
// virtual/base/interface call to the STATIC RECEIVER TYPE mined onto the call edge (CallEdge.
// ReceiverType) — `company.Save()` reaches CompanyEntity.Save (+ Company subtypes), not all 114
// CommonEntityBase.Save overrides. It falls back to the full receiver-blind set whenever the receiver
// is unreliable (null/interface/error-type/the declaring base/not a known first-party type), so no
// real target is ever dropped. The precomputed dispatch_edges table and AllDispatchEdges stay
// receiver-blind (the sound superset that bounds the SQL load); narrowing lives ONLY in the
// in-memory edge traversal.
public static partial class FactPathFinder
{
    // How traversal treats async HANDOFF edges (Kind=="handoff" — a delegate handed to a dispatcher
    // to run later / on another thread). SyncCut (the default everywhere) skips them, so a timer/
    // background registration does NOT look like it executes its callback synchronously. AsyncInclude
    // walks them, tagging the reached subtree with HandoffVia provenance (cloned from the DispatchVia
    // machinery) so `--async` can show the scheduled reach distinctly. sync ⊆ async by construction.
    public enum TraversalMode
    {
        // No handoff edge is crossed: a scheduled/deferred callback is not a synchronous call, so the
        // registrar does not reach the callback. The default for every traversal command.
        SyncCut,

        // The default for `--async`: cross handoff edges EXCEPT symbol-blind delivery FAN-OUT
        // (DeliveryPrecisions.Fanout). Sound handoffs are walked — event `+= H` registrant→handler,
        // scheduler/timer/spawn dispatch, and single-subscriber (Exact) delivery — but a producer→handler
        // fan-out that joined a raise to every same-symbol subscriber is NOT, because it crosses caller/
        // instance boundaries it cannot prove (see CutsHandoff + docs/FIX-event-raise-overapproximation.md).
        AsyncExact,

        // `--async --include-delivery`: cross EVERY handoff edge, including the imprecise delivery fan-out.
        // The historical `--async` behavior, kept as an explicit opt-in for the over-approximate superset.
        AsyncInclude,
    }

    // Which reverse-dispatch predecessors a reverse walk admits, on top of the direct callers it always
    // yields. The forward walk already splits dispatch by DEGREE (a single-target hop is deterministic and
    // tagged like a real call; only `Fanout > 1` is disclosed as fan-out — see ReachesWithFanoutCore), and
    // this is the reverse mirror of that split, so a read model can separate "reached deterministically" from
    // "reached only through a hop that could land on several implementations".
    public enum DispatchAdmission
    {
        // Direct callers only — the reverse of Successors' direct-call arm.
        None,

        // Direct callers plus dispatch hops whose receiver-narrowed candidate set has exactly ONE target.
        // Such a hop is deterministic: the one candidate is the only body the call can run.
        SingleTarget,

        // Every dispatch hop, whatever its degree. The default for every reverse traversal.
        All,
    }

    // The SINGLE handoff-gate predicate, shared by the forward (Dispatch) and reverse (GraphIndex) walks so
    // they cut identically. Returns true when `edge` must NOT be crossed in `mode`:
    //   * SyncCut       — cut ALL handoff edges (a deferred callback is not a synchronous call).
    //   * AsyncExact    — cut ONLY delivery fan-out edges (Kind=handoff, DeliveryPrecision=Fanout). These
    //                     join a publish to every same-symbol subscriber with no instance/call-site
    //                     identity, so they connect unrelated callers to unrelated handlers — empirically
    //                     false (22/22 sampled on MedDBase). The sound registrant→handler `event` edge,
    //                     scheduler/spawn handoffs, and single-subscriber `exact` delivery are all kept, so
    //                     real deferred reach is preserved; only the manufactured cross-product is dropped.
    //   * AsyncInclude  — cut nothing (walk every handoff, incl. the fan-out superset).
    // Non-handoff edges are never cut here. NOT a blanket "disable handoff search": it is precision-targeted
    // — see docs/FIX-event-raise-overapproximation.md for why fan-out delivery is the only class removed.
    public static bool CutsHandoff(TraversalMode mode, CallEdge edge) =>
        edge.Kind == EdgeKinds.Handoff
        && mode switch
        {
            TraversalMode.SyncCut => true,
            TraversalMode.AsyncExact => string.Equals(edge.DeliveryPrecision, DeliveryPrecisions.Fanout, StringComparison.Ordinal),
            _ => false,
        };

    public static IReadOnlyList<PathStep>? Find(
        FactGraphData graph,
        string fromPattern,
        string toPattern,
        int maxDepth = 20,
        TraversalMode mode = TraversalMode.SyncCut
    )
    {
        var index = BuildIndex(graph);

        // Parent links carry the edge that reached the node (for path + kind reconstruction),
        // including its enclosing-loop context so the reconstructed path can mark looped hops, the
        // dispatch fan-out degree so a path that traverses a base-virtual fan-out shows it, the
        // handoff-dispatcher provenance so an --async path can render the cross-thread hop, and the
        // dispatch BASIS (roslyn-mined vs name/arity heuristic) so inferred hops are flagged.
        var parent = new Dictionary<
            string,
            (
                string From,
                string Kind,
                string? File,
                int Line,
                string? LoopKind,
                string? LoopDetail,
                int Fanout,
                string? HandoffVia,
                string? Basis
            )?
        >(StringComparer.Ordinal);
        var queue = new Queue<(string Node, int Depth)>();
        // Receiver of the edge that reached each node — narrows that node's dispatch when expanded.
        var receiverOf = new Dictionary<string, string?>(StringComparer.Ordinal);
        // Whether the (first) edge that reached each node was a NON-VIRTUAL `base.M()` call — if so the
        // node is not re-dispatched into sibling overrides (suppressed via `fromDispatch`, one-hop). Mirrors
        // the dispatch-edge `fromDispatch` tracking; a parallel map, since the `parent` tuple is path-shape.
        var viaNonVirtualOf = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var start in MatchNodes(index.Nodes, fromPattern))
        {
            if (parent.ContainsKey(start))
            {
                continue;
            }

            parent[start] = null;
            receiverOf[start] = null;
            queue.Enqueue((start, 0));
        }

        // Resolve the target the same exact-match-wins way as the seeds, once, so a fully-qualified `to`
        // hits exactly its member rather than every member it is a prefix of (Proceed vs ProceedTo…).
        var targets = MatchNodes(index.Nodes, toPattern).ToHashSet(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();

            if (parent[current] is not null && targets.Contains(current))
            {
                return Reconstruct(parent, current);
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (
                var s in Successors(
                    current: current,
                    index: index,
                    incomingReceiver: receiverOf.TryGetValue(key: current, value: out var rc) ? rc : null,
                    incomingBinding: null,
                    mode: mode,
                    fromDispatch: (parent.TryGetValue(current, out var pe) && pe is { } p && IsDispatchEdgeKind(p.Kind))
                        || (viaNonVirtualOf.TryGetValue(current, out var nv) && nv)
                )
            )
            {
                if (!parent.ContainsKey(s.Node))
                {
                    receiverOf[s.Node] = s.OutReceiver;
                    viaNonVirtualOf[s.Node] = s.OutNonVirtual;
                }

                Enqueue(
                    parent,
                    queue,
                    node: s.Node,
                    from: current,
                    kind: s.Kind,
                    file: s.File,
                    line: s.Line,
                    loopKind: s.LoopKind,
                    loopDetail: s.LoopDetail,
                    fanout: s.Fanout,
                    handoffVia: s.HandoffVia,
                    basis: s.Basis,
                    depth: depth
                );
            }
        }

        return null;
    }

    // What reaching a node cost: shortest BFS depth, plus the loop-fanout picked up along that
    // shortest path. LoopNesting = how many looped call edges were traversed to first reach the
    // node (0 = no loop on the path; >=1 = fanned out; >=2 = loop-within-loop / nested fanout).
    // NearestLoop* = the enclosing-loop kind/detail of the looped edge closest to the node (the
    // innermost loop wrapping its call chain), for display. BFS-shortest path is used, so the
    // fanout reported is the one on the shortest route — a defensible single answer when a node is
    // reachable several ways.
    // DispatchVia/DispatchDegree (A1/D3/D7): when the shortest path to this node crossed a base->
    // override (or interface->impl) dispatch that fanned ONE source method out to N(>1) targets,
    // DispatchVia = that source method's DocID (e.g. EntityBase.Save) and DispatchDegree = N. The tag
    // is inherited forward through the fanned-out subtree (like NearestLoop), and is null/0 when the
    // node is reachable directly (a real call) or only through single-target dispatch. Lets `reaches`
    // separate genuine per-entry reach from base-virtual dispatch fan-out instead of over-counting it.
    // HandoffVia (clone of DispatchVia): under AsyncInclude, when the BFS-shortest path to this node
    // crossed an async handoff edge, HandoffVia = that edge's dispatcher id (e.g. the rule that
    // matched RepeatingBackgroundProcessSchedule). Inherited forward through the scheduled subtree
    // (like NearestLoop/DispatchVia), and null when the node is also reachable synchronously (the
    // shorter sync route reaches it first, with no handoff ancestor). Always null under SyncCut.
    // DispatchBasis: provenance of the dispatch hops on the BFS-shortest path to this node —
    // "heuristic" (STICKY: at least one name/arity-guessed dispatch hop on the path; the reach is
    // only as trustworthy as that guess), "roslyn" (dispatch crossed, all hops exact mined facts),
    // or null (no dispatch hop on the path). Inherited forward like NearestLoop/HandoffVia.
    public sealed record ReachInfo(
        int Depth,
        int LoopNesting,
        string? NearestLoopKind,
        string? NearestLoopDetail,
        string? DispatchVia = null,
        int DispatchDegree = 0,
        string? HandoffVia = null,
        string? DispatchBasis = null
    );

    // Full reachability: BFS the call graph (incl. interface->impl dispatch) from every node
    // matching `fromPattern`, returning each reachable method DocID with its shortest depth.
    // Same traversal as Find — so "what does this entry point reach" is consistent with `rig path`.
    // narrowDispatch=false forces full CHA (the receiver-blind superset) — the equivalence oracle
    // path that matches the CHA SQL traversal exactly; the live CLI uses the default (narrowed).
    public static IReadOnlyDictionary<string, int> Reaches(
        FactGraphData graph,
        string fromPattern,
        int maxDepth = 20,
        int maxNodes = 20000,
        bool narrowDispatch = true,
        TraversalMode mode = TraversalMode.SyncCut
    )
    {
        var info = ReachesWithFanout(graph, fromPattern, maxDepth, maxNodes, narrowDispatch, mode);
        var depthOf = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in info)
        {
            depthOf[kv.Key] = kv.Value.Depth;
        }

        return depthOf;
    }

    // Reachability enriched with loop-fanout: same BFS as Reaches, but each node also carries how
    // many looped call edges were crossed to reach it (and the innermost such loop). Lets callers
    // flag effects that fire inside a loop somewhere along the call chain — the static signal behind
    // the "🔁/⇉ fanout" annotations (true runtime ×N is not statically known; this is the nesting).
    public static IReadOnlyDictionary<string, ReachInfo> ReachesWithFanout(
        FactGraphData graph,
        string fromPattern,
        int maxDepth = 20,
        int maxNodes = 20000,
        bool narrowDispatch = true,
        TraversalMode mode = TraversalMode.SyncCut
    )
    {
        var index = BuildIndex(graph, narrowDispatch);
        return ReachesWithFanoutCore(index, MatchNodes(index.Nodes, fromPattern), maxDepth, maxNodes, mode);
    }

    // Exact-id, one-hop forward reach from EACH seed independently, returning one reachable-node set per
    // seed (same dispatch semantics as ReachesWithFanout — narrowDispatch one-hop — but seeded by EXACT
    // symbol id, not a substring pattern, so `EditLive.Save` does NOT also seed `EditLive.SaveFinal`).
    // The index is built ONCE and shared across a parallel per-seed loop (read-only traversal over an
    // immutable graph; the lone mutable cache is concurrent + idempotent). This is the engine behind
    // `rig impact`'s per-EP behavioral attribution: forward-reach hundreds of EPs over one loaded graph.
    public static IReadOnlyList<HashSet<string>> ReachesFromEachSeed(
        FactGraphData graph,
        IReadOnlyList<string> seedIds,
        int maxDepth = 20,
        int maxNodes = 20000,
        bool narrowDispatch = true,
        TraversalMode mode = TraversalMode.SyncCut
    ) => OpenSession(graph, narrowDispatch).ReachesFromEachSeed(seedIds, maxDepth, maxNodes, mode);

    // A traversal SESSION over one graph: builds the GraphIndex ONCE and serves any number of batch
    // traversals from it.
    //
    // Every public batch method builds its own private index, which is right for a one-shot query and wasteful
    // for a caller that runs several over a byte-identical graph. `rig impact` is that caller: reach sets,
    // footprints, hazards (and, since guard-condition deltas, an effects-from-callee walk) each rebuilt the
    // full adjacency + four-key sort + MethodsByStrippedType/ImplsByInterface/context-family/mined-dispatch
    // construction — 3-4× per side, 6-7× per cold diff, over the same graph each time.
    //
    // The index is safe to share across the parallel per-seed walks: the traversal is read-only over an
    // immutable graph and the one mutable cache (DescendantsCache) is concurrent and idempotent — which is
    // already relied on WITHIN each batch call, so sharing across calls adds no new concurrency assumption.
    // GraphIndex stays private; a caller holds this instead of constructing one.
    public sealed class TraversalSession
    {
        private readonly GraphIndex index;

        // INTERNAL: GraphIndex is internal to Rig.Domain, so this ctor cannot be public without leaking a
        // less-accessible type — and must not be, since the index is an implementation detail. Callers in other
        // assemblies get a session from OpenSession.
        internal TraversalSession(GraphIndex index) => this.index = index;

        public IReadOnlyList<HashSet<string>> ReachesFromEachSeed(
            IReadOnlyList<string> seedIds,
            int maxDepth = 20,
            int maxNodes = 20000,
            TraversalMode mode = TraversalMode.SyncCut
        )
        {
            var results = new HashSet<string>[seedIds.Count];
            Parallel.For(
                fromInclusive: 0,
                toExclusive: seedIds.Count,
                body: i => results[i] = new HashSet<string>(InfoFor(seedIds[i], maxDepth, maxNodes, mode).Keys, StringComparer.Ordinal)
            );
            return results;
        }

        public IReadOnlyList<IReadOnlyDictionary<string, ReachInfo>> ReachesInfoFromEachSeed(
            IReadOnlyList<string> seedIds,
            int maxDepth = 20,
            int maxNodes = 20000,
            TraversalMode mode = TraversalMode.SyncCut
        )
        {
            var results = new IReadOnlyDictionary<string, ReachInfo>[seedIds.Count];
            Parallel.For(
                fromInclusive: 0,
                toExclusive: seedIds.Count,
                body: i => results[i] = InfoFor(seedIds[i], maxDepth, maxNodes, mode)
            );
            return results;
        }

        // An unknown seed id yields an EMPTY reach rather than an error — callers pass "" for an EP whose site
        // resolved to no indexed method, and both batch APIs have always tolerated that.
        private IReadOnlyDictionary<string, ReachInfo> InfoFor(string seed, int maxDepth, int maxNodes, TraversalMode mode) =>
            ReachesWithFanoutCore(index, index.Nodes.Contains(seed) ? [seed] : [], maxDepth, maxNodes, mode);
    }

    // Open a traversal session over `graph`, building its index once. Use when running SEVERAL batch
    // traversals over one graph; the one-shot statics remain correct for a single query.
    public static TraversalSession OpenSession(FactGraphData graph, bool narrowDispatch = true) => new(BuildIndex(graph, narrowDispatch));

    // Forward-VERIFY a set of candidate seed GROUPS against a target id set: for each group, true iff ANY
    // seed in that group forward-reaches ANY id in `targetIds` (one-hop narrowed dispatch, same engine as
    // `rig impact`). Backs `rig callers --entrypoints` forward-confirmation: reverse reachability is set-
    // based BFS, so a shared base/interface virtual node pulls in ALL its callers — including callers whose
    // FORWARD (receiver-narrowed) dispatch resolves to a DIFFERENT sibling override, never the target's.
    // This pass re-checks each emitted EP's handler methods FORWARD (where narrowDispatch prunes the sibling
    // override) and partitions confirmed vs reverse-only — recall-safe, since narrowing never drops a real
    // target. Implemented by flattening the groups to DISTINCT seeds, reusing ReachesFromEachSeed (one shared
    // index, parallel per-seed), then OR-reducing each group's seeds' reach sets against targetIds. The
    // returned bool[] is aligned to `seedGroups` order.
    public static bool[] SeedsReachTarget(
        FactGraphData graph,
        IReadOnlyList<IReadOnlyList<string>> seedGroups,
        IReadOnlyCollection<string> targetIds,
        int maxDepth,
        TraversalMode mode
    )
    {
        var targets = targetIds as HashSet<string> ?? new HashSet<string>(targetIds, StringComparer.Ordinal);
        // Distinct seed ids across all groups — reach each once, then index back per group.
        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in seedGroups)
        foreach (var seed in group)
        {
            if (seen.Add(seed))
            {
                distinct.Add(seed);
            }
        }

        var reachSets = ReachesFromEachSeed(graph, distinct, maxDepth, maxNodes: 20000, narrowDispatch: true, mode: mode);
        var reachOf = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (var i = 0; i < distinct.Count; i++)
        {
            reachOf[distinct[i]] = reachSets[i];
        }

        var result = new bool[seedGroups.Count];
        for (var g = 0; g < seedGroups.Count; g++)
        {
            var confirmed = false;
            foreach (var seed in seedGroups[g])
            {
                if (reachOf.TryGetValue(seed, out var reach) && reach.Overlaps(targets))
                {
                    confirmed = true;
                    break;
                }
            }

            result[g] = confirmed;
        }

        return result;
    }

    // Exact-id forward reach from EACH seed independently, returning the FULL per-node ReachInfo per seed
    // (NOT just the reachable-node set, as ReachesFromEachSeed does). Same index/dispatch semantics; the
    // extra payload is the loop context (NearestLoopKind) and depth/dispatch tags already computed by the
    // BFS — `rig impact`'s effect-AMPLIFICATION pass needs the loop flag per reachable effect-bearing node,
    // which the set-only twin discards. Built once, run in parallel over the shared read-only index.
    public static IReadOnlyList<IReadOnlyDictionary<string, ReachInfo>> ReachesInfoFromEachSeed(
        FactGraphData graph,
        IReadOnlyList<string> seedIds,
        int maxDepth = 20,
        int maxNodes = 20000,
        bool narrowDispatch = true,
        TraversalMode mode = TraversalMode.SyncCut
    ) => OpenSession(graph, narrowDispatch).ReachesInfoFromEachSeed(seedIds, maxDepth, maxNodes, mode);

    // The shared BFS body of ReachesWithFanout / ReachesFromEachSeed: one-hop dispatch forward reach over a
    // PREBUILT index from the given seed nodes. All traversal state below is LOCAL — safe to run concurrently
    // over one shared (read-only) index (DescendantsCache is concurrent).
    private static IReadOnlyDictionary<string, ReachInfo> ReachesWithFanoutCore(
        GraphIndex index,
        IEnumerable<string> seeds,
        int maxDepth,
        int maxNodes,
        TraversalMode mode
    )
    {
        var info = new Dictionary<string, ReachInfo>(StringComparer.Ordinal);
        // Which DISPATCH CONTEXTS each node has already been expanded under (DispatchContextKey). The
        // receiver a node is reached with selects WHICH OVERRIDE its virtual calls resolve to, so expanding
        // a node once — under whichever receiver happened to arrive first — silently dropped every other
        // receiver's override from the reach set. This mirrors the treatment `bindingOf` already gets: a
        // context that adds something new re-enqueues the node instead of being discarded. For a node with
        // no dispatch fan the key is the bare symbol, so it is still expanded exactly once.
        var expandedContexts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        // Generic-dispatch narrowing in the CLOSURE: the concrete type-arg binding accumulated at each
        // node. This is UNIONED across every path that reaches a node
        // and the node is re-enqueued when its binding GROWS — so a shared generic hub (e.g. Construct`2.
        // New reached via several entity caches) ends up narrowed to ALL really-reachable constructors,
        // never just the first path's (which would unsoundly drop the others). Monotone (sets only grow,
        // capped) so it reaches a fixpoint. Narrowing is recall-safe in DispatchTargets: an empty/unmatched
        // binding leaves the full CHA set, so a hub reached without a matching type arg is never emptied.
        var bindingOf = new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal);
        // The (receiver, fromDispatch) contexts each node has already been QUEUED under. The queue carries
        // the context on the work item rather than storing one per node, because a node reached under two
        // receivers has two different expansions to do — the old per-node `receiverOf`/`viaDispatchOf`
        // recorded only the BFS-first one and discarded the rest. This set makes the walk terminate
        // (contexts per node are finite) and bounds the queue; the fan-key check at dequeue is what avoids
        // the redundant WORK when two receivers happen to resolve to the same dispatch.
        var queuedContexts = new Dictionary<string, HashSet<(string? Receiver, bool FromDispatch)>>(StringComparer.Ordinal);
        var queue = new Queue<(string Node, string? Receiver, bool FromDispatch)>();

        // Queues `node` under one call context, unless that context is already queued or the node has hit
        // the per-node context cap. `force` bypasses both for a node whose type-arg BINDING just grew: the
        // binding is an input to dispatch resolution, so the same (receiver, fromDispatch) pair can now
        // resolve to a larger fan and must be walked again. That stays finite because the binding is
        // monotone and capped at MaxBinding, so a node can only grow finitely often.
        void Enqueue(string node, string? receiver, bool fromDispatch, bool force = false)
        {
            if (!queuedContexts.TryGetValue(node, out var contexts))
            {
                contexts = new HashSet<(string?, bool)>();
                queuedContexts[node] = contexts;
            }

            var isNewContext = contexts.Add((receiver, fromDispatch));
            if (!force && (!isNewContext || contexts.Count > MaxDispatchContexts))
            {
                return;
            }

            queue.Enqueue((node, receiver, fromDispatch));
        }

        foreach (var start in seeds)
        {
            if (info.ContainsKey(start))
            {
                continue;
            }

            info[start] = new ReachInfo(Depth: 0, LoopNesting: 0, NearestLoopKind: null, NearestLoopDetail: null);
            bindingOf[start] = null;
            Enqueue(start, receiver: null, fromDispatch: false);
        }

        while (queue.Count > 0 && info.Count < maxNodes)
        {
            var (current, currentReceiver, currentFromDispatch) = queue.Dequeue();
            var cur = info[current];
            if (cur.Depth >= maxDepth)
            {
                continue;
            }

            var currentBinding = bindingOf.TryGetValue(current, out var curBinding) ? curBinding : null;
            // Resolve this context's dispatch fan once — it both keys the expansion memo and is handed to
            // Successors, so the fan costs the same single resolution per expansion it always did.
            var dispatch = ResolveDispatch(current, index, currentReceiver, currentBinding, currentFromDispatch);
            var contextKey = DispatchContextKey(current, dispatch);
            if (!expandedContexts.TryGetValue(current, out var expandedForNode))
            {
                expandedForNode = new HashSet<string>(StringComparer.Ordinal);
                expandedContexts[current] = expandedForNode;
            }

            // Already expanded under a context that resolves to the SAME dispatch — the successors would be
            // identical, so there is nothing new to walk. (A node with no fan has the bare symbol as its
            // key, so it is expanded exactly once, as before.)
            if (!expandedForNode.Add(contextKey))
            {
                continue;
            }

            foreach (
                var s in Successors(
                    current: current,
                    index: index,
                    incomingReceiver: currentReceiver,
                    incomingBinding: currentBinding,
                    mode: mode,
                    fromDispatch: currentFromDispatch,
                    resolvedDispatch: dispatch
                )
            )
            {
                // Suppress re-dispatch of a node reached via a dispatch edge OR a non-virtual `base.M()`
                // call — both resolve to exactly one concrete method whose own override fan-out is spurious.
                var targetFromDispatch = IsDispatchEdgeKind(s.Kind) || s.OutNonVirtual;
                // Merge this edge's carried binding into the target; a node whose binding GREW is
                // re-enqueued (even if already reached) so its generic dispatch re-expands under the
                // larger binding — this is what keeps the closure sound at shared generic hubs.
                var grew = MergeBinding(bindingOf, s.Node, s.OutBinding);
                if (info.ContainsKey(s.Node))
                {
                    // Already reached, but possibly not under THIS context: a new receiver can resolve its
                    // dispatch to an override the earlier expansion never reached (the devirtualization gap
                    // this fixes), and a grown binding can widen a generic hub's fan. Re-queueing is cheap —
                    // Enqueue drops a context already queued, and the fan-key check at dequeue drops any
                    // context that resolves to a dispatch already walked.
                    Enqueue(s.Node, s.OutReceiver, targetFromDispatch, force: grew);
                    continue;
                }
                var looped = s.LoopKind is not null;
                var nesting = cur.LoopNesting + (looped ? 1 : 0);
                var nearKind = looped ? s.LoopKind : cur.NearestLoopKind;
                var nearDetail = looped ? s.LoopDetail : cur.NearestLoopDetail;
                // Dispatch fan-out (A1/D3): when the reaching edge fanned a virtual/base method out to
                // >1 targets, this node is reached via that fan-out, not a real call — tag it with the
                // dispatch SOURCE (s.Via, the virtual method) and degree. A single-target dispatch
                // (degree 1) is deterministic, so it's treated like a real call. Otherwise inherit the
                // tag, so the whole fanned-out subtree (BFS-shortest) carries it — unless reached more
                // directly elsewhere.
                var fannedOut = s.Fanout > 1;
                var via = fannedOut ? s.Via : cur.DispatchVia;
                var degree = fannedOut ? s.Fanout : cur.DispatchDegree;
                // HandoffVia (clone of the DispatchVia inheritance): set when THIS edge is a handoff,
                // else inherited from the parent — so the whole scheduled subtree carries the
                // provenance, and a node first reached synchronously (shorter route) carries none.
                var handoffVia = s.HandoffVia ?? cur.HandoffVia;
                // Dispatch-basis inheritance: "heuristic" is STICKY (one guessed hop taints the whole
                // downstream reach), otherwise this edge's basis or the inherited one.
                var basis = s.Basis == "heuristic" || cur.DispatchBasis == "heuristic" ? "heuristic" : (s.Basis ?? cur.DispatchBasis);
                info[s.Node] = new ReachInfo(
                    Depth: cur.Depth + 1,
                    LoopNesting: nesting,
                    NearestLoopKind: nearKind,
                    NearestLoopDetail: nearDetail,
                    DispatchVia: via,
                    DispatchDegree: degree,
                    HandoffVia: handoffVia,
                    DispatchBasis: basis
                );
                Enqueue(s.Node, s.OutReceiver, targetFromDispatch);
            }
        }

        return info;
    }

    // Upper bound on a node's accumulated type-arg binding — a runaway guard for whole-codebase reaches
    // (real per-node bindings are a handful of types; entity-construct fan-outs are ~tens). At the cap,
    // growth stops: the binding stays recall-safe (it only ever narrows when a candidate matches, never
    // empties the set), so a saturated binding simply narrows less, never wrongly.
    private const int MaxBinding = 256;

    // Upper bound on the distinct DISPATCH CONTEXTS one node is expanded under in a single traversal — the
    // runaway guard for the context-aware expansion memo. A node's dispatch fan depends on the receiver it
    // was reached with, so a shared virtual hub legitimately needs one expansion per receiver that resolves
    // to a DIFFERENT override; in practice that is a handful (the MedDBase `EntityBase.Save` case that
    // motivated this is ~tens of call sites over far fewer distinct fans, since the fan-key check collapses
    // receivers that resolve identically). The cap only bites on a pathologically shared hub, where it
    // degrades to the OLD behaviour for the contexts past the cap — an under-approximation, but a bounded
    // and disclosed one rather than an unbounded walk.
    private const int MaxDispatchContexts = 64;

    // Unions `incoming` into the carried binding of `node`, creating the entry if absent. Returns true
    // when the node's binding actually GREW (new concrete types added) — the signal to re-enqueue a
    // shared generic hub so its dispatch re-expands under the larger binding (the closure fixpoint).
    private static bool MergeBinding(Dictionary<string, HashSet<string>?> bindingOf, string node, IReadOnlyCollection<string>? incoming)
    {
        var hadEntry = bindingOf.TryGetValue(node, out var existing);
        if (incoming is null || incoming.Count == 0)
        {
            if (!hadEntry)
            {
                bindingOf[node] = null; // record the node as reached with no binding (full CHA)
            }

            return false;
        }
        if (!hadEntry || existing is null)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in incoming)
            {
                if (set.Count >= MaxBinding)
                {
                    break;
                }

                set.Add(t);
            }
            bindingOf[node] = set;
            return set.Count > 0;
        }
        var before = existing.Count;
        foreach (var t in incoming)
        {
            if (existing.Count >= MaxBinding)
            {
                break;
            }

            existing.Add(t);
        }
        return existing.Count > before;
    }

    // Builds the call TREE rooted at every node matching `fromPattern` (rig tree). Same edge model
    // as Reaches/Find (direct calls + interface->impl + base->override dispatch, with loop context),
    // but materialized as a tree for rendering. Each method is EXPANDED ONCE globally: the first time
    // it's reached (shallowest depth, source order among same-depth peers) its children are built;
    // later encounters become a Truncated leaf ("⋯elided"), so a cycle or a heavily-shared callee can't
    // blow the tree up. maxDepth bounds depth; maxNodes bounds total emitted nodes (a Truncated leaf
    // is emitted at the cap). Returns one TraceNode per root.
    //
    // BFS (shallowest-first) ensures a shallow direct call is NEVER stolen by a deep infra seam that
    // happened to be expanded first in DFS source order: BFS processes nodes at increasing depth, and
    // among same-depth peers it preserves source order (Successors yields children in line order).
    // Cut + context shaping is carried on `graph` (set by ShapeGraph at load) and applied via BuildIndex:
    // a cut node is expanded as a leaf — its own effects are visible but its subtree is not walked.
    // Default node budget for BuildTree — the safety cap `tree` runs under when --limit is absent.
    public const int DefaultTreeNodeBudget = 50000;

    public static IReadOnlyList<TraceNode> BuildTree(
        FactGraphData graph,
        string fromPattern,
        int maxDepth = 50,
        int maxNodes = DefaultTreeNodeBudget,
        TraversalMode mode = TraversalMode.SyncCut
    )
    {
        var index = BuildIndex(graph);

        // Expansion memo, keyed by DISPATCH CONTEXT (DispatchContextKey), not by bare symbol. A node's
        // subtree is its body (fixed) plus its dispatch fan (varies with the receiver that reached it), so
        // a symbol-only key made the FIRST occurrence of a virtual hub win with ITS receiver's fan and
        // collapsed every later occurrence — resolving to a DIFFERENT override — into a "⋯elided" leaf.
        // For a node with no fan the key IS the symbol, so non-virtual nodes are still expanded once.
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        // How many distinct dispatch contexts each symbol has already been expanded under, so a hub reached
        // under very many receivers can't multiply the forest without bound (see MaxDispatchContexts).
        var contextsPerSymbol = new Dictionary<string, int>(StringComparer.Ordinal);
        var budget = maxNodes;

        // Mutable build node — built during BFS, converted to immutable TraceNode at the end.
        // Receiver/Binding are the narrowing contexts inherited from the edge that enqueued this node,
        // passed into Successors when this node is expanded so its own dispatch is narrowed correctly.
        var mutableRoots = new List<MutableNode>();
        // DEPTH-FIRST, PRE-ORDER traversal (a stack, children pushed in reverse so they pop in render
        // order). This makes `expanded` fill in exactly top-to-bottom reading order, so the FIRST visual
        // occurrence of a shared symbol is the one expanded and every LATER occurrence is the "⋯elided"
        // leaf — the marker always refers to a subtree already shown ABOVE it. (A breadth-first walk
        // expanded whichever occurrence was shallowest, which could render BELOW a deeper twin, leaving
        // the "⋯elided" reading before its expansion.)
        var stack = new Stack<MutableNode>();

        var matched = MatchNodes(index.Nodes, fromPattern).ToHashSet(StringComparer.Ordinal);
        foreach (var root in matched.Where(n => !IsContainedLambdaOfMatched(n, matched)).OrderBy(n => n, StringComparer.Ordinal))
        {
            var node = new MutableNode(
                symbol: root,
                edgeKind: "entry",
                loopKind: null,
                loopDetail: null,
                enclosingGuards: null,
                depth: 0,
                handoffVia: null,
                dispatchBasis: null,
                fanout: 0,
                receiver: null,
                binding: null,
                declaringTypeArgBinding: null,
                methodTypeArgBinding: null,
                callFile: null,
                callLine: 0
            );
            mutableRoots.Add(node);
        }
        // Push roots reversed so the first root's whole subtree is walked before the next root's.
        for (var i = mutableRoots.Count - 1; i >= 0; i--)
        {
            stack.Push(mutableRoots[i]);
        }

        while (stack.Count > 0 && budget > 0)
        {
            var n = stack.Pop();
            // A node becomes part of the emitted forest only when the DFS actually visits it. Successor
            // discovery stages every sibling before pushing them; without this bit, siblings still sitting
            // on the stack when the budget expires were converted into ordinary leaf TraceNodes and rendered
            // beyond --limit even though traversal never visited them.
            n.Visited = true;
            budget--;

            // Resolve this visit's dispatch fan ONCE: it both keys the expansion memo below and is handed
            // to Successors, so the fan is computed a single time per visit (as before the memo was
            // context-aware) rather than once for the key and again inside Successors.
            var fromDispatch = IsDispatchEdgeKind(n.EdgeKind) || n.ViaNonVirtual;
            var dispatch = ResolveDispatch(n.Symbol, index, n.Receiver, n.Binding, fromDispatch);
            var contextKey = DispatchContextKey(n.Symbol, dispatch);

            // Already expanded IN THIS DISPATCH CONTEXT (cycle / shared callee reached the same way), at
            // depth cap, or out of budget: mark as truncated and do NOT expand. budget check is re-checked
            // after decrement. Cause is attributed by PRECEDENCE: AlreadyExpanded wins when multiple
            // conditions apply (it is the meaningful redundancy signal); DepthCapped next; BudgetCapped last.
            var contextCount = contextsPerSymbol.TryGetValue(n.Symbol, out var seenContexts) ? seenContexts : 0;
            if (expanded.Contains(contextKey) || contextCount >= MaxDispatchContexts)
            {
                n.Truncated = true;
                n.TruncationCause = TruncationCause.AlreadyExpanded;
                continue;
            }
            else if (n.Depth >= maxDepth)
            {
                n.Truncated = true;
                n.TruncationCause = TruncationCause.DepthCapped;
                continue;
            }
            else if (budget <= 0)
            {
                n.Truncated = true;
                n.TruncationCause = TruncationCause.BudgetCapped;
                continue;
            }

            expanded.Add(contextKey);
            contextsPerSymbol[n.Symbol] = contextCount + 1;

            // Traversal cut: the node is a leaf — emit it but don't walk its successors.
            // The cut is checked AFTER marking expanded so the node itself is rendered correctly
            // (its own effects are visible); only its successors are suppressed.
            if (index.ApplyTraversalCuts && index.IsTraversalCut(n.Symbol))
            {
                continue;
            }

            foreach (
                var s in Successors(
                    current: n.Symbol,
                    index: index,
                    incomingReceiver: n.Receiver,
                    incomingBinding: n.Binding,
                    mode: mode,
                    fromDispatch: fromDispatch,
                    resolvedDispatch: dispatch
                )
            )
            {
                // Collapse identical sibling edges: a generic method or bodied accessor called N times
                // under one parent resolves to one symbol → N edges that would render byte-identically
                // (1 expansion + N-1 "⋯elided"). Fold them into a single kid carrying a call-site count.
                // Keyed on every field that affects the rendered line so only true duplicates merge.
                // Manual scan rather than Kids.FirstOrDefault(k => ...): the lambda captures `s`, so the
                // LINQ form heap-allocated a closure + delegate on every successor edge of every node.
                MutableNode? dup = null;
                foreach (var k in n.Kids)
                {
                    if (
                        k.Symbol == s.Node
                        && k.EdgeKind == s.Kind
                        && k.LoopKind == s.LoopKind
                        && k.LoopDetail == s.LoopDetail
                        && k.HandoffVia == s.HandoffVia
                        && k.Fanout == s.Fanout
                        && k.DispatchBasis == s.Basis
                        // Two sites calling the same callee under DIFFERENT guards (e.g. `Save(x); if(d) Save(x);`)
                        // are distinct conditionalities — keep them separate so each renders its own ⎇.
                        && k.EnclosingGuards == s.EnclosingGuards
                    )
                    {
                        dup = k;
                        break;
                    }
                }

                if (dup is not null)
                {
                    dup.CallSites++;
                    continue;
                }
                var kid = new MutableNode(
                    symbol: s.Node,
                    edgeKind: s.Kind,
                    loopKind: s.LoopKind,
                    loopDetail: s.LoopDetail,
                    enclosingGuards: s.EnclosingGuards,
                    depth: n.Depth + 1,
                    handoffVia: s.HandoffVia,
                    dispatchBasis: s.Basis,
                    fanout: s.Fanout,
                    receiver: s.OutReceiver,
                    binding: s.OutBinding,
                    declaringTypeArgBinding: s.OutDeclaringBinding,
                    methodTypeArgBinding: s.OutMethodBinding,
                    callFile: s.File,
                    callLine: s.Line,
                    viaNonVirtual: s.OutNonVirtual
                );
                n.Kids.Add(kid);
            }

            // Push this node's children reversed, so the first child (render order) is popped — and thus
            // expanded — next: a pre-order depth-first walk.
            for (var i = n.Kids.Count - 1; i >= 0; i--)
            {
                stack.Push(n.Kids[i]);
            }
        }

        return mutableRoots.Where(n => n.Visited).Select(n => ToTraceNode(n, index.ExternalLeaves)).ToArray();
    }

    // Mutable node used during BFS tree construction; converted to immutable TraceNode afterward.
    private sealed class MutableNode
    {
        public readonly string Symbol;
        public readonly string EdgeKind;
        public readonly string? LoopKind;
        public readonly string? LoopDetail;

        // CFG control-dependence guards of the edge that reached this node (CallEdge.EnclosingGuards):
        // the branch predicates gating the call within the parent. Null == must-run. RENDERING only
        // (-> TraceNode.EnclosingGuards), surfaced by `tree --guards` as the ⎇ analog of 🔁.
        public readonly string? EnclosingGuards;
        public readonly int Depth;
        public readonly string? HandoffVia;
        public readonly string? DispatchBasis;
        public readonly int Fanout;

        // True when the reaching edge was a NON-VIRTUAL `base.M()` call — suppresses this node's own
        // override-dispatch fan when expanded (one-hop, like a node reached via a dispatch edge).
        public readonly bool ViaNonVirtual;

        // Narrowing contexts carried to Successors when this node is expanded:
        public readonly string? Receiver;
        public readonly IReadOnlyCollection<string>? Binding;

        // Generic monomorphization bindings of the reaching edge — RENDERING only (-> TraceNode).
        public readonly string? DeclaringTypeArgBinding;
        public readonly string? MethodTypeArgBinding;

        // The reaching edge's call site (File/Line) — RENDERING only (-> TraceNode.CallFile/CallLine).
        public readonly string? CallFile;
        public readonly int CallLine;
        public bool Visited;
        public bool Truncated;
        public TruncationCause TruncationCause;

        // Distinct call sites under this node's parent that produced an identical edge (collapsed
        // siblings). Bumped instead of adding a duplicate kid; rendered as "×N calls".
        public int CallSites = 1;
        public readonly List<MutableNode> Kids = new List<MutableNode>();

        public MutableNode(
            string symbol,
            string edgeKind,
            string? loopKind,
            string? loopDetail,
            string? enclosingGuards,
            int depth,
            string? handoffVia,
            string? dispatchBasis,
            int fanout,
            string? receiver,
            IReadOnlyCollection<string>? binding,
            string? declaringTypeArgBinding,
            string? methodTypeArgBinding,
            string? callFile,
            int callLine,
            bool viaNonVirtual = false
        )
        {
            Symbol = symbol;
            EdgeKind = edgeKind;
            ViaNonVirtual = viaNonVirtual;
            LoopKind = loopKind;
            LoopDetail = loopDetail;
            EnclosingGuards = enclosingGuards;
            Depth = depth;
            HandoffVia = handoffVia;
            DispatchBasis = dispatchBasis;
            Fanout = fanout;
            Receiver = receiver;
            Binding = binding;
            DeclaringTypeArgBinding = declaringTypeArgBinding;
            MethodTypeArgBinding = methodTypeArgBinding;
            CallFile = callFile;
            CallLine = callLine;
        }
    }

    // `externalLeaves` is the index's admitted-external node set (external-node admission): stamped onto
    // each TraceNode so the marker survives into the CACHED forest, where no graph is available to ask.
    private static TraceNode ToTraceNode(MutableNode n, IReadOnlySet<string> externalLeaves)
    {
        if (n.Truncated)
        {
            return new TraceNode(
                SymbolId: n.Symbol,
                EdgeKind: n.EdgeKind,
                LoopKind: n.LoopKind,
                LoopDetail: n.LoopDetail,
                Children: EmptyNodes,
                Truncated: true,
                TruncationCause: n.TruncationCause,
                Fanout: n.Fanout,
                HandoffVia: n.HandoffVia,
                DispatchBasis: n.DispatchBasis,
                CallSites: n.CallSites,
                DeclaringTypeArgBinding: n.DeclaringTypeArgBinding,
                MethodTypeArgBinding: n.MethodTypeArgBinding,
                CallFile: n.CallFile,
                CallLine: n.CallLine,
                EnclosingGuards: n.EnclosingGuards,
                IsExternal: externalLeaves.Contains(n.Symbol)
            );
        }

        // Successors are staged as siblings before the DFS visits them. Drop every never-visited child:
        // --limit is a strict TraceNode bound, and an unvisited successor is omitted work rather than a real
        // leaf. The final visited node already carries BudgetCapped, preserving an explicit stop marker.
        var visitedChildren = n.Kids.Where(k => k.Visited).ToArray();
        var children = visitedChildren.Length == 0 ? EmptyNodes : visitedChildren.Select(k => ToTraceNode(k, externalLeaves)).ToArray();

        return new TraceNode(
            SymbolId: n.Symbol,
            EdgeKind: n.EdgeKind,
            LoopKind: n.LoopKind,
            LoopDetail: n.LoopDetail,
            Children: children,
            Fanout: n.Fanout,
            HandoffVia: n.HandoffVia,
            DispatchBasis: n.DispatchBasis,
            CallSites: n.CallSites,
            DeclaringTypeArgBinding: n.DeclaringTypeArgBinding,
            MethodTypeArgBinding: n.MethodTypeArgBinding,
            CallFile: n.CallFile,
            CallLine: n.CallLine,
            EnclosingGuards: n.EnclosingGuards,
            IsExternal: externalLeaves.Contains(n.Symbol)
        );
    }

    private static readonly IReadOnlyList<TraceNode> EmptyNodes = [];

    // Multi-source forward reachability: the union of everything reachable from ANY of the given root
    // symbol IDs, using the same edge model as Reaches/Find/tree (direct calls + method-group/ctor
    // edges + interface->impl and base->override dispatch). Roots are matched by EXACT SymbolId (not
    // substring) — callers pass concrete entry-point DocIDs. Unknown root ids (not present as graph
    // nodes) are skipped. Underpins the unreachable-symbol / dead-code finder: dead = first-party
    // methods − this set − the roots themselves.
    public static HashSet<string> ReachableFromAll(
        FactGraphData graph,
        IEnumerable<string> roots,
        int maxNodes = 2_000_000,
        TraversalMode mode = TraversalMode.SyncCut
    )
    {
        var index = BuildIndex(graph);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var receiverOf = new Dictionary<string, string?>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var root in roots)
        {
            if (index.Nodes.Contains(root) && seen.Add(root))
            {
                receiverOf[root] = null;
                queue.Enqueue(root);
            }
        }

        while (queue.Count > 0 && seen.Count < maxNodes)
        {
            var current = queue.Dequeue();
            foreach (
                var s in Successors(
                    current: current,
                    index: index,
                    incomingReceiver: receiverOf.TryGetValue(key: current, value: out var rc) ? rc : null,
                    incomingBinding: null,
                    mode: mode
                )
            )
            {
                if (seen.Add(s.Node))
                {
                    receiverOf[s.Node] = s.OutReceiver;
                    queue.Enqueue(s.Node);
                }
            }
        }

        return seen;
    }

    // Reverse reachability — every method that can REACH any node matching toPattern (transitive
    // callers), keyed to its shortest reverse hop count. Inverts Successors: direct caller edges,
    // plus the reverse of the dispatch hops — an impl method is reached via its interface's
    // same-named method, an override via its base's. Powers `rig callers` ("which entry points
    // touch this method"), and underpins the planned unreachable-symbol (dead-code) finder.
    public static IReadOnlyDictionary<string, int> ReachedBy(
        FactGraphData graph,
        string toPattern,
        int maxDepth = 20,
        int maxNodes = 20000,
        bool narrowDispatch = true,
        TraversalMode mode = TraversalMode.SyncCut
    )
    {
        var index = BuildIndex(graph, narrowDispatch);
        var rev = BuildReverseMaps(graph, narrowDispatch, mode);
        return ReachedByCore(index, rev, toPattern, maxDepth, maxNodes);
    }

    // The graph's MATCHED SEED NODES for a pattern — precisely the depth-0 entries of a ReachedBy closure,
    // and nothing else. `ReachedBy(graph, p, maxDepth: 0).Where(kv => kv.Value == 0)` returns the same set
    // (both seed through the one MatchNodes matcher over the same index.Nodes universe), but it additionally
    // runs BuildReverseMaps — a whole-graph receiver-blind dispatch scan that dominates the call and that a
    // depth-0 answer never reads a single edge of. Exposed for the callers path, which needs the matched
    // target ids beside its own traversal and was paying for a second reverse-map build to get them.
    // Semantics-free: no traversal, no reachability, just "which nodes does this pattern name".
    public static IReadOnlyList<string> MatchedNodes(FactGraphData graph, string pattern) => MatchNodes(BuildIndex(graph).Nodes, pattern);

    // The reverse-BFS core, factored out so a caller that ALREADY holds the index + reverse maps can reuse
    // them instead of rebuilding. EntryRootsReaching builds both for its own no-predecessor root check and
    // then needs this same closure — calling ReachedBy() rebuilt index + reverse maps a second time, and
    // BuildReverseMaps does a whole-graph receiver-blind dispatch scan, so that was the dominant cost of
    // `callers --roots`. Passing the prebuilt pair here halves it.
    private static IReadOnlyDictionary<string, int> ReachedByCore(
        GraphIndex index,
        ReverseMaps rev,
        string toPattern,
        int maxDepth,
        int maxNodes
    )
    {
        var depthOf = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var start in MatchNodes(index.Nodes, toPattern))
        {
            if (depthOf.ContainsKey(start))
            {
                continue;
            }

            depthOf[start] = 0;
            queue.Enqueue(start);
        }

        while (queue.Count > 0 && depthOf.Count < maxNodes)
        {
            var current = queue.Dequeue();
            var depth = depthOf[current];
            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var (pred, _) in Predecessors(current, index, rev))
            {
                if (depthOf.ContainsKey(pred))
                {
                    continue;
                }

                depthOf[pred] = depth + 1;
                queue.Enqueue(pred);
            }
        }

        return depthOf;
    }

    // Multi-source reverse reachability — like ReachedBy, but seeded from a SET of EXACT SymbolIds
    // (not a substring pattern), returning the union of everything that can reach ANY of them keyed
    // to its shortest reverse hop count to the nearest seed. Mirrors ReachableFromAll's exact-id
    // seeding on the reverse maps: unknown seed ids (not graph nodes) are skipped. This is the engine
    // `rig impact` reverse-reaches from a diff's changed-method set with — one index/reverse-map build
    // shared across all seeds, instead of calling ReachedBy once per changed method (which rebuilds
    // both each time). Same Predecessors edge model as ReachedBy, so the closure is identical.
    public static IReadOnlyDictionary<string, int> ReachedByAny(
        FactGraphData graph,
        IEnumerable<string> seeds,
        int maxDepth = 20,
        int maxNodes = 20000,
        bool narrowDispatch = true,
        TraversalMode mode = TraversalMode.SyncCut
    )
    {
        var index = BuildIndex(graph, narrowDispatch);
        var rev = BuildReverseMaps(graph, narrowDispatch, mode);

        var depthOf = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var seed in seeds)
        {
            // Seed by EXACT id (the changed methods are concrete DocIDs). A seed absent from the graph
            // (e.g. a method with no edges either way) is simply not a traversal node — skip it.
            if (index.Nodes.Contains(seed) && !depthOf.ContainsKey(seed))
            {
                depthOf[seed] = 0;
                queue.Enqueue(seed);
            }
        }

        while (queue.Count > 0 && depthOf.Count < maxNodes)
        {
            var current = queue.Dequeue();
            var depth = depthOf[current];
            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var (pred, _) in Predecessors(current, index, rev))
            {
                if (depthOf.ContainsKey(pred))
                {
                    continue;
                }

                depthOf[pred] = depth + 1;
                queue.Enqueue(pred);
            }
        }

        return depthOf;
    }

    // LABELLED multi-source reverse reachability: k independent ReachedByAny closures computed in ONE pass.
    // Result[i] is exactly what ReachedByAny(graph, seedsByLabel[i], …) returns — same keys, same depths —
    // which is what the equivalence test pins. That equality is exact for maxDepth; maxNodes is a RESOURCE
    // bound counting nodes TOUCHED by any label, so a bounded fused walk can admit a different node set than
    // k bounded separate walks. Every caller today passes int.MaxValue for both.
    //
    // Why fuse instead of looping: the index and reverse maps are already memoised per graph, but the BFS and
    // its predecessor enumeration are not, so k labels meant k full walks of a 442k-node graph. Here a node
    // carries a MASK of the labels whose distance it just improved, so one predecessor enumeration serves
    // every label travelling with it — and labels do travel together, because the callers that reach a db
    // effect are largely the callers that reach a cache effect. The fallback is graceful: labels that spread
    // differently simply re-enqueue the node with a smaller mask, which is the k-walk cost and no worse.
    //
    // Cap: 64 labels (one ulong of mask). A caller with more families than that has a taxonomy problem, not
    // a traversal problem, so this throws rather than silently truncating.
    public static IReadOnlyList<IReadOnlyDictionary<string, int>> ReachedByLabelledSeeds(
        FactGraphData graph,
        IReadOnlyList<IReadOnlyCollection<string>> seedsByLabel,
        int maxDepth = 20,
        int maxNodes = 20000,
        bool narrowDispatch = true,
        TraversalMode mode = TraversalMode.SyncCut,
        DispatchAdmission dispatch = DispatchAdmission.All
    )
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(seedsByLabel);
        if (seedsByLabel.Count > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seedsByLabel),
                seedsByLabel.Count,
                "At most 64 labels can be traversed in one pass."
            );
        }

        var labels = seedsByLabel.Count;
        if (labels == 0)
        {
            return [];
        }

        var index = BuildIndex(graph, narrowDispatch);
        var rev = BuildReverseMaps(graph, narrowDispatch, mode);

        // int.MaxValue = "this label has not reached this node". One array per node rather than one dictionary
        // per label: the node key is interned once and the label lookup is an index.
        var depths = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var queue = new Queue<(string Node, ulong Mask)>();

        int[] Slot(string node)
        {
            if (depths.TryGetValue(node, out var existing))
            {
                return existing;
            }

            var fresh = new int[labels];
            Array.Fill(fresh, int.MaxValue);
            depths.Add(node, fresh);
            return fresh;
        }

        for (var label = 0; label < labels; label++)
        {
            // A seed absent from the graph (a method with no edges either way) is not a traversal node —
            // skipped, exactly as ReachedByAny does.
            foreach (var seed in seedsByLabel[label])
            {
                if (!index.Nodes.Contains(seed))
                {
                    continue;
                }

                var slot = Slot(seed);
                if (slot[label] == 0)
                {
                    continue;
                }

                slot[label] = 0;
                queue.Enqueue((seed, 1UL << label));
            }
        }

        while (queue.Count > 0 && depths.Count < maxNodes)
        {
            var (current, mask) = queue.Dequeue();
            var currentDepths = depths[current];

            // Only the labels still in the mask can propagate from here, and only while under maxDepth.
            var travelling = 0UL;
            for (var label = 0; label < labels; label++)
            {
                if ((mask & (1UL << label)) != 0 && currentDepths[label] < maxDepth)
                {
                    travelling |= 1UL << label;
                }
            }

            if (travelling == 0)
            {
                continue;
            }

            foreach (var (pred, _) in Predecessors(current, index, rev, dispatch))
            {
                var predDepths = Slot(pred);
                var improved = 0UL;
                for (var label = 0; label < labels; label++)
                {
                    if ((travelling & (1UL << label)) == 0)
                    {
                        continue;
                    }

                    var candidate = currentDepths[label] + 1;
                    if (candidate < predDepths[label])
                    {
                        predDepths[label] = candidate;
                        improved |= 1UL << label;
                    }
                }

                if (improved != 0)
                {
                    queue.Enqueue((pred, improved));
                }
            }
        }

        var result = new IReadOnlyDictionary<string, int>[labels];
        for (var label = 0; label < labels; label++)
        {
            var perLabel = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in depths)
            {
                if (pair.Value[label] != int.MaxValue)
                {
                    perLabel.Add(pair.Key, pair.Value[label]);
                }
            }

            result[label] = perLabel;
        }

        return result;
    }

    // Entry-point CANDIDATES that reach toPattern: the reachable methods with NO predecessor at all
    // (no caller, not an impl of a called interface, not an override of a called base) — the tops of
    // the reverse closure, i.e. methods invoked only by the framework / DI / reflection / externally.
    // The honest static approximation of "which entry points touch this method".
    public static IReadOnlyList<string> EntryRootsReaching(
        FactGraphData graph,
        string toPattern,
        int maxDepth = 20,
        int maxNodes = 20000,
        TraversalMode mode = TraversalMode.SyncCut
    )
    {
        var index = BuildIndex(graph);
        var rev = BuildReverseMaps(graph, narrowDispatch: true, mode);
        // Reuse the index + reverse maps just built (for the Predecessors root check below) — ReachedByCore
        // takes them prebuilt, so the closure shares this one build instead of ReachedBy rebuilding both.
        var reachable = ReachedByCore(index, rev, toPattern, maxDepth, maxNodes);
        var roots = new List<string>();
        foreach (var m in reachable.Keys)
        {
            if (!Predecessors(m, index, rev).Any())
            {
                roots.Add(m);
            }
        }

        roots.Sort(StringComparer.Ordinal);
        return roots;
    }

    // Materialises EVERY synthetic dispatch edge in the graph — (sourceMethod -> targetMethod, kind,
    // basis) for interface->impl and base-virtual/abstract->override: the Roslyn-MINED edges
    // (Basis="roslyn", the forward closure of dispatch_facts) plus the flagged heuristic fallback
    // (error-type simple-name recovery + name/arity CHA for unmined members, Basis="heuristic"), for
    // all method nodes, receiver-blind. This feeds the precomputed `dispatch_edges` table, the SOUND
    // SUPERSET that bounds the SQL reachability load — narrowing happens only in the in-memory edge
    // traversal (Successors), so dispatch_edges must stay receiver-blind so the bounded subgraph it
    // produces still contains every edge a narrowed traversal could visit. Deduped per source;
    // sources are distinct.
    public static IEnumerable<(string From, string To, string Kind, string Basis)> AllDispatchEdges(FactGraphData graph)
    {
        var index = BuildIndex(graph, narrowDispatch: false);
        foreach (var node in index.Nodes)
        foreach (var target in DispatchTargets(node, index, receiverType: null))
        {
            yield return (node, target.Node, target.Kind, target.Basis);
        }
    }

    // The direct call edges as (caller -> callee, kind), deduped — the other half of the graph the
    // SQL reachability path traverses. Mirrors graph.CallEdges (already filtered to first-party
    // invocation/methodGroup/ctor at load), exposed here so the materialiser and the oracle agree.
    public static IEnumerable<(
        string From,
        string To,
        string Kind,
        string? File,
        int Line,
        string? LoopKind,
        string? LoopDetail,
        string? ReceiverType,
        string? HandoffDispatcher,
        string? DeliveryPrecision,
        bool NonVirtual,
        // CFG control-dependence guard set of the call SITE (CallEdge.EnclosingGuards) — materialized into
        // the derived call_edges view so the SQL-bounded graph load round-trips it (the tree --guards glyph).
        string? EnclosingGuards
    )> AllCallEdges(FactGraphData graph)
    {
        foreach (var edge in graph.CallEdges)
        {
            yield return (
                edge.Caller,
                edge.Callee,
                edge.Kind,
                edge.FilePath,
                edge.Line,
                edge.LoopKind,
                edge.LoopDetail,
                edge.ReceiverType,
                edge.HandoffDispatcher,
                edge.DeliveryPrecision,
                edge.NonVirtual,
                edge.EnclosingGuards
            );
        }
    }

    // Transitive strict descendants of a type, memoised. Keyed on the generic-stripped id so the
    // instantiated/open-generic forms share one cache entry.
    private static HashSet<string> Descendants(string typeId, GraphIndex index)
    {
        var key = TypeClosure.StripGeneric(typeId);
        return index.DescendantsCache.GetOrAdd(key, _ => TypeClosure.ComputeStrictDescendants(index.StrippedBaseEdges, new[] { typeId }));
    }

    private static void Enqueue(
        Dictionary<
            string,
            (
                string From,
                string Kind,
                string? File,
                int Line,
                string? LoopKind,
                string? LoopDetail,
                int Fanout,
                string? HandoffVia,
                string? Basis
            )?
        > parent,
        Queue<(string, int)> queue,
        string node,
        string from,
        string kind,
        string? file,
        int line,
        string? loopKind,
        string? loopDetail,
        int fanout,
        string? handoffVia,
        string? basis,
        int depth
    )
    {
        if (parent.ContainsKey(node))
        {
            return;
        }

        parent[node] = (from, kind, file, line, loopKind, loopDetail, fanout, handoffVia, basis);
        queue.Enqueue((node, depth + 1));
    }

    private static IReadOnlyList<PathStep> Reconstruct(
        Dictionary<
            string,
            (
                string From,
                string Kind,
                string? File,
                int Line,
                string? LoopKind,
                string? LoopDetail,
                int Fanout,
                string? HandoffVia,
                string? Basis
            )?
        > parent,
        string target
    )
    {
        var steps = new List<PathStep>();
        var node = target;
        while (true)
        {
            var link = parent[node];
            steps.Add(
                new PathStep(
                    SymbolId: node,
                    Kind: link?.Kind ?? "entry",
                    FilePath: link?.File,
                    Line: link?.Line ?? 0,
                    LoopKind: link?.LoopKind,
                    LoopDetail: link?.LoopDetail,
                    Fanout: link?.Fanout ?? 0,
                    HandoffVia: link?.HandoffVia,
                    DispatchBasis: link?.Basis
                )
            );
            if (link is null)
            {
                break;
            }

            node = link.Value.From;
        }
        steps.Reverse();
        return steps;
    }

    // "M:Ns.Type.Member(args)" -> ("T:Ns.Type", "Member"). Null when not a method DocID.
    private static (string TypeId, string Name)? ParseMethod(string docId)
    {
        if (!docId.StartsWith("M:", StringComparison.Ordinal))
        {
            return null;
        }

        var body = docId.Substring(2);
        var paren = body.IndexOf('(');
        if (paren >= 0)
        {
            body = body.Substring(startIndex: 0, length: paren);
        }

        var lastDot = body.LastIndexOf('.');
        if (lastDot < 0)
        {
            return null;
        }

        return ("T:" + body.Substring(startIndex: 0, length: lastDot), body.Substring(lastDot + 1));
    }

    // Parameter ARITY of a method DocID: the number of top-level parameters in its "(...)" list, or 0
    // when there is none ("M:T.M" / "M:T.M()"). Commas inside generic-argument braces "{...}" or array
    // brackets "[...]" don't count (e.g. "Func{A,B,C}" is ONE parameter). Used to stop name-only
    // interface/override dispatch from matching a same-named OVERLOAD with a different signature.
    private static int ParamArity(string docId)
    {
        var open = docId.IndexOf('(');
        if (open < 0)
        {
            return 0;
        }

        var close = docId.LastIndexOf(')');
        if (close <= open + 1)
        {
            return 0; // "()" — no parameters
        }

        var count = 1;
        var depth = 0;
        for (var i = open + 1; i < close; i++)
        {
            var c = docId[i];
            if (c is '{' or '[' or '(')
            {
                depth++;
            }
            else if (c is '}' or ']' or ')')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                count++;
            }
        }
        return count;
    }

    private static bool Contains(string value, string pattern) => value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

    // Resolve a from/to PATTERN to the set of node ids it selects, with EXACT MATCH WINS: if the pattern
    // exactly names one or more nodes — by full DocID, or by the `M:`-stripped, param-free FQN (the form
    // `rig` renders and a user pastes back) — only those are returned; otherwise the pattern is the usual
    // case-insensitive SUBSTRING (the partial-name convenience). This stops a fully-qualified name from also
    // dragging in every member it is a prefix of: `…Search.Proceed` resolves to exactly `Proceed`, not also
    // `Proceed`'s prefix-twin `ProceedToConfirmationScreen`. A partial/short pattern never equals a full
    // namespaced FQN, so substring behaviour is preserved for it. One pass collects both buckets; the exact
    // bucket wins when non-empty. Shared by every seed site (tree/reaches/callers/path roots + path target).
    internal static IReadOnlyList<string> MatchNodes(IEnumerable<string> nodes, string pattern)
    {
        var exact = new List<string>();
        var substring = new List<string>();
        foreach (var n in nodes)
        {
            if (IsExactNodeMatch(n, pattern))
            {
                exact.Add(n);
            }
            else if (Contains(value: n, pattern: pattern))
            {
                substring.Add(n);
            }
        }

        return exact.Count > 0 ? exact : substring;
    }

    // A node matches a pattern EXACTLY when the pattern is its concrete graph id, its canonical source-member
    // DocID, or that canonical member's param-free FQN. Static monomorphization redirects concrete generic calls
    // to `{baseId}~mono<binding>` nodes while retaining the open base body as a sound fallback. A user naming the
    // open member therefore names BOTH representations: return the actual graph ids so traversal keeps each
    // concrete binding, but compare through BaseOf so exact-match-wins does not discard every `~mono` execution
    // into its substring bucket. Direct synthetic-id matching remains exact to one instantiation.
    private static bool IsExactNodeMatch(string node, string pattern)
    {
        var canonical = MonomorphizedNodeId.BaseOf(node);
        return string.Equals(node, pattern, StringComparison.OrdinalIgnoreCase)
            || string.Equals(canonical, pattern, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ParamFreeFqn(canonical), pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string ParamFreeFqn(string node)
    {
        // Ambiguity disclosure is about source members, not query-local monomorphized executions. Canonicalize
        // before stripping parameters so a parameterless `M:Repo.Save``1~mono<...>` does not appear as a second
        // conceptual target and a signature-bearing id cannot depend accidentally on `(` preceding `~mono`.
        var canonical = MonomorphizedNodeId.BaseOf(node);
        var body = canonical.Length >= 2 && canonical[1] == ':' ? canonical[2..] : canonical;
        var paren = body.IndexOf('(', StringComparison.Ordinal);
        return paren >= 0 ? body[..paren] : body;
    }

    // A synthetic lambda node id is `{containerMemberId}~λ{ordinal}` (FactExtractor). When the root pattern
    // matches a method AND its inline lambdas (e.g. `tree "Foo"` matches Foo, Foo~λ0, Foo~λ1), the lambdas
    // are NOT independent roots: each already renders inline under its container, so re-rooting it would
    // emit a spurious top-level `⋯elided` (the container's expansion already marked it seen). Drop a matched
    // lambda only when its container ALSO matched; a lambda whose container did not match (e.g. a promoted
    // async-handoff entry point targeted on its own) stays a legitimate root.
    private static bool IsContainedLambdaOfMatched(string nodeId, HashSet<string> matched)
    {
        var marker = nodeId.IndexOf("~λ", StringComparison.Ordinal);
        return marker > 0 && matched.Contains(nodeId.Substring(0, marker));
    }

    // The DISTINCT conceptual targets a pattern resolves to — the ambiguity-disclosure set behind the
    // CLI's "pattern matched N distinct symbols" notice. Distinctness is by param-free FQN, so a method's
    // OVERLOADS collapse to one target (a pattern naming a method is not ambiguous because it has two
    // signatures), while same-named methods on different types stay distinct (`FactPathFinder.BuildIndex`
    // vs `IndexCommands.BuildIndex` — the silent wrong-tree case). Contained lambdas of a matched
    // container are dropped exactly like BuildTree root selection: they render inline under the
    // container, so they are not an independent answer the user could be surprised by.
    public static IReadOnlyList<string> DistinctMatchTargets(IEnumerable<string> nodes, string pattern)
    {
        var matched = MatchNodes(nodes, pattern).ToHashSet(StringComparer.Ordinal);
        return DistinctTargetFqns(matched.Where(n => !IsContainedLambdaOfMatched(n, matched)));
    }

    // The distinct param-free FQNs of an already-resolved id set (tree derives disclosure from its BUILT
    // roots so it works on the cached-forest path too, where the graph is never loaded).
    public static IReadOnlyList<string> DistinctTargetFqns(IEnumerable<string> ids) =>
        ids.Select(ParamFreeFqn).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.Ordinal).ToList();
}
