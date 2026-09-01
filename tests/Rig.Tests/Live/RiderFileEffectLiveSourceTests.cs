using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Live;

public sealed class RiderFileEffectLiveSourceTests
{
    [Test]
    public void Memoizes_semantically_equivalent_selectors_only_within_one_fact_generation()
    {
        var facts = new AnalysisResult("Fixture.sln", [], []);
        var firstGeneration = new LiveFactSource(facts, new RuleSet());
        var sql = new FileEffectSelector("sql", [new EffectPredicate("ado", "read"), new EffectPredicate("ef", "write")]);
        var reorderedSql = new FileEffectSelector(
            "sql",
            [new EffectPredicate("ef", "write"), new EffectPredicate("ado", "read"), new EffectPredicate("ado", "read")]
        );

        var cache = new FileEffectSelector("cache", [new EffectPredicate("cache")]);

        var first = firstGeneration.FileEffects([sql]);
        var equivalent = firstGeneration.FileEffects([reorderedSql]);
        var different = firstGeneration.FileEffects([cache]);
        // The SET is the key, so the same families in the other order is the same index — rebuilding it would
        // cost a whole labelled traversal.
        var reorderedSet = firstGeneration.FileEffects([cache, sql]);
        var sameSet = firstGeneration.FileEffects([sql, cache]);
        var nextGeneration = new LiveFactSource(facts, new RuleSet()).FileEffects([sql]);

        equivalent.ShouldBeSameAs(first);
        different.ShouldNotBeSameAs(first);
        sameSet.ShouldBeSameAs(reorderedSet);
        sameSet.ShouldNotBeSameAs(first);
        nextGeneration.ShouldNotBeSameAs(first);
        firstGeneration.BuildTimes.Count(build => build.Artifact == "fileEffects[sql]").ShouldBe(1);
        firstGeneration.BuildTimes.Count(build => build.Artifact == "fileEffects[cache]").ShouldBe(1);
        firstGeneration.BuildTimes.Count(build => build.Artifact == "fileEffects[cache+sql]").ShouldBe(1);
    }
}
