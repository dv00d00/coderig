# Web file-effect lens — product slice

**Status:** in progress · **Started:** 2026-09-01 · **Family:** web explorer / effect attribution

## Goal

Turn the validated Rider file read model into a fast browser workflow before adding more Rider frontend
machinery: choose an indexed solution file, render its C# source, and annotate methods/call sites with the
effect families they reach.

## Contract

- File inventory comes from `source_files`; an API-supplied path is accepted only after an exact membership
  check against an indexed row. The browser never gains an arbitrary-file-read endpoint.
- Semantic data and source text are separate responses. The semantic read model is immutable and cacheable by
  store + rules; source is resolved on every request through `SourceRenderer` so working-tree/git provenance
  remains honest.
- Method summaries and call-site marks carry `family` plus `nearestDepth`. Depth 0 is direct; depths 1 and 2
  receive progressively indirect visuals; 3+ is deliberately collapsed rather than pretending every remote
  descendant is an immediate call-site effect.
- A method or targeted call-site can pivot into the existing tree by DocID. External call sites have no target
  DocID and disclose that limitation instead of offering a dead link.

## Acceptance

- `GET /api/files` returns indexed physical files and can filter by basename/path.
- `GET /api/file-effects?file=<inventory path>` returns file-scoped methods and call sites from the existing
  `FileEffectReadModelIndex`; it does not materialize every solution symbol just to select one file.
- `GET /api/file-source?file=<inventory path>` refuses paths absent from `source_files` and preserves
  `SourceRenderer` provenance.
- The SPA has a shareable File view with C# highlighting, depth-specific glyphs, useful hover text, and pivots
  into Tree.

## Known limits

- Extraction stores source lines but not columns, so two calls on one line remain ambiguous.
- The first semantic request can still pay a whole-graph warm-up. The per-file projection is process-cached;
  cold-load timing and a more compact persistent artifact are follow-on calibration, not hidden.
- Concrete witness paths are not part of this response. A link opens the existing tree at the method/callee;
  witness-path interaction remains a later slice.

## Semantic review proof

The browser now has a renderer-neutral one-file diff contract and a small TypeScript/React island:

- `GET /api/file-diff?base=<store>&head=<store>&file=<indexed path>` returns the exact Git patch, exact
  base/head source blobs, and the existing file-effect projection for each immutable store.
- Review joins deleted rows to base annotations and inserted/head rows to head annotations. Effect glyphs
  disclose family/depth on hover, expand into a line widget, and pivot into the existing Tree by DocID.
- The island uses `react-diff-view` for unified/split Git hunks and modern `refractor` for C# tokenization;
  the rest of the explorer remains the existing plain-ES-module application.
- Stores indexed from a dirty tree fail closed: Git cannot reproduce the source text that owns those frozen
  semantic line coordinates.

The proof deliberately requires the same indexed physical path on both sides. Added/deleted/renamed-file
mapping, multi-file review navigation, hunk expansion, and provider adapters for remote GitHub/GitLab patches
remain product follow-ons. The renderer survey and decision record live in
[`docs/spikes/browser-diff-renderer-library-survey.md`](../../spikes/browser-diff-renderer-library-survey.md).
