using System.Text.Json;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Live;

public sealed class LivePathDemandPreparationTests
{
    private static readonly RuleSet Rules = new();

    [Test]
    public void Valid_text_path_prepares_exactly_two_quoted_endpoints_before_refinement()
    {
        var demand = LiveQueryRunner.PrepareTextPathDemand("path \"M:App.From(System.Int32, System.String)\" App.To", Rules);

        demand.ShouldNotBeNull();
        demand.FromPattern.ShouldBe("M:App.From(System.Int32, System.String)");
        demand.ToPattern.ShouldBe("App.To");
        demand.Mode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        demand.MaxDepth.ShouldBe(int.MaxValue);
        demand.Rules.Projection.ClassifyEventSubscriptions.ShouldBeTrue();
    }

    [Test]
    public void Invalid_or_non_path_text_never_prepares_refinement()
    {
        LiveQueryRunner.PrepareTextPathDemand("", Rules).ShouldBeNull();
        LiveQueryRunner.PrepareTextPathDemand("reaches App.From", Rules).ShouldBeNull();
        LiveQueryRunner.PrepareTextPathDemand("path only-one", Rules).ShouldBeNull();
        LiveQueryRunner.PrepareTextPathDemand("path one two three", Rules).ShouldBeNull();
        LiveQueryRunner.PrepareTextPathDemand("path \"unclosed one two", Rules).ShouldBeNull();
        LiveQueryRunner.PrepareTextPathDemand("path A \"B", Rules).ShouldBeNull();
    }

    [Test]
    public void Valid_transport_path_preserves_raw_async_delivery_and_depth_shape()
    {
        var options = Options(from: "App.From", to: "App.To", async: true, includeDelivery: true, raw: true, depth: 7);
        var demand = LiveQueryRunner.PrepareTransportPathDemand(Request(LiveQueryVerbs.Path, options), Rules);

        demand.ShouldNotBeNull();
        demand.FromPattern.ShouldBe("App.From");
        demand.ToPattern.ShouldBe("App.To");
        demand.Mode.ShouldBe(FactPathFinder.TraversalMode.AsyncInclude);
        demand.MaxDepth.ShouldBe(7);
        demand.Rules.Projection.ClassifyEventSubscriptions.ShouldBeFalse();
        demand.Rules.Cut.ShouldBeEmpty();
        demand.Rules.Context.ShouldBeEmpty();
    }

    [Test]
    public void Unsupported_malformed_or_extra_rules_transport_never_prepares_refinement()
    {
        LiveQueryRunner.PrepareTransportPathDemand(Request("tree", Options("A", "B")), Rules).ShouldBeNull();
        LiveQueryRunner
            .PrepareTransportPathDemand(new LiveQueryRequest(LiveQueryTransport.Protocol, LiveQueryVerbs.Path, "/repo", "{"), Rules)
            .ShouldBeNull();
        LiveQueryRunner
            .PrepareTransportPathDemand(Request(LiveQueryVerbs.Path, Options("A", "B") with { ExtraRules = ["custom.json"] }), Rules)
            .ShouldBeNull();
        LiveQueryRunner.PrepareTransportPathDemand(Request(LiveQueryVerbs.Path, Options("", "B")), Rules).ShouldBeNull();
    }

    [Test]
    public async Task Transport_preparation_and_execution_share_endpoint_and_rules_validation()
    {
        var facts = new LiveFactSource(new AnalysisResult("Exact.sln", [], []), Rules);
        var nullRules = Options("A", "B") with { ExtraRules = null! };
        var blankEndpoint = Options("", "B");

        LiveQueryRunner.PrepareTransportPathDemand(Request(LiveQueryVerbs.Path, nullRules), Rules).ShouldNotBeNull();
        var nullRulesResult = await LiveQueryRunner.RunRequestAsync(Request(LiveQueryVerbs.Path, nullRules), facts, "/repo");
        nullRulesResult.DeclineReason.ShouldBeNull();
        nullRulesResult.Answer.ShouldNotBeNull();

        LiveQueryRunner.PrepareTransportPathDemand(Request(LiveQueryVerbs.Path, blankEndpoint), Rules).ShouldBeNull();
        var blankResult = await LiveQueryRunner.RunRequestAsync(Request(LiveQueryVerbs.Path, blankEndpoint), facts, "/repo");
        blankResult.Answer.ShouldBeNull();
        blankResult.DeclineReason.ShouldNotBeNull();
    }

    [Test]
    public void Exact_unavailable_is_an_answered_exit_two_not_a_transport_decline()
    {
        var answer = LiveQueryRunner.ExactUnavailable(42, "generated ownership collision");

        answer.Exit.ShouldBe(2);
        answer.Out.ShouldBeEmpty();
        answer.Err.ShouldContain("revision 42");
        answer.Err.ShouldContain("generated ownership collision");
        WatchHost.TopologyStatusSegment.ShouldContain("restart required");
        WatchHost.TopologyStatusSegment.ShouldNotContain("all projects reconciled");
    }

    private static LiveQueryRequest Request(string verb, PathCommand.Options options) =>
        new(LiveQueryTransport.Protocol, verb, "/repo", JsonSerializer.Serialize(options, LiveQueryTransport.Json));

    private static PathCommand.Options Options(
        string from,
        string to,
        bool async = false,
        bool includeDelivery = false,
        bool raw = false,
        int? depth = null
    ) => new(from, to, async, includeDelivery, raw, [], depth, Format: null, Time: false);
}
