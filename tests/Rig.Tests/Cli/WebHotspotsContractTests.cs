using System.Text.Json;
using Rig.Cli.Services;
using Rig.Cli.Web;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class WebHotspotsContractTests
{
    [Test]
    public void Request_defaults_are_stable_and_match_the_cli_report()
    {
        var valid = HotspotsEndpoint.TryValidate(sort: null, top: null, noLambdas: null, intrinsic: null, out var request, out var error);

        valid.ShouldBeTrue();
        error.ShouldBeNull();
        request.ShouldNotBeNull();
        request.Sort.ShouldBe("density");
        request.Top.ShouldBe(50);
        request.NoLambdas.ShouldBeFalse();
        request.Intrinsic.ShouldBeFalse();
    }

    [Test]
    public void Every_named_sort_is_accepted_and_normalized()
    {
        string[] sorts = ["callers", "callees", "effects", "density", "hazards", "amplification", "dispatch"];

        foreach (var sort in sorts)
        {
            HotspotsEndpoint.TryValidate(sort.ToUpperInvariant(), 1, false, false, out var request, out var error).ShouldBeTrue();
            error.ShouldBeNull();
            request.ShouldNotBeNull().Sort.ShouldBe(sort);
        }
    }

    [Test]
    [Arguments("mystery", 50, "Invalid sort 'mystery'.")]
    [Arguments("density", 0, "Invalid top '0'.")]
    [Arguments("density", 501, "Invalid top '501'.")]
    public void Invalid_arguments_have_a_stable_bounded_400_contract(string sort, int top, string message)
    {
        var valid = HotspotsEndpoint.TryValidate(sort, top, noLambdas: false, intrinsic: false, out var request, out var error);

        valid.ShouldBeFalse();
        request.ShouldBeNull();
        error.ShouldNotBeNull();
        error.Error.ShouldStartWith(message);
        error.AllowedSorts.ShouldBe(["callers", "callees", "effects", "density", "hazards", "amplification", "dispatch"]);
        error.MinTop.ShouldBe(1);
        error.MaxTop.ShouldBe(500);
    }

    [Test]
    public void Response_reuses_cli_selection_and_carries_every_transparent_metric()
    {
        var artifact = new HotspotsQueryService.HotspotArtifact(
            Rows:
            [
                Row("M:Generated", effects: 999, generated: true),
                Row("M:Lambda", effects: 998, lambda: true),
                Row("M:App.Service.Lower", effects: 15),
                new FactHotspotReport.Row(
                    Id: "M:App.Service.Run",
                    Name: "App.Service.Run",
                    File: "Service.cs",
                    Line: 7,
                    Lines: 11,
                    CallerMethods: 12,
                    IncomingCallSites: 13,
                    CalleeMethods: 14,
                    OutgoingCallSites: 15,
                    EffectSites: 16,
                    EffectKinds: 17,
                    EffectSitesPer100Lines: 18.5,
                    HazardSites: 19,
                    HazardKinds: 20,
                    AmplificationSites: 21,
                    ResidualDispatchFan: 22,
                    DispatchIncomingEdges: 23,
                    DispatchRank: 506,
                    IsGenerated: false,
                    IsLambda: false
                ),
            ],
            HiddenIntrinsic: 24
        );
        var request = new HotspotsEndpoint.Request("effects", Top: 1, NoLambdas: true, Intrinsic: false);

        var response = HotspotsEndpoint.ToResponse(artifact, request);

        response.Sort.ShouldBe("effects");
        response.Top.ShouldBe(1);
        response.NoLambdas.ShouldBeTrue();
        response.Intrinsic.ShouldBeFalse();
        response.HiddenIntrinsic.ShouldBe(24);
        var row = response.Rows.ShouldHaveSingleItem();
        row.Id.ShouldBe("M:App.Service.Run");
        row.Name.ShouldBe("App.Service.Run");
        row.File.ShouldBe("Service.cs");
        row.Line.ShouldBe(7);
        row.Lines.ShouldBe(11);
        row.CallerMethods.ShouldBe(12);
        row.IncomingCallSites.ShouldBe(13);
        row.CalleeMethods.ShouldBe(14);
        row.OutgoingCallSites.ShouldBe(15);
        row.EffectSites.ShouldBe(16);
        row.EffectKinds.ShouldBe(17);
        row.EffectSitesPer100Lines.ShouldBe(18.5);
        row.HazardSites.ShouldBe(19);
        row.HazardKinds.ShouldBe(20);
        row.AmplificationSites.ShouldBe(21);
        row.ResidualDispatchFan.ShouldBe(22);
        row.DispatchIncomingEdges.ShouldBe(23);
        row.DispatchRank.ShouldBe(506);
        row.IsGenerated.ShouldBeFalse();
        row.IsLambda.ShouldBeFalse();
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.ShouldContain("\"hiddenIntrinsic\":24");
        json.ShouldContain("\"incomingCallSites\":13");
        json.ShouldNotContain("\"score\"");
    }

    [Test]
    public void Effects_diff_contract_exposes_explicit_a_and_b_without_a_peer_discovery_field()
    {
        var response = new EffectsDiffResponseDto(
            Label: "explicit pair",
            Matched: false,
            A: new EffectsDiffTargetDto("M:App.A", "matched", "M:App.A", ["App.A"]),
            B: new EffectsDiffTargetDto("M:App.B", "no-match", null, []),
            Common: [],
            AOnly: [],
            BOnly: [],
            Error: "No symbol matches 'M:App.B' (b)."
        );

        response.A.Pattern.ShouldBe("M:App.A");
        response.B.Pattern.ShouldBe("M:App.B");
        var fields = typeof(EffectsDiffResponseDto).GetProperties().Select(p => p.Name).ToArray();
        fields.ShouldContain("A");
        fields.ShouldContain("B");
        fields.ShouldNotContain("Peer");
        fields.ShouldNotContain("SuggestedPeer");
        fields.ShouldNotContain("AutoDiscovery");
    }

    private static FactHotspotReport.Row Row(string id, int effects, bool generated = false, bool lambda = false) =>
        new(
            Id: id,
            Name: id,
            File: "fixture.cs",
            Line: 1,
            Lines: 1,
            CallerMethods: 0,
            IncomingCallSites: 0,
            CalleeMethods: 0,
            OutgoingCallSites: 0,
            EffectSites: effects,
            EffectKinds: effects,
            EffectSitesPer100Lines: effects,
            HazardSites: 0,
            HazardKinds: 0,
            AmplificationSites: 0,
            ResidualDispatchFan: 0,
            DispatchIncomingEdges: 0,
            DispatchRank: 0,
            IsGenerated: generated,
            IsLambda: lambda
        );
}
