using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Live;

public sealed class LiveAsyncDemandExecutionTests
{
    private const string Producer = "M:N.Publisher.Raise";
    private const string Register = "M:N.Wiring.Register";
    private const string Handler = "M:N.Handler.Handle";
    private const string Event = "E:N.Publisher.Changed";

    // CHANGED 2026-08-24 (live materialize-once): the delivery-aware graph is no longer projected per query
    // off keyed partitions — it is the generation's ONE materialized graph, with the delivery edges folded in
    // the way the store folds them into `call_edges`. The load-bearing assertions are untouched: async
    // reaches/tree still cross the producer -> handler delivery hop and still land on Handler.Handle, which is
    // the only way to know the materialized graph really carries delivery edges.
    [Test]
    public async Task Reaches_and_tree_execute_the_delivery_aware_materialized_graph()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution);
        var rules = RuleSetLoader.Load("/repo");
        var live = new LiveFactSource(snapshot, rules);

        var reaches = await LiveQueryRunner.RunRequestAsync(Request(LiveQueryVerbs.Reaches, Reaches()), live, "/repo");
        var tree = await LiveQueryRunner.RunRequestAsync(Request(LiveQueryVerbs.Tree, Tree()), live, "/repo");

        reaches.DeclineReason.ShouldBeNull();
        reaches.Answer!.Exit.ShouldBe(0, reaches.Answer.Text);
        reaches.Answer.Out.ShouldContain("Reachable methods (<= depth 2147483647): 2");
        tree.DeclineReason.ShouldBeNull();
        tree.Answer!.Exit.ShouldBe(0, tree.Answer.Text);
        tree.Answer.Out.ShouldContain("Handler.Handle");
        // The flattened AnalysisResult is still never forced, and the TWO async queries share ONE graph.
        snapshot.FullMaterializationCount.ShouldBe(0);
        snapshot.ProjectedCallGraphCount.ShouldBe(1);
        live.BuildTimes.Count(build => build.Artifact == "traversalGraph").ShouldBe(1);
    }

    [Test]
    public async Task Repeated_async_execution_stays_on_the_same_materialized_generation_and_flattened_async_declines()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution);
        var rules = RuleSetLoader.Load("/repo");
        var live = new LiveFactSource(snapshot, rules);
        var request = Request(LiveQueryVerbs.Reaches, Reaches());

        (await LiveQueryRunner.RunRequestAsync(request, live, "/repo")).DeclineReason.ShouldBeNull();
        (await LiveQueryRunner.RunRequestAsync(request, live, "/repo")).DeclineReason.ShouldBeNull();

        snapshot.FullMaterializationCount.ShouldBe(0);
        live.BuildTimes.Count(build => build.Artifact == "epData").ShouldBe(1);
        live.BuildTimes.Count(build => build.Artifact == "invocations").ShouldBe(1);
        // ONCE per generation, not once per query — that is the whole point of materializing.
        live.BuildTimes.Count(build => build.Artifact == "traversalGraph").ShouldBe(1);
        snapshot.ProjectedCallGraphCount.ShouldBe(1);

        var flattened = new LiveFactSource(Facts(), rules);
        var declined = await LiveQueryRunner.RunRequestAsync(request, flattened, "/repo");
        declined.Answer.ShouldBeNull();
        declined.DeclineReason!.ShouldContain("flattened compatibility facts");
        flattened.BuildTimes.ShouldBeEmpty();

        var text = await LiveQueryRunner.AnswerAsync($"reaches {Producer} --async", flattened, "/repo");
        text.Exit.ShouldBe(2);
        text.Out.ShouldContain("demand projection is unavailable");
        flattened.BuildTimes.ShouldBeEmpty();
    }

    [Test]
    public async Task Async_tree_partial_sidecar_hit_reuses_the_materialized_delivery_graph()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution);
        var rules = RuleSetLoader.Load("/repo");
        var live = new LiveFactSource(snapshot, rules);

        var filterA = await LiveQueryRunner.RunRequestAsync(Request(LiveQueryVerbs.Tree, Tree(only: "db")), live, "/repo");
        var filterB = await LiveQueryRunner.RunRequestAsync(Request(LiveQueryVerbs.Tree, Tree(only: "cache")), live, "/repo");

        filterA.DeclineReason.ShouldBeNull();
        filterA.Answer!.Out.ShouldContain("Handler.Handle");
        filterB.DeclineReason.ShouldBeNull();
        filterB.Answer!.Out.ShouldContain("Handler.Handle");
        snapshot.FullMaterializationCount.ShouldBe(0);
        snapshot.ProjectedCallGraphCount.ShouldBe(1);
        live.BuildTimes.Count(build => build.Artifact == "traversalGraph").ShouldBe(1);
    }

    private static LiveQueryRequest Request<T>(string verb, T options) =>
        new(LiveQueryTransport.Protocol, verb, "/repo", JsonSerializer.Serialize(options, LiveQueryTransport.Json));

    private static ReachesCommand.Options Reaches() =>
        new(
            FromPattern: Producer,
            Async: true,
            IncludeDelivery: false,
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

    private static TreeCommand.Options Tree(string? only = null) =>
        new(
            FromPattern: Producer,
            View: "full",
            Async: true,
            IncludeDelivery: false,
            Raw: false,
            Files: false,
            Signatures: false,
            Plain: true,
            Guards: false,
            ExtraRules: [],
            Depth: null,
            Limit: null,
            Only: CommonOptions.FilterSet(only is null ? null : [only]),
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

    private static FactSnapshot Snapshot(Solution solution) =>
        new(new FactRevision(0), solution, Facts(), ImmutableDictionary<string, FileFacts>.Empty, DirtySet.Empty, SnapshotDelta.Empty);

    private static AnalysisResult Facts() =>
        new(
            SolutionPath: "/repo/App.sln",
            SourceFiles: [],
            DiRegistrations: [],
            Symbols:
            [
                Method(Producer, "Raise", "T:N.Publisher"),
                Method(Register, "Register", "T:N.Wiring"),
                Method(Handler, "Handle", "T:N.Handler"),
            ],
            References:
            [
                Reference(Producer, Event, RefKinds.Read, 10),
                Reference(Register, Event, RefKinds.Read, 20),
                Reference(Register, Handler, RefKinds.MethodGroup, 20),
            ],
            TypeRelations: [],
            DispatchFacts: [],
            AllocationFacts: []
        );

    private static SymbolFact Method(string id, string name, string containingType) =>
        new(id, SymbolKinds.Method, name, "N", containingType, "public", "", $"{name}()", "/repo/App.cs", 1, 1, "App", false);

    private static ReferenceFact Reference(string caller, string target, string kind, int line) =>
        new(target, kind, caller, "App", TargetInSource: true, "/repo/App.cs", line);
}
