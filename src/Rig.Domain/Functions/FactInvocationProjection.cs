using Rig.Domain.Data;

namespace Rig.Domain.Functions;

// The SINGLE source of truth for `reference_facts` (RefKind=invocation) -> FactInvocation: the column SET the
// record is a function of, and the row->record mapping itself. Three read paths build FactInvocation records —
// the EF whole-store loader (Reads.LoadInvocationRefsAsync), the raw-ADO BOUNDED loader
// (SqlReachability.LoadReachInputsAsync) and the in-memory twin (LiveReads.InvocationRefs) — and until
// 2026-08-21 each carried its OWN hand-written copy of the field list. They drifted: the bounded loader never
// selected `EnclosingScopes`, so FactInvocation.EnclosingScopes arrived null on the reaches/tree/path fast
// path and every lexical-scope observation (`lock_held_across_effect` / `transaction_spans_effect`, derived in
// FactEffectDeriver from exactly that field) silently vanished from rig's most-used commands while `derive`
// reported them. Same store, same rules, two different answers.
//
// So the mapping lives HERE, once, and every path funnels through it. The precedent is DeliverySiteProjection:
// the pure projection core in the domain, the row SUPPLY left to each caller (SQL scan / EF / AnalysisResult).
//
// `Column` is the load-bearing half of the fix. Its member NAMES are simultaneously
//   - the ReferenceFact property names this projection reads,
//   - the `reference_facts` column names (ReferenceFactEntity mirrors the table 1:1), and
//   - the ADO reader ORDINALS on the bounded path — because that path's SELECT list is GENERATED from this
//     enum in declaration order (ReferenceFactRows.InvocationSelectList), so `(int)Column.X` IS the ordinal.
// Adding a field to FactInvocation therefore means adding ONE enum member plus ONE line in Project: the SQL
// SELECT, the ordinals and the live path follow automatically. A field cannot be skipped on one path only.
// Kept honest by ReachInputProjectionTests, which compares bounded vs whole-store records field by field.
public static class FactInvocationProjection
{
    // The reference_facts column set FactInvocation is derived from, in SELECT order. Declaration order is
    // load-bearing (it is the ADO ordinal set) — APPEND new members, never insert or reorder.
    public enum Column
    {
        TargetSymbolId,
        EnclosingSymbolId,
        FilePath,
        Line,
        ReceiverType,
        FirstArgumentTemplate,
        FirstArgumentType,
        EnclosingLoopKind,
        EnclosingLoopDetail,
        EnclosingInvocations,
        EnclosingCatchTypes,
        TypeArguments,
        FirstArgumentName,
        EnclosingScopes,
        ArgumentTemplates,
        ArgumentNames,
        EnclosingGuards,
        EnclosingLoopElementType,
        EnclosingLoopBindType,
        InExpressionTree,
    }

    // The column names in ordinal order — Enum.GetNames orders by value, which for a default-valued enum is
    // declaration order. Drives the bounded path's SELECT list (see ReferenceFactRows.InvocationSelectList).
    public static readonly IReadOnlyList<string> Columns = Enum.GetNames<Column>();

    // THE mapping: one reference_facts row (as a ReferenceFact — the canonical row record, which every read
    // path can produce cheaply) -> the FactInvocation the stage-2 derivers consume. Reads ONLY the fields named
    // in `Column`; RefKind/TargetAssembly/TargetInSource and the graph-only columns (DelegateConsumer,
    // NonVirtual, the type-arg bindings) are deliberately not part of the invocation projection, so a loader
    // that supplies placeholders for them is still correct.
    public static FactInvocation Project(ReferenceFact r) =>
        new FactInvocation(
            Target: r.TargetSymbolId,
            Enclosing: r.EnclosingSymbolId,
            FilePath: r.FilePath,
            Line: r.Line,
            Receiver: r.ReceiverType,
            FirstArgTemplate: r.FirstArgumentTemplate,
            FirstArgType: r.FirstArgumentType,
            LoopKind: r.EnclosingLoopKind,
            LoopDetail: r.EnclosingLoopDetail,
            EnclosingInvocations: r.EnclosingInvocations,
            CatchTypes: r.EnclosingCatchTypes,
            TypeArguments: r.TypeArguments,
            FirstArgName: r.FirstArgumentName,
            EnclosingScopes: r.EnclosingScopes,
            ArgumentTemplates: r.ArgumentTemplates,
            ArgumentNames: r.ArgumentNames,
            EnclosingGuards: r.EnclosingGuards,
            LoopElementType: r.EnclosingLoopElementType,
            LoopBindType: r.EnclosingLoopBindType,
            InExpressionTree: r.InExpressionTree
        );
}
