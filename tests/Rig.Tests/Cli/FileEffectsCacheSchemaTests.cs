using Rig.Cli.Caching;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class FileEffectsCacheSchemaTests
{
    [Test]
    public void File_effect_cache_and_browser_derivation_are_gated_by_the_same_schema()
    {
        // Pinned on purpose: a schema bump must be a deliberate edit here, not a silent side effect.
        QueryCacheKeys.FileEffectsSchema.ShouldBe(4);
        QueryCacheKeys
            .FileEffectsCacheKey("store", "rules", "/repo/A.cs")
            .ShouldBe("2F3C6DCFC470D443BDD13651BE64780A72712C882C6933DD3A8FC29FB01B5295");
        QueryCacheKeys.DerivationSchemaToken().Split('.').Last().ShouldBe(QueryCacheKeys.FileEffectsSchema.ToString());
    }
}
