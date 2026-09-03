using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Storage;

public sealed class PersistedCompilationHealthTests
{
    [Test]
    public async Task Save_round_trips_located_errors_partial_projects_and_unlocated_count()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"rig-compile-health-{Guid.NewGuid():n}.db");
        var filePath = Path.Combine(Path.GetTempPath(), "Demo", "Broken.cs");
        try
        {
            var health = new CompilationHealth(
                [new FileCompileHealth(filePath, 2, "CS0103,CS0246", "The name 'missing' does not exist")],
                [new ProjectCompileFailure("Generator", ProjectCompileFailure.GeneratorRun)],
                UnlocatedErrorCount: 1
            );
            var result = new AnalysisResult(
                SolutionPath: "Demo.sln",
                SourceFiles: [new SourceFileInfo("Demo", filePath, "indexed", "high", "project", "source", "")],
                DiRegistrations: [],
                CompilationHealth: health
            );

            await using (var write = new RigDbContext(dbPath, pooling: false))
            {
                await Writes.SaveAsync(write, result);
            }

            await using var read = new RigDbContext(dbPath, pooling: false);
            var file = (await Reads.LoadCompileErrorFilesAsync(read)).ShouldNotBeNull().ShouldHaveSingleItem();
            file.FilePath.ShouldBe(filePath);
            file.CompileErrorCount.ShouldBe(2);
            file.CompileErrorCodes.ShouldBe("CS0103,CS0246");
            file.CompileErrorFirst.ShouldBe("The name 'missing' does not exist");

            var roundTrip = await Reads.LoadCompilationHealthAsync(read);
            roundTrip.Files.ShouldHaveSingleItem().ShouldBe(health.Files[0]);
            roundTrip.PartialProjects.ShouldHaveSingleItem().ShouldBe(health.PartialProjects[0]);
            roundTrip.UnlocatedErrorCount.ShouldBe(1);
            roundTrip.TotalErrorCount.ShouldBe(3);

            var run = (await Reads.ListRunsAsync(read)).ShouldHaveSingleItem();
            run.CompileErrorFiles.ShouldBe(1);
            run.CompileErrorTotal.ShouldBe(3);
            run.PartialProjects.ShouldBe("Generator:generator_run");
        }
        finally
        {
            DeleteStore(dbPath);
        }
    }

    [Test]
    public void Compilation_file_keys_normalize_slashes_and_follow_host_case_semantics()
    {
        var key = CompilationFilePath.Key("C:\\src\\Demo\\Broken.cs");
        key.ShouldBe("C:/src/Demo/Broken.cs");
        CompilationFilePath.Contains(new HashSet<string>([key], CompilationFilePath.Comparer), "C:/src/Demo/Broken.cs").ShouldBeTrue();

        var differentCase = CompilationFilePath.Contains(new HashSet<string>([key], CompilationFilePath.Comparer), "c:/SRC/demo/broken.cs");
        differentCase.ShouldBe(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
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
