using Rig.Domain.Data;
using Rig.Storage;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Storage;

public sealed class ExtractionVersionStampTests
{
    [Test]
    public async Task Fresh_run_round_trips_extraction_version_and_producing_rig_build()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"rig-extraction-stamp-{Guid.NewGuid():n}.db");
        try
        {
            var result = new AnalysisResult(SolutionPath: "Demo.sln", SourceFiles: [], DiRegistrations: []);
            await using (var write = new RigDbContext(dbPath, pooling: false))
            {
                await Writes.SaveAsync(write, result);
            }

            await using var read = new RigDbContext(dbPath, pooling: false);
            var run = (await Reads.ListRunsAsync(read)).ShouldHaveSingleItem();
            run.ExtractionVersion.ShouldBe(SchemaVersion.Extraction);
            run.ProducingRigBuild.ShouldNotBeNullOrWhiteSpace();
        }
        finally
        {
            DeleteStore(dbPath);
        }
    }

    private static void DeleteStore(string dbPath)
    {
        foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
