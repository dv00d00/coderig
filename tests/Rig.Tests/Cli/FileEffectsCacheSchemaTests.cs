using Rig.Cli.Caching;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class FileEffectsCacheSchemaTests
{
    [Test]
    public void File_effect_cache_and_browser_derivation_are_gated_by_the_same_schema()
    {
        // Pinned on purpose: a schema bump must be a deliberate edit here, not a silent side effect.
        QueryCacheKeys.FileEffectsSchema.ShouldBe(6);
        QueryCacheKeys
            .FileEffectsCacheKey("store", "rules", "/repo/A.cs")
            .ShouldBe("0ABFD748F1AD8FBE6671CBD28134F1B7A8EA637A1B13899D3E456BF4ACBC675C");
        QueryCacheKeys.DerivationSchemaToken().Split('.').Last().ShouldBe(QueryCacheKeys.FileEffectsSchema.ToString());
    }
}
