using Rig.Cli.Caching;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class FileEffectsCacheSchemaTests
{
    [Test]
    public void File_effect_cache_and_browser_derivation_are_gated_by_the_same_schema()
    {
        QueryCacheKeys.FileEffectsSchema.ShouldBe(1);
        QueryCacheKeys
            .FileEffectsCacheKey("store", "rules", "/repo/A.cs")
            .ShouldBe("9B4025D340872C273380142F35A3C91D966B9018B15BA1CEBAFE371C7A100114");
        QueryCacheKeys.DerivationSchemaToken().Split('.').Last().ShouldBe(QueryCacheKeys.FileEffectsSchema.ToString());
    }
}
