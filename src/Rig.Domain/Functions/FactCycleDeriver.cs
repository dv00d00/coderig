using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// Stage-2 GRAPH HAZARD derivation: the FIRST hazard that is NOT an effect-attached observation. Every
// existing hazard (race_window / lazy_init_race / dual_write / n_plus_1 / unserializable_payload) is a
// PATTERN over the flat effect list — a co-occurrence or ordering of point-fact effects inside one method.
// `event_cycle` is different in KIND: it is a property of the CALL GRAPH topology, not of any effect, so it
// is derived here over the graph rather than over effects (DeriveCommand wires it as an ADDITIVE second
// hazard source, never shoehorned into the over-effects HazardFindings pass).
//
// What it detects: a FEEDBACK CYCLE that closes through ≥1 publish→consumer DELIVERY edge. The graph now
// carries delivery edges as `Kind="handoff"` `CallEdge`s tagged with a `HandoffDispatcher` — `"event_raise"`
// (a C# event raise resolved to its subscribers) or `"actor_tell"` (an Echo `Process.tell` resolved to the
// handlers spawned under that process name), both added by the single FactPathFinder.AddDeliveryEdges join. A
// cycle that traverses such an edge is the dangerous shape: method A raises an event → a handler runs →
// (synchronously, or via further raises) eventually a raise is delivered back to A, an unbounded re-entrancy
// / event-storm / stack-blowing loop that no syntactic call records (the delivery hop is the invisible edge).
//
// Why SCC, not "find a path back": a strongly-connected component is EXACTLY the set of nodes that can all
// reach each other — i.e. every node in an SCC of size>1 lies on a cycle, and a self-edge makes a size-1 SCC
// a cycle too. So "is there a feedback cycle?" reduces to "is there an SCC that contains a delivery edge whose
// both endpoints are inside that same SCC?". One Tarjan pass over the whole call graph yields every SCC; we
// then keep only the ones a delivery edge closes. This is sound for the question asked (it never invents a
// cycle) and avoids the exponential blow-up of enumerating individual cycles.
//
// Pure, no I/O, input not mutated. The caller (DeriveCommand / GraphMaterializer-mirrored wiring) supplies a
// graph that ALREADY has the delivery edges baked in (FactPathFinder.AddDeliveryEdges); this
// deriver only reads `graph.CallEdges`.
public static class FactCycleDeriver
{
    // The hazard finding TYPE this deriver emits (re-stated in HazardKinds, the closed catalog).
    public const string EventCycleType = "event_cycle";

    private const string HandoffKind = "handoff";
    private const string ConfidenceHigh = "high";
    private const string ConfidenceLow = "low";

    // The delivery-edge dispatchers this deriver hunts through, taken from RULE DATA (core-purity F6): every
    // `deliveryRules` entry marked `cycleDelivery: true`, mapped to its `joinConfidence`. Core names NO tag —
    // the tags (`event_raise`, `actor_tell`, …) are ASSIGNED by the ruleset that declares the mechanism, so a
    // constant here would hardcode knowledge of one ruleset's vocabulary AND of how exact its join is. A
    // ruleset that marks no rule cycleDelivery therefore yields no event_cycle findings, which is honest: rig
    // does not know what a delivery hop is in that codebase.
    //
    // joinConfidence semantics: "low" = the producer→handler join is heuristic/over-approximate (e.g. resolved
    // by a shared name), so a cycle traversing such an edge is a CANDIDATE — any low edge caps the cycle at
    // low. Anything else (including an omitted value) is an exact join.
    public static IReadOnlyDictionary<string, string> CycleDeliveryDispatchers(IReadOnlyList<DeliveryRule> deliveryRules)
    {
        var dispatchers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rule in deliveryRules)
        {
            if (!rule.CycleDelivery || string.IsNullOrEmpty(rule.Tag))
            {
                continue;
            }

            // Two rules sharing a tag: the more doubtful join wins, so disclosure never gets upgraded away.
            var confidence = string.Equals(rule.JoinConfidence, ConfidenceLow, StringComparison.OrdinalIgnoreCase) ? ConfidenceLow : ConfidenceHigh;
            if (!dispatchers.TryGetValue(rule.Tag, out var existing) || string.Equals(existing, ConfidenceHigh, StringComparison.Ordinal))
            {
                dispatchers[rule.Tag] = confidence;
            }
        }

        return dispatchers;
    }

    // Returns every feedback cycle in `graph` that closes through at least one delivery edge. A cycle is one
    // strongly-connected component (SCC) of the Caller→Callee call graph that CONTAINS a delivery edge — a
    // `Kind="handoff"` `CallEdge` whose HandoffDispatcher is in `deliveryDispatchers` and whose BOTH endpoints
    // are members of that same SCC (so the delivery hop is genuinely part of the cycle, not merely incident to
    // it). A size-1 SCC qualifies on a SELF delivery edge (a method raising an event it itself handles).
    //
    // `deliveryDispatchers` maps each qualifying delivery TAG to its join confidence (see
    // CycleDeliveryDispatchers, which builds it from the `deliveryRules` marked cycleDelivery). Confidence is
    // "low" when ANY qualifying delivery edge in the SCC came from a "low"-join mechanism (a heuristic join,
    // over-approximate on a shared name); "high" when they are all exact joins. An empty/null map means no
    // mechanism was declared as cycle-carrying, so there are NO findings — core knows no tag of its own.
    //
    // Determinism (for stable tests + a future cache): SCC members sorted Ordinal; DeliveryEdges sorted by
    // (Caller, Callee, Line); the returned cycles ordered by their first member Ordinal.
    public static IReadOnlyList<EventCycle> DeriveEventCycles(
        FactGraphData graph,
        IReadOnlyDictionary<string, string>? deliveryDispatchers
    )
    {
        var dispatchers = deliveryDispatchers;
        if (dispatchers is null || dispatchers.Count == 0)
        {
            return [];
        }

        // The full edge topology for the SCC is ALL call edges — the delivery handoff edges are PART of the
        // graph here (we are hunting cycles that traverse them), unlike the sync-cut reachability traversal
        // that prunes them. Build a Caller→Callee adjacency once; node order is first-seen for determinism of
        // the index assignment (the final ordering re-sorts by member id, so this order is only an internal seed).
        var nodeIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodeIds = new List<string>();
        var adjacency = new List<List<int>>();

        int Intern(string id)
        {
            if (nodeIndex.TryGetValue(id, out var existing))
            {
                return existing;
            }

            var index = nodeIds.Count;
            nodeIndex[id] = index;
            nodeIds.Add(id);
            adjacency.Add([]);
            return index;
        }

        foreach (var edge in graph.CallEdges)
        {
            var from = Intern(edge.Caller);
            var to = Intern(edge.Callee);
            adjacency[from].Add(to);
        }

        var sccOf = TarjanScc(adjacency);

        // Group nodes by their SCC id, then keep only the components a delivery edge closes. We collect the
        // qualifying delivery edges per SCC (both endpoints in the same component) in one pass over the edges.
        var deliveryEdgesByScc = new Dictionary<int, List<CallEdge>>();
        foreach (var edge in graph.CallEdges)
        {
            if (
                !string.Equals(edge.Kind, HandoffKind, StringComparison.Ordinal)
                || edge.HandoffDispatcher is null
                || !dispatchers.ContainsKey(edge.HandoffDispatcher)
            )
            {
                continue;
            }

            // Both endpoints are guaranteed interned (every edge endpoint was Intern'd above). A delivery edge
            // closes a cycle only when BOTH endpoints land in the SAME SCC — a self-edge (from==to) is the
            // size-1 case and trivially satisfies this.
            var from = nodeIndex[edge.Caller];
            var to = nodeIndex[edge.Callee];
            if (sccOf[from] != sccOf[to])
            {
                continue;
            }

            var scc = sccOf[from];
            if (!deliveryEdgesByScc.TryGetValue(scc, out var list))
            {
                list = [];
                deliveryEdgesByScc[scc] = list;
            }
            list.Add(edge);
        }

        if (deliveryEdgesByScc.Count == 0)
        {
            return [];
        }

        // Members of each qualifying SCC (every node whose component is a qualifying one).
        var membersByScc = new Dictionary<int, List<string>>();
        for (var node = 0; node < nodeIds.Count; node++)
        {
            var scc = sccOf[node];
            if (!deliveryEdgesByScc.ContainsKey(scc))
            {
                continue;
            }

            if (!membersByScc.TryGetValue(scc, out var list))
            {
                list = [];
                membersByScc[scc] = list;
            }
            list.Add(nodeIds[node]);
        }

        var cycles = new List<EventCycle>(deliveryEdgesByScc.Count);
        foreach (var (scc, deliveryEdges) in deliveryEdgesByScc)
        {
            var members = membersByScc[scc];
            members.Sort(StringComparer.Ordinal);

            deliveryEdges.Sort(
                (a, b) =>
                {
                    var byCaller = string.CompareOrdinal(a.Caller, b.Caller);
                    if (byCaller != 0)
                    {
                        return byCaller;
                    }

                    var byCallee = string.CompareOrdinal(a.Callee, b.Callee);
                    return byCallee != 0 ? byCallee : a.Line.CompareTo(b.Line);
                }
            );

            // Confidence: low if ANY qualifying delivery edge came from a mechanism the RULES declare as a
            // heuristic join (joinConfidence "low"); high when every one of them is an exact join. The
            // exactness is a property of the declared mechanism, not of a tag core recognizes.
            var anyHeuristicJoin = deliveryEdges.Any(e =>
                e.HandoffDispatcher is { } tag
                && dispatchers.TryGetValue(tag, out var join)
                && string.Equals(join, ConfidenceLow, StringComparison.Ordinal)
            );
            cycles.Add(
                new EventCycle(Members: members, DeliveryEdges: deliveryEdges, Confidence: anyHeuristicJoin ? ConfidenceLow : ConfidenceHigh)
            );
        }

        // Order the cycles deterministically by their first (Ordinal-sorted) member.
        cycles.Sort((a, b) => string.CompareOrdinal(a.Members[0], b.Members[0]));
        return cycles;
    }

    // ITERATIVE (explicit-stack) Tarjan strongly-connected-components over the integer adjacency list. Returns
    // an array mapping each node index to its SCC id. RECURSION IS NOT AN OPTION here — the real MedDBase graph
    // is ~100k nodes / ~500k edges and a recursive DFS blows the stack — so the classic recursive Tarjan is
    // unrolled onto an explicit work stack. Each frame is (node, the index of the NEXT successor to visit); a
    // successor not yet indexed is pushed (the "descend" arm), and on the way back up we relax the parent's
    // lowlink by the child's (the "return" arm). A node whose lowlink equals its own index is an SCC root: pop
    // the component stack down to it. SCC ids are assigned in pop order — opaque (only used to group), so the
    // numeric value carries no meaning; the public ordering re-sorts by member id.
    private static int[] TarjanScc(List<List<int>> adjacency)
    {
        var n = adjacency.Count;
        var index = new int[n];
        var lowlink = new int[n];
        var onStack = new bool[n];
        var visited = new bool[n];
        for (var i = 0; i < n; i++)
        {
            index[i] = -1;
        }

        var sccOf = new int[n];
        var nextIndex = 0;
        var sccCount = 0;

        var componentStack = new Stack<int>();
        // The DFS work stack: each frame is the node plus a cursor into its successor list (how far we've
        // descended). A separate "iterator position" array keeps the cursor across push/pop of inner frames.
        var workStack = new Stack<int>();
        var successorCursor = new int[n];

        for (var start = 0; start < n; start++)
        {
            if (visited[start])
            {
                continue;
            }

            workStack.Push(start);
            while (workStack.Count > 0)
            {
                var v = workStack.Peek();

                if (!visited[v])
                {
                    // First time we touch v: assign its index/lowlink and put it on the component stack.
                    visited[v] = true;
                    index[v] = nextIndex;
                    lowlink[v] = nextIndex;
                    nextIndex++;
                    componentStack.Push(v);
                    onStack[v] = true;
                    successorCursor[v] = 0;
                }

                var successors = adjacency[v];
                // Advance through v's successors. A not-yet-visited successor is DESCENDED into (push + break
                // so the outer loop processes it first); an already-on-stack successor relaxes v's lowlink.
                var descended = false;
                while (successorCursor[v] < successors.Count)
                {
                    var w = successors[successorCursor[v]];
                    successorCursor[v]++;
                    if (!visited[w])
                    {
                        workStack.Push(w);
                        descended = true;
                        break;
                    }
                    else if (onStack[w])
                    {
                        if (index[w] < lowlink[v])
                        {
                            lowlink[v] = index[w];
                        }
                    }
                }

                if (descended)
                {
                    continue;
                }

                // All of v's successors are processed: v is done. If v is an SCC root, pop its component.
                if (lowlink[v] == index[v])
                {
                    int popped;
                    do
                    {
                        popped = componentStack.Pop();
                        onStack[popped] = false;
                        sccOf[popped] = sccCount;
                    } while (popped != v);
                    sccCount++;
                }

                workStack.Pop();
                // Returning to v's parent (the new top, if any): relax the parent's lowlink by v's.
                if (workStack.Count > 0)
                {
                    var parent = workStack.Peek();
                    if (lowlink[v] < lowlink[parent])
                    {
                        lowlink[parent] = lowlink[v];
                    }
                }
            }
        }

        return sccOf;
    }
}

// A feedback cycle that closes through ≥1 publish→consumer DELIVERY edge — the `event_cycle` graph hazard.
// Members are the SCC node ids participating (sorted Ordinal). DeliveryEdges are the delivery edges fully
// inside the SCC (sorted by Caller, Callee, Line). Confidence is "high" when all delivery edges are
// event_raise (exact symbol joins) and "low" when any is actor_tell (a heuristic process-name join).
public sealed record EventCycle(IReadOnlyList<string> Members, IReadOnlyList<CallEdge> DeliveryEdges, string Confidence);
