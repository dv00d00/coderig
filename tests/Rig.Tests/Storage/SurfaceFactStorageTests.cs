using Microsoft.EntityFrameworkCore;
using Rig.Domain;
using Rig.Domain.Data;
using Rig.Storage;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Storage;

public sealed class SurfaceFactStorageTests
{
    [Test]
    public async Task Schema_v7_round_trips_symbol_and_assembly_surface_and_rejects_v6()
    {
        var directory = Directory.CreateTempSubdirectory("rig-surface-").FullName;
        var database = Path.Combine(directory, "rig.db");
        try
        {
            var symbol = new SymbolFact(
                "M:App.C.M",
                "method",
                "M",
                "App",
                "T:App.C",
                "public",
                "",
                "App.C.M()",
                "C.cs",
                1,
                1,
                "App",
                false,
                BodyHash: "body",
                SurfaceHash: "surface",
                IsIterator: true
            );
            var projectSurface = new ProjectSurfaceSnapshot(
                "App",
                Path.Combine(directory, "App.csproj"),
                "App",
                [new ProjectSurfaceShard("C.cs", false, "shard")],
                "aggregate"
            );
            var result = new AnalysisResult(
                Path.Combine(directory, "App.slnx"),
                [],
                [],
                Symbols: [symbol],
                References: [],
                ProjectSurfaces: [projectSurface]
            );

            await using (var write = new RigDbContext(database, pooling: false))
            {
                await Writes.SaveAsync(write, result);
            }

            await using (var read = new RigDbContext(database, pooling: false))
            {
                var storedSymbol = await read.SymbolFacts.AsNoTracking().SingleAsync();
                storedSymbol.SurfaceHash.ShouldBe("surface");
                storedSymbol.IsIterator.ShouldBeTrue();
                (await read.Assemblies.AsNoTracking().SingleAsync()).SurfaceHash.ShouldBe("aggregate");
                await SchemaGate.AssertReadableAsync(read);
            }

            var collidingProject = projectSurface with
            {
                ProjectName = "OtherApp",
                ProjectFilePath = Path.Combine(directory, "OtherApp.csproj"),
                SurfaceHash = "other-aggregate",
            };
            await using (var write = new RigDbContext(database, pooling: false))
            {
                await Writes.SaveAsync(write, result with { ProjectSurfaces = [projectSurface, collidingProject] });
            }

            await using (var read = new RigDbContext(database, pooling: false))
            {
                (await read.Assemblies.AsNoTracking().SingleAsync()).SurfaceHash.ShouldBe(
                    ProjectContentHash.Compute(["aggregate", "other-aggregate"])
                );
                await read.Database.ExecuteSqlRawAsync("UPDATE meta SET index_schema_version = 6 WHERE id = 0;");
            }

            await using var stale = new RigDbContext(database, pooling: false);
            var exception = await Should.ThrowAsync<RigStoreException>(() => SchemaGate.AssertReadableAsync(stale));
            exception.Message.ShouldContain("schema v6");
            exception.Message.ShouldContain("re-index");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
