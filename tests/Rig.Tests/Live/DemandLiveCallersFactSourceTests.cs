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

    [Test]
    public async Task Indexed_snapshot_uses_keyed_reverse_graph_without_forcing_flattened_or_live_whole_graph_artifacts()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution, ChainFacts());
        var live = new LiveFactSource(snapshot, new RuleSet());
        var source = (IDemandReverseCallersFactSource)new LiveQueryFactSource(live);

        var result = await source.LoadDemandReverseCallersGraphAsync(Rules(classifyEvents: true), Request(Target));

        result.Diagnostics.Load.Mode.ShouldBe(DemandReverseLoadMode.KeyedDemand);
        result.Diagnostics.Load.UsedLegacyFallback.ShouldBeFalse();
        result.TargetIds.ToArray().ShouldBe([Target]);
        var reached = FactPathFinder.ReachedBy(result.Graph, Target, int.MaxValue);
        reached.Keys.ShouldContain(Root);
        reached[Root].ShouldBe(2);
        result.Graph.CallEdges.ShouldContain(edge => edge.Caller == Root && edge.Callee == Middle);
        result.Graph.CallEdges.ShouldContain(edge => edge.Caller == Middle && edge.Callee == Target);
        snapshot.FullMaterializationCount.ShouldBe(0);
        live.BuildTimes.ShouldNotContain(build => build.Artifact == "traversalGraph");
        live.BuildTimes.ShouldNotContain(build => build.Artifact == "eventSites");
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

    [Test]
    public async Task Keyed_cap_failure_is_typed_and_never_falls_back_to_whole_graph()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution, ChainFacts());
        var live = new LiveFactSource(snapshot, new RuleSet());
        var source = (IDemandReverseCallersFactSource)new LiveQueryFactSource(live);

        await Should.ThrowAsync<DemandReverseCallersGraphUnavailableException>(async () =>
            await source.LoadDemandReverseCallersGraphAsync(Rules(classifyEvents: true), Request(Target) with { MaxNodes = 1 })
        );

        snapshot.FullMaterializationCount.ShouldBe(0);
        live.BuildTimes.ShouldNotContain(build => build.Artifact == "traversalGraph");
        live.BuildTimes.ShouldNotContain(build => build.Artifact == "eventSites");
    }

    [Test]
    public async Task Keyed_event_subscription_is_classified_locally_while_raw_projection_stays_unclassified()
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

        classified.EventSubscriptionsClassified.ShouldBeTrue();
        classified.Graph.CallEdges.Single(edge => edge.Caller == subscribe && edge.Callee == handler).Kind.ShouldBe(EdgeKinds.Handoff);
        raw.EventSubscriptionsClassified.ShouldBeFalse();
        raw.Graph.CallEdges.Single(edge => edge.Caller == subscribe && edge.Callee == handler).Kind.ShouldBe(EdgeKinds.MethodGroup);
        live.BuildTimes.ShouldNotContain(build => build.Artifact == "traversalGraph");
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
