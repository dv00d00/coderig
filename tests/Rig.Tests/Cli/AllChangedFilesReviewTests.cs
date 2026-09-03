using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class AllChangedFilesReviewTests
{
    [Test]
    public async Task Every_git_changed_file_opens_with_side_optional_semantics()
    {
        using var fixture = await EndpointFixture.CreateAsync();
        var inventory = await fixture.GetJsonAsync("/api/review-files?base=" + fixture.BaseStore + "&head=" + fixture.HeadStore);
        var files = inventory.RootElement.GetProperty("files").EnumerateArray().ToArray();

        files.Length.ShouldBe(6);
        files.ShouldAllBe(file => file.GetProperty("reviewable").GetBoolean());
        files.Single(file => PathOf(file) == "Modified.cs").GetProperty("semanticReady").GetBoolean().ShouldBeTrue();
        files.Single(file => PathOf(file) == "Renamed.cs").GetProperty("semanticReady").GetBoolean().ShouldBeTrue();
        files.Single(file => PathOf(file) == "Copied.cs").GetProperty("semanticReady").GetBoolean().ShouldBeTrue();
        files.Single(file => PathOf(file) == "Added.cs").GetProperty("semanticReady").GetBoolean().ShouldBeFalse();
        files.Single(file => PathOf(file) == "Deleted.cs").GetProperty("semanticReady").GetBoolean().ShouldBeFalse();
        files.Single(file => PathOf(file) == "README.md").GetProperty("semanticReady").GetBoolean().ShouldBeFalse();

        foreach (var file in files)
        {
            var path = PathOf(file);
            var (status, body) = await fixture.GetAsync(
                "/api/file-diff?base=" + fixture.BaseStore + "&head=" + fixture.HeadStore + "&file=" + Uri.EscapeDataString(path)
            );
            status.ShouldBe(HttpStatusCode.OK, $"{path}: {body}");
            using var response = JsonDocument.Parse(body);
            response.RootElement.GetProperty("relativePath").GetString().ShouldBe(path);
            response.RootElement.GetProperty("patch").GetString().ShouldNotBeNull().ShouldNotBeEmpty();
        }
    }

    [Test]
    public async Task Modified_indexed_csharp_keeps_both_semantic_sides_and_accepts_absolute_deep_link()
    {
        using var fixture = await EndpointFixture.CreateAsync();
        using var response = await fixture.GetJsonAsync(
            "/api/file-diff?base="
                + fixture.BaseStore
                + "&head="
                + fixture.HeadStore
                + "&file="
                + Uri.EscapeDataString(fixture.Path("Modified.cs"))
        );
        var root = response.RootElement;

        root.GetProperty("status").GetString().ShouldBe("M");
        root.GetProperty("language").GetString().ShouldBe("csharp");
        root.GetProperty("base").GetProperty("semanticState").GetString().ShouldBe("available");
        root.GetProperty("head").GetProperty("semanticState").GetString().ShouldBe("available");
        root.GetProperty("base").GetProperty("effects").GetProperty("file").GetString().ShouldBe(fixture.Path("Modified.cs"));
        root.GetProperty("head").GetProperty("effects").GetProperty("file").GetString().ShouldBe(fixture.Path("Modified.cs"));
    }

    [Test]
    [Arguments("Added.cs", "A", "not-present", "available")]
    [Arguments("Deleted.cs", "D", "available", "not-present")]
    public async Task Added_and_deleted_csharp_expose_the_one_indexed_semantic_side(
        string path,
        string expectedStatus,
        string baseState,
        string headState
    )
    {
        using var fixture = await EndpointFixture.CreateAsync();
        using var response = await fixture.GetJsonAsync(
            "/api/file-diff?base=" + fixture.BaseStore + "&head=" + fixture.HeadStore + "&file=" + path
        );
        var root = response.RootElement;

        root.GetProperty("status").GetString().ShouldBe(expectedStatus);
        root.GetProperty("base").GetProperty("semanticState").GetString().ShouldBe(baseState);
        root.GetProperty("head").GetProperty("semanticState").GetString().ShouldBe(headState);
        if (baseState == "not-present")
        {
            root.GetProperty("base").GetProperty("effects").ValueKind.ShouldBe(JsonValueKind.Null);
            root.GetProperty("head").GetProperty("effects").GetProperty("file").GetString().ShouldBe(fixture.Path(path));
        }
        else
        {
            root.GetProperty("base").GetProperty("effects").GetProperty("file").GetString().ShouldBe(fixture.Path(path));
            root.GetProperty("head").GetProperty("effects").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task Rename_uses_old_and_new_paths_for_patch_and_semantics()
    {
        using var fixture = await EndpointFixture.CreateAsync();
        using var response = await fixture.GetJsonAsync(
            "/api/file-diff?base=" + fixture.BaseStore + "&head=" + fixture.HeadStore + "&file=Renamed.cs"
        );
        var root = response.RootElement;

        root.GetProperty("status").GetString().ShouldBe("R");
        root.GetProperty("oldPath").GetString().ShouldBe("Original.cs");
        root.GetProperty("newPath").GetString().ShouldBe("Renamed.cs");
        root.GetProperty("patch").GetString().ShouldNotBeNull().ShouldContain("rename from Original.cs");
        root.GetProperty("base").GetProperty("path").GetString().ShouldBe("Original.cs");
        root.GetProperty("head").GetProperty("path").GetString().ShouldBe("Renamed.cs");
        root.GetProperty("base").GetProperty("effects").GetProperty("file").GetString().ShouldBe(fixture.Path("Original.cs"));
        root.GetProperty("head").GetProperty("effects").GetProperty("file").GetString().ShouldBe(fixture.Path("Renamed.cs"));
    }

    [Test]
    public async Task Copy_uses_source_and_destination_paths_for_patch_and_semantics()
    {
        using var fixture = await EndpointFixture.CreateAsync();
        using var response = await fixture.GetJsonAsync(
            "/api/file-diff?base=" + fixture.BaseStore + "&head=" + fixture.HeadStore + "&file=Copied.cs"
        );
        var root = response.RootElement;

        root.GetProperty("status").GetString().ShouldBe("C");
        root.GetProperty("oldPath").GetString().ShouldBe("CopySource.cs");
        root.GetProperty("newPath").GetString().ShouldBe("Copied.cs");
        root.GetProperty("patch").GetString().ShouldNotBeNull().ShouldContain("copy from CopySource.cs");
        root.GetProperty("base").GetProperty("effects").GetProperty("file").GetString().ShouldBe(fixture.Path("CopySource.cs"));
        root.GetProperty("head").GetProperty("effects").GetProperty("file").GetString().ShouldBe(fixture.Path("Copied.cs"));
    }

    [Test]
    public async Task Unindexed_readme_opens_as_text_without_fabricated_effects()
    {
        using var fixture = await EndpointFixture.CreateAsync();
        using var response = await fixture.GetJsonAsync(
            "/api/file-diff?base=" + fixture.BaseStore + "&head=" + fixture.HeadStore + "&file=README.md"
        );
        var root = response.RootElement;

        root.GetProperty("language").GetString().ShouldBe("text");
        root.GetProperty("base").GetProperty("semanticState").GetString().ShouldBe("not-indexed");
        root.GetProperty("head").GetProperty("semanticState").GetString().ShouldBe("not-indexed");
        root.GetProperty("base").GetProperty("effects").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("head").GetProperty("effects").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("patch").GetString().ShouldNotBeNull().ShouldContain("+head documentation");
    }

    [Test]
    public async Task Endpoint_refuses_a_repo_path_that_is_not_in_the_selected_diff()
    {
        using var fixture = await EndpointFixture.CreateAsync();
        var (status, body) = await fixture.GetAsync(
            "/api/file-diff?base=" + fixture.BaseStore + "&head=" + fixture.HeadStore + "&file=Unchanged.cs"
        );

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("is not in the Git diff");
    }

    [Test]
    public void Client_derivation_token_includes_review_file_effects_and_disclosure_schemas_in_order()
    {
        var components = QueryCacheKeys.DerivationSchemaToken().Split('.').Select(int.Parse).ToArray();

        components[^3].ShouldBe(QueryCacheKeys.ReviewSchema);
        components[^2].ShouldBe(QueryCacheKeys.FileEffectsSchema);
        components[^1].ShouldBe(QueryCacheKeys.DisclosureSchema);
    }

    private static string PathOf(JsonElement file) => file.GetProperty("path").GetString().ShouldNotBeNull();

    private sealed class EndpointFixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("rig-all-review-files-").FullName;
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public string BaseStore { get; private set; } = "";

        public string HeadStore { get; private set; } = "";

        public string Path(string relative) => System.IO.Path.Combine(_root, relative);

        public static async Task<EndpointFixture> CreateAsync()
        {
            var fixture = new EndpointFixture();
            Git(fixture._root, "init");
            Git(fixture._root, "config", "user.email", "rig@example.test");
            Git(fixture._root, "config", "user.name", "Rig Tests");
            await File.WriteAllTextAsync(fixture.Path("Modified.cs"), "class Modified { int Value() => 1; }\n");
            await File.WriteAllTextAsync(fixture.Path("Deleted.cs"), "class Deleted { }\n");
            await File.WriteAllTextAsync(fixture.Path("Original.cs"), "class RenameTarget { }\n");
            await File.WriteAllTextAsync(fixture.Path("CopySource.cs"), "class CopyTarget { }\n");
            await File.WriteAllTextAsync(fixture.Path("README.md"), "base documentation\n");
            await File.WriteAllTextAsync(fixture.Path("Unchanged.cs"), "class Unchanged { }\n");
            Git(fixture._root, "add", ".");
            Git(fixture._root, "commit", "-m", "base");
            var baseCommit = Git(fixture._root, "rev-parse", "HEAD");

            await File.WriteAllTextAsync(fixture.Path("Modified.cs"), "class Modified { int Value() => 2; }\n");
            await File.WriteAllTextAsync(fixture.Path("Added.cs"), "class Added { }\n");
            File.Delete(fixture.Path("Deleted.cs"));
            Git(fixture._root, "mv", "Original.cs", "Renamed.cs");
            File.Copy(fixture.Path("CopySource.cs"), fixture.Path("Copied.cs"));
            await File.WriteAllTextAsync(fixture.Path("README.md"), "head documentation\n");
            Git(fixture._root, "add", "-A");
            Git(fixture._root, "commit", "-m", "head");
            var headCommit = Git(fixture._root, "rev-parse", "HEAD");

            fixture.BaseStore = baseCommit[..12];
            fixture.HeadStore = headCommit[..12];
            await MaterializeStoreAsync(
                fixture._root,
                fixture.BaseStore,
                baseCommit,
                [
                    fixture.Path("Modified.cs"),
                    fixture.Path("Deleted.cs"),
                    fixture.Path("Original.cs"),
                    fixture.Path("CopySource.cs"),
                    fixture.Path("Unchanged.cs"),
                ]
            );
            await MaterializeStoreAsync(
                fixture._root,
                fixture.HeadStore,
                headCommit,
                [
                    fixture.Path("Modified.cs"),
                    fixture.Path("Added.cs"),
                    fixture.Path("Renamed.cs"),
                    fixture.Path("CopySource.cs"),
                    fixture.Path("Copied.cs"),
                    fixture.Path("Unchanged.cs"),
                ]
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

        private static async Task MaterializeStoreAsync(
            string workingDirectory,
            string storeId,
            string commit,
            IReadOnlyList<string> filePaths
        )
        {
            var result = new AnalysisResult(
                SolutionPath: System.IO.Path.Combine(workingDirectory, "Demo.sln"),
                SourceFiles: filePaths.Select(path => new SourceFileInfo("Demo", path, "indexed", "high", "project", "", "")).ToArray(),
                DiRegistrations: []
            );
            var storeDir = StoreLayout.NewStoreDir(workingDirectory, storeId);
            await using var context = new RigDbContext(System.IO.Path.Combine(storeDir, StoreLayout.DbFileName), pooling: false);
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
