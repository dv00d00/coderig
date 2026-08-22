namespace Rig.Domain.Data;

public sealed record AnalysisResult(
    string SolutionPath,
    IReadOnlyList<SourceFileInfo> SourceFiles,
    IReadOnlyList<DiRegistrationInfo> DiRegistrations,
    string? ProjectIdentity = null,
    string? SourceProjectPath = null,
    IReadOnlyList<SymbolFact>? Symbols = null,
    IReadOnlyList<ReferenceFact>? References = null,
    IReadOnlyList<TypeRelationFact>? TypeRelations = null,
    IReadOnlyList<DispatchFact>? DispatchFacts = null,
    IReadOnlyList<AllocationFact>? AllocationFacts = null,
    // Compile-health provenance for the tree these facts were extracted from (null = not collected,
    // e.g. a hand-built AnalysisResult in a test). Travels WITH the facts so every surface that serves
    // them can disclose that they may be missing or wrong; the resident overlay merges it per file
    // exactly as it merges the facts themselves (see ResidentIndex.MergeFacts).
    CompilationHealth? CompilationHealth = null,
    // Per-project, per-emitter surface shards captured before the Roslyn compilations are released.
    // Dormant substrate for the live cascade gate; ordinary query projections do not consume it.
    IReadOnlyList<ProjectSurfaceSnapshot>? ProjectSurfaces = null
) : IFactSnapshotView
{
    public IEnumerable<SourceFileInfo> EnumerateSourceFiles() => SourceFiles;

    public IEnumerable<DiRegistrationInfo> EnumerateDiRegistrations() => DiRegistrations;

    public IEnumerable<SymbolFact> EnumerateSymbols() => Symbols ?? [];

    public IEnumerable<ReferenceFact> EnumerateReferences() => References ?? [];

    public IEnumerable<TypeRelationFact> EnumerateTypeRelations() => TypeRelations ?? [];

    public IEnumerable<DispatchFact> EnumerateDispatchFacts() => DispatchFacts ?? [];

    public IEnumerable<AllocationFact> EnumerateAllocationFacts() => AllocationFacts ?? [];

    public CompilationHealth? GetCompilationHealth() => CompilationHealth;
}
