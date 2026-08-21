using System.Data.Common;
using System.Linq.Expressions;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Storage;

namespace Rig.Storage.Queries;

// The STORE-side row supply for the reference_facts projections in Rig.Domain — FactInvocationProjection,
// CallEdgeProjection and FactFieldAccessProjection. It holds the SQL SELECT list, the raw-ADO row reader and
// the EF projections; the invocation trio is generated from / typed against the ONE column set in
// FactInvocationProjection.Column, so no loader writes a field list of its own and none maps a hard-coded
// ordinal: the bounded reader indexes by `(int)Column.X`, which is the ordinal precisely because
// InvocationSelectList emits the columns in that same enum order.
//
// Why the hop through ReferenceFact instead of projecting straight to the target record on each path: the
// record mapping must exist exactly ONCE (that is the whole point — see FactInvocationProjection), and the EF
// path can only build its projection inside an expression tree while the ADO path can only build one from a
// reader. So each path produces the canonical ROW record and the shared function does the mapping. The
// intermediate is per-row and immediately discarded (the whole-store EF paths STREAM, so they never
// materialize two whole lists). Each projection selects only the columns its mapping reads and passes
// placeholders for the rest, which keeps every scan exactly as narrow as it was before it was single-sourced.
internal static class ReferenceFactRows
{
    // `r.TargetSymbolId, r.EnclosingSymbolId, …` for a raw-SQL scan of reference_facts, in Column order.
    internal static string InvocationSelectList(string alias) =>
        string.Join(", ", FactInvocationProjection.Columns.Select(column => $"{alias}.{column}"));

    // The EF (whole-store) projection: exactly the invocation column set, marshalled server-side, in the
    // canonical row record. RefKind is a constant because the caller's WHERE already pins it; TargetAssembly /
    // TargetInSource are placeholders NOT selected from the store — FactInvocationProjection.Project reads
    // neither, and keeping them out of the projection keeps the whole-store scan as narrow as it was before
    // this was single-sourced (it is the `derive` path over millions of rows).
    internal static readonly Expression<Func<ReferenceFactEntity, ReferenceFact>> InvocationRow = r => new ReferenceFact(
        TargetSymbolId: r.TargetSymbolId,
        RefKind: RefKinds.Invocation,
        EnclosingSymbolId: r.EnclosingSymbolId,
        TargetAssembly: "",
        TargetInSource: false,
        FilePath: r.FilePath,
        Line: r.Line,
        ReceiverType: r.ReceiverType,
        FirstArgumentTemplate: r.FirstArgumentTemplate,
        FirstArgumentType: r.FirstArgumentType,
        EnclosingLoopKind: r.EnclosingLoopKind,
        EnclosingLoopDetail: r.EnclosingLoopDetail,
        EnclosingInvocations: r.EnclosingInvocations,
        EnclosingCatchTypes: r.EnclosingCatchTypes,
        TypeArguments: r.TypeArguments,
        FirstArgumentName: r.FirstArgumentName,
        // Not part of the invocation column set (methodGroup/graph-only fields) — passed explicitly because an
        // expression tree may not skip an optional parameter and then name a later one (CS9307).
        DelegateConsumer: null,
        EnclosingScopes: r.EnclosingScopes,
        ArgumentTemplates: r.ArgumentTemplates,
        ArgumentNames: r.ArgumentNames,
        DeclaringTypeArgBinding: null,
        MethodTypeArgBinding: null,
        NonVirtual: false,
        EnclosingGuards: r.EnclosingGuards,
        EnclosingLoopElementType: r.EnclosingLoopElementType,
        EnclosingLoopBindType: r.EnclosingLoopBindType,
        InExpressionTree: r.InExpressionTree
    );

    // The EF (whole-store) row supply for CallEdgeProjection: exactly the columns a CallEdge is a function of
    // (see that file), marshalled server-side into the canonical row record. RefKind is SELECTED here (unlike
    // the invocation row, where the caller's WHERE pins it to one value) because a call edge keeps its
    // originating ref's kind — invocation / methodGroup / ctor. TargetAssembly / TargetInSource are
    // placeholders NOT selected from the store: the caller's WHERE already decided first-party-ness, and
    // CallEdgeProjection reads neither — keeping them out holds this whole-store scan to the same 14 columns
    // it selected before the mapping was single-sourced.
    internal static readonly Expression<Func<ReferenceFactEntity, ReferenceFact>> CallEdgeRow = r => new ReferenceFact(
        TargetSymbolId: r.TargetSymbolId,
        RefKind: r.RefKind,
        EnclosingSymbolId: r.EnclosingSymbolId,
        TargetAssembly: "",
        TargetInSource: false,
        FilePath: r.FilePath,
        Line: r.Line,
        ReceiverType: r.ReceiverType,
        // Invocation-only columns the call-edge projection does not read — passed explicitly because an
        // expression tree may not skip an optional parameter and then name a later one (CS9307).
        FirstArgumentTemplate: null,
        FirstArgumentType: null,
        EnclosingLoopKind: r.EnclosingLoopKind,
        EnclosingLoopDetail: r.EnclosingLoopDetail,
        EnclosingInvocations: null,
        EnclosingCatchTypes: null,
        TypeArguments: r.TypeArguments,
        FirstArgumentName: null,
        DelegateConsumer: r.DelegateConsumer,
        EnclosingScopes: null,
        ArgumentTemplates: null,
        ArgumentNames: null,
        DeclaringTypeArgBinding: r.DeclaringTypeArgBinding,
        MethodTypeArgBinding: r.MethodTypeArgBinding,
        NonVirtual: r.NonVirtual,
        EnclosingGuards: r.EnclosingGuards,
        EnclosingLoopElementType: null,
        EnclosingLoopBindType: null,
        InExpressionTree: false
    );

    // One static-field ACCESS ref joined to its target symbol: the canonical row record plus the target's
    // readonly-ness (`Row.RefKind` carries read-vs-write, so the combined loader can partition on it).
    internal sealed record FieldAccessJoinRow(ReferenceFact Row, bool IsReadonly);

    // The row supply for FactFieldAccessProjection, shared by BOTH static-field-access loaders (the combined
    // two-arm one and the single-kind, enclosing-scope-bounded one — see Reads.StaticFieldAccessRowsAsync,
    // its only caller, so this column list exists once). Selects exactly the columns a FactFieldAccess is a
    // function of, plus RefKind.
    //
    // Written as the JOIN rather than a bare row expression because EF cannot translate a Join whose key
    // selector reads a projected record (`.Select(row).Join(…, r => r.TargetSymbolId, …)` fails at
    // translation — the projection is inlined into the key selector). So the entities are joined first and
    // the row record is built in the SELECT, which is a plain shaper EF handles fine. `refs` carries the
    // caller's ref filters (kind, first-party, enclosing scope) and `staticSymbols` its static/readonly gate.
    internal static IQueryable<FieldAccessJoinRow> FieldAccessJoin(
        IQueryable<ReferenceFactEntity> refs,
        IQueryable<SymbolFactEntity> staticSymbols
    ) =>
        refs.Join(
            staticSymbols,
            r => r.TargetSymbolId,
            s => s.SymbolId,
            (r, s) =>
                new FieldAccessJoinRow(
                    new ReferenceFact(
                        TargetSymbolId: r.TargetSymbolId,
                        RefKind: r.RefKind,
                        EnclosingSymbolId: r.EnclosingSymbolId,
                        TargetAssembly: "",
                        TargetInSource: false,
                        FilePath: r.FilePath,
                        Line: r.Line,
                        // Not part of the field-access column set — passed explicitly for the same CS9307
                        // reason as above.
                        ReceiverType: null,
                        FirstArgumentTemplate: null,
                        FirstArgumentType: null,
                        EnclosingLoopKind: r.EnclosingLoopKind,
                        EnclosingLoopDetail: r.EnclosingLoopDetail,
                        EnclosingInvocations: r.EnclosingInvocations,
                        EnclosingCatchTypes: r.EnclosingCatchTypes,
                        TypeArguments: null,
                        FirstArgumentName: null,
                        DelegateConsumer: null,
                        EnclosingScopes: r.EnclosingScopes,
                        ArgumentTemplates: null,
                        ArgumentNames: null,
                        DeclaringTypeArgBinding: null,
                        MethodTypeArgBinding: null,
                        NonVirtual: false,
                        EnclosingGuards: null,
                        EnclosingLoopElementType: null,
                        EnclosingLoopBindType: null,
                        InExpressionTree: false
                    ),
                    s.Modifiers.Contains("readonly")
                )
        );

    // The bounded (raw-ADO) row reader. Every ordinal is `(int)Column.X` — derived from the same enum that
    // generated the SELECT list, so adding a column shifts both together and no read can silently land on the
    // wrong (or a missing) field. Same null handling the loader has always applied: a null FilePath reads as
    // "" and a null Line as 0, matching the non-nullable ReferenceFact fields.
    internal static ReferenceFact ReadInvocationRow(DbDataReader reader)
    {
        string? Text(FactInvocationProjection.Column column) =>
            reader.IsDBNull((int)column) ? null : reader.GetString((int)column);

        return new ReferenceFact(
            TargetSymbolId: reader.GetString((int)FactInvocationProjection.Column.TargetSymbolId),
            RefKind: RefKinds.Invocation,
            EnclosingSymbolId: Text(FactInvocationProjection.Column.EnclosingSymbolId),
            TargetAssembly: "",
            TargetInSource: false,
            FilePath: Text(FactInvocationProjection.Column.FilePath) ?? "",
            Line: reader.IsDBNull((int)FactInvocationProjection.Column.Line)
                ? 0
                : reader.GetInt32((int)FactInvocationProjection.Column.Line),
            ReceiverType: Text(FactInvocationProjection.Column.ReceiverType),
            FirstArgumentTemplate: Text(FactInvocationProjection.Column.FirstArgumentTemplate),
            FirstArgumentType: Text(FactInvocationProjection.Column.FirstArgumentType),
            EnclosingLoopKind: Text(FactInvocationProjection.Column.EnclosingLoopKind),
            EnclosingLoopDetail: Text(FactInvocationProjection.Column.EnclosingLoopDetail),
            EnclosingInvocations: Text(FactInvocationProjection.Column.EnclosingInvocations),
            EnclosingCatchTypes: Text(FactInvocationProjection.Column.EnclosingCatchTypes),
            TypeArguments: Text(FactInvocationProjection.Column.TypeArguments),
            FirstArgumentName: Text(FactInvocationProjection.Column.FirstArgumentName),
            EnclosingScopes: Text(FactInvocationProjection.Column.EnclosingScopes),
            ArgumentTemplates: Text(FactInvocationProjection.Column.ArgumentTemplates),
            ArgumentNames: Text(FactInvocationProjection.Column.ArgumentNames),
            EnclosingGuards: Text(FactInvocationProjection.Column.EnclosingGuards),
            EnclosingLoopElementType: Text(FactInvocationProjection.Column.EnclosingLoopElementType),
            EnclosingLoopBindType: Text(FactInvocationProjection.Column.EnclosingLoopBindType),
            InExpressionTree: !reader.IsDBNull((int)FactInvocationProjection.Column.InExpressionTree)
                && reader.GetBoolean((int)FactInvocationProjection.Column.InExpressionTree)
        );
    }
}
