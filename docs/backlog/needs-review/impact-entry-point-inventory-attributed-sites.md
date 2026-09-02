# Added/removed entry-point inventory is summary-only in the web Impact response

**Status:** todo · **Family:** impact / web review navigation
**Extracted from:** [web-review-impact-deep-links](../done/web-review-impact-deep-links.md) ("Remaining"), 2026-09-02
**Triage:** needs-triage

## The gap

Added and removed entry points are **summary-only** in the web Impact response: the count is there, the
attributed sites are not. So those rows cannot get a Review link without guessing, and the shipped slice
correctly refuses to guess.

## What already shipped

The effect-site deep-link slice (2026-09-02) and its rule: a behavioral EP card gets a Review link only when
its already-attributed EP source site is itself in the Git changed-file inventory, and deep callee effects
never receive guessed links from their parameter-free enclosing names. Entry-point sites win; added
effects/hazards use head coordinates and removed rows use base coordinates. Record:
[web-review-impact-deep-links](../done/web-review-impact-deep-links.md).

## What counts as finishing

- The web Impact response exposes the attributed site for each added and removed entry point.
- Only then do those rows render `review :line`, under the same unique-declaration and
  in-the-changed-inventory conditions as the effect rows.
- Ambiguous or missing attribution has **no dead link** and discloses why — the rule this card must not
  break to satisfy itself.
- Added/removed fixtures deep-link to the correct side.
