using Rig.Cli.Caching;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class FileEffectsCacheSchemaTests
{
    [Test]
    public void File_effect_cache_and_browser_derivation_are_gated_by_the_same_schema()
    {
        // Pinned on purpose: a schema bump must be a deliberate edit here, not a silent side effect.
        QueryCacheKeys.FileEffectsSchema.ShouldBe(5);
        QueryCacheKeys
            .FileEffectsCacheKey("store", "rules", "/repo/A.cs")
            .ShouldBe("1624A5DD70727668AE31545ED1F5C3F323EBE9BFDF28184E95D3C80D04405325");
        QueryCacheKeys.DerivationSchemaToken().Split('.').Last().ShouldBe(QueryCacheKeys.FileEffectsSchema.ToString());
    }
}
