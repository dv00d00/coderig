using System.Text.Json;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Live;

public sealed class LiveCallersDeclineReasonTests
{
    private static readonly RuleSet Rules = new();

    [Test]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Async_traversal_modes_decline_when_only_flattened_compatibility_facts_exist(bool asyncMode, bool includeDelivery)
    {
        var facts = Facts();
        var result = await LiveQueryRunner.RunRequestAsync(
            Request(Options(asyncMode: asyncMode, includeDelivery: includeDelivery)),
            facts,
            "/repo"
        );

        result.Answer.ShouldBeNull();
        result.DeclineReason!.ShouldContain("flattened compatibility facts cannot project delivery exactly");
        facts.BuildTimes.ShouldBeEmpty();
    }

    [Test]
    public async Task Include_only_keeps_sync_compatibility_semantics()
    {
        var facts = Facts();
        var result = await LiveQueryRunner.RunRequestAsync(Request(Options(asyncMode: false, includeDelivery: true)), facts, "/repo");

        result.DeclineReason.ShouldBeNull();
        result.Answer.ShouldNotBeNull();
        facts.BuildTimes.ShouldContain(build => build.Artifact == "traversalGraph");
    }

    [Test]
    public async Task Sync_human_entrypoints_can_use_async_discovery_with_flattened_compatibility_facts()
    {
        var facts = Facts();
        var result = await LiveQueryRunner.RunRequestAsync(Request(Options(entrypointsOnly: true, format: null)), facts, "/repo");

        result.DeclineReason.ShouldBeNull();
        result.Answer.ShouldNotBeNull();
        facts.BuildTimes.ShouldContain(build => build.Artifact == "traversalGraph");
    }

    [Test]
    public async Task Malformed_callers_options_remain_an_unreadable_options_decline()
    {
        var malformedJson = new LiveQueryRequest(LiveQueryTransport.Protocol, LiveQueryVerbs.Callers, "/repo", "{");
        var invalidShape = Request(Options() with { ToPattern = " " });

        (await LiveQueryRunner.RunRequestAsync(malformedJson, Facts(), "/repo")).DeclineReason.ShouldBe("unreadable options for `callers`");
        (await LiveQueryRunner.RunRequestAsync(invalidShape, Facts(), "/repo")).DeclineReason.ShouldBe("unreadable options for `callers`");
    }

    [Test]
    public async Task Default_callers_options_are_still_served()
    {
        var result = await LiveQueryRunner.RunRequestAsync(Request(Options()), Facts(), "/repo");

        result.DeclineReason.ShouldBeNull();
        result.Answer.ShouldNotBeNull();
    }

    [Test]
    public async Task Extra_rules_keep_the_rules_specific_decline()
    {
        var result = await LiveQueryRunner.RunRequestAsync(
            Request(Options(asyncMode: true) with { ExtraRules = ["extra.rules.json"] }),
            Facts(),
            "/repo"
        );

        result.Answer.ShouldBeNull();
        result.DeclineReason.ShouldStartWith("--rules is not honoured by the resident index");
    }

    private static LiveFactSource Facts() => new(new AnalysisResult("Callers.sln", [], []), Rules);

    private static LiveQueryRequest Request(CallersCommand.Options options) =>
        new(LiveQueryTransport.Protocol, LiveQueryVerbs.Callers, "/repo", JsonSerializer.Serialize(options, LiveQueryTransport.Json));

    private static CallersCommand.Options Options(
        bool asyncMode = false,
        bool includeDelivery = false,
        bool entrypointsOnly = false,
        string? format = "tsv"
    ) =>
        new(
            ToPattern: "App.Target",
            RootsOnly: false,
            EntrypointsOnly: entrypointsOnly,
            IncludeReverseOnly: false,
            Async: asyncMode,
            IncludeDelivery: includeDelivery,
            Raw: false,
            ExtraRules: [],
            Depth: null,
            Format: format,
            Limit: null,
            Time: false
        );
}
