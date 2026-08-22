namespace Rig.Domain.Data;

// Roslyn-free fact surface shared by cold AnalysisResult values and immutable resident snapshots.
// Enumerations deliberately preserve fact-row multiplicity: provenance-bearing relation/dispatch rows
// are deduplicated only when projected to semantic graph edges.
public interface IFactSnapshotView
{
    string SolutionPath { get; }

    IEnumerable<SourceFileInfo> EnumerateSourceFiles();
    IEnumerable<DiRegistrationInfo> EnumerateDiRegistrations();
    IEnumerable<SymbolFact> EnumerateSymbols();
    IEnumerable<ReferenceFact> EnumerateReferences();
    IEnumerable<TypeRelationFact> EnumerateTypeRelations();
    IEnumerable<DispatchFact> EnumerateDispatchFacts();
    IEnumerable<AllocationFact> EnumerateAllocationFacts();
    CompilationHealth? GetCompilationHealth();
}

public static class FactSnapshotViewMaterializer
{
    // Explicit compatibility/oracle boundary for consumers that still require one flattened value.
    public static AnalysisResult Materialize(
        this IFactSnapshotView view,
        string? projectIdentity = null,
        string? sourceProjectPath = null
    ) =>
        new(
            view.SolutionPath,
            view.EnumerateSourceFiles().ToArray(),
            view.EnumerateDiRegistrations().ToArray(),
            projectIdentity,
            sourceProjectPath,
            view.EnumerateSymbols().ToArray(),
            view.EnumerateReferences().ToArray(),
            view.EnumerateTypeRelations().ToArray(),
            view.EnumerateDispatchFacts().ToArray(),
            view.EnumerateAllocationFacts().ToArray(),
            view.GetCompilationHealth()
        );
}
