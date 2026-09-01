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

public sealed class ReviewFilesWebContractTests
{
    [Test]
    public async Task Endpoint_lists_all_changed_files_and_discloses_which_rows_are_semantically_reviewable()
    {
        using var fixture = await EndpointFixture.CreateAsync();

        var (status, body) = await fixture.GetAsync(
            "/api/review-files?base=" + Uri.EscapeDataString(fixture.BaseStore) + "&head=" + Uri.EscapeDataString(fixture.HeadStore)
        );

        status.ShouldBe(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        root.GetProperty("baseStore").GetString().ShouldBe(fixture.BaseStore);
        root.GetProperty("headStore").GetString().ShouldBe(fixture.HeadStore);
        var files = root.GetProperty("files").EnumerateArray().ToArray();

        var modified = files.Single(file => file.GetProperty("path").GetString() == "Widget.cs");
        modified.GetProperty("status").GetString().ShouldBe("M");
        modified.GetProperty("reviewable").GetBoolean().ShouldBeTrue();
        modified.GetProperty("oldFile").GetString().ShouldBe(fixture.WidgetPath);
        modified.GetProperty("newFile").GetString().ShouldBe(fixture.WidgetPath);

        var added = files.Single(file => file.GetProperty("path").GetString() == "Added.cs");
        added.GetProperty("status").GetString().ShouldBe("A");
        added.GetProperty("reviewable").GetBoolean().ShouldBeFalse();
        added.GetProperty("reason").GetString().ShouldNotBeNull().ShouldContain("two-path");

        var renamed = files.Single(file => file.GetProperty("path").GetString() == "Moved.cs");
        renamed.GetProperty("status").GetString().ShouldBe("R");
        renamed.GetProperty("oldPath").GetString().ShouldBe("Gone.cs");
        renamed.GetProperty("newPath").GetString().ShouldBe("Moved.cs");
        renamed.GetProperty("reviewable").GetBoolean().ShouldBeFalse();

        var documentation = files.Single(file => file.GetProperty("path").GetString() == "README.md");
        documentation.GetProperty("status").GetString().ShouldBe("M");
        documentation.GetProperty("reviewable").GetBoolean().ShouldBeFalse();
    }

    private sealed class EndpointFixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("rig-review-files-").FullName;
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public string WidgetPath => Path.Combine(_root, "Widget.cs");

        public string BaseStore { get; private set; } = "";

        public string HeadStore { get; private set; } = "";

        public static async Task<EndpointFixture> CreateAsync()
        {
            var fixture = new EndpointFixture();
            Git(fixture._root, "init");
            Git(fixture._root, "config", "user.email", "rig@example.test");
            Git(fixture._root, "config", "user.name", "Rig Tests");
            await File.WriteAllTextAsync(fixture.WidgetPath, "class Widget { int Value() => 1; }\n");
            await File.WriteAllTextAsync(Path.Combine(fixture._root, "Gone.cs"), "class Gone { }\n");
            await File.WriteAllTextAsync(Path.Combine(fixture._root, "README.md"), "base\n");
            Git(fixture._root, "add", "Widget.cs", "Gone.cs", "README.md");
            Git(fixture._root, "commit", "-m", "base");
            var baseCommit = Git(fixture._root, "rev-parse", "HEAD");

            await File.WriteAllTextAsync(fixture.WidgetPath, "class Widget { int Value() => 2; }\n");
            Git(fixture._root, "mv", "Gone.cs", "Moved.cs");
            await File.WriteAllTextAsync(Path.Combine(fixture._root, "Added.cs"), "class Added { }\n");
            await File.WriteAllTextAsync(Path.Combine(fixture._root, "README.md"), "head\n");
            Git(fixture._root, "add", "Widget.cs", "Moved.cs", "Added.cs", "README.md");
            Git(fixture._root, "commit", "-m", "head");
            var headCommit = Git(fixture._root, "rev-parse", "HEAD");

            fixture.BaseStore = baseCommit[..12];
            fixture.HeadStore = headCommit[..12];
            await MaterializeStoreAsync(
                fixture._root,
                fixture.BaseStore,
                baseCommit,
                [fixture.WidgetPath, Path.Combine(fixture._root, "Gone.cs")]
            );
            await MaterializeStoreAsync(
                fixture._root,
                fixture.HeadStore,
                headCommit,
                [fixture.WidgetPath, Path.Combine(fixture._root, "Moved.cs"), Path.Combine(fixture._root, "Added.cs")]
            );

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

        private static async Task MaterializeStoreAsync(
            string workingDirectory,
            string storeId,
            string commit,
            IReadOnlyList<string> filePaths
        )
        {
            var result = new AnalysisResult(
                SolutionPath: Path.Combine(workingDirectory, "Demo.sln"),
                SourceFiles: filePaths.Select(path => new SourceFileInfo("Demo", path, "indexed", "high", "project", "", "")).ToArray(),
                DiRegistrations: []
            );
            var storeDir = StoreLayout.NewStoreDir(workingDirectory, storeId);
            await using var context = new RigDbContext(Path.Combine(storeDir, StoreLayout.DbFileName), pooling: false);
            await Writes.SaveAsync(context, result, provenance: new GitProvenance(commit, "main", false));
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
