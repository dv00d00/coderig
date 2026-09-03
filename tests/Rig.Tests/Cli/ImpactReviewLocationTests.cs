using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.Impact;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class ImpactReviewLocationTests
{
    [Test]
    public async Task Impact_rows_carry_unique_revision_native_locations_and_fail_closed_on_overload_ambiguity()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("rig-impact-review-location-").FullName;
        try
        {
            await MaterializeAsync(workingDirectory, "base", [Method("M:Demo.Worker.Removed(System.Int32)", "/repo/Base.cs", 11)]);
            await MaterializeAsync(
                workingDirectory,
                "head",
                [
                    Method("M:Demo.Worker.Added(System.Int32)", "/repo/Head.cs", 22),
                    Method("M:Demo.Worker.Ambiguous(System.Int32)", "/repo/Ambiguous.cs", 30),
                    Method("M:Demo.Worker.Ambiguous(System.String)", "/repo/Ambiguous.cs", 40),
                ]
            );

            var delta = new EpFootprintDelta(
                Kind: "http",
                Route: "/demo",
                FilePath: "",
                Line: 0,
                BranchEffects: 2,
                BaseEffects: 1,
                Added: [("sql", "read", "Added", "Demo.Worker.Added"), ("sql", "read", "Ambiguous", "Demo.Worker.Ambiguous")],
                Removed: [("file", "write", "Removed", "Demo.Worker.Removed")],
                Amplified: [],
                HazardsAdded: [new HazardFinding("n_plus_1", "id", "Demo.Worker.Added", "high")]
            );
            var artifact = new ImpactCacheArtifact(
                new ImpactDiff(Ep: null, AffectedEps: [], PerEp: [delta]),
                new StoreProvenance("main", "base", "base"),
                new StoreProvenance("feature", "head", "head"),
                []
            );

            var response = await ImpactMapper.ToResponseAsync(
                workingDirectory,
                "base",
                "head",
                artifact,
                only: [],
                exclude: [],
                includeIntrinsic: false
            );
            var mapped = response.PerEp.Single();

            mapped.Added.Single(effect => effect.Resource == "Added").File.ShouldBe("/repo/Head.cs");
            mapped.Added.Single(effect => effect.Resource == "Added").Line.ShouldBe(22);
            mapped.Removed.Single().File.ShouldBe("/repo/Base.cs");
            mapped.Removed.Single().Line.ShouldBe(11);
            mapped.HazardsAdded.Single().File.ShouldBe("/repo/Head.cs");
            mapped.HazardsAdded.Single().Line.ShouldBe(22);
            mapped.Added.Single(effect => effect.Resource == "Ambiguous").File.ShouldBeNull();
            mapped.Added.Single(effect => effect.Resource == "Ambiguous").Line.ShouldBe(0);
        }
        finally
        {
            TryDelete(workingDirectory);
        }
    }

    private static SymbolFact Method(string id, string file, int line) =>
        new(
            SymbolId: id,
            Kind: "method",
            Name: id.Split('.').Last(),
            Namespace: "Demo",
            ContainingSymbolId: "T:Demo.Worker",
            Modifiers: "public",
            TypeKind: "",
            Signature: "void Method()",
            FilePath: file,
            Line: line,
            EndLine: line + 3,
            DefiningAssembly: "Demo",
            IsOverride: false
        );

    private static async Task MaterializeAsync(string workingDirectory, string store, IReadOnlyList<SymbolFact> symbols)
    {
        var result = new AnalysisResult(
            SolutionPath: "/repo/Demo.sln",
            SourceFiles: [],
            DiRegistrations: [],
            Symbols: symbols,
            References: [],
            TypeRelations: [],
            DispatchFacts: [],
            AllocationFacts: []
        );
        var directory = StoreLayout.NewStoreDir(workingDirectory, store);
        await using var context = new RigDbContext(Path.Combine(directory, StoreLayout.DbFileName), pooling: false);
        await Writes.SaveAsync(context, result, provenance: null);
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
