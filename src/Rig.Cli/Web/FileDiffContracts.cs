namespace Rig.Cli.Web;

// One immutable side of a file review. Effects keep their store-native line coordinates; the patch is the
// only thing that maps those coordinates into visible old/new rows.
internal sealed record FileDiffRevisionDto(string Store, string Commit, string Content, FileEffectsResponseDto Effects);

// Renderer-neutral contract: a Git patch plus semantic annotations for both revisions. A browser renderer,
// Rider adapter, or future GitHub/GitLab transport can consume the same old/new line-keyed facts.
internal sealed record FileDiffResponseDto(
    string File,
    string RelativePath,
    string Patch,
    int ContextLines,
    FileDiffRevisionDto Base,
    FileDiffRevisionDto Head
);

// One Git-changed file matched back to the immutable C# source-file inventories on both sides. The current
// renderer accepts one stable absolute path, so rename/add/delete rows remain useful navigation context but
// explicitly disclose why they cannot be opened yet.
internal sealed record ReviewFileDto(
    string Status,
    string Path,
    string? OldPath,
    string? NewPath,
    string? OldFile,
    string? NewFile,
    bool Reviewable,
    string? Reason
);

internal sealed record ReviewFilesResponseDto(
    string BaseStore,
    string HeadStore,
    string BaseCommit,
    string HeadCommit,
    IReadOnlyList<ReviewFileDto> Files
);
