using System.Diagnostics;
using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.Commands;
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
        (Directory.Exists(rigDirectory) ? Directory.GetFiles(rigDirectory, "rig.db", SearchOption.AllDirectories) : [])
            .ShouldBeEmpty("`watch --query` must answer without needing or writing a fact store.");
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
                    + "`callers <to>`; `quit` (or EOF) exits."
            );
    }

    // Acceptance 2: the loop end-to-end on a temp copy of DeepChain — boot with the watcher live,
    // write a REAL edit to a file on disk, poll until the watcher applies it, assert the facts
    // reflect it, then poll until the background reconcile clears the disclosure.
    [Test]
    public async Task Watcher_applies_a_disk_edit_and_the_background_reconcile_clears_the_disclosure()
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
        (bootFacts.References ?? []).ShouldNotContain(r =>
            r.TargetSymbolId == QueryTarget && r.EnclosingSymbolId == BookEnclosing
        );

        // The real edit, written to DISK: Book gains a direct Foundation.Db.Query call (the same edit
        // ResidentIndexTests proves fact-identical to a cold index).
        var editedFilePath = Path.Combine(playground.WorkingDirectory, "Business", "BookingService.cs");
        var originalText = await File.ReadAllTextAsync(editedFilePath);
        const string Marker = "var patient = _repository.GetById(patientId);";
        originalText.ShouldContain(Marker);
        var editedText = originalText.Replace(
            Marker,
            "Foundation.Db.Query(\"audit: booking attempt\");\n        " + Marker,
            StringComparison.Ordinal
        );
        await File.WriteAllTextAsync(editedFilePath, editedText);

        // (a) the watcher picks the save up and applies it (debounce + eager re-extract).
        await WaitUntilAsync(
            () => Task.FromResult(host.AppliedFileCount >= 1),
            TimeSpan.FromSeconds(60),
            "the watcher never applied the disk edit"
        );

        // (b) the served facts reflect the edit immediately (the eager arm).
        var facts = await host.GetCurrentFactsAsync();
        (facts.References ?? []).ShouldContain(r =>
            r.RefKind == "invocation" && r.TargetSymbolId == QueryTarget && r.EnclosingSymbolId == BookEnclosing
        );

        // (c) the background reconcile the host kicked drains the cascade; the disclosure clears
        // without any further prompting.
        await WaitUntilAsync(
            async () => (await host.GetUnreconciledProjectsAsync()).Count == 0,
            TimeSpan.FromSeconds(120),
            "the background reconcile never cleared the disclosure"
        );

        // The applied edit survives the reconcile, and the status line reported the loop's stages.
        facts = await host.GetCurrentFactsAsync();
        (facts.References ?? []).ShouldContain(r =>
            r.RefKind == "invocation" && r.TargetSymbolId == QueryTarget && r.EnclosingSymbolId == BookEnclosing
        );
        var printed = output.ToString();
        printed.ShouldContain("file(s) applied");
        printed.ShouldContain("all projects reconciled");
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
