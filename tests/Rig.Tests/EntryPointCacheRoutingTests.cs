using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.EntryPoints;
using Rig.Cli.Services;
using Rig.Storage.Queries;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests;

// `rig callers --entrypoints` was routed through the cached whole-store EP RECORD set on 2026-08-24
// (EntryPointContext.LoadOrDeriveEntryPointRecordsAsync, keyed by store identity + rule fingerprint +
// EpSchema) under a comment claiming "ONE code path and ONE key". Three surfaces were still taking the raw
// Reads.LoadFactEntryPointDataAsync + FactEntryPointDeriver.Derive path and paying the whole-store derivation
// per question (~4-5s measured on the 227-project MedDBase store, a cost that does not scale with the
// question): `rig entrypoints`, EntryPointService.ListAsync (/api/entrypoints) and
// CallersQueryService.BuildAsync's entry-point lens (/api/callers?mode=entrypoints).
//
// THESE TESTS PIN THE ROUTING, NOT THE LATENCY: that each of the three now consults the SAME artifact-cache
// entry (a fresh store has none; one call populates exactly the key a fresh process recomputes from the same
// two axes), that the entry holds the WHOLE-STORE set rather than one question's answer, and that the answers
// are unchanged — a cache may only change latency. No `*Schema` bump was involved: nothing about the payload
// or the derivation moved.
public sealed class EntryPointCacheRoutingTests
{
    // The nine entry points the EntryPointEffects playground yields under the default rule cascade, in the
    // (kind, route) ordinal order every one of these surfaces sorts by. Pasted from an actual run.
    private static readonly string[] ExpectedRoutes =
    [
        "EntryPointEffects.Api.FastEndpointsFixture.CreateTeamEndpoint.ExecuteAsync",
        "EntryPointEffects.Api.Controllers.NotificationsController.OnSaved",
        "EntryPointEffects.Api.Controllers.NotificationsController.RecordDirect",
        "EntryPointEffects.Api.Controllers.NotificationsController.Subscribe",
        "EntryPointEffects.Api.Controllers.TeamsController.Create",
        "EntryPointEffects.Api.Controllers.TeamsController.CreateViaInterface",
        "EntryPointEffects.Api.Controllers.TeamsController.Get",
        "EntryPointEffects.Api.Controllers.TeamsController.ListViaInterface",
        "EntryPointEffects.Api.Controllers.TeamsController.ListViaMethodGroup",
    ];

    // `rig entrypoints` — cold (nothing cached) vs warm (served from cache.db). Byte-for-byte in both
    // renderings, and the run has to have POPULATED the shared entry: comparing two runs alone would pass just
    // as happily if the first Put silently failed and both derived from the facts.
    [Test]
    [Arguments(false)] // the human listing
    [Arguments(true)] // the tsv lens (requires + the DocID-resolved fqn column)
    public async Task Rig_entrypoints_is_byte_identical_cold_and_warm(bool tsv)
    {
        string[] query = tsv ? ["entrypoints", "--format", "tsv"] : ["entrypoints"];
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        await IndexAsync(playground, workingDirectory);
        CachedRecords(workingDirectory).ShouldBeNull();

        var cold = await RunAsync(query, workingDirectory);
        var warm = await RunAsync(query, workingDirectory);

        cold.Exit.ShouldBe(0, cold.Err);
        cold.Out.ShouldNotBeEmpty();
        warm.Out.ShouldBe(cold.Out);
        warm.Exit.ShouldBe(cold.Exit);
        CachedRecords(workingDirectory).ShouldNotBeNull().Count.ShouldBe(ExpectedRoutes.Length);
    }

    // The listing's actual content, so the routing change is pinned against the rendered answer and not just
    // against itself. Pasted from a real run on the playground.
    [Test]
    public async Task Rig_entrypoints_lists_the_playgrounds_nine_entry_points()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        await IndexAsync(playground, workingDirectory);

        var result = await RunAsync(["entrypoints"], workingDirectory);

        result.Exit.ShouldBe(0, result.Err);
        result.Out.ShouldContain("Entry points: 9");
        result.Out.ShouldContain("http: 8");
        result.Out.ShouldContain("fastendpoint: 1");
        foreach (var route in ExpectedRoutes)
        {
            result.Out.ShouldContain(route);
        }
    }

    // /api/entrypoints. The whole listing is a pure function of (store + rules) — no question attached — so it
    // is the surface the uncached derivation hurt most (a measured ~5s per request on the MedDBase store).
    [Test]
    public async Task Entry_point_service_listing_populates_and_reuses_the_shared_entry()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        await IndexAsync(playground, workingDirectory);
        CachedRecords(workingDirectory).ShouldBeNull();

        var first = await EntryPointService.ListAsync(workingDirectory);
        var cached = CachedRecords(workingDirectory).ShouldNotBeNull();
        var second = await EntryPointService.ListAsync(workingDirectory);

        // The entry a FRESH process would look under — this is the "one key" claim, checked rather than
        // asserted in a comment.
        cached.Count.ShouldBe(ExpectedRoutes.Length);
        first.Select(v => v.Route).ShouldBe(ExpectedRoutes);
        first.Select(v => v.Kind).ShouldBe(["fastendpoint", "http", "http", "http", "http", "http", "http", "http", "http"]);
        // Fqn comes off the record's PRE-RESOLVED handler DocID; on this playground every EP site resolves to
        // an indexed method, so no row falls back to its route by accident.
        first.Select(v => v.Fqn).ShouldBe(ExpectedRoutes);
        first.ShouldAllBe(v => v.File != null && v.Line > 0);
        // A cache may only change latency.
        second.ShouldBe(first);
    }

    // /api/callers?mode=entrypoints. The cached entry must hold the WHOLE-STORE set, not this question's
    // one-row answer: caching the intersection would make the entry serve one pattern and silently
    // under-report every other — and "no entry point reaches this" de-risks a change wrongly.
    [Test]
    public async Task Callers_entry_point_lens_populates_the_shared_entry_with_the_whole_store_set()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        await IndexAsync(playground, workingDirectory);
        CachedRecords(workingDirectory).ShouldBeNull();

        var first = await BuildCallersAsync(workingDirectory);
        var cached = CachedRecords(workingDirectory).ShouldNotBeNull();
        var second = await BuildCallersAsync(workingDirectory);

        cached.Select(r => r.Route).ShouldBe(ExpectedRoutes, ignoreOrder: true);
        first.Matched.ShouldBeTrue();
        var hits = first.EntryPoints.ShouldNotBeNull();
        hits.Count.ShouldBe(1);
        hits[0].View.Kind.ShouldBe("http");
        hits[0].View.Route.ShouldBe("EntryPointEffects.Api.Controllers.TeamsController.Create");
        hits[0].View.Fqn.ShouldBe("EntryPointEffects.Api.Controllers.TeamsController.Create");
        hits[0].View.Line.ShouldBe(26);
        // No deployments.json on the playground, so the owning-service chip is empty rather than absent.
        hits[0].Services.ShouldBeEmpty();
        second.EntryPoints.ShouldNotBeNull().ShouldBe(hits);
    }

    // The CLI and the web listing must consult ONE entry, not two entries that happen to agree. The web call
    // populates it; the CLI then renders from it and reports the SAME rows, column for column.
    [Test]
    public async Task The_cli_listing_and_the_web_listing_share_one_entry()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        await IndexAsync(playground, workingDirectory);

        var web = await EntryPointService.ListAsync(workingDirectory);
        var written = CachedRecords(workingDirectory).ShouldNotBeNull();
        var cli = await RunAsync(["entrypoints", "--format", "tsv"], workingDirectory);

        cli.Exit.ShouldBe(0, cli.Err);
        // The CLI run neither missed nor rewrote a different set — the entry is the one the web call wrote.
        CachedRecords(workingDirectory).ShouldNotBeNull().ShouldBe(written);
        // tsv columns: kind, route, file, line, requires, loaded services, active services, fqn.
        var rows = cli
            .Out.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Select(c => (Kind: c[0], Route: c[1], File: c[2], Line: c[3], Fqn: c[7]))
            .ToList();
        rows.Select(r => r.Kind).ShouldBe(web.Select(v => v.Kind));
        rows.Select(r => r.Route).ShouldBe(web.Select(v => v.Route));
        rows.Select(r => r.File).ShouldBe(web.Select(v => v.File));
        rows.Select(r => r.Line).ShouldBe(web.Select(v => v.Line.ToString()));
        rows.Select(r => r.Fqn).ShouldBe(web.Select(v => v.Fqn));
    }

    // A rule edit is the second invalidation axis (the first, a reindex, is the store identity the key already
    // carries). The fingerprint must be REAL on the service surfaces too — a constant there would make an
    // `extraRules` request serve the default cascade's entry points, silently.
    [Test]
    public async Task A_rules_file_keys_its_own_entry_on_the_service_surface()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        await IndexAsync(playground, workingDirectory);
        var extraRules = Path.Combine(workingDirectory, "extra.rules.json");
        await File.WriteAllTextAsync(extraRules, "{ \"entryPoints\": { \"classInheritance\": [] } }");

        await EntryPointService.ListAsync(workingDirectory);
        await EntryPointService.ListAsync(workingDirectory, extraRules: [extraRules]);

        // Two distinct entries, one per rule fingerprint — neither aliasing the other. (This playground's EPs
        // come from rule sections the extra file does not zero, so the two SETS coincide; what is pinned here
        // is that the request keyed under its own fingerprint rather than reading the default one.)
        EpKey(workingDirectory).ShouldNotBe(EpKey(workingDirectory, extraRules));
        CachedRecords(workingDirectory).ShouldNotBeNull().Count.ShouldBe(ExpectedRoutes.Length);
        CachedRecords(workingDirectory, extraRules).ShouldNotBeNull().Count.ShouldBe(ExpectedRoutes.Length);
    }

    private static Task<CallersQueryService.CallersResult> BuildCallersAsync(string workingDirectory) =>
        CallersQueryService.BuildAsync(
            workingDirectory: workingDirectory,
            fromPattern: "CreateTeamAsync",
            storeRef: null,
            mode: CallersQueryService.CallersMode.EntryPoints,
            async: false
        );

    private static async Task IndexAsync(TempPlayground playground, string workingDirectory)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], output, error, workingDirectory)).ShouldBe(0, error.ToString());
    }

    private static async Task<(int Exit, string Out, string Err)> RunAsync(string[] arguments, string workingDirectory)
    {
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var exit = await CliApplication.RunAsync(arguments, output, error, workingDirectory);
        return (exit, output.ToString(), error.ToString());
    }

    // The key a fresh process derives for the EP record set, recomputed here from the same two axes.
    private static string EpKey(string workingDirectory, string? extraRules = null)
    {
        var rigDirectory = StoreLayout.ResolveStoreDir(workingDirectory);
        var storeKey = QueryCacheKeys.StoreKey(Path.Combine(rigDirectory, StoreLayout.DbFileName));
        return QueryCacheKeys.EpRecordsCacheKey(
            storeKey,
            RulesFingerprint.Compute(workingDirectory, extraRules is null ? null : [extraRules])
        );
    }

    // The cached EP records for this working directory, or null when nothing was cached under that key.
    private static IReadOnlyList<EntryPointContext.EntryPointRecord>? CachedRecords(string workingDirectory, string? extraRules = null)
    {
        var rigDirectory = StoreLayout.ResolveStoreDir(workingDirectory);
        var storeKey = QueryCacheKeys.StoreKey(Path.Combine(rigDirectory, StoreLayout.DbFileName));
        using var cache = QueryCache.Open(rigDirectory: rigDirectory, storeKey: storeKey);
        var blob = cache?.Get(EpKey(workingDirectory, extraRules));
        return blob is null ? null : EntryPointRecordCodec.Decode(blob);
    }
}
