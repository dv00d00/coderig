using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Rig.Cli.CommandLine;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class ReviewFullFileTests
{
    [Test]
    public async Task Full_source_is_exact_on_both_sides_even_when_worktree_is_dirty_and_patch_stays_bounded()
    {
        using var fixture = await Fixture.CreateAsync();
        await File.WriteAllTextAsync(fixture.Path("Modified.cs"), "dirty local source, never a review source");
        foreach (var side in new[] { "base", "head" })
        {
            using var document = await fixture.SourceAsync("Modified.cs", side);
            var source = document.RootElement;
            source.GetProperty("state").GetString().ShouldBe("available");
            source.GetProperty("content").GetString().ShouldBe(side == "base" ? Fixture.BaseText : Fixture.HeadText);
            source.GetProperty("side").GetString().ShouldBe(side);
            source.GetProperty("commit").GetString().ShouldBe(side == "base" ? fixture.BaseCommit : fixture.HeadCommit);
            source.GetProperty("store").GetString().ShouldBe(side == "base" ? fixture.BaseStore : fixture.HeadStore);
            source
                .GetProperty("byteLength")
                .GetInt64()
                .ShouldBe(Encoding.UTF8.GetByteCount(side == "base" ? Fixture.BaseText : Fixture.HeadText));
        }

        using var patch = await fixture.GetJsonAsync(fixture.Url("/api/file-diff", "Modified.cs"));
        patch.RootElement.GetProperty("base").GetProperty("content").GetString().ShouldBe("");
        patch.RootElement.GetProperty("head").GetProperty("content").GetString().ShouldBe("");
        patch.RootElement.GetProperty("patch").GetString().ShouldNotBeNull().ShouldNotContain("source line 199");
        patch.RootElement.GetProperty("contextLines").GetInt32().ShouldBe(20);
    }

    [Test]
    public async Task Renames_missing_sides_empty_text_binary_and_large_sources_are_explicit()
    {
        using var fixture = await Fixture.CreateAsync();
        foreach (
            var (file, side, state, path, content) in new (string, string, string, string?, string?)[]
            {
                ("Renamed.cs", "base", "available", "Original.cs", "class RenameTarget { }\n"),
                ("Renamed.cs", "head", "available", "Renamed.cs", "class RenameTarget { }\n"),
                ("Added.cs", "base", "not-present", null, null),
                ("Added.cs", "head", "available", "Added.cs", "class Added { }\n"),
                ("Deleted.cs", "base", "available", "Deleted.cs", "class Deleted { }\n"),
                ("Deleted.cs", "head", "not-present", null, null),
                ("README.md", "base", "available", "README.md", "before\r\n\r\nlast line"),
                ("README.md", "head", "available", "README.md", "after\r\n\r\nlast line\r\n"),
                ("Empty.txt", "head", "available", "Empty.txt", ""),
                ("Image.bin", "head", "binary", "Image.bin", null),
                ("Encoding.txt", "head", "binary", "Encoding.txt", null),
                ("Huge.txt", "head", "too-large", "Huge.txt", null),
                ("ManyLines.txt", "head", "too-large", "ManyLines.txt", null),
            }
        )
        {
            using var document = await fixture.SourceAsync(file, side);
            var source = document.RootElement;
            source.GetProperty("state").GetString().ShouldBe(state, $"{file}:{side}");
            source.GetProperty("path").GetString().ShouldBe(path);
            source.GetProperty("content").GetString().ShouldBe(content);
            if (state != "available")
                source.GetProperty("reason").GetString().ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Test]
    public async Task Source_rejects_nonmembers_invalid_side_and_dirty_index_provenance()
    {
        using var fixture = await Fixture.CreateAsync();
        foreach (var path in new[] { "Unchanged.cs", "../outside", fixture.Path("Unchanged.cs") })
        {
            var response = await fixture.GetAsync(fixture.Url("/api/review-source", path) + "&side=head");
            response.Status.ShouldBe(HttpStatusCode.BadRequest);
            response.Body.ShouldContain("is not in the Git diff");
        }
        (await fixture.GetAsync(fixture.Url("/api/review-source", "Modified.cs") + "&side=worktree")).Status.ShouldBe(
            HttpStatusCode.BadRequest
        );
        await fixture.AddDirtyStoreAsync();
        var dirty = await fixture.GetAsync("/api/review-source?base=" + fixture.BaseStore + "&head=dirty&file=Modified.cs&side=head");
        dirty.Status.ShouldBe(HttpStatusCode.BadRequest);
        dirty.Body.ShouldContain("dirty tree");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("rig-review-source-").FullName;
        private WebApplication _app = null!;
        private HttpClient _client = null!;
        public static readonly string BaseText =
            string.Join("\n", Enumerable.Range(1, 200).Select(line => $"// source line {line}")) + "\n";
        public static readonly string HeadText = BaseText.Replace("source line 5\n", "changed source line 5\n", StringComparison.Ordinal);
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
            Git(fixture._root, "config", "core.autocrlf", "false");
            await File.WriteAllTextAsync(fixture.Path("Modified.cs"), BaseText);
            await File.WriteAllTextAsync(fixture.Path("Original.cs"), "class RenameTarget { }\n");
            await File.WriteAllTextAsync(fixture.Path("Deleted.cs"), "class Deleted { }\n");
            await File.WriteAllTextAsync(fixture.Path("Unchanged.cs"), "class Unchanged { }\n");
            await File.WriteAllTextAsync(fixture.Path("README.md"), "before\r\n\r\nlast line");
            Git(fixture._root, "add", ".");
            Git(fixture._root, "commit", "-m", "base");
            fixture.BaseCommit = Git(fixture._root, "rev-parse", "HEAD");
            await File.WriteAllTextAsync(fixture.Path("Modified.cs"), HeadText);
            Git(fixture._root, "mv", "Original.cs", "Renamed.cs");
            File.Delete(fixture.Path("Deleted.cs"));
            await File.WriteAllTextAsync(fixture.Path("Added.cs"), "class Added { }\n");
            await File.WriteAllTextAsync(fixture.Path("README.md"), "after\r\n\r\nlast line\r\n");
            await File.WriteAllTextAsync(fixture.Path("Empty.txt"), "");
            await File.WriteAllBytesAsync(fixture.Path("Image.bin"), [0, 1, 2, 3]);
            await File.WriteAllBytesAsync(fixture.Path("Encoding.txt"), [0xff, 0xfe, 0xff]);
            await File.WriteAllTextAsync(fixture.Path("Huge.txt"), new string('x', 4 * 1024 * 1024 + 1));
            await File.WriteAllTextAsync(fixture.Path("ManyLines.txt"), string.Concat(Enumerable.Repeat("x\n", 20_001)));
            Git(fixture._root, "add", "-A");
            Git(fixture._root, "commit", "-m", "head");
            fixture.HeadCommit = Git(fixture._root, "rev-parse", "HEAD");
            await fixture.StoreAsync(fixture.BaseStore, fixture.BaseCommit, false);
            await fixture.StoreAsync(fixture.HeadStore, fixture.HeadCommit, false);
            fixture._app = RigWebHost.Build(fixture._root, FreePort());
            await fixture._app.StartAsync();
            fixture._client = new HttpClient { BaseAddress = new Uri(fixture._app.Urls.First()) };
            return fixture;
        }

        public Task AddDirtyStoreAsync() => StoreAsync("dirty", HeadCommit, true);

        public string Url(string endpoint, string file) =>
            endpoint + "?base=" + BaseStore + "&head=" + HeadStore + "&file=" + Uri.EscapeDataString(file);

        public Task<JsonDocument> SourceAsync(string file, string side) => GetJsonAsync(Url("/api/review-source", file) + "&side=" + side);

        public async Task<(HttpStatusCode Status, string Body)> GetAsync(string path)
        {
            using var response = await _client.GetAsync(path);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public async Task<JsonDocument> GetJsonAsync(string path)
        {
            var (status, body) = await GetAsync(path);
            status.ShouldBe(HttpStatusCode.OK, body);
            return JsonDocument.Parse(body);
        }

        public void Dispose()
        {
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

        private async Task StoreAsync(string storeId, string commit, bool dirty)
        {
            var result = new AnalysisResult(
                SolutionPath: Path("Demo.sln"),
                SourceFiles: [new SourceFileInfo("Demo", Path("Modified.cs"), "indexed", "high", "project", "", "")],
                DiRegistrations: []
            );
            var storeDir = StoreLayout.NewStoreDir(_root, storeId);
            await using var context = new RigDbContext(System.IO.Path.Combine(storeDir, StoreLayout.DbFileName), pooling: false);
            await Writes.SaveAsync(context, result, provenance: new GitProvenance(commit, "main", dirty));
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
