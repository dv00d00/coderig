using System.Text.Json;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;

namespace Rig.Tests.Live;

public sealed class LiveCallersDemandPreparationTests
{
    private static readonly FactHandoffRule Handoff = new("background.schedule", "background", [".Schedule"]);
    private static readonly FactRedirectRule Redirect = new("M:External.Save", "M:App.Base.Save");
    private static readonly FactGenericFactoryRule Factory = new("M:App.Factory.New", 0, "New");
    private static readonly FactTraversalCutRule Cut = new("M:App.Seam.*", "seam");
    private static readonly FactContextDispatchRule Context = new("IState", "StateBase");
    private static readonly RuleSet Rules = new()
    {
        Handoff = [Handoff],
        Redirect = [Redirect],
        Factory = [Factory],
        Cut = [Cut],
        Context = [Context],
    };

    [Test]
    public void Text_callers_uses_the_whole_trimmed_remainder_and_default_sync_shape()
    {
        var demand = LiveQueryRunner
            .PrepareTextExactDemand("  callers   \"M:App.Target(System.Int32, System.String)\"  ", Rules, deploymentsConfigured: false)
            .ShouldBeOfType<ExactCallersDemand>();

        demand.ToPattern.ShouldBe("M:App.Target(System.Int32, System.String)");
        demand.ExecutionMode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        demand.DiscoveryMode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        demand.MaxDepth.ShouldBe(int.MaxValue);
        demand.DebtScope.ShouldBe(ExactForwardDebtScope.DemandBoundary);
        demand.Rules.Projection.ClassifyEventSubscriptions.ShouldBeTrue();

        LiveQueryRunner.PrepareTextExactDemand("callers", Rules, deploymentsConfigured: false).ShouldBeNull();
        LiveQueryRunner.PrepareTextExactDemand("callers   ", Rules, deploymentsConfigured: false).ShouldBeNull();
        LiveQueryRunner.PrepareTextExactDemand("callers \"\"", Rules, deploymentsConfigured: false).ShouldBeNull();
        LiveQueryRunner.PrepareTextExactDemand("callers \"   \"", Rules, deploymentsConfigured: false).ShouldBeNull();
    }

    [Test]
    public void Ordinary_transport_callers_preserves_depth_and_shaped_rules()
    {
        var demand = Demand(Options(depth: 7));

        demand.ToPattern.ShouldBe("App.Target");
        demand.MaxDepth.ShouldBe(7);
        demand.ExecutionMode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        demand.DiscoveryMode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        demand.DebtScope.ShouldBe(ExactForwardDebtScope.DemandBoundary);
        demand.Rules.Projection.Handoff.ShouldBe([Handoff]);
        demand.Rules.Projection.Redirect.ShouldBe([Redirect]);
        demand.Rules.Projection.Factory.ShouldBe([Factory]);
        demand.Rules.Cut.ShouldBe([Cut]);
        demand.Rules.Context.ShouldBe([Context]);
    }

    [Test]
    public void Human_sync_entrypoints_discovers_async_but_executes_sync_and_pays_whole_resident_debt()
    {
        var demand = Demand(Options(entrypoints: true));

        demand.ExecutionMode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        demand.DiscoveryMode.ShouldBe(FactPathFinder.TraversalMode.AsyncExact);
        demand.DebtScope.ShouldBe(ExactForwardDebtScope.WholeResident);
    }

    [Test]
    public void Tsv_entrypoints_stays_sync_while_deployments_make_ordinary_callers_whole_resident()
    {
        var tsv = Demand(Options(entrypoints: true, format: "tsv"));
        var deployed = Demand(Options(), deploymentsConfigured: true);

        tsv.ExecutionMode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        tsv.DiscoveryMode.ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        tsv.DebtScope.ShouldBe(ExactForwardDebtScope.WholeResident);
        deployed.DebtScope.ShouldBe(ExactForwardDebtScope.WholeResident);
    }

    [Test]
    public void Raw_callers_keeps_handoff_and_redirect_but_removes_other_shaping_and_normalizes_null_rules()
    {
        var demand = Demand(Options(raw: true) with { ExtraRules = null! });

        demand.Rules.Projection.Handoff.ShouldBe([Handoff]);
        demand.Rules.Projection.Redirect.ShouldBe([Redirect]);
        demand.Rules.Projection.Factory.ShouldBeEmpty();
        demand.Rules.Projection.ClassifyEventSubscriptions.ShouldBeFalse();
        demand.Rules.Cut.ShouldBeEmpty();
        demand.Rules.Context.ShouldBeEmpty();
    }

    [Test]
    public async Task Null_extra_rules_normalizes_for_preparation_and_execution()
    {
        var facts = new LiveFactSource(new AnalysisResult("Exact.sln", [], []), Rules);
        var request = Request(Options(format: "tsv") with { ExtraRules = null! });

        LiveQueryRunner.PrepareTransportExactDemand(request, Rules, deploymentsConfigured: false).ShouldNotBeNull();
        var result = await LiveQueryRunner.RunRequestAsync(request, facts, "/repo");

        result.DeclineReason.ShouldBeNull();
        result.Answer.ShouldNotBeNull();
    }

    [Test]
    public async Task Unsupported_callers_options_decline_without_preparing_refinement()
    {
        var facts = new LiveFactSource(new AnalysisResult("Exact.sln", [], []), Rules);
        var invalid = new[]
        {
            Options() with
            {
                ExtraRules = ["custom.json"],
            },
            Options(asyncMode: true),
            Options(includeDelivery: true),
            Options(roots: true, entrypoints: true),
            Options(depth: -1),
            Options(limit: 0),
            Options(to: "  "),
        };

        foreach (var options in invalid)
        {
            var request = Request(options);
            LiveQueryRunner.PrepareTransportExactDemand(request, Rules, deploymentsConfigured: false).ShouldBeNull();

            var result = await LiveQueryRunner.RunRequestAsync(request, facts, "/repo");
            result.Answer.ShouldBeNull();
            result.DeclineReason.ShouldNotBeNull();
        }
    }

    [Test]
    public async Task Null_transport_options_and_whitespace_text_decline_instead_of_bypassing_refinement()
    {
        var facts = new LiveFactSource(new AnalysisResult("Exact.sln", [], []), Rules);
        var request = new LiveQueryRequest(LiveQueryTransport.Protocol, LiveQueryVerbs.Callers, "/repo", null!);

        LiveQueryRunner.PrepareTransportExactDemand(request, Rules, deploymentsConfigured: false).ShouldBeNull();
        (await LiveQueryRunner.RunRequestAsync(request, facts, "/repo")).DeclineReason.ShouldNotBeNull();

        var text = await LiveQueryRunner.AnswerAsync("callers \"   \"", facts, "/repo");
        text.Exit.ShouldBe(2);
        text.Out.ShouldContain("`callers` needs a target pattern");
    }

    [Test]
    public void Exact_unavailable_names_callers()
    {
        var answer = LiveQueryRunner.ExactUnavailable("callers", 23, "reverse ownership is ambiguous");

        answer.Exit.ShouldBe(2);
        answer.Err.ShouldContain("exact callers unavailable");
        answer.Err.ShouldContain("revision 23");
    }

    private static ExactCallersDemand Demand(CallersCommand.Options options, bool deploymentsConfigured = false) =>
        LiveQueryRunner.PrepareTransportExactDemand(Request(options), Rules, deploymentsConfigured).ShouldBeOfType<ExactCallersDemand>();

    private static LiveQueryRequest Request(CallersCommand.Options options) =>
        new(LiveQueryTransport.Protocol, LiveQueryVerbs.Callers, "/repo", JsonSerializer.Serialize(options, LiveQueryTransport.Json));

    private static CallersCommand.Options Options(
        string to = "App.Target",
        bool roots = false,
        bool entrypoints = false,
        bool asyncMode = false,
        bool includeDelivery = false,
        bool raw = false,
        int? depth = null,
        string? format = null,
        int? limit = null
    ) =>
        new(
            ToPattern: to,
            RootsOnly: roots,
            EntrypointsOnly: entrypoints,
            IncludeReverseOnly: false,
            Async: asyncMode,
            IncludeDelivery: includeDelivery,
            Raw: raw,
            ExtraRules: [],
            Depth: depth,
            Format: format,
            Limit: limit,
            Time: false
        );
}
