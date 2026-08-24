using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.EntryPoints;
using Rig.Storage.Queries;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// `rig callers <x> --entrypoints` used to derive the WHOLE SOLUTION's entry-point set on every invocation —
// load every method/type/base-edge/ctor-ref fact, run the deriver, classify every handoff — no matter how
// small the query's closure was. Measured on the 227-project MedDBase store with `--time`, that phase was the
// single largest addressable cost in the hottest question rig answers:
//
//   phase              wall      %      diskR          phase              wall      %      diskR
//   graph load         4.2s  45.0%      2.0GB          graph load         4.2s  74.4%      2.1GB
//   deployments        0.1s   1.2%      0MB            deployments        0.1s   1.9%      n/a
//   reverse closure    0.5s   5.7%      0MB     -->    reverse closure    0.5s   9.2%      0MB
//   entry points       3.6s  39.5%      1.5GB          entry points       0.1s   1.2%      0MB
//   forward verify     0.3s   3.2%      0MB            forward verify     0.3s   5.8%      0MB
//   async probe        0.5s   5.4%      0MB            async probe        0.4s   7.3%      0MB
//   total              9.2s 100.0%                     total              5.6s 100.0%
//
// The set is a pure function of (store identity + effective rules) — no pattern, no depth, no traversal mode —
// so it is now memoized through the source's artifact cache, the same seam `tree` uses: `.rig/cache.db` on the
// store path, the fact generation's memo on the live one.
//
// THE THING THESE TESTS EXIST TO PIN: a cache may only change LATENCY. Every assertion below is about the
// answer being byte-identical warm, cold and bypassed — plus the round-trip fidelity of the two fields whose
// loss would silently change one (a null vs empty `Requires` feeds deployment activation; a null `DocId` is
// what makes an EP's FQN column fall back to its route).
public sealed class CallersEntryPointCacheTests
{
    // Cold (nothing cached) vs warm (served from cache.db) vs --no-cache (derived, cache bypassed): three
    // different code paths through the EP set, one answer. Byte-for-byte, in both renderings — the TSV lens
    // carries the columns the cached record now supplies (requires, and the fqn resolved from the DocID).
    [Test]
    [Arguments(false)] // the human listing
    [Arguments(true)] // the tsv lens (requires + the DocID-resolved fqn column), reverse-only rows included
    public async Task Cached_and_uncached_runs_are_byte_identical(bool tsv)
    {
        string[] query = tsv
            ? ["callers", "CreateTeamAsync", "--entrypoints", "--format", "tsv", "--include-reverse-only", "--no-live"]
            : ["callers", "CreateTeamAsync", "--entrypoints", "--no-live"];
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        await IndexAsync(playground, workingDirectory);

        var cold = await RunAsync(query, workingDirectory);
        var warm = await RunAsync(query, workingDirectory);
        var bypassed = await RunAsync([.. query, "--no-cache"], workingDirectory);

        cold.Exit.ShouldBe(0);
        cold.Out.ShouldNotBeEmpty();
        warm.Out.ShouldBe(cold.Out);
        bypassed.Out.ShouldBe(cold.Out);
        bypassed.Exit.ShouldBe(cold.Exit);
        warm.Exit.ShouldBe(cold.Exit);
    }

    // The zero-entry-point answer (with its async hint + frontier attribution) is derived from the SAME cached
    // set, and is the answer most easily corrupted by a cache that drops rows — "no entry points reach this"
    // de-risks a change WRONGLY. Pin it warm-vs-cold too, not just the populated listing.
    [Test]
    public async Task A_zero_entrypoint_answer_survives_the_cache_unchanged()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        await IndexAsync(playground, workingDirectory);
        string[] query = ["callers", "NoSuchMethodAnywhere", "--entrypoints", "--no-live"];

        var cold = await RunAsync(query, workingDirectory);
        var warm = await RunAsync(query, workingDirectory);

        cold.Exit.ShouldBe(1);
        warm.Exit.ShouldBe(1);
        warm.Out.ShouldBe(cold.Out);
    }

    // The cache is actually POPULATED (a test that only compared two runs would pass just as happily if the
    // Put silently failed and both runs derived). Assert the blob under the artifact's own key exists, decodes,
    // and holds a real EP set — and that the SAME key is what a fresh process would look under.
    [Test]
    public async Task A_first_run_writes_the_ep_record_set_under_the_ep_key()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        await IndexAsync(playground, workingDirectory);

        (await RunAsync(["callers", "CreateTeamAsync", "--entrypoints", "--no-live"], workingDirectory)).Exit.ShouldBe(0);

        var records = CachedRecords(workingDirectory).ShouldNotBeNull();
        records.ShouldNotBeEmpty();
        // The whole-store set, not the query's answer: caching the intersection would make the entry serve one
        // pattern and quietly under-report every other.
        records.Count.ShouldBeGreaterThan(1);
        records.ShouldAllBe(r => r.Kind.Length > 0 && r.Route.Length > 0);
        // The handler DocID is resolved at derivation time (the whole-store method map is discarded with the
        // fact bundle), so at least the method-declared EPs must carry one — else every FQN column would have
        // silently degraded to the slash route, which matches nothing as a `rig tree`/`reaches` pattern.
        records.Any(r => r.DocId is not null).ShouldBeTrue();
    }

    // --no-cache must BYPASS, not just re-derive-and-overwrite: a run that writes nothing is what makes the
    // flag usable as a "prove it from the facts" escape hatch after a suspected bad cache entry.
    [Test]
    public async Task No_cache_neither_reads_nor_writes_the_ep_record_entry()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        await IndexAsync(playground, workingDirectory);

        var bypassed = await RunAsync(["callers", "CreateTeamAsync", "--entrypoints", "--no-cache", "--no-live"], workingDirectory);

        bypassed.Exit.ShouldBe(0);
        bypassed.Out.ShouldNotBeEmpty();
        CachedRecords(workingDirectory).ShouldBeNull();
    }

    // A rule edit is the second invalidation axis (the first, a reindex, is the store identity every key
    // already carries). --rules shifts the fingerprint, so the run must MISS rather than serve the set derived
    // under the default cascade — the failure mode being a `--rules` query silently answering with the
    // default-rule entry points.
    [Test]
    public async Task A_rules_file_keys_its_own_entry_rather_than_serving_the_default_one()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        await IndexAsync(playground, workingDirectory);
        var extraRules = Path.Combine(workingDirectory, "extra.rules.json");
        await File.WriteAllTextAsync(extraRules, "{ \"entryPoints\": { \"classInheritance\": [] } }");

        (await RunAsync(["callers", "CreateTeamAsync", "--entrypoints", "--no-live"], workingDirectory)).Exit.ShouldBe(0);
        // Exit is deliberately unasserted: under a rule file that declares no entry points the honest answer
        // is "none reach it" (exit 1). What matters here is WHICH entry it consulted and wrote.
        await RunAsync(["callers", "CreateTeamAsync", "--entrypoints", "--rules", extraRules, "--no-live"], workingDirectory);

        // Two distinct entries, one per rule fingerprint — neither aliasing the other.
        CachedRecords(workingDirectory).ShouldNotBeNull();
        CachedRecords(workingDirectory, extraRules).ShouldNotBeNull();
        EpKey(workingDirectory).ShouldNotBe(EpKey(workingDirectory, extraRules));
    }

    // The codec's two answer-bearing subtleties, pinned directly because a store round-trip hides them:
    //   * `Requires` null (ungated EP) must NOT come back as an empty list — DeploymentMap.ActiveServices
    //     distinguishes the two, so conflating them changes which services an EP is reported active in;
    //   * ORDER must survive — the listing group-bys (first-occurrence order) then STABLE-sorts by kind+route,
    //     so a reordered payload reorders ties in the rendered answer.
    [Test]
    public void Codec_round_trips_null_requires_empty_requires_and_order()
    {
        IReadOnlyList<EntryPointContext.EntryPointRecord> records =
        [
            new EntryPointContext.EntryPointRecord(
                "page",
                "Accounts/Create",
                "Accounts/Create.aspx.cs",
                12,
                null,
                "M:Accounts.Create..ctor"
            ),
            new EntryPointContext.EntryPointRecord("action", "Accounts/Save", "Accounts/Save.cs", 40, [], "M:Accounts.Save.Post"),
            new EntryPointContext.EntryPointRecord("background", "Jobs.Nightly.Run", "Jobs/Nightly.cs", 7, ["dataserver"], null),
        ];

        var decoded = EntryPointRecordCodec.Decode(EntryPointRecordCodec.Encode(records)).ShouldNotBeNull();

        decoded.Count.ShouldBe(3);
        decoded.Select(r => r.Route).ShouldBe(records.Select(r => r.Route)); // order preserved
        decoded[0].Requires.ShouldBeNull(); // ungated stays ungated
        decoded[1].Requires.ShouldNotBeNull().ShouldBeEmpty(); // empty stays empty, not null
        decoded[2].Requires.ShouldBe(["dataserver"]);
        decoded[2].DocId.ShouldBeNull(); // a site with no indexed method symbol -> FQN falls back to the route
        decoded[0].DocId.ShouldBe("M:Accounts.Create..ctor");
    }

    // A corrupt or foreign blob is a MISS (recompute), never a crash: the cache can only ever change latency,
    // including when it is damaged.
    [Test]
    public void A_corrupt_blob_decodes_as_a_miss()
    {
        EntryPointRecordCodec.Decode([1, 2, 3, 4]).ShouldBeNull();
        EntryPointRecordCodec.Decode([]).ShouldBeNull();
    }

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

    // The key a fresh `rig callers --entrypoints` process derives, recomputed here from the same two axes.
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
