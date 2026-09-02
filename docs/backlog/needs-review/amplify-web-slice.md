# Web slice for `rig amplify` — a degree-ranked amplification view

**Status:** todo · **Family:** amplification / web
**Extracted from:** [nonlinear-amplification-degree](../done/nonlinear-amplification-degree.md) (its first follow-up), 2026-09-02
**Triage:** needs-info (a new web surface; see the decide-at-design-time rule below)

## Why this card exists

`rig amplify` shipped 2026-08-28 (`b1e2952a`) and its output is a **ranked report**, which per CLAUDE.md's
decide-CLI-only-vs-CLI+web rule qualifies for a web slice. The parent card scoped that slice as an explicit
follow-on and deliberately excluded it from v1:
[nonlinear-amplification-degree](../done/nonlinear-amplification-degree.md).

## What already shipped

The whole CLI feature: `FactAmplificationDegreeDeriver` (pure engine) plus `AmplifyCommand`, three sections
(super-linear / configured separate category / recursion), human and TSV rendering, 16 tests. The whole
MedDBase estate (10,109 EPs) runs in **1m06s**, emitting 663 super-linear findings plus 186 recursion.
Measured degree distribution on `2f944e739e47-dirty`: degree 1: 2,051 · 2: 509 · 3: 82 · 4: 33 · 5: 28 ·
6: 7 · 7: 4 · recursion 186, with 368 ✔ / 35 ~ confidence on the degree≥2 set.

## Shape, as the parent card specified it

- Reuse the **hazards mark stream** rather than inventing a second transport.
- Grouping, ranking and exclusion are **rules data** (`observations.amplificationCategories`, first match
  wins) — core ships no default categories, so the web view must render whatever the store's categories say,
  including a category's own `label` as a section heading. No provider token may appear in the web code
  either.
- It needs **its own cache-key thinking**: `amplify` is query-side and currently uncached — see
  [`rig amplify` is uncached](./amplify-is-uncached.md).

## What counts as finishing

- A degree-ranked view whose sections and ordering come from the store's categories, not from client code.
- Every hop of a reported chain is navigable — the CLI output already names entry point, each loop with its
  range variable and method, the terminal effect, and `file:line` per hop; the web view must not drop that.
- The `~` (span-containment heuristic) confidence tag is visible, never silently upgraded to `✔`.
- A cache-key decision recorded before it ships warm.
