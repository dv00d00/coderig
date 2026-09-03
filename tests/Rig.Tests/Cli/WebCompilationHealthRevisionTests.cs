using Rig.Cli;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.Impact;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class WebCompilationHealthRevisionTests
{
    [Test]
    public void Tree_collapse_keeps_node_health_but_marks_a_folded_child_effect_from_its_own_file()
    {
        const string rootId = "M:Demo.Root()";
        const string foldId = "M:Demo.Pricing.GetData()";
        const string childId = "M:Demo.Pricing.Load()";
        const string cleanFile = "/src/Clean.cs";
        const string brokenFile = "/src/Broken.cs";
        var roots = new[]
        {
            new TraceNode(
                rootId,
                "invocation",
                null,
                null,
                [
                    new TraceNode(
                        foldId,
                        "invocation",
                        null,
                        null,
                        [new TraceNode(childId, "invocation", null, null, [], CallSites: 1)],
                        CallSites: 1
                    ),
                ],
                CallSites: 1
            ),
        };
        var locations = new Dictionary<string, Rig.Cli.Services.TreeQueryService.SymbolLocation>(StringComparer.Ordinal)
        {
            [rootId] = new(cleanFile, 1),
            [foldId] = new(cleanFile, 2),
            [childId] = new(brokenFile, 3),
        };
        var effects = new[] { new DerivedEffect("db", "read", "Patient", childId, brokenFile, 3) };
        var rules = new FactRenderRules([new FactRenderRule("Pricing.GetData", "pricing")], []);
        var compileErrors = new HashSet<string>([CompilationFilePath.Key(brokenFile)], CompilationFilePath.Comparer);

        var response = TreeMapper.ToResponse(
            "Demo.Root",
            roots,
            effects,
            locations,
            new Dictionary<string, string>(),
            rules,
            compileErrors
        );
        var seam = response.Roots.ShouldHaveSingleItem().Children.ShouldHaveSingleItem();

        seam.BindingHealth.ShouldBe("ok");
        seam.Effects.ShouldHaveSingleItem().BindingHealth.ShouldBe("compile_error");
    }

    [Test]
    public async Task Impact_mapper_marks_entrypoint_effect_and_hazard_rows_from_their_revision_store()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rig-web-health-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        try
        {
            var baseFile = Path.Combine(root, "Base.cs");
            var headFile = Path.Combine(root, "Head.cs");
            const string baseId = "M:Demo.BaseEffect()";
            const string headId = "M:Demo.HeadEffect()";
            await SaveStoreAsync(root, "base", baseFile, baseId, broken: true);
            await SaveStoreAsync(root, "head", headFile, headId, broken: true);
            var delta = new EpFootprintDelta(
                "http",
                "Demo/Run",
                headFile,
                1,
                BranchEffects: 1,
                BaseEffects: 1,
                Added: [("db", "write", "Patient", headId)],
                Removed: [("db", "read", "Patient", baseId)],
                Amplified: [],
                HazardsAdded: [new HazardFinding("race_window", "Patient", headId, "high")],
                HazardsRemoved: [new HazardFinding("race_window", "Patient", baseId, "high")]
            );
            var artifact = new ImpactCacheArtifact(
                new ImpactDiff(null, [], [delta]),
                new StoreProvenance(null, null, "base", [1], ["rig"]),
                new StoreProvenance(null, null, "head", [1], ["rig"]),
                []
            );

            var response = await ImpactMapper.ToResponseAsync(root, "base", "head", artifact, [], [], includeIntrinsic: true);
            var ep = response.PerEp.ShouldHaveSingleItem();
            ep.BindingHealth.ShouldBe("compile_error");
            ep.Added.ShouldHaveSingleItem().BindingHealth.ShouldBe("compile_error");
            ep.Removed.ShouldHaveSingleItem().BindingHealth.ShouldBe("compile_error");
            ep.HazardsAdded.ShouldHaveSingleItem().BindingHealth.ShouldBe("compile_error");
            ep.HazardsRemoved.ShouldHaveSingleItem().BindingHealth.ShouldBe("compile_error");
            response.BaseCompileErrors.ShouldNotBeNull().Files.ShouldBe(1);
            response.HeadCompileErrors.ShouldNotBeNull().Files.ShouldBe(1);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Test]
    public async Task Impact_mapper_marks_a_base_only_removed_entrypoint_from_the_base_store()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rig-web-health-base-only-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        try
        {
            var baseFile = Path.Combine(root, "Removed.cs");
            var headFile = Path.Combine(root, "Unrelated.cs");
            const string baseId = "M:Demo.RemovedEffect()";
            await SaveStoreAsync(root, "base", baseFile, baseId, broken: true);
            await SaveStoreAsync(root, "head", headFile, "M:Demo.Unrelated()", broken: false);
            var delta = new EpFootprintDelta(
                "http",
                "Demo/Removed",
                baseFile,
                1,
                BranchEffects: 0,
                BaseEffects: 1,
                Added: [],
                Removed: [("db", "read", "Patient", baseId)],
                Amplified: []
            );
            var artifact = new ImpactCacheArtifact(
                new ImpactDiff(null, [], [delta]),
                new StoreProvenance(null, null, "base", [1], ["rig"]),
                new StoreProvenance(null, null, "head", [1], ["rig"]),
                []
            );

            var response = await ImpactMapper.ToResponseAsync(root, "base", "head", artifact, [], [], includeIntrinsic: true);
            var ep = response.PerEp.ShouldHaveSingleItem();

            ep.BindingHealth.ShouldBe("compile_error");
            ep.Removed.ShouldHaveSingleItem().BindingHealth.ShouldBe("compile_error");
            response.HeadCompileErrors.ShouldNotBeNull().Files.ShouldBe(0);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task SaveStoreAsync(string root, string store, string file, string symbolId, bool broken)
    {
        var result = new AnalysisResult(
            Path.Combine(root, "Demo.sln"),
            [new SourceFileInfo("Demo", file, "indexed", "high", "project", "source", "")],
            [],
            Symbols:
            [
                new SymbolFact(symbolId, "method", "Effect", "Demo", "T:Demo", "public", "", "void Effect()", file, 1, 1, "Demo", false),
            ],
            CompilationHealth: broken
                ? new CompilationHealth([new FileCompileHealth(file, 1, "CS0103", "missing")], [], 0)
                : CompilationHealth.Empty
        );
        var dir = StoreLayout.NewStoreDir(root, store);
        await using var context = new RigDbContext(Path.Combine(dir, StoreLayout.DbFileName), pooling: false);
        await Writes.SaveAsync(context, result);
    }
}
