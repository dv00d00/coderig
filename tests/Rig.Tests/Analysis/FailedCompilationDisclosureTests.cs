using System.Text;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Cli;
using Rig.Cli.CommandLine;
using Rig.Domain.Data;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Analysis;

// The failed-compilation disclosure (docs/spikes/failed-compilation-disclosure-spec.md; the real-data
// finding in docs/backlog/todo/live-index-serves-confident-answers-from-a-broken-compilation.md). What
// these tests pin, in order of how much they cost to get wrong:
//
//   1. the status line does NOT claim health on a tree that did not compile, and NAMES the count;
//   2. an ANSWER computed over such a tree carries the disclosure — not just the boot log;
//   3. broken -> fixed -> broken over ONE retained workspace: the flag CLEARS on the fix and RETURNS on
//      the next break. This is the resident-specific case, and the one where a stale flag hides: in a
//      resident process a flag that only ever accumulates lies "safe" for the whole process lifetime,
//      which is worse than no flag at all because it trains the reader to ignore the marker;
//   4. a CLEAN tree adds NO disclosure noise. Without this arm the feature is indistinguishable from one
//      that fires on everything, which is the failure mode the spec's §6.1 says this design errs toward.
//
// Harness note (spec §5.0(1)): every broken fixture is written by the TEST into the TEMP copy.
// DeepChainPlayground copies playgrounds/DeepChain to a temp dir and restores it there; a non-compiling
// file must never be checked in, because the playground fixtures are shared per session.
[NotInParallel]
public sealed class FailedCompilationDisclosureTests
{
    // A body-local reference to an undefined name: CS0103, located in Db.cs itself, with the PUBLIC
    // SURFACE untouched — so every dependent project still compiles. That is deliberate: it makes the
    // must-not-cascade property observable (only one file is ever flagged) while keeping the edit small
    // enough that broken -> fixed -> broken is a pure text round-trip.
    private const string CleanQueryBody = "public static string Query(string sql) => $\"rows for: {sql}\";";
    private const string BrokenQueryBody = "public static string Query(string sql) => $\"rows for: {undefinedLocalName}\";";

    // Acceptance 1 + 2: on a tree that did not compile, `rig watch --once --query` must not claim
    // health, must quantify the damage on the SAME line as the answer's staleness disclosure, and must
    // put the footer note on stderr.
    [Test]
    public async Task A_broken_tree_replaces_the_health_claim_with_a_quantified_disclosure()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var brokenFile = Path.Combine(playground.WorkingDirectory, "Foundation", "Db.cs");
        await BreakAsync(brokenFile);

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await CliApplication.RunAsync(
            ["watch", playground.SolutionPath, "--once", "--query", "reaches HomePage.Show"],
            output,
            error,
            playground.WorkingDirectory
        );

        var stdout = output.ToString();
        var stderr = error.ToString();
        Report("=== BROKEN TREE — stdout ===", stdout, "=== BROKEN TREE — stderr ===", stderr);

        exitCode.ShouldBe(0, stdout + stderr);

        // (a) NO health claim. This is the whole defect: the original run printed
        // "all projects reconciled" over a tree where nothing had resolved.
        stdout.ShouldNotContain("all projects reconciled");

        // (b) the count, quantified over the population it names, on the answer's own line. The
        // POPULATION is not pinned to a literal: it is every indexed file, which includes the SDK's
        // per-project generated AssemblyInfo/GlobalUsings documents (26 on this playground today), and
        // pinning that would make the test a hostage to the SDK rather than to the disclosure. The
        // numerator IS pinned — exactly one file is flagged, which is also the must-not-cascade property:
        // Db.cs's public surface is untouched, so no dependent project produces a diagnostic of its own.
        var segment = System.Text.RegularExpressions.Regex.Match(
            stdout,
            @"live: facts current as of 0 file\(s\) applied \| (\d+) of (\d+) indexed file\(s\) had compile errors"
        );
        segment.Success.ShouldBeTrue(stdout);
        segment.Groups[1].Value.ShouldBe("1");
        int.Parse(segment.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(12);

        // (c) the footer note, on STDERR (so stdout stays greppable), with the wording rules intact:
        // a DOUBT marker ("may be MISSING or WRONG"), never a claim of wrongness.
        stderr.ShouldContain("note: these facts come from a tree that did not fully compile");
        stderr.ShouldContain("may be MISSING or WRONG");
        stderr.ShouldNotContain("is WRONG.");
    }

    // Acceptance 4 (the anti-false-positive arm): the same command on the UNMODIFIED playground adds no
    // compile-health segment and no note at all. A disclosure that fires on a tree that compiles is a
    // false positive, and the spec is explicit that this must be ZERO there.
    [Test]
    public async Task A_clean_tree_adds_no_compile_health_disclosure()
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

        var stdout = output.ToString();
        var stderr = error.ToString();
        Report("=== CLEAN TREE — stdout ===", stdout, "=== CLEAN TREE — stderr ===", stderr);

        exitCode.ShouldBe(0, stdout + stderr);
        stdout.ShouldContain("live: facts current as of 0 file(s) applied | all projects reconciled");
        stdout.ShouldNotContain("compile error");
        stderr.ShouldNotContain("did not fully compile");
        stderr.ShouldNotContain("produced NO facts");
    }

    // Acceptance 3 — the resident case, over ONE retained workspace and ONE ResidentIndex, exactly as
    // the overlay serves a live edit. Four generations: clean -> broken -> fixed -> broken.
    //
    // Two distinct sticking bugs this catches and nothing else does:
    //   (a) the flag accumulated in a resident set and never cleared, so E2 still reads broken;
    //   (b) diagnostics memoized per file path rather than recomputed from the CURRENT compilation, so
    //       E3 still reads clean.
    // A single-edit test cannot see either by construction.
    [Test]
    public async Task Broken_then_fixed_then_broken_over_one_retained_workspace()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);
        var brokenFilePath = Path.Combine(playground.WorkingDirectory, "Foundation", "Db.cs");
        var cleanText = await File.ReadAllTextAsync(brokenFilePath);
        cleanText.ShouldContain(CleanQueryBody);
        var brokenText = cleanText.Replace(CleanQueryBody, BrokenQueryBody, StringComparison.Ordinal);

        var (baseFacts, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(playground.SolutionPath, rules);
        using var index = new ResidentIndex(workspace, baseFacts, playground.SolutionPath, rules);

        // E0 — the cold baseline. Anti-vacuity: if the playground itself does not compile, every later
        // assertion is meaningless.
        var e0 = Describe(index);
        Report("E0 (cold, clean)", e0);
        e0.ShouldBe("clean");

        // E1 — break it. One file flagged, and ONLY that file: the public surface is untouched, so no
        // dependent project produces a diagnostic of its own (no propagation, per spec §4).
        await ApplyAsync(index, brokenFilePath, brokenText);
        var e1 = Describe(index);
        Report("E1 (broken)", e1);
        e1.ShouldStartWith("Db.cs errors=");
        e1.ShouldContain("codes=CS0103");

        // E2 — revert to the ORIGINAL text. The flag must CLEAR. A resident flag that survives the fix
        // is the headline bug of this whole arm.
        await ApplyAsync(index, brokenFilePath, cleanText);
        var e2 = Describe(index);
        Report("E2 (fixed)", e2);
        e2.ShouldBe("clean", "the compile-error flag must CLEAR when the file is fixed, not persist for the process lifetime");

        // E3 — break it again. The flag must RETURN, with the same evidence E1 recorded.
        await ApplyAsync(index, brokenFilePath, brokenText);
        var e3 = Describe(index);
        Report("E3 (broken again)", e3);
        e3.ShouldBe(e1, "re-breaking the same file must reproduce E1's evidence exactly");
    }

    // The project channel's WORDING, unit-tested against a synthetic health record. A project that
    // produced no facts must read as a RECALL warning: zero facts make an ABSENCE argument unsound, so
    // "no callers" / "unreachable" is not evidence for symbols declared there. That is a strictly worse
    // failure than a doubtful presence, which is why it is its own note line rather than a clause.
    [Test]
    public void A_project_that_produced_no_facts_is_disclosed_as_a_recall_warning()
    {
        var health = new CompilationHealth(
            Files: [],
            PartialProjects: [new ProjectCompileFailure("Contracts", ProjectCompileFailure.NoCompilation)],
            UnlocatedErrorCount: 1
        );

        var indexed = new HashSet<string>(Enumerable.Range(0, 12).Select(i => $"C:/src/File{i}.cs"), StringComparer.OrdinalIgnoreCase);
        var segments = CompilationHealthNotice.StatusSegments(health, indexed);
        var note = CompilationHealthNotice.Note(health, indexed);
        Report("PROJECT CHANNEL — status", string.Join(" | ", segments), "PROJECT CHANNEL — note", string.Join("\n", note));

        segments.ShouldContain("1 project(s) produced NO facts");
        segments.ShouldNotContain("all projects reconciled");
        note.ShouldContain(line =>
            line.Contains("1 project(s) produced NO facts at all (Contracts: no_compilation)", StringComparison.Ordinal)
        );
        note.ShouldContain(line =>
            line.Contains("\"no callers\" / \"unreachable\" is NOT evidence for those symbols", StringComparison.Ordinal)
        );
    }

    // A clean record must produce nothing at all — the notice's own anti-false-positive gate, checked
    // without paying for a design-time build.
    [Test]
    public void A_clean_health_record_produces_no_segments_and_no_note()
    {
        var indexed = new HashSet<string>(["C:/src/A.cs"], StringComparer.OrdinalIgnoreCase);
        CompilationHealthNotice.StatusSegments(CompilationHealth.Empty, indexed).ShouldBeEmpty();
        CompilationHealthNotice.Note(CompilationHealth.Empty, indexed).ShouldBeEmpty();
        CompilationHealthNotice.StatusSegments(health: null, indexed).ShouldBeEmpty();
        CompilationHealthNotice.Note(health: null, indexed).ShouldBeEmpty();
    }

    // The ratio's two halves must come from ONE population. The real-data run that motivated this test
    // printed "10648 of 10565 indexed file(s)" on an unrestored MedDBase clone: Roslyn reports errors in
    // files rig never indexed (obj/ AssemblyInfo and anything the classifier skipped), and counting them
    // in the numerator while excluding them from the denominator produced a numerator larger than its
    // denominator. Files outside the indexed set are disclosed SEPARATELY — neither folded in nor hidden.
    [Test]
    public void Files_outside_the_indexed_set_are_disclosed_separately_and_never_break_the_ratio()
    {
        var indexed = new HashSet<string>(["C:/src/A.cs", "C:/src/B.cs"], StringComparer.OrdinalIgnoreCase);
        var health = new CompilationHealth(
            Files:
            [
                new FileCompileHealth("C:/src/A.cs", ErrorCount: 2, ErrorCodes: "CS0103", FirstMessage: "x"),
                new FileCompileHealth("C:/src/obj/A.AssemblyInfo.cs", ErrorCount: 3, ErrorCodes: "CS0234", FirstMessage: "y"),
            ],
            PartialProjects: [],
            UnlocatedErrorCount: 0
        );

        var segments = CompilationHealthNotice.StatusSegments(health, indexed);
        var note = CompilationHealthNotice.Note(health, indexed);
        Report("RATIO — status", string.Join(" | ", segments), "RATIO — note", string.Join("\n", note));

        segments.ShouldHaveSingleItem().ShouldBe("1 of 2 indexed file(s) had compile errors (+1 outside the indexed set)");
        note.ShouldHaveSingleItem()
            .ShouldBe(
                "note: these facts come from a tree that did not fully compile — 1 of 2 indexed file(s) had compile errors, "
                    + "plus 1 file(s) outside the indexed set (5 error diagnostic(s) in total), so facts from them may be "
                    + "MISSING or WRONG."
            );
    }

    private static async Task ApplyAsync(ResidentIndex index, string filePath, string text)
    {
        await index.ApplyEditAsync(filePath, SourceText.From(text, Encoding.UTF8));
        // Drain the cascade too: the eager arm covers the edited file, the cascade covers its dependents,
        // and BOTH must have refreshed before the flag set is read — otherwise a passing test could be
        // reading a half-converged generation.
        await index.ReconcileAsync();
    }

    private static async Task BreakAsync(string filePath)
    {
        var text = await File.ReadAllTextAsync(filePath);
        text.ShouldContain(CleanQueryBody);
        await File.WriteAllTextAsync(filePath, text.Replace(CleanQueryBody, BrokenQueryBody, StringComparison.Ordinal));
    }

    // A stable, path-independent rendering of the current generation's per-file flag set: "clean", or
    // one "<file> errors=N codes=..." entry per flagged file. Paths are reduced to their file name so
    // the string can be compared across generations (and printed) without the temp directory in it.
    private static string Describe(ResidentIndex index)
    {
        var health = index.CurrentFacts.CompilationHealth ?? CompilationHealth.Empty;
        if (health.Files.Count == 0)
        {
            return health.PartialProjects.Count == 0 && health.UnlocatedErrorCount == 0
                ? "clean"
                : $"no flagged files, partialProjects={health.PartialProjects.Count}, unlocated={health.UnlocatedErrorCount}";
        }

        return string.Join(
            "; ",
            health
                .Files.OrderBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(f => $"{Path.GetFileName(f.FilePath)} errors={f.ErrorCount} codes={f.ErrorCodes}")
        );
    }

    // Evidence to a FILE, never Console.WriteLine — TUnit on Microsoft.Testing.Platform swallows console
    // output, so a Console line here would read like observability and provide none. Set
    // RIG_PARITY_REPORT to a path to collect the real status lines and footers these tests produced.
    private static readonly object ReportLock = new();

    private static void Report(params string[] parts)
    {
        var path = Environment.GetEnvironmentVariable("RIG_PARITY_REPORT");
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
                    File.AppendAllText(path, string.Join(Environment.NewLine, parts) + Environment.NewLine);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(25);
                }
            }
        }
    }
}
