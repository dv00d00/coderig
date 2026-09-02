using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rig.Domain.Functions;

namespace Rig.Cli.Caching;

// The query cache's key derivation + the best-effort write wrapper. The keys embed a store identity
// (rig.db size+mtime) so any reindex auto-invalidates them, plus the effective rule fingerprint and the
// traversal parameters the cached artifact is a function of.
internal static class QueryCacheKeys
{
    // Payload/derivation-LOGIC schema versions — the "vN" that gates each cached artifact on the derivation
    // logic + payload shape, independently of the two data axes every key already carries (store identity =
    // reindex tripwire; rulesHash = rule-edit tripwire). Bump the one whose logic/shape changed; the change
    // then misses server-side AND (via DerivationSchemaToken below) flushes the client cache in lockstep.
    // These REPLACE the old assembly-MVID hedge: the MVID moved on every recompile — busting the expensive
    // impact diff and the >1 MB client trees on any unrelated `.cs` edit — whereas these move only on a
    // deliberate logic/schema change, which is the honest signal. The cost is discipline: a derivation
    // change with no matching bump serves stale (same tradeoff the tree/hazard keys have always carried).
    // Gates BOTH entry-point artifacts, because both are the SAME derivation (FactEntryPointDeriver.Derive +
    // the classified-handoff promotion) projected two ways: the site->kind map (EpCacheKey / EpSiteCacheCodec)
    // and the full EP RECORD list (EpRecordsCacheKey / EntryPointRecordCodec — route + requires + handler
    // DocID, which the site map collapses away). One constant, so a change to that derivation can never
    // invalidate one projection and leave the other serving the pre-change EP set.
    // NOT bumped when the record projection was added (2026-08-24): `eprecords|v1|…` is a NEW key namespace no
    // prior rig ever wrote a blob under, and the site-map payload/derivation it shares this constant with did
    // not change — so there was nothing warm to flush, on disk or in a browser. Bump on the next EP-DERIVATION
    // change (same store + same rules, different EP set), which is what this constant is for.
    // v1->v2: EXTERNAL-NODE ADMISSION. The handoff-origin promotion this constant also gates runs over the
    // call graph's method-group edges, and the graph now admits out-of-source targets — so a LIBRARY method
    // group handed to a configured dispatcher is promoted to a handoff entry point where it previously fell
    // out with the TargetInSource filter. Same store, same rules, a (possibly) larger EP set.
    internal const int EpSchema = 2;

    // v1->v2: TraceNode gained TruncationCause (no stale conflated seen flags); v2->v3: the BOUNDED reach-input
    // loader now carries reference_facts.EnclosingScopes (it never selected the column), so the cached effects
    // gain the lexical-scope observations — lock_held_across_effect / transaction_spans_effect — that were
    // silently absent on the SQL fast path. Same store, same rules, MORE observations: a warm v2 blob would
    // keep serving the pre-fix effects (and, through DeriveCommand.HazardFindings, the mis-tiered race_window /
    // lazy_init classification the span observations drive) forever.
    // v3->v4: whole-graph monomorphization no longer has a corpus-global instantiation cap, and generic-
    // factory candidate resolution now preserves containing-type arity; both change same-store tree reach.
    // v4->v5: --limit now excludes never-visited staged siblings, making cached forests strictly node-bounded.
    // v5->v6: exact open-generic identities now include their monomorphized executions instead of retaining a
    // warm forest rooted only at the open fallback body.
    // v6->v7: the expansion memo is keyed by DISPATCH CONTEXT, not by bare symbol, so a virtual hub reached
    // under several receivers devirtualizes per receiver instead of the first occurrence winning and every
    // later one collapsing to "⋯elided". Same store, same rules, MORE forest: a warm v6 blob would keep
    // serving trees whose child overrides (and their effects) are invisible.
    // v7->v8: EXTERNAL-NODE ADMISSION — admitted library/BCL call targets are now first-class LEAF nodes,
    // so the same store + rules yields a forest with MORE children (and a larger "Reachable methods" count),
    // and TraceNode gained IsExternal, which the renderers key the «external» tag off. A warm v7 blob both
    // misses the new leaves and decodes IsExternal=false for every node it does have.
    internal const int TreeSchema = 8;

    // v1->v2 EnclosingGuards; v2->v3 lazy_init_race lock-enclosed tier; v3->v4 the n_plus_1 read gate gained
    // object_store + the `execute` operation (a BUILTIN-rules edit, which the rulesHash — computed over the
    // loaded rule FILES — does not see, so without this bump a warm cache would keep serving the old 175).
    internal const int HazardEffectsSchema = 4;

    // v1->v2: remove the corpus-global generic-instantiation cap and preserve generic-factory type arity.
    // v2->v3: cache_coherence runs FactCorrelationDeriver over forward reach sets, and reach is now resolved
    // per dispatch context — a companion invalidate behind a second receiver's override is no longer missed.
    // v3->v4: cache_coherence's anchor/companion/normalizers/discovery-read are RULE DATA (core-purity F1+F2)
    // and event_cycle's delivery tags + join confidence come from `deliveryRules` (F6). Same store, same rules
    // file, DIFFERENT findings: a ruleset that does not declare them now yields no cache_coherence anchors and
    // no event_cycle edges, and a warm v3 blob would keep serving findings derived from the deleted built-in
    // literals forever (the rulesHash cannot see a C#-side derivation change).
    // v4->v5: EXTERNAL-NODE ADMISSION. These findings are derived over the SHAPED CALL GRAPH, whose node/edge
    // set now includes the admitted external leaves. Conservative bump: an admitted leaf has no outgoing edge,
    // so it can neither close a cycle nor carry an effect — but any REACH-BOUNDED correlation
    // (cross_method_amplification's reach bound) counts NODES, and a bound that used to be met can now be
    // exceeded. Cheap to flush, wrong to serve stale.
    internal const int GraphHazSchema = 5;

    // The FINDING-VIEW payload/logic version: how the hazard-augmented effect set is CLASSIFIED and PROJECTED
    // into displayed findings (the /api/hazards mark stream, the derive Hazards/Amplification split), as opposed
    // to how the effects themselves are derived (HazardEffectsSchema). v1 = the amplification tier (looped_effect
    // promoted to its own displayed finding, 2026-08): the same store + rules now yield MORE marks, so a warm
    // client IndexedDB entry would keep serving the pre-tier mark list forever. No SERVER key uses this constant
    // — the /api/hazards marks are recomputed per call off the (separately keyed) effect cache — it exists to move
    // DerivationSchemaToken, which is the client's only invalidation signal.
    internal const int FindingViewSchema = 1;

    // v1: tree/reaches/path web effect views hide intrinsic alloc/throw by default and disclose that state.
    // Query payloads now depend on the intrinsic view flag; move the browser derivation version once so a
    // warm pre-filter response can never masquerade as the new default-hidden response.
    internal const int EffectViewSchema = 1;

    // v1: effects-diff joins canonical generic-method effects through concrete monomorphized executions and
    // fails closed on overload unions. The web comparison is client-cached, so its derivation token must move.
    // v1->v2: the per-EP reach sets it diffs are now resolved per dispatch context, so an EP's effect set can
    // legitimately GROW with no store or rule change.
    internal const int EffectsDiffSchema = 2;

    // v1: transparent whole-store method hotspot metrics. v1->v2: persisted lambdas joined the method
    // universe and monomorphized graph/effect/hazard identities now aggregate onto their source method.
    // Sort/top/lambda/generated filters are presentation-only.
    // v2->v3: EXTERNAL-NODE ADMISSION — CalleeMethods / OutgoingCallSites are counted straight off
    // graph.CallEdges, so every first-party method that calls a library member now scores higher on both
    // columns. Same store, same rules, a different ranking.
    internal const int HotspotSchema = 3;

    // v1: every Git-changed file has a two-path, side-optional review payload. This gates the client-cached
    // /api/review-files and /api/file-diff contracts independently of the per-file semantic projection.
    internal const int ReviewSchema = 1;

    // v1: the per-file semantic effect projection shared by web, annotate and Rider. This gates both the
    // browser derivation token and the resident process LRU, so same-store projection fixes cannot leave
    // either surface serving the pre-fix read model.
    // v1->v2: fold lambda-owned effects to declarations, preserve co-located direct rows, and enforce method/site consistency.
    // v2->v3: badges disclose dispatch-only reach (ViaDispatchOnly).
    internal const int FileEffectsSchema = 4; // v3->v4: badges carry the amplification tier (Looped)

    // v2(+MVID) -> v3: one-time flush when the per-compile MVID hedge was dropped; v3 -> v4: guard-condition
    // deltas added to the payload; v4 -> v5: the per-EP AMPLIFICATION delta (ep_amplification_added/_removed)
    // added to the payload — a warm v4 blob decodes with empty amplification lists and would silently render a
    // newly-looped effect as "no change".
    // v5->v6: remove the global mono cap and preserve factory type arity in both shaped graphs in the diff.
    // v6->v7: per-EP reach is resolved per dispatch context on BOTH sides of the diff, so an EP's footprint
    // now includes overrides reached through a receiver that was not the first to arrive at a virtual hub.
    // v7->v8: EXTERNAL-NODE ADMISSION — the per-EP reach FOOTPRINT the diff is computed from now contains
    // the admitted external leaves, so a cached side is over a different node universe than a freshly computed
    // one and would report every library leaf as a reach GAIN. (First-party reachability itself is unchanged:
    // an admitted leaf has no successors.)
    internal const int ImpactSchema = 8;

    // NOT bumped for external-node admission, deliberately:
    //   * HazardEffectsSchema — the whole-store effect set is a per-METHOD fact derived from reference_facts
    //     + rules, EP- and graph-independent. Admission changes the call GRAPH only, and an effect is keyed to
    //     its first-party ENCLOSING method (which an external leaf can never be), so the set is byte-identical.
    //   * EffectsDiffSchema — the same argument one level up: it diffs per-EP EFFECT sets, and no new effect
    //     can appear (an admitted leaf has no successors, so first-party reachability does not change either).
    //   * FindingViewSchema / EffectViewSchema — both are PROJECTION-logic versions (the mark-stream shape,
    //     the intrinsic-view default) and neither changed. They exist to move the client token, which
    //     TreeSchema / GraphHazSchema / ImpactSchema / HotspotSchema / EpSchema already do.
    //
    // The composite token the CLIENT keys its cache by (hashed with the rules fingerprint in /api/meta). It
    // folds in EVERY per-artifact schema version, so bumping ANY one above also moves the client's derivation
    // version — the client can never keep serving an artifact whose server-side schema advanced. This is the
    // desync guard that a single hand-bumped client constant would lack, and it needs no MVID.
    internal static string DerivationSchemaToken() =>
        $"{EpSchema}.{TreeSchema}.{HazardEffectsSchema}.{GraphHazSchema}.{ImpactSchema}.{FindingViewSchema}.{EffectViewSchema}.{EffectsDiffSchema}.{HotspotSchema}.{ReviewSchema}.{FileEffectsSchema}";

    internal static string FileEffectsCacheKey(string storeKey, string rulesHash, string filePath)
    {
        var material = $"filefx|v{FileEffectsSchema}|{storeKey}|{rulesHash}|{filePath}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    // Identity of the current store for cache keying + invalidation: rig.db size + last-write time.
    // `rig index` publishes a fresh db (atomic rename → new mtime/size) and `rig graph` rewrites the
    // derived edge tables in place (mtime changes), so any reindex shifts this — old cache entries no
    // longer match and are purged. Missing db → a constant sentinel (cache simply never hits).
    internal static string StoreKey(string dbPath)
    {
        try
        {
            var info = new FileInfo(dbPath);
            return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : "absent";
        }
        catch (IOException)
        {
            return "absent";
        }
    }

    // Cache key for the pattern-INDEPENDENT EP-site map: store identity + rule fingerprint only (no
    // pattern, no traversal params), so a single derivation serves every query against the store.
    internal static string EpCacheKey(string storeKey, string rulesHash)
    {
        var material = $"ep|v{EpSchema}|{storeKey}|{rulesHash}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    // Cache key for the pattern-INDEPENDENT EP RECORD list — the derived EPs + promoted handoff origins with
    // their route, capability requirements, declaration site and handler DocID intact. Same two axes as
    // EpCacheKey (store identity + rule fingerprint) and the same EpSchema gate, because it is the same
    // derivation; a DISTINCT `eprecords` namespace so the two projections can never decode each other's blob.
    // No pattern / depth / mode in the material: `callers --entrypoints` intersects this whole-store set with
    // its own closure AFTER the cache, so one derivation serves every query against the store.
    internal static string EpRecordsCacheKey(string storeKey, string rulesHash)
    {
        var material = $"eprecords|v{EpSchema}|{storeKey}|{rulesHash}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    // A forest cache key — a newtype over the hashed key string, produced ONLY by TreeCacheKey. The render
    // sidecar (RenderSidecarKey) takes this type, not a bare string, so it can NEVER be derived from a
    // non-forest key; and because the sidecar suffixes `.Value`, it inherits TreeCacheKey's full dependency +
    // version set automatically — a forest-key bump (new param / v-bump) flows through for free, so the
    // sidecar can never drift out of lockstep with the forest it hangs off. Free at runtime: a one-field
    // readonly struct over a string reference (pass-by-value = copy one pointer, no allocation, no boxing).
    internal readonly record struct ForestCacheKey(string Value);

    // The cache key for a `rig tree` forest+effects artifact: everything the artifact is a function of —
    // the store identity, the effective rule fingerprint, and the traversal parameters. `v2` is the
    // payload-schema version (bump to ignore older blobs) — bumped from v1 when TraceNode gained
    // TruncationCause, so a warm cache from before the split doesn't render stale conflated `seen` flags.
    // Render-only flags (--files/--summary/--effects and --only/--exclude) are deliberately absent: they
    // don't change the forest or the unfiltered effects, only how they're presented, so they must not
    // fragment the cache.
    internal static ForestCacheKey TreeCacheKey(
        string storeKey,
        string rulesHash,
        string fromPattern,
        int maxDepth,
        int maxNodes,
        FactPathFinder.TraversalMode mode,
        bool raw
    )
    {
        // maxNodes is in the key because a forest built under one --limit must not serve another (a
        // budget-capped forest is a DIFFERENT tree, not a different rendering of the same tree). Adding
        // the field shifts every existing key once (one cache re-warm) — accepted in lieu of a bump.
        var material = $"tree|v{TreeSchema}|{storeKey}|{rulesHash}|{fromPattern}|{maxDepth}|{maxNodes}|{mode}|{raw}";
        return new ForestCacheKey(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))));
    }

    // The cache key for the WHOLE-STORE hazard-augmented effect set (derive's effect computation: every
    // indexed symbol's effects + the field-fed shared_state arms + the race_window/dual_write/
    // thread_local_context post-pass). It is a pure function of the store + the effective rule fingerprint
    // and is EP-INDEPENDENT and TRAVERSAL-MODE-INDEPENDENT — an effect is a per-method fact, not a function
    // of which entry point reaches it or whether the walk is sync/async. So `derive`, `tree --hazards` (any
    // EP, any mode), and any future hazard surface all share ONE entry: compute once, reuse everywhere.
    // Reindex shifts storeKey (miss); a changed rule shifts rulesHash (miss) — so hazards stay query-side
    // data (a rule edit needs no re-index, just recomputes the cache). The payload-schema version bumped
    // v1->v2 when DerivedEffect gained EnclosingGuards (branch-aware-effects); a pre-guard cached set must
    // miss, else a stale hit would decode null guards and drop the ⎇ markers on effect leaves. Bumped
    // v2->v3 for the lazy_init_race lock-enclosed tier (2026-07-02): the CLASSIFIER changed with no key
    // input changing, so a warm v2 entry would keep serving pre-tier reasons indefinitely.
    internal static string HazardEffectsCacheKey(string storeKey, string rulesHash)
    {
        var material = $"hazardfx|v{HazardEffectsSchema}|{storeKey}|{rulesHash}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    // The cache key for the WHOLE-STORE GRAPH-TIER hazard findings (cache_coherence + event_cycle +
    // static_init_capture). Like the effect set above these are EP-INDEPENDENT, whole-store facts — a property
    // of the SHAPED call graph (forward-closure correlation + cycle detection) + the static-field universe, not
    // a function of which entry point reaches them. So `derive` and `tree --hazards` share ONE entry: derive
    // once over the shaped graph (the cost we must NOT pay per-EP), reuse everywhere. A reindex shifts storeKey
    // (miss); a changed rule shifts rulesHash (miss). DISTINCT namespace (`graphhaz`) from HazardEffectsCacheKey
    // so the effect-attached set and the graph-tier set never collide. `v1` is the payload-schema version.
    internal static string GraphHazardFindingsCacheKey(string storeKey, string rulesHash)
    {
        var material = $"graphhaz|v{GraphHazSchema}|{storeKey}|{rulesHash}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    // Full unsorted hotspot artifact. Intrinsic changes the effect/density columns and therefore keys the
    // artifact; sort/top/no-lambdas/generated filtering do not and must stay out of this expensive key.
    internal static string HotspotsCacheKey(string storeKey, string rulesHash, bool intrinsic)
    {
        var material = $"hotspots|v{HotspotSchema}|{storeKey}|{rulesHash}|intrinsic={intrinsic}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    // The cache key for a `rig impact` two-store diff artifact: the artifact is a pure function of the TWO
    // immutable per-commit stores (each addressed by its own StoreKey = rig.db size+mtime), the effective
    // rule fingerprint, and the traversal mode (sync-cut vs async-handoff — it changes the reach footprint).
    // Both store keys are folded in, so reindexing EITHER side shifts the key (miss); `mode` distinguishes a
    // --async run from a sync one. Render-only flags (--structural / --format / --limit) are deliberately
    // ABSENT: they only change how the SAME diff is presented (which section, truncation, tsv vs human), not
    // the diff itself, so they must not fragment the cache. `v1` is the payload-schema version (bump to
    // ignore older blobs). The artifact is stored in the HEAD store's cache.db (its store_key purge column),
    // so the base side's identity lives only in this key — a stale base store can never serve a hit.
    internal static string ImpactCacheKey(string baseStoreKey, string headStoreKey, string rulesHash, FactPathFinder.TraversalMode mode)
    {
        // Keyed like the other four artifacts: the two immutable store identities + the rule fingerprint + the
        // traversal mode + the payload/logic schema version (ImpactSchema). A `rig` upgrade that CHANGES how the
        // diff is derived must bump ImpactSchema to miss — the honest signal. (Previously this folded in the
        // assembly MVID, which changed on EVERY recompile and so recomputed this — the most expensive artifact,
        // minutes over both stores — on any unrelated edit; ImpactSchema was bumped once when that was removed.)
        var material = $"impact|v{ImpactSchema}|{baseStoreKey}|{headStoreKey}|{rulesHash}|{mode}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    // A stable signature of the effect filters (--only/--exclude) for the render-sidecar key: sorted +
    // lowercased so token order/casing don't fragment it, empty in the common no-filter case. The seam
    // summaries in the sidecar are a function of the FILTERED effects, so two queries that differ only by
    // these flags must get distinct sidecars (the forest itself is filter-independent and is not affected).
    // `intrinsic` participates for the same reason --only/--exclude do: the seam summaries are a function of
    // the FILTERED effects, and hiding alloc/throw by default changes them. Omitting it would let a default
    // run serve a sidecar built with --intrinsic (or vice versa) off an otherwise-identical key.
    internal static string EffectFilterSignature(
        IReadOnlyCollection<string> only,
        IReadOnlyCollection<string> exclude,
        bool intrinsic = false
    )
    {
        var o = string.Join(',', only.Select(x => x.ToLowerInvariant()).OrderBy(x => x, StringComparer.Ordinal));
        var e = string.Join(',', exclude.Select(x => x.ToLowerInvariant()).OrderBy(x => x, StringComparer.Ordinal));
        return $"only={o};exclude={e};intrinsic={(intrinsic ? "1" : "0")}";
    }

    // The render-sidecar cache keys (locations + seam) derived off a forest TreeCacheKey. Encapsulated as a
    // typed record so the seam key's full dependency set is explicit and impossible to omit (a missing
    // component here previously pinned `tree --view hazards` to a permanent render-miss). The seam summary is
    // a function of the FILTERED effects AND, under --view hazards, the whole-store hazard-augmented effect set
    // (which depends on the write-pairing gate) — so Hazards+Gate MUST namespace the key, else a hazards run
    // would either never cache (old behaviour) or taint a plain tree's seam (same forest+filter key).
    internal readonly record struct RenderSidecarKey(ForestCacheKey Forest, string FilterSignature, bool Hazards, bool Gate)
    {
        // Locations (DocID -> file:line) are filter- AND hazard-independent -> keyed off the forest key alone.
        public string Locations() => Forest.Value + ":loc";

        // Seam: namespaced by hazards (+gate, which only affects the hazard-augmented effects) so the hazards
        // seam and the plain-tree seam never share a slot. NON-hazards key is byte-identical to the legacy
        // `Forest.Value + ":seam:" + FilterSignature` (back-compat: existing plain-tree warm caches still hit,
        // and gate must NOT fragment the non-hazards key — a plain tree has no gate-dependent effects).
        public string Seam() => Forest.Value + ":seam:" + (Hazards ? $"haz:{(Gate ? "g" : "ng")}:" : "") + FilterSignature;
    }

    // Best-effort cache write: encoding a pathologically deep forest (or any IO hiccup) must never fail
    // the query — on error we simply don't cache and the next run recomputes. The single home for the
    // try/catch the tree forest, render sidecar, and EP-site writes all shared.
    internal static void TryCache(Action put)
    {
        try
        {
            put();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or IOException)
        {
            // skip caching this result
        }
    }
}
