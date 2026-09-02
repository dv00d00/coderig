# CLI `tree` has no amplification fold — the web tree shows it, the terminal does not

**Status:** todo · **Family:** amplification / output-fidelity
**Extracted from:** [amplification-context-propagation](../done/amplification-context-propagation.md)
("What remains" item 1), 2026-09-02
**Triage:** needs-triage

## The problem

Amplification-as-a-propagated-context shipped 2026-08-04, but the per-child emission shipped for the **web
tree only**. The SPA folds an `amp` depth down the tree and emits `🔁↑n` per child
(`src/Rig.Cli/wwwroot/components.js`); `Rig.Cli/Rendering/TreeRenderer.cs` still renders only the loop edge's
own `🔁[detail]`. So `rig tree --view effects|hazards` does not show that an effect sits beneath a looped
call, while the browser does — the two surfaces disagree about the same store.

## What already shipped

Sections 1 and 3 of [amplification-context-propagation](../done/amplification-context-propagation.md): the
finding is one row per anchor CALL SITE (6,437 → 2,562 on the v5 MedDBase store), the closure is derived at
query time rather than materialized, and the pair grain is retired as a finding surface. Section 2 shipped
for the web tree.

## What counts as finishing

- A fold state bit carried down the existing tree walk in `TreeRenderer`, the same shape as the guard fold
  already in that renderer — not a second traversal and not a materialized per-child row.
- Nested loops compound (render as loop-crossing count; N is never known).
- Opt-out mirrors `--no-amplification`.
- The caveat rides on the rendering, never on truncation: reach is path-insensitive, so a deep child may sit
  on a branch this chain never takes. Depth plus `⎇` guard marks are the confidence signal.
- CLI and web trees agree on which children are amplified for one store and one root.
