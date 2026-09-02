# Web file-effect lens — product slice

**Status:** done 2026-09-02 — the file lens, semantic one-file review, changed-file navigation, and desktop
review work queue are shipped. Independent follow-ons are linked below. · **Started:** 2026-09-01 ·
**Family:** web explorer / effect attribution

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

- Extraction stores source lines but not columns, so two calls on one line remain ambiguous; this is tracked in
  [call-site facts have no column](../todo/call-site-facts-no-column-same-line-calls-collapse.md).
- The first resident semantic request can still pay a whole-graph warm-up. The generation-owned projection is
  process-cached, so this is host-startup cost rather than a per-file N+1.
- Concrete witness paths deliberately stay out of the ready model; the on-demand contract is tracked in
  [file-lens-lazy-witness-path](../todo/file-lens-lazy-witness-path.md).

## Semantic review proof

The browser now has a renderer-neutral one-file diff contract and a small TypeScript/React island:

- `GET /api/file-diff?base=<store>&head=<store>&file=<indexed path>` returns exact Git hunks plus the existing
  file-effect projection for each immutable store. It deliberately does not ship or tokenize both complete
  source blobs for a small change in a large file.
- Review joins deleted rows to base annotations and inserted/head rows to head annotations. Effect glyphs
  use the same Windows lens grammar (`●` here, `○` below, `?` dispatch-only, `⟳` looped), expand into a line
  widget, and pivot into the existing Tree by DocID.
- The exact patch/effect response renders first. The existing `/api/file-findings` requests for base and head
  then enrich visible rows with tier-1 hazards, tier-2 amplification, and tier-3 cross-method anchors. A slow
  or failed findings derivation degrades explicitly without withholding the diff.
- The island uses `react-diff-view` for unified/split Git hunks and modern `refractor` for C# tokenization;
  the rest of the explorer remains the existing plain-ES-module application.
- Stores indexed from a dirty tree fail closed: Git cannot reproduce the source text that owns those frozen
  semantic line coordinates.

`GET /api/review-files` now inventories every Git-changed path, and the desktop Review surface adds path search,
Tree/List navigation, All/Unreviewed/Semantic-ready filters, base/head-scoped Viewed progress, and filename-first
rows. Added/deleted/copied/renamed and non-indexed rows stay visible but cannot yet open; that is an explicit
contract boundary, not a hidden omission. The renderer survey and decision record live in
[`docs/spikes/browser-diff-renderer-library-survey.md`](../../spikes/browser-diff-renderer-library-survey.md).

Impact is the intended inventory above Review, not a replacement annotation source: Impact answers which
entry points/behaviours changed, Review places each revision's own facts on Git rows, and Tree explains a
selected call.

## Independent follow-ons

- [Open every changed-file shape through a two-path contract](./web-review-two-path-file-diffs.md).
- [Add effect-aware per-file inventory and an honest Effects-changed filter](../todo/web-review-effect-aware-file-inventory.md).
- [Link Impact changes directly into Review](../progress/web-review-impact-deep-links.md).
- [Expand hunk context on demand without loading full files](../todo/web-review-expand-context.md).
- [Adapt remote pull-request providers only after the local review contract is complete](../todo/web-review-provider-adapters.md).
- [Resolve a concrete witness path lazily from web or Rider](../todo/file-lens-lazy-witness-path.md).
- [Promote the file lens from family grain to provider grain](../todo/file-lens-provider-grain.md).
