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
        var demand = LiveQueryRunner.PrepareTextForwardDemand(
            "path \"M:App.From(System.Int32, System.String)\" App.To",
            Rules,
            deploymentsConfigured: false
        );

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
        LiveQueryRunner.PrepareTextForwardDemand("", Rules, deploymentsConfigured: false).ShouldBeNull();
        LiveQueryRunner.PrepareTextForwardDemand("callers App.From", Rules, deploymentsConfigured: false).ShouldBeNull();
        LiveQueryRunner.PrepareTextForwardDemand("path only-one", Rules, deploymentsConfigured: false).ShouldBeNull();
        LiveQueryRunner.PrepareTextForwardDemand("path one two three", Rules, deploymentsConfigured: false).ShouldBeNull();
        LiveQueryRunner.PrepareTextForwardDemand("path \"unclosed one two", Rules, deploymentsConfigured: false).ShouldBeNull();
        LiveQueryRunner.PrepareTextForwardDemand("path A \"B", Rules, deploymentsConfigured: false).ShouldBeNull();
    }

    [Test]
    public void Valid_transport_path_preserves_raw_and_depth_shape()
    {
        var options = Options(from: "App.From", to: "App.To", raw: true, depth: 7);
        var demand = LiveQueryRunner.PrepareTransportForwardDemand(
            Request(LiveQueryVerbs.Path, options),
            Rules,
            deploymentsConfigured: false
        );

        demand.ShouldNotBeNull();
        demand.FromPattern.ShouldBe("App.From");
        demand.ToPattern.ShouldBe("App.To");
        demand.Mode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        demand.MaxDepth.ShouldBe(7);
        demand.Rules.Projection.ClassifyEventSubscriptions.ShouldBeFalse();
        demand.Rules.Cut.ShouldBeEmpty();
        demand.Rules.Context.ShouldBeEmpty();
    }

    [Test]
    public void Unsupported_malformed_or_extra_rules_transport_never_prepares_refinement()
    {
        LiveQueryRunner
            .PrepareTransportForwardDemand(Request("callers", Options("A", "B")), Rules, deploymentsConfigured: false)
            .ShouldBeNull();
        LiveQueryRunner
            .PrepareTransportForwardDemand(
                new LiveQueryRequest(LiveQueryTransport.Protocol, LiveQueryVerbs.Path, "/repo", "{"),
                Rules,
                deploymentsConfigured: false
            )
            .ShouldBeNull();
        LiveQueryRunner
            .PrepareTransportForwardDemand(
                Request(LiveQueryVerbs.Path, Options("A", "B") with { ExtraRules = ["custom.json"] }),
                Rules,
                deploymentsConfigured: false
            )
            .ShouldBeNull();
        LiveQueryRunner
            .PrepareTransportForwardDemand(Request(LiveQueryVerbs.Path, Options("", "B")), Rules, deploymentsConfigured: false)
            .ShouldBeNull();
    }

    [Test]
    public async Task Transport_preparation_and_execution_share_endpoint_and_rules_validation()
    {
        var facts = new LiveFactSource(new AnalysisResult("Exact.sln", [], []), Rules);
        var nullRules = Options("A", "B") with { ExtraRules = null! };
        var blankEndpoint = Options("", "B");

        LiveQueryRunner
            .PrepareTransportForwardDemand(Request(LiveQueryVerbs.Path, nullRules), Rules, deploymentsConfigured: false)
            .ShouldNotBeNull();
        var nullRulesResult = await LiveQueryRunner.RunRequestAsync(Request(LiveQueryVerbs.Path, nullRules), facts, "/repo");
        nullRulesResult.DeclineReason.ShouldBeNull();
        nullRulesResult.Answer.ShouldNotBeNull();

        LiveQueryRunner
            .PrepareTransportForwardDemand(Request(LiveQueryVerbs.Path, blankEndpoint), Rules, deploymentsConfigured: false)
            .ShouldBeNull();
        var blankResult = await LiveQueryRunner.RunRequestAsync(Request(LiveQueryVerbs.Path, blankEndpoint), facts, "/repo");
        blankResult.Answer.ShouldBeNull();
        blankResult.DeclineReason.ShouldNotBeNull();
    }

    [Test]
    // The STDIN-loop rendering contract, which is not the transport policy: a routed request that cannot be
    // prepared now DECLINES so the client reads the store, while `rig watch`'s own terminal loop has no store
    // to fall back to and still renders this exit-2 answer for the person running the watcher.
    public void Exact_unavailable_renders_an_exit_two_answer_for_the_stdin_loop()
    {
        var answer = LiveQueryRunner.ExactUnavailable("path", 42, "generated ownership collision");

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
