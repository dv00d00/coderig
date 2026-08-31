using System.Data.Common;
using Rig.Domain.Data;

namespace Rig.Storage.Queries;

// dispatch_facts is written through raw ADO rather than EF's change tracker: one row per Roslyn-mined
// override/impl edge. Keep that performance choice local without making Writes.SaveFactsBatchedAsync
// understand the storage layout. Column names, SQL order, and parameter ordinals all come from this one enum;
// a hand-aligned trio of INSERT text, parameter-name array and ordinal binder silently shifts every column
// after one inserted mid-list, and SourceMember/TargetMember are interchangeable-looking DocIDs — a swapped
// pair reverses every dispatch edge in the store with no error at write time.
internal static class DispatchFactBulkInsert
{
    internal static IReadOnlyList<string> ColumnNames { get; } = Enum.GetNames<Column>();

    internal static readonly string[] ParameterNames = Enumerable.Range(0, ColumnNames.Count).Select(i => $"$p{i}").ToArray();

    internal static readonly string Sql =
        $"INSERT INTO dispatch_facts ({string.Join(", ", ColumnNames)}) VALUES ({string.Join(",", ParameterNames)});";

    internal static Action<DbParameter[], DispatchFact, int> Binder(string runId) =>
        (parameters, dispatch, index) => Bind(parameters, runId, index, dispatch);

    private static void Bind(DbParameter[] parameters, string runId, int index, DispatchFact dispatch)
    {
        Set(parameters, Column.RunId, runId);
        Set(parameters, Column.DispatchFactIndex, index);
        Set(parameters, Column.SourceMember, dispatch.SourceMember);
        Set(parameters, Column.TargetMember, dispatch.TargetMember);
        Set(parameters, Column.Kind, dispatch.Kind);
        Set(parameters, Column.FilePath, dispatch.FilePath);
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
        DispatchFactIndex,
        SourceMember,
        TargetMember,
        Kind,
        FilePath,
    }
}
