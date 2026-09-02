using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Rig.Cli.CommandLine;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class ReviewLineCountsTests
{
    [Test]
    public async Task Every_text_row_carries_its_git_changed_line_counts_including_across_a_rename()
    {
        using var fixture = await Fixture.CreateAsync();
        var files = await fixture.ReviewFilesAsync();

        // Asymmetric on purpose: equal counts would pass even with additions and deletions transposed.
        Counts(files, "Modified.cs").ShouldBe((3, 1));
        Counts(files, "Added.cs").ShouldBe((3, 0));
        Counts(files, "Deleted.cs").ShouldBe((0, 2));
        // Rename detection reports the counts against the record's NEW path, which is the row's identity.
        Counts(files, "Renamed.cs").ShouldBe((1, 0));
        Row(files, "Renamed.cs").GetProperty("status").GetString().ShouldBe("R");
        Row(files, "Renamed.cs").GetProperty("oldPath").GetString().ShouldBe("Original.cs");
    }

    [Test]
    public async Task A_binary_row_reports_unknown_line_counts_rather_than_zero()
    {
        using var fixture = await Fixture.CreateAsync();
        var binary = Row(await fixture.ReviewFilesAsync(), "Image.bin");

        // Git prints "-" for a binary file: the changed-line count is unknown, and a rendered "0" would be a
        // claim it measured zero. Null is the only honest projection.
        binary.GetProperty("additions").ValueKind.ShouldBe(JsonValueKind.Null);
        binary.GetProperty("deletions").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public void Numstat_z_frames_a_rename_as_counts_then_two_paths_and_a_binary_as_dashes()
    {
        // Verbatim `git diff --numstat -z --find-renames --find-copies --find-copies-harder` output. Unlike
        // --name-status -z, the counts and the path share ONE NUL-terminated record; a rename ends that record
        // after the second tab and emits the old and the new path as two further records.
        var counts = FileDiffEndpoint.ParseNumstat(
            "2\t0\tadded.txt\0" + "0\t1\tdeleted.txt\0" + "1\t1\tkeep.txt\0" + "1\t0\t\0oldname.txt\0newname.txt\0" + "-\t-\tpic.bin\0"
        );

        counts.Count.ShouldBe(5);
        counts["added.txt"].ShouldBe(new FileDiffEndpoint.NumstatCounts(2, 0));
        counts["deleted.txt"].ShouldBe(new FileDiffEndpoint.NumstatCounts(0, 1));
        counts["keep.txt"].ShouldBe(new FileDiffEndpoint.NumstatCounts(1, 1));
        counts["newname.txt"].ShouldBe(new FileDiffEndpoint.NumstatCounts(1, 0));
        counts.ContainsKey("oldname.txt").ShouldBeFalse();
        counts["pic.bin"].ShouldBe(new FileDiffEndpoint.NumstatCounts(null, null));
    }

    private static JsonElement Row(JsonElement[] files, string path) => files.Single(file => file.GetProperty("path").GetString() == path);

    private static (int?, int?) Counts(JsonElement[] files, string path)
    {
        var row = Row(files, path);
        return (row.GetProperty("additions").GetInt32(), row.GetProperty("deletions").GetInt32());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("rig-review-counts-").FullName;
        private WebApplication _app = null!;
        private HttpClient _client = null!;
        private JsonDocument? _inventory;
        public string BaseCommit { get; private set; } = "";
        public string HeadCommit { get; private set; } = "";
        public string BaseStore => BaseCommit[..12];
        public string HeadStore => HeadCommit[..12];

        public string Path(string relative) => System.IO.Path.Combine(_root, relative);

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            Git(fixture._root, "init");
            Git(fixture._root, "config", "user.email", "rig@example.test");
            Git(fixture._root, "config", "user.name", "Rig Tests");
            // Line counts must not depend on the checkout's newline translation.
            Git(fixture._root, "config", "core.autocrlf", "false");
            await File.WriteAllTextAsync(fixture.Path("Modified.cs"), "class Modified { }\n// one\n// two\n");
            await File.WriteAllTextAsync(
                fixture.Path("Original.cs"),
                "class RenameTarget { }\n" + string.Join("\n", Enumerable.Range(1, 9).Select(line => $"// body {line}")) + "\n"
            );
            await File.WriteAllTextAsync(fixture.Path("Deleted.cs"), "class Deleted { }\n// gone\n");
            await File.WriteAllBytesAsync(fixture.Path("Image.bin"), [0, 1, 2, 3]);
            Git(fixture._root, "add", ".");
            Git(fixture._root, "commit", "-m", "base");
            fixture.BaseCommit = Git(fixture._root, "rev-parse", "HEAD");

            await File.WriteAllTextAsync(fixture.Path("Modified.cs"), "class Modified { }\n// ONE\n// two\n// three\n// four\n");
            Git(fixture._root, "mv", "Original.cs", "Renamed.cs");
            await File.AppendAllTextAsync(fixture.Path("Renamed.cs"), "// body 10\n");
            File.Delete(fixture.Path("Deleted.cs"));
            await File.WriteAllTextAsync(fixture.Path("Added.cs"), "class Added { }\n// first\n// second\n");
            await File.WriteAllBytesAsync(fixture.Path("Image.bin"), [4, 5, 6, 7, 8, 9]);
            Git(fixture._root, "add", "-A");
            Git(fixture._root, "commit", "-m", "head");
            fixture.HeadCommit = Git(fixture._root, "rev-parse", "HEAD");

            await fixture.StoreAsync(fixture.BaseStore, fixture.BaseCommit);
            await fixture.StoreAsync(fixture.HeadStore, fixture.HeadCommit);
            fixture._app = RigWebHost.Build(fixture._root, FreePort());
            await fixture._app.StartAsync();
            fixture._client = new HttpClient { BaseAddress = new Uri(fixture._app.Urls.First()) };
            return fixture;
        }

        public async Task<JsonElement[]> ReviewFilesAsync()
        {
            var (status, body) = await GetAsync("/api/review-files?base=" + BaseStore + "&head=" + HeadStore);
            status.ShouldBe(HttpStatusCode.OK, body);
            _inventory = JsonDocument.Parse(body);
            return _inventory.RootElement.GetProperty("files").EnumerateArray().ToArray();
        }

        public async Task<(HttpStatusCode Status, string Body)> GetAsync(string path)
        {
            using var response = await _client.GetAsync(path);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public void Dispose()
        {
            _inventory?.Dispose();
            _client.Dispose();
            _app.StopAsync().GetAwaiter().GetResult();
            (_app as IDisposable)?.Dispose();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private async Task StoreAsync(string storeId, string commit)
        {
            var result = new AnalysisResult(
                SolutionPath: Path("Demo.sln"),
                SourceFiles: [new SourceFileInfo("Demo", Path("Modified.cs"), "indexed", "high", "project", "", "")],
                DiRegistrations: []
            );
            var storeDir = StoreLayout.NewStoreDir(_root, storeId);
            await using var context = new RigDbContext(System.IO.Path.Combine(storeDir, StoreLayout.DbFileName), pooling: false);
            await Writes.SaveAsync(context, result, provenance: new GitProvenance(commit, "main", Dirty: false));
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string Git(string directory, params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start).ShouldNotBeNull();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            process.ExitCode.ShouldBe(0, stderr);
            return stdout.Trim();
        }
    }
}
