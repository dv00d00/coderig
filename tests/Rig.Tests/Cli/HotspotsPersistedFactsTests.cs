using Rig.Cli.Services;
using Rig.Cli.CommandLine;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class HotspotsPersistedFactsTests
{
    private const string Owner = "M:Demo.Owner.Run";
    private const string Lambda = "M:Demo.Owner.Run~λ0";
    private const string Callee = "M:Demo.Repo.Save";

    [Test]
    public async Task Persisted_lambda_reaches_the_hotspot_artifact_with_extent_and_metrics()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("rig-hotspot-lambda-").FullName;
        const string storeId = "lambda000001";
        try
        {
            var result = new AnalysisResult(
                SolutionPath: Path.Combine(workingDirectory, "Demo.sln"),
                SourceFiles: [],
                DiRegistrations: [],
                Symbols:
                [
                    Symbol(Owner, "method", "Run", line: 1, endLine: 8),
                    Symbol(Lambda, "lambda", "Run~λ0", line: 3, endLine: 6),
                    Symbol(Callee, "method", "Save", line: 10, endLine: 12),
                ],
                References:
                [
                    new ReferenceFact(Lambda, RefKinds.MethodGroup, Owner, "Demo", true, "Demo.cs", 2),
                    new ReferenceFact(Callee, RefKinds.Invocation, Lambda, "Demo", true, "Demo.cs", 4),
                ],
                TypeRelations: [],
                DispatchFacts: [],
                AllocationFacts: [new AllocationFact("object", "Demo.WorkItem", Lambda, "Demo.cs", 5)]
            );
            var storeDirectory = StoreLayout.NewStoreDir(workingDirectory, storeId);
            var databasePath = Path.Combine(storeDirectory, StoreLayout.DbFileName);
            await using (var context = new RigDbContext(databasePath, pooling: false))
            {
                await Writes.SaveAsync(context, result);
            }
            await using (var context = new RigDbContext(databasePath, pooling: false, readOnly: true))
            {
                (await Reads.LoadDeadCodeMethodsAsync(context)).ShouldNotContain(m => m.SymbolId == Lambda);
                (await Reads.LoadHotspotMethodsAsync(context)).ShouldContain(m => m.SymbolId == Lambda);
                (await Reads.LoadMethodEndLinesAsync(context)).ShouldNotContainKey(Lambda);
                (await Reads.LoadHotspotEndLinesAsync(context))[Lambda].ShouldBe(6);
            }

            var artifact = await HotspotsQueryService.BuildAsync(workingDirectory, storeRef: storeId, intrinsic: true);

            var row = artifact.Rows.Single(r => r.Id == Lambda);
            row.IsLambda.ShouldBeTrue();
            row.Line.ShouldBe(3);
            row.Lines.ShouldBe(4);
            row.CallerMethods.ShouldBe(1);
            row.IncomingCallSites.ShouldBe(1);
            row.CalleeMethods.ShouldBe(1);
            row.OutgoingCallSites.ShouldBe(1);
            row.EffectSites.ShouldBe(1);
            row.EffectSitesPer100Lines.ShouldBe(25d);
        }
        finally
        {
            TryDelete(workingDirectory);
        }
    }

    private static SymbolFact Symbol(string id, string kind, string name, int line, int endLine) =>
        new(
            SymbolId: id,
            Kind: kind,
            Name: name,
            Namespace: "Demo",
            ContainingSymbolId: "T:Demo.Owner",
            Modifiers: "private",
            TypeKind: "",
            Signature: kind == "lambda" ? "lambda" : $"void {name}()",
            FilePath: "Demo.cs",
            Line: line,
            EndLine: endLine,
            DefiningAssembly: "Demo",
            IsOverride: false
        );

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup: a pooled SQLite handle must not fail the behavioral assertion.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup on Windows agents.
        }
    }
}
