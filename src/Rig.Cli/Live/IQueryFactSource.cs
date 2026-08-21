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
// It is deliberately MINIMAL: exactly the six things ReachesCommand needs from a context beyond the traversal
// itself, and nothing speculative. It is not a RigDbContext abstraction and must not grow into one — every
// member here is a capability a live source can honestly implement, which is the whole test of whether it
// belongs. (`tree`/`callers`/`path`/`derive` still take the context directly; migrating them is later work.)
internal interface IQueryFactSource : IAsyncDisposable
{
    // The graph + bounded effect-derivation inputs (invocations / ctor refs / throw refs / allocations / EP
    // data) for `pattern`. `shapedRules` is the command's already-`--raw`-gated RuleSet; the store path shapes
    // with it, and the live path REQUIRES it to match the rules its facts were extracted under (see
    // LiveQueryFactSource). Store: bounded to the pattern's SQL closure when `rig graph` has run. Live: the
    // whole in-memory fact set, since there is no SQL to bound with.
    Task<SqlReachability.ReachInputs> LoadEffectReachInputsAsync(string pattern, SqlReachability.Direction direction, RuleSet shapedRules);

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
