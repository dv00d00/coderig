using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Rig.Analysis;
using Rig.Analysis.Extraction;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Analysis;

public sealed class FactEmitterProvenanceTests
{
    [Test]
    public async Task Relation_and_dispatch_emitter_paths_survive_extraction_and_storage()
    {
        var contractsPath = Path.GetFullPath("EmitterContracts.cs");
        var implementationPath = Path.GetFullPath("EmitterImplementation.cs");
        var contracts = CSharpSyntaxTree.ParseText(
            """
            namespace App;

            public interface IFoo { void M(); }
            public class Base { public virtual void M() { } }
            """,
            path: contractsPath
        );
        var implementation = CSharpSyntaxTree.ParseText(
            """
            namespace App;

            public sealed class Impl : Base, IFoo
            {
                public override void M() { }
            }
            """
        );
        implementation.FilePath.ShouldBeEmpty("generated syntax trees can rely on SourceModel's synthetic fallback path");
        var compilation = CSharpCompilation.Create(
            "EmitterProvenance",
            [contracts, implementation],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var extracted = FactExtractor.Extract(
            new SourceModel(
                "EmitterProvenance",
                implementationPath,
                implementation,
                implementation.GetRoot(),
                compilation.GetSemanticModel(implementation)
            ),
            new SymbolStringCache()
        );

        extracted.TypeRelations.Count.ShouldBeGreaterThan(0);
        extracted.Dispatch.Count.ShouldBeGreaterThan(0);
        extracted.TypeRelations.All(f => f.FilePath == implementationPath).ShouldBeTrue();
        extracted.Dispatch.All(f => f.FilePath == implementationPath).ShouldBeTrue();
        extracted.TypeRelations.ShouldContain(f =>
            f.TypeSymbolId == "T:App.Impl" && f.RelatedSymbolId == "T:App.IFoo" && f.RelationKind == RelationKinds.Interface
        );
        extracted.Dispatch.ShouldContain(f =>
            f.SourceMember == "M:App.IFoo.M"
            && f.TargetMember == "M:App.Impl.M"
            && f.Kind == DispatchKinds.Impl
            && f.FilePath == implementationPath
        );
        extracted.Dispatch.ShouldContain(f =>
            f.SourceMember == "M:App.Base.M"
            && f.TargetMember == "M:App.Impl.M"
            && f.Kind == DispatchKinds.Override
            && f.FilePath == implementationPath
        );

        var result = new AnalysisResult(
            SolutionPath: Path.GetFullPath("EmitterProvenance.sln"),
            SourceFiles: [],
            DiRegistrations: [],
            Symbols: extracted.Symbols,
            References: extracted.References,
            TypeRelations: extracted.TypeRelations,
            DispatchFacts: extracted.Dispatch
        );
        var directory = Directory.CreateTempSubdirectory("rig-emitter-provenance-").FullName;
        var databasePath = Path.Combine(directory, "rig.db");
        try
        {
            await using (var write = new RigDbContext(databasePath, pooling: false))
            {
                await Writes.SaveAsync(write, result);
            }

            await using var read = new RigDbContext(databasePath, pooling: false, readOnly: true);
            var storedRelations = await read.TypeRelationFacts.AsNoTracking().ToListAsync();
            var storedDispatch = await read.DispatchFacts.AsNoTracking().ToListAsync();

            storedRelations.Count.ShouldBe(extracted.TypeRelations.Count);
            storedDispatch.Count.ShouldBe(extracted.Dispatch.Count);
            storedRelations.All(f => f.FilePath == implementationPath).ShouldBeTrue();
            storedDispatch.All(f => f.FilePath == implementationPath).ShouldBeTrue();
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup: a failed assertion must remain the test's useful failure.
            }
        }
    }
}
