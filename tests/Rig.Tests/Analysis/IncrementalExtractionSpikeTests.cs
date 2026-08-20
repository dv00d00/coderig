using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Rules;
using Rig.Domain.Data;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Analysis;

// SPIKE: gate for a resident/incremental indexing architecture. Question under test — does
// re-extracting over an INCREMENTALLY-UPDATED Roslyn solution (the retained RigWorkspace from the
// cold load, plus one Solution.WithDocumentText edit) produce facts IDENTICAL to a cold full-solution
// index of the same tree state? A negative result is a valid outcome: the test's job on divergence is
// to print the exact symmetric difference, not to pass.
public sealed class IncrementalExtractionSpikeTests
{
    [Test]
    public async Task Incremental_reextraction_over_retained_workspace_matches_cold_full_index()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);

        // ---- Cold analyze (F0), retaining the workspace ----
        var (f0, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(playground.SolutionPath, rules);
        using var _ = workspace;

        // Baseline sanity: the three key cross-project bindings must resolve in the cold load, or the
        // playground/loader is broken and any incremental-vs-cold comparison would be meaningless.
        var f0References = f0.References ?? [];
        f0References.ShouldContain(r =>
            r.RefKind == "invocation"
            && r.TargetSymbolId == "M:ApiGateway.BookingController.Book(Contracts.PatientDto)"
            && r.EnclosingSymbolId == "M:Web.HomePage.Show"
        );
        f0References.ShouldContain(r =>
            r.RefKind == "invocation"
            && r.TargetSymbolId == "M:Contracts.IPatientRepository.GetById(System.Int32)"
            && r.EnclosingSymbolId == "M:Business.BookingService.Book(System.Int32)"
        );
        f0References.ShouldContain(r =>
            r.RefKind == "invocation"
            && r.TargetSymbolId == "M:Foundation.Db.Query(System.String)"
            && r.EnclosingSymbolId == "M:DataAccess.PatientRepository.GetById(System.Int32)"
        );

        // ---- The edit: Business.BookingService.Book gains a DIRECT call to Foundation.Db.Query ----
        // Crosses a project boundary Business previously used only transitively (Foundation is not a
        // direct ProjectReference of Business). Expected fact delta: one new `invocation` reference
        // TargetSymbolId=M:Foundation.Db.Query(System.String) enclosed by
        // M:Business.BookingService.Book(System.Int32) in Business/BookingService.cs (plus the +1 line
        // shift of the facts below the insertion in that one file).
        var editedFilePath = Path.Combine(playground.WorkingDirectory, "Business", "BookingService.cs");
        var originalText = await File.ReadAllTextAsync(editedFilePath);
        const string Marker = "var patient = _repository.GetById(patientId);";
        originalText.ShouldContain(Marker);
        var newline = originalText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var editedText = originalText.Replace(
            Marker,
            "Foundation.Db.Query(\"audit: booking attempt\");" + newline + "        " + Marker,
            StringComparison.Ordinal
        );
        editedText.ShouldNotBe(originalText);

        // ---- INCREMENTAL arm: same edit applied in-memory over the RETAINED workspace ----
        var solution = workspace.CurrentSolution;
        var businessProject = solution.Projects.Single(p => p.Name == "Business");
        var document = businessProject.Documents.Single(d =>
            string.Equals(Path.GetFileName(d.FilePath), "BookingService.cs", StringComparison.OrdinalIgnoreCase)
        );
        var updatedSolution = solution.WithDocumentText(document.Id, SourceText.From(editedText, Encoding.UTF8));
        var f1Incremental = await SolutionAnalyzer.ExtractFromSolutionAsync(updatedSolution, playground.SolutionPath, rules);

        // ---- ORACLE arm: same edit written to disk, cold full-solution analyze from scratch ----
        await File.WriteAllTextAsync(editedFilePath, editedText);
        var f1Cold = await SolutionAnalyzer.AnalyzeAsync(playground.SolutionPath, rules);

        // The edit must actually change facts in the oracle: guards against a no-op edit making the
        // whole comparison vacuous.
        (f1Cold.References ?? []).ShouldContain(r =>
            r.RefKind == "invocation"
            && r.TargetSymbolId == "M:Foundation.Db.Query(System.String)"
            && r.EnclosingSymbolId == "M:Business.BookingService.Book(System.Int32)"
        );

        // ---- Compare: canonical fact tuples, temp paths normalized ----
        var root = playground.RootDirectory;
        var incrementalFacts = CanonicalFacts(f1Incremental, root);
        var coldFacts = CanonicalFacts(f1Cold, root);

        var onlyIncremental = incrementalFacts.Except(coldFacts).OrderBy(l => l, StringComparer.Ordinal).ToArray();
        var onlyCold = coldFacts.Except(incrementalFacts).OrderBy(l => l, StringComparer.Ordinal).ToArray();

        // Locality view (the one-project re-extraction claim): which FILES have any fact difference
        // between F0 and the incremental F1? For the architecture to work per-project, only the edited
        // file may differ. Printed before asserting so the evidence survives a main-assert failure.
        var changedFiles = ChangedFiles(CanonicalFacts(f0, root), incrementalFacts);

        var report = new StringBuilder();
        report.AppendLine($"F0: {Counts(f0)} | F1_incremental: {Counts(f1Incremental)} | F1_cold: {Counts(f1Cold)}");
        report.AppendLine($"files changed F0 -> F1_incremental: {(changedFiles.Length == 0 ? "(none)" : string.Join(", ", changedFiles))}");
        if (onlyIncremental.Length == 0 && onlyCold.Length == 0)
        {
            report.AppendLine("VERDICT: IDENTICAL — incremental facts set-equal cold facts.");
        }
        else
        {
            report.AppendLine(
                $"VERDICT: NOT IDENTICAL — {onlyIncremental.Length} fact(s) only in INCREMENTAL, {onlyCold.Length} only in COLD."
            );
            AppendGroup(report, "ONLY IN INCREMENTAL", onlyIncremental);
            AppendGroup(report, "ONLY IN COLD", onlyCold);
        }

        Console.WriteLine(report.ToString());

        (onlyIncremental.Length + onlyCold.Length).ShouldBe(0, report.ToString());

        // One-project locality: the in-memory edit must have perturbed facts in the edited file ONLY —
        // otherwise per-project incremental re-extraction cannot splice into an existing store.
        changedFiles.ShouldBe(new[] { "DeepChain/Business/BookingService.cs" }, ignoreOrder: true);
    }

    private static string Counts(AnalysisResult result) =>
        $"{(result.Symbols ?? []).Count} sym / {(result.References ?? []).Count} ref / "
        + $"{(result.TypeRelations ?? []).Count} rel / {(result.DispatchFacts ?? []).Count} disp / "
        + $"{(result.AllocationFacts ?? []).Count} alloc";

    private static void AppendGroup(StringBuilder report, string header, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        report.AppendLine($"--- {header} ({lines.Count}) ---");
        foreach (var line in lines)
        {
            report.AppendLine("  " + line);
        }
    }

    // One canonical string per fact so set comparison and human-readable diff use the same shape.
    // References use the spike-brief tuple (TargetSymbolId, RefKind, EnclosingSymbolId, FilePath, Line);
    // the other fact kinds get their natural identity tuples. Absolute temp paths are normalized.
    private static HashSet<string> CanonicalFacts(AnalysisResult result, string root)
    {
        var lines = new HashSet<string>(StringComparer.Ordinal);

        foreach (var s in result.Symbols ?? [])
        {
            lines.Add($"sym  | {s.SymbolId} | {s.Kind} | {Normalize(s.FilePath, root)}:{s.Line}-{s.EndLine} | bodyHash={s.BodyHash}");
        }

        foreach (var r in result.References ?? [])
        {
            lines.Add(
                // TargetAssembly + TargetInSource are in the tuple DELIBERATELY: they are the two fields a
                // duplicate-assembly-identity regression moves while every DocID stays byte-identical (the
                // failure class the `--no-closure` experiment demonstrated — a type visible as BOTH a live
                // compilation and a metadata DLL stops binding and the edge is silently dropped). Comparing
                // DocIDs alone is blind to exactly the hazard this architecture is most exposed to.
                $"ref  | {r.TargetSymbolId} | {r.RefKind} | encl={r.EnclosingSymbolId ?? ""} | "
                    + $"asm={r.TargetAssembly} | inSource={r.TargetInSource} | {Normalize(r.FilePath, root)}:{r.Line}"
            );
        }

        foreach (var t in result.TypeRelations ?? [])
        {
            lines.Add($"rel  | {t.TypeSymbolId} | {t.RelationKind} | {t.RelatedSymbolId}");
        }

        foreach (var d in result.DispatchFacts ?? [])
        {
            lines.Add($"disp | {d.SourceMember} | {d.Kind} | {d.TargetMember}");
        }

        foreach (var a in result.AllocationFacts ?? [])
        {
            lines.Add($"alloc| {a.Operation} | {a.ResourceType} | encl={a.EnclosingSymbolId} | {Normalize(a.FilePath, root)}:{a.Line}");
        }

        return lines;
    }

    // Files whose canonical fact set differs between two runs (path-carrying fact kinds only —
    // type-relation and dispatch facts carry no FilePath, so they are compared globally above).
    private static string[] ChangedFiles(IReadOnlySet<string> before, IReadOnlySet<string> after)
    {
        static Dictionary<string, HashSet<string>> ByFile(IEnumerable<string> lines)
        {
            var byFile = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                if (line.StartsWith("rel", StringComparison.Ordinal) || line.StartsWith("disp", StringComparison.Ordinal))
                {
                    continue; // no FilePath on these fact kinds
                }

                var segments = line.Split('|').Select(s => s.Trim()).ToArray();
                var pathAndLine = segments.FirstOrDefault(s => s.Contains(".cs:", StringComparison.OrdinalIgnoreCase));
                if (pathAndLine is null)
                {
                    continue;
                }

                var file = pathAndLine[..pathAndLine.LastIndexOf(".cs:", StringComparison.OrdinalIgnoreCase)] + ".cs";
                if (!byFile.TryGetValue(file, out var set))
                {
                    byFile[file] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                set.Add(line);
            }

            return byFile;
        }

        var beforeByFile = ByFile(before);
        var afterByFile = ByFile(after);
        return beforeByFile
            .Keys.Union(afterByFile.Keys, StringComparer.Ordinal)
            .Where(file => !beforeByFile.TryGetValue(file, out var b) || !afterByFile.TryGetValue(file, out var a) || !b.SetEquals(a))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Normalize(string path, string root)
    {
        var normalized = path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path[root.Length..] : path;
        return normalized.Replace('\\', '/').TrimStart('/');
    }
}
