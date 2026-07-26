namespace Rig.Cli.Web;

// JSON contract for /api/source — the web equivalent of `rig show`: the declaration SOURCE behind one
// symbol id, resolved by SourceRenderer against the STORE'S OWN revision. Same data the CLI renders
// (working tree / git blob / refusal), reshaped for the SPA: the gutter is data (`Number` per line) rather
// than pre-padded text, so the client can right-align it itself.

// One source line, exactly as SourceRenderer.SourceLine — `Number` is the file line number (not an index).
internal sealed record SourceLineDto(int Number, string Text);

internal sealed record SourceResponseDto(
    // Echoed back so a response can be matched to its request (the client keys panels by symbol id).
    string SymbolId,
    // The STORED location — absolute path + declaration range, as indexed. Always present even when the
    // text could not be resolved, so the location is never lost (mirrors the CLI, which prints file:line
    // above the refusal).
    string File,
    int Line,
    int EndLine,
    // "worktree" | "git" | "unavailable" — the same three words `rig show --format tsv` emits.
    //   worktree    — the file on disk, PROVEN to be the indexed revision; no disclosure needed.
    //   git         — the indexed blob out of git; NOT the reader's working tree, so `Commit` must be shown.
    //   unavailable — no attributable text; `Reason` says why and there are no `Lines`.
    string Origin,
    // Short (12-char) sha of the revision the text came from — set only for Origin == "git", matching the
    // `(from git <shortsha>)` marker the CLI renders and the sha `rig runs` shows.
    string? Commit,
    // Lines of the requested range dropped by the renderer's absurd-output cap (0 when nothing was cut).
    int TruncatedCount,
    // One-line explanation, set only for Origin == "unavailable".
    string? Reason,
    // The resolved slice in file order; empty for a refusal.
    IReadOnlyList<SourceLineDto> Lines,
    // True when the store was indexed from a DIRTY tree, which makes even the exact commit's blob possibly
    // different from what was indexed. Carried alongside `Commit` because it is part of the SAME disclosure
    // the CLI makes in one string (SourceRenderer.OriginMarker); the client re-assembles that marker.
    bool StoreDirty
);
