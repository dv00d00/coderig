using Rig.Cli.Commands;
using Rig.Cli.Impact;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// Storage round-trip: a real playground extraction must EMIT first-party field/property read/write reference
// facts, and LoadFieldAccessRefsAsync must read them back keyed to an enclosing method — the substrate Phase 3
// unions into the reach. Pins that the read/write RefKinds exist + load + key correctly end to end.
[ClassDataSource<AnalyzedPlaygrounds>(Shared = SharedType.PerTestSession)]
public sealed class ImpactRefReachStorageTests(AnalyzedPlaygrounds playgrounds)
{
    [Test]
    public async Task Field_access_refs_round_trip_and_union_as_degenerate_nodes()
    {
        var pg = await playgrounds.EntryPointEffectsAsync();
        var db = Path.Combine(Path.GetTempPath(), $"rig-refreach-{Guid.NewGuid():n}.db");
        await using (var ctx = new RigDbContext(db, pooling: false))
        {
            await Writes.SaveAsync(ctx, pg.Result);
        }

        await using var read = new RigDbContext(db, pooling: false, readOnly: true);
        var refs = await Reads.LoadFieldAccessRefsAsync(read);

        // The playground has fields/properties accessed inside methods => first-party read/write refs exist,
        // each keyed to an enclosing method DocID and pointing at a first-party target.
        refs.ShouldNotBeEmpty();
        refs.ShouldAllBe(r => r.Enclosing != null);

        // The union step turns a reachable method's targets into `R:`-prefixed degenerate nodes.
        var anyEnclosing = refs.First(r => r.Enclosing != null).Enclosing!;
        var byEnclosing = refs.Where(r => r.Enclosing != null)
            .GroupBy(r => r.Enclosing!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(r => r.Target).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal
            );
        var union = ImpactEngine.RefTargetsFor(new HashSet<string>(StringComparer.Ordinal) { anyEnclosing }, byEnclosing);

        union.ShouldNotBeEmpty();
        union.ShouldAllBe(n => n.StartsWith("R:"));
    }
}
