using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Rig.Analysis.Inventory;
using Rig.Cli.CommandLine;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Tests.Live;

// THE SCALE GATE for the live query path.
//
// The resident index used to answer reaches/path/callers/tree by building a KEYED DEMAND graph per query.
// For the reverse direction that meant a fixed point whose every pass called FactPathFinder.ReachedBy over
// the partial snapshot — and ReachedBy rebuilds the whole graph index (BuildIndex + BuildReverseMaps, the
// second of which is a whole-graph receiver-blind dispatch scan) from scratch on every call. Cost was
// therefore O(passes x graph), and on the real 227-project target (300,961 nodes / 630,932 call edges) it
// did not terminate: 8.4 GB resident and 36+ minutes of CPU for a question the same store answers in 12s.
//
// The fix is not a faster fixed point, it is not having one: materialize the projected call graph ONCE per
// fact generation and let the SHARED FactPathFinder narrow it — exactly what the store path does with the
// `call_edges` table. These tests pin the two properties that makes true, on a corpus big enough that the
// difference is not a rounding error:
//
//   1. The graph is built EXACTLY ONCE per generation, however many queries ask for it, and whatever their
//      traversal mode. Build count — not wall time — is the assertion, because it is the property that
//      cannot be flaky and it is precisely the one the fixed point violated.
//   2. A reverse (callers) query over >20,000 nodes returns a CORRECT answer, in seconds.
//
// The corpus is generated, not a playground: 22,001 methods and 22,000 call edges, shaped like the query
// that wedged — a wide fan-in (20,000 direct callers of one target) behind a 2,000-deep chain, so a
// pass-per-frontier fixed point needs thousands of passes over the whole graph. It is wrapped in a real
// FactSnapshot, so the graph view under test is the INDEXED segmented one the host publishes (a linear-scan
// test view would be quadratic at this size and would measure the harness rather than the code).
//
// CHAIN DEPTH IS DELIBERATELY MODEST, and the reason is a measurement worth keeping: at 12,000 deep, a plain
// `callers` spent 23 SECONDS in FactPathFinder.SeedsReachTarget — the per-caller FORWARD verification pass,
// which is quadratic in chain depth and is shared verbatim with the store path. That is not a live-path cost
// and no amount of materializing touches it; a 12,000-deep call chain is also not a shape real code has. The
// corpus keeps the width (which is real: `Save` has thousands of callers) and drops the pathological depth.
public sealed class LiveMaterializedGraphTests
{
    private const string Target = "M:Scale.Sink.Target";
    private const int FanIn = 20_000;
    private const int ChainDepth = 2_000;

    private static readonly object ReportLock = new();

    [Test]
    public async Task Reverse_callers_over_a_twenty_thousand_node_graph_materializes_once_and_answers()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution, ScaleFacts());
        var live = new LiveFactSource(snapshot, new RuleSet());
        var source = (IDemandReverseCallersFactSource)new LiveQueryFactSource(live);

        var materializeWatch = Stopwatch.StartNew();
        var first = await source.LoadDemandReverseCallersGraphAsync(Rules(), Request(Target));
        materializeWatch.Stop();

        // A SECOND query, in a DIFFERENT traversal mode, on the SAME generation. A per-mode projection would
        // build a second graph here; one edge set tagged by kind does not.
        var reuseWatch = Stopwatch.StartNew();
        var second = await source.LoadDemandReverseCallersGraphAsync(
            Rules(),
            Request(Target) with
            {
                DiscoveryMode = FactPathFinder.TraversalMode.AsyncExact,
                ExecutionMode = FactPathFinder.TraversalMode.AsyncExact,
            }
        );
        reuseWatch.Stop();

        first.Diagnostics.Load.Mode.ShouldBe(DemandReverseLoadMode.MaterializedWholeGraph);
        first.TargetIds.ToArray().ShouldBe([Target]);
        first.Graph.Methods.Count.ShouldBeGreaterThan(20_000);

        // THE ACCEPTANCE PROPERTY: one build, not one per query and not one per fixed-point pass.
        second.Graph.ShouldBeSameAs(first.Graph);
        snapshot.ProjectedCallGraphCount.ShouldBe(1);
        live.BuildTimes.Count(build => build.Artifact == "traversalGraph").ShouldBe(1);
        snapshot.FullMaterializationCount.ShouldBe(0);

        // …and the answer is right. Every fan-in caller and every chain link reverse-reaches the target.
        var traversalWatch = Stopwatch.StartNew();
        var reached = FactPathFinder.ReachedBy(first.Graph, Target, maxDepth: int.MaxValue, maxNodes: int.MaxValue);
        traversalWatch.Stop();
        reached.Count.ShouldBe(FanIn + ChainDepth + 1);
        reached[Target].ShouldBe(0);
        reached[Caller(0)].ShouldBe(1);
        reached[Caller(FanIn - 1)].ShouldBe(1);
        reached[Chain(0)].ShouldBe(2);
        reached[Chain(ChainDepth - 1)].ShouldBe(ChainDepth + 1);

        Report(
            $"[materialize] {first.Graph.Methods.Count} methods / {first.Graph.CallEdges.Count} call edges — "
                + $"materialize {Ms(materializeWatch.Elapsed)}, second query (async mode) {Ms(reuseWatch.Elapsed)}, "
                + $"reverse traversal {Ms(traversalWatch.Elapsed)}"
        );
    }

    // The wedge shape, end to end: `callers <target> --entrypoints` over the same corpus, through the live
    // query surface rather than through the fact-source seam. This is the invocation that never returned.
    [Test]
    public async Task Callers_entrypoints_over_a_twenty_thousand_node_graph_returns_an_answer()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution, ScaleFacts());
        var live = new LiveFactSource(snapshot, new RuleSet());

        var watch = Stopwatch.StartNew();
        var result = await LiveQueryRunner.RunRequestAsync(
            new LiveQueryRequest(
                LiveQueryTransport.Protocol,
                LiveQueryVerbs.Callers,
                "/repo",
                JsonSerializer.Serialize(EntrypointsOptions(), LiveQueryTransport.Json)
            ),
            live,
            "/repo"
        );
        watch.Stop();

        result.DeclineReason.ShouldBeNull();
        result.Answer.ShouldNotBeNull();
        // No rule set is configured for this generated corpus, so there are no entry points to report — the
        // load-bearing claim is that the query TERMINATES with a definite answer instead of wedging.
        result.Answer!.Exit.ShouldBeOneOf(0, 1);
        snapshot.ProjectedCallGraphCount.ShouldBe(1);
        live.BuildTimes.Count(build => build.Artifact == "traversalGraph").ShouldBe(1);

        Report($"[materialize] callers --entrypoints end to end: {Ms(watch.Elapsed)} (exit {result.Answer.Exit})");
    }

    // A repeat question on the same generation must pay for NO graph work at all — the property that makes a
    // resident host worth running. Measured through the whole query surface, not the seam.
    [Test]
    public async Task A_repeat_query_on_the_same_generation_pays_nothing_for_its_graph()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution, ScaleFacts());
        var live = new LiveFactSource(snapshot, new RuleSet());

        var coldWatch = Stopwatch.StartNew();
        var cold = await LiveQueryRunner.AnswerAsync($"callers {Target}", live, "/repo");
        coldWatch.Stop();

        var warmWatch = Stopwatch.StartNew();
        var warm = await LiveQueryRunner.AnswerAsync($"callers {Target}", live, "/repo");
        warmWatch.Stop();

        cold.Exit.ShouldBe(0, cold.Out + cold.Err);
        warm.Out.ShouldBe(cold.Out);
        snapshot.ProjectedCallGraphCount.ShouldBe(1);
        live.BuildTimes.Count(build => build.Artifact == "traversalGraph").ShouldBe(1);

        Report($"[materialize] callers cold {Ms(coldWatch.Elapsed)} -> warm {Ms(warmWatch.Elapsed)}");
    }

    // `--raw` is the ONE shaping the live surface can still vary, and it genuinely changes the edges
    // (factory monomorphization rewrites them; cut/context ride on the graph). It therefore gets its own
    // cache slot rather than its own per-query build: two slots, not two builds per query, and not one graph
    // silently serving both shapings.
    [Test]
    public async Task Raw_shaping_gets_its_own_cached_slot_rather_than_a_rebuild_per_query()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = Snapshot(workspace.CurrentSolution, ScaleFacts());
        var rules = new RuleSet { Cut = [new FactTraversalCutRule("M:Scale.Nothing.*", "seam")] };
        var live = new LiveFactSource(snapshot, rules);
        var source = (IDemandReverseCallersFactSource)new LiveQueryFactSource(live);

        var shaped = await source.LoadDemandReverseCallersGraphAsync(Rules(cut: rules.Cut), Request(Target));
        var raw = await source.LoadDemandReverseCallersGraphAsync(Rules(), Request(Target));
        var rawAgain = await source.LoadDemandReverseCallersGraphAsync(Rules(), Request(Target));

        raw.Graph.ShouldNotBeSameAs(shaped.Graph);
        rawAgain.Graph.ShouldBeSameAs(raw.Graph);
        snapshot.ProjectedCallGraphCount.ShouldBe(2);
    }

    // ---- corpus ----

    // 10,000 methods calling one target directly (the fan-in that made the reverse frontier wide) behind a
    // 12,000-link chain into the first of them (the depth that made a pass-per-frontier fixed point long).
    private static AnalysisResult ScaleFacts()
    {
        var symbols = new List<SymbolFact>(FanIn + ChainDepth + 1) { Method(Target, "Target", "T:Scale.Sink") };
        var references = new List<ReferenceFact>(FanIn + ChainDepth);

        for (var i = 0; i < FanIn; i++)
        {
            symbols.Add(Method(Caller(i), $"Call{i}", $"T:Scale.Callers.C{i / 50}"));
            references.Add(Reference(Caller(i), Target, RefKinds.Invocation, line: i + 1));
        }

        for (var i = 0; i < ChainDepth; i++)
        {
            symbols.Add(Method(Chain(i), $"Link{i}", $"T:Scale.Chain.L{i / 50}"));
            references.Add(Reference(Chain(i), i == 0 ? Caller(0) : Chain(i - 1), RefKinds.Invocation, line: i + 1));
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

    private static DemandForwardGraphRules Rules(IReadOnlyList<FactTraversalCutRule>? cut = null) =>
        new(new ForwardCallProjectionRules(ClassifyEventSubscriptions: true), cut ?? [], []);

    private static DemandReverseCallersGraphRequest Request(string target) =>
        new(target, int.MaxValue, FactPathFinder.TraversalMode.SyncCut);

    private static CallersCommand.Options EntrypointsOptions() =>
        new(
            ToPattern: Target,
            RootsOnly: false,
            EntrypointsOnly: true,
            IncludeReverseOnly: false,
            Async: false,
            IncludeDelivery: false,
            Raw: false,
            ExtraRules: [],
            Depth: null,
            Format: "tsv",
            Limit: null,
            Time: false
        );

    private static FactSnapshot Snapshot(Solution solution, AnalysisResult facts) =>
        new(new FactRevision(0), solution, facts, ImmutableDictionary<string, FileFacts>.Empty, DirtySet.Empty, SnapshotDelta.Empty);

    private static SymbolFact Method(string id, string name, string containingType) =>
        new(
            id,
            SymbolKinds.Method,
            name,
            "Scale",
            containingType,
            "public",
            "",
            $"{name}()",
            "/repo/Scale.cs",
            1,
            1,
            "Scale",
            IsOverride: false
        );

    private static ReferenceFact Reference(string caller, string target, string kind, int line) =>
        new(target, kind, caller, "Scale", TargetInSource: true, "/repo/Scale.cs", line);

    private static string Ms(TimeSpan elapsed) => elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + "ms";

    // Measurements to a FILE (RIG_LIVE_REPORT), never Console — TUnit swallows console output in its default
    // mode, so a Console line here would be a dead instrument that looks like observability. Nothing asserts
    // on this; the assertions above are all build COUNTS, which cannot be flaky on a loaded machine.
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
