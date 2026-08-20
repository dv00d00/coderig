using System.Text;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Domain.Data;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Analysis;

// The SCALE gate for ResidentIndex.MergeFacts, forced by a measured MedDBase regression (2026-08-20):
// a single-file edit lost 9.6% of type relations and 24.5% of dispatch edges, because the merge
// dropped base TypeRelationFact/DispatchFact rows BY SYMBOL (either endpoint declared in an overlaid
// file) and expected the overlay to re-emit them — but these fact kinds carry no FilePath, and their
// EMITTING site is routinely a different file from either endpoint, so a single file's re-extraction
// cannot reproduce them. The original DeepChain playground had only 2 relations / 2 dispatch facts,
// none of the vulnerable shapes, so the existing equivalence gates were structurally too small to
// catch this. DeepChain now hosts the vulnerable shapes (all with an endpoint in the edited file,
// Business/ChannelBase.cs, and the emitting site elsewhere):
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
// The decisive assertion is at the EAGER window (after ApplyEditAsync, before ReconcileAsync): the
// edit only adds a call inside a method body, so no other file's facts legitimately change, and
// CurrentFacts — which IS served during the disclosure window — must already be set-equal to a cold
// index of the edited tree on all five fact kinds. Under the symbol-dropping merge the four
// cross-file dispatch edges above vanish here (verified: this test FAILS against that merge). The
// post-reconcile assertion then pins the brief's letter: cold facts == CurrentFacts after
// edit+reconcile.
public sealed class ResidentIndexScaleTests
{
    private const string NotifierNotify = "M:Domain.INotifier.Notify(System.String)";
    private const string ChannelBaseNotify = "M:Business.ChannelBase.Notify(System.String)";
    private const string SmsChannelNotify = "M:Business.SmsChannel.Notify(System.String)";
    private const string PushChannelNotify = "M:Web.PushChannel.Notify(System.String)";
    private const string RelaySendField = "F:ApiGateway.NotificationRelay._send";

    [Test]
    public async Task Cross_file_dispatch_and_relation_facts_survive_a_single_file_edit_and_reconcile()
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
            t.TypeSymbolId == "T:ApiGateway.EmailChannel" && t.RelationKind == "base" && t.RelatedSymbolId == "T:Business.ChannelBase"
        );
        baseRelations.ShouldContain(t =>
            t.TypeSymbolId == "T:ApiGateway.EmailChannel" && t.RelationKind == "interface" && t.RelatedSymbolId == "T:Domain.INotifier"
        );
        baseRelations.ShouldContain(t =>
            t.TypeSymbolId == "T:Web.PushChannel" && t.RelationKind == "base" && t.RelatedSymbolId == "T:Business.ChannelBase"
        );

        // ---- 2. The edit: ChannelBase.Notify's BODY gains a Foundation.Db.Query call ----
        // Body-only + call-add, so no OTHER file's facts legitimately change — which is what licenses
        // the eager-window equality assertion below.
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

        // ---- 4. EAGER window: the decisive assertion. CurrentFacts is served during the disclosure
        // window, and only the edited file's own facts may differ from the cold truth — the four
        // cross-file dispatch edges must all still be present. Crisp per-edge checks first (diagnosis),
        // then the full five-kind set equality.
        var eager = index.CurrentFacts;
        index.UnreconciledProjects.ShouldNotBeEmpty(); // it IS the eager window, not a reconciled state
        var eagerDispatch = eager.DispatchFacts ?? [];
        eagerDispatch.ShouldContain(
            d => d.SourceMember == NotifierNotify && d.TargetMember == ChannelBaseNotify && d.Kind == DispatchKinds.Impl,
            "LOST inherited-impl edge: emitted at EmailChannel.cs, endpoint in the edited file"
        );
        eagerDispatch.ShouldContain(
            d => d.SourceMember == ChannelBaseNotify && d.TargetMember == PushChannelNotify && d.Kind == DispatchKinds.Override,
            "LOST override edge: emitted at PushChannel.cs, endpoint in the edited file"
        );
        eagerDispatch.ShouldContain(
            d => d.SourceMember == RelaySendField && d.TargetMember == ChannelBaseNotify && d.Kind == DispatchKinds.DelegateBind,
            "LOST delegate-bind edge: emitted at EmailChannel.cs, target in the edited file"
        );

        AssertFactSetsEqual(eager, oracle, playground.RootDirectory, arm: "EAGER (edited file only, cascade still owed)");

        // ---- 5. RECONCILED: cold-index facts == CurrentFacts after edit+reconcile, all five kinds ----
        await index.ReconcileAsync();
        index.UnreconciledProjects.ShouldBeEmpty();
        AssertFactSetsEqual(index.CurrentFacts, oracle, playground.RootDirectory, arm: "RECONCILED");
    }

    private static void AssertFactSetsEqual(AnalysisResult actual, AnalysisResult oracle, string root, string arm)
    {
        var actualFacts = CanonicalFacts(actual, root);
        var oracleFacts = CanonicalFacts(oracle, root);

        var onlyActual = actualFacts.Except(oracleFacts).OrderBy(l => l, StringComparer.Ordinal).ToArray();
        var onlyOracle = oracleFacts.Except(actualFacts).OrderBy(l => l, StringComparer.Ordinal).ToArray();

        var report = new StringBuilder();
        report.AppendLine($"[{arm}] overlay: {Counts(actual)} | cold oracle: {Counts(oracle)}");
        if (onlyActual.Length == 0 && onlyOracle.Length == 0)
        {
            report.AppendLine("VERDICT: IDENTICAL — overlay CurrentFacts set-equal cold facts.");
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

    // Same canonical identity tuples as ResidentIndexTests/IncrementalExtractionSpikeTests — full
    // identity per fact kind (relations: TypeSymbolId|RelationKind|RelatedSymbolId; dispatch:
    // SourceMember|Kind|TargetMember), set-based so base-internal duplicates (the same edge emitted
    // from several files) never mask a loss and never fake one.
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
