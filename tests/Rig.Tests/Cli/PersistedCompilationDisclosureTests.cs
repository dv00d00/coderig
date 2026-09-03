using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class PersistedCompilationDisclosureTests
{
    [Test]
    public async Task Files_runs_and_derive_disclose_persisted_compile_health_without_polluting_tsv()
    {
        await using var fixture = await BrokenStore.CreateAsync();

        var files = await fixture.RunAsync("files", "--compile-errors", "--format", "tsv");
        files.Exit.ShouldBe(0, files.Error);
        var fileLines = files.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        fileLines[0].ShouldBe("project\tfile\terror_count\tcodes\tfirst_message");
        fileLines[1].Split('\t').Length.ShouldBe(5);
        fileLines[1].ShouldContain("Demo\t");
        fileLines[1].ShouldContain("\t2\tCS0103,CS0246\tThe name 'missing' does not exist");

        var runs = await fixture.RunAsync("runs");
        runs.Exit.ShouldBe(0, runs.Error);
        runs.Output.ShouldContain("partial=1 file(s), 3 compile error(s), projects=Generator:generator_run");

        var derive = await fixture.RunAsync("derive", "--format", "tsv");
        derive.Exit.ShouldBe(0, derive.Error);
        var compileRow = derive.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldHaveSingleItem();
        compileRow.ShouldStartWith("compile_error\tDemo\t");
        compileRow.Split('\t').Length.ShouldBe(6);
        derive.Output.ShouldNotContain("did not fully compile");
        derive.Error.ShouldContain("did not fully compile");
        derive.Error.ShouldContain("MISSING or WRONG");
        derive.Error.ShouldContain("rig files --compile-errors");
        derive.Error.ShouldContain("Generator: generator_run");
    }

    [Test]
    public void Tree_mapper_marks_only_nodes_from_compile_error_files_and_exposes_store_block()
    {
        var broken = "/src/Broken.cs";
        var clean = "/src/Clean.cs";
        var roots = new[]
        {
            new TraceNode("M:Demo.Broken()", "entry", null, null, []),
            new TraceNode("M:Demo.Clean()", "entry", null, null, []),
        };
        var locations = new Dictionary<string, Rig.Cli.Services.TreeQueryService.SymbolLocation>(StringComparer.Ordinal)
        {
            [roots[0].SymbolId] = new(broken, 3),
            [roots[1].SymbolId] = new(clean, 7),
        };

        var response = TreeMapper.ToResponse(
            "Demo",
            roots,
            effects: [],
            locations: locations,
            emoji: new Dictionary<string, string>(),
            renderRules: FactRenderRules.Empty,
            compileErrorFiles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { broken }
        ) with
        {
            CompileErrors = new CompileErrorsDto(1, 2, [new CompileProjectDto("Generator", "generator_run")]),
        };

        response.Roots[0].BindingHealth.ShouldBe("compile_error");
        response.Roots[1].BindingHealth.ShouldBe("ok");
        response.CompileErrors.ShouldNotBeNull().Files.ShouldBe(1);
        response.CompileErrors.Projects.ShouldHaveSingleItem().Reason.ShouldBe("generator_run");
    }

    [Test]
    public async Task Meta_api_exposes_compile_error_rollup_from_the_selected_store()
    {
        await using var fixture = await BrokenStore.CreateAsync();
        WebApplication? app = null;
        try
        {
            app = RigWebHost.Build(fixture.Root, FreePort());
            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            using var response = await client.GetAsync("/api/meta");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var compileErrors = json.RootElement.GetProperty("compileErrors");
            compileErrors.GetProperty("files").GetInt32().ShouldBe(1);
            compileErrors.GetProperty("total").GetInt32().ShouldBe(3);
            var project = compileErrors.GetProperty("projects").EnumerateArray().ShouldHaveSingleItem();
            project.GetProperty("name").GetString().ShouldBe("Generator");
            project.GetProperty("reason").GetString().ShouldBe("generator_run");

            using var hazardsResponse = await client.GetAsync("/api/hazards?from=Demo.Root");
            hazardsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var hazardsJson = JsonDocument.Parse(await hazardsResponse.Content.ReadAsStringAsync());
            hazardsJson.RootElement.GetProperty("marks").ValueKind.ShouldBe(JsonValueKind.Array);
            hazardsJson.RootElement.GetProperty("compileErrors").GetProperty("files").GetInt32().ShouldBe(1);
        }
        finally
        {
            if (app is not null)
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task Tree_full_cache_hit_still_emits_the_persisted_health_footer()
    {
        await using var fixture = await BrokenStore.CreateAsync();

        var cold = await fixture.RunAsync("tree", "Demo.Root", "--view", "full", "--time");
        var warm = await fixture.RunAsync("tree", "Demo.Root", "--view", "full", "--time");

        cold.Exit.ShouldBe(0, cold.Error);
        warm.Exit.ShouldBe(0, warm.Error);
        cold.Error.ShouldContain("did not fully compile");
        warm.Error.ShouldContain("did not fully compile");
        warm.Error.ShouldContain("forest + render-data hit (no graph load)");
    }

    [Test]
    public async Task Explicit_impact_store_roles_emit_each_captured_snapshot_once()
    {
        await using var @base = await BrokenStore.CreateAsync(partialProject: false);
        await using var head = await BrokenStore.CreateAsync(partialProject: false);
        var error = new StringWriter();
        using var invocation = StoreAnswerDisclosure.BeginInvocation(@base.Root, error, enabled: true);
        await using var baseContext = new RigDbContext(@base.DbPath, readOnly: true, pooling: false);
        await using var headContext = new RigDbContext(head.DbPath, readOnly: true, pooling: false);
        await StoreAnswerDisclosure.DiscloseCurrentAsync(baseContext, @base.StoreDirectory);
        await StoreAnswerDisclosure.DiscloseCurrentAsync(headContext, head.StoreDirectory);

        StoreAnswerDisclosure.WriteCompilationHealth("base", @base.StoreDirectory);
        StoreAnswerDisclosure.WriteCompilationHealth("base", @base.StoreDirectory);
        StoreAnswerDisclosure.WriteCompilationHealth("head", head.StoreDirectory);
        StoreAnswerDisclosure.WriteCompilationHealth("head", head.StoreDirectory);

        var rendered = error.ToString();
        rendered.Split("[base store", StringSplitOptions.None).Length.ShouldBe(2, rendered);
        rendered.Split("[head store", StringSplitOptions.None).Length.ShouldBe(2, rendered);
    }

    private sealed class BrokenStore(string root, string storeDirectory) : IAsyncDisposable
    {
        public string Root => root;
        public string StoreDirectory => storeDirectory;
        public string DbPath => Path.Combine(storeDirectory, StoreLayout.DbFileName);

        public static async Task<BrokenStore> CreateAsync(bool partialProject = true)
        {
            var root = Path.Combine(Path.GetTempPath(), $"rig-persisted-compile-{Guid.NewGuid():n}");
            Directory.CreateDirectory(root);
            var filePath = Path.Combine(root, "Broken.cs");
            var storeId = "broken000001";
            var storeDir = StoreLayout.NewStoreDir(root, storeId);
            var health = new CompilationHealth(
                [new FileCompileHealth(filePath, 2, "CS0103,CS0246", "The name 'missing' does not exist")],
                partialProject ? [new ProjectCompileFailure("Generator", ProjectCompileFailure.GeneratorRun)] : [],
                UnlocatedErrorCount: partialProject ? 1 : 0
            );
            var result = new AnalysisResult(
                SolutionPath: Path.Combine(root, "Demo.sln"),
                SourceFiles: [new SourceFileInfo("Demo", filePath, "indexed", "high", "project", "source", "")],
                DiRegistrations: [],
                Symbols:
                [
                    new SymbolFact(
                        "M:Demo.Root()",
                        "method",
                        "Root",
                        "Demo",
                        "T:Demo",
                        "public static",
                        "",
                        "void Root()",
                        filePath,
                        1,
                        1,
                        "Demo",
                        false
                    ),
                ],
                CompilationHealth: health
            );
            await using (var context = new RigDbContext(Path.Combine(storeDir, StoreLayout.DbFileName), pooling: false))
            {
                await Writes.SaveAsync(context, result);
                await GraphMaterializer.BuildAsync(context);
            }

            StoreLayout.WriteLatestPointer(root, storeId);
            return new BrokenStore(root, storeDir);
        }

        public async Task<(int Exit, string Output, string Error)> RunAsync(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var exit = await CliApplication.RunAsync(args, output, error, root);
            return (exit, output.ToString(), error.ToString());
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return ValueTask.CompletedTask;
        }
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
