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
