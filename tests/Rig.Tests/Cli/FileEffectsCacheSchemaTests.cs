using Rig.Cli.Caching;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class FileEffectsCacheSchemaTests
{
    [Test]
    public void File_effect_cache_and_browser_derivation_are_gated_by_the_same_schema()
    {
        // Pinned on purpose: a schema bump must be a deliberate edit here, not a silent side effect.
        QueryCacheKeys.FileEffectsSchema.ShouldBe(3);
        QueryCacheKeys
            .FileEffectsCacheKey("store", "rules", "/repo/A.cs")
            .ShouldBe("B61862FF47AB83281DEE04BB6E6F3FF49697E180AD336AB57A37C2A6068A5428");
        QueryCacheKeys.DerivationSchemaToken().Split('.').Last().ShouldBe(QueryCacheKeys.FileEffectsSchema.ToString());
    }
}
