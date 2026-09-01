using Rig.Cli.Caching;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class FileEffectsCacheSchemaTests
{
    [Test]
    public void File_effect_cache_and_browser_derivation_are_gated_by_the_same_schema()
    {
        QueryCacheKeys.FileEffectsSchema.ShouldBe(2);
        QueryCacheKeys
            .FileEffectsCacheKey("store", "rules", "/repo/A.cs")
            .ShouldBe("0C31D6B9ADA8755244A0D6A59DF4B83D351CF6EE54620D40154056088ED8A93B");
        QueryCacheKeys.DerivationSchemaToken().Split('.').Last().ShouldBe(QueryCacheKeys.FileEffectsSchema.ToString());
    }
}
