using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
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
internal sealed class StoreQueryFactSource(RigDbContext context, WorkspaceLocation workspaceLocation, bool ownsContext = true)
    : IQueryFactSource
{
    // Resolved once, lazily: the same two values TreeCommand used to compute inline (`ResolveReadStoreDir` +
    // `StoreKey` off rig.db's size/mtime). Lazy because the uncached commands (reaches/path/callers) never ask
    // for them, so they must not start paying a directory resolve + a file stat for the seam's sake.
    private string? _rigDirectory;
    private string? _storeKey;

    // Opens the store READ-ONLY through the single schema-gate chokepoint, exactly as the command did.
    // Deferred behind a factory at the call site so the gate still fires at the same point in the command
    // (after the rules load and the unknown-filter-token warning), not earlier.
    public static async Task<IQueryFactSource> OpenAsync(WorkspaceLocation workspaceLocation) =>
        new StoreQueryFactSource(await TraversalGraphLoader.OpenReadContextGatedAsync(workspaceLocation), workspaceLocation);

    // A source over a context the CALLER owns and will dispose. For the one store-only consumer that needs the
    // seam (to reach the shared tree compute) AND the raw context beside it — HazardsService, whose tier-3
    // amplification arm reads through Caching.WarmStore. DisposeAsync is a no-op here on purpose: disposing a
    // borrowed context would close the file out from under its owner, which is exactly the bug the
    // "it OWNS the context it opened" rule above exists to prevent. Opening a SECOND context for the same store
    // would work too, and is strictly worse (two connections, two schema gates, for one query).
    public static IQueryFactSource Borrowing(RigDbContext context, WorkspaceLocation workspaceLocation) =>
        new StoreQueryFactSource(context, workspaceLocation, ownsContext: false);

    private string RigDirectory => _rigDirectory ??= StoreLayout.ResolveReadStoreDir(workspaceLocation);

    private string StoreIdentity => _storeKey ??= QueryCacheKeys.StoreKey(Path.Combine(RigDirectory, StoreLayout.DbFileName));

    public Task<SqlReachability.ReachInputs> LoadEffectReachInputsAsync(
        string pattern,
        SqlReachability.Direction direction,
        RuleSet shapedRules
    ) => TraversalGraphLoader.LoadEffectReachInputsAsync(context, pattern, direction, shapedRules);

    public Task<FactGraphData> LoadShapedTraversalGraphAsync(string pattern, SqlReachability.Direction direction, RuleSet shapedRules) =>
        TraversalGraphLoader.LoadShapedTraversalGraphAsync(context, pattern, direction, shapedRules);

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

    public Task<bool> SymbolExistsAnywhereAsync(string pattern) => SeedResolutionNotice.ExistsInStoreAsync(context, pattern);

    public Task<FactEntryPointDeriver.FactEntryPointData> LoadEntryPointDataAsync() => Reads.LoadFactEntryPointDataAsync(context);

    public Task<(
        IReadOnlyList<DerivedEntryPoint> Derived,
        IReadOnlyList<HandoffEntryPoint> ClassifiedHandoffs,
        IReadOnlyList<DerivedEntryPoint> PromotedOrigins
    )> DeriveEntryPointsAsync(FactEntryPointDeriver.FactEntryPointData epData, RuleSet rules) =>
        EntryPoints.EntryPointContext.DeriveEntryPointsAsync(context, epData, rules);

    // The `.rig/cache.db` arm — the same QueryCache.Open, against the same rigDirectory/storeKey the command
    // computed inline before the seam existed. `--store <ref>` still resolves to that ref's store dir, so a
    // commit-scoped query caches against the right commit.
    public IQueryArtifactCache OpenArtifactCache(bool useCache) =>
        new StoreQueryArtifactCache(rigDirectory: RigDirectory, storeKey: StoreIdentity, useCache: useCache);

    public Task<IReadOnlyList<DerivedEffect>> HazardEffectsAsync(string rulesHash, RuleSet rules, bool useCache, bool gate) =>
        Effects.EffectDerivation.LoadOrDeriveHazardEffectsAsync(
            context: context,
            rigDirectory: RigDirectory,
            storeKey: StoreIdentity,
            rulesHash: rulesHash,
            rules: rules,
            useCache: useCache,
            gate: gate
        );

    public Task<IReadOnlyList<DeriveCommand.HazardFinding>> GraphHazardFindingsAsync(string rulesHash, RuleSet rules, bool useCache) =>
        Effects.EffectDerivation.LoadOrDeriveGraphHazardFindingsAsync(
            context: context,
            rigDirectory: RigDirectory,
            storeKey: StoreIdentity,
            rulesHash: rulesHash,
            rules: rules,
            useCache: useCache
        );

    public Task<IReadOnlyDictionary<(string File, int Line), (string Kind, IReadOnlyList<string>? Requires)>> EpSiteKindAsync(
        string workingDirectory,
        IReadOnlyList<string> extraRules,
        RuleSet rules,
        bool useCache,
        FactEntryPointDeriver.FactEntryPointData? epData
    ) => EntryPoints.EntryPointContext.LoadOrDeriveEpSiteKindAsync(context, workingDirectory, extraRules, rules, useCache, epData);

    public Task<IReadOnlyList<SymbolRef>> LibraryCallSitesAsync(IReadOnlyCollection<string> enclosingIds) =>
        Reads.LoadLibraryCallSitesAsync(context, enclosingIds);

    public ValueTask DisposeAsync() => ownsContext ? context.DisposeAsync() : ValueTask.CompletedTask;
}
