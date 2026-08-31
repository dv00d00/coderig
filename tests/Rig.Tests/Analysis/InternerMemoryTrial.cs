using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Extraction;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Domain.Data;

namespace Rig.Tests.Analysis;

// OPT-IN MEASUREMENT HARNESS — not a unit test; a silent no-op in the normal suite.
//
// Measures what the extraction-side StringInterner buys a RESIDENT process at real scale: managed live
// set and OS working set at the three states that matter — after cold boot, after one eager edit, and
// after the full cascade reconcile (the state measured at 19.1 GB on MedDBase before this work). The
// interner arm is selected by RIG_NO_INTERN (the StringInterner kill switch), so ONE binary hosts both
// arms — interleave runs (base, interned, base, interned) to cancel machine drift; sequential
// all-A-then-all-B has already faked a regression on this machine once.
//
//   $env:RIG_INTERN_TRIAL_SOLUTION="C:\git\meddbase-main-application-2\MedDBase.slnx"
//   $env:RIG_TRIAL_RULES="C:\git\meddbase-analysis\rig.rules.json"
//   $env:RIG_TRIAL_BUILD_CACHE="<a dtb-cache dir reused across all runs>"
//   $env:RIG_INTERN_TRIAL_EDIT_FILE="<abs path to a .cs file in a HUB project>"  # picks cascade size
//   $env:RIG_NO_INTERN="1"   # base arm; unset/anything else = interned arm
//   dotnet run --project tests/Rig.ManualIntegrationTests -- --maximum-parallel-tests 1 --treenode-filter "/*/*/InternerMemoryTrial/*"
//
// Report goes to a FILE (RIG_INTERN_TRIAL_REPORT, appended as it goes) — TUnit does not surface
// Console.WriteLine, and this program already lost an 8-minute run to that.
public sealed class InternerMemoryTrial
{
    [Test]
    public async Task Measure_resident_memory_with_and_without_interning()
    {
        var solutionPath = Environment.GetEnvironmentVariable("RIG_INTERN_TRIAL_SOLUTION");
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return; // opt-in harness
        }

        var reportPath =
            Environment.GetEnvironmentVariable("RIG_INTERN_TRIAL_REPORT") ?? Path.Combine(Path.GetTempPath(), "rig-interner-trial.log");
        void Say(string line)
        {
            Console.WriteLine(line);
            try
            {
                File.AppendAllText(reportPath, line + Environment.NewLine);
            }
            catch (IOException) { }
        }

        var rulesPath = Environment.GetEnvironmentVariable("RIG_TRIAL_RULES");
        var buildCacheDir = Environment.GetEnvironmentVariable("RIG_TRIAL_BUILD_CACHE");
        var editFile = Environment.GetEnvironmentVariable("RIG_INTERN_TRIAL_EDIT_FILE");
        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        var rules = string.IsNullOrWhiteSpace(rulesPath)
            ? RuleSetLoader.Load(workingDirectory)
            : RuleSetLoader.Load(Path.GetDirectoryName(Path.GetFullPath(rulesPath))!, [Path.GetFullPath(rulesPath)]);

        // The arm is whatever the kill switch says: null = base (no interning), instance = interned.
        // The SAME instance feeds the boot and the resident index, mirroring WatchCommand's wiring.
        var interner = StringInterner.CreateDefault();
        var arm = interner is null ? "BASE (RIG_NO_INTERN=1)" : "INTERNED";
        Say($"[interner-trial] arm={arm} solution={solutionPath}");

        // Managed LIVE set = after a forced, compacting full collection; working set = what the OS
        // holds for the process (ServerGC/DATAS may keep segments the live set released — reporting
        // both is the point, because the two have diverged before in this program).
        (double ManagedGb, double WorkingSetGb) Memory()
        {
            var managed = GC.GetTotalMemory(forceFullCollection: true);
            var workingSet = Process.GetCurrentProcess().WorkingSet64;
            return (managed / (1024.0 * 1024 * 1024), workingSet / (1024.0 * 1024 * 1024));
        }

        var bootWatch = Stopwatch.StartNew();
        var (baseFacts, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(
            solutionPath: solutionPath,
            rules: rules,
            excludeTests: true,
            buildCacheDir: buildCacheDir,
            interner: interner
        );
        bootWatch.Stop();
        using var _ = workspace;

        var (bootManaged, bootWs) = Memory();
        Say(
            $"[interner-trial] BOOT   wall {bootWatch.Elapsed.TotalSeconds:F1}s"
                + $" | managed-live {bootManaged:F2} GB | workingSet {bootWs:F2} GB"
                + $" | {(baseFacts.Symbols ?? []).Count} sym / {(baseFacts.References ?? []).Count} ref"
                + $" | interned distinct {interner?.Count.ToString() ?? "-"}"
        );

        var document = PickDocument(workspace.CurrentSolution, editFile);
        if (document?.FilePath is null)
        {
            Say("[interner-trial] no editable document found — aborting.");
            return;
        }

        Say($"[interner-trial] edit target: {document.FilePath} (project {document.Project.Name})");
        using var index = new ResidentIndex(workspace, baseFacts, solutionPath, rules, interner: interner);

        // --- The edit PLAN: RIG_INTERN_TRIAL_EDITS=N (default 1) sequential edit+reconcile
        // generations, sampling memory after each — the growth CURVE a host-lifetime interner exists
        // to flatten, which a single edit cannot show. Plan shape for N=10: the HUB file (env) at
        // positions 1, 5 and 10 (a full cascade, repeated — does a repeated generation accumulate or
        // plateau?), deterministic per-project samples in between (the realistic small-cascade edits).
        // The sample is a pure function of the solution (ordinal sort, even spacing), so base and
        // interned arms edit the IDENTICAL file sequence.
        var editCount = int.TryParse(Environment.GetEnvironmentVariable("RIG_INTERN_TRIAL_EDITS"), out var n) && n > 0 ? n : 1;
        var plan = BuildEditPlan(workspace.CurrentSolution, document, editCount);

        var totalEdits = 0;
        for (var i = 0; i < plan.Count; i++)
        {
            var target = plan[i];
            totalEdits++;
            var text = await target.GetTextAsync();
            var editedText = SourceText.From(text.ToString() + Environment.NewLine + $"// rig interner trial edit {totalEdits}");

            var editWatch = Stopwatch.StartNew();
            await index.ApplyEditAsync(target.FilePath!, editedText);
            var merged = index.CurrentFacts; // materialize the merged view, as a served query would
            GC.KeepAlive(merged);
            editWatch.Stop();
            var unreconciled = index.UnreconciledProjects.Count;

            var reconcileWatch = Stopwatch.StartNew();
            await index.ReconcileAsync();
            var reconciled = index.CurrentFacts;
            reconcileWatch.Stop();
            var (genManaged, genWs) = Memory();
            Say(
                $"[interner-trial] GEN {totalEdits, 2}  edit {editWatch.Elapsed.TotalSeconds:F2}s"
                    + $" | recon {reconcileWatch.Elapsed.TotalSeconds:F1}s ({unreconciled} proj)"
                    + $" | managed-live {genManaged:F2} GB | workingSet {genWs:F2} GB"
                    + $" | {(reconciled.References ?? []).Count} ref"
                    + $" | interned distinct {interner?.Count.ToString() ?? "-"}"
                    + $" | {Path.GetFileName(target.FilePath)}"
            );

            if (totalEdits == 1)
            {
                Say(
                    $"[interner-trial] SUMMARY arm={arm}"
                        + $" boot={bootWatch.Elapsed.TotalSeconds:F1}s/{bootManaged:F2}/{bootWs:F2}"
                        + $" edit={editWatch.Elapsed.TotalSeconds:F2}s"
                        + $" recon={reconcileWatch.Elapsed.TotalSeconds:F1}s/{genManaged:F2}/{genWs:F2}"
                );
            }
        }

        var (finalManaged, finalWs) = Memory();
        Say(
            $"[interner-trial] FINAL arm={arm} after {totalEdits} generation(s):"
                + $" managed-live {finalManaged:F2} GB | workingSet {finalWs:F2} GB"
                + $" | interned distinct {interner?.Count.ToString() ?? "-"}"
        );
    }

    // Deterministic plan: the hub document at positions 1, 5, 10, 15, … (full cascades, repeated);
    // between them, the first real source file of evenly spaced non-hub projects (ordinal-sorted), so
    // both arms — and every rerun — edit the identical sequence.
    private static List<Document> BuildEditPlan(Solution solution, Document hub, int editCount)
    {
        var plan = new List<Document>(editCount) { hub };
        if (editCount == 1)
        {
            return plan;
        }

        static bool IsRealSource(Document d) =>
            d.FilePath is not null
            && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && !d.FilePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !d.FilePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

        var candidates = solution
            .Projects.Where(p => p.Language == LanguageNames.CSharp && p.Id != hub.Project.Id)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => p.Documents.Where(IsRealSource).OrderBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase).FirstOrDefault())
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();
        if (candidates.Count == 0)
        {
            return plan;
        }

        var step = Math.Max(1, candidates.Count / editCount);
        var next = 0;
        for (var i = 2; i <= editCount; i++)
        {
            if (i % 5 == 0)
            {
                plan.Add(hub); // repeated full cascade
                continue;
            }

            plan.Add(candidates[next % candidates.Count]);
            next += step;
        }

        return plan;
    }

    // Same exclusions as ResidentWorkspaceTrial's picker (obj/bin are generated), but the FALLBACK is
    // deliberately different: this trial wants a representative CASCADE, so pass
    // RIG_INTERN_TRIAL_EDIT_FILE pointing into a hub project; the fallback (first real source anywhere)
    // is only there so the harness still runs unconfigured.
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
        }

        static bool IsRealSource(Document d) =>
            d.FilePath is not null
            && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && !d.FilePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !d.FilePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

        return csharp.SelectMany(p => p.Documents).FirstOrDefault(IsRealSource);
    }
}
