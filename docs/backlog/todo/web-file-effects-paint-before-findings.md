# Web File view waits for findings before painting already-known effects

**Status:** todo · **Family:** web file lens / progressive rendering
**Triage:** ready-for-agent
**Extracted from:** [the shipped Web Review delivery record](../done/web-review-effect-gutter-and-delta.md),
2026-09-03.

## Problem

Review paints effect marks as soon as their request completes, but the ordinary File view still joins effects
and findings behind one `Promise.all`. On a large store the independent findings request can take seconds, so
known effect annotations remain invisible and the file reads as though analysis is stalled.

## Accepted contract

- Effects paint immediately when their request completes.
- Findings enrich the same rows later without replacing newer navigation/filter state.
- Each stream owns distinct loading, unavailable, empty, and error disclosure; a findings failure must not
  erase a valid effects result.
- Generation guards prevent a late response for a prior file or revision from overwriting the current view.

## Acceptance

- Tests cover effects-first, findings-first, findings failure, navigation during either request, and a stale
  late response.
- Progressive painting issues no additional semantic request and preserves the current final combined output.
- Render-only orchestration changes owe no `*Schema` bump.

## Out of scope

- Caching the cross-method correlation; see
  [the dedicated cache card](./derivation-cache-2-cross-method-hazards-cache.md).
- Changing effect or finding derivation.
