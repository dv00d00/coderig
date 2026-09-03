using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Rig.Cli.CommandLine;
using Rig.Cli.Git;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

// Dirty-tree provenance is per FILE: `rig index` records which files differed from HEAD while it read them
// (source_files.Dirty) and review reads that one bit back. These tests own the claims that make the bit
// sound — dirt outside the indexed source flags nothing, a dirty indexed file loses its semantic-ready
// claim while its neighbours keep theirs, and no dirt anywhere alters the Git-derived changed-file rows.
// See docs/backlog/todo/dirty-store-provenance-per-file-not-per-run.md.
public sealed class DirtyFileProvenanceTests
{
    // The case measured on meddbase-main-application, and the one the old whole-run refusal got maximally
    // wrong: every dirty file is local config that cannot affect a single semantic fact. Non-`.cs` rows are
    // not semantic-ready anyway (they were never indexed), so the claim under test is that no INDEXED file
    // is downgraded and no row blames uncommitted source.
    [Test]
    public async Task Dirt_confined_to_files_that_were_never_indexed_leaves_every_indexed_file_semantic_ready()
    {
        using var fixture = await Fixture.CreateAsync(async soiled =>
        {
            await File.WriteAllTextAsync(soiled.At("Web.config"), "<configuration><!-- local --></configuration>\n");
            await File.WriteAllTextAsync(soiled.At("notes.md"), "scratch\n");
        });

        // The probe really did see the dirt; the review is clean because the join to source_files drops it.
        fixture.DirtyFiles.ShouldContain(fixture.At("Web.config"));
        fixture.DirtyFiles.ShouldContain(fixture.At("notes.md"));

        var body = await fixture.ReviewFilesAsync();
        using var json = JsonDocument.Parse(body);
        var files = json.RootElement.GetProperty("files").EnumerateArray().ToArray();
        Row(files, "Widget.cs").GetProperty("semanticReady").GetBoolean().ShouldBeTrue(body);
        Row(files, "Other.cs").GetProperty("semanticReady").GetBoolean().ShouldBeTrue(body);
        body.ShouldNotContain("uncommitted source");
    }

    [Test]
    public async Task A_dirty_indexed_file_is_not_semantic_ready_and_the_files_beside_it_are_untouched()
    {
        using var fixture = await Fixture.CreateAsync(async soiled =>
            await File.WriteAllTextAsync(soiled.At("Widget.cs"), "class Widget { int Value() => 3; }\n")
        );

        var body = await fixture.ReviewFilesAsync();
        using var json = JsonDocument.Parse(body);
        var files = json.RootElement.GetProperty("files").EnumerateArray().ToArray();
        var widget = Row(files, "Widget.cs");
        widget.GetProperty("semanticReady").GetBoolean().ShouldBeFalse(body);
        widget.GetProperty("reason").GetString().ShouldNotBeNull().ShouldContain("uncommitted source");
        // Still reviewable as text, and still carrying its Git-derived identity: only the semantic claim went.
        widget.GetProperty("reviewable").GetBoolean().ShouldBeTrue();
        widget.GetProperty("status").GetString().ShouldBe("M");

        var other = Row(files, "Other.cs");
        other.GetProperty("semanticReady").GetBoolean().ShouldBeTrue(body);
        other.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // The changed-file list comes from `git diff <base> <head>` — two immutable commits, which dirt cannot
    // touch. Only the annotations were ever in question, so the rows must be byte-identical either way.
    [Test]
    public async Task Dirt_never_changes_the_git_derived_changed_file_rows()
    {
        using var clean = await Fixture.CreateAsync();
        using var dirty = await Fixture.CreateAsync(async soiled =>
            await File.WriteAllTextAsync(soiled.At("Widget.cs"), "class Widget { int Value() => 3; }\n")
        );

        var cleanRows = GitRows(await clean.ReviewFilesAsync());
        var dirtyRows = GitRows(await dirty.ReviewFilesAsync());

        cleanRows.ShouldBe(["M|Web.config|1|1", "M|Other.cs|1|1", "M|Widget.cs|1|1"], ignoreOrder: true);
        dirtyRows.ShouldBe(cleanRows);
    }

    // `-uall` is load-bearing: an untracked `.cs` that got indexed has no blob at HEAD at all, so it is the
    // worst case rather than an exempt one. Ignored build output is never listed (only `--ignored` would),
    // which is why `obj/` cannot pollute the set even before the source_files join drops it.
    [Test]
    public async Task Git_status_reports_modified_and_untracked_source_but_never_committed_or_ignored_paths()
    {
        var root = Directory.CreateTempSubdirectory("rig-dirty-probe-").FullName;
        try
        {
            Git(root, "init");
            Git(root, "config", "user.email", "rig@example.test");
            Git(root, "config", "user.name", "Rig Tests");
            await File.WriteAllTextAsync(Path.Combine(root, ".gitignore"), "obj/\n");
            await File.WriteAllTextAsync(Path.Combine(root, "Modified.cs"), "class Modified { }\n");
            await File.WriteAllTextAsync(Path.Combine(root, "Committed.cs"), "class Committed { }\n");
            Git(root, "add", ".");
            Git(root, "commit", "-m", "base");

            await File.WriteAllTextAsync(Path.Combine(root, "Modified.cs"), "class Modified { int Value() => 1; }\n");
            await File.WriteAllTextAsync(Path.Combine(root, "Untracked.cs"), "class Untracked { }\n");
            Directory.CreateDirectory(Path.Combine(root, "obj"));
            await File.WriteAllTextAsync(Path.Combine(root, "obj", "Generated.g.cs"), "class Generated { }\n");

            var dirtyFiles = GitProvenanceProbe.CaptureDirtyFiles(root);

            dirtyFiles.ShouldContain(Path.Combine(root, "Modified.cs"));
            dirtyFiles.ShouldContain(Path.Combine(root, "Untracked.cs"));
            dirtyFiles.ShouldNotContain(Path.Combine(root, "Committed.cs"));
            dirtyFiles.ShouldNotContain(Path.Combine(root, "obj", "Generated.g.cs"));
        }
        finally
        {
            Delete(root);
        }
    }

    // Belt and braces on top of `--porcelain -uall` not listing ignored paths: the bit is written row by row
    // over the files this run actually indexed, so a path the status output volunteered cannot enter the store.
    [Test]
    public async Task Only_paths_the_run_indexed_can_be_marked_dirty()
    {
        var root = Directory.CreateTempSubdirectory("rig-dirty-rows-").FullName;
        try
        {
            var indexed = Path.Combine(root, "Widget.cs");
            var clean = Path.Combine(root, "Other.cs");
            var neverIndexed = Path.Combine(root, "obj", "Widget.g.cs");
            var result = new AnalysisResult(
                SolutionPath: Path.Combine(root, "Demo.sln"),
                SourceFiles:
                [
                    new SourceFileInfo("Demo", indexed, "indexed", "high", "project", "", ""),
                    new SourceFileInfo("Demo", clean, "indexed", "high", "project", "", ""),
                ],
                DiRegistrations: []
            );

            await using var context = new RigDbContext(Path.Combine(root, StoreLayout.DbFileName), pooling: false);
            await Writes.SaveAsync(
                context,
                result,
                dirtyFiles: new HashSet<string>([indexed, neverIndexed], StringComparer.OrdinalIgnoreCase)
            );

            var rows = await context.SourceFiles.AsNoTracking().ToListAsync();
            rows.Single(row => row.Dirty).FilePath.ShouldBe(indexed);
            rows.Single(row => row.FilePath == clean).Dirty.ShouldBeFalse();
        }
        finally
        {
            Delete(root);
        }
    }

    private static JsonElement Row(IReadOnlyList<JsonElement> files, string path) =>
        files.Single(file => file.GetProperty("path").GetString() == path);

    private static string[] GitRows(string body)
    {
        using var json = JsonDocument.Parse(body);
        return json
            .RootElement.GetProperty("files")
            .EnumerateArray()
            .Select(file =>
                string.Join(
                    '|',
                    file.GetProperty("status").GetString(),
                    file.GetProperty("path").GetString(),
                    file.GetProperty("additions").ToString(),
                    file.GetProperty("deletions").ToString()
                )
            )
            .ToArray();
    }

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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

        using var process = Process.Start(start).ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, stderr);
        return stdout.Trim();
    }

    // Two commits touching one `.cs` pair and one `.config`, then an optional soiling of the work tree. The
    // dirty set is whatever `git status` reports at that point — the same probe `rig index` calls — so the
    // store is marked the way a real index of this tree would mark it.
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("rig-dirty-review-").FullName;
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public string BaseCommit { get; private set; } = "";

        public string HeadCommit { get; private set; } = "";

        public IReadOnlySet<string> DirtyFiles { get; private set; } = new HashSet<string>();

        public string At(string relative) => Path.Combine(_root, relative);

        public static async Task<Fixture> CreateAsync(Func<Fixture, Task>? soil = null)
        {
            var fixture = new Fixture();
            Git(fixture._root, "init");
            Git(fixture._root, "config", "user.email", "rig@example.test");
            Git(fixture._root, "config", "user.name", "Rig Tests");
            Git(fixture._root, "config", "core.autocrlf", "false");
            await File.WriteAllTextAsync(fixture.At("Widget.cs"), "class Widget { int Value() => 1; }\n");
            await File.WriteAllTextAsync(fixture.At("Other.cs"), "class Other { int Value() => 1; }\n");
            await File.WriteAllTextAsync(fixture.At("Web.config"), "<configuration><!-- base --></configuration>\n");
            Git(fixture._root, "add", ".");
            Git(fixture._root, "commit", "-m", "base");
            fixture.BaseCommit = Git(fixture._root, "rev-parse", "HEAD");

            await File.WriteAllTextAsync(fixture.At("Widget.cs"), "class Widget { int Value() => 2; }\n");
            await File.WriteAllTextAsync(fixture.At("Other.cs"), "class Other { int Value() => 2; }\n");
            await File.WriteAllTextAsync(fixture.At("Web.config"), "<configuration><!-- head --></configuration>\n");
            Git(fixture._root, "add", ".");
            Git(fixture._root, "commit", "-m", "head");
            fixture.HeadCommit = Git(fixture._root, "rev-parse", "HEAD");

            if (soil is not null)
            {
                await soil(fixture);
            }

            // Captured before the stores are written, so the `.rig` directory itself never enters the set.
            fixture.DirtyFiles = GitProvenanceProbe.CaptureDirtyFiles(fixture._root);
            await fixture.StoreAsync(fixture.BaseStore, fixture.BaseCommit, new HashSet<string>());
            await fixture.StoreAsync(fixture.HeadStore, fixture.HeadCommit, fixture.DirtyFiles);

            fixture._app = RigWebHost.Build(fixture._root, FreePort());
            await fixture._app.StartAsync();
            fixture._client = new HttpClient { BaseAddress = new Uri(fixture._app.Urls.First()) };
            return fixture;
        }

        public string BaseStore => BaseCommit[..12];

        public string HeadStore => HeadCommit[..12];

        public async Task<string> ReviewFilesAsync()
        {
            using var response = await _client.GetAsync("/api/review-files?base=" + BaseStore + "&head=" + HeadStore);
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            return body;
        }

        public void Dispose()
        {
            _client.Dispose();
            _app.StopAsync().GetAwaiter().GetResult();
            (_app as IDisposable)?.Dispose();
            Delete(_root);
        }

        private async Task StoreAsync(string storeId, string commit, IReadOnlySet<string> dirtyFiles)
        {
            var result = new AnalysisResult(
                SolutionPath: At("Demo.sln"),
                SourceFiles:
                [
                    new SourceFileInfo("Demo", At("Widget.cs"), "indexed", "high", "project", "", ""),
                    new SourceFileInfo("Demo", At("Other.cs"), "indexed", "high", "project", "", ""),
                ],
                DiRegistrations: []
            );
            var storeDir = StoreLayout.NewStoreDir(_root, storeId);
            await using var context = new RigDbContext(Path.Combine(storeDir, StoreLayout.DbFileName), pooling: false);
            await Writes.SaveAsync(
                context,
                result,
                provenance: new GitProvenance(commit, "main", dirtyFiles.Count > 0),
                dirtyFiles: dirtyFiles
            );
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
