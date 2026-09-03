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

public sealed class FileDiffWebContractTests
{
    [Test]
    public async Task Endpoint_returns_exact_git_patch_with_store_native_old_and_new_annotations()
    {
        using var fixture = await EndpointFixture.CreateAsync();

        var path =
            "/api/file-diff?base="
            + Uri.EscapeDataString(fixture.BaseStore)
            + "&head="
            + Uri.EscapeDataString(fixture.HeadStore)
            + "&file="
            + Uri.EscapeDataString(fixture.FilePath);
        var (status, body) = await fixture.GetAsync(path);

        status.ShouldBe(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        root.GetProperty("relativePath").GetString().ShouldBe("Widget.cs");
        var patch = root.GetProperty("patch").GetString().ShouldNotBeNull();
        patch.ShouldContain("-        return 1;");
        patch.ShouldContain("+        return 2;");
        root.GetProperty("base").GetProperty("store").GetString().ShouldBe(fixture.BaseStore);
        root.GetProperty("head").GetProperty("store").GetString().ShouldBe(fixture.HeadStore);
        // The renderer receives bounded patch hunks, not both complete files; this keeps a one-line change in
        // a generated/legacy file proportional to the diff rather than the total source size.
        root.GetProperty("base").GetProperty("content").GetString().ShouldBe("");
        root.GetProperty("head").GetProperty("content").GetString().ShouldBe("");
        root.GetProperty("base").GetProperty("effects").GetProperty("file").GetString().ShouldBe(fixture.FilePath);
        root.GetProperty("head").GetProperty("effects").GetProperty("file").GetString().ShouldBe(fixture.FilePath);
    }

    [Test]
    public async Task Endpoint_can_ignore_whitespace_only_changes()
    {
        using var fixture = await EndpointFixture.CreateAsync(headLine: "        return  1;");

        var (status, body) = await fixture.GetAsync(
            "/api/file-diff?ignoreWhitespace=true&base="
                + fixture.BaseStore
                + "&head="
                + fixture.HeadStore
                + "&file="
                + Uri.EscapeDataString(fixture.FilePath)
        );

        status.ShouldBe(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("patch").GetString().ShouldBe("");
    }

    // A dirty store no longer REFUSES the diff. The whole-run refusal was disabled deliberately — see
    // TODO(dirty-provenance) in FileDiffEndpoint and
    // docs/backlog/todo/dirty-store-provenance-per-file-not-per-run.md: the invariant it protected is
    // per-FILE, and the git diff between two commits never depended on the store at all, so refusing threw
    // away a sound diff. This test pins the current behaviour so restoring a guard is a deliberate act that
    // has to come back through here.
    [Test]
    public async Task A_dirty_store_does_not_block_the_diff()
    {
        using var fixture = await EndpointFixture.CreateAsync(headDirty: true);

        var (status, body) = await fixture.GetAsync(
            "/api/file-diff?base=" + fixture.BaseStore + "&head=" + fixture.HeadStore + "&file=" + Uri.EscapeDataString(fixture.FilePath)
        );

        status.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldNotContain("indexed from a dirty tree");
    }

    private sealed class EndpointFixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("rig-file-diff-").FullName;
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public string FilePath => Path.Combine(_root, "Widget.cs");

        public string BaseStore { get; private set; } = "";

        public string HeadStore { get; private set; } = "";

        public static async Task<EndpointFixture> CreateAsync(bool headDirty = false, string headLine = "        return 2;")
        {
            var fixture = new EndpointFixture();
            Git(fixture._root, "init");
            Git(fixture._root, "config", "user.email", "rig@example.test");
            Git(fixture._root, "config", "user.name", "Rig Tests");
            await File.WriteAllTextAsync(fixture.FilePath, "class Widget\n{\n    int Value()\n    {\n        return 1;\n    }\n}\n");
            Git(fixture._root, "add", "Widget.cs");
            Git(fixture._root, "commit", "-m", "base");
            var baseCommit = Git(fixture._root, "rev-parse", "HEAD");

            await File.WriteAllTextAsync(fixture.FilePath, $"class Widget\n{{\n    int Value()\n    {{\n{headLine}\n    }}\n}}\n");
            Git(fixture._root, "add", "Widget.cs");
            Git(fixture._root, "commit", "-m", "head");
            var headCommit = Git(fixture._root, "rev-parse", "HEAD");

            fixture.BaseStore = baseCommit[..12];
            fixture.HeadStore = headCommit[..12];
            await MaterializeStoreAsync(fixture._root, fixture.FilePath, fixture.BaseStore, baseCommit, dirty: false);
            await MaterializeStoreAsync(fixture._root, fixture.FilePath, fixture.HeadStore, headCommit, dirty: headDirty);

            fixture._app = RigWebHost.Build(fixture._root, FreePort());
            await fixture._app.StartAsync();
            fixture._client = new HttpClient { BaseAddress = new Uri(fixture._app.Urls.First()) };
            return fixture;
        }

        public async Task<(HttpStatusCode Status, string Body)> GetAsync(string path)
        {
            using var response = await _client.GetAsync(path);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
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

        private static async Task MaterializeStoreAsync(string workingDirectory, string filePath, string storeId, string commit, bool dirty)
        {
            var result = new AnalysisResult(
                SolutionPath: Path.Combine(workingDirectory, "Demo.sln"),
                SourceFiles: [new SourceFileInfo("Demo", filePath, "indexed", "high", "project", "", "")],
                DiRegistrations: []
            );
            var storeDir = StoreLayout.NewStoreDir(workingDirectory, storeId);
            await using var context = new RigDbContext(Path.Combine(storeDir, StoreLayout.DbFileName), pooling: false);
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
    }
}
