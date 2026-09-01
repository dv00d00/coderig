using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Rig.Cli.CommandLine;
using Rig.Cli.Services;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class FileEffectsWebContractTests
{
    private const string File = "/repo/src/Demo/Orders.cs";
    private const string LoadId = "M:Demo.Orders.Load(System.Int32)";
    private const string QueryId = "M:Demo.Repository.Query(System.Int32)";

    [Test]
    public void File_response_keeps_depth_and_tree_pivot_identity_at_both_grains()
    {
        var model = new FileEffectReadModel(
            File,
            ["sql", "filesystem"],
            [new FileEffectMethod(LoadId, [new FileEffectAggregate("sql", 2), new FileEffectAggregate("filesystem", 0)])],
            [
                new FileEffectCallSite(LoadId, QueryId, Line: 17, [new FileEffectAggregate("sql", 1)]),
                new FileEffectCallSite(LoadId, TargetSymbolId: "", Line: 21, [new FileEffectAggregate("filesystem", 0)]),
            ]
        );
        var artifact = new FileEffectsQueryService.Artifact(
            model,
            new Dictionary<string, FileEffectsQueryService.MethodLocation>(StringComparer.Ordinal)
            {
                [LoadId] = new(LoadId, "Load", "Order Load(int id)", Line: 10, EndLine: 24),
            }
        );

        var response = FileEffectsEndpoint.ToResponse(artifact);

        response.File.ShouldBe(File);
        response.Families.ShouldBe(["sql", "filesystem"]);
        response.ColumnsAvailable.ShouldBeFalse();
        response.WitnessPathsIncluded.ShouldBeFalse();
        var method = response.Methods.ShouldHaveSingleItem();
        method.Id.ShouldBe(LoadId);
        method.Name.ShouldBe("Load");
        method.Line.ShouldBe(10);
        method.EndLine.ShouldBe(24);
        method.Effects.Select(effect => (effect.Family, effect.NearestDepth)).ShouldBe([("sql", 2), ("filesystem", 0)]);

        response.Sites.Count.ShouldBe(2);
        response.Sites[0].TargetMethodId.ShouldBe(QueryId);
        response.Sites[0].Effects.ShouldHaveSingleItem().NearestDepth.ShouldBe(1);
        response.Sites[1].TargetMethodId.ShouldBe("");
        response.Sites[1].Effects.ShouldHaveSingleItem().NearestDepth.ShouldBe(0);
    }

    [Test]
    public void Wire_shape_is_camel_case_and_discloses_precision_limits()
    {
        var response = new FileEffectsResponseDto(
            File,
            ["sql"],
            [new FileEffectMethodDto(LoadId, "Load", "", 10, 24, [new FileEffectAggregateDto("sql", 2)])],
            [new FileEffectCallSiteDto(LoadId, QueryId, 17, [new FileEffectAggregateDto("sql", 1)])],
            ColumnsAvailable: false,
            WitnessPathsIncluded: false
        );

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.ShouldContain("\"nearestDepth\":2");
        json.ShouldContain("\"targetMethodId\":");
        json.ShouldContain("\"columnsAvailable\":false");
        json.ShouldContain("\"witnessPathsIncluded\":false");
    }

    [Test]
    public async Task Inventory_and_source_endpoints_admit_only_indexed_store_paths()
    {
        using var fixture = await EndpointFixture.CreateAsync();

        var (inventoryStatus, inventoryBody) = await fixture.GetAsync("/api/files?q=Orders");
        inventoryStatus.ShouldBe(HttpStatusCode.OK);
        using var inventory = JsonDocument.Parse(inventoryBody);
        var file = inventory.RootElement.GetProperty("files").EnumerateArray().ShouldHaveSingleItem();
        file.GetProperty("path").GetString().ShouldBe(fixture.IndexedPath);
        file.GetProperty("projects").EnumerateArray().Select(project => project.GetString()).ShouldBe(["Demo"]);

        var (sourceStatus, sourceBody) = await fixture.GetAsync("/api/file-source?file=" + Uri.EscapeDataString("/etc/hosts"));
        sourceStatus.ShouldBe(HttpStatusCode.BadRequest);
        sourceBody.ShouldContain("not returned by this store's indexed file inventory");

        var (effectsStatus, effectsBody) = await fixture.GetAsync("/api/file-effects?file=" + Uri.EscapeDataString(fixture.SkippedPath));
        effectsStatus.ShouldBe(HttpStatusCode.BadRequest);
        effectsBody.ShouldContain("not an indexed source file");
    }

    private sealed class EndpointFixture : IDisposable
    {
        private readonly string _workingDirectory = Directory.CreateTempSubdirectory("rig-file-web-").FullName;
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public string IndexedPath => Path.Combine(_workingDirectory, "Orders.cs");

        public string SkippedPath => Path.Combine(_workingDirectory, "Generated.g.cs");

        public static async Task<EndpointFixture> CreateAsync()
        {
            var fixture = new EndpointFixture();
            const string storeId = "filewebfixture";
            var result = new AnalysisResult(
                SolutionPath: Path.Combine(fixture._workingDirectory, "Demo.sln"),
                SourceFiles:
                [
                    new SourceFileInfo("Demo", fixture.IndexedPath, "indexed", "high", "project", "", ""),
                    new SourceFileInfo("Demo", fixture.SkippedPath, "skipped", "high", "generated", "excluded", ""),
                ],
                DiRegistrations: []
            );
            var storeDir = StoreLayout.NewStoreDir(fixture._workingDirectory, storeId);
            await using (var context = new RigDbContext(Path.Combine(storeDir, StoreLayout.DbFileName), pooling: false))
            {
                await Writes.SaveAsync(context, result, provenance: new GitProvenance(new string('a', 40), "main", Dirty: false));
            }

            for (var attempt = 1; ; attempt++)
            {
                fixture._app = RigWebHost.Build(fixture._workingDirectory, FreePort());
                try
                {
                    await fixture._app.StartAsync();
                    break;
                }
                catch (IOException) when (attempt < 5)
                {
                    await fixture._app.DisposeAsync();
                }
            }

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
                Directory.Delete(_workingDirectory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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
