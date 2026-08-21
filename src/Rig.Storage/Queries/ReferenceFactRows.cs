using System.Data.Common;
using System.Linq.Expressions;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Storage;

namespace Rig.Storage.Queries;

// The STORE-side row supply for FactInvocationProjection: the SQL SELECT list, the raw-ADO row reader and the
// EF projection, all three generated from / typed against the ONE column set in
// FactInvocationProjection.Column. Neither loader writes a field list of its own any more, and neither maps a
// hard-coded ordinal: the bounded reader indexes by `(int)Column.X`, which is the ordinal precisely because
// InvocationSelectList emits the columns in that same enum order.
//
// Why the hop through ReferenceFact instead of projecting straight to FactInvocation on each path: the record
// mapping must exist exactly ONCE (that is the whole point — see FactInvocationProjection), and the EF path
// can only build its projection inside an expression tree while the ADO path can only build one from a reader.
// So each path produces the canonical ROW record and the shared function does the mapping. The intermediate is
// per-row and immediately discarded (the EF path streams, so it never materializes two whole lists).
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
