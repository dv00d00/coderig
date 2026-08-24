using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Rig.Analysis.Inventory;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Tests.Live;

public sealed class DemandLiveCallersFactSourceTests
{
    private const string Root = "M:N.Root.Run";
    private const string Middle = "M:N.Middle.Run";
    private const string Target = "M:N.Target.Run";

    // CHANGED 2026-08-24 (live materialize-once): an indexed snapshot no longer projects a keyed reverse
    // graph per query. The keyed builder's fixed point re-derived the whole graph index once per pass, so on a
    // real monorepo it never terminated; the resident index now materializes the projected call graph ONCE per
    // generation and lets the shared FactPathFinder narrow it, the way the store path always has. The ANSWER
    // assertions below are unchanged — they are what this test is for; only the arm that produced it moved.
    [Test]
    public async Task Indexed_snapshot_answers_from_the_generation_materialized_graph_without_flattening_facts()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution, ChainFacts());
        var live = new LiveFactSource(snapshot, new RuleSet());
        var source = (IDemandReverseCallersFactSource)new LiveQueryFactSource(live);

        var result = await source.LoadDemandReverseCallersGraphAsync(Rules(classifyEvents: true), Request(Target));

        result.Diagnostics.Load.Mode.ShouldBe(DemandReverseLoadMode.MaterializedWholeGraph);
        result.Diagnostics.Load.UsedLegacyFallback.ShouldBeFalse();
        result.TargetIds.ToArray().ShouldBe([Target]);
        var reached = FactPathFinder.ReachedBy(result.Graph, Target, int.MaxValue);
        reached.Keys.ShouldContain(Root);
        reached[Root].ShouldBe(2);
        result.Graph.CallEdges.ShouldContain(edge => edge.Caller == Root && edge.Callee == Middle);
        result.Graph.CallEdges.ShouldContain(edge => edge.Caller == Middle && edge.Callee == Target);
        // The FLATTENED AnalysisResult is still never forced — materializing a graph reads the segmented view.
        snapshot.FullMaterializationCount.ShouldBe(0);
        live.BuildTimes.ShouldNotContain(build => build.Artifact == "eventSites");
    }

    // The point of materializing: the SECOND query against the same generation pays nothing for its graph.
    [Test]
    public async Task A_second_query_on_the_same_generation_reuses_the_materialized_graph()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution, ChainFacts());
        var live = new LiveFactSource(snapshot, new RuleSet());
        var source = (IDemandReverseCallersFactSource)new LiveQueryFactSource(live);

        var first = await source.LoadDemandReverseCallersGraphAsync(Rules(classifyEvents: true), Request(Target));
        var second = await source.LoadDemandReverseCallersGraphAsync(Rules(classifyEvents: true), Request(Middle));

        // Reference identity is the proof: the second query did not re-project anything.
        second.Graph.ShouldBeSameAs(first.Graph);
        second.TargetIds.ToArray().ShouldBe([Middle]);
        snapshot.ProjectedCallGraphCount.ShouldBe(1);
        live.BuildTimes.Count(build => build.Artifact == "traversalGraph").ShouldBe(1);
    }

    [Test]
    public async Task Flattened_facts_use_only_the_explicit_legacy_whole_graph_fallback()
    {
        var facts = ChainFacts();
        var live = new LiveFactSource(facts, new RuleSet());
        var source = (IDemandReverseCallersFactSource)new LiveQueryFactSource(live);

        var result = await source.LoadDemandReverseCallersGraphAsync(Rules(classifyEvents: true), Request(Target));

        result.Diagnostics.Load.Mode.ShouldBe(DemandReverseLoadMode.LegacyWholeGraphFallback);
        result.Diagnostics.Load.UsedLegacyFallback.ShouldBeTrue();
        result.EventSubscriptionsClassified.ShouldBeFalse();
        result.TargetIds.ToArray().ShouldBe([Target]);
        result.Ownership.SymbolIds.ShouldBeEmpty();
        result.Ownership.EmitterFilePaths.ShouldBeEmpty();
        FactPathFinder.ReachedBy(result.Graph, Target, int.MaxValue).Keys.ShouldContain(Root);
        live.BuildTimes.ShouldContain(build => build.Artifact == "traversalGraph");
        live.BuildTimes.ShouldNotContain(build => build.Artifact == "eventSites");
    }

    // CHANGED 2026-08-24 (live materialize-once): MaxNodes was a DEMAND BUDGET — how much graph a keyed
    // projection may expand before it must fail closed rather than serve a partial answer. A materialized
    // whole graph has nothing partial to disclose, and enforcing the budget against it would decline every
    // query on any solution bigger than the budget — precisely the solutions materializing exists for
    // (MedDBase: 300,961 nodes against a 250,000 default). It is therefore not applied on the indexed arm.
    // The FLATTENED arm's typed decline for async is unchanged and still covered below.
    [Test]
    public async Task A_demand_node_budget_does_not_decline_a_materialized_whole_graph_answer()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution, ChainFacts());
        var live = new LiveFactSource(snapshot, new RuleSet());
        var source = (IDemandReverseCallersFactSource)new LiveQueryFactSource(live);

        var result = await source.LoadDemandReverseCallersGraphAsync(Rules(classifyEvents: true), Request(Target) with { MaxNodes = 1 });

        result.Diagnostics.Load.Mode.ShouldBe(DemandReverseLoadMode.MaterializedWholeGraph);
        result.TargetIds.ToArray().ShouldBe([Target]);
        FactPathFinder.ReachedBy(result.Graph, Target, int.MaxValue).Keys.ShouldContain(Root);
        snapshot.FullMaterializationCount.ShouldBe(0);
    }

    // CHANGED 2026-08-24 (live materialize-once): the keyed builder classified `+=` subscription edges
    // ITSELF, so it reported EventSubscriptionsClassified: true and the command skipped its own pass. The
    // materialized graph is deliberately left UNCLASSIFIED — AddDeliveryEdges must see the unreclassified
    // methodGroup edges, so the reclassification has to happen after it, which is exactly where the command
    // (and the store path) already does it, gated on `--raw`. So the source now reports false for both, and
    // the classified-vs-raw distinction lives one layer up, in CallersCommand/PathCommand/reaches/tree.
    [Test]
    public async Task Materialized_graph_leaves_event_subscription_classification_to_the_command()
    {
        const string subscribe = "M:N.Events.Subscribe";
        const string handler = "M:N.Events.Handle";
        var facts = Facts(
            [Method(subscribe, "Subscribe", "T:N.Events"), Method(handler, "Handle", "T:N.Events")],
            [
                Reference(subscribe, "E:N.Events.Changed", RefKinds.Read, line: 7),
                Reference(subscribe, handler, RefKinds.MethodGroup, line: 7),
            ]
        );
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution, facts);
        var live = new LiveFactSource(snapshot, new RuleSet());
        var source = (IDemandReverseCallersFactSource)new LiveQueryFactSource(live);

        var classified = await source.LoadDemandReverseCallersGraphAsync(Rules(classifyEvents: true), Request(handler));
        var raw = await source.LoadDemandReverseCallersGraphAsync(Rules(classifyEvents: false), Request(handler));

        classified.EventSubscriptionsClassified.ShouldBeFalse();
        classified.Graph.CallEdges.Single(edge => edge.Caller == subscribe && edge.Callee == handler).Kind.ShouldBe(EdgeKinds.MethodGroup);
        raw.EventSubscriptionsClassified.ShouldBeFalse();
        raw.Graph.CallEdges.Single(edge => edge.Caller == subscribe && edge.Callee == handler).Kind.ShouldBe(EdgeKinds.MethodGroup);
        // Both shapes are identical here, so they share the ONE materialized graph rather than building two.
        classified.Graph.ShouldBeSameAs(raw.Graph);
        // Reclassification is the command's pass, and it reads the generation's memoized event-site set —
        // which the SOURCE still never forces on its own.
        live.BuildTimes.ShouldNotContain(build => build.Artifact == "eventSites");
    }

    [Test]
    public void Callers_helpers_keep_execution_and_shaping_contracts_explicit()
    {
        CallersCommand.DiscoveryMode(Options(), tsv: false).ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        CallersCommand.DiscoveryMode(Options(roots: true), tsv: false).ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        CallersCommand.DiscoveryMode(Options(entrypoints: true), tsv: false).ShouldBe(FactPathFinder.TraversalMode.AsyncExact);
        CallersCommand.DiscoveryMode(Options(entrypoints: true), tsv: true).ShouldBe(FactPathFinder.TraversalMode.SyncCut);
        CallersCommand.DiscoveryMode(Options(async: true), tsv: false).ShouldBe(FactPathFinder.TraversalMode.AsyncExact);
        CallersCommand
            .DiscoveryMode(Options(async: true, includeDelivery: true), tsv: false)
            .ShouldBe(FactPathFinder.TraversalMode.AsyncInclude);

        var redirect = new FactRedirectRule("M:External.Save", "M:N.Base.Save");
        var factory = new FactGenericFactoryRule("M:N.Factory.New", 0, "New");
        var cut = new FactTraversalCutRule("M:N.Seam.*", "seam");
        var context = new FactContextDispatchRule("IState", "StateBase");
        var shaped = CallersCommand.ShapeRules(
            Options(raw: true),
            new RuleSet
            {
                Redirect = [redirect],
                Factory = [factory],
                Cut = [cut],
                Context = [context],
            }
        );

        shaped.Redirect.ShouldBe([redirect]);
        shaped.Factory.ShouldBeEmpty();
        shaped.Cut.ShouldBeEmpty();
        shaped.Context.ShouldBeEmpty();
    }

    private static DemandForwardGraphRules Rules(bool classifyEvents) =>
        new(new ForwardCallProjectionRules(ClassifyEventSubscriptions: classifyEvents), [], []);

    private static DemandReverseCallersGraphRequest Request(string target) =>
        new(target, int.MaxValue, FactPathFinder.TraversalMode.SyncCut);

    private static CallersCommand.Options Options(
        bool roots = false,
        bool entrypoints = false,
        bool async = false,
        bool includeDelivery = false,
        bool raw = false
    ) =>
        new(
            ToPattern: Target,
            RootsOnly: roots,
            EntrypointsOnly: entrypoints,
            IncludeReverseOnly: false,
            Async: async,
            IncludeDelivery: includeDelivery,
            Raw: raw,
            ExtraRules: [],
            Depth: null,
            Format: null,
            Limit: null,
            Time: false
        );

    private static FactSnapshot Snapshot(Solution solution, AnalysisResult facts) =>
        new(new FactRevision(0), solution, facts, ImmutableDictionary<string, FileFacts>.Empty, DirtySet.Empty, SnapshotDelta.Empty);

    private static AnalysisResult ChainFacts() =>
        Facts(
            [Method(Root, "Run", "T:N.Root"), Method(Middle, "Run", "T:N.Middle"), Method(Target, "Run", "T:N.Target")],
            [Reference(Root, Middle, RefKinds.Invocation, 3), Reference(Middle, Target, RefKinds.Invocation, 5)]
        );

    private static AnalysisResult Facts(IReadOnlyList<SymbolFact> symbols, IReadOnlyList<ReferenceFact> references) =>
        new(
            SolutionPath: "/repo/App.sln",
            SourceFiles: [],
            DiRegistrations: [],
            Symbols: symbols,
            References: references,
            TypeRelations: [],
            DispatchFacts: [],
            AllocationFacts: []
        );

    private static SymbolFact Method(string id, string name, string containingType) =>
        new(id, SymbolKinds.Method, name, "N", containingType, "public", "", $"{name}()", "/repo/App.cs", 1, 1, "App", IsOverride: false);

    private static ReferenceFact Reference(string caller, string target, string kind, int line) =>
        new(target, kind, caller, "App", TargetInSource: true, "/repo/App.cs", line);
}
