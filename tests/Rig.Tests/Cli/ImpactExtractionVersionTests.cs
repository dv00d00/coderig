using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rig.Cli;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Cli.Impact;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Storage;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

// End-to-end trust provenance: real temporary SQLite stores go through the public impact command so the
// warning/gate streams, all-run aggregation, and TSV cleanliness are tested together.
public sealed class ImpactExtractionVersionTests
{
    [Test]
    public void Equal_extraction_versions_with_different_builds_are_compatible()
    {
        var @base = new StoreProvenance("main", "abc", "base", [SchemaVersion.Extraction], ["rig-old"]);
        var head = new StoreProvenance("feature", "def", "head", [SchemaVersion.Extraction], ["rig-new"]);
        var error = new StringWriter();

        ImpactCommand.WriteExtractionVersionWarning(@base, head, error).ShouldBeTrue();
        error.ToString().ShouldBeEmpty();
    }

    [Test]
    public void Impact_cache_round_trips_extraction_and_build_provenance()
    {
        var baseProvenance = new StoreProvenance("main", "abc", "base", [1], ["rig-a"]);
        var headProvenance = new StoreProvenance("feature", "def", "head", [1, 2], ["rig-a", "rig-b"]);
        var blob = ImpactCacheCodec.Encode(
            new ImpactDiff(Ep: null, AffectedEps: [], PerEp: []),
            baseProvenance,
            headProvenance,
            new Dictionary<(string File, int Line), string>()
        );

        var artifact = ImpactCacheCodec.Decode(blob).ShouldNotBeNull();
        artifact.BaseProvenance.ExtractionVersionsOrEmpty.ShouldBe([1]);
        artifact.BaseProvenance.ProducingRigBuildsOrEmpty.ShouldBe(["rig-a"]);
        artifact.HeadProvenance.ExtractionVersionsOrEmpty.ShouldBe([1, 2]);
        artifact.HeadProvenance.ProducingRigBuildsOrEmpty.ShouldBe(["rig-a", "rig-b"]);
    }

    [Test]
    public async Task Same_extraction_version_is_silent_and_guard_gate_reports_OK()
    {
        var wd = NewWorkingDirectory();
        try
        {
            await MaterializeStoreAsync(wd, "samebase");
            await MaterializeStoreAsync(wd, "samehead");

            var result = await RunImpactAsync(wd, "samebase", "samehead", gate: true);

            result.Exit.ShouldBe(0);
            result.Error.ShouldNotContain("incompatible extraction versions");
            result.Error.ShouldContain("--expect-no-guard-narrowing OK");
            AssertMachineCleanTsv(result.Output);
        }
        finally
        {
            TryDelete(wd);
        }
    }

    [Test]
    public async Task Web_mapper_exposes_versions_builds_and_incompatible_warning_state()
    {
        var wd = NewWorkingDirectory();
        try
        {
            await MaterializeStoreAsync(wd, "webbase");
            await MaterializeStoreAsync(wd, "webhead");
            var artifact = new ImpactCacheArtifact(
                new ImpactDiff(Ep: null, AffectedEps: [], PerEp: []),
                new StoreProvenance("main", "abc", "webbase", [1], ["rig-a"]),
                new StoreProvenance("feature", "def", "webhead", [2], ["rig-b"]),
                []
            );

            var response = await ImpactMapper.ToResponseAsync(
                wd,
                "webbase",
                "webhead",
                artifact,
                only: [],
                exclude: [],
                includeIntrinsic: false
            );

            response.ExtractionCompatible.ShouldBeFalse();
            response.Base.ExtractionVersions.ShouldBe([1]);
            response.Base.ProducingRigBuilds.ShouldBe(["rig-a"]);
            response.Head.ExtractionVersions.ShouldBe([2]);
            response.Head.ProducingRigBuilds.ShouldBe(["rig-b"]);
            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            json.ShouldContain("\"extractionCompatible\":false");
            json.ShouldContain("\"extractionVersions\":[1]");
            json.ShouldContain("\"producingRigBuilds\":[\"rig-a\"]");
        }
        finally
        {
            TryDelete(wd);
        }
    }

    [Test]
    public async Task Mismatched_extraction_version_warns_and_guard_gate_fails_closed()
    {
        var wd = NewWorkingDirectory();
        try
        {
            await MaterializeStoreAsync(wd, "mismatchbase");
            await MaterializeStoreAsync(wd, "mismatchhead");
            await SetFirstRunExtractionVersionAsync(wd, "mismatchhead", SchemaVersion.Extraction + 1);

            var result = await RunImpactAsync(wd, "mismatchbase", "mismatchhead", gate: false);

            result.Exit.ShouldBe(0);
            result.Error.ShouldContain("WARNING: incompatible extraction versions");
            result.Error.ShouldContain($"base mismatchbase has [v{SchemaVersion.Extraction}]");
            result.Error.ShouldContain($"head mismatchhead has [v{SchemaVersion.Extraction + 1}]");
            result.Error.ShouldContain("Re-index BOTH stores with the current rig");
            AssertMachineCleanTsv(result.Output);

            var gated = await RunImpactAsync(wd, "mismatchbase", "mismatchhead", gate: true, time: true);
            gated.Exit.ShouldNotBe(0);
            gated.Error.ShouldContain("--expect-no-guard-narrowing FAILED");
            gated.Error.ShouldNotContain("--expect-no-guard-narrowing OK");
            gated.Error.ShouldContain("cache hit"); // --time phase: proves this arm replayed the first call's artifact.
            AssertMachineCleanTsv(gated.Output);
        }
        finally
        {
            TryDelete(wd);
        }
    }

    [Test]
    public async Task Extraction_warning_is_emitted_before_a_later_rules_failure()
    {
        var wd = NewWorkingDirectory();
        try
        {
            await MaterializeStoreAsync(wd, "earlybase");
            await MaterializeStoreAsync(wd, "earlyhead");
            await SetFirstRunExtractionVersionAsync(wd, "earlyhead", SchemaVersion.Extraction + 1);
            var invalidRules = Path.Combine(wd, "invalid-rules.json");
            await File.WriteAllTextAsync(invalidRules, "{ not valid json");

            var result = await RunImpactAsync(wd, "earlybase", "earlyhead", gate: false, extraArgs: ["--rules", invalidRules]);

            result.Exit.ShouldNotBe(0);
            result.Error.ShouldContain("WARNING: incompatible extraction versions");
        }
        finally
        {
            TryDelete(wd);
        }
    }

    [Test]
    public async Task Mixed_extraction_versions_inside_one_store_warn_and_fail_closed()
    {
        var wd = NewWorkingDirectory();
        try
        {
            await MaterializeStoreAsync(wd, "mixedbase");
            await MaterializeStoreAsync(wd, "mixedhead");
            await AppendRunAsync(wd, "mixedhead");
            await SetFirstRunExtractionVersionAsync(wd, "mixedhead", SchemaVersion.Extraction + 1);

            var result = await RunImpactAsync(wd, "mixedbase", "mixedhead", gate: true);

            result.Exit.ShouldNotBe(0);
            result.Error.ShouldContain("WARNING: incompatible extraction versions");
            result.Error.ShouldContain($"head mixedhead has [v{SchemaVersion.Extraction},v{SchemaVersion.Extraction + 1}]");
            result.Error.ShouldContain("--expect-no-guard-narrowing FAILED");
            result.Error.ShouldNotContain("--expect-no-guard-narrowing OK");
            AssertMachineCleanTsv(result.Output);
        }
        finally
        {
            TryDelete(wd);
        }
    }

    private static async Task MaterializeStoreAsync(string workingDirectory, string storeId)
    {
        var dir = StoreLayout.NewStoreDir(workingDirectory, storeId);
        await using var context = new RigDbContext(Path.Combine(dir, StoreLayout.DbFileName), pooling: false);
        await Writes.SaveAsync(context, EmptyResult(storeId));
    }

    private static async Task AppendRunAsync(string workingDirectory, string storeId)
    {
        var dbPath = StoreLayout.DbPathForRef(new WorkspaceLocation(workingDirectory, storeId));
        await using var context = new RigDbContext(dbPath, pooling: false);
        await Writes.SaveAsync(context, EmptyResult(storeId + "-append"));
    }

    private static async Task SetFirstRunExtractionVersionAsync(string workingDirectory, string storeId, int version)
    {
        var dbPath = StoreLayout.DbPathForRef(new WorkspaceLocation(workingDirectory, storeId));
        await using var context = new RigDbContext(dbPath, pooling: false);
        var run = await context.Runs.OrderBy(r => r.Id).FirstAsync();
        run.ExtractionVersion = version;
        await context.SaveChangesAsync();
    }

    private static AnalysisResult EmptyResult(string name) =>
        new(SolutionPath: Path.Combine(Path.GetTempPath(), name + ".sln"), SourceFiles: [], DiRegistrations: []);

    private static async Task<(int Exit, string Output, string Error)> RunImpactAsync(
        string workingDirectory,
        string baseStore,
        string headStore,
        bool gate,
        bool time = false,
        bool noCache = false,
        IReadOnlyList<string>? extraArgs = null
    )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var args = new List<string> { "impact", "--base", baseStore, "--head", headStore, "--format", "tsv" };
        if (gate)
        {
            args.Add("--expect-no-guard-narrowing");
        }
        if (time)
        {
            args.Add("--time");
        }
        if (noCache)
        {
            args.Add("--no-cache");
        }
        if (extraArgs is not null)
        {
            args.AddRange(extraArgs);
        }

        var exit = await CliApplication.RunAsync([.. args], output, error, workingDirectory);
        return (exit, output.ToString(), error.ToString());
    }

    private static void AssertMachineCleanTsv(string output)
    {
        output.ShouldContain("impact_summary\t");
        output.ShouldNotContain("WARNING");
        output.ShouldNotContain("--expect-no-guard-narrowing");
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            line.Split('\t').Length.ShouldBeGreaterThan(1);
        }
    }

    private static string NewWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rig-impact-extraction-{Guid.NewGuid():n}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup for SQLite handles on CI.
        }
    }
}
