using System.Diagnostics;
using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Cli.Graph;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

// Every immutable-store answer identifies the store and compares its indexed provenance with the source
// checkout that produced it. The disclosure is stderr-only so TSV/JSON remain safe to pipe.
public sealed class StoreFreshnessDisclosureTests
{
    [Test]
    public async Task Current_latest_store_is_disclosed_without_polluting_machine_stdout()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        await fixture.MaterializeAsync("current000001", new GitProvenance(fixture.Commit, "main", Dirty: false), latest: true);

        var (exit, stdout, stderr) = await fixture.RunAsync("symbols", "nothing", "--format", "tsv");

        exit.ShouldBe(0, stderr);
        stdout.ShouldBe("id\tkind\tname\tsignature\tfile\tline\tassembly\n");
        stdout.ShouldNotContain("store:");
        stderr.ShouldContain($"store: current000001 (LATEST) @ {fixture.Commit[..12]} — current");
    }

    [Test]
    public async Task Source_worktree_changes_make_a_clean_index_stale()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        await fixture.MaterializeAsync("dirtywork0001", new GitProvenance(fixture.Commit, "main", Dirty: false), latest: true);
        File.AppendAllText(fixture.SolutionPath, "<!-- uncommitted -->\n");

        var (exit, _, stderr) = await fixture.RunAsync("symbols", "nothing", "--format", "json");

        exit.ShouldBe(0, stderr);
        stderr.ShouldContain("STALE: working tree has unindexed changes");
    }

    [Test]
    public async Task A_checkout_at_a_newer_commit_makes_the_index_stale()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        var indexedCommit = fixture.Commit;
        await fixture.MaterializeAsync("oldhead000001", new GitProvenance(indexedCommit, "main", Dirty: false), latest: true);
        File.AppendAllText(fixture.SolutionPath, "<!-- committed later -->\n");
        var checkoutCommit = fixture.CommitAll("advance checkout");

        var (exit, _, stderr) = await fixture.RunAsync("symbols", "nothing", "--format", "tsv");

        exit.ShouldBe(0, stderr);
        stderr.ShouldContain($"@ {indexedCommit[..12]} — STALE vs checkout HEAD {checkoutCommit[..12]}");
    }

    [Test]
    public async Task A_store_indexed_from_dirty_source_is_unverifiable()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        await fixture.MaterializeAsync("dirtyidx00001", new GitProvenance(fixture.Commit, "main", Dirty: true), latest: true);

        var (exit, _, stderr) = await fixture.RunAsync("symbols", "nothing", "--format", "tsv");

        exit.ShouldBe(0, stderr);
        stderr.ShouldContain("UNVERIFIABLE: indexed from a dirty tree");
    }

    [Test]
    public async Task Missing_index_provenance_is_reported_as_unknown()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        await fixture.MaterializeAsync("unknown000001", GitProvenance.None, latest: true);

        var (exit, _, stderr) = await fixture.RunAsync("symbols", "nothing", "--format", "tsv");

        exit.ShouldBe(0, stderr);
        stderr.ShouldContain("store: unknown000001 (LATEST) @ unknown — freshness unknown: no source commit");
    }

    [Test]
    public async Task Explicit_store_selection_is_identified_as_pinned()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        await fixture.MaterializeAsync("pinned000001", new GitProvenance(fixture.Commit, "main", Dirty: false));
        await fixture.MaterializeAsync("latest000001", new GitProvenance(fixture.Commit, "main", Dirty: false), latest: true);

        var (exit, _, stderr) = await fixture.RunAsync(
            "symbols",
            "nothing",
            "--store",
            "pinned000001",
            "--format",
            "tsv"
        );

        exit.ShouldBe(0, stderr);
        stderr.ShouldContain("store: pinned000001 (pinned)");
        stderr.ShouldNotContain("store: latest000001");
    }

    [Test]
    public async Task Reopening_one_store_in_an_invocation_discloses_it_once_and_runs_stays_quiet()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        await fixture.MaterializeAsync("dedupe000001", new GitProvenance(fixture.Commit, "main", Dirty: false), latest: true);
        var error = new StringWriter();

        var exit = await CommandGuard.RunGuardedAsync(
            fixture.AnalysisRoot,
            error,
            async () =>
            {
                await using var first = await TraversalGraphLoader.OpenReadContextGatedAsync(
                    new WorkspaceLocation(fixture.AnalysisRoot)
                );
                await using var second = await TraversalGraphLoader.OpenReadContextGatedAsync(
                    new WorkspaceLocation(fixture.AnalysisRoot)
                );
                return 0;
            }
        );

        exit.ShouldBe(0, error.ToString());
        DisclosureLines(error.ToString()).Count.ShouldBe(1);

        var (runsExit, _, runsError) = await fixture.RunAsync("runs");
        runsExit.ShouldBe(0, runsError);
        DisclosureLines(runsError).ShouldBeEmpty();
    }

    [Test]
    public async Task Impact_discloses_both_pinned_stores_and_keeps_tsv_stdout_pure()
    {
        await using var fixture = await FreshnessFixture.CreateAsync();
        await fixture.MaterializeAsync("impactbase01", new GitProvenance(fixture.Commit, "main", Dirty: false));
        await fixture.MaterializeAsync("impacthead01", new GitProvenance(fixture.Commit, "main", Dirty: false), latest: true);

        var (exit, stdout, stderr) = await fixture.RunAsync(
            "impact",
            "--base",
            "impactbase01",
            "--head",
            "impacthead01",
            "--format",
            "tsv",
            "--no-cache"
        );

        exit.ShouldBe(0, stderr);
        stdout.ShouldNotContain("store:");
        var lines = DisclosureLines(stderr);
        lines.Count.ShouldBe(2);
        lines.ShouldContain(line => line.Contains("store: impactbase01 (pinned)", StringComparison.Ordinal));
        lines.ShouldContain(line => line.Contains("store: impacthead01 (pinned)", StringComparison.Ordinal));
    }

    private static List<string> DisclosureLines(string stderr) =>
        stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => line.StartsWith("store: ")).ToList();

    private sealed class FreshnessFixture : IAsyncDisposable
    {
        private FreshnessFixture(string root, string sourceRoot, string analysisRoot, string solutionPath, string commit)
        {
            Root = root;
            SourceRoot = sourceRoot;
            AnalysisRoot = analysisRoot;
            SolutionPath = solutionPath;
            Commit = commit;
        }

        private string Root { get; }
        private string SourceRoot { get; }
        public string AnalysisRoot { get; }
        public string SolutionPath { get; }
        public string Commit { get; private set; }

        public static Task<FreshnessFixture> CreateAsync()
        {
            var root = Directory.CreateTempSubdirectory("rig-store-freshness-").FullName;
            var sourceRoot = Path.Combine(root, "source");
            var analysisRoot = Path.Combine(root, "analysis");
            Directory.CreateDirectory(sourceRoot);
            Directory.CreateDirectory(analysisRoot);
            var solutionPath = Path.Combine(sourceRoot, "Demo.slnx");
            File.WriteAllText(solutionPath, "<Solution />\n");
            Git(sourceRoot, "init", "--quiet");
            Git(sourceRoot, "add", "--all");
            Git(sourceRoot, "-c", "user.name=Rig Tests", "-c", "user.email=rig@example.invalid", "commit", "--quiet", "-m", "initial");
            var commit = Git(sourceRoot, "rev-parse", "HEAD");
            return Task.FromResult(new FreshnessFixture(root, sourceRoot, analysisRoot, solutionPath, commit));
        }

        public string CommitAll(string message)
        {
            Git(SourceRoot, "add", "--all");
            Git(
                SourceRoot,
                "-c",
                "user.name=Rig Tests",
                "-c",
                "user.email=rig@example.invalid",
                "commit",
                "--quiet",
                "-m",
                message
            );
            Commit = Git(SourceRoot, "rev-parse", "HEAD");
            return Commit;
        }

        public async Task MaterializeAsync(string storeId, GitProvenance provenance, bool latest = false)
        {
            var result = new AnalysisResult(
                SolutionPath: SolutionPath,
                SourceFiles: [],
                DiRegistrations: [],
                Symbols: [],
                References: [],
                TypeRelations: [],
                DispatchFacts: [],
                AllocationFacts: []
            );
            var storeDir = StoreLayout.NewStoreDir(AnalysisRoot, storeId);
            await using (var context = new RigDbContext(Path.Combine(storeDir, StoreLayout.DbFileName), pooling: false))
            {
                await Writes.SaveAsync(context, result, provenance: provenance);
            }

            if (latest)
            {
                StoreLayout.WriteLatestPointer(AnalysisRoot, storeId);
            }
        }

        public async Task<(int Exit, string Out, string Err)> RunAsync(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var exit = await CliApplication.RunAsync(args, output, error, AnalysisRoot);
            return (exit, output.ToString(), error.ToString());
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return ValueTask.CompletedTask;
        }

        private static string Git(string workingDirectory, params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr}");
            }

            return stdout.Trim();
        }
    }
}
