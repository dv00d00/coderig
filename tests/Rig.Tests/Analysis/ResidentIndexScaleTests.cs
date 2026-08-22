using System.Text;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Domain.Data;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Analysis;

// The SCALE gate for ResidentIndex.MergeFacts, forced by measured MedDBase regressions (2026-08-20):
// relation/dispatch facts must be replaced by their exact EMITTER file. Symbol-driven replacement
// lost cross-file edges; unioning avoided that false negative but retained deleted edges as ghosts.
// DeepChain hosts both sides of the gate (all with an endpoint in Business/ChannelBase.cs):
//   - INotifier implemented by classes in two DIFFERENT projects (Business.SmsChannel direct,
//     ApiGateway.EmailChannel inherited) — cross-project impl edges;
//   - an override chain (ChannelBase.Notify virtual -> Web.PushChannel.Notify override, edge emitted
//     at PushChannel's file);
//   - an INHERITED interface implementation (EmailChannel : INotifier satisfied by ChannelBase.Notify
//     — the impl edge is emitted at EmailChannel's declaration while BOTH endpoints live in other
//     projects' files, the shape flagged as unattributable without an emitter FilePath);
//   - a delegate/method-group bind site (NotificationRelay._send = channel.Notify, delegate_bind edge
//     emitted at the bind site in ApiGateway).
//
// The edit adds a call and removes SmsChannel's INotifier declaration in the SAME file. The latter
// deletes one relation and one dispatch emission. At the EAGER window CurrentFacts must retain the
// other files' cross-file edges, remove the edited file's deleted edges, and be multiset-equal to a cold
// oracle. The post-reconcile assertion pins the same contract after the full dependent cascade.
[NotInParallel]
public sealed class ResidentIndexScaleTests
{
    private const string NotifierNotify = "M:Domain.INotifier.Notify(System.String)";
    private const string ChannelBaseNotify = "M:Business.ChannelBase.Notify(System.String)";
    private const string SmsChannelNotify = "M:Business.SmsChannel.Notify(System.String)";
    private const string PushChannelNotify = "M:Web.PushChannel.Notify(System.String)";
    private const string RelaySendField = "F:ApiGateway.NotificationRelay._send";
    private const string DuplicateEmitterType = "T:ApiGateway.DuplicateEmitter";
    private const string DuplicateNotifierType = "T:ApiGateway.IDuplicateNotifier";
    private const string DuplicateNotifierSend = "M:ApiGateway.IDuplicateNotifier.Send(System.String)";
    private const string DuplicateBaseSend = "M:ApiGateway.DuplicateBase.Send(System.String)";

    [Test]
    public async Task Duplicate_relation_and_dispatch_emitters_are_preserved_then_retired_independently()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);
        var emailPath = Path.Combine(playground.WorkingDirectory, "ApiGateway", "EmailChannel.cs");
        var bookingPath = Path.Combine(playground.WorkingDirectory, "ApiGateway", "BookingController.cs");
        var emailText = await File.ReadAllTextAsync(emailPath);
        var bookingText = await File.ReadAllTextAsync(bookingPath);

        // Two partial declarations in different files resolve to the SAME aggregate type symbol. The
        // first supplies DuplicateBase+IDuplicateNotifier; extraction at BOTH declaration sites emits the same
        // interface relation and inherited-implementation dispatch edge, each with its own owner path.
        const string EmitterA = """
            public interface IDuplicateNotifier { string Send(string message); }
            public abstract class DuplicateBase { public virtual string Send(string message) => message; }
            public partial class DuplicateEmitter : DuplicateBase, IDuplicateNotifier { }
            """;
        const string EmitterB = "public partial class DuplicateEmitter { }";
        var emailWithEmitter = emailText + Environment.NewLine + EmitterA + Environment.NewLine;
        var bookingWithEmitter = bookingText + Environment.NewLine + EmitterB + Environment.NewLine;
        await File.WriteAllTextAsync(emailPath, emailWithEmitter);
        await File.WriteAllTextAsync(bookingPath, bookingWithEmitter);

        var (baseFacts, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(playground.SolutionPath, rules);
        using var index = new ResidentIndex(workspace, baseFacts, playground.SolutionPath, rules);

        AssertDuplicateEmitters(baseFacts, emailPath, bookingPath, "COLD BASE");

        // First force MergeFacts through a body-only edit in emitter B. Neither provenance row changes;
        // a merge-time semantic Distinct nevertheless collapsed 2 -> 1 here (the schema-v4 trial bug).
        const string BookMarker = "public string Book(Contracts.PatientDto dto) => _bookings.Book(dto.Id);";
        bookingWithEmitter.ShouldContain(BookMarker);
        var bookingBodyEdit = bookingWithEmitter.Replace(
            BookMarker,
            "public string Book(Contracts.PatientDto dto) { return _bookings.Book(dto.Id); }",
            StringComparison.Ordinal
        );
        await index.ApplyEditAsync(bookingPath, SourceText.From(bookingBodyEdit, Encoding.UTF8));
        AssertDuplicateEmitters(index.CurrentFacts, emailPath, bookingPath, "BODY-ONLY EAGER");

        emailWithEmitter.ShouldContain(EmitterA);
        var emailWithoutEmitter = emailWithEmitter.Replace(EmitterA, "", StringComparison.Ordinal);
        await index.ApplyEditAsync(emailPath, SourceText.From(emailWithoutEmitter, Encoding.UTF8));

        var remainingDispatch = DuplicateDispatchEmissions(index.CurrentFacts);
        var remainingRelations = DuplicateRelationEmissions(index.CurrentFacts);
        remainingDispatch.Count.ShouldBe(1, "removing emitter A must retain emitter B's dispatch emission");
        remainingRelations.Count.ShouldBe(1, "removing emitter A must retain emitter B's relation emission");
        remainingDispatch[0].FilePath.ShouldBe(bookingPath);
        remainingRelations[0].FilePath.ShouldBe(bookingPath);

        bookingBodyEdit.ShouldContain(EmitterB);
        var bookingWithoutEmitter = bookingBodyEdit.Replace(EmitterB, "", StringComparison.Ordinal);
        await index.ApplyEditAsync(bookingPath, SourceText.From(bookingWithoutEmitter, Encoding.UTF8));

        DuplicateDispatchEmissions(index.CurrentFacts).ShouldBeEmpty("removing the final emitter must retire its dispatch emission");
        DuplicateRelationEmissions(index.CurrentFacts).ShouldBeEmpty("removing the final emitter must retire its relation emission");
        await index.ReconcileAsync();
        DuplicateDispatchEmissions(index.CurrentFacts).ShouldBeEmpty("reconciliation must not resurrect dispatch provenance");
        DuplicateRelationEmissions(index.CurrentFacts).ShouldBeEmpty("reconciliation must not resurrect relation provenance");
    }

    [Test]
    public async Task Emitter_owned_dispatch_and_relation_facts_are_replaced_on_edit_and_reconcile()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var rules = RuleSetLoader.Load(playground.WorkingDirectory);

        // ---- 1. Cold analyze retaining the workspace -> build the ResidentIndex ----
        var (f0, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(playground.SolutionPath, rules);
        using var index = new ResidentIndex(workspace, f0, playground.SolutionPath, rules);

        // Anti-vacuity: every vulnerable shape must exist in the cold base, or the gate is testing
        // nothing. Each of these four dispatch edges has an endpoint declared in ChannelBase.cs (the
        // file the edit below dirties) but is EMITTED in a different file.
        var baseDispatch = f0.DispatchFacts ?? [];
        baseDispatch.ShouldContain(
            d => d.SourceMember == NotifierNotify && d.TargetMember == ChannelBaseNotify && d.Kind == DispatchKinds.Impl,
            "inherited impl (emitted at ApiGateway/EmailChannel.cs, both endpoints elsewhere)"
        );
        baseDispatch.ShouldContain(
            d => d.SourceMember == ChannelBaseNotify && d.TargetMember == PushChannelNotify && d.Kind == DispatchKinds.Override,
            "override chain (emitted at Web/PushChannel.cs)"
        );
        baseDispatch.ShouldContain(
            d => d.SourceMember == RelaySendField && d.TargetMember == ChannelBaseNotify && d.Kind == DispatchKinds.DelegateBind,
            "method-group delegate bind (emitted at ApiGateway/EmailChannel.cs)"
        );
        // The two-projects-implement-one-interface pair (SmsChannel is same-file, so it survives even
        // the symbol-dropping merge — it pins the cross-project impl STRUCTURE, not the drop).
        baseDispatch.ShouldContain(d =>
            d.SourceMember == NotifierNotify && d.TargetMember == SmsChannelNotify && d.Kind == DispatchKinds.Impl
        );

        var baseRelations = f0.TypeRelations ?? [];
        baseRelations.ShouldContain(t =>
            t.TypeSymbolId == "T:Business.SmsChannel" && t.RelationKind == "interface" && t.RelatedSymbolId == "T:Domain.INotifier"
        );
        baseRelations.ShouldContain(t =>
            t.TypeSymbolId == "T:ApiGateway.EmailChannel" && t.RelationKind == "base" && t.RelatedSymbolId == "T:Business.ChannelBase"
        );
        baseRelations.ShouldContain(t =>
            t.TypeSymbolId == "T:ApiGateway.EmailChannel" && t.RelationKind == "interface" && t.RelatedSymbolId == "T:Domain.INotifier"
        );
        baseRelations.ShouldContain(t =>
            t.TypeSymbolId == "T:Web.PushChannel" && t.RelationKind == "base" && t.RelatedSymbolId == "T:Business.ChannelBase"
        );

        // ---- 2. The edit: ChannelBase.Notify's BODY gains a Foundation.Db.Query call, while the
        // same file's SmsChannel stops declaring INotifier. That removes both the interface relation
        // and the exact impl dispatch edge emitted by this file.
        var editedFilePath = Path.Combine(playground.WorkingDirectory, "Business", "ChannelBase.cs");
        var originalText = await File.ReadAllTextAsync(editedFilePath);
        const string Marker = "public virtual string Notify(string message) => $\"channel: {message}\";";
        originalText.ShouldContain(Marker);
        var newline = originalText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var replacement = string.Join(
            newline,
            "public virtual string Notify(string message)",
            "    {",
            "        Foundation.Db.Query(\"audit: notify\");",
            "        return $\"channel: {message}\";",
            "    }"
        );
        var editedText = originalText.Replace(Marker, replacement, StringComparison.Ordinal);
        const string SmsInterfaceMarker = "public sealed class SmsChannel : Domain.INotifier";
        editedText.ShouldContain(SmsInterfaceMarker);
        editedText = editedText.Replace(SmsInterfaceMarker, "public sealed class SmsChannel", StringComparison.Ordinal);
        editedText.ShouldNotBe(originalText);

        await index.ApplyEditAsync(editedFilePath, SourceText.From(editedText, Encoding.UTF8));

        // ---- 3. Oracle: the same edit written to disk, cold full analyze from scratch ----
        await File.WriteAllTextAsync(editedFilePath, editedText);
        var oracle = await SolutionAnalyzer.AnalyzeAsync(playground.SolutionPath, rules);

        // The edit must actually change facts in the oracle (guards against a vacuous comparison).
        (oracle.References ?? []).ShouldContain(r =>
            r.RefKind == "invocation"
            && r.TargetSymbolId == "M:Foundation.Db.Query(System.String)"
            && r.EnclosingSymbolId == ChannelBaseNotify
        );
        AssertDeletedEmitterEdgesAbsent(oracle, "COLD ORACLE");

        // ---- 4. EAGER window: the decisive assertion. CurrentFacts is served during the disclosure
        // window, and only the edited file's own facts may differ from the cold truth — the four
        // cross-file dispatch edges must all still be present. Crisp per-edge checks first (diagnosis),
        // then the full five-kind fact-multiset equality.
        var eager = index.CurrentFacts;
        index.UnreconciledProjects.ShouldNotBeEmpty(); // it IS the eager window, not a reconciled state
        var eagerDispatch = eager.DispatchFacts ?? [];
        eagerDispatch.ShouldContain(
            d => d.SourceMember == NotifierNotify && d.TargetMember == ChannelBaseNotify && d.Kind == DispatchKinds.Impl,
            "LOST inherited-impl edge: emitted at EmailChannel.cs, endpoint in the edited file"
        );
        AssertDeletedEmitterEdgesAbsent(eager, "EAGER");
        eagerDispatch.ShouldContain(
            d => d.SourceMember == ChannelBaseNotify && d.TargetMember == PushChannelNotify && d.Kind == DispatchKinds.Override,
            "LOST override edge: emitted at PushChannel.cs, endpoint in the edited file"
        );
        eagerDispatch.ShouldContain(
            d => d.SourceMember == RelaySendField && d.TargetMember == ChannelBaseNotify && d.Kind == DispatchKinds.DelegateBind,
            "LOST delegate-bind edge: emitted at EmailChannel.cs, target in the edited file"
        );

        AssertFactMultisetsEqual(eager, oracle, playground.RootDirectory, arm: "EAGER (edited file only, cascade still owed)");

        // ---- 5. RECONCILED: cold-index facts == CurrentFacts after edit+reconcile, all five kinds ----
        await index.ReconcileAsync();
        index.UnreconciledProjects.ShouldBeEmpty();
        var reconciled = index.CurrentFacts;
        AssertDeletedEmitterEdgesAbsent(reconciled, "RECONCILED");
        AssertFactMultisetsEqual(reconciled, oracle, playground.RootDirectory, arm: "RECONCILED");
    }

    private static void AssertDeletedEmitterEdgesAbsent(AnalysisResult result, string arm)
    {
        (result.DispatchFacts ?? []).ShouldNotContain(
            d => d.SourceMember == NotifierNotify && d.TargetMember == SmsChannelNotify && d.Kind == DispatchKinds.Impl,
            $"[{arm}] deleted SmsChannel interface implementation survived as a ghost dispatch edge"
        );
        (result.TypeRelations ?? []).ShouldNotContain(
            t => t.TypeSymbolId == "T:Business.SmsChannel" && t.RelationKind == "interface" && t.RelatedSymbolId == "T:Domain.INotifier",
            $"[{arm}] deleted SmsChannel interface declaration survived as a ghost relation edge"
        );
    }

    private static void AssertDuplicateEmitters(AnalysisResult result, string emitterA, string emitterB, string arm)
    {
        var expectedPaths = new[] { emitterA, emitterB }.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        DuplicateDispatchEmissions(result)
            .Select(d => d.FilePath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            .ShouldBe(expectedPaths, $"[{arm}] both dispatch provenance rows must survive");
        DuplicateRelationEmissions(result)
            .Select(r => r.FilePath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            .ShouldBe(expectedPaths, $"[{arm}] both relation provenance rows must survive");
    }

    private static List<DispatchFact> DuplicateDispatchEmissions(AnalysisResult result) =>
        (result.DispatchFacts ?? [])
            .Where(d => d.SourceMember == DuplicateNotifierSend && d.TargetMember == DuplicateBaseSend && d.Kind == DispatchKinds.Impl)
            .ToList();

    private static List<TypeRelationFact> DuplicateRelationEmissions(AnalysisResult result) =>
        (result.TypeRelations ?? [])
            .Where(r =>
                r.TypeSymbolId == DuplicateEmitterType
                && r.RelatedSymbolId == DuplicateNotifierType
                && r.RelationKind == RelationKinds.Interface
            )
            .ToList();

    private static void AssertFactMultisetsEqual(AnalysisResult actual, AnalysisResult oracle, string root, string arm)
    {
        var actualFacts = CanonicalFacts(actual, root);
        var oracleFacts = CanonicalFacts(oracle, root);

        var onlyActual = MultisetDifference(actualFacts, oracleFacts);
        var onlyOracle = MultisetDifference(oracleFacts, actualFacts);

        var report = new StringBuilder();
        report.AppendLine($"[{arm}] overlay: {Counts(actual)} | cold oracle: {Counts(oracle)}");
        if (onlyActual.Length == 0 && onlyOracle.Length == 0)
        {
            report.AppendLine("VERDICT: IDENTICAL — overlay CurrentFacts multiset-equal cold facts.");
        }
        else
        {
            report.AppendLine($"VERDICT: NOT IDENTICAL — {onlyActual.Length} fact(s) only in OVERLAY, {onlyOracle.Length} only in COLD.");
            AppendGroup(report, "ONLY IN OVERLAY", onlyActual);
            AppendGroup(report, "ONLY IN COLD (i.e. LOST by the merge)", onlyOracle);
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

    // Multiplicity and normalized emitter paths are part of this overlay oracle: two files emitting
    // one semantic edge are two fact rows, and losing either owner must fail the comparison.
    private static List<string> CanonicalFacts(AnalysisResult result, string root)
    {
        var lines = new List<string>();

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
            lines.Add($"rel  | {t.TypeSymbolId} | {t.RelationKind} | {t.RelatedSymbolId} | {Normalize(t.FilePath, root)}");
        }

        foreach (var d in result.DispatchFacts ?? [])
        {
            lines.Add($"disp | {d.SourceMember} | {d.Kind} | {d.TargetMember} | {Normalize(d.FilePath, root)}");
        }

        foreach (var a in result.AllocationFacts ?? [])
        {
            lines.Add($"alloc| {a.Operation} | {a.ResourceType} | encl={a.EnclosingSymbolId} | {Normalize(a.FilePath, root)}:{a.Line}");
        }

        return lines;
    }

    private static string[] MultisetDifference(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var rightCounts = right
            .GroupBy(row => row, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var difference = new List<string>();
        foreach (var row in left.OrderBy(row => row, StringComparer.Ordinal))
        {
            if (rightCounts.TryGetValue(row, out var count) && count > 0)
            {
                rightCounts[row] = count - 1;
            }
            else
            {
                difference.Add(row);
            }
        }

        return difference.ToArray();
    }

    private static string Normalize(string path, string root)
    {
        var normalized = path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path[root.Length..] : path;
        return normalized.Replace('\\', '/').TrimStart('/');
    }
}
