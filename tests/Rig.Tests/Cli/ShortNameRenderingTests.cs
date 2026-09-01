using Rig.Cli.Rendering;
using Rig.Cli.Services;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class ShortNameRenderingTests
{
    [Test]
    [Arguments("M:App.TypedListExtension.Fill``1(App.TypedList,``0)", "TypedListExtension.Fill<T>")]
    [Arguments("M:App.Router.fromConfig``2(``0,``1)", "Router.fromConfig<T, U>")]
    [Arguments("M:App.Cache`2.Get``1(`0,`1,``0)", "Cache<T, U>.Get<T>")]
    [Arguments(
        "M:App.Outer{System.String,System.Collections.Generic.List{System.Int32}}.Run()",
        "Outer<String, List<Int32>>.Run"
    )]
    [Arguments("M:App.Worker.Run(System.String)", "Worker.Run")]
    public void Short_names_render_generic_arity_as_csharp_placeholders(string symbolId, string expected)
    {
        SymbolNameFormatter.ShortName(symbolId).ShouldBe(expected);
    }

    [Test]
    public void File_effect_site_targets_use_the_same_human_readable_short_name()
    {
        var model = new FileEffectReadModel(
            "src/Caller.cs",
            ["db"],
            [],
            [
                new FileEffectCallSite(
                    "M:App.Caller.Run()",
                    "M:App.TypedListExtension.Fill``1(App.TypedList,``0)",
                    42,
                    [new FileEffectAggregate("db", 1)]
                ),
            ]
        );
        var artifact = new FileEffectsQueryService.Artifact(
            model,
            new Dictionary<string, FileEffectsQueryService.MethodLocation>(StringComparer.Ordinal)
        );

        var line = FileEffectLens.Project(artifact).Lines.Single();

        line.Targets.ShouldBe(["TypedListExtension.Fill<T>"]);
    }
}
