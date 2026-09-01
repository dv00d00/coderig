namespace Rig.Cli.Web;

internal sealed record IndexedFileDto(string Path, string Name, string Status, IReadOnlyList<string> Projects);

internal sealed record IndexedFilesResponseDto(IReadOnlyList<IndexedFileDto> Files, int Total, int Limit);

internal sealed record FileEffectAggregateDto(string Family, int NearestDepth);

internal sealed record FileEffectMethodDto(
    string Id,
    string Name,
    string Signature,
    int Line,
    int EndLine,
    IReadOnlyList<FileEffectAggregateDto> Effects
);

internal sealed record FileEffectCallSiteDto(
    string EnclosingMethodId,
    string TargetMethodId,
    int Line,
    IReadOnlyList<FileEffectAggregateDto> Effects
);

internal sealed record FileEffectsResponseDto(
    string File,
    IReadOnlyList<string> Families,
    IReadOnlyList<FileEffectMethodDto> Methods,
    IReadOnlyList<FileEffectCallSiteDto> Sites,
    bool ColumnsAvailable,
    bool WitnessPathsIncluded
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
