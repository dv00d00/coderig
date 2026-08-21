using Rig.Cli.CommandLine;
using Rig.Cli.Deployments;
using Rig.Cli.Graph;
using Rig.Cli.Rendering;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;

namespace Rig.Cli.Live;

// IQueryFactSource over a saved .rig store — the path every `rig reaches` invocation has always taken.
//
// This type adds NO behaviour. Every member is a straight delegation to the exact call ReachesCommand made
// inline before the seam existed (schema-gated open, TraversalGraphLoader.LoadEffectReachInputsAsync,
// Reads.EventSubscriptionSitesAsync, EffectDerivation.DeriveEffects, EntryPointContext.LoadDeploymentsAsync /
// BuildEpContextAsync, SeedResolutionNotice.ReportNoNodeMatchAsync) with the same arguments and the same
// defaults — including BuildEpContextAsync's `useCache: true`. That is deliberate: the store path's output and
// its cache behaviour must be bit-identical after the refactor, so the diff of this file against the old
// command body should read as a pure move.
//
// It OWNS the context it opened: DisposeAsync closes it, so the command's `await using` keeps the same
// lifetime the old `await using var context` had.
internal sealed class StoreQueryFactSource(RigDbContext context) : IQueryFactSource
{
    // Opens the store READ-ONLY through the single schema-gate chokepoint, exactly as the command did.
    // Deferred behind a factory at the call site so the gate still fires at the same point in the command
    // (after the rules load and the unknown-filter-token warning), not earlier.
    public static async Task<IQueryFactSource> OpenAsync(WorkspaceLocation workspaceLocation) =>
        new StoreQueryFactSource(await TraversalGraphLoader.OpenReadContextGatedAsync(workspaceLocation));

    public Task<SqlReachability.ReachInputs> LoadEffectReachInputsAsync(
        string pattern,
        SqlReachability.Direction direction,
        RuleSet shapedRules
    ) => TraversalGraphLoader.LoadEffectReachInputsAsync(context, pattern, direction, shapedRules);

    public Task<ISet<EventSubscriptionSite>> EventSubscriptionSitesAsync() => Reads.EventSubscriptionSitesAsync(context);

    public Task<IReadOnlyList<DerivedEffect>> DeriveEffectsAsync(SqlReachability.ReachInputs inputs, FactGraphData graph, RuleSet rules) =>
        Task.FromResult(QueryEffectDerivation.ForReach(rules, inputs, graph));

    public Task<DeploymentMap> LoadDeploymentsAsync(string workingDirectory) =>
        EntryPoints.EntryPointContext.LoadDeploymentsAsync(context, workingDirectory);

    public Task<EpRenderContext?> BuildEpContextAsync(
        FactGraphData graph,
        string workingDirectory,
        IReadOnlyList<string> extraRules,
        RuleSet rules,
        DeploymentMap deployments,
        FactEntryPointDeriver.FactEntryPointData? epData
    ) =>
        EntryPoints.EntryPointContext.BuildEpContextAsync(
            context: context,
            graph: graph,
            workingDirectory: workingDirectory,
            extraRules: extraRules,
            rules: rules,
            deployments: deployments,
            epData: epData
        );

    public Task ReportNoNodeMatchAsync(TextWriter output, string pattern) =>
        SeedResolutionNotice.ReportNoNodeMatchAsync(output, context, pattern);

    public ValueTask DisposeAsync() => context.DisposeAsync();
}
