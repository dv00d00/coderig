using System.Data.Common;
using Rig.Domain.Data;

namespace Rig.Storage.Queries;

// allocation_facts is written through raw ADO rather than EF's change tracker: every object/array/boxing
// allocation site in the solution emits a row, which makes this the highest-volume fact table after
// reference_facts. Keep that performance choice local without making Writes.SaveFactsBatchedAsync understand
// a 15-column storage layout. Column names, SQL order, and parameter ordinals all come from this one enum;
// a hand-aligned trio of INSERT text, parameter-name array and ordinal binder silently shifts every column
// after one inserted mid-list — and here the shifted neighbours are same-typed nullable strings, so nothing
// would ever throw.
internal static class AllocationFactBulkInsert
{
    internal static IReadOnlyList<string> ColumnNames { get; } = Enum.GetNames<Column>();

    internal static readonly string[] ParameterNames = Enumerable.Range(0, ColumnNames.Count).Select(i => $"$p{i}").ToArray();

    internal static readonly string Sql =
        $"INSERT INTO allocation_facts ({string.Join(", ", ColumnNames)}) VALUES ({string.Join(",", ParameterNames)});";

    internal static Action<DbParameter[], AllocationFact, int> Binder(string runId) =>
        (parameters, allocation, index) => Bind(parameters, runId, index, allocation);

    private static void Bind(DbParameter[] parameters, string runId, int index, AllocationFact allocation)
    {
        BindSite(parameters, runId, index, allocation);
        BindStructuralContext(parameters, allocation);
        BindSize(parameters, allocation);
    }

    private static void BindSite(DbParameter[] parameters, string runId, int index, AllocationFact allocation)
    {
        Set(parameters, Column.RunId, runId);
        Set(parameters, Column.AllocationFactIndex, index);
        Set(parameters, Column.Operation, allocation.Operation);
        Set(parameters, Column.ResourceType, allocation.ResourceType);
        Set(parameters, Column.EnclosingSymbolId, allocation.EnclosingSymbolId);
        Set(parameters, Column.FilePath, allocation.FilePath);
        Set(parameters, Column.Line, allocation.Line);
    }

    private static void BindStructuralContext(DbParameter[] parameters, AllocationFact allocation)
    {
        Set(parameters, Column.EnclosingLoopKind, allocation.EnclosingLoopKind);
        Set(parameters, Column.EnclosingLoopDetail, allocation.EnclosingLoopDetail);
        Set(parameters, Column.EnclosingGuards, allocation.EnclosingGuards);
        Set(parameters, Column.Mechanism, allocation.Mechanism);
        Set(parameters, Column.Cardinality, allocation.Cardinality);
    }

    private static void BindSize(DbParameter[] parameters, AllocationFact allocation)
    {
        Set(parameters, Column.ShallowSizeBytes, allocation.ShallowSizeBytes);
        Set(parameters, Column.SizeConfidence, allocation.SizeConfidence);
        Set(parameters, Column.SizeBasis, allocation.SizeBasis);
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
        AllocationFactIndex,
        Operation,
        ResourceType,
        EnclosingSymbolId,
        FilePath,
        Line,
        EnclosingLoopKind,
        EnclosingLoopDetail,
        EnclosingGuards,
        Mechanism,
        Cardinality,
        ShallowSizeBytes,
        SizeConfidence,
        SizeBasis,
    }
}
