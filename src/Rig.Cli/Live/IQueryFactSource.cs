using Rig.Cli.Deployments;
using Rig.Cli.Rendering;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;

namespace Rig.Cli.Live;

// The SEAM between a query command and where its facts come from. `rig reaches` used to be written directly
// against a RigDbContext; it is now written against this, so the SAME command code answers off a saved .rig
// store (StoreQueryFactSource) or off the resident in-memory facts `rig watch` keeps current
// (LiveQueryFactSource) — one renderer, one traversal, one effect derivation, two fact sources.
//
// It is deliberately MINIMAL: exactly what `reaches`/`path`/`callers` need from a context beyond the traversal
// itself, and nothing speculative. It is not a RigDbContext abstraction and must not grow into one — every
// member here is a capability a live source can honestly implement, which is the whole test of whether it
// belongs. (`tree`/`derive` still take the context directly; migrating them is later work.)
//
// THE MEMBERSHIP TEST, stated because it is the only thing keeping this from becoming a `Reads` mirror: a
// member belongs here iff BOTH paths can answer it HONESTLY. Three store capabilities `path`/`callers` touch
// deliberately did NOT become members, because the live path has no store to answer them from:
//
//   * the FTS symbol search (`symbol_fts`, built by `rig graph`),
//   * the materialized `entry_point_sites` table,
//   * the `.rig/cache.db` query cache.
//
// The two that a live query still NEEDS are expressed as capabilities whose live arm is a real in-memory
// computation over the same facts (SymbolExistsAnywhereAsync / ReportNoNodeMatchAsync mirror the LIKE arm of
// the search; the EP-site map is derived per fact generation) — never a member whose live body throws. The
// third (the query cache) is simply absent on the live path: per-generation memoization replaces it.
internal interface IQueryFactSource : IAsyncDisposable
{
    // The graph + bounded effect-derivation inputs (invocations / ctor refs / throw refs / allocations / EP
    // data) for `pattern`. `shapedRules` is the command's already-`--raw`-gated RuleSet; the store path shapes
    // with it, and the live path REQUIRES it to match the rules its facts were extracted under (see
    // LiveQueryFactSource). Store: bounded to the pattern's SQL closure when `rig graph` has run. Live: the
    // whole in-memory fact set, since there is no SQL to bound with.
    Task<SqlReachability.ReachInputs> LoadEffectReachInputsAsync(string pattern, SqlReachability.Direction direction, RuleSet shapedRules);

    // The GRAPH-ONLY shaped traversal load — what `path` and `callers` need (they derive no effects, so the
    // bounded invocation/throw/ctor inputs above would be pure waste). Same direction parameter the store
    // loader already took: `path` walks Forward, `callers` walks Reverse. Store: the bounded subgraph for the
    // pattern's closure in that direction when `rig graph` has run, else the full EF graph. Live: the whole
    // in-memory graph, which is a SUPERSET of either closure — the traversal narrows it identically, and the
    // one place that superset is VISIBLE is `path`'s "Fact graph: N call edges …" banner (see PathCommand).
    Task<FactGraphData> LoadShapedTraversalGraphAsync(string pattern, SqlReachability.Direction direction, RuleSet shapedRules);

    // Call sites containing an EVENT read (`someEvent += H`), for FactPathFinder.MarkEventSubscriptionHandoffs.
    Task<ISet<EventSubscriptionSite>> EventSubscriptionSitesAsync();

    // The effect set for these inputs. Factored through the source ONLY so a resident host can memoize it per
    // fact generation (it is the most expensive derived artifact, and every query in a generation wants the
    // same one). Both implementations run the identical EffectDerivation.DeriveEffects call.
    Task<IReadOnlyList<DerivedEffect>> DeriveEffectsAsync(SqlReachability.ReachInputs inputs, FactGraphData graph, RuleSet rules);

    // deployments.json, resolved against the indexed solution. Empty (a no-op) when unconfigured.
    Task<DeploymentMap> LoadDeploymentsAsync(string workingDirectory);

    // The per-tree entry-point render context (the "▶ kind ⟦svc⟧" chip source). Null when deployments are
    // unconfigured, so the default query pays nothing.
    Task<EpRenderContext?> BuildEpContextAsync(
        FactGraphData graph,
        string workingDirectory,
        IReadOnlyList<string> extraRules,
        RuleSet rules,
        DeploymentMap deployments,
        FactEntryPointDeriver.FactEntryPointData? epData
    );

    // Seed disclosure for a pattern that matched no call-graph NODE: "nothing by that name" vs "a real `P:`/
    // `F:`/`E:` symbol that can never be a node". Only ever called on the already-failed path.
    Task ReportNoNodeMatchAsync(TextWriter output, string pattern);

    // Does the pattern name ANY indexed symbol, anywhere in the fact set? `path`'s TO-endpoint probe: the graph
    // `path` walks is the FROM node's forward slice, so a `to` that exists but is simply UNREACHABLE is absent
    // from it, and deciding "no symbol matches" off the graph alone would libel a real symbol. Consulted ONLY
    // on the already-failed path (no path found), so neither implementation costs anything in the normal case.
    Task<bool> SymbolExistsAnywhereAsync(string pattern);

    // The entry-point FACT bundle (base/interface edges, method + type symbols, ctor refs) `callers
    // --entrypoints` derives from. Store: Reads.LoadFactEntryPointDataAsync. Live: the per-generation memo.
    Task<FactEntryPointDeriver.FactEntryPointData> LoadEntryPointDataAsync();

    // The rule-detected entry-point set + the classified async-handoff origins promoted alongside it — the
    // same set `rig derive` emits. Split from BuildEpContextAsync (which flattens it to a site->kind map)
    // because `callers --entrypoints` needs the DerivedEntryPoint records themselves: their route, requires
    // and declaration site all render.
    Task<(
        IReadOnlyList<DerivedEntryPoint> Derived,
        IReadOnlyList<HandoffEntryPoint> ClassifiedHandoffs,
        IReadOnlyList<DerivedEntryPoint> PromotedOrigins
    )> DeriveEntryPointsAsync(FactEntryPointDeriver.FactEntryPointData epData, RuleSet rules);
}

// The ONE effect-derivation call both fact sources make, so a store-served and a live-served answer cannot
// derive effects differently. Lifted verbatim out of ReachesCommand when the seam was introduced — same
// positional arguments, same omitted optionals (no static-field feeds, no hazard post-pass: those are
// `derive`'s whole-store arms, not a bounded traversal's).
internal static class QueryEffectDerivation
{
    public static IReadOnlyList<DerivedEffect> ForReach(RuleSet rules, SqlReachability.ReachInputs inputs, FactGraphData graph) =>
        Effects.EffectDerivation.DeriveEffects(
            rules.Effects,
            rules.Observations,
            inputs.Invocations,
            Graph.TraversalGraphLoader.BaseEdgeTuples(graph),
            ctorRefs: inputs.CtorRefs,
            throwRefs: inputs.ThrowRefs,
            allocationFacts: inputs.AllocationFacts
        );
}
