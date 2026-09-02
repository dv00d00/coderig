namespace Rig.Cli.Web;

// One immutable side of a file review. Git presence and indexed semantic availability are separate axes:
// an added/deleted side is not-present, while a text or otherwise unindexed side is present but not-indexed.
internal sealed record FileDiffRevisionDto(
    string Store,
    string Commit,
    string SemanticState,
    string? Path,
    string? File,
    string Content,
    FileEffectsResponseDto? Effects
);

// Renderer-neutral contract: a Git patch plus semantic annotations for both revisions. A browser renderer,
// Rider adapter, or future GitHub/GitLab transport can consume the same old/new line-keyed facts.
internal sealed record FileDiffResponseDto(
    string File,
    string RelativePath,
    string Status,
    string? OldPath,
    string? NewPath,
    string Language,
    string Patch,
    int ContextLines,
    FileDiffRevisionDto Base,
    FileDiffRevisionDto Head
);

// One Git-changed file matched back to the immutable source-file inventories on both sides. Every Git row
// is reviewable as text; SemanticReady is the narrower both-sides semantic capability. Additions/Deletions
// are null when Git reports no line counts at all (a binary file), which is a different fact from a text
// file whose changed-line count really is zero — a renderer must not show "0" for an unknown count.
internal sealed record ReviewFileDto(
    string Status,
    string Path,
    string? OldPath,
    string? NewPath,
    string? OldFile,
    string? NewFile,
    bool Reviewable,
    bool SemanticReady,
    string? Reason,
    int? Additions,
    int? Deletions
);

internal sealed record ReviewFilesResponseDto(
    string BaseStore,
    string HeadStore,
    string BaseCommit,
    string HeadCommit,
    IReadOnlyList<ReviewFileDto> Files
);

// Exact, lazily requested Git source. Null content is unavailable; an empty string is a real empty blob.
internal sealed record ReviewSourceResponseDto(
    string File,
    string Side,
    string Store,
    string Commit,
    string? Path,
    string Language,
    string State,
    string? Content,
    long? ByteLength,
    string? Reason
);
