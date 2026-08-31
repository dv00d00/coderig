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

        var first = firstGeneration.FileEffects(sql);
        var equivalent = firstGeneration.FileEffects(reorderedSql);
        var different = firstGeneration.FileEffects(new FileEffectSelector("cache", [new EffectPredicate("cache")]));
        var nextGeneration = new LiveFactSource(facts, new RuleSet()).FileEffects(sql);

        equivalent.ShouldBeSameAs(first);
        different.ShouldNotBeSameAs(first);
        nextGeneration.ShouldNotBeSameAs(first);
        firstGeneration.BuildTimes.Count(build => build.Artifact == "fileEffects[sql]").ShouldBe(1);
        firstGeneration.BuildTimes.Count(build => build.Artifact == "fileEffects[cache]").ShouldBe(1);
    }
}
