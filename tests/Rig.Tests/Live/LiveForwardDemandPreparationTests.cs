using System.Text.Json;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Live;

public sealed class LiveForwardDemandPreparationTests
{
    private static readonly FactGenericFactoryRule Factory = new("N.Factory.Create", 0, "Create");
    private static readonly RuleSet Rules = new() { Factory = [Factory] };

    [Test]
    public void Default_text_reaches_and_tree_prepare_forward_demands_but_invalid_text_does_not()
    {
        var reaches = LiveQueryRunner.PrepareTextForwardDemand("reaches App.Start", Rules, deploymentsConfigured: false);
        var tree = LiveQueryRunner.PrepareTextForwardDemand("tree \"M:App.Start(System.Int32)\"", Rules, deploymentsConfigured: false);

        reaches.ShouldNotBeNull();
        reaches.QueryKind.ShouldBe(ExactForwardQueryKind.Reaches);
        reaches.FromPattern.ShouldBe("App.Start");
        reaches.ToPattern.ShouldBeNull();
        reaches.DebtScope.ShouldBe(ExactForwardDebtScope.DemandBoundary);
        reaches.Rules.Projection.Factory.ShouldBe([Factory]);
        tree.ShouldNotBeNull();
        tree.QueryKind.ShouldBe(ExactForwardQueryKind.Tree);
        tree.FromPattern.ShouldBe("M:App.Start(System.Int32)");
        tree.ToPattern.ShouldBeNull();

        LiveQueryRunner.PrepareTextForwardDemand("reaches", Rules, deploymentsConfigured: false).ShouldBeNull();
        LiveQueryRunner.PrepareTextForwardDemand("reaches   ", Rules, deploymentsConfigured: false).ShouldBeNull();
        LiveQueryRunner.PrepareTextForwardDemand("tree \"\"", Rules, deploymentsConfigured: false).ShouldBeNull();
        LiveQueryRunner.PrepareTextForwardDemand("callers App.Start", Rules, deploymentsConfigured: false).ShouldBeNull();
    }

    [Test]
    public void Transport_raw_shaping_preserves_reaches_factory_but_removes_tree_factory()
    {
        var reaches = LiveQueryRunner.PrepareTransportForwardDemand(
            Request(LiveQueryVerbs.Reaches, Reaches(from: "App.Start", raw: true, depth: 4)),
            Rules,
            deploymentsConfigured: false
        );
        var tree = LiveQueryRunner.PrepareTransportForwardDemand(
            Request(LiveQueryVerbs.Tree, Tree(from: "App.Start", raw: true, depth: 9, limit: 1)),
            Rules,
            deploymentsConfigured: false
        );

        reaches.ShouldNotBeNull();
        reaches.Mode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        reaches.MaxDepth.ShouldBe(4);
        reaches.Rules.Projection.Factory.ShouldBe([Factory]);
        reaches.Rules.Projection.ClassifyEventSubscriptions.ShouldBeFalse();
        reaches.Rules.Cut.ShouldBeEmpty();
        reaches.Rules.Context.ShouldBeEmpty();
        tree.ShouldNotBeNull();
        tree.Mode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        tree.MaxDepth.ShouldBe(9, "tree --limit is presentation-only and must not cap exact planning");
        tree.Rules.Projection.Factory.ShouldBeEmpty();
        tree.Rules.Projection.ClassifyEventSubscriptions.ShouldBeFalse();
    }

    [Test]
    public async Task Async_forward_transport_prepares_but_flattened_execution_declines_while_include_only_stays_sync()
    {
        var facts = new LiveFactSource(new AnalysisResult("Exact.sln", [], []), Rules);
        var asyncRequests = new[]
        {
            Request(LiveQueryVerbs.Path, Path(asyncMode: true)),
            Request(LiveQueryVerbs.Reaches, Reaches("App.Start", asyncMode: true)),
            Request(LiveQueryVerbs.Tree, Tree("App.Start", asyncMode: true)),
        };

        foreach (var request in asyncRequests)
        {
            LiveQueryRunner.PrepareTransportForwardDemand(request, Rules, deploymentsConfigured: false).ShouldNotBeNull();
            var result = await LiveQueryRunner.RunRequestAsync(request, facts, "/repo");
            result.Answer.ShouldBeNull();
            result.DeclineReason!.ShouldContain("flattened compatibility facts");
        }

        var includeOnlyRequests = new[]
        {
            Request(LiveQueryVerbs.Path, Path(delivery: true)),
            Request(LiveQueryVerbs.Reaches, Reaches("App.Start", delivery: true)),
            Request(LiveQueryVerbs.Tree, Tree("App.Start", delivery: true)),
        };
        foreach (var request in includeOnlyRequests)
        {
            var demand = LiveQueryRunner.PrepareTransportForwardDemand(request, Rules, deploymentsConfigured: false).ShouldNotBeNull();
            demand.Mode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
            (await LiveQueryRunner.RunRequestAsync(request, facts, "/repo")).DeclineReason.ShouldBeNull();
        }
    }

    [Test]
    public void Hazard_view_and_deployment_backed_forward_queries_pay_whole_resident_debt()
    {
        var hazards = LiveQueryRunner.PrepareTransportForwardDemand(
            Request(LiveQueryVerbs.Tree, Tree("App.Start") with { View = "hazards" }),
            Rules,
            deploymentsConfigured: false
        );
        var deployedReaches = LiveQueryRunner.PrepareTransportForwardDemand(
            Request(LiveQueryVerbs.Reaches, Reaches("App.Start")),
            Rules,
            deploymentsConfigured: true
        );
        var deployedPath = LiveQueryRunner.PrepareTransportForwardDemand(
            Request(LiveQueryVerbs.Path, Path()),
            Rules,
            deploymentsConfigured: true
        );
        var deployedTree = LiveQueryRunner.PrepareTransportForwardDemand(
            Request(LiveQueryVerbs.Tree, Tree("App.Start")),
            Rules,
            deploymentsConfigured: true
        );
        var ordinaryTree = LiveQueryRunner.PrepareTransportForwardDemand(
            Request(LiveQueryVerbs.Tree, Tree("App.Start")),
            Rules,
            deploymentsConfigured: false
        );

        hazards!.DebtScope.ShouldBe(ExactForwardDebtScope.WholeResident);
        deployedReaches!.DebtScope.ShouldBe(ExactForwardDebtScope.WholeResident);
        deployedPath!.DebtScope.ShouldBe(ExactForwardDebtScope.WholeResident);
        deployedTree!.DebtScope.ShouldBe(ExactForwardDebtScope.WholeResident);
        ordinaryTree!.DebtScope.ShouldBe(ExactForwardDebtScope.DemandBoundary);
    }

    [Test]
    public async Task Transport_preparation_and_execution_share_blank_and_extra_rules_validation()
    {
        var facts = new LiveFactSource(new AnalysisResult("Exact.sln", [], []), Rules);
        var nullRules = Reaches("App.Start") with { ExtraRules = null! };
        var extraRules = Tree("App.Start") with { ExtraRules = ["custom.json"] };
        var blank = Reaches("  ");

        LiveQueryRunner
            .PrepareTransportForwardDemand(Request(LiveQueryVerbs.Reaches, nullRules), Rules, deploymentsConfigured: false)
            .ShouldNotBeNull();
        (await LiveQueryRunner.RunRequestAsync(Request(LiveQueryVerbs.Reaches, nullRules), facts, "/repo")).DeclineReason.ShouldBeNull();

        LiveQueryRunner
            .PrepareTransportForwardDemand(Request(LiveQueryVerbs.Tree, extraRules), Rules, deploymentsConfigured: false)
            .ShouldBeNull();
        (await LiveQueryRunner.RunRequestAsync(Request(LiveQueryVerbs.Tree, extraRules), facts, "/repo")).DeclineReason.ShouldNotBeNull();

        LiveQueryRunner
            .PrepareTransportForwardDemand(Request(LiveQueryVerbs.Reaches, blank), Rules, deploymentsConfigured: false)
            .ShouldBeNull();
        (await LiveQueryRunner.RunRequestAsync(Request(LiveQueryVerbs.Reaches, blank), facts, "/repo")).DeclineReason.ShouldNotBeNull();
    }

    [Test]
    public async Task Tree_transport_normalizes_nullable_collections_and_declines_invalid_shape()
    {
        var facts = new LiveFactSource(new AnalysisResult("Exact.sln", [], []), Rules);
        var nullableCollections = Tree("App.Start") with { ExtraRules = null!, Only = null!, Exclude = null!, ExcludeNamespaces = null! };
        var invalid = new[]
        {
            Request(LiveQueryVerbs.Tree, Tree("App.Start") with { View = null! }),
            Request(LiveQueryVerbs.Tree, Tree("App.Start") with { View = "unknown" }),
            Request(LiveQueryVerbs.Tree, Tree("App.Start") with { Depth = -1 }),
            Request(LiveQueryVerbs.Tree, Tree("App.Start") with { Limit = 0 }),
            new LiveQueryRequest(LiveQueryTransport.Protocol, LiveQueryVerbs.Tree, "/repo", "{"),
        };

        LiveQueryRunner
            .PrepareTransportForwardDemand(Request(LiveQueryVerbs.Tree, nullableCollections), Rules, deploymentsConfigured: false)
            .ShouldNotBeNull();
        (
            await LiveQueryRunner.RunRequestAsync(Request(LiveQueryVerbs.Tree, nullableCollections), facts, "/repo")
        ).DeclineReason.ShouldBeNull();

        foreach (var request in invalid)
        {
            LiveQueryRunner.PrepareTransportForwardDemand(request, Rules, deploymentsConfigured: false).ShouldBeNull();
            (await LiveQueryRunner.RunRequestAsync(request, facts, "/repo")).DeclineReason.ShouldNotBeNull();
        }
    }

    [Test]
    public void Exact_unavailable_names_the_requested_forward_verb()
    {
        foreach (var verb in new[] { "path", "reaches", "tree" })
        {
            var answer = LiveQueryRunner.ExactUnavailable(verb, 17, "not classifiable");

            answer.Exit.ShouldBe(2);
            answer.Err.ShouldContain($"exact {verb} unavailable");
            answer.Err.ShouldContain("revision 17");
        }
    }

    private static LiveQueryRequest Request<T>(string verb, T options) =>
        new(LiveQueryTransport.Protocol, verb, "/repo", JsonSerializer.Serialize(options, LiveQueryTransport.Json));

    private static ReachesCommand.Options Reaches(
        string from,
        bool raw = false,
        bool asyncMode = false,
        bool delivery = false,
        int? depth = null
    ) =>
        new(
            FromPattern: from,
            Async: asyncMode,
            IncludeDelivery: delivery,
            Raw: raw,
            ExtraRules: [],
            Depth: depth,
            Format: null,
            Only: CommonOptions.FilterSet(null),
            Exclude: CommonOptions.FilterSet(null),
            Intrinsic: false,
            Limit: null,
            Time: false
        );

    private static PathCommand.Options Path(bool asyncMode = false, bool delivery = false) =>
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

    private static TreeCommand.Options Tree(
        string from,
        bool raw = false,
        bool asyncMode = false,
        bool delivery = false,
        int? depth = null,
        int? limit = null
    ) =>
        new(
            FromPattern: from,
            View: "paths",
            Async: asyncMode,
            IncludeDelivery: delivery,
            Raw: raw,
            Files: false,
            Signatures: false,
            Plain: false,
            Guards: false,
            ExtraRules: [],
            Depth: depth,
            Limit: limit,
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
}
