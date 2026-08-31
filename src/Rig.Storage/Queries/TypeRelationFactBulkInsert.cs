using System.Data.Common;
using Rig.Domain.Data;

namespace Rig.Storage.Queries;

// type_relation_facts is written through raw ADO rather than EF's change tracker: one row per base/interface
// edge in the solution. Keep that performance choice local without making Writes.SaveFactsBatchedAsync
// understand the storage layout. Column names, SQL order, and parameter ordinals all come from this one enum;
// a hand-aligned trio of INSERT text, parameter-name array and ordinal binder silently shifts every column
// after one inserted mid-list, and TypeSymbolId/RelatedSymbolId are interchangeable-looking DocIDs — a
// swapped pair inverts every class hierarchy in the store with no error at write time.
internal static class TypeRelationFactBulkInsert
{
    internal static IReadOnlyList<string> ColumnNames { get; } = Enum.GetNames<Column>();

    internal static readonly string[] ParameterNames = Enumerable.Range(0, ColumnNames.Count).Select(i => $"$p{i}").ToArray();

    internal static readonly string Sql =
        $"INSERT INTO type_relation_facts ({string.Join(", ", ColumnNames)}) VALUES ({string.Join(",", ParameterNames)});";

    internal static Action<DbParameter[], TypeRelationFact, int> Binder(string runId) =>
        (parameters, relation, index) => Bind(parameters, runId, index, relation);

    private static void Bind(DbParameter[] parameters, string runId, int index, TypeRelationFact relation)
    {
        Set(parameters, Column.RunId, runId);
        Set(parameters, Column.TypeRelationFactIndex, index);
        Set(parameters, Column.TypeSymbolId, relation.TypeSymbolId);
        Set(parameters, Column.RelatedSymbolId, relation.RelatedSymbolId);
        Set(parameters, Column.RelationKind, relation.RelationKind);
        Set(parameters, Column.FilePath, relation.FilePath);
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
        TypeRelationFactIndex,
        TypeSymbolId,
        RelatedSymbolId,
        RelationKind,
        FilePath,
    }
}
