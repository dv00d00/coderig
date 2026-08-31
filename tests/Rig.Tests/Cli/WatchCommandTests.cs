using System.Diagnostics;
using System.Text.RegularExpressions;
using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.Commands;
using Rig.Cli.Live;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Cli;

// The integration slice of live-background-index: `rig watch`, the resident host that owns the loop
// ResidentIndex deliberately does not (watcher, debounce, background reconcile, status lines).
// ResidentIndexTests remains the fact-equivalence gate for the overlay itself; these tests pin the
// HOST: --once boots and exits, and the end-to-end loop applies a real disk edit and clears the
// disclosure. All waits poll a condition with a bounded timeout — no sleep-and-hope.
public sealed class WatchCommandTests
{
    private const string BookEnclosing = "M:Business.BookingService.Book(System.Int32)";
    private const string QueryTarget = "M:Foundation.Db.Query(System.String)";

    // Acceptance 1: `rig watch <solution> --once` cold-boots the resident index, prints the status
    // line, and exits 0 — driven through CliApplication exactly as a user invocation would be.
    [Test]
    public async Task Once_boots_prints_the_status_line_and_exits_zero()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["watch", playground.SolutionPath, "--once"],
            output,
            error,
            playground.WorkingDirectory
        );

        exitCode.ShouldBe(0, output.ToString() + error.ToString());
        var text = output.ToString();
        text.ShouldContain("live: facts current as of 0 file(s) applied");
        text.ShouldContain("all projects reconciled");
        text.ShouldContain("cold boot");
    }

    // The `--query` CLI surface: boot, answer ONE query off the resident facts, exit — the whole
    // point being that this needs no `.rig` store at all (the playground copy has none: DeepChainPlayground
    // skips the checked-in `.rig` directory). Composes with --once, and the answer carries the staleness
    // disclosure rather than the standalone boot status line, so the disclosure is attached to the ANSWER.
    [Test]
    public async Task Once_with_a_query_answers_off_the_resident_facts_with_no_store()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["watch", playground.SolutionPath, "--once", "--query", "reaches HomePage.Show"],
            output,
            error,
            playground.WorkingDirectory
        );

        exitCode.ShouldBe(0, output.ToString() + error.ToString());
        var text = output.ToString();
        text.ShouldContain("live: facts current as of 0 file(s) applied | all projects reconciled");
        text.ShouldContain("From: HomePage.Show");
        // The chain's 8 reachable methods, five project hops deep — the same number the store path reports.
        text.ShouldContain("Reachable methods (<= depth 2147483647): 8");
        text.ShouldContain("live: derived layer built this generation: traversalGraph ");
        // No FACT STORE anywhere: the answer came out of memory. (`.rig/` itself does get created — it holds
        // the design-time-build cache the cold boot shares with `rig index` — but no rig.db is written.)
        var rigDirectory = Path.Combine(playground.WorkingDirectory, ".rig");
        (Directory.Exists(rigDirectory) ? Directory.GetFiles(rigDirectory, "rig.db", SearchOption.AllDirectories) : []).ShouldBeEmpty(
            "`watch --query` must answer without needing or writing a fact store."
        );
    }

    // An unrecognized query says what IS supported instead of failing obscurely.
    [Test]
    public async Task An_unsupported_query_reports_what_is_supported()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["watch", playground.SolutionPath, "--once", "--query", "impact HEAD~1"],
            output,
            error,
            playground.WorkingDirectory
        );

        exitCode.ShouldBe(0, output.ToString() + error.ToString());
        output
            .ToString()
            .ShouldContain(
                "live: unsupported query 'impact' — supported live queries: `reaches <pattern>`, `path <from> <to>`, "
                    + "`callers <to>`, `tree <pattern>` (optionally `--async [--include-delivery]`); `quit` (or EOF) exits."
            );
    }

    // Acceptance 2: the loop end-to-end on a temp copy of DeepChain — boot with the watcher live,
    // write a REAL two-file burst on disk, poll until one eager generation publishes, and prove a
    // forward query pays exactly the intersecting dependent debt before it captures its answer.
    [Test]
    public async Task Watcher_publishes_a_dirty_edit_and_reaches_refines_its_forward_boundary()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);
        var output = new StringWriter();

        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: rules,
            buildCacheDir: null,
            output: output,
            watch: true
        );

        host.AppliedFileCount.ShouldBe(0);
        (await host.GetUnreconciledProjectsAsync()).ShouldBeEmpty();

        // Anti-vacuity: the reference the edit will add must NOT be in the boot facts.
        var bootFacts = await host.GetCurrentFactsAsync();
        (bootFacts.References ?? []).ShouldNotContain(r => r.TargetSymbolId == QueryTarget && r.EnclosingSymbolId == BookEnclosing);
        var bootQueryBodyHash = bootFacts.Symbols!.Single(s => s.SymbolId == QueryTarget).BodyHash;

        // The real edit, written to DISK: Book gains a direct Foundation.Db.Query call (the same edit
        // ResidentIndexTests proves fact-identical to a cold index).
        var editedFilePath = Path.Combine(playground.WorkingDirectory, "Business", "BookingService.cs");
        var originalText = await File.ReadAllTextAsync(editedFilePath);
        const string Marker = "var patient = _repository.GetById(patientId);";
        originalText.ShouldContain(Marker);
        var editedText = originalText.Replace(
            Marker,
            "new SmsChannel().Notify(\"audit\");\n        Foundation.Db.Query(\"audit: booking attempt\");\n        " + Marker,
            StringComparison.Ordinal
        );
        await File.WriteAllTextAsync(editedFilePath, editedText);

        // A second project changes in the same debounce window. Its body-only edit gives this test an
        // independent fact to inspect without changing the reachability shape above.
        var dbFilePath = Path.Combine(playground.WorkingDirectory, "Foundation", "Db.cs");
        var dbText = await File.ReadAllTextAsync(dbFilePath);
        dbText.ShouldContain("rows for:");
        await File.WriteAllTextAsync(dbFilePath, dbText.Replace("rows for:", "db rows for:", StringComparison.Ordinal));

        // (a) the watcher coalesces both saves into one revision and one eager extraction batch.
        await WaitUntilAsync(
            () => Task.FromResult(host.AppliedFileCount >= 2),
            TimeSpan.FromSeconds(60),
            "the watcher never applied the two-file disk burst"
        );
        (await host.GetCurrentRevisionAsync()).ShouldBe(1);

        // (b) the served facts reflect the edit immediately (the eager arm).
        var facts = await host.GetCurrentFactsAsync();
        (facts.References ?? []).ShouldContain(r =>
            r.RefKind == "invocation" && r.TargetSymbolId == QueryTarget && r.EnclosingSymbolId == BookEnclosing
        );
        facts.Symbols!.Single(s => s.SymbolId == QueryTarget).BodyHash.ShouldNotBe(bootQueryBodyHash);

        // (c) Reaches pays the exact forward boundary before capturing its answer. Both changed projects
        // intersect this demand (Business owns the seed; Foundation owns its new target), so the published
        // successor is current and the first query reports the derived factories it actually forced.
        (await host.GetUnreconciledProjectsAsync()).ShouldNotBeEmpty();
        var answer = await host.AnswerQueryAsync("reaches BookingService.Book");
        answer.Split('\n')[0].ShouldContain("all projects reconciled");
        answer.ShouldContain("live: derived layer built this generation:");
        (await host.GetCurrentRevisionAsync()).ShouldBe(2);
        (await host.GetUnreconciledProjectsAsync()).ShouldBeEmpty();
        output.ToString().ShouldNotContain("derived layer warmed");
        output.ToString().ShouldNotContain("| reconcile ");

        // (d) Reconcile-all remains an explicit scheduler/verification primitive, but has no work after
        // the demand-shaped publication and therefore cannot manufacture another revision.
        (await host.ReconcileAllAsync()).ShouldBeFalse();
        (await host.GetCurrentRevisionAsync()).ShouldBe(2);
        (await host.GetUnreconciledProjectsAsync()).ShouldBeEmpty();

        facts = await host.GetCurrentFactsAsync();
        (facts.References ?? []).ShouldContain(r =>
            r.RefKind == "invocation" && r.TargetSymbolId == QueryTarget && r.EnclosingSymbolId == BookEnclosing
        );
        (await host.GetStatusLineAsync()).ShouldContain("all projects reconciled");

        // Atomic saves are classified from the debounced FINAL filesystem state. Both common macOS event
        // orders below include an unretained temporary *.cs and a transiently missing retained target; neither
        // is a topology change once the burst settles, so exact live forward queries must remain available.
        var tempCreateThenReplace = Path.Combine(playground.WorkingDirectory, "Business", "BookingService.atomic-one.cs");
        await File.WriteAllTextAsync(tempCreateThenReplace, editedText.Replace("audit: booking attempt", "audit: atomic one"));
        File.Move(tempCreateThenReplace, editedFilePath, overwrite: true);
        await WaitUntilAsync(
            () => Task.FromResult(host.AppliedFileCount >= 3),
            TimeSpan.FromSeconds(60),
            "the watcher never applied create-temp then replace-target atomic save"
        );
        // A routed tree at depth 1 puts the newly-added SmsChannel.Notify edge exactly on the presentation
        // boundary. The query must refine before capture, then agree byte-for-byte with a fresh store; the
        // repeated raw-live answer exercises the new generation's memo rather than a pre-edit cache entry.
        var depthOneTree = new LiveQueryRequest(
            LiveQueryTransport.Protocol,
            LiveQueryVerbs.Tree,
            playground.WorkingDirectory,
            """{"fromPattern":"BookingService.Book","view":"full","depth":1}"""
        );
        var firstAtomicAnswer = await host.ServeAsync(depthOneTree);
        firstAtomicAnswer.DeclineReason.ShouldBeNull();
        firstAtomicAnswer.Exit.ShouldBe(0);
        firstAtomicAnswer.Out.ShouldContain("SmsChannel.Notify");
        firstAtomicAnswer.Disclosure.ShouldContain("all projects reconciled");
        firstAtomicAnswer.Err.ShouldNotContain("exact tree unavailable");
        firstAtomicAnswer.Err.ShouldNotContain("source topology changed");

        var indexOut = new StringWriter();
        var indexErr = new StringWriter();
        (await CliApplication.RunAsync(["index", playground.SolutionPath], indexOut, indexErr, playground.WorkingDirectory)).ShouldBe(
            0,
            indexOut.ToString() + indexErr
        );
        var storeOut = new StringWriter();
        var storeErr = new StringWriter();
        var storeExit = await CliApplication.RunAsync(
            ["tree", "BookingService.Book", "--view", "full", "--depth", "1"],
            storeOut,
            storeErr,
            playground.WorkingDirectory
        );
        var refinedFacts = new LiveFactSource(await host.GetCurrentFactsAsync(), rules);
        var firstRaw = await LiveQueryRunner.RunRequestAsync(depthOneTree, refinedFacts, playground.WorkingDirectory);
        var repeatedRaw = await LiveQueryRunner.RunRequestAsync(depthOneTree, refinedFacts, playground.WorkingDirectory);
        firstRaw.DeclineReason.ShouldBeNull();
        repeatedRaw.DeclineReason.ShouldBeNull();
        firstRaw.Answer!.Exit.ShouldBe(storeExit);
        firstRaw.Answer.Out.ShouldBe(storeOut.ToString());
        firstRaw.Answer.Err.ShouldBe(AnswerStreamParity.WithoutImmutableStoreDisclosure(storeErr.ToString()));
        repeatedRaw.Answer.ShouldBe(firstRaw.Answer);

        var tempDeleteThenRename = Path.Combine(playground.WorkingDirectory, "Business", "BookingService.atomic-two.cs");
        await File.WriteAllTextAsync(tempDeleteThenRename, editedText.Replace("audit: booking attempt", "audit: atomic two"));
        File.Delete(editedFilePath);
        File.Move(tempDeleteThenRename, editedFilePath);
        await WaitUntilAsync(
            () => Task.FromResult(host.AppliedFileCount >= 4),
            TimeSpan.FromSeconds(60),
            "the watcher never applied delete-target then rename-temp atomic save"
        );
        var secondAtomicAnswer = await host.AnswerQueryAsync("path BookingService.Book Db.Query");
        secondAtomicAnswer.ShouldNotContain("exact path unavailable");
        secondAtomicAnswer.ShouldNotContain("source topology changed");
    }

    // The regression these four tests exist for: before the host owned a reconcile SCHEDULER, the cascade debt
    // ApplyEdits owes was never paid — `ReconcileAllAsync` had no production caller — so the FIRST save of a
    // session left `k project(s) unreconciled` set forever and every Rider file-effects request was declined
    // as stale for the process lifetime. The quiet period is injected in each test so none of them sleeps on
    // the real 750 ms default, and every wait polls a condition rather than hoping.

    // (a) An applied save is reconciled by the loop alone: no ReconcileAllAsync call anywhere in this test,
    // and the whole-file semantic read Rider makes comes back EXACT rather than stale.
    [Test]
    public async Task The_background_loop_pays_the_debt_of_a_save_with_no_explicit_reconcile()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var output = new ConcurrentWriter();
        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: RuleSetLoader.Load(playground.WorkingDirectory),
            buildCacheDir: null,
            output: output,
            watch: true,
            reconcileQuietPeriod: TimeSpan.FromMilliseconds(100)
        );

        (await host.GetUnreconciledProjectsAsync()).ShouldBeEmpty();
        var editedFilePath = await ApplyBookingEditAsync(playground);
        await WaitUntilAsync(
            () => Task.FromResult(host.AppliedFileCount >= 1),
            TimeSpan.FromSeconds(60),
            "the watcher never applied the save"
        );

        // The debt is paid WITHOUT anything calling ReconcileAllAsync — that call is the bug's fingerprint,
        // so its absence here is the point of the test.
        await WaitUntilAsync(
            async () => (await host.GetUnreconciledProjectsAsync()).Count == 0,
            TimeSpan.FromSeconds(60),
            "the background loop never reconciled the debt the save owed"
        );
        host.ReconcileAttemptCount.ShouldBeGreaterThanOrEqualTo(1);
        (await host.GetStatusLineAsync()).ShouldContain("all projects reconciled");
        Regex
            .IsMatch(output.Text, @"live: reconciled [1-9]\d* project\(s\) in \d+\.\d{2}s")
            .ShouldBeTrue($"the reconcile status line is missing or reshaped:\n{output.Text}");

        var effects = await host.ServeFileEffectsAsync(FileEffectRequest(playground, editedFilePath));
        effects.SourceStatus.ShouldBe(RiderFileEffectResponder.SourceExact, effects.Reason);
        effects.Reason.ShouldBeEmpty();
    }

    // (b) N rapid saves cost ONE cascade, not N. Driven through the scheduler's own signal with a counting
    // reconcile delegate: the debounce upstream would hide the coalescing this test is about, and counting
    // attempts is honest where asserting on reformatted console prose would not be.
    [Test]
    public async Task A_burst_of_signals_coalesces_into_one_reconcile()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var calls = 0;
        var output = new ConcurrentWriter();
        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: RuleSetLoader.Load(playground.WorkingDirectory),
            buildCacheDir: null,
            output: output,
            watch: true,
            reconcileQuietPeriod: TimeSpan.FromMilliseconds(200),
            reconcile: _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(false);
            }
        );

        // All five land in the channel synchronously, so they are all in flight well inside the first 200 ms
        // quiet window — the loop cannot have reconciled between them.
        for (var i = 0; i < 5; i++)
        {
            host.RequestReconcile();
        }

        await WaitUntilAsync(
            () => Task.FromResult(host.ReconcileAttemptCount >= 1),
            TimeSpan.FromSeconds(30),
            "the background loop never reconciled the signalled burst"
        );
        // Ten quiet periods of headroom: if the loop fired per signal rather than per burst, the extra four
        // attempts have long since happened by now.
        await Task.Delay(TimeSpan.FromSeconds(2));
        host.ReconcileAttemptCount.ShouldBe(1);
        Volatile.Read(ref calls).ShouldBe(1);
        // Returned false — nothing was published, so the loop must stay silent rather than claim a reconcile.
        output.Text.ShouldNotContain("live: reconciled ");
    }

    // (c) Quiet period 0 is exactly the pre-loop behaviour: eager facts published, debt retained, nothing
    // scheduled. Kept reachable because the debt disclosure is a deliberate product surface.
    [Test]
    public async Task A_zero_quiet_period_disables_the_loop_and_retains_the_debt()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var output = new ConcurrentWriter();
        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: RuleSetLoader.Load(playground.WorkingDirectory),
            buildCacheDir: null,
            output: output,
            watch: true,
            reconcileQuietPeriod: TimeSpan.Zero
        );

        var editedFilePath = await ApplyBookingEditAsync(playground);
        await WaitUntilAsync(
            () => Task.FromResult(host.AppliedFileCount >= 1),
            TimeSpan.FromSeconds(60),
            "the watcher never applied the save"
        );

        await Task.Delay(TimeSpan.FromSeconds(2));
        (await host.GetUnreconciledProjectsAsync()).ShouldNotBeEmpty();
        host.ReconcileAttemptCount.ShouldBe(0);
        output.Text.ShouldNotContain("live: reconciled ");
        (await host.GetStatusLineAsync()).ShouldContain("project(s) unreconciled");
        // And the fail-closed policy is untouched: with debt outstanding, file effects are still declined.
        (await host.ServeFileEffectsAsync(FileEffectRequest(playground, editedFilePath))).SourceStatus.ShouldBe(
            RiderFileEffectResponder.SourceStale
        );
    }

    // (d) A failed reconcile must not kill the scheduler. Letting one exception end the loop would silently
    // restore the permanent-staleness bug: the debt would never be paid again for the process lifetime.
    [Test]
    public async Task A_failed_reconcile_is_disclosed_retains_the_debt_and_leaves_the_loop_alive()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var output = new ConcurrentWriter();
        await using var host = await WatchHost.StartAsync(
            solutionPath: playground.SolutionPath,
            rules: RuleSetLoader.Load(playground.WorkingDirectory),
            buildCacheDir: null,
            output: output,
            watch: true,
            reconcileQuietPeriod: TimeSpan.FromMilliseconds(100),
            reconcile: _ => throw new InvalidOperationException("cascade re-extraction exploded")
        );

        await ApplyBookingEditAsync(playground);
        await WaitUntilAsync(
            () => Task.FromResult(host.AppliedFileCount >= 1),
            TimeSpan.FromSeconds(60),
            "the watcher never applied the save"
        );
        await WaitUntilAsync(
            () => Task.FromResult(output.Text.Contains("live: reconcile FAILED: cascade re-extraction exploded — debt retained")),
            TimeSpan.FromSeconds(60),
            "the failed reconcile was never disclosed"
        );

        (await host.GetUnreconciledProjectsAsync()).ShouldNotBeEmpty();
        var attemptsAfterFailure = host.ReconcileAttemptCount;
        attemptsAfterFailure.ShouldBeGreaterThanOrEqualTo(1);

        // The loop survived it: a later signal is still served.
        host.RequestReconcile();
        await WaitUntilAsync(
            () => Task.FromResult(host.ReconcileAttemptCount > attemptsAfterFailure),
            TimeSpan.FromSeconds(30),
            "the loop died on the first reconcile failure"
        );
        (await host.GetUnreconciledProjectsAsync()).ShouldNotBeEmpty();
    }

    // The same real disk edit the end-to-end test uses: Book gains a direct Foundation.Db.Query call, which
    // makes Business dirty and genuinely owes a dependent cascade. Returns the edited path.
    private static async Task<string> ApplyBookingEditAsync(DeepChainPlayground playground)
    {
        var editedFilePath = Path.Combine(playground.WorkingDirectory, "Business", "BookingService.cs");
        var originalText = await File.ReadAllTextAsync(editedFilePath);
        const string Marker = "var patient = _repository.GetById(patientId);";
        originalText.ShouldContain(Marker);
        await File.WriteAllTextAsync(
            editedFilePath,
            originalText.Replace(Marker, "Foundation.Db.Query(\"audit: booking attempt\");\n        " + Marker, StringComparison.Ordinal)
        );
        return editedFilePath;
    }

    private static RiderFileEffectRequest FileEffectRequest(DeepChainPlayground playground, string filePath) =>
        new(
            LiveQueryTransport.Protocol,
            RiderFileEffectResponder.Verb,
            playground.WorkingDirectory,
            RequestId: "req-1",
            FilePath: filePath,
            ClientSnapshotToken: "token-1"
        );

    // The host has TWO background loops writing status lines while a test reads them, and StringWriter is not
    // thread-safe — an unsynchronized read of one mid-write throws or returns a torn string. Small enough to
    // keep local to the tests that need concurrent reads.
    private sealed class ConcurrentWriter : TextWriter
    {
        private readonly StringWriter _inner = new();

        public override System.Text.Encoding Encoding => _inner.Encoding;

        public string Text
        {
            get
            {
                lock (_inner)
                {
                    return _inner.ToString();
                }
            }
        }

        public override void Write(char value)
        {
            lock (_inner)
            {
                _inner.Write(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_inner)
            {
                _inner.Write(value);
            }
        }

        public override void WriteLine(string? value)
        {
            lock (_inner)
            {
                _inner.WriteLine(value);
            }
        }
    }

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
}
