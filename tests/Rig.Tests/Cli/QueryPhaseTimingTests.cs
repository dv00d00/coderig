using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis.Inventory;
using Rig.Cli;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Tests.Fixtures;
using Shouldly;
using RuleSet = Rig.Domain.Data.RuleSet;

namespace Rig.Tests.Cli;

// WHERE DID THE TIME GO — the two halves of that question rig had stopped being able to answer.
//
// 1. `rig callers <x> --entrypoints --time` recorded NO phase at all. The other two callers lenses recorded
//    `traversal` and `render`; the entry-points branch — the SLOWEST and most-used of the three — returned
//    through a code path that touched the timer exactly once, for `graph load`. On a 227-project store that
//    printed "graph load 4.6s / total 4.6s" for a query that took 12 seconds: seven dark seconds in the
//    hottest question the tool answers, with no way to say which of the reverse closure, the entry-point
//    derivation or the forward verification owned them. (Measured after: the entry-point derivation owned
//    3.5s of it — a third of the query — and nothing had ever pointed at it.)
//
// 2. The live host's "derived layer built this generation" note stopped naming the projected call graph.
//    The exact-query planner began filling FactSnapshot's cache slot BEFORE the query runs, so
//    LiveFactSource's own `traversalGraph` memo never fired and contributed no row — and the seconds the
//    user waits inside their first query of a generation stopped being disclosed. rig's whole convention is
//    answer-plus-disclosure; a silently-dropped multi-second cost is a regression in the disclosure half.
//
// The assertions below are written against ACTUAL captured output (a real store indexed from the
// EntryPointEffects playground, and a real snapshot), not against an imagined table shape.
public sealed class QueryPhaseTimingTests
{
    // ---- 1. the `--time` phase table ----

    // THE REGRESSION, stated as a table: every phase named, and `graph load` no longer 100% of a query it is
    // a minority of. Captured shape (playground store, `callers CreateTeamAsync --entrypoints --time`):
    //
    //   phase                    wall      %  ...
    //   graph load               0.1s  39.5%
    //   deployments              0.0s   1.4%
    //   reverse closure          0.0s   8.4%
    //   entry points             0.1s  38.4%
    //   forward verify           0.0s  10.0%
    //   render                   0.0s   2.3%
    //   total                    0.1s 100.0%
    [Test]
    public async Task Callers_entrypoints_time_table_names_every_phase_of_the_query()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        var output = new StringWriter();
        var error = new StringWriter();

        (await CliApplication.RunAsync(["index", playground.SolutionPath], output, error, workingDirectory)).ShouldBe(0);

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        (
            await CliApplication.RunAsync(
                ["callers", "CreateTeamAsync", "--entrypoints", "--time", "--no-live"],
                output,
                error,
                workingDirectory
            )
        ).ShouldBe(0);

        var table = error.ToString();
        table.ShouldContain("Timing breakdown");
        // The four phases the entry-points branch actually spends its time in, plus the two bookends. Named
        // separately because they scale differently: the closure with graph size, the verification with
        // candidate count x depth, the entry-point derivation with the whole store's EP facts.
        table.ShouldContain("graph load");
        table.ShouldContain("deployments");
        table.ShouldContain("reverse closure");
        table.ShouldContain("entry points");
        table.ShouldContain("forward verify");
        table.ShouldContain("render");
        table.ShouldContain("total");

        // THE point of the change: `graph load` used to be the only row, and therefore 100.0% of a "total"
        // that was really just itself. It must now be a minority of a total that accounts for the query.
        PhasePercent(table, "graph load").ShouldBeLessThan(99.0, table);
        // Every non-total row's share is a real fraction, and they sum to the 100% the table claims.
        var shares = PhasePercents(table);
        shares.Count.ShouldBe(6, table);
        shares.Values.Sum().ShouldBeInRange(99.0, 101.0, table);
    }

    // The two lenses that DID record a phase recorded one fat `traversal` bucket over both hot spots. Split
    // at the call sites so all three callers lenses read in the same vocabulary and a slow query says which
    // half it was slow in.
    [Test]
    [Arguments("--roots")] // the no-predecessor-origins lens
    [Arguments("")] // the plain reachable-callers lens
    public async Task Callers_splits_the_old_traversal_bucket_into_closure_and_verification(string lens)
    {
        string[] arguments =
            lens.Length == 0
                ? ["callers", "CreateTeamAsync", "--time", "--no-live"]
                : ["callers", "CreateTeamAsync", lens, "--time", "--no-live"];
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        var output = new StringWriter();
        var error = new StringWriter();

        (await CliApplication.RunAsync(["index", playground.SolutionPath], output, error, workingDirectory)).ShouldBe(0);

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        (await CliApplication.RunAsync(arguments, output, error, workingDirectory)).ShouldBe(0);

        var table = error.ToString();
        table.ShouldContain("reverse closure");
        table.ShouldContain("forward verify");
        // The old lumped name is gone from ALL three lenses — one vocabulary, not two.
        table.ShouldNotContain("  traversal ");
    }

    // Instrumentation must not change ANSWERS, and must cost nothing when it is off. Same stdout, byte for
    // byte, with and without `--time`; the table goes to stderr so `--format tsv` stays pipeable.
    [Test]
    public async Task Timing_changes_neither_the_answer_nor_stdout()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        var output = new StringWriter();
        var error = new StringWriter();

        (await CliApplication.RunAsync(["index", playground.SolutionPath], output, error, workingDirectory)).ShouldBe(0);

        var untimed = new StringWriter();
        var untimedError = new StringWriter();
        var timed = new StringWriter();
        var timedError = new StringWriter();
        string[] query = ["callers", "CreateTeamAsync", "--entrypoints", "--no-live"];

        var untimedExit = await CliApplication.RunAsync(query, untimed, untimedError, workingDirectory);
        var timedExit = await CliApplication.RunAsync([.. query, "--time"], timed, timedError, workingDirectory);

        timedExit.ShouldBe(untimedExit);
        timed.ToString().ShouldBe(untimed.ToString());
        untimedError.ToString().ShouldNotContain("Timing breakdown");
        timedError.ToString().ShouldContain("Timing breakdown");
    }

    // ---- 2. the live "derived layer built this generation" disclosure ----

    // The regression itself: a generation whose graph was materialized by the PLANNER (i.e. through
    // FactSnapshot's cache slot, never through LiveFactSource's memo) must still disclose what that cost.
    [Test]
    public void Build_time_line_reports_a_planner_materialized_call_graph()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = TinyFixture.Snapshot(workspace);

        // What the exact-query planner does, one line: materialize the generation's graph on the snapshot,
        // before any LiveFactSource exists.
        snapshot.ProjectedCallGraph(TinyFixture.Rules());
        var live = new LiveFactSource(snapshot, new RuleSet());

        live.BuildTimes.Count(t => t.Artifact == "traversalGraph").ShouldBe(1);
        live.BuildTimeLine().ShouldContain("traversalGraph");
        // The memo genuinely never ran — this row IS the planner's build, surfaced, not a second one.
        live.BuildTimes.Count.ShouldBe(1);
    }

    // ONCE PER GENERATION, not once per shaping slot. `--raw` legitimately takes a second slot; a second
    // multi-second graph row would read as the generation having paid twice for one artifact.
    [Test]
    public void A_second_shaping_slot_does_not_report_a_second_graph_build()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = TinyFixture.Snapshot(workspace);

        snapshot.ProjectedCallGraph(TinyFixture.Rules());
        var first = snapshot.ProjectedCallGraphBuild;
        first.ShouldNotBeNull();
        first.Value.Artifact.ShouldBe("traversalGraph");

        // A different shape (the `--raw` axis): a second real build, a second cache slot, still ONE row.
        snapshot.ProjectedCallGraph(TinyFixture.RawRules());
        snapshot.ProjectedCallGraphCount.ShouldBe(2);
        snapshot.ProjectedCallGraphBuild!.Value.Elapsed.ShouldBe(first.Value.Elapsed);

        new LiveFactSource(snapshot, new RuleSet()).BuildTimes.Count(t => t.Artifact == "traversalGraph").ShouldBe(1);
    }

    // ResidentIndex.WithRevision rebuilds a snapshot over the SAME facts to publish an exact refinement, and
    // deliberately carries the materialized graph across. The build COST must ride along with it: a revision
    // stamp is not a rebuild, so the generation that inherits the graph is the one that paid for it — and it
    // must still report exactly ONE row, not zero (cost lost) and not two (cost double-counted).
    [Test]
    public void A_revision_stamp_carries_the_graph_cost_across_without_double_reporting()
    {
        using var workspace = new AdhocWorkspace();
        var before = TinyFixture.Snapshot(workspace);
        before.ProjectedCallGraph(TinyFixture.Rules());
        var paid = before.ProjectedCallGraphBuild;
        paid.ShouldNotBeNull();

        var stamped = TinyFixture.WithRevision(before, workspace);
        stamped.ProjectedCallGraphBuild.ShouldBeNull("a fresh snapshot has paid nothing until it inherits");

        stamped.InheritProjectedCallGraphsFrom(before);

        stamped.ProjectedCallGraphCount.ShouldBe(1);
        stamped.ProjectedCallGraphBuild!.Value.Elapsed.ShouldBe(paid.Value.Elapsed);
        var live = new LiveFactSource(stamped, new RuleSet());
        live.BuildTimes.Count(t => t.Artifact == "traversalGraph").ShouldBe(1);
        live.BuildTimeLine().ShouldContain("traversalGraph");
    }

    // The un-routed path still builds the graph through LiveFactSource's memo, and the snapshot's build
    // STRICTLY CONTAINS that memo's (materialization = the memo's shaping pass + delivery edges). Reporting
    // both would print the same milliseconds twice in one disclosure line.
    [Test]
    public void The_memo_and_the_snapshot_never_both_report_the_graph()
    {
        using var workspace = new AdhocWorkspace();
        var snapshot = TinyFixture.Snapshot(workspace);
        var live = new LiveFactSource(snapshot, new RuleSet());

        _ = live.TraversalGraph; // the memo's own build — its row goes in first, in true access order
        snapshot.ProjectedCallGraph(TinyFixture.Rules()); // and then a materialization on top of it

        live.BuildTimes.Count(t => t.Artifact == "traversalGraph").ShouldBe(1);
        live.BuildTimes[0].Artifact.ShouldBe("traversalGraph");
    }

    // A generation nothing has traversed discloses nothing — the note must stay empty rather than acquiring a
    // permanent 0ms graph row.
    [Test]
    public void An_untouched_generation_discloses_no_graph_build()
    {
        using var workspace = new AdhocWorkspace();
        var live = new LiveFactSource(TinyFixture.Snapshot(workspace), new RuleSet());

        live.BuildTimes.ShouldBeEmpty();
        live.BuildTimeLine().ShouldBeEmpty();
    }

    // ---- helpers ----

    private static double PhasePercent(string table, string phase) =>
        PhasePercents(table).TryGetValue(phase, out var pct) ? pct : throw new ShouldAssertException($"no '{phase}' row in:\n{table}");

    // Every non-total phase row of the breakdown, as name -> percent share. The renderer pads the name to 20
    // columns and prints `<wall> <pct>%`, so the name is everything before the first wall figure.
    private static Dictionary<string, double> PhasePercents(string table)
    {
        var rows = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(table, @"^\s{2}(\S(?:.*\S)?)\s+\S+?s\s+(\d+\.\d)%", RegexOptions.Multiline))
        {
            var name = match.Groups[1].Value;
            if (!string.Equals(name, "total", StringComparison.Ordinal))
            {
                rows[name] = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            }
        }

        return rows;
    }

    // A three-node corpus in one real project: enough for a genuine projection (the graph builder walks the
    // whole fact view), small enough that these tests are microseconds. The SHAPE is what matters here, not
    // the scale — PlannerMaterializedGraphTests owns the scale gate.
    private static class TinyFixture
    {
        private const string Target = "M:Tiny.Sink.Target";
        private const string Caller = "M:Tiny.Root.Go";
        private const string Middle = "M:Tiny.Mid.Hop";
        private static readonly string CorpusPath = Path.Combine(Path.GetTempPath(), "rig-query-phase-timing", "Tiny.cs");

        // Two shapes, and they must be genuinely different shapes or the second ProjectedCallGraph call would
        // hit the first one's slot and build nothing. A Cut rule is the `--raw` axis: present here, zeroed there.
        internal static DemandForwardGraphRules Rules() =>
            new(new ForwardCallProjectionRules(ClassifyEventSubscriptions: true), Cut: [], Context: [], Delivery: []);

        internal static DemandForwardGraphRules RawRules() =>
            new(
                new ForwardCallProjectionRules(ClassifyEventSubscriptions: true),
                Cut: [new FactTraversalCutRule("Tiny.Nothing.AtAll", "nothing")],
                Context: [],
                Delivery: []
            );

        internal static FactSnapshot Snapshot(AdhocWorkspace workspace) => Build(workspace, new FactRevision(0));

        // What ResidentIndex.WithRevision does: the SAME base facts and the SAME overlay instance, a new
        // revision stamp. Reference identity on both halves is what InheritProjectedCallGraphsFrom gates on.
        internal static FactSnapshot WithRevision(FactSnapshot snapshot, AdhocWorkspace workspace) =>
            new(
                new FactRevision(snapshot.Revision.Value + 1),
                workspace.CurrentSolution,
                snapshot.BaseFacts,
                snapshot.Overlay,
                snapshot.Dirty,
                snapshot.Delta
            );

        private static FactSnapshot Build(AdhocWorkspace workspace, FactRevision revision)
        {
            if (workspace.CurrentSolution.ProjectIds.Count == 0)
            {
                var project = ProjectId.CreateNewId("Tiny");
                var document = DocumentId.CreateNewId(project, "Tiny.cs");
                workspace.AddSolution(
                    SolutionInfo.Create(
                        SolutionId.CreateNewId(),
                        VersionStamp.Create(),
                        projects:
                        [
                            ProjectInfo.Create(
                                project,
                                VersionStamp.Create(),
                                "Tiny",
                                "Tiny",
                                LanguageNames.CSharp,
                                filePath: Path.ChangeExtension(CorpusPath, ".csproj"),
                                documents:
                                [
                                    DocumentInfo.Create(
                                        document,
                                        "Tiny.cs",
                                        loader: TextLoader.From(
                                            TextAndVersion.Create(SourceText.From(""), VersionStamp.Create(), CorpusPath)
                                        ),
                                        filePath: CorpusPath
                                    ),
                                ]
                            ),
                        ]
                    )
                );
            }

            return new FactSnapshot(
                revision,
                workspace.CurrentSolution,
                Facts(),
                ImmutableDictionary<string, FileFacts>.Empty,
                DirtySet.Empty,
                SnapshotDelta.Empty
            );
        }

        private static AnalysisResult Facts() =>
            new(
                SolutionPath: "/repo/Tiny.sln",
                SourceFiles: [],
                DiRegistrations: [],
                Symbols: [Method(Target, "T:Tiny.Sink"), Method(Middle, "T:Tiny.Mid"), Method(Caller, "T:Tiny.Root")],
                References:
                [
                    new ReferenceFact(Target, RefKinds.Invocation, Middle, "Tiny", true, CorpusPath, 1),
                    new ReferenceFact(Middle, RefKinds.Invocation, Caller, "Tiny", true, CorpusPath, 2),
                ],
                TypeRelations: [],
                DispatchFacts: [],
                AllocationFacts: []
            );

        private static SymbolFact Method(string id, string containingType) =>
            new(id, SymbolKinds.Method, id, "", containingType, "public", "", id, CorpusPath, 1, 1, "Tiny", false);
    }
}
