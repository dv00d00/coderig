namespace Rig.Cli.Web;

internal sealed record IndexedFileDto(string Path, string Name, string Status, IReadOnlyList<string> Projects);

internal sealed record IndexedFilesResponseDto(IReadOnlyList<IndexedFileDto> Files, int Total, int Limit);

// ViaDispatchOnly mirrors FileEffectAggregate: the reach exists only through a dispatch hop with more than one
// candidate implementation (a single-implementation hop is deterministic and reads as an ordinary reach).
// Looped mirrors it too: the effect runs once per iteration of an enclosing loop (the amplification tier).
// Both are ADDITIVE on the wire — an older client that ignores them reads the same badge it always did.
internal sealed record FileEffectAggregateDto(string Family, int NearestDepth, bool ViaDispatchOnly, bool Looped = false);

internal sealed record FileEffectMethodDto(
    string Id,
    string Name,
    string Signature,
    int Line,
    int EndLine,
    IReadOnlyList<FileEffectAggregateDto> Effects
);

internal sealed record FileEffectDeclarationDto(string Id, string Name, string Signature, int Line, int EndLine);

internal sealed record FileEffectCallSiteDto(
    string EnclosingMethodId,
    string TargetMethodId,
    int Line,
    IReadOnlyList<FileEffectAggregateDto> Effects
);

// What a filter REMOVED from this response. Sent whenever a filter was applied, including when it removed
// nothing: a client cannot otherwise distinguish a narrowed view from a quiet file, and that is the one
// mistake this overlay must never invite. Notes carry the token resolutions the server had to widen or ignore
// (`only=llblgen` matched at family grain, so it also matched dapper).
internal sealed record FileEffectsFilterDto(bool Active, int HiddenBadges, int HiddenMethods, int HiddenLines, IReadOnlyList<string> Notes);

internal sealed record FileEffectsResponseDto(
    string File,
    IReadOnlyList<string> Families,
    IReadOnlyList<FileEffectMethodDto> Methods,
    IReadOnlyList<FileEffectCallSiteDto> Sites,
    bool ColumnsAvailable,
    bool WitnessPathsIncluded,
    IReadOnlyList<FileEffectDeclarationDto>? Declarations = null,
    FileEffectsFilterDto? Filter = null
);

// TIERS 1-3 for one file (see FileFindingsQueryService). A SEPARATE payload from the effect badges, because
// it is a separate derivation with a separate cost: the lens renders badges the moment /api/file-effects
// answers and folds these in when they arrive, so a slow findings query never delays the source.
//
// Field names are the finding records' own (Reason is the hazard SUBTYPE, Context the key / iteration kind),
// renamed here only where the record's name would be meaningless on the wire.
internal sealed record FileHazardDto(string Type, string Confidence, string Subtype, string Key, string Enclosing, int Line, string Detail);

internal sealed record FileAmplificationDto(
    string Type,
    string Confidence,
    string Subtype,
    string Key,
    string Enclosing,
    int Line,
    string Iteration,
    string Provider,
    string Operation
);

// Anchor grain: one row per looped CALL SITE with its nearest witness. These are EXACTLY the fields
// CrossMethodAmplificationDataset.AnchorFinding carries — the calibrated displayed grain (93% TP+TP-weak on a
// stratified hand audit) — and nothing more. The richer (anchor x witness) dataset has the iterated source,
// the key token and the witness site too, but it is ~40x larger and collapsing it here would re-implement a
// calibrated decision. Confidence is DERIVED from WitnessDepth (<=1 high, <=4 medium, else low) and sent
// rather than left to the client, so the two cannot disagree about what counts as a lead.
//
// Evidence rides alongside it for the same reason and is likewise derived server-side: it is what the note
// under the finding list is allowed to claim ("direct" = the call is unconditional in its loop and the
// witness was reached with no dispatch inference; "inferred" = a guessed or fanned-out dispatch hop is on the
// path; "candidate" = neither). Guards / DispatchBasis / DispatchDegree are sent WITH it so the client can
// say WHY a row is not direct without re-deriving the tier and drifting from the server's definition.
internal sealed record FileAnchorDto(
    int Line,
    string Caller,
    string IterationKind,
    string WitnessProvider,
    string WitnessOperation,
    string WitnessResource,
    int WitnessDepth,
    string Confidence,
    string Evidence,
    // Rendered display text, "" when the row carries no guard a reader should act on — never the
    // encoded fact, and never null, so the client can test it as a plain string.
    string Guards,
    string? DispatchBasis,
    int DispatchDegree
);

internal sealed record FileFindingsResponseDto(
    string File,
    IReadOnlyList<FileHazardDto> Hazards,
    IReadOnlyList<FileAmplificationDto> Amplifications,
    IReadOnlyList<FileAnchorDto> Anchors,
    // False when the rule set declares no crossMethodAmplification section: the tier is OFF, not empty, and a
    // reader must be able to tell "no anchors here" from "this store never looked".
    bool CrossMethodAvailable
);

internal sealed record CompileProjectDto(string Name, string Reason);

internal sealed record CompileErrorsDto(int Files, int Total, IReadOnlyList<CompileProjectDto> Projects)
{
    internal bool IsClean => Files == 0 && Total == 0 && Projects.Count == 0;
}

internal sealed record RigMetaResponseDto(
    string DerivationVersion,
    string WorkingDirectory,
    string StoreDirectory,
    string StoreId,
    CompileErrorsDto? CompileErrors = null
);

internal sealed record FileSourceResponseDto(
    string File,
    int StartLine,
    int EndLine,
    string Origin,
    string? Commit,
    string? Reason,
    IReadOnlyList<SourceLineDto> Lines,
    bool HasPrevious,
    bool HasMore,
    bool StoreDirty
);
