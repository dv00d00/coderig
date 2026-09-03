using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Analysis.Rules;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Rig.Tests.Fixtures;
using Shouldly;
using RigAnalysisResult = Rig.Domain.Data.AnalysisResult;

namespace Rig.Tests.Analysis;

[NotInParallel]
public sealed class FailedCompilationPersistenceStressTests
{
    [Test]
    public async Task Real_generator_exception_records_generator_run_without_inventing_a_file()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var (_, workspace) = await SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync(
            playground.SolutionPath,
            RuleSetLoader.Load(playground.WorkingDirectory),
            parallelism: 1
        );
        var project = workspace.CurrentSolution.Projects.First(project => project.Language == LanguageNames.CSharp);
        project = project.AddAnalyzerReference(new TestGeneratorReference());
        var compilation = await project.GetCompilationAsync();
        compilation.ShouldNotBeNull();
        var collector = new CompilationHealthCollector();

        await SolutionSourceLoader.RunSourceGeneratorsAsync(project, compilation, progress: null, collector, CancellationToken.None);
        var health = collector.Build();

        health.PartialProjects.ShouldContain(failure =>
            failure.ProjectName == project.Name && failure.Reason == ProjectCompileFailure.GeneratorRun
        );
        health.Files.ShouldBeEmpty();
    }

    [Test]
    public async Task Removed_interface_member_flags_the_caller_not_the_edited_interface_and_round_trips()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        const string interfaceBook = "M:Domain.IBookingService.Book(System.Int32)";
        const string implementationBook = "M:Business.BookingService.Book(System.Int32)";
        const string callerBook = "M:ApiGateway.BookingController.Book(Contracts.PatientDto)";
        var interfacePath = Path.Combine(playground.WorkingDirectory, "Domain", "IBookingService.cs");
        var callerPath = Path.Combine(playground.WorkingDirectory, "ApiGateway", "BookingController.cs");
        var clean = await AnalyzeAsync(playground);
        clean.Symbols.ShouldNotBeNull().ShouldContain(symbol => symbol.SymbolId == interfaceBook);
        clean
            .DispatchFacts.ShouldNotBeNull()
            .ShouldContain(fact => fact.SourceMember == interfaceBook && fact.TargetMember == implementationBook);
        clean
            .References.ShouldNotBeNull()
            .ShouldContain(reference =>
                reference.EnclosingSymbolId == callerBook
                && reference.TargetSymbolId == interfaceBook
                && reference.RefKind == RefKinds.Invocation
            );
        FactPathFinder.Find(FactProjection.GraphData(clean), callerBook, implementationBook).ShouldNotBeNull();

        var text = await File.ReadAllTextAsync(interfacePath);
        await File.WriteAllTextAsync(interfacePath, text.Replace("    string Book(int patientId);\n", "", StringComparison.Ordinal));

        var result = await AnalyzeAsync(playground);
        result.CompilationHealth.ShouldNotBeNull();
        result.CompilationHealth.Files.Select(file => Path.GetFullPath(file.FilePath)).ShouldContain(Path.GetFullPath(callerPath));
        result.CompilationHealth.Files.Select(file => Path.GetFullPath(file.FilePath)).ShouldNotContain(Path.GetFullPath(interfacePath));
        result.Symbols.ShouldNotBeNull().ShouldNotContain(symbol => symbol.SymbolId == interfaceBook);
        result
            .DispatchFacts.ShouldNotBeNull()
            .ShouldNotContain(fact => fact.SourceMember == interfaceBook || fact.TargetMember == interfaceBook);
        result
            .References.ShouldNotBeNull()
            .ShouldNotContain(reference =>
                reference.EnclosingSymbolId == callerBook
                && reference.TargetSymbolId == interfaceBook
                && reference.RefKind == RefKinds.Invocation
            );
        FactPathFinder.Find(FactProjection.GraphData(result), callerBook, implementationBook).ShouldBeNull();

        var dbPath = Path.Combine(playground.RootDirectory, "handoff.db");
        await using (var write = new RigDbContext(dbPath, pooling: false))
        {
            await Writes.SaveAsync(write, result);
        }

        await using var read = new RigDbContext(dbPath, readOnly: true, pooling: false);
        var persisted = await Reads.LoadCompilationHealthAsync(read);
        persisted.Files.Select(file => Path.GetFullPath(file.FilePath)).ShouldContain(Path.GetFullPath(callerPath));
        persisted.Files.Select(file => Path.GetFullPath(file.FilePath)).ShouldNotContain(Path.GetFullPath(interfacePath));
    }

    [Test]
    public async Task Project_with_no_compilation_is_run_level_only()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var projectPath = Path.Combine(playground.WorkingDirectory, "Contracts", "Contracts.csproj");
        await File.WriteAllTextAsync(
            projectPath,
            "<Project><PropertyGroup><TargetFramework>not-a-tfm</TargetFramework></PropertyGroup></Project>"
        );

        var result = await AnalyzeAsync(playground);
        result.CompilationHealth.ShouldNotBeNull();
        result.CompilationHealth.PartialProjects.ShouldContain(project =>
            project.ProjectName == "Contracts" && project.Reason == ProjectCompileFailure.NoCompilation
        );
        result.CompilationHealth.Files.ShouldNotContain(file => file.FilePath.Contains("Contracts", StringComparison.OrdinalIgnoreCase));
        result.SourceFiles.ShouldNotContain(file => file.ProjectName == "Contracts");
        result.Symbols.ShouldNotBeNull().ShouldNotContain(symbol => symbol.DefiningAssembly == "Contracts");
        result.References.ShouldNotBeNull().ShouldNotContain(reference => IsUnderProject(reference.FilePath, "Contracts"));
        result.TypeRelations.ShouldNotBeNull().ShouldNotContain(relation => IsUnderProject(relation.FilePath, "Contracts"));
        result.DispatchFacts.ShouldNotBeNull().ShouldNotContain(dispatch => IsUnderProject(dispatch.FilePath, "Contracts"));
        result.Symbols.ShouldContain(symbol => symbol.SymbolId == "M:Foundation.Db.Query(System.String)");

        var dbPath = Path.Combine(playground.RootDirectory, "no-compilation.db");
        await using (var write = new RigDbContext(dbPath, pooling: false))
        {
            await Writes.SaveAsync(write, result);
        }

        await using var read = new RigDbContext(dbPath, readOnly: true, pooling: false);
        var persisted = await Reads.LoadCompilationHealthAsync(read);
        persisted.PartialProjects.ShouldContain(project =>
            project.ProjectName == "Contracts" && project.Reason == ProjectCompileFailure.NoCompilation
        );
        (await read.SymbolFacts.AsNoTracking().AnyAsync(symbol => symbol.DefiningAssembly == "Contracts")).ShouldBeFalse();
        (await read.ReferenceFacts.AsNoTracking().ToArrayAsync()).ShouldNotContain(reference =>
            IsUnderProject(reference.FilePath, "Contracts")
        );
        (await read.TypeRelationFacts.AsNoTracking().ToArrayAsync()).ShouldNotContain(relation =>
            IsUnderProject(relation.FilePath, "Contracts")
        );
        (await read.DispatchFacts.AsNoTracking().ToArrayAsync()).ShouldNotContain(dispatch =>
            IsUnderProject(dispatch.FilePath, "Contracts")
        );
        (
            await read.SymbolFacts.AsNoTracking().AnyAsync(symbol => symbol.SymbolId == "M:Foundation.Db.Query(System.String)")
        ).ShouldBeTrue();
    }

    [Test]
    public async Task Ambiguous_duplicate_type_records_real_diagnostic_files_and_unresolved_reference_provenance()
    {
        using var playground = await DeepChainPlayground.CreateAsync();
        var duplicatePath = Path.Combine(playground.WorkingDirectory, "Foundation", "DuplicatePatientDto.cs");
        await File.WriteAllTextAsync(
            duplicatePath,
            "namespace Contracts; public sealed class PatientDto { public int Id { get; set; } public string Name { get; set; } = \"\"; }"
        );

        var result = await AnalyzeAsync(playground);
        var health = result.CompilationHealth.ShouldNotBeNull();
        health.Files.ShouldNotBeEmpty();
        health.Files.SelectMany(file => file.ErrorCodes.Split(',')).ShouldContain("CS0433");

        var diagnosticPaths = health.Files.Select(file => CompilationFilePath.Key(file.FilePath)).ToHashSet(CompilationFilePath.Comparer);
        var patientReferences = (result.References ?? [])
            .Where(reference => reference.TargetSymbolId.Contains("PatientDto", StringComparison.Ordinal))
            .Where(reference => reference.RefKind == RefKinds.TypeUse)
            .Where(reference => CompilationFilePath.Contains(diagnosticPaths, reference.FilePath))
            .ToArray();
        patientReferences.ShouldNotBeEmpty();
        patientReferences.ShouldAllBe(reference => reference.TargetInSource);
        patientReferences.ShouldAllBe(reference => reference.TargetAssembly == "Contracts");
    }

    private static Task<RigAnalysisResult> AnalyzeAsync(DeepChainPlayground playground) =>
        SolutionAnalyzer.AnalyzeAsync(playground.SolutionPath, RuleSetLoader.Load(playground.WorkingDirectory), parallelism: 1);

    private static bool IsUnderProject(string path, string project) =>
        CompilationFilePath.Key(path).Contains($"/{project}/", StringComparison.OrdinalIgnoreCase);

#pragma warning disable RS1036, RS1038, RS1041, RS1042
    public sealed class ThrowingGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context) { }

        public void Execute(GeneratorExecutionContext context) => throw new InvalidOperationException("generator boom");
    }
#pragma warning restore RS1036, RS1038, RS1041, RS1042

    private sealed class TestGeneratorReference : AnalyzerReference
    {
        public override string? FullPath => null;

        public override string Display => nameof(ThrowingGenerator);

        public override object Id { get; } = new();

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];

        public override ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages() => [new ThrowingGenerator()];

        public override ImmutableArray<ISourceGenerator> GetGenerators(string language) =>
            language == LanguageNames.CSharp ? [new ThrowingGenerator()] : [];
    }
}
