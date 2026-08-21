using System.Data.Common;
using System.Linq.Expressions;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Storage;

namespace Rig.Storage.Queries;

// The STORE-side row supply for SymbolFactProjections: the EF projections, plus the SQL SELECT list and
// raw-ADO reader for the one bounded (ordinal-indexed) path. Every loader hands the canonical SymbolFact ROW
// record to the shared mapping instead of writing a field list of its own — see SymbolFactProjections for the
// why, and ReferenceFactRows for the identical pattern on reference_facts.
//
// Each projection fills ONLY the columns its mapping reads and passes placeholders ("" / 0 / null / false) for
// the rest of the 14-field SymbolFact. That is deliberate and load-bearing for perf: the method scans here run
// over every declared method in the store (~217k on the real one), so widening them to select Signature /
// BodyHash / TypeKind for a projection that never reads those would cost real marshalling. A placeholder is
// a SQL constant, not a column — the emitted SELECT stays exactly as narrow as it was before the mappings
// were single-sourced.
internal static class SymbolFactRows
{
    // MethodRef's column set (SymbolFactProjections.MethodRefColumn), for the EF whole-store graph load.
    internal static readonly Expression<Func<SymbolFactEntity, SymbolFact>> MethodRefRow = s => new SymbolFact(
        SymbolId: s.SymbolId,
        Kind: "",
        Name: s.Name,
        Namespace: "",
        ContainingSymbolId: s.ContainingSymbolId,
        Modifiers: "",
        TypeKind: "",
        Signature: "",
        FilePath: s.FilePath,
        Line: s.Line,
        EndLine: 0,
        DefiningAssembly: "",
        IsOverride: s.IsOverride,
        BodyHash: ""
    );

    // `s.SymbolId, s.Name, …` for the raw-SQL bounded scan of symbol_facts, in MethodRefColumn order — so
    // `(int)MethodRefColumn.X` IS the reader ordinal in ReadMethodRefRow below.
    internal static string MethodRefSelectList(string alias) =>
        string.Join(", ", SymbolFactProjections.MethodRefColumns.Select(column => $"{alias}.{column}"));

    // The bounded (raw-ADO) MethodRef row reader. Ordinals come from the same enum that generated the SELECT
    // list, so adding a column shifts both together and no read can land on the wrong field. Null handling is
    // the loader's own, unchanged: a null ContainingSymbolId reads as null, a null Line as 0, and IsOverride
    // as false. (A null FilePath now reads as "" rather than null, matching SymbolFact's non-nullable field —
    // unreachable in practice: symbol_facts.FilePath is written from a non-nullable record field.)
    internal static SymbolFact ReadMethodRefRow(DbDataReader reader)
    {
        string? Text(SymbolFactProjections.MethodRefColumn column) =>
            reader.IsDBNull((int)column) ? null : reader.GetString((int)column);

        return new SymbolFact(
            SymbolId: reader.GetString((int)SymbolFactProjections.MethodRefColumn.SymbolId),
            Kind: "",
            Name: Text(SymbolFactProjections.MethodRefColumn.Name) ?? "",
            Namespace: "",
            ContainingSymbolId: Text(SymbolFactProjections.MethodRefColumn.ContainingSymbolId),
            Modifiers: "",
            TypeKind: "",
            Signature: "",
            FilePath: Text(SymbolFactProjections.MethodRefColumn.FilePath) ?? "",
            Line: reader.IsDBNull((int)SymbolFactProjections.MethodRefColumn.Line)
                ? 0
                : reader.GetInt32((int)SymbolFactProjections.MethodRefColumn.Line),
            EndLine: 0,
            DefiningAssembly: "",
            IsOverride: !reader.IsDBNull((int)SymbolFactProjections.MethodRefColumn.IsOverride)
                && reader.GetInt32((int)SymbolFactProjections.MethodRefColumn.IsOverride) != 0,
            BodyHash: ""
        );
    }

    // MethodSymbol's column set (MethodRef's + Signature), for the EF entry-point-data load.
    internal static readonly Expression<Func<SymbolFactEntity, SymbolFact>> MethodSymbolRow = s => new SymbolFact(
        SymbolId: s.SymbolId,
        Kind: "",
        Name: s.Name,
        Namespace: "",
        ContainingSymbolId: s.ContainingSymbolId,
        Modifiers: "",
        TypeKind: "",
        Signature: s.Signature,
        FilePath: s.FilePath,
        Line: s.Line,
        EndLine: 0,
        DefiningAssembly: "",
        IsOverride: s.IsOverride,
        BodyHash: ""
    );

    // TypeSymbol's column set. Modifiers is the raw space-joined token string: IsAbstract is a String.Split
    // test, which has no SQL translation, so the projection runs client-side on this row (exactly as the
    // in-memory twin does).
    internal static readonly Expression<Func<SymbolFactEntity, SymbolFact>> TypeSymbolRow = s => new SymbolFact(
        SymbolId: s.SymbolId,
        Kind: "",
        Name: "",
        Namespace: s.Namespace,
        ContainingSymbolId: null,
        Modifiers: s.Modifiers,
        TypeKind: "",
        Signature: "",
        FilePath: s.FilePath,
        Line: s.Line,
        EndLine: 0,
        DefiningAssembly: "",
        IsOverride: false,
        BodyHash: ""
    );

    // MethodMeta's column set (the dead-code finder's), for the EF whole-store load.
    internal static readonly Expression<Func<SymbolFactEntity, SymbolFact>> MethodMetaRow = s => new SymbolFact(
        SymbolId: s.SymbolId,
        Kind: "",
        Name: s.Name,
        Namespace: "",
        ContainingSymbolId: null,
        Modifiers: s.Modifiers,
        TypeKind: "",
        Signature: "",
        FilePath: s.FilePath,
        Line: s.Line,
        EndLine: 0,
        DefiningAssembly: "",
        IsOverride: s.IsOverride,
        BodyHash: ""
    );
}
