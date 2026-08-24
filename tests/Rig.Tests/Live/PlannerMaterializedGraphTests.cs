using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis.Inventory;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Tests.Live;

// THE SCALE GATE for the exact-refinement PLANNER — the second half of the live materialize-once program.
//
// LiveMaterializedGraphTests pinned the QUERY arm: LiveQueryFactSource stopped projecting a keyed demand
// graph per query and now materializes the whole projected call graph once per fact generation. But a routed
// live query does not start at the query — WatchHost.CaptureForQueryAsync runs the exact-refinement PLANNER
// first, and the planner still called DemandReverseCallersGraph.Build, whose fixed point re-derives the whole
// graph index once per pass (O(passes x graph)). ResidentIndex.EnsureExactAsync loops that planner up to 12
// times. So `callers` on a large solution still wedged in the planner, and only a 30s client timeout rescued
// it by falling back to the store.
//
// The fix is the same one, applied one layer up: derive the planner's three boundary inputs — matched
// targets, closure symbols, emitter files — from the graph the query is about to traverse anyway. These tests
// pin the two properties that makes true:
//
//   1. DIFFERENTIAL. On a corpus with real dirty state and real structure (interface dispatch, an override
//      chain, a sibling implementation of the same interface, and a project with no structural relationship
//      at all), the plan produced from the materialized graph EQUALS the plan the demand builder produced:
//      same SelectedOrigins, same UnknownOrigins, same ToMatched, same UnavailableReason, for both planners,
//      across every demand shape the live surface can hand them and both debt shapes. The old derivation is
//      retained behind ExactCallersBoundarySource/ExactForwardBoundarySource purely as this oracle.
//   2. SCALE. The planner over a >20,000-node graph terminates in the time a single traversal takes, and its
//      graph is the SAME instance the query then traverses — one materialization per generation, not one per
//      planner replan and not one each for planner and query.
public sealed class PlannerMaterializedGraphTests
{
    private static readonly object ReportLock = new();

    // ---- 1. DIFFERENTIAL ----

    // BOTH debt shapes. With an Unknown origin present, the policy admits every generator-capable Unknown
    // whose project depends on the boundary — an escape hatch broad enough to mask a boundary that under-
    // selected. `allChanged: true` removes it, so the comparison rests on the boundary alone.
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Materialized_and_demand_built_plans_agree_on_a_dirty_corpus(bool allChanged)
    {
        using var fixture = DifferentialFixture.Create(allChanged);

        foreach (var demand in DifferentialFixture.Demands())
        {
            // A FRESH snapshot per arm: the materialized arm caches its graph on the generation, and reusing
            // one snapshot would let the second arm read state the first created.
            var materialized = ExactCallersRefinement.Plan(fixture.Snapshot(), demand, ExactCallersBoundarySource.MaterializedGraph);
            var keyed = ExactCallersRefinement.Plan(fixture.Snapshot(), demand, ExactCallersBoundarySource.KeyedDemand);

            var label =
                $"allChanged={allChanged} / {demand.ToPattern} / {demand.DebtScope} / depth {demand.MaxDepth} / {demand.DiscoveryMode} :: materialized=[{string.Join(",", fixture.Names(materialized.SelectedOrigins).Order())}] keyed=[{string.Join(",", fixture.Names(keyed.SelectedOrigins).Order())}] unknownM=[{string.Join(",", fixture.Names(materialized.UnknownOrigins).Order())}] unknownK=[{string.Join(",", fixture.Names(keyed.UnknownOrigins).Order())}]";
            materialized.UnavailableReason.ShouldBe(keyed.UnavailableReason, label);
            materialized.ToMatched.ShouldBe(keyed.ToMatched, label);
            fixture.Names(materialized.SelectedOrigins).ShouldBe(fixture.Names(keyed.SelectedOrigins), ignoreOrder: true, label);
            fixture.Names(materialized.UnknownOrigins).ShouldBe(fixture.Names(keyed.UnknownOrigins), ignoreOrder: true, label);
        }
    }

    // The forward planner (path / reaches / tree) has the SAME fixed point one class over —
    // DemandForwardPathGraph.Build re-runs FactPathFinder.Reaches over its partial snapshot once per pass.
    // Its conversion is narrower than the callers one, because only ONE of its inputs came from the demand
    // load: the graph. Endpoint matching always ran against the whole method catalog, and the load's
    // Ownership is a delivery-only side channel that is EMPTY under SyncCut — so for the default sync shapes
    // below the two arms are the same computation over a subgraph and a supergraph, which is exactly the
    // claim worth pinning.
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Materialized_and_demand_built_forward_plans_agree_on_a_dirty_corpus(bool allChanged)
    {
        using var fixture = DifferentialFixture.Create(allChanged);

        foreach (var demand in DifferentialFixture.ForwardDemands())
        {
            var materialized = ExactForwardRefinement.Plan(fixture.Snapshot(), demand, ExactForwardBoundarySource.MaterializedGraph);
            var keyed = ExactForwardRefinement.Plan(fixture.Snapshot(), demand, ExactForwardBoundarySource.KeyedDemand);

            var label =
                $"allChanged={allChanged} / {demand.QueryKind} {demand.FromPattern} -> {demand.ToPattern ?? "*"} / {demand.DebtScope} / depth {demand.MaxDepth}"
                + $" :: materialized=[{string.Join(",", fixture.Names(materialized.SelectedOrigins).Order())}]"
                + $" keyed=[{string.Join(",", fixture.Names(keyed.SelectedOrigins).Order())}]";
            materialized.UnavailableReason.ShouldBe(keyed.UnavailableReason, label);
            materialized.FromMatched.ShouldBe(keyed.FromMatched, label);
            materialized.ToMatched.ShouldBe(keyed.ToMatched, label);
            fixture.Names(materialized.SelectedOrigins).ShouldBe(fixture.Names(keyed.SelectedOrigins), ignoreOrder: true, label);
            fixture.Names(materialized.UnknownOrigins).ShouldBe(fixture.Names(keyed.UnknownOrigins), ignoreOrder: true, label);
        }
    }

    // The differential above proves the two AGREE; this proves the agreement is not vacuous — the corpus
    // really does discriminate, so a boundary derivation that collapsed to "everything" or "nothing" would
    // fail here rather than sail through the comparison.
    [Test]
    public void The_differential_corpus_actually_discriminates()
    {
        using var fixture = DifferentialFixture.Create();

        var plan = ExactCallersRefinement.Plan(fixture.Snapshot(), DifferentialFixture.Demand(DifferentialFixture.Target));

        plan.UnavailableReason.ShouldBeNull();
        plan.ToMatched.ShouldBeTrue();
        // The hub's contracts project is in the boundary even though nothing in the closure is DECLARED there
        // except the interface member itself — FactPathFinder's narrowed reverse dispatch jumps over the hub,
        // so the planner has to re-add it from the graph's mined dispatch facts.
        fixture.Names(plan.SelectedOrigins).ShouldContain("Contracts");
        fixture.Names(plan.SelectedOrigins).ShouldContain("Impl");
        fixture.Names(plan.SelectedOrigins).ShouldContain("Caller");
        // Unrelated debt in a project no boundary edge touches stays unselected — the whole point of a
        // demand-shaped boundary rather than "refine the world".
        fixture.Names(plan.SelectedOrigins).ShouldNotContain("Unrelated");
    }

    // ---- 2. SCALE ----

    // The shape that wedged: a 20,000-wide fan-in behind a 2,000-deep chain, so a pass-per-frontier fixed
    // point needs thousands of passes over the whole graph. The assertion is a BUILD COUNT, not wall time —
    // it cannot be flaky and it is precisely the property the fixed point violated. The elapsed time is
    // reported to RIG_LIVE_REPORT for the record.
    [Test]
    public void Planner_over_a_twenty_thousand_node_graph_materializes_once_and_terminates()
    {
        using var workspace = new AdhocWorkspace();
        var scale = ScaleFixture.Create(workspace);

        var watch = Stopwatch.StartNew();
        var plan = ExactCallersRefinement.Plan(scale.Snapshot, ScaleFixture.Demand());
        watch.Stop();

        plan.UnavailableReason.ShouldBeNull();
        plan.ToMatched.ShouldBeTrue();
        // One dirty origin, and it owns the whole corpus file, so it is on the boundary.
        plan.SelectedOrigins.ShouldBe([scale.Project]);

        // THE ACCEPTANCE PROPERTY: ONE materialized graph for the whole plan, however many nodes it walked.
        // The keyed builder took 2,003 fixed-point passes on this corpus, each re-deriving the graph index.
        scale.Snapshot.ProjectedCallGraphCount.ShouldBe(1);
        scale.Snapshot.FullMaterializationCount.ShouldBe(0);

        Report($"[planner] plan over {ScaleFixture.FanIn + ScaleFixture.ChainDepth + 1} nodes: {Ms(watch.Elapsed)}");
    }

    // A REPLAN on the same generation — what ResidentIndex.EnsureExactAsync does up to 12 times — must pay
    // for no further graph work at all. Under the keyed builder every replan rebuilt its whole keyed
    // projection AND re-ran the fixed point.
    [Test]
    public void Replanning_the_same_generation_builds_no_second_graph()
    {
        using var workspace = new AdhocWorkspace();
        var scale = ScaleFixture.Create(workspace);

        var coldWatch = Stopwatch.StartNew();
        var first = ExactCallersRefinement.Plan(scale.Snapshot, ScaleFixture.Demand());
        coldWatch.Stop();

        var warmWatch = Stopwatch.StartNew();
        var second = ExactCallersRefinement.Plan(scale.Snapshot, ScaleFixture.Demand());
        warmWatch.Stop();

        second.SelectedOrigins.ShouldBe(first.SelectedOrigins);
        second.ToMatched.ShouldBe(first.ToMatched);
        scale.Snapshot.ProjectedCallGraphCount.ShouldBe(1);

        Report($"[planner] cold plan {Ms(coldWatch.Elapsed)} -> replan {Ms(warmWatch.Elapsed)}");
    }

    // ONE GRAPH FOR BOTH ARMS. The planner runs first on the routed path; the query that follows must find
    // the generation's slot already warm rather than materializing a second copy. This is what makes the
    // planner's materialization free rather than a doubling — and it only holds because
    // FactSnapshot.ProjectedCallGraph(DemandForwardGraphRules) formats the same shaping key
    // LiveQueryFactSource.ShapingKey does.
    [Test]
    public async Task The_planner_and_the_query_that_follows_it_share_one_materialized_graph()
    {
        using var workspace = new AdhocWorkspace();
        var scale = ScaleFixture.Create(workspace);

        var plan = ExactCallersRefinement.Plan(scale.Snapshot, ScaleFixture.Demand());
        plan.UnavailableReason.ShouldBeNull();
        scale.Snapshot.ProjectedCallGraphCount.ShouldBe(1);

        var live = new LiveFactSource(scale.Snapshot, new RuleSet());
        var source = (IDemandReverseCallersFactSource)new LiveQueryFactSource(live);
        var answered = await source.LoadDemandReverseCallersGraphAsync(
            ScaleFixture.Rules(),
            new DemandReverseCallersGraphRequest(ScaleFixture.Target, int.MaxValue, FactPathFinder.TraversalMode.SyncCut)
        );

        answered.Diagnostics.Load.Mode.ShouldBe(DemandReverseLoadMode.MaterializedWholeGraph);
        answered.TargetIds.ToArray().ShouldBe([ScaleFixture.Target]);
        // Still ONE slot, and the query never forced LiveFactSource's own traversalGraph memo: the planner's
        // graph IS the query's graph.
        scale.Snapshot.ProjectedCallGraphCount.ShouldBe(1);
        // CHANGED 2026-08-24 (graph-cost disclosure restored): was
        // `live.BuildTimes.ShouldNotContain(build => build.Artifact == "traversalGraph")`.
        //
        // The CLAIM is unchanged and is still exactly what this line pins: the generation built its graph
        // ONCE. What changed is that the single build is now DISCLOSED. The absence assertion was reading the
        // single-build claim off an instrumentation GAP — the planner's build was not in BuildTimes at all,
        // so the host's "derived layer built this generation" note had stopped naming the call graph and the
        // seconds a user waits for it went unreported (the gap this file's own comment recorded as open).
        // FactSnapshot now times the build and LiveFactSource.BuildTimes merges it, so the same claim is read
        // the honest way round: EXACTLY ONE row, never two. A second graph build would fail this as loudly as
        // the old absence assertion did.
        live.BuildTimes.Count(build => build.Artifact == "traversalGraph").ShouldBe(1);
    }

    // ---- differential corpus ----

    // Five projects, wired so every ownership route the planner has is exercised by a DIFFERENT project:
    //
    //   Contracts   IWork.Run           the interface hub — reached ONLY through mined dispatch, never as a
    //                                   reverse-closure node (narrowed reverse dispatch jumps over it)
    //   Impl        Work.Run            the target itself; also Derived.Run, an override of Work.Run
    //   Caller      Root.Go             calls IWork.Run — a call-site emitter file
    //   Sibling     Other.Run           an unrelated IWork implementation; NOT a caller of the target
    //   Unrelated   Noise.Idle          debt with no structural relationship at all
    //
    // Contracts is referenced by Impl/Caller/Sibling, so the reverse project-reference closure pulls those in
    // once Contracts is admitted — which is exactly the policy the planner keeps unchanged.
    private sealed class DifferentialFixture : IDisposable
    {
        internal const string Target = "M:Impl.Work.Run()";
        private const string Hub = "M:Contracts.IWork.Run()";
        private const string Derived = "M:Impl.Derived.Run()";
        private const string Sibling = "M:Sibling.Other.Run()";
        private const string RootMethod = "M:Caller.Root.Go()";
        private const string NoiseMethod = "M:Unrelated.Noise.Idle()";

        private static readonly string Root = Path.Combine(Path.GetTempPath(), "rig-planner-differential-tests");

        private readonly AnalysisResult _facts;
        private readonly ImmutableDictionary<ProjectId, ImmutableHashSet<DocumentId>> _dirty;
        private readonly ImmutableDictionary<ProjectId, SurfaceState> _states;

        private DifferentialFixture(
            AdhocWorkspace workspace,
            AnalysisResult facts,
            ImmutableDictionary<ProjectId, ImmutableHashSet<DocumentId>> dirty,
            ImmutableDictionary<ProjectId, SurfaceState> states,
            IReadOnlyDictionary<ProjectId, string> names
        )
        {
            Workspace = workspace;
            _facts = facts;
            _dirty = dirty;
            _states = states;
            Names_ = names;
        }

        private AdhocWorkspace Workspace { get; }
        private IReadOnlyDictionary<ProjectId, string> Names_ { get; }

        public void Dispose() => Workspace.Dispose();

        // A fresh generation each call — the materialized arm caches its graph on the snapshot it is given.
        internal FactSnapshot Snapshot()
        {
            var solution = Workspace.CurrentSolution;
            return new FactSnapshot(
                new FactRevision(1),
                solution,
                _facts,
                ImmutableDictionary.Create<string, FileFacts>(StringComparer.OrdinalIgnoreCase),
                DirtySet.FromContributions(solution, _dirty),
                SnapshotDelta.Empty with
                {
                    SurfaceStates = _states,
                }
            );
        }

        internal string[] Names(IEnumerable<ProjectId> projects) => [.. projects.Select(id => Names_[id])];

        internal static ExactCallersDemand Demand(string target) =>
            new(
                target,
                new DemandForwardGraphRules(new ForwardCallProjectionRules(), [], [], []),
                int.MaxValue,
                FactPathFinder.TraversalMode.SyncCut,
                FactPathFinder.TraversalMode.SyncCut
            );

        // The demand shapes the planner can actually be handed by LiveQueryRunner.BuildCallersDemand, plus
        // the two degenerate ones (no match, bounded depth) whose policy branches differ.
        internal static IEnumerable<ExactCallersDemand> Demands()
        {
            yield return Demand(Target);
            yield return Demand(Hub);
            yield return Demand(Derived);
            yield return Demand("M:Nothing.Matches.This()");
            yield return Demand(Target) with
            {
                MaxDepth = 1,
            };
            yield return Demand(Target) with
            {
                DebtScope = ExactForwardDebtScope.WholeResident,
            };
            // sync `callers --entrypoints` deliberately DISCOVERS wider than it executes.
            yield return Demand(Target) with
            {
                DiscoveryMode = FactPathFinder.TraversalMode.AsyncExact,
            };
        }

        internal static IEnumerable<ExactForwardDemand> ForwardDemands()
        {
            ExactForwardDemand Forward(ExactForwardQueryKind kind, string from, string? to = null) =>
                new(
                    kind,
                    from,
                    to,
                    new DemandForwardGraphRules(new ForwardCallProjectionRules(), [], [], []),
                    int.MaxValue,
                    FactPathFinder.TraversalMode.SyncCut
                );

            yield return Forward(ExactForwardQueryKind.Reaches, RootMethod);
            yield return Forward(ExactForwardQueryKind.Tree, RootMethod);
            yield return Forward(ExactForwardQueryKind.Path, RootMethod, Target);
            yield return Forward(ExactForwardQueryKind.Path, RootMethod, RootMethod);
            yield return Forward(ExactForwardQueryKind.Path, RootMethod, "M:Nothing.Matches.This()");
            yield return Forward(ExactForwardQueryKind.Reaches, "M:Nothing.Matches.This()");
            yield return Forward(ExactForwardQueryKind.Reaches, Hub);
            yield return Forward(ExactForwardQueryKind.Reaches, RootMethod) with
            {
                MaxDepth = 1,
            };
            yield return Forward(ExactForwardQueryKind.Reaches, RootMethod) with
            {
                DebtScope = ExactForwardDebtScope.WholeResident,
            };
        }

        internal static DifferentialFixture Create(bool allChanged = false)
        {
            var workspace = new AdhocWorkspace();
            var contracts = ProjectId.CreateNewId("Contracts");
            var impl = ProjectId.CreateNewId("Impl");
            var caller = ProjectId.CreateNewId("Caller");
            var sibling = ProjectId.CreateNewId("Sibling");
            var unrelated = ProjectId.CreateNewId("Unrelated");
            var names = new Dictionary<ProjectId, string>
            {
                [contracts] = "Contracts",
                [impl] = "Impl",
                [caller] = "Caller",
                [sibling] = "Sibling",
                [unrelated] = "Unrelated",
            };

            var documents = names.ToDictionary(pair => pair.Key, pair => DocumentId.CreateNewId(pair.Key, pair.Value + ".cs"));
            var paths = names.ToDictionary(pair => pair.Key, pair => Path.Combine(Root, pair.Value, pair.Value + ".cs"));

            workspace.AddSolution(
                SolutionInfo.Create(
                    SolutionId.CreateNewId(),
                    VersionStamp.Create(),
                    projects:
                    [
                        Project(contracts, "Contracts", documents[contracts], paths[contracts]),
                        Project(impl, "Impl", documents[impl], paths[impl], [new ProjectReference(contracts)]),
                        Project(caller, "Caller", documents[caller], paths[caller], [new ProjectReference(contracts)]),
                        Project(sibling, "Sibling", documents[sibling], paths[sibling], [new ProjectReference(contracts)]),
                        Project(unrelated, "Unrelated", documents[unrelated], paths[unrelated]),
                    ]
                )
            );

            var facts = new AnalysisResult(
                "PlannerDifferential.sln",
                [],
                [],
                Symbols:
                [
                    Method(Hub, "T:Contracts.IWork", paths[contracts], "Contracts"),
                    Method(Target, "T:Impl.Work", paths[impl], "Impl"),
                    Method(Derived, "T:Impl.Derived", paths[impl], "Impl"),
                    Method(Sibling, "T:Sibling.Other", paths[sibling], "Sibling"),
                    Method(RootMethod, "T:Caller.Root", paths[caller], "Caller"),
                    Method(NoiseMethod, "T:Unrelated.Noise", paths[unrelated], "Unrelated"),
                ],
                References:
                [
                    new ReferenceFact(Hub, RefKinds.Invocation, RootMethod, "Contracts", true, paths[caller], 1, "Impl.Work"),
                    new ReferenceFact(NoiseMethod, RefKinds.Invocation, NoiseMethod, "Unrelated", true, paths[unrelated], 1),
                ],
                TypeRelations:
                [
                    new TypeRelationFact("T:Impl.Work", "T:Contracts.IWork", RelationKinds.Interface, paths[impl]),
                    new TypeRelationFact("T:Sibling.Other", "T:Contracts.IWork", RelationKinds.Interface, paths[sibling]),
                    new TypeRelationFact("T:Impl.Derived", "T:Impl.Work", RelationKinds.Base, paths[impl]),
                ],
                DispatchFacts:
                [
                    new DispatchFact(Hub, Target, DispatchKinds.Impl, paths[impl]),
                    new DispatchFact(Hub, Sibling, DispatchKinds.Impl, paths[sibling]),
                    new DispatchFact(Target, Derived, DispatchKinds.Override, paths[impl]),
                ]
            );

            // Every project carries debt, so the BOUNDARY — not the debt set — is what discriminates.
            // Sibling is the Unknown one: the policy admits an Unknown origin that merely DEPENDS on a
            // boundary project, which is a wide enough escape hatch to mask an under-selecting boundary.
            // `allChanged: true` closes it and reruns the whole comparison without it.
            var dirty = ImmutableDictionary.CreateBuilder<ProjectId, ImmutableHashSet<DocumentId>>();
            var states = ImmutableDictionary.CreateBuilder<ProjectId, SurfaceState>();
            foreach (var (projectId, _) in names)
            {
                dirty[projectId] = [documents[projectId]];
                states[projectId] = projectId == sibling && !allChanged ? SurfaceState.Unknown : SurfaceState.Changed;
            }

            return new DifferentialFixture(workspace, facts, dirty.ToImmutable(), states.ToImmutable(), names);
        }

        private static ProjectInfo Project(
            ProjectId id,
            string name,
            DocumentId document,
            string path,
            IReadOnlyList<ProjectReference>? references = null
        ) =>
            ProjectInfo.Create(
                id,
                VersionStamp.Create(),
                name,
                name,
                LanguageNames.CSharp,
                filePath: Path.ChangeExtension(path, ".csproj"),
                projectReferences: references ?? [],
                documents: [Document(document, path)]
            );
    }

    // ---- scale corpus ----

    // The same generated shape LiveMaterializedGraphTests uses for the query arm, wrapped in a real one-project
    // solution so ProjectSurfaceCatalog.ResolveEmitterOwnership resolves every symbol EXACTLY (an unowned
    // emitter fails the plan closed on the first symbol and would measure nothing).
    private sealed record ScaleFixture(FactSnapshot Snapshot, ProjectId Project)
    {
        internal const string Target = "M:Scale.Sink.Target";
        internal const int FanIn = 20_000;
        internal const int ChainDepth = 2_000;
        private static readonly string CorpusPath = Path.Combine(Path.GetTempPath(), "rig-planner-scale", "Scale.cs");

        internal static ExactCallersDemand Demand() =>
            new(Target, Rules(), int.MaxValue, FactPathFinder.TraversalMode.SyncCut, FactPathFinder.TraversalMode.SyncCut);

        internal static DemandForwardGraphRules Rules() =>
            new(new ForwardCallProjectionRules(ClassifyEventSubscriptions: true), [], [], []);

        internal static ScaleFixture Create(AdhocWorkspace workspace)
        {
            var project = ProjectId.CreateNewId("Scale");
            var document = DocumentId.CreateNewId(project, "Scale.cs");
            workspace.AddSolution(
                SolutionInfo.Create(
                    SolutionId.CreateNewId(),
                    VersionStamp.Create(),
                    projects:
                    [
                        ProjectInfo.Create(
                            project,
                            VersionStamp.Create(),
                            "Scale",
                            "Scale",
                            LanguageNames.CSharp,
                            filePath: Path.ChangeExtension(CorpusPath, ".csproj"),
                            documents: [Document(document, CorpusPath)]
                        ),
                    ]
                )
            );

            var solution = workspace.CurrentSolution;
            var dirty = ImmutableDictionary<ProjectId, ImmutableHashSet<DocumentId>>.Empty.Add(project, [document]);
            var snapshot = new FactSnapshot(
                new FactRevision(0),
                solution,
                Facts(),
                ImmutableDictionary<string, FileFacts>.Empty,
                DirtySet.FromContributions(solution, dirty),
                SnapshotDelta.Empty with
                {
                    SurfaceStates = ImmutableDictionary<ProjectId, SurfaceState>.Empty.Add(project, SurfaceState.Changed),
                }
            );
            return new ScaleFixture(snapshot, project);
        }

        private static AnalysisResult Facts()
        {
            var symbols = new List<SymbolFact>(FanIn + ChainDepth + 1) { Method(Target, "T:Scale.Sink", CorpusPath, "Scale") };
            var references = new List<ReferenceFact>(FanIn + ChainDepth);

            for (var i = 0; i < FanIn; i++)
            {
                symbols.Add(Method(Caller(i), $"T:Scale.Callers.C{i / 50}", CorpusPath, "Scale"));
                references.Add(new ReferenceFact(Target, RefKinds.Invocation, Caller(i), "Scale", true, CorpusPath, i + 1));
            }

            for (var i = 0; i < ChainDepth; i++)
            {
                symbols.Add(Method(Chain(i), $"T:Scale.Chain.L{i / 50}", CorpusPath, "Scale"));
                references.Add(
                    new ReferenceFact(i == 0 ? Caller(0) : Chain(i - 1), RefKinds.Invocation, Chain(i), "Scale", true, CorpusPath, i + 1)
                );
            }

            return new AnalysisResult(
                SolutionPath: "/repo/Scale.sln",
                SourceFiles: [],
                DiRegistrations: [],
                Symbols: symbols,
                References: references,
                TypeRelations: [],
                DispatchFacts: [],
                AllocationFacts: []
            );
        }

        private static string Caller(int i) => string.Create(CultureInfo.InvariantCulture, $"M:Scale.Callers.C{i / 50}.Call{i}");

        private static string Chain(int i) => string.Create(CultureInfo.InvariantCulture, $"M:Scale.Chain.L{i / 50}.Link{i}");
    }

    private static SymbolFact Method(string id, string containingType, string path, string assembly) =>
        new(id, SymbolKinds.Method, id, "", containingType, "public", "", id, path, 1, 1, assembly, false);

    private static DocumentInfo Document(DocumentId id, string path) =>
        DocumentInfo.Create(
            id,
            Path.GetFileName(path),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(""), VersionStamp.Create(), path)),
            filePath: path
        );

    private static string Ms(TimeSpan elapsed) => elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + "ms";

    // Measurements to a FILE (RIG_LIVE_REPORT), never Console — TUnit swallows console output in its default
    // mode. Nothing asserts on this; the assertions above are build COUNTS, which cannot be flaky.
    private static void Report(string block)
    {
        var path = Environment.GetEnvironmentVariable("RIG_LIVE_REPORT");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (ReportLock)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.AppendAllText(path, block + Environment.NewLine);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(10);
                }
            }
        }
    }
}
