using System.Text;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Domain.Data;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Analysis;

// Slice 3 of live-background-index: the equivalence gate, extended from the raw incremental spike
// (IncrementalExtractionSpikeTests) to the CONVERGING OVERLAY. A cold-analyzed base + ResidentIndex
// edits (eager per-file re-extract, then the ProjectCascadePolicy reconcile) must produce CurrentFacts
// set-equal to a cold full index of the same tree — including TargetAssembly/TargetInSource on
// references, the two fields a duplicate-assembly-identity regression moves while every DocID stays
// identical. The ghost-fact test pins the replace-not-append property: a call REMOVED by an edit must
// leave no stale reference row behind.
public sealed class ResidentIndexTests
{
    private const string GetByIdTarget = "M:Contracts.IPatientRepository.GetById(System.Int32)";
    private const string BookEnclosing = "M:Business.BookingService.Book(System.Int32)";

    [Test]
    public async Task Overlay_after_reconcile_matches_cold_full_index_and_discloses_the_cascade_window()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);

        // ---- 1. Cold analyze retaining the workspace -> build the ResidentIndex ----
        var (f0, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(playground.SolutionPath, rules);
        using var index = new ResidentIndex(workspace, f0, playground.SolutionPath, rules);

        // Baseline sanity: the key cross-project binding resolves in the cold load, or every later
        // comparison is meaningless.
        (f0.References ?? []).ShouldContain(r =>
            r.RefKind == "invocation" && r.TargetSymbolId == GetByIdTarget && r.EnclosingSymbolId == BookEnclosing
        );
        index.CurrentFacts.ShouldBeSameAs(f0); // no overlay yet -> the base passes through untouched
        index.UnreconciledProjects.ShouldBeEmpty();

        // ---- 2. The spike's edit: Book gains a direct Foundation.Db.Query call ----
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

        await index.ApplyEditAsync(editedFilePath, SourceText.From(editedText, Encoding.UTF8));

        // ---- 5a. The disclosure window is real: the eager arm covered only the edited file, so the
        // cascade (Business's transitive dependents, over the MSBuild ProjectReference graph) is owed.
        var unreconciled = index.UnreconciledProjects;
        unreconciled.ShouldNotBeEmpty();
        unreconciled.ShouldContain("ApiGateway");
        unreconciled.ShouldContain("Web");

        // The eager arm already serves the edit (converging: answer now, reconcile in the background).
        (index.CurrentFacts.References ?? []).ShouldContain(r =>
            r.RefKind == "invocation" && r.TargetSymbolId == "M:Foundation.Db.Query(System.String)" && r.EnclosingSymbolId == BookEnclosing
        );

        // ---- 5b. Reconcile clears the disclosure ----
        await index.ReconcileAsync();
        index.UnreconciledProjects.ShouldBeEmpty();

        // ---- 3. Oracle: the same edit written to disk, cold full analyze from scratch ----
        await File.WriteAllTextAsync(editedFilePath, editedText);
        var oracle = await SolutionAnalyzer.AnalyzeAsync(playground.SolutionPath, rules);
        (oracle.References ?? []).ShouldContain(r =>
            r.RefKind == "invocation" && r.TargetSymbolId == "M:Foundation.Db.Query(System.String)" && r.EnclosingSymbolId == BookEnclosing
        );

        // ---- 4. CurrentFacts set-equals the oracle on canonical tuples ----
        AssertFactSetsEqual(index.CurrentFacts, oracle, playground.RootDirectory);
    }

    // The replace-not-append property, the highest-risk bug in the overlay: an edit REMOVES a call;
    // after reconcile the removed call's reference fact must be GONE from CurrentFacts (a stale row
    // for a re-extracted file is a ghost fact). Sealed with the full oracle comparison, so any OTHER
    // ghost or lost fact fails too.
    [Test]
    public async Task Removed_call_leaves_no_ghost_reference_after_reconcile()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);

        var (f0, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(playground.SolutionPath, rules);
        using var index = new ResidentIndex(workspace, f0, playground.SolutionPath, rules);

        // Anti-vacuity: the reference we will remove exists in the base.
        (f0.References ?? []).ShouldContain(r =>
            r.RefKind == "invocation" && r.TargetSymbolId == GetByIdTarget && r.EnclosingSymbolId == BookEnclosing
        );

        // The edit: Book loses its _repository.GetById call (and the use of its result).
        var editedFilePath = Path.Combine(playground.WorkingDirectory, "Business", "BookingService.cs");
        var originalText = await File.ReadAllTextAsync(editedFilePath);
        const string CallLine = "var patient = _repository.GetById(patientId);";
        const string ReturnLine = "return patient is null ? \"no patient\" : $\"booked {patient.Name}\";";
        originalText.ShouldContain(CallLine);
        originalText.ShouldContain(ReturnLine);
        var editedText = originalText
            .Replace(CallLine, string.Empty, StringComparison.Ordinal)
            .Replace(ReturnLine, "return \"no patient\";", StringComparison.Ordinal);
        editedText.ShouldNotBe(originalText);

        await index.ApplyEditAsync(editedFilePath, SourceText.From(editedText, Encoding.UTF8));
        await index.ReconcileAsync();
        index.UnreconciledProjects.ShouldBeEmpty();

        // THE ghost check: the removed call's reference fact is gone — not merely outnumbered.
        (index.CurrentFacts.References ?? []).ShouldNotContain(r =>
            r.TargetSymbolId == GetByIdTarget && r.EnclosingSymbolId == BookEnclosing
        );

        // And the full equivalence gate on the same tree state, so ANY stale/lost fact fails the test.
        await File.WriteAllTextAsync(editedFilePath, editedText);
        var oracle = await SolutionAnalyzer.AnalyzeAsync(playground.SolutionPath, rules);
        (oracle.References ?? []).ShouldNotContain(r => r.TargetSymbolId == GetByIdTarget && r.EnclosingSymbolId == BookEnclosing);

        AssertFactSetsEqual(index.CurrentFacts, oracle, playground.RootDirectory);
    }

    private static void AssertFactSetsEqual(AnalysisResult actual, AnalysisResult oracle, string root)
    {
        var actualFacts = CanonicalFacts(actual, root);
        var oracleFacts = CanonicalFacts(oracle, root);

        var onlyActual = actualFacts.Except(oracleFacts).OrderBy(l => l, StringComparer.Ordinal).ToArray();
        var onlyOracle = oracleFacts.Except(actualFacts).OrderBy(l => l, StringComparer.Ordinal).ToArray();

        var report = new StringBuilder();
        report.AppendLine($"overlay: {Counts(actual)} | cold oracle: {Counts(oracle)}");
        if (onlyActual.Length == 0 && onlyOracle.Length == 0)
        {
            report.AppendLine("VERDICT: IDENTICAL — overlay CurrentFacts set-equal cold facts.");
        }
        else
        {
            report.AppendLine($"VERDICT: NOT IDENTICAL — {onlyActual.Length} fact(s) only in OVERLAY, {onlyOracle.Length} only in COLD.");
            AppendGroup(report, "ONLY IN OVERLAY", onlyActual);
            AppendGroup(report, "ONLY IN COLD", onlyOracle);
        }

        Console.WriteLine(report.ToString());
        (onlyActual.Length + onlyOracle.Length).ShouldBe(0, report.ToString());
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

    // Same canonical tuples as IncrementalExtractionSpikeTests, INCLUDING TargetAssembly and
    // TargetInSource on references — the two fields a duplicate-assembly-identity regression moves
    // while every DocID stays byte-identical. Relation/dispatch comparison is set-based, which is also
    // what makes it a real test of the merged (recomputed-whole, deduplicated) tables.
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

    private static string Normalize(string path, string root)
    {
        var normalized = path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path[root.Length..] : path;
        return normalized.Replace('\\', '/').TrimStart('/');
    }
}
