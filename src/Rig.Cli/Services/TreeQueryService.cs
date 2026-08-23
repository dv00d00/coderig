using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Live;
using Rig.Cli.Rendering;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using static Rig.Cli.Effects.EffectDerivation;

namespace Rig.Cli.Services;

// The reusable call-TREE computation, lifted out of TreeCommand.RunAsync so BOTH the CLI and the in-process
// web host (Web/) run the SAME engine — no shelling out, no re-parsing text. Produces the three things a
// consumer needs to render a tree: the TraceNode forest, the effects keyed to enclosing method, and a
// symbol-id -> file:line map. This is the COLD compute (rules load -> graph load -> BuildTree -> derive
// effects); the CLI's query-cache is a presentation-layer optimization that stays in TreeCommand for now.
//
// Deliberately public + primitives-in (workingDirectory/storeRef, not the internal WorkspaceLocation) so the
// contract survives a later lift to a standalone Rig.Web project. Return types are public Rig.Domain records.
public static class TreeQueryService
{
    public sealed record SymbolLocation(string? File, int Line);

    public sealed record TreeQueryResult(
        IReadOnlyList<TraceNode> Roots,
        IReadOnlyList<DerivedEffect> Effects,
        IReadOnlyDictionary<string, SymbolLocation> Locations,
        // The repo's provider:operation -> glyph map (from rig.effect-emoji.json / builtins), carried through
        // so a renderer shows the SAME glyphs as `rig tree` without re-loading the rule set.
        IReadOnlyDictionary<string, string> EffectEmoji,
        // Opaque/collapse render rules so the web mapper folds seams the same way the pretty/llm renderers do.
        // Empty under raw=true (the endpoint's ?raw= opt-out) — the tree is then served fully unfolded.
        FactRenderRules Render,
        bool IntrinsicHidden
    );

    // The richer result of the shared cold compute (ComputeAsync): the forest + effects PLUS the graph and
    // entry-point data the CLI needs downstream. Internal — the web only consumes the public TreeQueryResult
    // projection; TreeCommand consumes this directly to keep its existing render pipeline unchanged.
    internal sealed record TreeComputation(
        IReadOnlyList<TraceNode> Roots,
        IReadOnlyList<DerivedEffect> Effects,
        FactGraphData Graph,
        FactEntryPointDeriver.FactEntryPointData? EpData
    );

    // Build the forest + effects for `fromPattern` over the store at `workingDirectory` (optionally a specific
    // `storeRef` commit/id). Mirrors the cold path of TreeCommand.RunAsync: same shaping rules, same event-
    // subscription handoff marking, same BuildTree + monomorph collapse, same DeriveEffects inputs — so the
    // web renders exactly what `rig tree` would (minus the CLI-only render chrome).
    public static async Task<TreeQueryResult> BuildAsync(
        string workingDirectory,
        string fromPattern,
        string? storeRef = null,
        int? depth = null,
        bool async = false,
        bool includeDelivery = false,
        bool raw = false,
        bool intrinsic = false,
        IReadOnlyList<string>? extraRules = null
    )
    {
        var rules = RuleSetLoader.Load(workingDirectory: workingDirectory, extraRules: extraRules ?? [], loadedPaths: out _);
        // --raw parity: zero the graph-shaping rules so the tree is the exact unfiltered structure.
        var shaped = raw ? rules with { Factory = [], Cut = [], Context = [] } : rules;

        // The web host is a STORE consumer: same schema-gated read-only open as before, now expressed as the
        // store arm of the fact-source seam so the shared compute below has exactly one implementation.
        await using var source = await StoreQueryFactSource.OpenAsync(
            new WorkspaceLocation(WorkingDirectory: workingDirectory, StoreRef: storeRef)
        );

        var computation = await ComputeAsync(
            source: source,
            rules: rules,
            shaped: shaped,
            fromPattern: fromPattern,
            maxDepth: CommonOptions.DepthOrUnbounded(depth),
            maxNodes: FactPathFinder.DefaultTreeNodeBudget,
            mode: CommonOptions.Mode(async: async, includeDelivery: includeDelivery),
            raw: raw
        );

        var locations = computation
            .Graph.Methods.GroupBy(m => m.SymbolId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new SymbolLocation(g.First().FilePath, g.First().Line), StringComparer.Ordinal);

        var treeMethods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in computation.Roots)
        {
            TreeRenderer.CollectTreeMethods(root, treeMethods);
        }
        var selection = SelectEffectsForMethods(
            computation.Effects,
            treeMethods,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            intrinsic
        );

        // raw parity: no fold rules either, so the web serves the exact unfolded tree (mirrors CLI --raw).
        var renderRules = raw ? FactRenderRules.Empty : rules.Render;
        return new TreeQueryResult(
            computation.Roots,
            selection.Effects,
            locations,
            rules.EffectEmoji,
            renderRules,
            selection.HiddenIntrinsic > 0
        );
    }

    // The COLD tree computation shared by the web host (BuildAsync, above) and `rig tree`'s cold path
    // (TreeCommand). It operates on an ALREADY-OPEN fact SOURCE + ALREADY-LOADED/SHAPED rules, so a caller that
    // holds those — TreeCommand does, plus its query-cache — reuses them instead of re-opening/re-loading.
    // Returns the graph + entry-point data too (not just roots/effects): the CLI threads those into its
    // downstream render stages (locations, seam, EP-site chips, --full library calls). This is the single
    // source of truth for the forest + effects, so `rig tree` and `/api/tree` cannot diverge.
    //
    // Parameterized on IQueryFactSource rather than RigDbContext (2026-08-21) so the SAME cold compute also
    // serves the resident live index — the forest a `rig watch` query answers with is this function's output,
    // not a parallel in-memory reimplementation of it.
    internal static async Task<TreeComputation> ComputeAsync(
        IQueryFactSource source,
        RuleSet rules,
        RuleSet shaped,
        string fromPattern,
        int maxDepth,
        int maxNodes,
        FactPathFinder.TraversalMode mode,
        bool raw
    )
    {
        DemandForwardReachInputs? demandInputs = null;
        SqlReachability.ReachInputs inputs;
        FactGraphData graph;
        if (mode != FactPathFinder.TraversalMode.SyncCut && source is IDemandForwardPathFactSource demand)
        {
            demandInputs = await demand.LoadDemandForwardReachInputsAsync(
                fromPattern,
                shaped,
                maxDepth,
                mode,
                classifyEventSubscriptions: !raw
            );
            inputs = demandInputs.Inputs;
            graph = demandInputs.Demand.Graph;
        }
        else
        {
            inputs = await source.LoadEffectReachInputsAsync(fromPattern, SqlReachability.Direction.Forward, shaped);
            graph = inputs.Graph;
        }
        // Event subscriptions (`someEvent += Handler`) are deferred handlers, not synchronous calls — mark them
        // as handoffs so the sync tree doesn't expand the handler as if the registrar ran it. Skipped under --raw.
        if (!raw && demandInputs?.Demand.EventSubscriptionsClassified != true)
        {
            graph = FactPathFinder.MarkEventSubscriptionHandoffs(graph, await source.EventSubscriptionSitesAsync());
        }

        var roots = MonomorphCollapse.CollapseTree(FactPathFinder.BuildTree(graph, fromPattern, maxDepth, maxNodes: maxNodes, mode: mode));

        // The one shared effect derivation (QueryEffectDerivation.ForReach, through the source so a resident
        // host can serve its per-generation memo) — same arguments as before the seam, and still skipped
        // entirely when the pattern matched nothing.
        IReadOnlyList<DerivedEffect> effects =
            roots.Count == 0 ? []
            : demandInputs is null ? await source.DeriveEffectsAsync(inputs, graph, rules)
            : QueryEffectDerivation.ForReach(rules, inputs, graph);

        return new TreeComputation(roots, effects, graph, inputs.EpData);
    }
}
