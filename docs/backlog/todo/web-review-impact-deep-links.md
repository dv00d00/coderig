# Web Review: deep-link Impact changes into the exact file diff

**Status:** todo · **Family:** impact / web review navigation
**Triage:** ready-for-agent

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

## Acceptance

- Added/removed/changed method fixtures deep-link to the correct old or new side.
- A predicate-only Impact delta lands on its changed edge or guard line when that location exists.
- Ambiguous/missing locations have no dead link and disclose why.
- Browser back/forward preserves the Impact → Review pivot.

**Blocked by:** [Two-path file diffs](../progress/web-review-two-path-file-diffs.md) for added, deleted, and
renamed locations.

## Out of scope

- Embedding the entire Impact report inside Review.
- Remote provider URLs or posting comments.
