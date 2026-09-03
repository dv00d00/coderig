using System.Text.Json;
using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Domain.Functions;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Live;

[NotInParallel]
public sealed class DemandLivePathTests
{
    [Test]
    public async Task Deep_chain_live_path_matches_store_for_positive_negative_missing_and_depth_cases()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        await using var host = await StartAsync(playground.WorkingDirectory, playground.SolutionPath);

        await AssertParityAsync(host, playground.WorkingDirectory, ["path", "HomePage.Show", "Db.Query"]);
        await AssertParityAsync(host, playground.WorkingDirectory, ["path", "Db.Query", "HomePage.Show"]);
        await AssertParityAsync(host, playground.WorkingDirectory, ["path", "HomePage.Show", "NoSuchSymbol"]);
        await AssertParityAsync(host, playground.WorkingDirectory, ["path", "HomePage.Show", "Db.Query", "--depth", "1"]);
    }

    [Test]
    public async Task Generic_interface_and_async_delivery_live_paths_match_store()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        await using var host = await StartAsync(playground.WorkingDirectory, playground.SolutionPath);

        var dispatch = await AssertParityAsync(
            host,
            playground.WorkingDirectory,
            ["path", "TeamsController.CreateViaInterface", "TeamRepository.AddAsync"]
        );
        var payment = await AssertParityAsync(
            host,
            playground.WorkingDirectory,
            ["path", "PaymentGatewayCaller.Dispatch", "PaymentGatewayProcess.Ask"]
        );
        var syncEvent = await AssertParityAsync(
            host,
            playground.WorkingDirectory,
            ["path", "NotificationsController.Subscribe", "AuditSink.WriteAuditEntry"]
        );
        var asyncEvent = await AssertParityAsync(
            host,
            playground.WorkingDirectory,
            ["path", "NotificationsController.Subscribe", "AuditSink.WriteAuditEntry", "--async"]
        );
        syncEvent.Exit.ShouldBe(1);
        asyncEvent.Exit.ShouldBe(0);

        // MATERIALIZE-ONCE (2026-08-24), asserted as the invariant it actually is: the live `path` banner
        // reports the GENERATION's whole graph, so every query in the generation reports the SAME numbers —
        // three different seed pairs and both traversal modes above. A regression to per-query projection
        // (the pre-2026-08-24 behaviour) makes these diverge immediately, because each seed expands a
        // different closure; `--async` reporting the same count is the separate half of the claim, that
        // delivery edges are folded in unconditionally rather than costing a second materialization.
        //
        // REPLACED 2026-09-03 a `ShouldBeGreaterThan(new LiveFactSource(await host.GetCurrentFactsAsync(),
        // rules).TraversalGraph.CallEdges.Count)`, which had been failing 44-vs-87 deterministically. That
        // oracle compares two DIFFERENT FACT MODELS: GetCurrentFactsAsync returns `.FlattenedFacts`
        // (WatchCommand.cs:947), so it built the legacy whole-graph projection — the arm
        // Flattened_fact_compatibility_is_explicitly_diagnosed_as_legacy_fallback below pins as
        // LegacyWholeGraphFallback and which WatchHost never takes — while the banner reports the KEYED
        // resident snapshot's materialized graph. There is no superset relation between them, and the more
        // precise keyed projection carrying FEWER edges is the expected direction, not a regression.
        var banners = new[] { dispatch, payment, syncEvent, asyncEvent }.Select(r => FactGraphCallEdgeCount(r.Out)).ToArray();

        banners.ShouldAllBe(count => count > 0);
        banners.Distinct().Count().ShouldBe(1, $"materialize-once means one graph per generation; got {string.Join(", ", banners)}");
    }

    [Test]
    public async Task Flattened_fact_compatibility_is_explicitly_diagnosed_as_legacy_fallback()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        await using var host = await StartAsync(playground.WorkingDirectory, playground.SolutionPath);
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);
        var flattened = new LiveFactSource(await host.GetCurrentFactsAsync(), rules);
        var source = (IDemandForwardPathFactSource)new LiveQueryFactSource(flattened);

        var result = await source.LoadDemandForwardPathGraphAsync(
            "HomePage.Show",
            rules,
            int.MaxValue,
            FactPathFinder.TraversalMode.SyncCut,
            classifyEventSubscriptions: true
        );

        result.Diagnostics.Load.Mode.ShouldBe(DemandForwardLoadMode.LegacyWholeGraphFallback);
        result.Diagnostics.Load.UsedLegacyFallback.ShouldBeTrue();
        result.EventSubscriptionsClassified.ShouldBeFalse();
        flattened.BuildTimes.ShouldContain(build => build.Artifact == "traversalGraph");
    }

    private static async Task<WatchHost> StartAsync(string workingDirectory, string solutionPath)
    {
        var indexLog = new StringWriter();
        (await CliApplication.RunAsync(["index", solutionPath], indexLog, indexLog, workingDirectory)).ShouldBe(0, indexLog.ToString());
        var rules = RuleSetLoader.Load(workingDirectory);
        return await WatchHost.StartAsync(
            solutionPath,
            rules,
            buildCacheDir: null,
            output: new StringWriter(),
            watch: false,
            workingDirectory: workingDirectory
        );
    }

    private static async Task<LiveServeResult> AssertParityAsync(WatchHost host, string workingDirectory, string[] arguments)
    {
        var storeOut = new StringWriter();
        var storeErr = new StringWriter();
        var storeExit = await CliApplication.RunAsync(arguments, storeOut, storeErr, workingDirectory);

        var live = await ServeAsync(host, workingDirectory, arguments);

        live.DeclineReason.ShouldBeNull();
        live.Exit.ShouldBe(storeExit);
        WithoutBanner(live.Out).ShouldBe(WithoutBanner(storeOut.ToString()));
        // Store provenance is intentionally absent from a resident answer, and the host's per-generation cost
        // note is its twin on the live side (CHANGED 2026-08-24: materializing the graph means the FIRST query
        // of a generation now genuinely builds traversalGraph/eventSites and honestly says so, so the note is
        // no longer always empty). Every answer-local diagnostic must still match byte-for-byte.
        AnswerStreamParity
            .WithoutLiveCostDisclosure(live.Err)
            .ShouldBe(AnswerStreamParity.WithoutImmutableStoreDisclosure(storeErr.ToString()));
        return live;
    }

    private static Task<LiveServeResult> ServeAsync(WatchHost host, string workingDirectory, string[] arguments)
    {
        var opts = new PathCommand.Options(
            FromPattern: arguments[1],
            ToPattern: arguments[2],
            Async: arguments.Contains("--async", StringComparer.Ordinal),
            IncludeDelivery: false,
            Raw: false,
            ExtraRules: [],
            Depth: Depth(arguments),
            Format: null,
            Time: false
        );
        return host.ServeAsync(
            new LiveQueryRequest(
                Protocol: LiveQueryTransport.Protocol,
                Verb: LiveQueryVerbs.Path,
                WorkingDirectory: Path.GetFullPath(workingDirectory),
                Options: JsonSerializer.Serialize(opts, LiveQueryTransport.Json)
            )
        );
    }

    private static int? Depth(string[] arguments)
    {
        var index = Array.IndexOf(arguments, "--depth");
        return index < 0 ? null : int.Parse(arguments[index + 1], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string WithoutBanner(string value) =>
        Fixtures.AnswerStreamParity.Canonical(
            string.Join(
                "\n",
                Fixtures
                    .AnswerStreamParity.Canonical(value)
                    .Split('\n')
                    .Where(line => !line.StartsWith("Fact graph:", StringComparison.Ordinal))
            )
        );

    private static int FactGraphCallEdgeCount(string value)
    {
        var line = value.Split('\n').Single(item => item.StartsWith("Fact graph:", StringComparison.Ordinal));
        var start = "Fact graph: ".Length;
        var end = line.IndexOf(" call edges", StringComparison.Ordinal);
        return int.Parse(line[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }
}
