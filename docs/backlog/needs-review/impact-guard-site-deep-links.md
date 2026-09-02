# Guard/predicate Impact deltas cannot deep-link — the guard diff has no file/line identity

**Status:** todo · **Family:** impact / web review navigation
**Extracted from:** [web-review-impact-deep-links](../done/web-review-impact-deep-links.md) ("Remaining"), 2026-09-02
**Triage:** needs-triage

## The gap

Guard-condition deltas are **not in the web Impact contract at all**, and the current guard diff deliberately
carries no file/line identity. So a predicate-only Impact delta has nowhere to link: the reader is told the
behaviour changed and must find the edge by hand.

## What already shipped

The effect-site slice, 2026-09-02: `/api/impact` effect and hazard rows carry a revision-native source
location when their parameter-free enclosing identity resolves to **one unique** method declaration (overload
ambiguity fails closed, `file=null`); the Impact client joins those to the `/api/review-files` inventory and
renders `review :line` only for files in the selected Git diff; the link is a shareable URL
(`app=review&base=…&head=…&file=…&line=…&side=base`), and Review focuses the exact old/new diff row or says
the requested revision line is outside the rendered hunks. Dogfood: 15 valid links from a 15-card report,
`AnnotateCommand.cs:32` focused in an added-file diff, browser console clean, gate 1350/1350. Record:
[web-review-impact-deep-links](../done/web-review-impact-deep-links.md).

## Why the guard half is separate work

Guard deltas are **per-edge**, not per-symbol. `impact-guard-delta-for-predicate-only-changes` keys the guard
delta on edges precisely to avoid composing predicate text across methods, so there is no single owning
declaration to resolve a line from. Deep-linking therefore needs an explicit **edge-site projection**: the
call site whose guard changed, on the correct revision's coordinates.

## What counts as finishing

- Guard-condition deltas appear in the web Impact contract with base→head conditions and the same verdicts
  the CLI shows (the CLI-only half of this is §3 of
  [cli-web-parity-1](../todo/cli-web-parity-1-web-api-seed-and-effect-disclosure-parity.md)).
- A predicate-only delta lands on its changed edge or guard line when that location exists.
- The no-dead-link rule holds: an unresolvable site has no link and discloses why.
- Added effects use head coordinates, removed rows use base coordinates — the same convention the shipped
  slice set.
