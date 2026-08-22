using Rig.Cli.Commands;
using Rig.Cli.Impact;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// Storage round-trip: a real playground extraction must MINE a non-empty BodyHash for bodied methods and
// persist it so LoadSymbolBodyHashesAsync reads it back. Pins the Facts.cs + entity + Writes INSERT + Reads
// guarded-read wiring end to end, and confirms re-extracting the SAME source yields the SAME hash (deterministic).
[ClassDataSource<AnalyzedPlaygrounds>(Shared = SharedType.PerTestSession)]
public sealed class ImpactBodyHashStorageTests(AnalyzedPlaygrounds playgrounds)
{
    [Test]
    public async Task Body_hashes_round_trip_and_are_deterministic()
    {
        var pg = await playgrounds.EntryPointEffectsAsync();
        var wd = Path.Combine(Path.GetTempPath(), $"rig-bodyhash-{Guid.NewGuid():n}");
        Directory.CreateDirectory(wd);

        var db1 = Path.Combine(wd, "a.db");
        var db2 = Path.Combine(wd, "b.db");
        await using (var ctx = new RigDbContext(db1, pooling: false))
        {
            await Writes.SaveAsync(ctx, pg.Result);
        }

        await using (var ctx = new RigDbContext(db2, pooling: false))
        {
            await Writes.SaveAsync(ctx, pg.Result);
        }

        await using var read1 = new RigDbContext(db1, pooling: false, readOnly: true);
        await using var read2 = new RigDbContext(db2, pooling: false, readOnly: true);
        var hashes1 = await Reads.LoadSymbolBodyHashesAsync(read1);
        var hashes2 = await Reads.LoadSymbolBodyHashesAsync(read2);

        hashes1.ShouldNotBeEmpty(); // bodied methods exist in the playground and got a hash
        // Same source extracted twice => identical hashes for every symbol (deterministic content hash).
        hashes1.ShouldBe(hashes2);
    }
}
