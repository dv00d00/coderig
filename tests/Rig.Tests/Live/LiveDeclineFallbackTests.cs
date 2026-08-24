using System.Text.Json;
using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Live;

// A LIVE QUERY THAT CANNOT BE ANSWERED MUST FALL BACK, NOT DIE — whichever half of the host failed.
//
// The resident host can fail a query in two places, and until this slice they had opposite outcomes:
//
//   * EXECUTION — a demand projection will not load. LiveQueryRunner.RunRequestAsync catches the two
//     Demand*Unavailable exceptions and DECLINES; the client reads the store and states why. The user gets
//     an answer.
//   * PREPARATION — exactness cannot be established before the query even starts: a refinement returned
//     ExactUnavailable, the file watcher overflowed, the source topology changed, or the resident snapshot
//     kept moving under the refinement. This was rendered as an exit-2 ANSWER, which the transport faithfully
//     carried to the client and printed. The user got NOTHING — and because the overflow/topology flags are
//     STICKY for the life of the host, on a large solution that killed every routed query from the first
//     create/delete/rename onward, with a perfectly good store answer one fallback away.
//
// Same class of failure, so the same policy: both decline. These tests pin that, and pin the thing that makes
// a silent fallback unacceptable — the store is a SEPARATELY INDEXED snapshot of this tree, so an undisclosed
// fallback would hand back an answer about different code. The disclosure is therefore two stderr lines that
// must BOTH be present: LiveRoute's reason ("why live declined") and StoreAnswerDisclosure's provenance line
// ("which snapshot answered"). stdout stays byte-identical to the plain store answer, because it IS one.
[NotInParallel]
public sealed class LiveDeclineFallbackTests
{
    // THE ACCEPTANCE CASE, end to end and with nothing stubbed: a real indexed store, a real resident host, a
    // real named-pipe endpoint, and a real preparation failure (the sticky topology-changed flag, forced
    // through the same method the FileSystemWatcher callback uses rather than by racing the watcher).
    //
    // The bar is exit 0 with the store's own answer, NOT exit 2 with nothing — and stdout compared against the
    // `--no-live` run in the same directory rather than against a string typed here, because "the store
    // answered" is exactly the claim and a hand-written expectation could not distinguish it from a coincidence.
    [Test]
    public async Task A_preparation_failure_falls_back_to_the_store_and_discloses_the_reason_and_the_store()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var indexLog = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], indexLog, indexLog, playground.WorkingDirectory)).ShouldBe(
            0,
            indexLog.ToString()
        );

        var hostLog = new StringWriter();
        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: RuleSetLoader.Load(playground.WorkingDirectory),
            buildCacheDir: null,
            output: hostLog,
            watch: false,
            workingDirectory: playground.WorkingDirectory
        );

        // The CONTROL: the same question answered by the resident host while it is still healthy. Without it
        // this test would also pass against a transport that never routed anything in the first place.
        var routed = await host.ServeAsync(Request(playground.WorkingDirectory));
        routed.DeclineReason.ShouldBeNull(routed.DeclineReason);
        routed.Exit.ShouldBe(0, routed.Out + routed.Err);

        // THE FAILURE, forced: a source file created/deleted/renamed makes the retained solution topology
        // stale, which is one of the four producers of an unavailable preparation and the easiest to force
        // deterministically. Sticky for the process lifetime, so every query after this one hits it.
        host.RecordTopologyChange(Path.Combine(playground.WorkingDirectory, "BrandNew.cs"), "created");

        // HOST LEVEL: a decline, not an answer. A decline carries no rendered output on purpose — an empty
        // exit-2 answer would read to the client as "no matches" and be printed as the result.
        var declined = await host.ServeAsync(Request(playground.WorkingDirectory));
        declined.DeclineReason.ShouldNotBeNull();
        declined.DeclineReason.ShouldContain("exact reaches unavailable at resident revision");
        declined.DeclineReason.ShouldContain("source topology changed");
        declined.DeclineReason.ShouldContain("restart the watcher and retry");
        declined.Out.ShouldBeEmpty();
        declined.Err.ShouldBeEmpty();

        // CLIENT LEVEL: publish the endpoint and ask the way a user does.
        await using var server = LiveQueryServer.Start(playground.WorkingDirectory, host.ServeAsync, hostLog);
        (await server.WaitUntilReadyAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue("the resident host never published its endpoint");

        var (exitCode, standardOut, standardError) = await RunCliAsync(playground.WorkingDirectory, "reaches", "HomePage.Show");

        // EXIT 0 AND AN ANSWER — the whole point. Before this slice: exit 2 and an empty stdout.
        exitCode.ShouldBe(0, standardOut + standardError);
        standardOut.ShouldNotBeEmpty();

        // WHY live declined…
        standardError.ShouldContain("live: a resident index is watching this directory but did not answer");
        standardError.ShouldContain("exact reaches unavailable at resident revision");
        standardError.ShouldContain("answering from the .rig store instead");
        standardError.ShouldContain("the store is a separately indexed snapshot, and the `store:` line below names it and its freshness.");
        // …and WHICH source answered instead. Two halves of one disclosure: the store is a different snapshot
        // of this tree, so naming the reason without naming the source would still leave the reader unable to
        // tell what code the answer is about.
        standardError.ShouldContain("store: ");

        // And it really is the STORE's answer, byte for byte: the same question with routing off.
        var (forcedExit, forcedOut, _) = await RunCliAsync(playground.WorkingDirectory, "reaches", "HomePage.Show", "--no-live");
        forcedExit.ShouldBe(0);
        standardOut.ShouldBe(forcedOut);
        // No resident source line: the answer did NOT come from the live index, and the positive marker for
        // that must be absent (present => resident facts, absent => the store, no third possibility).
        standardOut.ShouldNotContain("live:");
        standardError.ShouldNotContain("live: facts from resident index");
    }

    // EVERY producer of an unavailable preparation goes through ONE reason builder, so the transport cannot
    // grow a fourth case that quietly keeps the old terminal-error shape. The four strings here are the four
    // reasons WatchHost.CaptureForQueryAsync can attach, plus a budget reason for the remedy conditional.
    //
    // The two channels share the sentence: the stdin loop still RENDERS it (it answers in process and has no
    // store to fall back to), the transport DECLINES with it. Pinning that they agree is what stops the
    // rendered and declined wordings drifting apart.
    [Test]
    public void Every_unavailable_reason_declines_through_one_sentence_shared_with_the_rendered_answer()
    {
        string[] reasons =
        [
            WatchHost.TopologyStatusSegment,
            "file-watcher overflowed — exactness cannot be established; restart required",
            "resident snapshot changed repeatedly while exact reaches refinement was running",
            "exact reaches refinement is not implemented",
        ];

        foreach (var reason in reasons)
        {
            var decline = LiveQueryRunner.ExactUnavailableDecline("reaches", 11, reason);

            decline.ShouldContain("exact reaches unavailable at resident revision 11");
            decline.ShouldContain(reason);
            // A stale-state failure is worth retrying after a restart, so the remedy is attached…
            decline.ShouldContain("restart the watcher and retry");
            // …and the rendered (stdin-loop) answer is the SAME sentence, prefixed and newline-terminated.
            LiveQueryRunner.ExactUnavailable("reaches", 11, reason).Err.ShouldBe($"live: {decline}\n");
        }

        // …but a BUDGET failure is the same size after a restart, so it does not get the remedy. Pinned here
        // because the decline path now carries this sentence to the user's terminal via LiveRoute.
        var budget = LiveQueryRunner.ExactUnavailableDecline("tree", 4, "demand forward graph exceeded the node cap");
        budget.ShouldContain("node cap");
        budget.ShouldNotContain("restart the watcher and retry");
    }

    private static LiveQueryRequest Request(string workingDirectory) =>
        new(
            Protocol: LiveQueryTransport.Protocol,
            Verb: LiveQueryVerbs.Reaches,
            WorkingDirectory: workingDirectory,
            Options: JsonSerializer.Serialize(
                new ReachesCommand.Options(
                    FromPattern: "HomePage.Show",
                    Async: false,
                    IncludeDelivery: false,
                    Raw: false,
                    ExtraRules: [],
                    Depth: null,
                    Format: null,
                    Only: [],
                    Exclude: [],
                    Intrinsic: false,
                    Limit: null,
                    Time: false
                ),
                LiveQueryTransport.Json
            )
        );

    private static async Task<(int Exit, string Out, string Err)> RunCliAsync(string workingDirectory, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await CliApplication.RunAsync(args, output, error, workingDirectory);
        return (exitCode, output.ToString(), error.ToString());
    }
}
