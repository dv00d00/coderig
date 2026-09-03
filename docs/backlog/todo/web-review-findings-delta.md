# Web Review: compare findings across revisions instead of rendering two snapshots

**Status:** todo · **Family:** web review / semantic delta
**Triage:** ready-for-agent
**Extracted from:** [the shipped Web Review delivery record](../done/web-review-effect-gutter-and-delta.md),
2026-09-03.

## Problem

Review compares method effect reach across base and head, but findings are still rendered as two independent
snapshots. A hazard present on both revisions reads as news twice, while a finding that changed evidence,
severity, dispatch basis, repetition, or witness context has no explicit delta.

## Accepted contract

- Match findings by the most stable semantic identity already present in the payload; never use line equality
  as the sole identity because method bodies move and rewrite.
- Report added, removed, and materially changed findings. A byte-identical finding is context, not a delta.
- Fail closed when the two findings are not safely comparable. A changes-only filter must keep uncomparable
  findings visible with that state rather than silently discarding them.
- Keep the current effect delta unchanged; this slice adds the finding axis rather than inventing a second
  effect model.

## Acceptance

- Stable, added, removed, evidence-changed, loop-changed, and dispatch-basis-changed fixtures are pinned in
  unified and split review.
- A finding whose line moves but whose semantic identity and evidence do not change is not marked changed.
- Added/deleted files do not paint every finding as a misleading cross-revision delta.
- If the existing DTO is sufficient, this is render-side and owes no `*Schema` bump. If a payload field must
  change, stop and record the required schema/cache slice before implementation.

## Out of scope

- Computing a new hazard family.
- Shipping every witness path eagerly.
- Ranged Git hunk expansion; that is [its own card](./web-review-expand-context.md).
