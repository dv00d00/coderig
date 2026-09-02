# `TreeCommand`'s root filter still asks the old question, so a root whose effects sit behind an elided edge is skipped whole

**Status:** needs-review — value not agreed. Same-as-before behaviour, **not a regression**, and the fix
means restructuring how the renderer is entered. · **Family:** output-fidelity / control-dependence
**Extracted from:** [pruned-elided-edges-drop-their-guards](../done/pruned-elided-edges-drop-their-guards.md)
(open sub-item 1), 2026-09-02
**Triage:** needs-info

## The finding, with its anchor

`TreeCommand.cs:839` calls `SubtreeHasEffect(root, effectsByMethod)` with **no scope**, before that root has
been observed. A `Truncated` ("⋯elided") node's `Children` is empty by construction, so a root whose only
effects sit behind an elided edge answers "no effect below" and is skipped whole.

## What already shipped

The prune fix itself, on all four surfaces. `ElidedEffectScope` (`Rendering/TreeRenderer.cs`) computes
symbol-level "does this callee reach an effect?" over the rendered forest — a caller-edge map plus a backward
BFS from every effect-bearing symbol, order-independent and deterministic. `SubtreeHasEffect` takes an
optional scope: a `Truncated` node answers from it, expanded nodes keep the exact per-position walk.
`LlmSummaryRenderer` deleted its private copy; `wwwroot/components.js` got the same rule. Cost on the real
store: the effect-pruned tree grows ~10–25% in lines, every added line a real call edge. Tests:
`tests/Rig.Tests/Cli/TreeElidedGuardedEdgeTests.cs`. Record:
[pruned-elided-edges-drop-their-guards](../done/pruned-elided-edges-drop-their-guards.md).

## Why it is held rather than scheduled

The behaviour is unchanged from before the fix, so nothing regressed. The fix requires giving the renderer
**the forest** rather than one root at a time: `RenderTreeNode` is called per root and `FoldSingleImplHops`
is applied per root too. That is a structural change to the pretty path for a case whose real-world
frequency has not been measured.

## If it is agreed

- Measure first: how many roots on the real store are skipped by this filter.
- A forest-level entry point for the pretty renderer, which also retires sub-item 2 (root ORDER being
  load-bearing) — see
  [pruned-elided-2-root-order-is-load-bearing](./pruned-elided-2-root-order-is-load-bearing.md).
- The invariant to keep: an elided edge that reaches NO effect stays pruned. The fix is not "never prune an
  elided edge".
