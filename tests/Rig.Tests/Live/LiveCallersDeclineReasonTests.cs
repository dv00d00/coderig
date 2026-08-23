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
    [Arguments(true, false, "resident `callers` does not yet support `--async`; this traversal mode requires the immutable store")]
    [Arguments(
        false,
        true,
        "resident `callers` does not yet support `--include-delivery`; this traversal mode requires the immutable store"
    )]
    [Arguments(
        true,
        true,
        "resident `callers` does not yet support `--async` and `--include-delivery`; these traversal modes require the immutable store"
    )]
    public async Task Valid_store_only_traversal_modes_name_the_unsupported_live_capability(
        bool asyncMode,
        bool includeDelivery,
        string expectedReason
    )
    {
        var result = await LiveQueryRunner.RunRequestAsync(
            Request(Options(asyncMode: asyncMode, includeDelivery: includeDelivery)),
            Facts(),
            "/repo"
        );

        result.Answer.ShouldBeNull();
        result.DeclineReason.ShouldBe(expectedReason);
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

    private static CallersCommand.Options Options(bool asyncMode = false, bool includeDelivery = false) =>
        new(
            ToPattern: "App.Target",
            RootsOnly: false,
            EntrypointsOnly: false,
            IncludeReverseOnly: false,
            Async: asyncMode,
            IncludeDelivery: includeDelivery,
            Raw: false,
            ExtraRules: [],
            Depth: null,
            Format: "tsv",
            Limit: null,
            Time: false
        );
}
