using System.Data.Common;
using Rig.Domain.Data;

namespace Rig.Storage.Queries;

// symbol_facts is written through raw ADO rather than EF's change tracker: indexing emits one row per
// declared symbol, hundreds of thousands of them on a monorepo. Keep that performance choice local without
// making Writes.SaveFactsBatchedAsync understand an 18-column storage layout. Column names, SQL order, and
// parameter ordinals all come from this one enum; a hand-aligned trio of INSERT text, parameter-name array
// and ordinal binder (p[0]..p[17]) silently shifts every column after one inserted mid-list, with no
// compiler complaint and no error at write time — just wrong data.
internal static class SymbolFactBulkInsert
{
    internal static IReadOnlyList<string> ColumnNames { get; } = Enum.GetNames<Column>();

    internal static readonly string[] ParameterNames = Enumerable.Range(0, ColumnNames.Count).Select(i => $"$p{i}").ToArray();

    internal static readonly string Sql =
        $"INSERT INTO symbol_facts ({string.Join(", ", ColumnNames)}) VALUES ({string.Join(",", ParameterNames)});";

    internal static Action<DbParameter[], SymbolFact, int> Binder(string runId) =>
        (parameters, symbol, index) => Bind(parameters, runId, index, symbol);

    private static void Bind(DbParameter[] parameters, string runId, int index, SymbolFact symbol)
    {
        BindIdentity(parameters, runId, index, symbol);
        BindDeclaration(parameters, symbol);
        BindLocation(parameters, symbol);
        BindSurface(parameters, symbol);
    }

    private static void BindIdentity(DbParameter[] parameters, string runId, int index, SymbolFact symbol)
    {
        Set(parameters, Column.RunId, runId);
        Set(parameters, Column.SymbolFactIndex, index);
        Set(parameters, Column.SymbolId, symbol.SymbolId);
        Set(parameters, Column.ContainingSymbolId, symbol.ContainingSymbolId);
    }

    private static void BindDeclaration(DbParameter[] parameters, SymbolFact symbol)
    {
        Set(parameters, Column.Kind, symbol.Kind);
        Set(parameters, Column.Name, symbol.Name);
        Set(parameters, Column.Namespace, symbol.Namespace);
        Set(parameters, Column.Modifiers, symbol.Modifiers);
        Set(parameters, Column.TypeKind, symbol.TypeKind);
        Set(parameters, Column.Signature, symbol.Signature);
        Set(parameters, Column.IsOverride, symbol.IsOverride);
        Set(parameters, Column.IsIterator, symbol.IsIterator);
    }

    private static void BindLocation(DbParameter[] parameters, SymbolFact symbol)
    {
        Set(parameters, Column.FilePath, symbol.FilePath);
        Set(parameters, Column.Line, symbol.Line);
        Set(parameters, Column.EndLine, symbol.EndLine);
        Set(parameters, Column.DefiningAssembly, symbol.DefiningAssembly);
    }

    private static void BindSurface(DbParameter[] parameters, SymbolFact symbol)
    {
        Set(parameters, Column.BodyHash, symbol.BodyHash);
        Set(parameters, Column.SurfaceHash, symbol.SurfaceHash);
    }

    private static void Set(DbParameter[] parameters, Column column, object? value) =>
        parameters[(int)column].Value = value switch
        {
            null => DBNull.Value,
            bool boolean => boolean ? 1 : 0,
            _ => value,
        };

    private enum Column
    {
        RunId,
        SymbolFactIndex,
        SymbolId,
        Kind,
        Name,
        Namespace,
        ContainingSymbolId,
        Modifiers,
        TypeKind,
        Signature,
        FilePath,
        Line,
        EndLine,
        DefiningAssembly,
        IsOverride,
        BodyHash,
        SurfaceHash,
        IsIterator,
    }
}
