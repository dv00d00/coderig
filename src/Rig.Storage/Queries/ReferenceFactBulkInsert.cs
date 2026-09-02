using System.Data.Common;
using Rig.Domain.Data;

namespace Rig.Storage.Queries;

// The reference row is deliberately written through raw ADO rather than EF's change tracker: indexing can
// emit millions of these rows. Keep that performance choice local without making Writes.SaveFactsBatchedAsync
// understand a 30-column storage layout. Column names, SQL order, and parameter ordinals all come from this
// one enum; the bind methods use named columns and stay grouped by the corresponding ReferenceFact concerns.
internal static class ReferenceFactBulkInsert
{
    internal static IReadOnlyList<string> ColumnNames { get; } = Enum.GetNames<Field>();

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
        Set(parameters, Field.RunId, runId);
        Set(parameters, Field.ReferenceFactIndex, index);
        Set(parameters, Field.TargetSymbolId, reference.TargetSymbolId);
        Set(parameters, Field.RefKind, reference.RefKind);
        Set(parameters, Field.EnclosingSymbolId, reference.EnclosingSymbolId);
        Set(parameters, Field.TargetAssembly, reference.TargetAssembly);
        Set(parameters, Field.TargetInSource, reference.TargetInSource);
        Set(parameters, Field.FilePath, reference.FilePath);
        Set(parameters, Field.Line, reference.Line);
        Set(parameters, Field.Column, reference.Column);
    }

    private static void BindInvocation(DbParameter[] parameters, ReferenceFact reference)
    {
        Set(parameters, Field.ReceiverType, reference.ReceiverType);
        Set(parameters, Field.FirstArgumentTemplate, reference.FirstArgumentTemplate);
        Set(parameters, Field.FirstArgumentType, reference.FirstArgumentType);
        Set(parameters, Field.TypeArguments, reference.TypeArguments);
        Set(parameters, Field.FirstArgumentName, reference.FirstArgumentName);
        Set(parameters, Field.DelegateConsumer, reference.DelegateConsumer);
        Set(parameters, Field.ArgumentTemplates, reference.ArgumentTemplates);
        Set(parameters, Field.ArgumentNames, reference.ArgumentNames);
        Set(parameters, Field.InExpressionTree, reference.InExpressionTree);
    }

    private static void BindStructuralContext(DbParameter[] parameters, ReferenceFact reference)
    {
        Set(parameters, Field.EnclosingLoopKind, reference.EnclosingLoopKind);
        Set(parameters, Field.EnclosingLoopDetail, reference.EnclosingLoopDetail);
        Set(parameters, Field.EnclosingInvocations, reference.EnclosingInvocations);
        Set(parameters, Field.EnclosingCatchTypes, reference.EnclosingCatchTypes);
        Set(parameters, Field.EnclosingScopes, reference.EnclosingScopes);
        Set(parameters, Field.EnclosingGuards, reference.EnclosingGuards);
        Set(parameters, Field.EnclosingLoopElementType, reference.EnclosingLoopElementType);
        Set(parameters, Field.EnclosingLoopBindType, reference.EnclosingLoopBindType);
    }

    private static void BindTypeFlow(DbParameter[] parameters, ReferenceFact reference)
    {
        Set(parameters, Field.DeclaringTypeArgBinding, reference.DeclaringTypeArgBinding);
        Set(parameters, Field.MethodTypeArgBinding, reference.MethodTypeArgBinding);
        Set(parameters, Field.NonVirtual, reference.NonVirtual);
    }

    private static void Set(DbParameter[] parameters, Field field, object? value) =>
        parameters[(int)field].Value = value switch
        {
            null => DBNull.Value,
            bool boolean => boolean ? 1 : 0,
            _ => value,
        };

    // Named Field rather than Column (as its sibling bulk inserts are) because one of the columns it names
    // IS `Column`, and an enum may not declare a member with its own type's name.
    private enum Field
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
        Column,
    }
}
