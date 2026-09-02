# Web Review: deep-link Impact changes into the exact file diff

**Status:** progress — effect-site slice shipped 2026-09-02; guard/predicate sites remain · **Family:** impact / web review navigation

## Problem

Impact answers which entry points and behaviours changed; Review places base/head facts on Git rows. Today the
reader must manually find the changed file and line after discovering an Impact delta, even though both views
already carry store identities and symbol locations.

## Accepted interaction

- Add a Review action only where Impact can resolve an exact changed file and useful line.
- The link carries base, head, file identity, and line in the URL so it is shareable and survives refresh.
- Review opens that file, selects the hunk containing the line when one exists, and otherwise explains that the
  symbol is outside the textual patch rather than scrolling to a fabricated row.
- Review remains revision-native: Impact is navigation inventory, not an alternate annotation source.

The first slice is deliberately narrower than symbol-location enrichment: a behavioral EP card gets a Review
link only when its already-attributed EP source site is itself in the Git changed-file inventory. Deep callee
effects do not receive guessed links from their parameter-free enclosing names. That preserves the “no dead
link” rule while proving the cross-mode URL/focus contract; exact effect/guard-site enrichment remains below.

## Shipped slice

- `/api/impact` effect and hazard rows now carry a revision-native source location only when their parameter-free
  enclosing identity resolves to one unique method declaration. Overload ambiguity fails closed (`file=null`).
- The Impact client joins those locations to the exact `/api/review-files` inventory and renders `review :line`
  only for files in the selected Git diff. Entry-point sites win; added effects/hazards use head coordinates and
  removed rows use base coordinates.
- The link is a normal shareable URL (`app=review&base=…&head=…&file=…&line=…&side=base` when needed), so browser
  Back returns to Impact without a bespoke history emulation.
- Review focuses the exact old/new diff row when it is in a hunk or the 20-line context. If not, it says the
  requested revision line is outside the rendered hunks rather than scrolling to a fabricated match.
- Dogfood on CodeRig's 15-card Impact report produced 15 valid Review links; `AnnotateCommand.cs:32` focused in an
  added-file diff, Back restored Impact, and an out-of-hunk target disclosed the limit. Browser console clean;
  local release gate 1350/1350.

## Remaining

- Guard-condition deltas are not in the web Impact contract and the current guard diff deliberately has no
  file/line identity. Predicate-only Impact → Review therefore still needs an explicit edge-site projection.
- Added/removed entry-point inventory is summary-only in the web response; expose its attributed sites before
  claiming direct Review links for those rows.

## Acceptance

- Added/removed/changed method fixtures deep-link to the correct old or new side.
- A predicate-only Impact delta lands on its changed edge or guard line when that location exists.
- Ambiguous/missing locations have no dead link and disclose why.
- Browser back/forward preserves the Impact → Review pivot.

**Unblocked by:** [Two-path file diffs](../done/web-review-two-path-file-diffs.md), including added, deleted, and
renamed locations.

## Out of scope

- Embedding the entire Impact report inside Review.
- Remote provider URLs or posting comments.

## Remainder extracted

Moved `progress/` -> `done/` on 2026-09-02 when `progress/` was unbundled into a shipped record plus its
tail. Everything above is unchanged. The open items now live on their own cards:

- [Guard/predicate deltas need an edge-site projection before they can deep-link](../needs-review/impact-guard-site-deep-links.md)
- [Added/removed entry-point inventory is summary-only in the web response](../needs-review/impact-entry-point-inventory-attributed-sites.md)
