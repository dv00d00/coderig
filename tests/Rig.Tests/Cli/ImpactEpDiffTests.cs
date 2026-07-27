using Rig.Analysis.Rules;
using Rig.Cli.Commands;
using Rig.Cli.EntryPoints;
using Rig.Cli.Impact;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// The two-store entry-point diff behind `impact --base` (docs/design-impact-behavioral-diff.md §3.1-3.2):
// derive EPs on the branch and base stores, set-diff on (Kind, Route). Identical content => empty diff
// (the formatting/no-op-immunity guarantee); genuinely different sources => the symmetric difference shows.
[ClassDataSource<AnalyzedPlaygrounds>(Shared = SharedType.PerTestSession)]
public sealed class ImpactEpDiffTests(AnalyzedPlaygrounds playgrounds)
{
    [Test]
    public async Task Identical_stores_produce_an_empty_entry_point_diff()
    {
        var pg = await playgrounds.EntryPointEffectsAsync();
        var (branchDb, baseDb, wd) = await TwoStoresAsync(pg.Result, pg.Result);

        var diff = await DiffAsync(branchDb, baseDb, wd);

        diff.Added.ShouldBeEmpty();
        diff.Removed.ShouldBeEmpty();
    }

    [Test]
    public async Task Different_sources_surface_added_and_removed_entry_points()
    {
        var branchPg = await playgrounds.EntryPointEffectsAsync();
        var basePg = await playgrounds.LegacyNet48Async();
        var (branchDb, baseDb, wd) = await TwoStoresAsync(branchPg.Result, basePg.Result);

        var diff = await DiffAsync(branchDb, baseDb, wd);

        // Each solution has entry points the other lacks → the symmetric difference is non-empty both ways.
        diff.Added.ShouldNotBeEmpty();
        diff.Removed.ShouldNotBeEmpty();
    }

    private static async Task<EpDiff> DiffAsync(string branchDb, string baseDb, string wd)
    {
        var rules = RuleSetLoader.Load(wd);
        // Derive BOTH sides here and diff the pure sets. This mirrors what ImpactEngine now does: the base EP
        // set comes from the base side's single load, rather than a second base-store open inside the diff
        // helper (which was a full duplicate EP read on every impact run — see impact-base-store-double-load).
        var branchEps = await DeriveEpsAsync(branchDb, rules);
        var baseEps = await DeriveEpsAsync(baseDb, rules);
        return ImpactEngine.DiffEntryPointSets(branchEps, baseEps);
    }

    private static async Task<IReadOnlyList<DerivedEntryPoint>> DeriveEpsAsync(string db, RuleSet rules)
    {
        await using var ctx = new RigDbContext(db, pooling: false, readOnly: true);
        var epData = await Reads.LoadFactEntryPointDataAsync(ctx);
        var set = await EntryPointContext.DeriveEntryPointsAsync(ctx, epData, rules);
        return set.Derived.Concat(set.PromotedOrigins).ToList();
    }

    private static async Task<(string BranchDb, string BaseDb, string Wd)> TwoStoresAsync(AnalysisResult branch, AnalysisResult @base)
    {
        var wd = Path.Combine(Path.GetTempPath(), $"rig-epdiff-{Guid.NewGuid():n}");
        Directory.CreateDirectory(wd);
        return (await MaterializeAsync(branch, wd, "branch"), await MaterializeAsync(@base, wd, "base"), wd);
    }

    private static async Task<string> MaterializeAsync(AnalysisResult result, string wd, string name)
    {
        var db = Path.Combine(wd, $"{name}.db");
        await using var ctx = new RigDbContext(db, pooling: false);
        await Writes.SaveAsync(ctx, result);
        return db;
    }
}
