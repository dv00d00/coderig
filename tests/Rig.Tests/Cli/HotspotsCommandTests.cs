using Rig.Cli.Commands;
using Rig.Cli.Caching;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class HotspotsCommandTests
{
    [Test]
    [Arguments("callers", "callers")]
    [Arguments("callees", "callees")]
    [Arguments("effects", "effects")]
    [Arguments("density", "density")]
    [Arguments("hazards", "hazards")]
    [Arguments("amplification", "amplification")]
    [Arguments("dispatch", "dispatch")]
    public void Every_sort_uses_its_named_metric(string sort, string expected)
    {
        var rows = new[]
        {
            Row("baseline"),
            Row("callers", callers: 9),
            Row("callees", callees: 9),
            Row("effects", effects: 9),
            Row("density", density: 99),
            Row("hazards", hazards: 9),
            Row("amplification", amplification: 9),
            Row("dispatch", dispatchRank: 99),
        };

        HotspotsCommand.Order(rows, sort)[0].Id.ShouldBe(expected);
    }

    [Test]
    public void Ties_are_broken_by_id_for_deterministic_output()
    {
        HotspotsCommand.Order([Row("z", effects: 2), Row("a", effects: 2)], "effects").Select(r => r.Id).ShouldBe(["a", "z"]);
    }

    [Test]
    public void Selection_excludes_generated_by_default_keeps_lambdas_unless_requested_and_applies_top()
    {
        var rows = new[]
        {
            Row("generated", effects: 99, generated: true),
            Row("lambda", effects: 8, lambda: true),
            Row("ordinary", effects: 7),
        };

        HotspotsCommand.SelectRows(rows, "effects", top: 1, noLambdas: false).Select(r => r.Id).ShouldBe(["lambda"]);
        HotspotsCommand.SelectRows(rows, "effects", top: 5, noLambdas: true).Select(r => r.Id).ShouldBe(["ordinary"]);
    }

    [Test]
    public void Tsv_has_a_named_header_and_invariant_transparent_columns()
    {
        var output = new StringWriter();
        HotspotsCommand.WriteTsv(output, [Row("M:N.Work", callers: 2, effects: 3, density: 12.5, hazards: 1)]);

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldStartWith("id\tname\tfile\tline\tlines\tcaller_methods\tincoming_call_sites");
        lines[0].ShouldContain("effect_sites_per_100_lines");
        lines[0].ShouldContain("dispatch_rank");
        lines[1].ShouldContain("M:N.Work\tWork\tWork.cs\t10\t20\t2");
        lines[1].ShouldContain("\t3\t1\t12.5\t1\t1\t");
    }

    [Test]
    public void Cache_key_distinguishes_intrinsic_effect_scope()
    {
        QueryCacheKeys.HotspotsCacheKey("store", "rules", intrinsic: false)
            .ShouldNotBe(QueryCacheKeys.HotspotsCacheKey("store", "rules", intrinsic: true));
    }

    private static FactHotspotReport.Row Row(
        string id,
        int callers = 0,
        int callees = 0,
        int effects = 0,
        double density = 0,
        int hazards = 0,
        int amplification = 0,
        long dispatchRank = 0,
        bool generated = false,
        bool lambda = false
    ) =>
        new(
            Id: id,
            Name: id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id,
            File: "Work.cs",
            Line: 10,
            Lines: 20,
            CallerMethods: callers,
            IncomingCallSites: callers,
            CalleeMethods: callees,
            OutgoingCallSites: callees,
            EffectSites: effects,
            EffectKinds: effects > 0 ? 1 : 0,
            EffectSitesPer100Lines: density,
            HazardSites: hazards,
            HazardKinds: hazards > 0 ? 1 : 0,
            AmplificationSites: amplification,
            ResidualDispatchFan: dispatchRank > 0 ? 3 : 0,
            DispatchIncomingEdges: dispatchRank > 0 ? 2 : 0,
            DispatchRank: dispatchRank,
            IsGenerated: generated,
            IsLambda: lambda
        );
}
