using System.Data.Common;
using Rig.Domain.Data;

namespace Rig.Storage.Queries;

// The reference row is deliberately written through raw ADO rather than EF's change tracker: indexing can
// emit millions of these rows. Keep that performance choice local without making Writes.SaveFactsBatchedAsync
// understand a 29-column storage layout. Column names, SQL order, and parameter ordinals all come from this
// one enum; the bind methods use named columns and stay grouped by the corresponding ReferenceFact concerns.
internal static class ReferenceFactBulkInsert
{
    internal static IReadOnlyList<string> ColumnNames { get; } = Enum.GetNames<Column>();

    internal static readonly string[] ParameterNames = Enumerable.Range(0, ColumnNames.Count).Select(i => $"$p{i}").ToArray();

    internal static readonly string Sql =
        $"INSERT INTO reference_facts ({string.Join(", ", ColumnNames)}) VALUES ({string.Join(",", ParameterNames)});";

    internal static Action<DbParameter[], ReferenceFact, int> Binder(string runId) =>
        (parameters, reference, index) => Bind(parameters, runId, index, reference);

    private static void Bind(DbParameter[] parameters, string runId, int index, ReferenceFact reference)
    {
        BindIdentity(parameters, runId, index, reference);
        BindInvocation(parameters, reference);
        BindStructuralContext(parameters, reference);
        BindTypeFlow(parameters, reference);
    }

    private static void BindIdentity(DbParameter[] parameters, string runId, int index, ReferenceFact reference)
    {
        Set(parameters, Column.RunId, runId);
        Set(parameters, Column.ReferenceFactIndex, index);
        Set(parameters, Column.TargetSymbolId, reference.TargetSymbolId);
        Set(parameters, Column.RefKind, reference.RefKind);
        Set(parameters, Column.EnclosingSymbolId, reference.EnclosingSymbolId);
        Set(parameters, Column.TargetAssembly, reference.TargetAssembly);
        Set(parameters, Column.TargetInSource, reference.TargetInSource);
        Set(parameters, Column.FilePath, reference.FilePath);
        Set(parameters, Column.Line, reference.Line);
    }

    private static void BindInvocation(DbParameter[] parameters, ReferenceFact reference)
    {
        Set(parameters, Column.ReceiverType, reference.ReceiverType);
        Set(parameters, Column.FirstArgumentTemplate, reference.FirstArgumentTemplate);
        Set(parameters, Column.FirstArgumentType, reference.FirstArgumentType);
        Set(parameters, Column.TypeArguments, reference.TypeArguments);
        Set(parameters, Column.FirstArgumentName, reference.FirstArgumentName);
        Set(parameters, Column.DelegateConsumer, reference.DelegateConsumer);
        Set(parameters, Column.ArgumentTemplates, reference.ArgumentTemplates);
        Set(parameters, Column.ArgumentNames, reference.ArgumentNames);
        Set(parameters, Column.InExpressionTree, reference.InExpressionTree);
    }

    private static void BindStructuralContext(DbParameter[] parameters, ReferenceFact reference)
    {
        Set(parameters, Column.EnclosingLoopKind, reference.EnclosingLoopKind);
        Set(parameters, Column.EnclosingLoopDetail, reference.EnclosingLoopDetail);
        Set(parameters, Column.EnclosingInvocations, reference.EnclosingInvocations);
        Set(parameters, Column.EnclosingCatchTypes, reference.EnclosingCatchTypes);
        Set(parameters, Column.EnclosingScopes, reference.EnclosingScopes);
        Set(parameters, Column.EnclosingGuards, reference.EnclosingGuards);
        Set(parameters, Column.EnclosingLoopElementType, reference.EnclosingLoopElementType);
        Set(parameters, Column.EnclosingLoopBindType, reference.EnclosingLoopBindType);
    }

    private static void BindTypeFlow(DbParameter[] parameters, ReferenceFact reference)
    {
        Set(parameters, Column.DeclaringTypeArgBinding, reference.DeclaringTypeArgBinding);
        Set(parameters, Column.MethodTypeArgBinding, reference.MethodTypeArgBinding);
        Set(parameters, Column.NonVirtual, reference.NonVirtual);
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
        ReferenceFactIndex,
        TargetSymbolId,
        RefKind,
        EnclosingSymbolId,
        TargetAssembly,
        TargetInSource,
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
        DelegateConsumer,
        EnclosingScopes,
        ArgumentTemplates,
        ArgumentNames,
        DeclaringTypeArgBinding,
        MethodTypeArgBinding,
        NonVirtual,
        EnclosingGuards,
        EnclosingLoopElementType,
        EnclosingLoopBindType,
        InExpressionTree,
    }
}
