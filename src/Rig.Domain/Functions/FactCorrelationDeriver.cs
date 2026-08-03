using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// Stage-2 GENERIC effect-correlation derivation — the FR-7 reframe. Many consistency hazards share one
// shape: an ANCHOR effect (e.g. a bulk write to entity X) whose forward closure is EXPECTED to also contain
// a COMPANION effect that refers to the SAME resource (e.g. a cache invalidation of X). When the companion
// is ABSENT on the closure, the anchor is a hazard candidate. This generalizes FactCacheCoherenceDeriver
// (cache_coherence) off EFFECTS + ResourceKey co-reference, rather than re-implementing the pairing per
// detector.
//
// This session implements ONLY Relation=CompanionForwardReachable, Polarity=Absence: flag every anchor whose
// normalized resource key has NO companion-of-the-same-key forward-reachable from the anchor's enclosing
// method. The reach question is answered by the SHIPPED engine (FactPathFinder.ReachesFromEachSeed, EXACT-id
// seeding — NOT the substring pattern Reaches the old FR-7 deriver used). The reach set of a seed INCLUDES
// the seed itself, so a companion in the anchor's OWN enclosing method counts as present.
//
// Pure, no I/O, input not mutated.

// A predicate over a DerivedEffect: matches iff Provider equals (ordinal) and, when Operation is non-null,
// Operation equals (ordinal) too. Operation null = any operation of that provider.
public sealed record EffectPredicate(string Provider, string? Operation = null);

// The structural relationship asserted between an anchor and its companion. Only one this session.
public enum CorrelationRelation
{
    // The companion (same resource key) must be FORWARD-REACHABLE from the anchor's enclosing method.
    CompanionForwardReachable,
}

// Which side of the relation is the FINDING.
public enum CorrelationPolarity
{
    // Flag anchors that LACK the expected companion (the missing-invalidation / dual-write-gap shape).
    Absence,

    // Flag anchors that HAVE the companion (the amplification / co-presence shape). The finding carries the
    // WITNESS that made it fire — an absence finding has nothing to point at, a presence one does.
    Presence,
}

// How a companion is matched to an anchor beyond the predicate.
public enum CorrelationKeyMatch
{
    // Today's behavior: normalized ResourceKey equality (bulk_write of X <-> invalidate of X).
    ResourceKeyEquality,

    // The anchor's key TOKEN is carried as EVIDENCE, not as a join condition: it cannot be followed across a
    // call boundary until the callee's parameter names are a fact, and — the point of the presence polarity —
    // C# statements are eager, so a read beneath a per-element call runs per element whatever its key. A null
    // token is therefore data, not a disqualifier, and every anchor's key state is reported rather than gated.
    PropagatedKeyToken,
}

public sealed record CorrelationSpec(
    EffectPredicate Anchor,
    EffectPredicate Companion,
    NormalizeSpec AnchorNormalize,
    NormalizeSpec CompanionNormalize,
    CorrelationRelation Relation = CorrelationRelation.CompanionForwardReachable,
    CorrelationPolarity Polarity = CorrelationPolarity.Absence,
    // Generated-ORM-noise filter: skip an anchor whose ENCLOSING NAMESPACE ends with any of these suffixes
    // (e.g. "CollectionClasses"/"DaoClasses" — the LLBLGen-generated mutators that are never the real bug
    // site). Null/empty = no filtering.
    IReadOnlyList<string>? ExcludeEnclosingNamespaceSuffix = null,
    // Restrict + TIER the anchors by resource key: key -> an opaque CERTAINTY token carried onto the finding.
    // When non-null, an anchor is considered ONLY if its key is present here; the finding inherits the token.
    // The deriver stays policy-free — the CALLER decides which keys are in scope and at what certainty. For
    // cache_coherence the wiring unions two sources (declared wins on overlap):
    //   * a DECLARED contract (the rule's named cached entities) -> "high". This is the intentional invariant
    //     ("X is cached and MUST be invalidated"); it is STABLE — if an accidental merge deletes every
    //     invalidation, declared entities STILL flag (the detector screams loudest exactly when the bug is
    //     worst), which an "infer cached-ness from invalidation existence" gate would silently disarm.
    //   * keys INFERRED from cache READS (entity_cache:read) -> "medium". Read evidence does NOT vanish when
    //     a bust is deleted (code still reads the cache), so this discovers cached sets not yet in the
    //     contract WITHOUT the self-silencing hole — it is keyed off READS, never off the companion itself.
    // Null = no restriction and no tiering (every anchor in scope; Certainty left null) — the generic default.
    IReadOnlyDictionary<string, string>? InScopeKeys = null,
    int MaxDepth = 20,
    FactPathFinder.TraversalMode Mode = FactPathFinder.TraversalMode.SyncCut,
    CorrelationKeyMatch KeyMatch = CorrelationKeyMatch.ResourceKeyEquality,
    // A companion matches if it satisfies ANY of these predicates. Null = the single `Companion` predicate.
    // The presence instance needs a SET (a read is any of ~7 provider:operation pairs) and must not pay for a
    // reach per pair.
    IReadOnlyList<EffectPredicate>? Companions = null,
    // Presence only: at most this many witnesses per anchor, nearest-depth first. 0 = unlimited, i.e. the full
    // (anchor x witness) cross product — the DATA-GATHERING grain, not the finding grain.
    int MaxWitnessesPerAnchor = 1
);

// One anchor effect with no matching companion on its forward closure. Method = the anchor's
// EnclosingSymbolId; ResourceKey = the normalized resource identity that went unmatched. FilePath/Line are
// the anchor effect's source location.
public sealed record CorrelationFinding(
    string Method,
    string ResourceKey,
    string FilePath,
    int Line,
    string AnchorProvider,
    string AnchorOperation,
    // The certainty token from spec.InScopeKeys for this finding's key (e.g. "high" for a declared-contract
    // entity, "medium" for one inferred from cache reads). Null when the spec did not tier keys.
    string? Certainty = null,
    // PRESENCE only: the companion that made this fire, with the reach evidence that found it. All null for
    // Absence — which has nothing to point at — so the Absence output (and the FR-7 golden oracle) is
    // byte-identical to before these fields existed.
    string? WitnessMethod = null,
    string? WitnessFilePath = null,
    int? WitnessLine = null,
    string? WitnessResourceKey = null,
    string? WitnessProvider = null,
    string? WitnessOperation = null,
    int? WitnessDepth = null,
    // Reach evidence for the witness, carried because dispatch fan-out must be visible as DOUBT: a witness
    // found only through a heuristically-resolved virtual call may live in an implementation this anchor never
    // reaches. Never a filter here — the analysis pass decides what it is worth.
    string? WitnessDispatchBasis = null,
    string? WitnessDispatchVia = null,
    int? WitnessDispatchDegree = null
);

public static class FactCorrelationDeriver
{
    // Returns every ANCHOR effect (matching spec.Anchor) whose normalized resource key has no COMPANION
    // effect (matching spec.Companion, same normalized key) forward-reachable from the anchor's enclosing
    // method. Determinism (stable tests + a future cache): findings de-duped, ordered by (Method ordinal,
    // Line). Mirrors FactCacheCoherenceDeriver.DeriveCacheCoherence.
    public static IReadOnlyList<CorrelationFinding> Derive(FactGraphData graph, IReadOnlyList<DerivedEffect> effects, CorrelationSpec spec)
    {
        if (spec.Polarity == CorrelationPolarity.Presence)
        {
            return DerivePresence(graph, effects, spec);
        }

        // 2. Companions index: resource key -> the set of enclosing nodes that invalidate it.
        var companions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var e in effects)
        {
            if (e.EnclosingSymbolId is null || !Matches(e, spec.Companion))
            {
                continue;
            }

            var key = ResourceKey.Of(e.ResourceType, spec.CompanionNormalize);
            if (key is null)
            {
                continue;
            }

            if (!companions.TryGetValue(key, out var nodes))
            {
                nodes = new HashSet<string>(StringComparer.Ordinal);
                companions[key] = nodes;
            }

            nodes.Add(e.EnclosingSymbolId);
        }

        // 3. Anchors: the surviving (effect, key, certainty) tuples after predicate + namespace-suffix +
        //    in-scope-key filtering. The certainty token (if any) rides from spec.InScopeKeys onto the finding.
        var anchors = new List<(DerivedEffect Effect, string Key, string? Certainty)>();
        foreach (var e in effects)
        {
            if (e.EnclosingSymbolId is null || !Matches(e, spec.Anchor))
            {
                continue;
            }

            if (ExcludedByNamespace(e.EnclosingSymbolId, spec.ExcludeEnclosingNamespaceSuffix))
            {
                continue;
            }

            var key = ResourceKey.Of(e.ResourceType, spec.AnchorNormalize);
            if (key is null)
            {
                continue;
            }

            // In-scope gate: when the spec tiers keys, an anchor whose key is not in scope is dropped; an
            // in-scope key carries its certainty token. No map => every key in scope, certainty null.
            string? certainty = null;
            if (spec.InScopeKeys is { } inScope)
            {
                if (!inScope.TryGetValue(key, out certainty))
                {
                    continue;
                }
            }

            anchors.Add((e, key, certainty));
        }

        // 4. Reach: one forward-reach per DISTINCT anchor enclosing id (one shared index, parallel per seed).
        var distinctEnclosing = anchors.Select(a => a.Effect.EnclosingSymbolId!).Distinct(StringComparer.Ordinal).ToList();
        var reachSets = FactPathFinder.ReachesFromEachSeed(
            graph,
            distinctEnclosing,
            maxDepth: spec.MaxDepth,
            maxNodes: 20000,
            narrowDispatch: true,
            mode: spec.Mode
        );
        var reachOf = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (var i = 0; i < distinctEnclosing.Count; i++)
        {
            reachOf[distinctEnclosing[i]] = reachSets[i];
        }

        // 5. Emit: an anchor is CLEAN iff its reach set (which INCLUDES the seed itself) intersects the
        // companion nodes for its key. Otherwise flag it.
        var findings = new List<CorrelationFinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (effect, key, certainty) in anchors)
        {
            var reach = reachOf[effect.EnclosingSymbolId!];
            if (companions.TryGetValue(key, out var companionNodes) && reach.Overlaps(companionNodes))
            {
                continue;
            }

            var finding = new CorrelationFinding(
                Method: effect.EnclosingSymbolId!,
                ResourceKey: key,
                FilePath: effect.FilePath,
                Line: effect.Line,
                AnchorProvider: effect.Provider,
                AnchorOperation: effect.Operation,
                Certainty: certainty
            );

            if (seen.Add(finding.Method + " " + finding.ResourceKey + " " + finding.FilePath + " " + finding.Line))
            {
                findings.Add(finding);
            }
        }

        // 6. Determinism.
        findings.Sort(
            (a, b) =>
            {
                var byMethod = string.CompareOrdinal(a.Method, b.Method);
                return byMethod != 0 ? byMethod : a.Line.CompareTo(b.Line);
            }
        );
        return findings;
    }

    // Polarity=Presence: every (anchor, witness) pair where a COMPANION is forward-reachable from the anchor's
    // enclosing symbol. Structurally the same three steps as the Absence path — companion index, anchor filter,
    // one reach per distinct anchor enclosing id — with three differences, each following from the polarity:
    //   * the companion index is by ENCLOSING NODE, not by resource key: presence does not join on identity
    //     (see CorrelationKeyMatch.PropagatedKeyToken), it asks only "is one of these below me";
    //   * the reach keeps its ReachInfo, because DEPTH picks the nearest witness and is itself evidence;
    //   * the emission is per PAIR (capped by MaxWitnessesPerAnchor), because a presence finding without its
    //     witness is unactionable.
    private static IReadOnlyList<CorrelationFinding> DerivePresence(
        FactGraphData graph,
        IReadOnlyList<DerivedEffect> effects,
        CorrelationSpec spec
    )
    {
        var companionsByNode = new Dictionary<string, List<DerivedEffect>>(StringComparer.Ordinal);
        foreach (var e in effects)
        {
            if (e.EnclosingSymbolId is null || !MatchesAny(e, spec))
            {
                continue;
            }

            if (!companionsByNode.TryGetValue(e.EnclosingSymbolId, out var here))
            {
                here = [];
                companionsByNode[e.EnclosingSymbolId] = here;
            }

            here.Add(e);
        }

        // Anchors. No key gate and no normalization: the anchor's ResourceType IS its key token, "" when the
        // site is keyless, and a keyless anchor is exactly as much of a finding (§ the eager-statement premise).
        var anchors = new List<(DerivedEffect Effect, string Key, string? Certainty)>();
        foreach (var e in effects)
        {
            if (e.EnclosingSymbolId is null || !Matches(e, spec.Anchor))
            {
                continue;
            }

            if (ExcludedByNamespace(e.EnclosingSymbolId, spec.ExcludeEnclosingNamespaceSuffix))
            {
                continue;
            }

            string? certainty = null;
            if (spec.InScopeKeys is { } inScope && !inScope.TryGetValue(e.ResourceType, out certainty))
            {
                continue;
            }

            anchors.Add((e, e.ResourceType, certainty));
        }

        var distinctEnclosing = anchors.Select(a => a.Effect.EnclosingSymbolId!).Distinct(StringComparer.Ordinal).ToList();
        var reachSets = FactPathFinder.ReachesInfoFromEachSeed(
            graph,
            distinctEnclosing,
            maxDepth: spec.MaxDepth,
            maxNodes: 20000,
            narrowDispatch: true,
            mode: spec.Mode
        );
        var reachOf = new Dictionary<string, IReadOnlyDictionary<string, FactPathFinder.ReachInfo>>(StringComparer.Ordinal);
        for (var i = 0; i < distinctEnclosing.Count; i++)
        {
            reachOf[distinctEnclosing[i]] = reachSets[i];
        }

        var findings = new List<CorrelationFinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (effect, key, certainty) in anchors)
        {
            var reach = reachOf[effect.EnclosingSymbolId!];
            var witnesses = new List<(DerivedEffect Witness, FactPathFinder.ReachInfo Reach)>();
            foreach (var (node, info) in reach)
            {
                if (companionsByNode.TryGetValue(node, out var here))
                {
                    witnesses.AddRange(here.Select(w => (w, info)));
                }
            }

            // Nearest-depth first, then a total order so the cap (and the emitted dataset) is deterministic.
            witnesses.Sort(
                (a, b) =>
                {
                    var byDepth = a.Reach.Depth.CompareTo(b.Reach.Depth);
                    if (byDepth != 0)
                    {
                        return byDepth;
                    }

                    var byMethod = string.CompareOrdinal(a.Witness.EnclosingSymbolId, b.Witness.EnclosingSymbolId);
                    return byMethod != 0 ? byMethod : a.Witness.Line.CompareTo(b.Witness.Line);
                }
            );

            var take = spec.MaxWitnessesPerAnchor <= 0 ? witnesses.Count : Math.Min(spec.MaxWitnessesPerAnchor, witnesses.Count);
            for (var i = 0; i < take; i++)
            {
                var (witness, info) = witnesses[i];
                var finding = new CorrelationFinding(
                    Method: effect.EnclosingSymbolId!,
                    ResourceKey: key,
                    FilePath: effect.FilePath,
                    Line: effect.Line,
                    AnchorProvider: effect.Provider,
                    AnchorOperation: effect.Operation,
                    Certainty: certainty,
                    WitnessMethod: witness.EnclosingSymbolId,
                    WitnessFilePath: witness.FilePath,
                    WitnessLine: witness.Line,
                    WitnessResourceKey: witness.ResourceType,
                    WitnessProvider: witness.Provider,
                    WitnessOperation: witness.Operation,
                    WitnessDepth: info.Depth,
                    WitnessDispatchBasis: info.DispatchBasis,
                    WitnessDispatchVia: info.DispatchVia,
                    WitnessDispatchDegree: info.DispatchDegree
                );

                if (
                    seen.Add(
                        finding.Method
                            + " "
                            + finding.FilePath
                            + " "
                            + finding.Line
                            + " "
                            + finding.WitnessMethod
                            + " "
                            + finding.WitnessFilePath
                            + " "
                            + finding.WitnessLine
                    )
                )
                {
                    findings.Add(finding);
                }
            }
        }

        findings.Sort(
            (a, b) =>
            {
                var byFile = string.CompareOrdinal(a.FilePath, b.FilePath);
                if (byFile != 0)
                {
                    return byFile;
                }

                var byLine = a.Line.CompareTo(b.Line);
                if (byLine != 0)
                {
                    return byLine;
                }

                var byMethod = string.CompareOrdinal(a.Method, b.Method);
                if (byMethod != 0)
                {
                    return byMethod;
                }

                var byDepth = (a.WitnessDepth ?? 0).CompareTo(b.WitnessDepth ?? 0);
                if (byDepth != 0)
                {
                    return byDepth;
                }

                var byWitness = string.CompareOrdinal(a.WitnessMethod, b.WitnessMethod);
                return byWitness != 0 ? byWitness : (a.WitnessLine ?? 0).CompareTo(b.WitnessLine ?? 0);
            }
        );
        return findings;
    }

    private static bool MatchesAny(DerivedEffect e, CorrelationSpec spec)
    {
        if (spec.Companions is not { Count: > 0 } predicates)
        {
            return Matches(e, spec.Companion);
        }

        foreach (var p in predicates)
        {
            if (Matches(e, p))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(DerivedEffect e, EffectPredicate p) =>
        string.Equals(e.Provider, p.Provider, StringComparison.Ordinal)
        && (p.Operation is null || string.Equals(e.Operation, p.Operation, StringComparison.Ordinal));

    // True iff the enclosing method's NAMESPACE ends with any of the given suffixes (ordinal). The enclosing
    // id is a method DocID like "M:MedDBase.CollectionClasses.AccountCollection.UpdateMulti(System.Object)".
    // The NAMESPACE is the DocID head (everything before the first '(') with the last TWO dot-segments — the
    // type simple name and the method simple name — removed: -> "MedDBase.CollectionClasses". Then suffix
    // "CollectionClasses" matches "MedDBase.CollectionClasses" via EndsWith.
    private static bool ExcludedByNamespace(string enclosingId, IReadOnlyList<string>? suffixes)
    {
        if (suffixes is not { Count: > 0 })
        {
            return false;
        }

        var ns = EnclosingNamespace(enclosingId);
        if (ns is null)
        {
            return false;
        }

        foreach (var suffix in suffixes)
        {
            if (suffix.Length > 0 && ns.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Extract the namespace from a method DocID: drop the parameter list (everything from the first '('),
    // then drop the last two '.'-segments (method simple name + declaring-type simple name). Returns null
    // when there are not enough segments to have a namespace.
    private static string? EnclosingNamespace(string enclosingId)
    {
        var paren = enclosingId.IndexOf('(');
        var head = paren >= 0 ? enclosingId[..paren] : enclosingId;

        // Drop the method simple name (last segment).
        var lastDot = head.LastIndexOf('.');
        if (lastDot < 0)
        {
            return null;
        }
        var withoutMethod = head[..lastDot];

        // Drop the declaring-type simple name (now the last segment); what remains is the namespace. The
        // leading "M:" prefix has no '.', so it stays attached to the first namespace segment but never
        // affects an EndsWith suffix test.
        var typeDot = withoutMethod.LastIndexOf('.');
        return typeDot < 0 ? null : withoutMethod[..typeDot];
    }
}
