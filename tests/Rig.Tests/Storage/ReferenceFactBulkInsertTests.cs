using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Storage;

public sealed class ReferenceFactBulkInsertTests
{
    [Test]
    public async Task Bulk_insert_covers_and_round_trips_every_reference_fact_field()
    {
        var reference = FullyPopulatedReference();
        var directory = Directory.CreateTempSubdirectory("rig-refinsert-").FullName;
        var databasePath = Path.Combine(directory, "rig.db");

        try
        {
            var result = new AnalysisResult(
                SolutionPath: Path.Combine(directory, "Test.slnx"),
                SourceFiles: [],
                DiRegistrations: [],
                References: [reference]
            );

            await using (var write = new RigDbContext(databasePath, pooling: false))
            {
                await Writes.SaveAsync(write, result);
            }

            await using var read = new RigDbContext(databasePath, pooling: false);
            var stored = await read.ReferenceFacts.AsNoTracking().SingleAsync();

            var entityProperties = typeof(ReferenceFactEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            ReferenceFactBulkInsert
                .ColumnNames.ToHashSet(StringComparer.Ordinal)
                .SetEquals(entityProperties.Select(property => property.Name))
                .ShouldBeTrue("the bulk insert must cover every reference_facts column exactly once");

            foreach (var sourceProperty in typeof(ReferenceFact).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var storedProperty = typeof(ReferenceFactEntity).GetProperty(sourceProperty.Name);
                storedProperty.ShouldNotBeNull($"reference_facts has no column for ReferenceFact.{sourceProperty.Name}");
                storedProperty
                    .GetValue(stored)
                    .ShouldBe(
                        sourceProperty.GetValue(reference),
                        $"ReferenceFact.{sourceProperty.Name} did not round-trip through the bulk insert"
                    );
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ReferenceFact FullyPopulatedReference() =>
        new(
            TargetSymbolId: "M:Ns.Callee.Do",
            RefKind: RefKinds.MethodGroup,
            EnclosingSymbolId: "M:Ns.Caller.Run",
            TargetAssembly: "Ns.Callee.dll",
            TargetInSource: true,
            FilePath: @"C:\src\Caller.cs",
            Line: 42,
            ReceiverType: "T:Ns.Receiver",
            FirstArgumentTemplate: "https://example/{id}",
            FirstArgumentType: "T:System.String",
            EnclosingLoopKind: "foreach",
            EnclosingLoopDetail: "row in rows",
            EnclosingInvocations: "Task/Tasks.Task/WhenAll",
            EnclosingCatchTypes: "System.Exception",
            TypeArguments: "Ns.Payload",
            FirstArgumentName: "Ns.ProcessDns.Worker",
            DelegateConsumer: "M:Ns.Scheduler.#ctor",
            EnclosingScopes: "lock/Ns.Gate",
            ArgumentTemplates: "[\"a\"]",
            ArgumentNames: "[\"b\"]",
            DeclaringTypeArgBinding: "[\"C:Ns.Account\"]",
            MethodTypeArgBinding: "[\"M:0\"]",
            NonVirtual: true,
            EnclosingGuards: "isEnabled",
            EnclosingLoopElementType: "T:Ns.Row",
            EnclosingLoopBindType: "T:Ns.Rows",
            InExpressionTree: true,
            Column: 17
        );
}
