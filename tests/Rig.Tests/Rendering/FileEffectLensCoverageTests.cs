using Rig.Cli.Rendering;
using Rig.Cli.Services;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Rendering;

public sealed class FileEffectLensCoverageTests
{
    [Test]
    public void Coverage_distinguishes_present_from_requested_but_absent_families()
    {
        var lens = Project(
            requested: ["rpc", "db", "io", "cache", "db"],
            methods: [new FileEffectMethod("M:Demo.Work.Run", [new FileEffectAggregate("db", 1)])],
            sites: [new FileEffectCallSite("M:Demo.Work.Run", "M:Demo.Remote.Send", 12, [new FileEffectAggregate("rpc", 0)])]
        );

        lens.RequestedFamilies.ShouldBe(["cache", "db", "io", "rpc"]);
        lens.PresentFamilies.ShouldBe(["db", "rpc"]);
        lens.AbsentRequestedFamilies.ShouldBe(["cache", "io"]);
        lens.Families.ShouldBe(lens.RequestedFamilies);
    }

    [Test]
    public void Coverage_reports_every_requested_family_absent_when_no_badges_are_present()
    {
        var lens = Project(requested: ["io", "cache", "db"]);

        lens.RequestedFamilies.ShouldBe(["cache", "db", "io"]);
        lens.PresentFamilies.ShouldBeEmpty();
        lens.AbsentRequestedFamilies.ShouldBe(["cache", "db", "io"]);
    }

    [Test]
    public void Present_order_is_derived_independently_and_keeps_an_observed_unrequested_family()
    {
        var lens = Project(
            requested: ["rpc", "db"],
            methods:
            [
                new FileEffectMethod(
                    "M:Demo.Work.Run",
                    [new FileEffectAggregate("io", 2), new FileEffectAggregate("db", 0)]
                ),
            ]
        );

        lens.RequestedFamilies.ShouldBe(["db", "rpc"]);
        lens.PresentFamilies.ShouldBe(["db", "io"]);
        lens.AbsentRequestedFamilies.ShouldBe(["rpc"]);
    }

    private static FileEffectLens.LensModel Project(
        IReadOnlyList<string> requested,
        IReadOnlyList<FileEffectMethod>? methods = null,
        IReadOnlyList<FileEffectCallSite>? sites = null
    ) =>
        FileEffectLens.Project(
            new FileEffectsQueryService.Artifact(
                new FileEffectReadModel("src/Work.cs", requested, methods ?? [], sites ?? []),
                new Dictionary<string, FileEffectsQueryService.MethodLocation>(StringComparer.Ordinal)
            )
        );
}
