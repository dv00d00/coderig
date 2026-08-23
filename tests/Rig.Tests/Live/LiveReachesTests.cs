using System.Globalization;
using System.Text;
using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Live;

// THE POINT-OF-THE-PROGRAM GATE. `rig watch` has kept an AnalysisResult ~0.75s current since the resident
// slice, and LiveFactSourceParityTests proved the derived artifacts projected off it are set-identical to a
// store's. Neither of those is a usable answer. These tests pin the first END-TO-END live answer: `reaches`
// served from resident memory, and served CORRECTLY.
//
// Two claims are made, and both are MEASURED rather than argued:
//
//  1. EQUALITY WITH THE STORE. For each pattern the same tree is indexed to a real .rig store and queried
//     through the CLI, then queried through the live source, and the two renderings are compared as text. This
//     is not a formality: the store path derives effects from SQL-BOUNDED inputs (SqlReachability narrows them
//     to the pattern's reach closure) while the live path has no SQL and derives over the WHOLE fact set, so
//     the two derivations genuinely see different input sizes. The ANSWER agrees because the command filters
//     effects by `reachable.ContainsKey(EnclosingSymbolId)` on both paths — an extra effect can only belong to
//     an unreachable method, which the filter drops. If that ever stops holding, THIS is where it surfaces,
//     with the differing lines printed.
//
//     Intrinsic hiding is counted AFTER the reachable-method filter. That ordering matters for live demand
//     refinement: a disjoint dirty project must not change this answer's stderr note, and it also makes the
//     live whole-generation derivation byte-identical to the store's bounded input on both streams.
//
//  2. THE EDIT IS REFLECTED. A live answer that does not change when the code changes is worthless however
//     fast it is. Reaches_reflects_a_disk_edit_the_pre_edit_answer_did_not asserts the pre-edit answer does
//     NOT contain the new effect and the post-edit answer DOES — the anti-vacuous form, because an assertion
//     that only checks the post-edit answer would pass against a source that always reported the effect.
//
// Measurement discipline (the lesson this program paid for twice): per-artifact derived-layer costs are
// written to the file named by RIG_LIVE_REPORT, never Console — TUnit does not surface console output in its
// default mode, so a Console line here would be a dead instrument that looks like observability.
public sealed class LiveReachesTests
{
    private static readonly object ReportLock = new();

    // DeepChain: a 7-project reference chain whose entry point reaches the DB effect five project hops down
    // through an interface dispatch. Chosen for the comparison because the dispatch fan-out and the deep
    // closure are exactly what a bounded-vs-whole-store difference would show up in.
    private static readonly string[] DeepChainPatterns =
    [
        "HomePage.Show",
        "BookingController.Book",
        "BookingService.Book",
        "PatientRepository.GetById",
        "NotificationRelay.Relay",
        "ChannelBase.Notify",
        "Db.Query",
    ];

    // EntryPointEffects: the effect-RICH playground (EF Core, Redis, HttpClient, a real C# event with
    // delivery edges, loops, cycles). Chosen as the second playground because it exercises the effect
    // derivation and the graph-shaping seams the DeepChain chain barely touches.
    private static readonly string[] EffectRichPatterns =
    [
        "TeamWorkflow.LoadTeamSummaryAsync",
        "TeamWorkflow.CreateTeamAsync",
        "BillingClient.LoadInvoicesAsync",
        "TeamWorkflow.ProcessBatchAsync",
        "TeamRepository.AddAsync",
        "SavePublisher.Raise",
        "CycleFixture.MutualA",
    ];

    [Test]
    public async Task Live_reaches_equals_the_store_answer_on_deep_chain()
    {
        using var playground = await DeepChainPlayground.CreateAsync();

        // DEPLOYMENT ATTRIBUTION ON. Without a deployments.json both paths short-circuit to DeploymentMap.Empty
        // and BuildEpContextAsync returns null before doing any work — so the live EP-context branch (the
        // in-memory twin of EntryPointContext's tier-3 derive: rule EPs + class-inheritance EPs + promoted
        // handoff origins, memoized per generation) would never execute and this comparison would silently
        // cover none of it. One file turns that whole branch on, on BOTH sides, and diffs the result.
        await File.WriteAllTextAsync(
            Path.Combine(playground.WorkingDirectory, "deployments.json"),
            """{ "services": [ { "name": "web", "host": "Web/Web.csproj", "kind": "site" } ] }"""
        );

        // …and it is asserted to be ON, not merely configured: the deployment chip must actually render, or a
        // regression that dropped it on BOTH paths would still compare equal and pass.
        await AssertLiveEqualsStoreAsync(
            "DeepChain",
            playground.WorkingDirectory,
            playground.SolutionPath,
            DeepChainPatterns,
            requiredInEveryAnswer: "⟦web (site)⟧"
        );
    }

    [Test]
    public async Task Live_reaches_equals_the_store_answer_on_the_effect_rich_playground()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        await AssertLiveEqualsStoreAsync("EntryPointEffects", playground.WorkingDirectory, playground.SolutionPath, EffectRichPatterns);
    }

    // THE WHOLE PROGRAM, IN ONE TEST: boot the resident index, ask a question, EDIT a file on disk, ask the
    // SAME question, and get a different (correct) answer — no re-index, no store write, no restart.
    //
    // Run on EntryPointEffects, not DeepChain, for a measured reason: under the default rule set DeepChain
    // derives ZERO effects (its `Foundation.Db.Query` matches no effect rule — every DeepChain pattern reports
    // "Direct effects (real call paths): 0"), so an effect-appears assertion there would be untestable.
    // EntryPointEffects ships its own rig.rules.json and real EF Core / Redis / HttpClient effects.
    [Test]
    public async Task Reaches_reflects_a_disk_edit_the_pre_edit_answer_did_not()
    {
        using var playground = await TempPlayground.CreateEntryPointEffectsAsync();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);
        var hostLog = new StringWriter();

        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: rules,
            buildCacheDir: null,
            output: hostLog,
            watch: true,
            workingDirectory: playground.WorkingDirectory
        );

        // BEFORE. AddAsync writes and commits; it does not READ. The pre-edit answer must therefore NOT carry
        // an `efcore read` — this is the anti-vacuity half, and without it the test would also pass against a
        // source that reported the read unconditionally.
        var before = await host.AnswerQueryAsync("reaches TeamRepository.AddAsync");
        before.ShouldContain("From: TeamRepository.AddAsync");
        before.ShouldContain("efcore pending_write");
        before.ShouldContain("efcore commit");
        before.ShouldNotContain("efcore read");
        Report($"[live/edit] BEFORE{Environment.NewLine}{before}");

        // The edit, written to DISK: AddAsync gains a read of the same DbSet before writing to it. The
        // watcher picks the save up, the eager arm re-extracts the file, and the next query sees it.
        var editedFilePath = Path.Combine(playground.WorkingDirectory, "EntryPointEffects.Api", "Services", "TeamRepository.cs");
        var originalText = await File.ReadAllTextAsync(editedFilePath);
        const string Marker = "public async Task AddAsync(Team team)\n    {\n";
        var normalized = originalText.Replace("\r\n", "\n", StringComparison.Ordinal);
        normalized.ShouldContain(Marker);
        await File.WriteAllTextAsync(
            editedFilePath,
            normalized.Replace(Marker, Marker + "        await _db.Teams.ToListAsync();\n", StringComparison.Ordinal)
        );

        // AFTER. Poll the ANSWER, not an internal counter: the assertion is about what a CALLER is told, which
        // is the only thing that matters, and the eager arm makes the edit servable well before the cascade
        // finishes reconciling.
        var after = await WaitForAnswerAsync(
            host,
            "reaches TeamRepository.AddAsync",
            answer => answer.Contains("efcore read", StringComparison.Ordinal),
            TimeSpan.FromSeconds(120),
            "the live reaches answer never reflected the disk edit"
        );
        Report($"[live/edit] AFTER{Environment.NewLine}{after}");

        after.ShouldContain("From: TeamRepository.AddAsync");
        after.ShouldContain("efcore read");
        // The pre-existing effects must SURVIVE the edit — a live answer that gains a fact by losing one is
        // not an answer, and a whole-file re-extract that replaced rather than merged would show up here.
        after.ShouldContain("efcore pending_write");
        after.ShouldContain("efcore commit");
    }

    // Staleness is not decoration: an answer served off a partially-reconciled tree that does not SAY so is
    // the exact failure mode this program exists to remove. Every live answer carries the disclosure line.
    [Test]
    public async Task Every_live_answer_is_prefixed_with_the_staleness_disclosure()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);

        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: rules,
            buildCacheDir: null,
            output: new StringWriter(),
            watch: false,
            workingDirectory: playground.WorkingDirectory
        );

        var answered = await host.AnswerQueryAsync("reaches HomePage.Show");
        answered.Split(Environment.NewLine)[0].ShouldBe("live: facts current as of 0 file(s) applied | all projects reconciled");
        answered.ShouldContain("From: HomePage.Show");
        // First query in the generation, so the derived layer was built now and its cost is disclosed.
        answered.ShouldContain("live: derived layer built this generation: ");
        answered.ShouldContain("traversalGraph ");
        answered.ShouldContain("effects ");

        // A SECOND query against the same generation reuses the memo, so it reports no build cost at all —
        // which is the resident host's entire advantage over a cold query, stated as an assertion.
        var second = await host.AnswerQueryAsync("reaches BookingController.Book");
        second.ShouldContain("From: BookingController.Book");
        second.ShouldNotContain("live: derived layer built this generation");

        // An unrecognized query says what IS supported rather than failing obscurely.
        var unsupported = await host.AnswerQueryAsync("hazards Foo");
        unsupported.ShouldContain("live: unsupported query 'hazards'");
        unsupported.ShouldContain("supported live queries: `reaches <pattern>`");
        var blank = await host.AnswerQueryAsync("reaches");
        blank.ShouldContain("`reaches` needs an entry-point pattern");
    }

    // Index the tree to a real store in the SAME working directory the live host uses (so both sides resolve
    // the identical rig.rules.json / deployments.json), then compare the two renderings pattern by pattern.
    private static async Task AssertLiveEqualsStoreAsync(
        string label,
        string workingDirectory,
        string solutionPath,
        string[] patterns,
        string? requiredInEveryAnswer = null
    )
    {
        var indexLog = new StringWriter();
        (await CliApplication.RunAsync(["index", solutionPath], indexLog, indexLog, workingDirectory)).ShouldBe(0, indexLog.ToString());

        var rules = RuleSetLoader.Load(workingDirectory);
        await using var host = await WatchHost.StartAsync(
            solutionPath: solutionPath,
            rules: rules,
            buildCacheDir: null,
            output: new StringWriter(),
            watch: false,
            workingDirectory: workingDirectory
        );

        var facts = await host.GetCurrentFactsAsync();
        var live = new LiveFactSource(facts, rules);

        var differences = new StringBuilder();
        var compared = 0;
        foreach (var pattern in patterns)
        {
            var storeOut = new StringWriter();
            var storeErr = new StringWriter();
            var storeExit = await CliApplication.RunAsync(["reaches", pattern], storeOut, storeErr, workingDirectory);

            var answer = await LiveQueryRunner.AnswerAsync($"reaches {pattern}", live, workingDirectory);
            Report($"[live/parity] {label} '{pattern}' STORE (exit {storeExit}):{Environment.NewLine}{storeOut}{storeErr}");

            // Anti-vacuity: a pattern that resolves to nothing on BOTH sides would compare equal and prove
            // nothing, so every pattern in the list must be a real answer on the store side.
            storeExit.ShouldBe(
                0,
                $"[{label}] '{pattern}' did not resolve on the STORE side — the comparison would be vacuous.{storeOut}{storeErr}"
            );
            storeOut.ToString().ShouldContain($"From: {pattern}");

            Diff(differences, label, pattern, "stdout", storeOut.ToString(), answer.Out);
            Diff(
                differences,
                label,
                pattern,
                "stderr",
                AnswerStreamParity.WithoutImmutableStoreDisclosure(storeErr.ToString()),
                answer.Err
            );

            answer.Exit.ShouldBe(storeExit, $"[{label}] '{pattern}': exit code differs (store={storeExit}, live={answer.Exit}).");
            if (requiredInEveryAnswer is not null)
            {
                answer
                    .Out.Contains(requiredInEveryAnswer, StringComparison.Ordinal)
                    .ShouldBeTrue(
                        $"[{label}] '{pattern}': the live answer is missing '{requiredInEveryAnswer}' — that feature is not being exercised."
                    );
            }

            compared++;
        }

        compared.ShouldBe(patterns.Length);
        Report($"[live/parity] {label}: {compared} pattern(s) compared, derived layer: {live.BuildTimeLine()}");
        differences.Length.ShouldBe(
            0,
            $"[{label}] the LIVE reaches answer differs from the STORE answer:{Environment.NewLine}{differences}"
        );
    }

    // Line-by-line, because a whole-blob mismatch message on a 40-line rendering is unreadable and the useful
    // information is WHICH line moved.
    private static void Diff(StringBuilder into, string label, string pattern, string stream, string store, string live)
    {
        if (string.Equals(store, live, StringComparison.Ordinal))
        {
            return;
        }

        var storeLines = store.Split(Environment.NewLine);
        var liveLines = live.Split(Environment.NewLine);
        into.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}--- [{label}] {pattern} ({stream}) ---");
        for (var i = 0; i < Math.Max(storeLines.Length, liveLines.Length); i++)
        {
            var s = i < storeLines.Length ? storeLines[i] : "<absent>";
            var l = i < liveLines.Length ? liveLines[i] : "<absent>";
            if (!string.Equals(s, l, StringComparison.Ordinal))
            {
                into.Append(
                    CultureInfo.InvariantCulture,
                    $"{Environment.NewLine}  line {i + 1} STORE: {s}{Environment.NewLine}  line {i + 1} LIVE : {l}"
                );
            }
        }
    }

    private static async Task<string> WaitForAnswerAsync(
        WatchHost host,
        string query,
        Func<string, bool> until,
        TimeSpan timeout,
        string reason
    )
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = "";
        while (DateTime.UtcNow < deadline)
        {
            last = await host.AnswerQueryAsync(query);
            if (until(last))
            {
                return last;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Timed out after {timeout.TotalSeconds:F0}s: {reason}. Last answer:{Environment.NewLine}{last}");
    }

    // Measurements and the before/after answers, to a FILE (RIG_LIVE_REPORT) — never Console, which TUnit
    // swallows in its default mode. Assertion failures never depend on this: the differing lines go in the
    // assertion message itself.
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
