using System.Security.Cryptography;
using System.Text;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Cli;

// /api/tree now answers off the SAME forest cache `rig tree` uses (TreeQueryService.BuildAsync). Adding a
// cache lookup changed no derivation and no payload, so TreeSchema did NOT move — which means the ONLY thing
// standing between the web path and a silently wrong tree is that every endpoint parameter that changes the
// forest is already an axis of TreeCacheKey. These tests state that mapping parameter by parameter.
//
// The endpoint signature is (from, depth, async, store, raw, intrinsic). Mapping:
//   from      -> fromPattern          intrinsic -> NOT an axis: a post-cache effect FILTER (see below)
//   depth     -> maxDepth             store     -> the store identity axis (and which cache.db is opened)
//   async     -> mode                 raw       -> raw
// plus maxNodes (the constant node budget BuildAsync passes) and the rule fingerprint.
public sealed class WebTreeCacheKeyTests
{
    private const string Pattern = "App.Svc.Handle";

    private static QueryCacheKeys.ForestCacheKey Key(
        string storeKey = "store",
        string rulesHash = "rules",
        string fromPattern = Pattern,
        int? depth = null,
        int maxNodes = FactPathFinder.DefaultTreeNodeBudget,
        bool async = false,
        bool includeDelivery = false,
        bool raw = false
    ) =>
        QueryCacheKeys.TreeCacheKey(
            storeKey: storeKey,
            rulesHash: rulesHash,
            fromPattern: fromPattern,
            maxDepth: CommonOptions.DepthOrUnbounded(depth),
            maxNodes: maxNodes,
            mode: CommonOptions.Mode(async: async, includeDelivery: includeDelivery),
            raw: raw
        );

    [Test]
    public void The_forest_key_material_is_pinned_and_its_schema_did_not_move_for_the_web_lookup()
    {
        // Pinned on purpose: a schema bump must be a deliberate edit here, not a silent side effect. It stayed
        // at 8 through this change because a cache LOOKUP is not a new answer.
        QueryCacheKeys.TreeSchema.ShouldBe(8);
        Key()
            .Value.ShouldBe(
                Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes($"tree|v8|store|rules|{Pattern}|{int.MaxValue}|50000|SyncCut|False"))
                )
            );
        Key().Value.ShouldBe("E4911215D66B47322A93F8E19BFD17D0FCC37A55A807DE1B86CBC7DC9B520F83");
    }

    [Test]
    public void Identical_web_tree_inputs_share_one_slot()
    {
        // The HIT. Two identical /api/tree requests must land on one forest, which is the whole 22s -> 0.5s.
        Key().Value.ShouldBe(Key().Value);
    }

    [Test]
    public void Every_web_tree_input_that_changes_the_forest_is_an_axis_of_the_key()
    {
        var baseline = Key().Value;

        // ?store= / a reindex: the store identity.
        Key(storeKey: "store-2").Value.ShouldNotBe(baseline);
        // A rig.rules.json edit (or ?rules-equivalent extraRules): the rule fingerprint.
        Key(rulesHash: "rules-2").Value.ShouldNotBe(baseline);
        // ?from=
        Key(fromPattern: "App.Svc.Other").Value.ShouldNotBe(baseline);
        // ?depth=
        Key(depth: 3).Value.ShouldNotBe(baseline);
        // The node budget: a budget-capped forest is a DIFFERENT tree, not a different rendering of one.
        Key(maxNodes: 1).Value.ShouldNotBe(baseline);
        // ?async= — folded in through `mode`, NOT as a separate field. This is the assertion that proves the
        // endpoint's async flag cannot be served an async=false forest.
        Key(async: true).Value.ShouldNotBe(baseline);
        // …and the delivery arm of the same axis, so the three traversal modes are three slots.
        Key(async: true, includeDelivery: true).Value.ShouldNotBe(Key(async: true).Value);
        // ?raw=
        Key(raw: true).Value.ShouldNotBe(baseline);
    }

    // WHY `intrinsic` is deliberately NOT a forest axis: it selects which effects are DISPLAYED, after the
    // cache, exactly as the CLI's --only/--exclude do. The cached payload is the UNFILTERED forest + effects,
    // so a toggle of the browser's intrinsic switch reuses the >1 MB artifact instead of recomputing it — and
    // it still returns a different response, because the filter runs on the way out. Folding it into the key
    // would make the toggle a second 22s query for the same tree.
    [Test]
    public void The_intrinsic_view_flag_keys_the_effect_filter_but_not_the_forest()
    {
        var hidden = QueryCacheKeys.EffectFilterSignature(only: [], exclude: [], intrinsic: false);
        var shown = QueryCacheKeys.EffectFilterSignature(only: [], exclude: [], intrinsic: true);
        // It IS part of the filter vocabulary — a filter-dependent slot separates on it.
        shown.ShouldNotBe(hidden);

        // …and the LOCATIONS sidecar, the only sidecar half the web path reads/writes, is filter-independent:
        // both intrinsic views share one location blob off one forest key. If this ever separated, the web
        // would reload and re-shape the graph on every intrinsic toggle.
        var forest = Key();
        new QueryCacheKeys.RenderSidecarKey(forest, shown, Hazards: false, Gate: false)
            .Locations()
            .ShouldBe(new QueryCacheKeys.RenderSidecarKey(forest, hidden, Hazards: false, Gate: false).Locations());

        // The location slot hangs off the FOREST key, so a different tree can never be handed this tree's
        // locations — the property that makes sharing the slot with `rig tree` safe.
        new QueryCacheKeys.RenderSidecarKey(Key(depth: 3), hidden, Hazards: false, Gate: false)
            .Locations()
            .ShouldNotBe(new QueryCacheKeys.RenderSidecarKey(forest, hidden, Hazards: false, Gate: false).Locations());
    }
}
