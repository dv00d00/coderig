using System.Text.Json;
using Rig.Analysis.Inventory;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Live;

public sealed class LiveAsyncDemandPreparationTests
{
    private static readonly DeliveryRule EventDelivery = new(
        "event",
        "event_raise",
        "exact",
        new DeliveryEndpoint("event-symbol", "symbol"),
        new DeliveryEndpoint("event-symbol", "symbol")
    );
    private static readonly RuleSet Rules = new() { Delivery = [EventDelivery] };

    [Test]
    public void Text_async_modes_prepare_the_same_normalized_demands_as_transport()
    {
        var textExact = LiveQueryRunner
            .PrepareTextExactDemand("reaches App.Start --async", Rules, deploymentsConfigured: false)
            .ShouldBeOfType<ExactForwardDemand>();
        var textInclude = LiveQueryRunner
            .PrepareTextExactDemand("callers App.End --include-delivery --async", Rules, deploymentsConfigured: false)
            .ShouldBeOfType<ExactCallersDemand>();
        var transportExact = LiveQueryRunner
            .PrepareTransportExactDemand(Request(LiveQueryVerbs.Reaches, Reaches(asyncMode: true)), Rules, false)
            .ShouldBeOfType<ExactForwardDemand>();
        var transportInclude = LiveQueryRunner
            .PrepareTransportExactDemand(Request(LiveQueryVerbs.Callers, Callers(asyncMode: true, delivery: true)), Rules, false)
            .ShouldBeOfType<ExactCallersDemand>();

        textExact.Mode.ShouldBe(FactPathFinder.TraversalMode.AsyncExact);
        transportExact.Mode.ShouldBe(textExact.Mode);
        textInclude.ExecutionMode.ShouldBe(FactPathFinder.TraversalMode.AsyncInclude);
        transportInclude.ExecutionMode.ShouldBe(textInclude.ExecutionMode);
        textExact.Rules.Delivery.ShouldBe([EventDelivery]);
        textInclude.Rules.Delivery.ShouldBe([EventDelivery]);
    }

    [Test]
    public void Include_only_preserves_store_sync_semantics_while_repeated_or_misplaced_flags_prepare_no_refinement()
    {
        LiveQueryRunner
            .PrepareTextExactDemand("tree App.Start --include-delivery", Rules, false)
            .ShouldBeOfType<ExactForwardDemand>()
            .Mode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        LiveQueryRunner.PrepareTextExactDemand("path App.Start App.End --async --async", Rules, false).ShouldBeNull();
        LiveQueryRunner.PrepareTextExactDemand("path App.Start --async App.End", Rules, false).ShouldBeNull();
        LiveQueryRunner
            .PrepareTransportExactDemand(Request(LiveQueryVerbs.Path, Path(asyncMode: false, delivery: true)), Rules, false)
            .ShouldBeOfType<ExactForwardDemand>()
            .Mode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        LiveQueryRunner
            .PrepareTransportExactDemand(Request(LiveQueryVerbs.Callers, Callers(asyncMode: false, delivery: true)), Rules, false)
            .ShouldBeOfType<ExactCallersDemand>()
            .ExecutionMode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
    }

    [Test]
    public void All_four_transport_verbs_accept_exact_and_include_modes()
    {
        var exact = new LiveQueryRequest[]
        {
            Request(LiveQueryVerbs.Path, Path(asyncMode: true)),
            Request(LiveQueryVerbs.Reaches, Reaches(asyncMode: true)),
            Request(LiveQueryVerbs.Tree, Tree(asyncMode: true)),
            Request(LiveQueryVerbs.Callers, Callers(asyncMode: true)),
        };
        var include = new LiveQueryRequest[]
        {
            Request(LiveQueryVerbs.Path, Path(asyncMode: true, delivery: true)),
            Request(LiveQueryVerbs.Reaches, Reaches(asyncMode: true, delivery: true)),
            Request(LiveQueryVerbs.Tree, Tree(asyncMode: true, delivery: true)),
            Request(LiveQueryVerbs.Callers, Callers(asyncMode: true, delivery: true)),
        };

        exact.Select(request => LiveQueryRunner.PrepareTransportExactDemand(request, Rules, false)).ShouldAllBe(demand => demand != null);
        include.Select(request => LiveQueryRunner.PrepareTransportExactDemand(request, Rules, false)).ShouldAllBe(demand => demand != null);
    }

    [Test]
    public async Task Quoted_flag_like_suffix_remains_pattern_content_for_preparation_and_answer()
    {
        var prepared = LiveQueryRunner
            .PrepareTextExactDemand("reaches \"App.Start --async\"", Rules, false)
            .ShouldBeOfType<ExactForwardDemand>();
        prepared.FromPattern.ShouldBe("App.Start --async");
        prepared.Mode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);

        var facts = new LiveFactSource(new AnalysisResult("Exact.sln", [], []), Rules);
        var answer = await LiveQueryRunner.AnswerAsync("reaches \"App.Start --include-delivery\"", facts, "/repo");
        answer.Out.ShouldNotContain("traversal flags are malformed");
        facts.BuildTimes.ShouldContain(build => build.Artifact == "traversalGraph");
    }

    [Test]
    public async Task Malformed_duplicate_and_misplaced_text_flags_do_not_execute_refinement()
    {
        var facts = new LiveFactSource(new AnalysisResult("Exact.sln", [], []), Rules);

        foreach (
            var query in new[]
            {
                "reaches App.Start --async --async",
                "tree App.Start --include-delivery middle",
                "callers \"App.End --async",
            }
        )
        {
            var answer = await LiveQueryRunner.AnswerAsync(query, facts, "/repo");
            answer.Exit.ShouldBe(2);
            answer.Out.ShouldContain("traversal flags are malformed");
        }

        facts.BuildTimes.ShouldBeEmpty();
    }

    [Test]
    public async Task Include_only_text_answer_executes_as_the_store_compatible_sync_no_op()
    {
        var facts = new LiveFactSource(new AnalysisResult("Exact.sln", [], []), Rules);

        var answer = await LiveQueryRunner.AnswerAsync("reaches App.Start --include-delivery", facts, "/repo");

        answer.Out.ShouldNotContain("traversal flags are malformed");
        facts.BuildTimes.ShouldContain(build => build.Artifact == "traversalGraph");
    }

    private static LiveQueryRequest Request<T>(string verb, T options) =>
        new(LiveQueryTransport.Protocol, verb, "/repo", JsonSerializer.Serialize(options, LiveQueryTransport.Json));

    private static PathCommand.Options Path(bool asyncMode, bool delivery = false) =>
        new(
            FromPattern: "App.Start",
            ToPattern: "App.End",
            Async: asyncMode,
            IncludeDelivery: delivery,
            Raw: false,
            ExtraRules: [],
            Depth: null,
            Format: null,
            Time: false
        );

    private static ReachesCommand.Options Reaches(bool asyncMode, bool delivery = false) =>
        new(
            FromPattern: "App.Start",
            Async: asyncMode,
            IncludeDelivery: delivery,
            Raw: false,
            ExtraRules: [],
            Depth: null,
            Format: null,
            Only: CommonOptions.FilterSet(null),
            Exclude: CommonOptions.FilterSet(null),
            Intrinsic: false,
            Limit: null,
            Time: false
        );

    private static TreeCommand.Options Tree(bool asyncMode, bool delivery = false) =>
        new(
            FromPattern: "App.Start",
            View: "paths",
            Async: asyncMode,
            IncludeDelivery: delivery,
            Raw: false,
            Files: false,
            Signatures: false,
            Plain: false,
            Guards: false,
            ExtraRules: [],
            Depth: null,
            Limit: null,
            Only: CommonOptions.FilterSet(null),
            Exclude: CommonOptions.FilterSet(null),
            Intrinsic: false,
            ExcludeNamespaces: CommonOptions.NamespacePrefixes(null),
            NoCache: false,
            Gate: true,
            Amplification: true,
            Time: false,
            Format: null,
            Suppress: null
        );

    private static CallersCommand.Options Callers(bool asyncMode, bool delivery = false) =>
        new(
            ToPattern: "App.End",
            RootsOnly: false,
            EntrypointsOnly: false,
            IncludeReverseOnly: false,
            Async: asyncMode,
            IncludeDelivery: delivery,
            Raw: false,
            ExtraRules: [],
            Depth: null,
            Format: null,
            Limit: null,
            Time: false
        );
}
