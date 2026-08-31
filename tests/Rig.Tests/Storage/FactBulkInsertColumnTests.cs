using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Storage;

// The four fact tables written by SymbolFactBulkInsert / AllocationFactBulkInsert /
// TypeRelationFactBulkInsert / DispatchFactBulkInsert derive their INSERT text and their parameter array from
// one Column enum each. These tests pin the two things that enum buys: the generated statement's column order
// and placeholder order agree with it, and every column round-trips to the field it names. A misalignment
// between the SQL, the parameters and the binder writes plausible-looking values into the wrong columns —
// SQLite reports nothing, so only a round-trip catches it.
public sealed class FactBulkInsertColumnTests
{
    [Test]
    public async Task Generated_statements_agree_with_the_column_declaration()
    {
        AssertStatementMatchesColumns(
            "symbol_facts",
            SymbolFactBulkInsert.Sql,
            SymbolFactBulkInsert.ColumnNames,
            SymbolFactBulkInsert.ParameterNames
        );
        AssertStatementMatchesColumns(
            "allocation_facts",
            AllocationFactBulkInsert.Sql,
            AllocationFactBulkInsert.ColumnNames,
            AllocationFactBulkInsert.ParameterNames
        );
        AssertStatementMatchesColumns(
            "type_relation_facts",
            TypeRelationFactBulkInsert.Sql,
            TypeRelationFactBulkInsert.ColumnNames,
            TypeRelationFactBulkInsert.ParameterNames
        );
        AssertStatementMatchesColumns(
            "dispatch_facts",
            DispatchFactBulkInsert.Sql,
            DispatchFactBulkInsert.ColumnNames,
            DispatchFactBulkInsert.ParameterNames
        );

        await Task.CompletedTask;
    }

    [Test]
    public async Task Column_declarations_cover_every_stored_column_exactly_once()
    {
        AssertCoversEntity<SymbolFactEntity>(SymbolFactBulkInsert.ColumnNames, "symbol_facts");
        AssertCoversEntity<AllocationFactEntity>(AllocationFactBulkInsert.ColumnNames, "allocation_facts");
        AssertCoversEntity<TypeRelationFactEntity>(TypeRelationFactBulkInsert.ColumnNames, "type_relation_facts");
        AssertCoversEntity<DispatchFactEntity>(DispatchFactBulkInsert.ColumnNames, "dispatch_facts");

        await Task.CompletedTask;
    }

    [Test]
    public async Task Bulk_insert_round_trips_every_field_of_every_fact_table()
    {
        var symbol = FullyPopulatedSymbol();
        var allocation = FullyPopulatedAllocation();
        var relation = new TypeRelationFact(
            TypeSymbolId: "T:Ns.Derived",
            RelatedSymbolId: "T:Ns.Base",
            RelationKind: "base",
            FilePath: @"C:\src\Derived.cs"
        );
        var dispatch = new DispatchFact(
            SourceMember: "M:Ns.IBase.Do",
            TargetMember: "M:Ns.Impl.Do",
            Kind: "impl",
            FilePath: @"C:\src\Impl.cs"
        );

        var directory = Directory.CreateTempSubdirectory("rig-factinsert-").FullName;
        var databasePath = Path.Combine(directory, "rig.db");

        try
        {
            var result = new AnalysisResult(
                SolutionPath: Path.Combine(directory, "Test.slnx"),
                SourceFiles: [],
                DiRegistrations: [],
                Symbols: [symbol],
                TypeRelations: [relation],
                DispatchFacts: [dispatch],
                AllocationFacts: [allocation]
            );

            await using (var write = new RigDbContext(databasePath, pooling: false))
            {
                await Writes.SaveAsync(write, result);
            }

            await using var read = new RigDbContext(databasePath, pooling: false);
            var storedSymbol = await read.SymbolFacts.AsNoTracking().SingleAsync();
            var storedAllocation = await read.AllocationFacts.AsNoTracking().SingleAsync();
            var storedRelation = await read.TypeRelationFacts.AsNoTracking().SingleAsync();
            var storedDispatch = await read.DispatchFacts.AsNoTracking().SingleAsync();

            AssertRoundTrip(symbol, storedSymbol);
            AssertRoundTrip(allocation, storedAllocation);
            AssertRoundTrip(relation, storedRelation);
            AssertRoundTrip(dispatch, storedDispatch);

            // The *Index column carries the row's position in the batch, not a value from the fact — a binder
            // that mis-set it would still round-trip every other column.
            storedSymbol.SymbolFactIndex.ShouldBe(0);
            storedAllocation.AllocationFactIndex.ShouldBe(0);
            storedRelation.TypeRelationFactIndex.ShouldBe(0);
            storedDispatch.DispatchFactIndex.ShouldBe(0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertStatementMatchesColumns(string table, string sql, IReadOnlyList<string> columnNames, string[] parameterNames)
    {
        parameterNames.Length.ShouldBe(columnNames.Count, $"{table}: one parameter per column");
        parameterNames.Distinct(StringComparer.Ordinal).Count().ShouldBe(parameterNames.Length, $"{table}: parameter names must be unique");

        var prefix = $"INSERT INTO {table} (";
        sql.ShouldStartWith(prefix);
        var columnList = sql[prefix.Length..sql.IndexOf(')', prefix.Length)];
        columnList.Split(", ").ShouldBe(columnNames, $"{table}: the statement column order must be the declared order");

        const string valuesPrefix = "VALUES (";
        var valuesStart = sql.IndexOf(valuesPrefix, StringComparison.Ordinal) + valuesPrefix.Length;
        var valuesList = sql[valuesStart..sql.IndexOf(')', valuesStart)];
        valuesList.Split(',').ShouldBe(parameterNames, $"{table}: the VALUES placeholders must be in the declared order");
    }

    private static void AssertCoversEntity<TEntity>(IReadOnlyList<string> columnNames, string table)
    {
        var entityProperties = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name);
        columnNames.Count.ShouldBe(columnNames.Distinct(StringComparer.Ordinal).Count(), $"{table}: a column is declared twice");
        columnNames
            .ToHashSet(StringComparer.Ordinal)
            .SetEquals(entityProperties)
            .ShouldBeTrue($"the bulk insert must cover every {table} column exactly once");
    }

    private static void AssertRoundTrip<TFact, TEntity>(TFact fact, TEntity stored)
    {
        foreach (var sourceProperty in typeof(TFact).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var storedProperty = typeof(TEntity).GetProperty(sourceProperty.Name);
            storedProperty.ShouldNotBeNull($"{typeof(TEntity).Name} has no column for {typeof(TFact).Name}.{sourceProperty.Name}");
            storedProperty
                .GetValue(stored)
                .ShouldBe(
                    sourceProperty.GetValue(fact),
                    $"{typeof(TFact).Name}.{sourceProperty.Name} did not round-trip through the bulk insert"
                );
        }
    }

    private static SymbolFact FullyPopulatedSymbol() =>
        new(
            SymbolId: "M:Ns.Owner.Run",
            Kind: "method",
            Name: "Run",
            Namespace: "Ns",
            ContainingSymbolId: "T:Ns.Owner",
            Modifiers: "public async",
            TypeKind: "class",
            Signature: "Task Run(int count)",
            FilePath: @"C:\src\Owner.cs",
            Line: 12,
            EndLine: 34,
            DefiningAssembly: "Ns.Owner.dll",
            IsOverride: true,
            BodyHash: "b0dyha5h",
            SurfaceHash: "5urfaceha5h",
            IsIterator: true
        );

    private static AllocationFact FullyPopulatedAllocation() =>
        new(
            Operation: "object",
            ResourceType: "T:Ns.Payload",
            EnclosingSymbolId: "M:Ns.Owner.Run",
            FilePath: @"C:\src\Owner.cs",
            Line: 21,
            EnclosingLoopKind: "foreach",
            EnclosingLoopDetail: "row in rows",
            EnclosingGuards: "isEnabled",
            Mechanism: "new",
            Cardinality: "per_element",
            ShallowSizeBytes: 4096,
            SizeConfidence: "high",
            SizeBasis: "layout"
        );
}
