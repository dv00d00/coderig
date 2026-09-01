namespace Rig.Cli.Web;

internal sealed record IndexedFileDto(string Path, string Name, string Status, IReadOnlyList<string> Projects);

internal sealed record IndexedFilesResponseDto(IReadOnlyList<IndexedFileDto> Files, int Total, int Limit);

// ViaDispatchOnly mirrors FileEffectAggregate: the reach exists only through virtual/interface dispatch.
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

internal sealed record RigMetaResponseDto(string DerivationVersion, string WorkingDirectory, string StoreDirectory, string StoreId);

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
