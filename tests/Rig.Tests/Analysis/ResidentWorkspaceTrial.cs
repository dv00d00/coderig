using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Rules;
using Rig.Domain.Data;

namespace Rig.Tests.Analysis;

// OPT-IN MEASUREMENT HARNESS — not a unit test, and it does NOT run in the normal suite.
//
// Answers the live-background-index program's first real-scale question: on a large solution, how much of a
// cold `rig index` is design-time-builds + workspace assembly (which a RESIDENT process deletes) versus
// extraction (which it does not)? Arm 1 loads and extracts cold while RETAINING the workspace; arm 2 applies
// one in-memory document edit and re-extracts over that same warm workspace. The delta is the resident
// ceiling BEFORE any per-file scoping (that is slice 3).
//
// The edit is TRIVIA-ONLY (a trailing comment). Two reasons: it cannot break compilation on a codebase this
// harness knows nothing about, and it doubles as a property check — a trivia-only edit must invalidate the
// compilation (so the re-extract is real work) while producing an IDENTICAL fact count. A fact-count drift
// here is a finding, not noise.
//
// Gated on RIG_TRIAL_SOLUTION so `dotnet test` is unaffected. Run it deliberately:
//
//   $env:RIG_TRIAL_SOLUTION="C:\git\meddbase-main-application\MedDBase.slnx"
//   $env:RIG_TRIAL_RULES="C:\git\meddbase-analysis\rig.rules.json"
//   $env:RIG_TRIAL_BUILD_CACHE="C:\git\meddbase-analysis\.rig\dtb-cache"
//   $env:RIG_TRIAL_EDIT_FILE="<abs path to a .cs file in the solution>"   # optional; auto-picks otherwise
//   dotnet run --project tests/Rig.Tests -- --treenode-filter "/*/*/ResidentWorkspaceTrial/*"
//
// RIG_TRIAL_BUILD_CACHE matters: without it the design-time-build cache is disabled and the cold arm pays a
// full MSBuild pass, which is not comparable to a `rig index` baseline.
public sealed class ResidentWorkspaceTrial
{
    [Test]
    public async Task Measure_cold_index_versus_warm_workspace_reextract()
    {
        var solutionPath = Environment.GetEnvironmentVariable("RIG_TRIAL_SOLUTION");
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return; // opt-in harness; silent no-op in the normal suite
        }

        // Report to a FILE, not Console: TUnit does not surface Console.WriteLine in its default output
        // mode, so an 8-minute measurement produced zero numbers the first time. Appending as we go also
        // means a crash mid-run still leaves the phase lines collected so far.
        var reportPath =
            Environment.GetEnvironmentVariable("RIG_TRIAL_REPORT") ?? Path.Combine(Path.GetTempPath(), "rig-resident-trial.log");
        var report = new List<string>();
        void Say(string line)
        {
            report.Add(line);
            Console.WriteLine(line);
            try
            {
                File.AppendAllText(reportPath, line + Environment.NewLine);
            }
            catch (IOException) { }
        }

        try
        {
            File.WriteAllText(reportPath, $"# rig resident-workspace trial{Environment.NewLine}");
        }
        catch (IOException) { }

        var rulesPath = Environment.GetEnvironmentVariable("RIG_TRIAL_RULES");
        var buildCacheDir = Environment.GetEnvironmentVariable("RIG_TRIAL_BUILD_CACHE");
        var editFile = Environment.GetEnvironmentVariable("RIG_TRIAL_EDIT_FILE");
        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        var rules = string.IsNullOrWhiteSpace(rulesPath)
            ? RuleSetLoader.Load(workingDirectory)
            : RuleSetLoader.Load(Path.GetDirectoryName(Path.GetFullPath(rulesPath))!, [Path.GetFullPath(rulesPath)]);

        Say($"[trial] solution   : {solutionPath}");
        Say($"[trial] rules      : {rulesPath ?? "(cwd cascade)"}");
        Say($"[trial] build cache: {buildCacheDir ?? "(DISABLED — cold MSBuild, not comparable to rig index)"}");

        // ---- ARM 1: cold load + extract, retaining the workspace ----
        var coldWatch = Stopwatch.StartNew();
        var (coldResult, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(
            solutionPath: solutionPath,
            rules: rules,
            progress: message => Say($"[cold] {message}"),
            excludeTests: true, // mirror `rig index`, which drops test projects
            buildCacheDir: buildCacheDir
        );
        coldWatch.Stop();
        using var _ = workspace;

        var afterColdBytes = GC.GetTotalMemory(forceFullCollection: false);
        var afterColdWorkingSet = Process.GetCurrentProcess().WorkingSet64;
        Say(
            $"[trial] ARM 1 cold load+extract : {coldWatch.Elapsed.TotalSeconds:F1}s"
                + $"  | {Counts(coldResult)}"
                + $"  | managed {afterColdBytes / (1024.0 * 1024 * 1024):F2} GB"
                + $"  | workingSet {afterColdWorkingSet / (1024.0 * 1024 * 1024):F2} GB"
        );

        // ---- Pick the document to edit ----
        var solution = workspace.CurrentSolution;
        var document = PickDocument(solution, editFile);
        if (document is null)
        {
            Say("[trial] no editable C# document found — arm 2 skipped.");
            return;
        }

        Say($"[trial] edit target: {document.FilePath} (project {document.Project.Name})");

        var originalText = await document.GetTextAsync();
        var editedText = SourceText.From(originalText.ToString() + Environment.NewLine + "// rig resident-workspace trial");

        // ---- ARM 2: one in-memory edit, then re-extract over the WARM workspace ----
        var warmWatch = Stopwatch.StartNew();
        var editedSolution = solution.WithDocumentText(document.Id, editedText);
        var warmResult = await SolutionAnalyzer.ExtractFromSolutionAsync(
            solution: editedSolution,
            solutionPath: solutionPath,
            rules: rules
        );
        warmWatch.Stop();

        Say(
            $"[trial] ARM 2 warm re-extract   : {warmWatch.Elapsed.TotalSeconds:F1}s"
                + $"  | {Counts(warmResult)}"
                + $"  | workingSet {Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024 * 1024):F2} GB"
        );

        var saved = coldWatch.Elapsed.TotalSeconds - warmWatch.Elapsed.TotalSeconds;
        var ratio = warmWatch.Elapsed.TotalSeconds <= 0 ? 0 : coldWatch.Elapsed.TotalSeconds / warmWatch.Elapsed.TotalSeconds;
        Say($"[trial] RESIDENT CEILING        : {saved:F1}s saved ({ratio:F1}x) — before any per-file scoping");

        // Property check, reported not asserted: this harness is a measurement, and a hard failure here would
        // hide the timings we came for. A drift is still a real finding — trivia must not change facts.
        var same = Counts(coldResult) == Counts(warmResult);
        Say(
            same
                ? "[trial] fact counts IDENTICAL across the trivia-only edit (expected)."
                : $"[trial] *** FACT COUNT DRIFT on a trivia-only edit — FINDING ***  cold={Counts(coldResult)}  warm={Counts(warmResult)}"
        );
    }

    // The requested file if it is in the solution, else the smallest document in the project with the fewest
    // documents — a leaf-ish, cheap-to-reparse target, so the measurement reflects re-extraction cost rather
    // than the cost of reparsing one enormous file.
    private static Document? PickDocument(Solution solution, string? requested)
    {
        var csharp = solution.Projects.Where(p => p.Language == LanguageNames.CSharp).ToArray();
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var full = Path.GetFullPath(requested);
            var match = csharp
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d =>
                    d.FilePath is not null && string.Equals(Path.GetFullPath(d.FilePath), full, StringComparison.OrdinalIgnoreCase)
                );
            if (match is not null)
            {
                return match;
            }

            Console.WriteLine($"[trial] RIG_TRIAL_EDIT_FILE not found in the solution ({requested}) — auto-picking instead.");
        }

        // Exclude obj/ and bin/: the first run of this harness auto-picked
        // src/components/obj/Debug/netstandard2.0/components.AssemblyInfo.cs — a GENERATED file, because the
        // "fewest documents" heuristic favours tiny projects whose only documents are MSBuild-generated. Fine
        // for a whole-solution timing, useless for per-file measurement.
        static bool IsRealSource(Document d) =>
            d.FilePath is not null
            && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && !d.FilePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !d.FilePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

        return csharp
            .Where(p => p.Documents.Any(IsRealSource))
            .OrderBy(p => p.Documents.Count(IsRealSource))
            .SelectMany(p => p.Documents)
            .FirstOrDefault(IsRealSource);
    }

    private static string Counts(AnalysisResult result) =>
        $"{(result.Symbols ?? []).Count} sym / {(result.References ?? []).Count} ref / "
        + $"{(result.TypeRelations ?? []).Count} rel / {(result.DispatchFacts ?? []).Count} disp";
}
