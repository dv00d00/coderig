using System.Diagnostics;
using System.Text.Json;
using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Live;

[NotInParallel]
public sealed class LiveCallersExactIntegrationTests
{
    private const string Target = "Db.Query";
    private const string ExistingHighLevelCaller = "HomePage.Show";
    private const string NewCaller = "ReviewBooking";
    private const string LoadBanner = "Fact graph:";

    [Test]
    public async Task Routed_callers_refines_a_disk_edit_exactly_without_building_the_whole_traversal_graph()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var workingDirectory = playground.WorkingDirectory;

        await IndexAsync(playground.SolutionPath, workingDirectory);
        var rules = RuleSetLoader.Load(workingDirectory);
        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: rules,
            buildCacheDir: null,
            output: new StringWriter(),
            watch: true,
            workingDirectory: workingDirectory
        );

        var request = CallersRequest(workingDirectory);
        var initialStore = await RunStoreCallersAsync(workingDirectory);
        var initialLive = await host.ServeAsync(request);

        AssertAnsweredCallers(initialLive, ExistingHighLevelCaller);
        AssertParity(initialStore, initialLive);
        AssertNoWholeGraphBuild(initialLive);

        var beforeEditRevision = await host.GetCurrentRevisionAsync();
        var homePagePath = Path.Combine(workingDirectory, "Web", "HomePage.cs");
        var source = (await File.ReadAllTextAsync(homePagePath)).Replace("\r\n", "\n", StringComparison.Ordinal);
        const string MethodTail = "    public string Show() => _controller.Book(new Contracts.PatientDto { Id = 42, Name = \"Ada\" });\n}";
        source.ShouldContain(MethodTail);
        await File.WriteAllTextAsync(
            homePagePath,
            source.Replace(
                MethodTail,
                "    public string Show() => _controller.Book(new Contracts.PatientDto { Id = 42, Name = \"Ada\" });\n\n"
                    + "    public string ReviewBooking() => Show();\n}",
                StringComparison.Ordinal
            )
        );

        await WaitUntilAsync(
            async () => await host.GetCurrentRevisionAsync() > beforeEditRevision,
            TimeSpan.FromSeconds(60),
            "the watcher never published the HomePage.cs disk edit"
        );
        var eagerRevision = await host.GetCurrentRevisionAsync();
        (await host.GetUnreconciledProjectsAsync()).ShouldNotBeEmpty(
            "the edit should leave dependency debt for the exact callers query to admit"
        );

        var editedLive = await host.ServeAsync(request);
        AssertAnsweredCallers(editedLive, ExistingHighLevelCaller, NewCaller);
        editedLive.Disclosure.ShouldContain("all projects reconciled");
        editedLive.Disclosure.ShouldNotContain("affected facts STALE");
        AssertNoWholeGraphBuild(editedLive);

        var refinedRevision = await host.GetCurrentRevisionAsync();
        refinedRevision.ShouldBeGreaterThan(eagerRevision, "the first post-edit callers query should publish its exact refinement");

        var repeatedLive = await host.ServeAsync(request);
        AssertAnsweredCallers(repeatedLive, ExistingHighLevelCaller, NewCaller);
        repeatedLive.Out.ShouldBe(editedLive.Out);
        repeatedLive.Err.ShouldBe(editedLive.Err);
        AssertNoWholeGraphBuild(repeatedLive);
        (await host.GetCurrentRevisionAsync()).ShouldBe(
            refinedRevision,
            "repeating an already-exact callers query must not publish another resident generation"
        );

        await IndexAsync(playground.SolutionPath, workingDirectory);
        var editedStore = await RunStoreCallersAsync(workingDirectory);
        editedStore.Out.ShouldContain(NewCaller);
        AssertParity(editedStore, editedLive);

        var revisionBeforeAsyncQueries = await host.GetCurrentRevisionAsync();
        foreach (var mode in new[] { (Async: true, Delivery: false), (Async: false, Delivery: true), (Async: true, Delivery: true) })
        {
            var asyncStore = await RunStoreCallersAsync(workingDirectory, mode.Async, mode.Delivery);
            var asyncLive = await host.ServeAsync(CallersRequest(workingDirectory, mode.Async, mode.Delivery));

            AssertAnsweredCallers(asyncLive, ExistingHighLevelCaller, NewCaller);
            AssertParity(asyncStore, asyncLive);
            AssertNoWholeGraphBuild(asyncLive);
        }

        (await host.GetCurrentRevisionAsync()).ShouldBe(
            revisionBeforeAsyncQueries,
            "repeating callers in another traversal mode over an exact generation must not publish another revision"
        );
    }

    private static LiveQueryRequest CallersRequest(string workingDirectory, bool asyncMode = false, bool includeDelivery = false)
    {
        var options = new CallersCommand.Options(
            ToPattern: Target,
            RootsOnly: false,
            EntrypointsOnly: false,
            IncludeReverseOnly: false,
            Async: asyncMode,
            IncludeDelivery: includeDelivery,
            Raw: false,
            ExtraRules: [],
            Depth: null,
            Format: null,
            Limit: null,
            Time: false
        );
        return new LiveQueryRequest(
            Protocol: LiveQueryTransport.Protocol,
            Verb: LiveQueryVerbs.Callers,
            WorkingDirectory: Path.GetFullPath(workingDirectory),
            Options: JsonSerializer.Serialize(options, LiveQueryTransport.Json)
        );
    }

    private static async Task<CommandAnswer> RunStoreCallersAsync(
        string workingDirectory,
        bool asyncMode = false,
        bool includeDelivery = false
    )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var arguments = new List<string> { "callers", Target, "--no-live" };
        if (asyncMode)
        {
            arguments.Add("--async");
        }
        if (includeDelivery)
        {
            arguments.Add("--include-delivery");
        }
        var exit = await CliApplication.RunAsync(arguments.ToArray(), output, error, workingDirectory);
        return new CommandAnswer(exit, output.ToString(), error.ToString());
    }

    private static async Task IndexAsync(string solutionPath, string workingDirectory)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApplication.RunAsync(["index", solutionPath], output, error, workingDirectory);
        exit.ShouldBe(0, output.ToString() + error);
    }

    private static void AssertAnsweredCallers(LiveServeResult answer, params string[] requiredCallers)
    {
        answer.DeclineReason.ShouldBeNull();
        answer.Exit.ShouldBe(0, answer.Out + answer.Err);
        answer.Out.ShouldContain($"Methods that reach '{Target}'");
        foreach (var caller in requiredCallers)
        {
            answer.Out.ShouldContain(caller);
        }
    }

    private static void AssertParity(CommandAnswer store, LiveServeResult live)
    {
        store.Exit.ShouldBe(0, store.Out + store.Err);
        store.Out.ShouldContain($"Methods that reach '{Target}'");
        store.Out.ShouldContain(ExistingHighLevelCaller);
        live.Exit.ShouldBe(store.Exit);
        WithoutLoadBanner(live.Out).ShouldBe(WithoutLoadBanner(store.Out));
        AnswerStreamParity.Canonical(live.Err).ShouldBe(AnswerStreamParity.WithoutImmutableStoreDisclosure(store.Err));
    }

    private static void AssertNoWholeGraphBuild(LiveServeResult answer)
    {
        var disclosure = answer.Disclosure + Environment.NewLine + answer.Err;
        disclosure.ShouldNotContain("traversalGraph");
        disclosure.ShouldNotContain("eventSites");
    }

    private static string WithoutLoadBanner(string stream) =>
        string.Join("\n", stream.Split('\n').Where(line => !line.StartsWith(LoadBanner, StringComparison.Ordinal)));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string reason)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out after {timeout.TotalSeconds:F0}s: {reason}");
    }

    private sealed record CommandAnswer(int Exit, string Out, string Err);
}
