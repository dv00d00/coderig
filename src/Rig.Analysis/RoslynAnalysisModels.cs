using Microsoft.CodeAnalysis;
using Rig.Domain.Data;

namespace Rig.Analysis;

internal sealed record SourceFileClassification(string Status, string Confidence, string Basis, string Reason, string Evidence);

// Roslyn-free BY CONSTRUCTION (live-background-index slice 2): the loader extracts each project's facts
// while that project's Compilation is alive and drops the per-file SourceModels before returning, so this
// set carries plain fact records only — no SemanticModel, no red syntax root, nothing that pins a
// Compilation. ExtractionStreamingTests pins that structurally. (An older shape retained a SourceModel per
// file for the whole run — ~9 GB of bound-node caches on MedDBase, and a per-generation leak once the
// process goes resident.)
internal sealed record SolutionSourceSet(IReadOnlyList<SourceFileInfo> SourceFiles, IReadOnlyList<ExtractedSource> ExtractedSources);

// One extracted file's facts, in the loader's global FilePath order (OrdinalIgnoreCase — the order the
// *FactIndex surrogate keys are assigned in; see SolutionSourceLoader's sort).
internal sealed record ExtractedSource(string ProjectName, string FilePath, SourceExtractionResult Facts);

internal sealed record SourceExtractionResult(
    IReadOnlyList<DiRegistrationInfo> DiRegistrations,
    IReadOnlyList<SymbolFact> Symbols,
    IReadOnlyList<ReferenceFact> References,
    IReadOnlyList<TypeRelationFact> TypeRelations,
    IReadOnlyList<DispatchFact> Dispatch,
    IReadOnlyList<AllocationFact> Allocations
);

// SHORT-LIVED per-file value: created by the loader while a project's Compilation is alive, handed to the
// per-project extraction callback, and dropped the moment that callback returns. Must never be retained
// past the callback — the SemanticModel here is the strong root of the compilation's bound-node caches,
// and retaining it is exactly the whole-run pin slice 2 removed.
internal sealed record SourceModel(string ProjectName, string FilePath, SyntaxTree Tree, SyntaxNode Root, SemanticModel SemanticModel);
