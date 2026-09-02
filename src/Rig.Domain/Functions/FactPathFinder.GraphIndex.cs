using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Rig.Domain.Data;

namespace Rig.Domain.Functions;

public static partial class FactPathFinder
{
    // ── Derived-structure MEMO, keyed on GRAPH IDENTITY ────────────────────────────────────────────
    //
    // BuildIndex / BuildReverseMaps are pure functions of (graph, narrowDispatch[, mode]) — they read
    // ONLY the graph's own collections (CallEdges / Methods / ImplementsEdges / BaseEdges / MinedDispatch
    // / CutRules / ContextRules). FactGraphData is a positional record with init-only IReadOnlyList
    // members, and every transform in the pipeline (FactDelegateFieldJoin.Apply, ShapeGraph,
    // RewriteGenericFactories, GenericMonomorphizer.Materialize, MarkEventSubscriptionHandoffs,
    // FactHotspotReport's collapse) produces a NEW record via `with` rather than mutating in place —
    // so object identity is a sound cache key: a graph object's content never changes after construction,
    // and a changed graph is always a different object.
    //
    // Before this memo, BOTH structures were rebuilt from scratch inside EVERY traversal (a whole-graph
    // scan each: adjacency + sort + dispatch maps, then a receiver-blind/per-edge dispatch inversion). A
    // resident host serving several queries against one materialized generation paid that repeatedly.
    //
    // ConditionalWeakTable holds the graph WEAKLY and the memo entry lives exactly as long as the graph:
    // a retired generation's graph becomes collectable the moment the host drops it, taking its index and
    // reverse maps with it. A strong-referenced Dictionary<FactGraphData, …> would pin every generation's
    // multi-GB derived state forever — the reason this is NOT a plain static dictionary.
    private static readonly ConditionalWeakTable<FactGraphData, GraphDerivedMemo> DerivedMemo = new();

    // Diagnostics: how many full builds this GRAPH's memo has performed. Graph-scoped (not a global
    // counter) so a concurrently-running query over a different graph cannot perturb the reading — which
    // is what lets GraphIndexMemoTests assert "a generation builds each structure once, not once per
    // traversal" deterministically. Returns (0, 0) for a graph the memo has never seen.
    private static (long Indexes, long ReverseMaps) DerivedBuildCounts(FactGraphData graph) =>
        DerivedMemo.TryGetValue(graph, out var memo)
            ? (Interlocked.Read(ref memo.IndexBuilds), Interlocked.Read(ref memo.ReverseMapBuilds))
            : (0L, 0L);

    private sealed class GraphDerivedMemo
    {
        public long IndexBuilds;
        public long ReverseMapBuilds;

        // Keyed by narrowDispatch: the flag is READ during traversal (index.NarrowDispatch gates receiver
        // narrowing in Successors/DispatchTargets), so the two variants are NOT interchangeable and must
        // not share an entry. Lazy with ExecutionAndPublication: ConcurrentDictionary's value factory can
        // run concurrently, so the Lazy is what guarantees exactly one build and that no thread can ever
        // observe a half-built index (racing threads block on the Lazy and get the finished object).
        public readonly ConcurrentDictionary<bool, Lazy<GraphIndex>> Indexes = new();

        // Keyed by (narrowDispatch, mode): narrowDispatch selects per-edge-narrowed vs receiver-blind
        // ReverseDispatch, and mode drives CutsHandoff (which caller edges are kept at all).
        public readonly ConcurrentDictionary<(bool NarrowDispatch, TraversalMode Mode), Lazy<ReverseMaps>> ReverseMapsByKey = new();

        // ONE strict-descendant closure cache per GRAPH, shared by every index built from it. The closure
        // is a pure function of graph.BaseEdges (identical across every index from this graph), so this is
        // the graph-scoped generalisation of the old `descendantsFrom` hand-off between the outer index
        // and BuildReverseMaps' internal one — now every index over the graph shares it by construction.
        public readonly ConcurrentDictionary<string, HashSet<string>> DescendantsCache = new(StringComparer.Ordinal);
    }

    // The memoised entry points. Identical results to the Core builders below — this is a pure caching
    // layer; no traversal semantics change.
    private static GraphIndex BuildIndex(FactGraphData graph, bool narrowDispatch = true)
    {
        var memo = DerivedMemo.GetValue(graph, static _ => new GraphDerivedMemo());
        return memo
            .Indexes.GetOrAdd(
                narrowDispatch,
                static (nd, state) =>
                    new Lazy<GraphIndex>(
                        () =>
                        {
                            Interlocked.Increment(ref state.Memo.IndexBuilds);
                            return BuildIndexCore(state.Graph, nd, state.Memo.DescendantsCache);
                        },
                        LazyThreadSafetyMode.ExecutionAndPublication
                    ),
                (Graph: graph, Memo: memo)
            )
            .Value;
    }

    private static ReverseMaps BuildReverseMaps(FactGraphData graph, bool narrowDispatch = true, TraversalMode mode = TraversalMode.SyncCut)
    {
        var memo = DerivedMemo.GetValue(graph, static _ => new GraphDerivedMemo());
        return memo
            .ReverseMapsByKey.GetOrAdd(
                (narrowDispatch, mode),
                static (key, state) =>
                    new Lazy<ReverseMaps>(
                        () =>
                        {
                            Interlocked.Increment(ref state.Memo.ReverseMapBuilds);
                            return BuildReverseMapsCore(state.Graph, key.NarrowDispatch, key.Mode);
                        },
                        LazyThreadSafetyMode.ExecutionAndPublication
                    ),
                (Graph: graph, Memo: memo)
            )
            .Value;
    }

    private sealed class ReverseMaps
    {
        public Dictionary<string, List<string>> Callers = new(StringComparer.Ordinal);

        // The reverse-dispatch map. Its MEANING differs by mode:
        //
        //   * NARROWED (NarrowDispatch=true): concrete dispatch TARGET O -> the set of CALLER methods that
        //     dispatch to O, already RECEIVER-NARROWED per call edge. Built by INVERTING forward's own
        //     per-edge output: for each real call edge `caller -(R)-> B`, the targets `DispatchTargets(B, R)`
        //     are exactly the concrete methods that edge can reach, so `caller` reverse-reaches each via that
        //     one dispatch hop. This is the precise mirror of Successors' forward narrowing — the god-seam
        //     over-approximation (a hub's 3,000 unrelated callers riding the fan to every override) is gone
        //     because each caller is only ever attributed to the override ITS receiver resolves to. A
        //     `base.M()` (NonVirtual) edge contributes NO entry (it reaches only the base BODY via Callers,
        //     never a sibling override). Predecessors yields these caller methods DIRECTLY — no further
        //     receiver gate is needed (the narrowing already happened at build time).
        //
        //   * RECEIVER-BLIND (NarrowDispatch=false): the sound superset — concrete TARGET O -> the dispatch
        //     SOURCE (base/interface/hub) methods that resolve to it, the exact reverse of the receiver-blind
        //     forward DispatchTargets(node, null) over every node. Predecessors yields the full hub-fan
        //     (every caller of the hub rides up), matching the SQL/dispatch_edges oracle. Unchanged.
        //
        // SingleTarget records the DEGREE of the hop that produced the entry: true when the DispatchTargets
        // resolution it was inverted from had exactly one candidate (the same count the forward walk reports
        // as Fanout, which tags only `> 1` as fan-out). Predecessors uses it to admit deterministic hops
        // without the polymorphic ones (DispatchAdmission.SingleTarget). It is per ENTRY, not per target:
        // two callers of one hub can narrow to different degrees through their own receivers.
        public Dictionary<string, List<(string Source, bool SingleTarget)>> ReverseDispatch = new(StringComparer.Ordinal);

        public bool NarrowDispatch = true;
    }

    // The uncached build. The old `descendantsFrom` parameter is gone: the strict-descendant closure cache
    // is now GRAPH-scoped (GraphDerivedMemo.DescendantsCache), so the internal index below and the caller's
    // own index already share one instance by construction — each type's descendants are still computed
    // once per command, and now once per GRAPH rather than once per command.
    private static ReverseMaps BuildReverseMapsCore(FactGraphData graph, bool narrowDispatch, TraversalMode mode)
    {
        var rev = new ReverseMaps { NarrowDispatch = narrowDispatch };

        // The internal index used for dispatch resolution. Receiver narrowing in DispatchTargets is driven by
        // the receiverType ARGUMENT, not by index.NarrowDispatch, so a narrowDispatch:false index serves both
        // the per-edge narrowed inversion (true mode, passing the real receiver) and the per-node blind
        // inversion (false mode, passing null) — exactly mirroring the forward walk.
        var index = BuildIndex(graph, narrowDispatch: false);

        // Memoise DispatchTargets per (hub B, stripped receiver R) — the god-seam has ~49 distinct receivers
        // across ~3,000 call edges into the same hub, so this collapses ~3,000 resolutions to ~49. A distinct
        // sentinel keys the null/unstripped-receiver case (full CHA, DispatchTargets(B, null)).
        const string nullReceiverSentinel = "\0null";
        var dispatchMemo = new Dictionary<(string Hub, string ReceiverKey), List<(string Node, string Kind, string Basis)>>();

        List<(string Node, string Kind, string Basis)> DispatchTargetsMemo(string hub, string? receiver)
        {
            var stripped = string.IsNullOrEmpty(receiver) ? null : ReceiverToStrippedTypeId(receiver!);
            var key = (hub, stripped ?? nullReceiverSentinel);
            if (!dispatchMemo.TryGetValue(key, out var targets))
            {
                // Pass the ORIGINAL receiver string (not the stripped key) so DispatchTargets does its own
                // ResolveNarrowRoot exactly as the forward walk does; a null/unstripped receiver -> full CHA.
                dispatchMemo[key] = targets = DispatchTargets(method: hub, index: index, receiverType: stripped is null ? null : receiver);
            }

            return targets;
        }

        foreach (var edge in graph.CallEdges)
        {
            // A handoff edge is NOT a synchronous caller->callee link, so under SyncCut it must not make
            // the registrar a predecessor of the callback (else `callers` would claim the registrar reaches
            // the callback synchronously, and the callback wouldn't surface as a background origin via
            // `--roots`). Under AsyncExact the link is kept EXCEPT for delivery fan-out; AsyncInclude keeps
            // all. CutsHandoff centralizes the policy so this reverse walk and the forward Dispatch walk agree.
            if (CutsHandoff(mode, edge))
            {
                continue;
            }

            // Direct callers (every mode): the real caller of the callee BODY. Includes base.M() callers —
            // a base call IS a direct caller of the base body, so `callers(base)` still lists it.
            if (!rev.Callers.TryGetValue(edge.Callee, out var list))
            {
                rev.Callers[edge.Callee] = list = new List<string>();
            }

            list.Add(edge.Caller);

            if (!narrowDispatch)
            {
                continue; // false mode builds ReverseDispatch per-NODE below (receiver-blind hub-fan)
            }

            // NARROWED per-edge reverse dispatch (true mode): invert forward's own output. A base.M() edge
            // binds to exactly the base body and never a sibling override, so it contributes no dispatch fan
            // (it is already a direct caller above). For an ordinary edge `caller -(R)-> B`, every concrete
            // target O of DispatchTargets(B, R) is a method `caller` reverse-reaches via this one dispatch
            // hop — so `caller` is a (already receiver-narrowed) reverse-dispatch caller of O.
            if (edge.NonVirtual)
            {
                continue;
            }

            var targets = DispatchTargetsMemo(hub: edge.Callee, receiver: edge.ReceiverType);
            var singleTarget = targets.Count == 1;
            foreach (var target in targets)
            {
                if (!rev.ReverseDispatch.TryGetValue(target.Node, out var sources))
                {
                    rev.ReverseDispatch[target.Node] = sources = new List<(string, bool)>();
                }

                sources.Add((edge.Caller, singleTarget));
            }
        }

        if (!narrowDispatch)
        {
            // RECEIVER-BLIND superset: ReverseDispatch = the forward CHA dispatch edges inverted, per node.
            // O -> [hub methods that resolve to O]. Predecessors yields the full hub-fan (every caller of the
            // hub rides up). The SQL/dispatch_edges oracle equivalence is by construction. Unchanged behaviour.
            foreach (var node in index.Nodes)
            {
                var targets = DispatchTargets(method: node, index: index, receiverType: null);
                var singleTarget = targets.Count == 1;
                foreach (var target in targets)
                {
                    if (!rev.ReverseDispatch.TryGetValue(target.Node, out var sources))
                    {
                        rev.ReverseDispatch[target.Node] = sources = new List<(string, bool)>();
                    }

                    sources.Add((node, singleTarget));
                }
            }
        }

        return rev;
    }

    // `dispatch` gates the reverse-dispatch arm. None yields ONLY real callers — the reverse of Successors'
    // direct-call arm. SingleTarget adds the hops whose narrowed candidate set had exactly one target: the
    // reverse of the forward walk's rule that a degree-1 dispatch is deterministic and only `Fanout > 1` is
    // disclosed as fan-out (ReachesWithFanoutCore). All (the default) admits every hop. The narrower modes
    // exist because a consumer may need to know whether a reach survives WITHOUT polymorphic devirtualization:
    // rig discloses dispatch fan-out everywhere else (reaches' "NOT a real call" bucket, tree's edge markers),
    // and a consumer that cannot separate the two silently launders an over-approximation into a fact.
    private static IEnumerable<(string Pred, bool ViaReverseDispatch)> Predecessors(
        string current,
        GraphIndex index,
        ReverseMaps rev,
        DispatchAdmission dispatch = DispatchAdmission.All
    )
    {
        // Cut symmetry: a cut node yields NO successors forward (Successors `yield break`s on it), so it
        // can never be the runtime caller/dispatcher of `current` — it must not surface as a predecessor
        // in reverse either. Dropping it here is exactly the reverse of the forward leaf-stop, so
        // `callers` cuts the reflection/service-locator seams at the same boundary `reaches`/`tree` do
        // (e.g. a ProvideService<T> seam stops the reverse BFS instead of fanning to all its callers).
        var cutting = index.ApplyTraversalCuts;

        if (rev.Callers.TryGetValue(current, out var direct))
        {
            foreach (var c in direct)
            {
                if (!cutting || !index.IsTraversalCut(c))
                {
                    yield return (c, false);
                }
            }
        }

        if (dispatch == DispatchAdmission.None)
        {
            yield break;
        }

        // Reverse dispatch.
        //   * NARROWED mode: `sources` are the CALLER methods that dispatch to `current`, already
        //     receiver-narrowed at build time (BuildReverseMaps inverted forward's per-edge DispatchTargets).
        //     Yield them directly — no per-method receiver gate is needed; the wrong-receiver callers were
        //     never added. The base.M() exclusion falls out for free (NonVirtual edges contributed nothing).
        //   * RECEIVER-BLIND mode: `sources` are the hub/base/interface methods that resolve to `current` —
        //     the full hub-fan rides up, as before.
        // Under SingleTarget only the entries whose producing hop had one candidate are yielded.
        if (rev.ReverseDispatch.TryGetValue(current, out var sources))
        {
            foreach (var (source, singleTarget) in sources)
            {
                if (dispatch == DispatchAdmission.SingleTarget && !singleTarget)
                {
                    continue;
                }

                if (!cutting || !index.IsTraversalCut(source))
                {
                    yield return (source, true);
                }
            }
        }
    }

    // INTERNAL, not private, only so the nested TraversalSession can take one in its ctor (a private type
    // cannot be a parameter of an accessible member). Still unreachable outside Rig.Domain, so a query-side
    // caller like ImpactEngine holds a TraversalSession and cannot construct or poke at the index itself.
    internal sealed class GraphIndex
    {
        public GraphIndex(ConcurrentDictionary<string, HashSet<string>> descendantsCache) => DescendantsCache = descendantsCache;

        public Dictionary<string, List<CallEdge>> Adjacency = new(StringComparer.Ordinal);

        // Methods keyed by the GENERIC-STRIPPED containing type (Foo`2 / Foo{A,B} -> Foo), so
        // dispatch lookups land regardless of whether the base/impl/interface type DocID is the
        // open-generic or an instantiated form. Generic base classes (EditPaneBase`2, the EditPane
        // hierarchy, ...) otherwise break dispatch: the base EDGE stores Foo{A,B} while the METHODS
        // are declared on Foo`2, so an exact-DocID MethodsByType lookup misses them.
        public Dictionary<string, List<MethodRef>> MethodsByStrippedType = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> ImplsByInterface = new(StringComparer.Ordinal);

        // Implementers indexed by interface SIMPLE NAME, but ONLY for edges whose interface failed to
        // resolve to a real type (error-type "!:Name" DocID — pervasive under net48 partial binding
        // when a project doesn't resolve a referenced assembly). Lets impl-dispatch still fire when
        // the CALL's interface resolved (T:Ns.IFoo.M) but the IMPLEMENTER's edge didn't (!:IFoo) —
        // the failure mode that silently kills dispatch and under-reports downstream effects.
        public Dictionary<string, List<string>> ImplsByErrorInterfaceName = new(StringComparer.Ordinal);

        // EXACT Roslyn-mined dispatch edges (dispatch_facts), source member -> [(target, kind)] with
        // kind "override"|"impl". When a method has any of these, they are AUTHORITATIVE for its
        // member-level dispatch (Basis="roslyn") and the name/arity CHA scan is skipped; the CHA scan
        // remains only as a flagged "heuristic" fallback for members with no mined edge, plus the
        // always-on error-type (`!:`) simple-name recovery (Roslyn never bound those, so no mined
        // edge can exist). Empty when the graph carries no mined facts (old store / synthetic test
        // graph) — then everything falls back to CHA exactly as before, marked heuristic.
        public Dictionary<string, List<(string Target, string Kind)>> MinedDispatchBySource = new(StringComparer.Ordinal);

        // Generic-stripped base-edge lookup (stripped base id -> subtype ids), for base-virtual/
        // abstract -> override dispatch. Stripped so a call on the open-generic base reaches overrides
        // on subtypes that store the instantiated base edge (see TypeClosure). Empty when no base edges.
        public ILookup<string, string> StrippedBaseEdges = Enumerable.Empty<string>().ToLookup(x => x, StringComparer.Ordinal);

        // Memoised strict-descendant closure per (stripped) base type, so transitive override dispatch
        // doesn't re-BFS the hierarchy on every visit during the main traversal. Concurrent so ONE index
        // can be shared across threads (ReachesFromEachSeed's parallel per-seed reach): the cache is pure
        // idempotent memoization — a racing double-compute yields the same set, harmless. Supplied by
        // BuildIndexCore from the GRAPH-scoped memo, so every index over one graph shares a single cache;
        // readonly because the index is now itself memoised and shared — nothing may swap it out.
        public readonly ConcurrentDictionary<string, HashSet<string>> DescendantsCache;

        // Whether a method has ANY dispatch fan at all, resolved receiver-BLIND. Narrowing only ever FILTERS
        // the receiver-blind candidate set (NarrowByReceiver / ByContextFamily / ByTypeArguments all return a
        // subset, and the candidate collection itself never consults the receiver), so a method with no blind
        // target can have no target under any receiver either — one memoized `false` then answers every
        // context for that method.
        //
        // This is what keeps the context-aware expansion memo cheap. Resolving a node's fan is NOT free for a
        // non-dispatching method — it parses the DocID and scans every descendant of the declaring type — and
        // the context key has to be computed for elided/skipped visits too, which previously did no dispatch
        // work at all. Without this gate that cost landed on every repeat visit in the forest (+18% on
        // BuildTree); with it, a non-dispatching method pays one blind resolution for the whole traversal.
        // Concurrent + idempotent for the same reason DescendantsCache is: one index is shared across
        // ReachesFromEachSeed's parallel per-seed walks, and a racing double-compute yields the same answer.
        public readonly ConcurrentDictionary<string, bool> DispatchCapableCache = new(StringComparer.Ordinal);
        public HashSet<string> Nodes = new(StringComparer.Ordinal);

        // Monomorphized instantiation nodes (`{base}~mono⟨…⟩`) grouped by the BASE method they instantiate.
        // Empty on every graph the monomorphizer did not materialize. Read by the reverse seeding in
        // ReachedByAny / ReachedByLabelledSeeds; see SeedsFor for why a base seed needs them.
        public Dictionary<string, List<string>> InstantiationsByBase = new(StringComparer.Ordinal);

        // ADMITTED EXTERNAL LEAVES (MethodRef.IsExternal — external-node admission): library/BCL call
        // targets that are graph nodes but have no indexed body. They are LEAVES by construction (nothing
        // inside the external DLL was indexed, so they own no adjacency) and DispatchTargets refuses them
        // as a dispatch source, so an admitted external interface/base declaration can never CHA-fan to
        // first-party implementations — dispatch through an external declaration is deliberately out of
        // scope for that change. Empty on every pre-existing graph (nothing sets IsExternal), so the
        // traversal is byte-identical without admission.
        public HashSet<string> ExternalLeaves = new(StringComparer.Ordinal);

        // When true (the default for the in-memory traversal), virtual/base/interface dispatch is
        // NARROWED to the call edge's static receiver type (CallEdge.ReceiverType). When false, full
        // CHA — every same-named override/impl — is used (the sound superset, for AllDispatchEdges /
        // dispatch_edges and the SQL-equivalence oracle path).
        public bool NarrowDispatch = true;

        // Traversal-cut rules (Task B): when ApplyTraversalCuts is true, a node matching any of
        // these rules is a traversal leaf — its successors are NOT yielded by Successors. Only
        // enabled for BuildTree / ReachesWithFanout (tree/reaches/path); never for dead-code or
        // callers traversals (which must see the full graph).
        public bool ApplyTraversalCuts;
        public IReadOnlyList<FactTraversalCutRule>? TraversalCutRules;

        public bool IsTraversalCut(string symbolId)
        {
            if (TraversalCutRules is null)
            {
                return false;
            }

            foreach (var rule in TraversalCutRules)
            {
                if (rule.IsMatch(symbolId))
                {
                    return true;
                }
            }

            return false;
        }

        // Context-bound interface dispatch (state-family narrowing). ContextInterfacePatterns holds the
        // configured interface substrings (e.g. "IWorkflowState"); StateFamilyByController maps a context
        // type (a "controller", normalised "T:"+stripped) to the concrete impl types bound to it via the
        // BindingBase{C} base edge (e.g. all InvoiceDebtChase state types). Empty unless a context-dispatch
        // rule is supplied. Used in Successors (carry the controller across the interface call) and
        // DispatchTargets (narrow the impl fan-out to the controller's family).
        public IReadOnlyList<string> ContextInterfacePatterns = [];
        public readonly Dictionary<string, HashSet<string>> StateFamilyByController = new(StringComparer.Ordinal);

        public bool IsContextInterface(string typeId)
        {
            foreach (var pattern in ContextInterfacePatterns)
            {
                if (typeId.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // The uncached build. Reached only through the memoising BuildIndex above.
    private static GraphIndex BuildIndexCore(
        FactGraphData graph,
        bool narrowDispatch,
        ConcurrentDictionary<string, HashSet<string>> descendantsCache
    )
    {
        var index = new GraphIndex(descendantsCache) { NarrowDispatch = narrowDispatch };
        // Pre-size the two collections that fill to the full graph size, so they don't resize/rehash ~log2(N)
        // times from empty as edges/nodes are added (each resize reallocates the backing arrays — pure churn).
        // Capacities are safe upper bounds (distinct callers <= edges; distinct nodes <= methods + edge
        // endpoints). (This build used to run on EVERY traversal; BuildIndex now memoises it per graph.)
        index.Adjacency.EnsureCapacity(graph.CallEdges.Count);
        index.Nodes.EnsureCapacity(graph.Methods.Count + graph.CallEdges.Count);
        foreach (var edge in graph.CallEdges)
        {
            if (!index.Adjacency.TryGetValue(edge.Caller, out var list))
            {
                index.Adjacency[edge.Caller] = list = new List<CallEdge>();
            }

            list.Add(edge);
            AddNode(index, edge.Caller);
            AddNode(index, edge.Callee);
        }

        // Sort each adjacency list ONCE here — total order: call-site line (primary, preserves source
        // order for distinct-line children), then callee SymbolId (first tie-break, ordinal), then edge
        // Kind (second tie-break), then ReceiverType (final tie-break) — so Successors iterates it
        // directly instead of re-running OrderBy().ThenBy() on every node expansion. The four-key total
        // order is store-independent: same-line edges that share even the callee id are distinguished by
        // Kind/ReceiverType, so a re-index (which reshuffles SQLite rowids) or a parallel-load (which
        // does not preserve insertion order) cannot change child ordering. Line stays primary, so
        // distinct-line children are unaffected. Adjacency is immutable after this build, and the build
        // completes (under the memo's Lazy, so exactly once and fully published) before any (possibly
        // parallel, e.g. ReachesFromEachSeed) traversal reads the shared index — the in-place sort is
        // race-free, and no thread can observe a partially-sorted adjacency list.
        foreach (var list in index.Adjacency.Values)
        {
            list.Sort(
                static (a, b) =>
                {
                    var byLine = a.Line.CompareTo(b.Line);
                    if (byLine != 0)
                    {
                        return byLine;
                    }

                    var byCallee = string.CompareOrdinal(a.Callee, b.Callee);
                    if (byCallee != 0)
                    {
                        return byCallee;
                    }

                    var byKind = string.CompareOrdinal(a.Kind, b.Kind);
                    if (byKind != 0)
                    {
                        return byKind;
                    }

                    return string.CompareOrdinal(a.ReceiverType, b.ReceiverType);
                }
            );
        }

        // Admitted external leaves are EXCLUDED from the dispatch index: they may be neither a CHA fan-out
        // ROOT (gated in DispatchTargets) nor a fan-out TARGET (this filter). A first-party call that CHA-
        // scans a type's methods must never land on a library member with no body.
        index.MethodsByStrippedType = graph
            .Methods.Where(m => m.ContainingTypeId is not null && !m.IsExternal)
            .GroupBy(m => TypeClosure.StripGeneric(m.ContainingTypeId!), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        index.ImplsByInterface = graph
            .ImplementsEdges.GroupBy(e => e.InterfaceType, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ImplType).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        index.ImplsByErrorInterfaceName = graph
            .ImplementsEdges.Where(e => e.InterfaceType.StartsWith("!:", StringComparison.Ordinal))
            .GroupBy(e => DispatchRelationKeys.SimpleTypeName(e.InterfaceType), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ImplType).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        index.StrippedBaseEdges = TypeClosure.BuildBaseEdgeLookup(
            (graph.BaseEdges ?? new List<BaseEdge>()).Select(e => (e.SubType, e.BaseType))
        );
        BuildContextFamilies(index, graph, graph.ContextRules);
        // Shaping carried on the graph (set once by ShapeGraph at load) drives the cut uniformly for
        // every traversal that builds an index — forward Successors AND reverse Predecessors — so no
        // command can accidentally walk an unshaped graph. Null/empty => no cut (the --raw / dead path).
        if (graph.CutRules is { Count: > 0 })
        {
            index.TraversalCutRules = graph.CutRules;
            index.ApplyTraversalCuts = true;
        }
        foreach (var fact in graph.MinedDispatch ?? new List<DispatchFact>())
        {
            if (!index.MinedDispatchBySource.TryGetValue(fact.SourceMember, out var list))
            {
                index.MinedDispatchBySource[fact.SourceMember] = list = new List<(string, string)>();
            }

            if (!list.Contains((fact.TargetMember, fact.Kind)))
            {
                list.Add((fact.TargetMember, fact.Kind));
            }
        }
        foreach (var method in graph.Methods)
        {
            AddNode(index, method.SymbolId);
            if (method.IsExternal)
            {
                index.ExternalLeaves.Add(method.SymbolId);
            }
        }

        return index;
    }

    // Registers a traversal node, indexing a monomorphized id under its base method on the way in. Gated on
    // the HashSet insert, so the marker test runs once per DISTINCT node rather than once per edge endpoint.
    private static void AddNode(GraphIndex index, string id)
    {
        if (!index.Nodes.Add(id) || !MonomorphizedNodeId.IsMonomorphized(id))
        {
            return;
        }

        var baseId = MonomorphizedNodeId.BaseOf(id);
        if (!index.InstantiationsByBase.TryGetValue(baseId, out var instantiations))
        {
            index.InstantiationsByBase[baseId] = instantiations = new List<string>();
        }

        instantiations.Add(id);
    }

    // Builds the context-bound dispatch maps from the configured rules: for each base edge of the form
    // `S --base--> BindingBase{C}` (the binding base matched by substring), bind the impl S — and every
    // transitive subtype of S — to the context type C. So a dispatch of a context-interface member carried
    // with controller C narrows to exactly the family { S, descendants(S) } per C. No rules => no-op.
    private static void BuildContextFamilies(GraphIndex index, FactGraphData graph, IReadOnlyList<FactContextDispatchRule>? rules)
    {
        if (rules is not { Count: > 0 })
        {
            return;
        }

        index.ContextInterfacePatterns = rules.Select(r => r.Interface).Distinct(StringComparer.Ordinal).ToArray();

        void Bind(string controllerKey, string stateTypeId)
        {
            if (!index.StateFamilyByController.TryGetValue(controllerKey, out var family))
            {
                index.StateFamilyByController[controllerKey] = family = new HashSet<string>(StringComparer.Ordinal);
            }

            family.Add(stateTypeId);
        }

        foreach (var edge in graph.BaseEdges ?? new List<BaseEdge>())
        foreach (var rule in rules)
        {
            if (edge.BaseType.IndexOf(rule.BindingBase, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var contextArg = ExtractGenericArg(edge.BaseType);
            if (contextArg is null)
            {
                continue;
            }

            var controllerKey = NormType(contextArg);
            Bind(controllerKey: controllerKey, stateTypeId: NormType(edge.SubType));
            foreach (var descendant in Descendants(edge.SubType, index))
            {
                Bind(controllerKey: controllerKey, stateTypeId: NormType(descendant));
            }
        }
    }

    // Normalised type key: a leading "T:" plus the generic-stripped name, matching the form ParseMethod
    // type ids and Descendants results use, so context-family membership compares apples to apples.
    private static string NormType(string typeId)
    {
        var body = typeId.StartsWith("T:", StringComparison.Ordinal) ? typeId.Substring(2) : typeId;
        return "T:" + TypeClosure.StripGeneric(body);
    }

    // The first top-level generic argument of a DocID type, i.e. the X in "Ns.Base{X}" (DocID renders
    // closed generics with braces). Null when there is no brace group. Honours nesting so "Base{A{B}}"
    // returns "A{B}".
    private static string? ExtractGenericArg(string typeId)
    {
        var open = typeId.IndexOf('{');
        if (open < 0)
        {
            return null;
        }

        var depth = 0;
        for (var i = open; i < typeId.Length; i++)
        {
            if (typeId[i] == '{')
            {
                depth++;
            }
            else if (typeId[i] == '}' && --depth == 0)
            {
                return typeId.Substring(startIndex: open + 1, length: i - open - 1).Trim();
            }
        }
        return null;
    }
}
