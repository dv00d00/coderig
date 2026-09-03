using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Tests.Fixtures;
using Shouldly;

namespace Rig.Tests.Storage;

public sealed class FactInvocationFixtureProjectionTests
{
    [Test]
    public void Fixture_invocations_are_exactly_the_production_projection_with_all_context_fields()
    {
        var reference = new ReferenceFact(
            TargetSymbolId: "M:Ns.Callee.Do",
            RefKind: RefKinds.Invocation,
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
        var result = new AnalysisResult(SolutionPath: @"C:\src\Ns.sln", SourceFiles: [], DiRegistrations: [], References: [reference]);

        var fixtureProjection = FactProjection.Invocations(result).ShouldHaveSingleItem();
        var productionProjection = FactInvocationProjection.Project(reference);

        fixtureProjection.ShouldBe(productionProjection);
        fixtureProjection.Nesting.Guards.ShouldBe("isEnabled");
        fixtureProjection.Loop.ElementType.ShouldBe("T:Ns.Row");
        fixtureProjection.Loop.BindType.ShouldBe("T:Ns.Rows");
        fixtureProjection.InExpressionTree.ShouldBeTrue();
    }
}
