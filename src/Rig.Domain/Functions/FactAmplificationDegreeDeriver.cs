using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// Estate-wide NON-LINEAR (super-linear) effect discovery: classify an effect not by "is it looped" (tier 2,
// FactObservationDeriver) nor by "is a looped CALL above it" (tier 3, the k=1 cross-method correlation), but by
// the AMPLIFICATION DEGREE — how many INDEPENDENT iteration contexts are stacked along a path from a caller
// down to the effect site.
//
//   degree 1 = linear   (N round trips; already covered by the shipped n_plus_1 / cross_method tiers)
//   degree 2 = QUADRATIC candidate, degree 3 = cubic, …
//   recursion = the chain enters a call CYCLE, so the degree is unbounded and no finite number is honest.
//
// This is the COMPOSITION of tier 3. That tier correlates ONE loop anchor with a downstream effect and stops;
// here the anchors are CHAINED — anchor a's callee reaches anchor b's enclosing method, so b's per-element call
// is itself issued once per element of a. The anchors themselves are NOT re-derived: they come verbatim from
// FactIterationFanoutDeriver, which is already FP-calibrated (monadic-comprehension gate + expression-tree
// exclusion; 93% precision on the MedDBase hand audit). Everything new here is the composition operator.
//
// Pure, no I/O, inputs not mutated. Everything reads the EXISTING store — no schema, no new fact column.
public static class FactAmplificationDegreeDeriver
{
    // The degree of an anchor whose chain enters a call cycle. Not a magnitude: a cycle multiplies by a
    // runtime-only bound, so any finite number would be a fabrication. Rendered as its own section.
    public const int Unbounded = -1;

    // Default anchor->anchor reach horizon. Six hops is deliberately SHORT: this is the distance between two
    // LOOP call sites, not between an entry point and a leaf, and every extra hop widens the path-insensitive
    // over-approximation that the whole tier already discloses.
    public const int DefaultMaxDepth = 6;

    // Default per-seed node budget for the anchor reach pass. Matches FactPathFinder's own default; combined
    // with the short depth horizon it keeps each transient reach set small.
    public const int DefaultMaxNodes = 20000;

    // How many anchor seeds are handed to one ReachesInfoFromEachSeed call. The pass extracts two SMALL things
    // per seed (which other anchors it reaches, which in-scope effect it reaches) and then DISCARDS the reach
    // set, so peak memory is (batch x reach-set size), not (anchors x reach-set size). On the real store there
    // are ~2.5k anchors; retaining all their reach sets at once is the shape that does not fit.
    public const int DefaultBatchSize = 64;

    // ONE looped call site — the composition unit. Identity is the CALL SITE (Caller, FilePath, Line), never
    // the method: rig's rollups dedupe per method, and doing that here would collapse two distinct looped call
    // sites in one method into a single node and silently lose one chain.
    public sealed record Anchor(
        string Caller,
        string FilePath,
        int Line,
        string IterationKind,
        string IterationDetail,
        // Every distinct callee at this call site. Usually one; a fluent chain on one line, or a CHA fan
        // already resolved in the facts, can give several. All are seeded and their reach UNIONED, because
        // dropping one would drop whatever only it reaches.
        IReadOnlyList<string> Callees,
        // Loops in the enclosing method that lexically CONTAIN this call site's own loop, +1 for that loop
        // itself. >1 means this single anchor already carries intra-method nesting — see IntraDepths.
        int IntraDepth
    );

    // One hop of a reported chain: where the loop is, what it iterates, and where to look.
    public sealed record ChainHop(
        string Caller,
        string Callee,
        string IterationKind,
        string IterationDetail,
        string FilePath,
        int Line,
        int IntraDepth
    );

    // One reported finding, at ANCHOR CALL SITE grain (mirroring CrossMethodAmplificationDataset.AnchorFinding
    // — the grain a human reviews). The chain is the whole evidence: every loop between the head and the
    // effect, each with its own file:line, so the finding is actionable without re-running rig.
    public sealed record Finding(
        // Stacked iteration contexts from the head down to the effect, or Unbounded for a recursive chain.
        int Degree,
        bool Recursion,
        // TRUE when any hop's degree contribution came from the intra-method SPAN-CONTAINMENT heuristic
        // (IntraDepth > 1) rather than from a hard cross-method anchor->anchor edge. Drives the ~ / ✔ tag:
        // the cross-method composition is a graph fact, the intra-method nesting is a line-range inference.
        bool IntraContribution,
        IReadOnlyList<ChainHop> Chain,
        string EffectProvider,
        string EffectOperation,
        string EffectResource,
        string EffectEnclosing,
        string EffectFilePath,
        int EffectLine,
        // Call-graph hops from the LAST chain hop's callee to the effect's enclosing method. 0 = the effect is
        // in that callee's own body. Same meaning as CorrelationFinding.WitnessDepth.
        int EffectDepth
    )
    {
        public ChainHop Head => Chain[0];

        public string EffectKind => $"{EffectProvider}:{EffectOperation}";

        // ✔ = every degree contribution is a cross-method anchor->anchor edge (a call-graph fact).
        // ~ = at least one contribution is intra-method line-span containment (a heuristic; see IntraDepths).
        public string Confidence => IntraContribution ? "~" : "✔";
    }

    // Every anchor whose chain terminates in an IN-SCOPE effect, with its degree, its chain and its terminal
    // effect. Unfiltered and unranked — --min-degree/--top/effect-kind ordering are presentation, and live in
    // the command. Deterministic order: degree desc, then file, then line.
    //
    // `scope` is the rules-declared display scope (observations.amplification), applied through
    // AmplificationScope — no provider is named in this file.
    public static IReadOnlyList<Finding> Derive(
        IReadOnlyList<FactInvocation> invocations,
        FactGraphData graph,
        IReadOnlyList<DerivedEffect> effects,
        FactObservationRules observationRules,
        IReadOnlyList<FactAmplificationRule> scope,
        int maxDepth = DefaultMaxDepth,
        int maxNodes = DefaultMaxNodes,
        int batchSize = DefaultBatchSize
    )
    {
        var anchors = Anchors(invocations, observationRules);
        if (anchors.Count == 0)
        {
            return [];
        }

        // The in-scope effect sites, indexed by their enclosing symbol — the reach pass's second lookup.
        var inScope = new List<DerivedEffect>();
        var effectsByEnclosing = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var e in effects)
        {
            if (e.EnclosingSymbolId is null || !AmplificationScope.Includes(scope, e.Provider, e.Operation))
            {
                continue;
            }

            if (!effectsByEnclosing.TryGetValue(e.EnclosingSymbolId, out var bucket))
            {
                bucket = [];
                effectsByEnclosing[e.EnclosingSymbolId] = bucket;
            }

            bucket.Add(inScope.Count);
            inScope.Add(e);
        }

        if (inScope.Count == 0)
        {
            return [];
        }

        // Anchor lookup by ENCLOSING method: reaching a method means reaching every looped call site in it.
        var anchorsByCaller = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < anchors.Count; i++)
        {
            if (!anchorsByCaller.TryGetValue(anchors[i].Caller, out var bucket))
            {
                bucket = [];
                anchorsByCaller[anchors[i].Caller] = bucket;
            }

            bucket.Add(i);
        }

        var (successors, effectOf, effectDepth) = ReachPass(
            graph: graph,
            anchors: anchors,
            anchorsByCaller: anchorsByCaller,
            effectsByEnclosing: effectsByEnclosing,
            inScope: inScope,
            maxDepth: maxDepth,
            maxNodes: maxNodes,
            batchSize: batchSize
        );

        return Compose(anchors, successors, effectOf, effectDepth, inScope);
    }

    // ---- 1. anchors -------------------------------------------------------------------------------------

    // The FP-calibrated iteration-fanout events, folded to one node per CALL SITE and annotated with their
    // intra-method loop nesting.
    internal static IReadOnlyList<Anchor> Anchors(IReadOnlyList<FactInvocation> invocations, FactObservationRules rules)
    {
        var fanouts = FactIterationFanoutDeriver.Derive(invocations, rules);

        // (Caller, FilePath, Line) -> the site. Insertion-ordered so the result is stable; the fanout deriver
        // already sorts by (file, line, callee).
        var order = new List<(string Caller, string FilePath, int Line)>();
        var sites = new Dictionary<(string, string, int), (string Kind, string Detail, List<string> Callees)>();
        foreach (var f in fanouts)
        {
            var key = (f.Caller, f.Event.FilePath, f.Event.Line);
            if (!sites.TryGetValue(key, out var site))
            {
                site = (f.IterationKind, f.IterationDetail, []);
                sites[key] = site;
                order.Add(key);
            }

            if (f.Event.EnclosingSymbolId is { } callee && !site.Callees.Contains(callee, StringComparer.Ordinal))
            {
                site.Callees.Add(callee);
            }
        }

        // Intra-method nesting, one method at a time (see IntraDepths for the heuristic and its known limit).
        var byCaller = new Dictionary<string, List<(string Kind, string Detail, int Line)>>(StringComparer.Ordinal);
        foreach (var key in order)
        {
            if (!byCaller.TryGetValue(key.Caller, out var rows))
            {
                rows = [];
                byCaller[key.Caller] = rows;
            }

            rows.Add((sites[key].Kind, sites[key].Detail, key.Line));
        }

        var depthByCaller = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var kv in byCaller)
        {
            depthByCaller[kv.Key] = IntraDepths(kv.Value);
        }

        var anchors = new List<Anchor>(order.Count);
        foreach (var key in order)
        {
            var site = sites[key];
            anchors.Add(
                new Anchor(
                    Caller: key.Caller,
                    FilePath: key.FilePath,
                    Line: key.Line,
                    IterationKind: site.Kind,
                    IterationDetail: site.Detail,
                    Callees: site.Callees,
                    IntraDepth: depthByCaller[key.Caller].TryGetValue(site.Detail, out var d) ? d : 1
                )
            );
        }

        return anchors;
    }

    // INTRA-METHOD LOOP NESTING, recovered by LINE-SPAN CONTAINMENT. Returns loopDetail -> depth (1 = not
    // nested inside another loop of the same method).
    //
    // WHY A HEURISTIC IS NEEDED AT ALL: the fact layer records only the INNERMOST loop per call site —
    // EnclosingLoopKind/Detail is a single value, with no nesting depth and no parent-loop id. So a call site
    // inside `foreach(a in as) foreach(b in bs) Load(b)` is indistinguishable, from the facts alone, from one
    // inside a single `foreach(b in bs)`. Recovering that from the store as it exists means inferring
    // containment from the only positional evidence there is: line numbers.
    //
    // THE RULE: within one enclosing method, group its looped call sites by loop DETAIL; the group's
    // [min(line), max(line)] is that loop's span. Loop B is nested inside loop A when B's span is STRICTLY
    // contained in A's — B.lo > A.lo && B.hi <= A.hi. depth(B) = 1 + |{A : A properly contains B}|.
    //
    // THIS IS THE CORRELATION REQUIREMENT, not an optimisation. Two SIBLING loops over the same collection are
    // ADDITIVE (2N), not multiplicative (N²), and a naive per-method loop COUNT would read them as degree 2.
    // Disjoint spans never contain one another, so siblings correctly stay depth 1. Measured on the real
    // MedDBase store: of the 1,290 methods with >=2 distinct loops, only 240 have a genuinely nested pair — a
    // count-based rule would have overstated 81% of them.
    //
    // ONE QUERY EXPRESSION IS ONE LOOP CONTEXT, however many details it emits. A `query` detail carries the
    // CUMULATIVE comma-joined bind set — every range variable bound so far — so a single query expression emits
    // one detail per clause, and those details' spans nest inside one another by construction. Left alone, the
    // containment rule reads one query as a stack of loops: measured on the real store,
    // MedDBase.DataServer.Default.Servlet.Register.GetRegisterByInvoiceDate emitted
    // `invoice, billingitem` [515,687] and `invoice, billingitem, account` [531,559] (plus three more clauses)
    // and read as intra-depth 5 — five stacked loops for one query, exactly the overcount this heuristic
    // exists to prevent. So before testing containment, two `query` loops whose IDENTIFIER SETS are
    // subset-related (one is a prefix/superset of the other — the cumulative shape) are FOLDED into one loop
    // FAMILY: their spans union, they contribute a single degree, and neither can nest inside the other.
    // Identifiers come from IterationContext.LoopIdentifiers, the same parse the anchor deriver uses.
    //
    // This deliberately also folds a genuine multi-`from` CROSS PRODUCT (`from a in A from b in B`), which
    // really is N² inside one query. The facts cannot separate a cross-product `from` from a `join`/`let`
    // (all three just extend the bind set), so folding is the PRECISION-favouring choice: a missed real
    // product costs recall on one shape, a manufactured one costs the credibility of every degree >= 2. Only
    // `query` folds — two `foreach` loops binding `a` and `b` with one span inside the other is genuine
    // nesting and still composes.
    //
    // KNOWN RESIDUAL IMPRECISION, disclosed rather than fixed: two sibling loops with the SAME detail text,
    // separated by a third loop, merge into one span that then appears to contain the third — inflating that
    // third loop's depth by one. Fixing it needs a real parent-loop id in the facts (a store-schema change,
    // out of scope here), so instead every intra-method contribution is tagged `~` and never `✔`.
    internal static Dictionary<string, int> IntraDepths(IReadOnlyList<(string Kind, string Detail, int Line)> sites)
    {
        // One entry per distinct loop detail in this method, with its kind and its [min,max] line span.
        var order = new List<string>();
        var loops = new Dictionary<string, (string Kind, int Lo, int Hi)>(StringComparer.Ordinal);
        foreach (var (kind, detail, line) in sites)
        {
            if (loops.TryGetValue(detail, out var loop))
            {
                loops[detail] = (loop.Kind, Math.Min(loop.Lo, line), Math.Max(loop.Hi, line));
                continue;
            }

            loops[detail] = (kind, line, line);
            order.Add(detail);
        }

        var count = order.Count;
        var identifiers = new HashSet<string>[count];
        var family = new int[count];
        for (var i = 0; i < count; i++)
        {
            identifiers[i] = new HashSet<string>(IterationContext.LoopIdentifiers(loops[order[i]].Kind, order[i]), StringComparer.Ordinal);
            family[i] = i;
        }

        // Fold the clauses of one query expression together (union-find over the subset relation).
        for (var i = 0; i < count; i++)
        for (var j = i + 1; j < count; j++)
        {
            if (!IsQuery(loops[order[i]].Kind) || !IsQuery(loops[order[j]].Kind))
            {
                continue;
            }

            if (identifiers[i].Count == 0 || identifiers[j].Count == 0)
            {
                continue;
            }

            if (identifiers[i].IsSubsetOf(identifiers[j]) || identifiers[j].IsSubsetOf(identifiers[i]))
            {
                Union(i, j);
            }
        }

        // A family's span is the union of its members' spans — the whole query expression's extent.
        var familySpans = new Dictionary<int, (int Lo, int Hi)>();
        for (var i = 0; i < count; i++)
        {
            var root = Find(i);
            var span = loops[order[i]];
            familySpans[root] = familySpans.TryGetValue(root, out var known)
                ? (Math.Min(known.Lo, span.Lo), Math.Max(known.Hi, span.Hi))
                : (span.Lo, span.Hi);
        }

        // Containment is tested between FAMILIES, so a query contributes one degree no matter how many
        // details it emitted.
        var familyDepths = new Dictionary<int, int>();
        foreach (var inner in familySpans)
        {
            var depth = 1;
            foreach (var outer in familySpans)
            {
                if (outer.Key != inner.Key && Contains(outer.Value, inner.Value))
                {
                    depth++;
                }
            }

            familyDepths[inner.Key] = depth;
        }

        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            depths[order[i]] = familyDepths[Find(i)];
        }

        return depths;

        int Find(int x)
        {
            while (family[x] != x)
            {
                family[x] = family[family[x]];
                x = family[x];
            }

            return x;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
            {
                family[rb] = ra;
            }
        }

        static bool IsQuery(string kind) => string.Equals(kind, "query", StringComparison.Ordinal);

        static bool Contains((int Lo, int Hi) outer, (int Lo, int Hi) inner) => inner.Lo > outer.Lo && inner.Hi <= outer.Hi;
    }

    // ---- 2. reach pass ----------------------------------------------------------------------------------

    // ONE traversal session, one forward BFS per anchor callee, batched. Two small projections are kept per
    // anchor — the anchors it can reach, and its nearest in-scope effect — and the reach set is then dropped.
    private static (List<HashSet<int>> Successors, int[] EffectOf, int[] EffectDepth) ReachPass(
        FactGraphData graph,
        IReadOnlyList<Anchor> anchors,
        Dictionary<string, List<int>> anchorsByCaller,
        Dictionary<string, List<int>> effectsByEnclosing,
        IReadOnlyList<DerivedEffect> inScope,
        int maxDepth,
        int maxNodes,
        int batchSize
    )
    {
        var successors = new List<HashSet<int>>(anchors.Count);
        for (var i = 0; i < anchors.Count; i++)
        {
            successors.Add([]);
        }

        var effectOf = new int[anchors.Count];
        var effectDepth = new int[anchors.Count];
        Array.Fill(effectOf, -1);

        // One seed per (anchor, callee); the per-anchor results are unioned.
        var seedAnchor = new List<int>();
        var seedIds = new List<string>();
        for (var i = 0; i < anchors.Count; i++)
        {
            foreach (var callee in anchors[i].Callees)
            {
                seedAnchor.Add(i);
                seedIds.Add(callee);
            }
        }

        var session = FactPathFinder.OpenSession(graph);
        var batch = Math.Max(1, batchSize);
        for (var start = 0; start < seedIds.Count; start += batch)
        {
            var count = Math.Min(batch, seedIds.Count - start);
            var slice = seedIds.GetRange(start, count);
            var reaches = session.ReachesInfoFromEachSeed(slice, maxDepth, maxNodes, FactPathFinder.TraversalMode.SyncCut);
            for (var k = 0; k < count; k++)
            {
                var a = seedAnchor[start + k];
                foreach (var node in reaches[k])
                {
                    if (anchorsByCaller.TryGetValue(node.Key, out var reachedAnchors))
                    {
                        foreach (var b in reachedAnchors)
                        {
                            successors[a].Add(b);
                        }
                    }

                    if (!effectsByEnclosing.TryGetValue(node.Key, out var reachedEffects))
                    {
                        continue;
                    }

                    foreach (var e in reachedEffects)
                    {
                        if (effectOf[a] < 0 || Closer(node.Value.Depth, e, effectDepth[a], effectOf[a], inScope))
                        {
                            effectOf[a] = e;
                            effectDepth[a] = node.Value.Depth;
                        }
                    }
                }
            }
            // `reaches` goes out of scope here — the batch's reach sets are the only ones ever live.
        }

        return (successors, effectOf, effectDepth);
    }

    // Which of two reachable in-scope effects REPRESENTS the anchor. Nearest wins (a depth-0 witness — the
    // effect is in the callee's own body — is a far stronger claim under path-insensitive reach than a depth-5
    // one, the same tiering CrossMethodAmplificationDataset.AnchorFinding uses); ties break on (file, line,
    // provider, operation) so the choice is deterministic across runs.
    private static bool Closer(int depth, int candidate, int bestDepth, int best, IReadOnlyList<DerivedEffect> effects)
    {
        if (depth != bestDepth)
        {
            return depth < bestDepth;
        }

        var x = effects[candidate];
        var y = effects[best];
        var byFile = string.CompareOrdinal(x.FilePath, y.FilePath);
        if (byFile != 0)
        {
            return byFile < 0;
        }

        if (x.Line != y.Line)
        {
            return x.Line < y.Line;
        }

        var byProvider = string.CompareOrdinal(x.Provider, y.Provider);
        return byProvider != 0 ? byProvider < 0 : string.CompareOrdinal(x.Operation, y.Operation) < 0;
    }

    // ---- 3. degree DP + chain reconstruction ------------------------------------------------------------

    private static IReadOnlyList<Finding> Compose(
        IReadOnlyList<Anchor> anchors,
        List<HashSet<int>> successors,
        int[] effectOf,
        int[] effectDepth,
        IReadOnlyList<DerivedEffect> inScope
    )
    {
        var n = anchors.Count;
        var succ = new int[n][];
        var selfEdge = new bool[n];
        for (var i = 0; i < n; i++)
        {
            selfEdge[i] = successors[i].Contains(i);
            var outgoing = new List<int>(successors[i].Count);
            foreach (var b in successors[i])
            {
                if (b != i)
                {
                    outgoing.Add(b);
                }
            }

            outgoing.Sort();
            succ[i] = [.. outgoing];
        }

        var (component, components) = StronglyConnected(succ);

        // Per-component state, filled in Tarjan's EMISSION order — which is reverse topological, so every
        // component a component can reach is already resolved when its turn comes.
        var cyclic = new bool[components.Count];
        var bearing = new bool[components.Count]; // reaches an in-scope effect, directly or through a successor
        var degree = new int[components.Count];
        var best = new int[n];
        Array.Fill(best, -1);

        for (var c = 0; c < components.Count; c++)
        {
            var members = components[c];
            cyclic[c] = members.Count > 1 || selfEdge[members[0]];

            var reachesEffect = false;
            foreach (var v in members)
            {
                reachesEffect |= effectOf[v] >= 0;
                foreach (var w in succ[v])
                {
                    if (component[w] != c)
                    {
                        reachesEffect |= bearing[component[w]];
                    }
                }
            }

            bearing[c] = reachesEffect;

            if (cyclic[c])
            {
                // A cycle multiplies by a runtime-only bound. No finite degree is honest here.
                degree[c] = Unbounded;
                continue;
            }

            // Acyclic component == exactly one anchor.
            var a = members[0];
            var deepest = 0;
            var unbounded = false;
            foreach (var w in succ[a])
            {
                if (!bearing[component[w]])
                {
                    continue; // a loop that leads to no in-scope effect composes with nothing
                }

                if (degree[component[w]] == Unbounded)
                {
                    unbounded = true; // the chain below enters a cycle, so this anchor's degree is too
                    continue;
                }

                if (degree[component[w]] > deepest)
                {
                    deepest = degree[component[w]];
                    best[a] = w;
                }
            }

            if (unbounded)
            {
                degree[c] = Unbounded;
                best[a] = -1;
                continue;
            }

            degree[c] = anchors[a].IntraDepth + deepest;
        }

        var findings = new List<Finding>();
        for (var a = 0; a < n; a++)
        {
            var c = component[a];
            if (!bearing[c])
            {
                continue;
            }

            if (degree[c] == Unbounded)
            {
                // A recursive anchor is reported only on its OWN reachable effect: the argmax walk below is
                // meaningless once a cycle is in play, so the chain is the single hop and the effect is the
                // one the anchor itself reaches.
                if (effectOf[a] < 0)
                {
                    continue;
                }

                findings.Add(Build(anchors, [a], effectOf[a], effectDepth[a], inScope, degree: Unbounded, recursion: true));
                continue;
            }

            // The argmax successor walk. Each hop strictly descends the condensation DAG (successors of a
            // finite-degree anchor are all finite, hence acyclic), so the walk terminates; the last hop has no
            // bearing successor, which by construction means it reaches an effect itself.
            var chain = new List<int>();
            var cur = a;
            while (cur >= 0)
            {
                chain.Add(cur);
                cur = best[cur];
            }

            var tail = chain[^1];
            if (effectOf[tail] < 0)
            {
                continue; // defensive: unreachable given `bearing` above
            }

            findings.Add(Build(anchors, chain, effectOf[tail], effectDepth[tail], inScope, degree: degree[c], recursion: false));
        }

        findings.Sort(
            (x, y) =>
            {
                var byDegree = Rank(y.Degree).CompareTo(Rank(x.Degree));
                if (byDegree != 0)
                {
                    return byDegree;
                }

                var byFile = string.CompareOrdinal(x.Head.FilePath, y.Head.FilePath);
                return byFile != 0 ? byFile : x.Head.Line.CompareTo(y.Head.Line);
            }
        );
        return findings;

        // Unbounded sorts above every finite degree.
        static int Rank(int degree) => degree == Unbounded ? int.MaxValue : degree;
    }

    private static Finding Build(
        IReadOnlyList<Anchor> anchors,
        IReadOnlyList<int> chain,
        int effect,
        int depth,
        IReadOnlyList<DerivedEffect> inScope,
        int degree,
        bool recursion
    )
    {
        var hops = new List<ChainHop>(chain.Count);
        var intra = false;
        foreach (var a in chain)
        {
            var anchor = anchors[a];
            intra |= anchor.IntraDepth > 1;
            hops.Add(
                new ChainHop(
                    Caller: anchor.Caller,
                    // The representative callee for display. Extra callees at one site are graph plumbing;
                    // the chain's next hop names the method that actually matters.
                    Callee: anchor.Callees.Count > 0 ? anchor.Callees[0] : "",
                    IterationKind: anchor.IterationKind,
                    IterationDetail: anchor.IterationDetail,
                    FilePath: anchor.FilePath,
                    Line: anchor.Line,
                    IntraDepth: anchor.IntraDepth
                )
            );
        }

        var e = inScope[effect];
        return new Finding(
            Degree: degree,
            Recursion: recursion,
            IntraContribution: intra,
            Chain: hops,
            EffectProvider: e.Provider,
            EffectOperation: e.Operation,
            EffectResource: e.ResourceType,
            EffectEnclosing: e.EnclosingSymbolId ?? "",
            EffectFilePath: e.FilePath,
            EffectLine: e.Line,
            EffectDepth: depth
        );
    }

    // Tarjan's SCC, ITERATIVE (the anchor graph is thousands of nodes and a recursive walk would risk the
    // stack on a long chain). Returns each node's component id and the components in EMISSION order, which is
    // reverse topological — the property the degree DP above depends on.
    private static (int[] Component, List<List<int>> Components) StronglyConnected(int[][] succ)
    {
        var n = succ.Length;
        var index = new int[n];
        var low = new int[n];
        var cursor = new int[n];
        var onStack = new bool[n];
        var component = new int[n];
        Array.Fill(index, -1);
        Array.Fill(component, -1);

        var components = new List<List<int>>();
        var pending = new Stack<int>();
        var work = new Stack<int>();
        var next = 0;

        for (var root = 0; root < n; root++)
        {
            if (index[root] >= 0)
            {
                continue;
            }

            index[root] = low[root] = next++;
            pending.Push(root);
            onStack[root] = true;
            work.Push(root);

            while (work.Count > 0)
            {
                var v = work.Peek();
                if (cursor[v] < succ[v].Length)
                {
                    var w = succ[v][cursor[v]++];
                    if (index[w] < 0)
                    {
                        index[w] = low[w] = next++;
                        pending.Push(w);
                        onStack[w] = true;
                        work.Push(w);
                    }
                    else if (onStack[w])
                    {
                        low[v] = Math.Min(low[v], index[w]);
                    }

                    continue;
                }

                work.Pop();
                if (work.Count > 0)
                {
                    var parent = work.Peek();
                    low[parent] = Math.Min(low[parent], low[v]);
                }

                if (low[v] != index[v])
                {
                    continue;
                }

                var members = new List<int>();
                while (true)
                {
                    var w = pending.Pop();
                    onStack[w] = false;
                    component[w] = components.Count;
                    members.Add(w);
                    if (w == v)
                    {
                        break;
                    }
                }

                components.Add(members);
            }
        }

        return (component, components);
    }
}
