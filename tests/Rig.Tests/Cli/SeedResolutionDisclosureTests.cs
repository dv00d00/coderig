using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Domain.Functions;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// A pattern argument has THREE outcomes and `reaches`/`path` used to collapse two of them. Both passed the
// raw pattern into the traversal, which resolves it internally, so a pattern that matched NOTHING produced an
// empty answer indistinguishable from a genuine leaf:
//
//   $ rig reaches "MedDBase.…DocumentPreviewBuilder.Get" --store e8858aa90e02-dirty   # no such symbol
//   From: MedDBase.…DocumentPreviewBuilder.Get
//   Reachable methods (<= depth 2147483647): 0
//   Direct effects (real call paths): 0  (fanned out under a loop: 0)
//
// — which reads as "this method does nothing" (it cost a full session of misdiagnosis on 2026-07-27) while
// `tree`/`callers` reported the same input correctly. These tests pin the three outcomes apart: no match =>
// tree's `No symbol matches '<pattern>'.` + exit 1; matched-but-leaf => the answer still succeeds (exit 0)
// and carries a stderr note naming what it resolved to; a normal answer stays untouched and note-free.
public sealed class SeedResolutionDisclosureTests
{
    // The one leaf in the CoreAllocations playground whose in-solution out-degree is ZERO — verified against
    // the installed `rig`: `rig reaches CompilerLoweredScenarios.Sum` reports "Reachable methods … : 1"
    // (itself, at depth 0), where a non-leaf like Program.Main reports 9 and an unmatched pattern reports 0.
    private const string LeafPattern = "CompilerLoweredScenarios.Sum";
    private const string LeafSymbolId = "M:CoreAllocations.CompilerLoweredScenarios.Sum(System.Int32[])";

    private static async Task<(int Exit, string Out, string Err)> RunAsync(string workingDirectory, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApplication.RunAsync(args, output, error, workingDirectory);
        return (exit, output.ToString(), error.ToString());
    }

    private static async Task<string> IndexedWorkspaceAsync(TempPlayground playground)
    {
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        var index = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], index, index, workingDirectory)).ShouldBe(0);
        return workingDirectory;
    }

    [Test]
    public async Task Reaches_separates_an_unmatched_pattern_from_a_leaf_and_from_a_real_answer()
    {
        using var playground = await TempPlayground.CreateCoreAllocationsAsync();
        var workingDirectory = await IndexedWorkspaceAsync(playground);

        // 1. NO MATCH — the regression proper. Pre-fix this printed the From:/Reachable-methods:0 header and
        // exited 0, i.e. it answered a question about a symbol that does not exist.
        var absent = await RunAsync(workingDirectory, "reaches", "NoSuchSymbolAnywhere");
        absent.Exit.ShouldBe(1);
        absent.Out.ShouldContain("No symbol matches 'NoSuchSymbolAnywhere'."); // tree's exact wording
        absent.Out.ShouldNotContain("Reachable methods"); // no empty answer alongside the refusal

        // 2. MATCHED, ZERO OUT-EDGES — a genuine leaf. Zero reach is the CORRECT answer here, so it must stay
        // a success and must NOT be reported as a resolution failure; the stderr note names what resolved.
        var leaf = await RunAsync(workingDirectory, "reaches", LeafPattern);
        leaf.Exit.ShouldBe(0);
        leaf.Out.ShouldContain("Reachable methods (<= depth 2147483647): 1"); // itself, at depth 0
        leaf.Out.ShouldNotContain("No symbol matches");
        leaf.Err.ShouldContain($"note: resolved to {LeafSymbolId}; it makes no in-solution calls (0 call edges).");

        // 3. A NORMAL answer is unchanged and note-free — otherwise the note is noise and stops being read.
        var normal = await RunAsync(workingDirectory, "reaches", "Program.Main");
        normal.Exit.ShouldBe(0);
        normal.Out.ShouldContain("From: Program.Main");
        normal.Out.ShouldNotContain("No symbol matches");
        // Deliberately not pinned to the exact node count (an extraction change legitimately moves it) —
        // only that it walked somewhere, which is what separates this from outcomes 1 and 2.
        normal.Out.ShouldNotContain("Reachable methods (<= depth 2147483647): 0");
        normal.Out.ShouldNotContain("Reachable methods (<= depth 2147483647): 1");
        normal.Err.ShouldNotContain("makes no in-solution calls");
    }

    [Test]
    public async Task Path_names_which_endpoint_failed_to_match_and_still_reports_a_real_but_unreachable_target()
    {
        using var playground = await TempPlayground.CreateCoreAllocationsAsync();
        var workingDirectory = await IndexedWorkspaceAsync(playground);

        // A working path first, so the new pre-search seed gate is proven not to reject a valid `from`
        // (a leaf `from` IS present in its own forward slice — the property the gate relies on).
        var found = await RunAsync(workingDirectory, "path", "Program.Main", "AllocationScenarios.CreateArrays");
        found.Exit.ShouldBe(0);
        found.Out.ShouldContain("M:CoreAllocations.AllocationScenarios.CreateArrays");

        // An unmatched FROM: pre-fix this blamed connectivity ("No path from … to …") for a resolution failure.
        var badFrom = await RunAsync(workingDirectory, "path", "NoSuchSourceXyz", "Program.Main");
        badFrom.Exit.ShouldBe(1);
        badFrom.Out.ShouldContain("No symbol matches 'NoSuchSourceXyz' (the 'from' endpoint).");
        badFrom.Out.ShouldNotContain("No path from");

        // An unmatched TO: same disclosure, attributed to the OTHER endpoint (the two patterns can even be
        // the same text, so the message has to say which one).
        var badTo = await RunAsync(workingDirectory, "path", "Program.Main", "NoSuchTargetXyz");
        badTo.Exit.ShouldBe(1);
        badTo.Out.ShouldContain("No symbol matches 'NoSuchTargetXyz' (the 'to' endpoint).");
        badTo.Out.ShouldNotContain("No path from");

        // The precision guard on the TO check: `CompilerLoweredScenarios.Sum` EXISTS in the store but is not
        // reachable from Main, so it is absent from Main's forward slice — the only graph `path` loads. A
        // graph-only no-match test would libel it as nonexistent; the store-wide check keeps "No path" here.
        var unreachable = await RunAsync(workingDirectory, "path", "Program.Main", LeafPattern);
        unreachable.Exit.ShouldBe(1);
        unreachable.Out.ShouldContain($"No path from 'Program.Main' to '{LeafPattern}'.");
        unreachable.Out.ShouldNotContain("No symbol matches");
    }

    [Test]
    public void The_no_match_line_matches_tree_wording_and_names_the_endpoint_only_when_given()
    {
        var bare = new StringWriter();
        SeedResolutionNotice.ReportNoMatch(bare, "Foo.Bar");
        bare.ToString().Trim().ShouldBe("No symbol matches 'Foo.Bar'.");

        var endpoint = new StringWriter();
        SeedResolutionNotice.ReportNoMatch(endpoint, "Foo.Bar", endpoint: "to");
        endpoint.ToString().Trim().ShouldBe("No symbol matches 'Foo.Bar' (the 'to' endpoint).");
    }

    [Test]
    public void The_zero_out_edges_note_fires_only_for_a_resolved_seed_with_no_successors()
    {
        // Resolved to exactly itself => the note (this is the case that must not read as a failure).
        var leaf = new StringWriter();
        SeedResolutionNotice.NoteIfNoOutEdges(leaf, Reach(("M:App.Svc.Leaf", 0)), maxDepth: int.MaxValue);
        leaf.ToString().ShouldContain("note: resolved to M:App.Svc.Leaf; it makes no in-solution calls (0 call edges).");

        // Something was reached => no note.
        var walked = new StringWriter();
        SeedResolutionNotice.NoteIfNoOutEdges(walked, Reach(("M:App.Svc.Root", 0), ("M:App.Svc.Callee", 1)), maxDepth: int.MaxValue);
        walked.ToString().ShouldBeEmpty();

        // Nothing resolved => no note; that is the no-match outcome, reported on stdout instead.
        var empty = new StringWriter();
        SeedResolutionNotice.NoteIfNoOutEdges(empty, Reach(), maxDepth: int.MaxValue);
        empty.ToString().ShouldBeEmpty();

        // `--depth 0` bounds the walk to depth 0, so a depth-0-only result says nothing about out-degree.
        var bounded = new StringWriter();
        SeedResolutionNotice.NoteIfNoOutEdges(bounded, Reach(("M:App.Svc.Leaf", 0)), maxDepth: 0);
        bounded.ToString().ShouldBeEmpty();
    }

    private static IReadOnlyDictionary<string, FactPathFinder.ReachInfo> Reach(params (string Id, int Depth)[] entries) =>
        entries.ToDictionary(
            e => e.Id,
            e => new FactPathFinder.ReachInfo(Depth: e.Depth, LoopNesting: 0, NearestLoopKind: null, NearestLoopDetail: null),
            StringComparer.Ordinal
        );

    // A FOURTH outcome, found while reviewing the above against the real store: a pattern can name a symbol
    // that is REAL and INDEXED yet can never be a call-graph node. Nodes are methods / bodied accessors /
    // lambdas / ctors — all `M:` ids; `P:` properties, `F:` fields and `E:` events never are (the effect ↔
    // reachability invariant in CLAUDE.md). Reporting a flat "No symbol matches" for those is misleading in
    // exactly the way the old empty result was: it says "no such thing" when the truth is "not traversable —
    // use its accessor". Real-store evidence: `reaches "PerformanceLogger.Factory"` matched no node while
    // `PerformanceLogger.get_Factory` reached 16 methods.
    //
    // Assertions below were taken from ACTUAL installed-`rig` output against a freshly indexed copy of this
    // playground, where `MetadataValuesAttribute.Value` is `P:`-only (its auto-property accessor is
    // `get_Value`, which the pattern does NOT substring-match).
    [Test]
    public async Task A_real_but_non_node_symbol_gets_the_accessor_hint_not_a_flat_denial()
    {
        using var playground = await TempPlayground.CreateCoreAllocationsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        var index = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], index, index, workingDirectory)).ShouldBe(0);

        var (exit, stdout, _) = await RunAsync(workingDirectory, "reaches", "MetadataValuesAttribute.Value");

        exit.ShouldBe(1); // still a failure — it cannot be traversed
        stdout.ShouldContain("is indexed but is not a call-graph node");
        stdout.ShouldContain("P:CoreAllocations.MetadataValuesAttribute.Value"); // names WHAT it found
        stdout.ShouldContain("get_X"); // and the way forward
        // It must NOT be confused with the genuine no-such-name case.
        stdout.ShouldNotContain("No symbol matches");
    }

    [Test]
    public async Task A_genuinely_absent_name_still_gets_the_plain_no_match_line()
    {
        // Negative control for the test above: the accessor hint must fire ONLY when a non-node symbol really
        // exists, or it degrades into noise on every typo.
        using var playground = await TempPlayground.CreateCoreAllocationsAsync();
        var workingDirectory = Path.Combine(playground.RootDirectory, "workspace");
        var index = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], index, index, workingDirectory)).ShouldBe(0);

        var (exit, stdout, _) = await RunAsync(workingDirectory, "reaches", "Totally.Bogus.Thing");

        exit.ShouldBe(1);
        stdout.ShouldContain("No symbol matches 'Totally.Bogus.Thing'.");
        stdout.ShouldNotContain("is indexed but is not a call-graph node");
    }
}
