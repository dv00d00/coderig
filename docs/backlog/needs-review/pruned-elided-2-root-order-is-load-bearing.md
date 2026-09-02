# Root ORDER is load-bearing in the pretty tree renderer

**Status:** needs-review — value not agreed. The current behaviour is **sound**; this is a latent coupling,
not a defect. · **Family:** output-fidelity
**Extracted from:** [pruned-elided-edges-drop-their-guards](../done/pruned-elided-edges-drop-their-guards.md)
(open sub-item 2), 2026-09-02
**Triage:** needs-info

## The finding

The pretty renderer accumulates `ElidedEffectScope` **root by root**. That is sound only because `BuildTree`
expands roots in the same order it renders them, so the expanded occurrence of a callee is always observed no
later than the elided edges naming it. The llm renderers observe the whole forest up front and have no such
dependency.

So the pretty path's correctness depends on an ordering agreement between two components that nothing
asserts. Change the expansion order for an unrelated reason and the guards start disappearing again — the
exact defect the parent card fixed.

## What already shipped

The prune fix on all four surfaces, including the order-independent `ElidedEffectScope` used by the llm
renderers. Record:
[pruned-elided-edges-drop-their-guards](../done/pruned-elided-edges-drop-their-guards.md).

## Why it is held rather than scheduled

Nothing is wrong today, and the fix is the same restructuring sub-item 1 needs: fold the pretty path onto a
forest-level entry point and the caveat goes.

## If it is agreed

- Do it together with
  [pruned-elided-1-tree-root-filter-asks-the-old-question](./pruned-elided-1-tree-root-filter-asks-the-old-question.md);
  they share one change.
- Cheaper interim option worth considering instead: a test that pins the ordering agreement, so a future
  expansion-order change fails loudly rather than silently deleting guards.
