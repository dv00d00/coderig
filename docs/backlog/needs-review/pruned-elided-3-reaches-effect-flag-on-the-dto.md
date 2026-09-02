# The elided-edge prune rule lives twice (C# and JS) — a `reachesEffect` flag on `TreeNodeDto` would make it once

**Status:** needs-review — value not agreed. The parent card's own guidance is "worth doing the next time
`TreeNodeDto` changes for another reason", i.e. not on its own. · **Family:** output-fidelity / web contracts
**Extracted from:** [pruned-elided-edges-drop-their-guards](../done/pruned-elided-edges-drop-their-guards.md)
(open sub-item 3), 2026-09-02
**Triage:** needs-info

## The finding

The prune rule was reimplemented **four** times, which is why one bug appeared on four surfaces (pretty tree,
llm, llm-ids, SPA — while `Web/TreeMapper.cs` and `--format tsv` were correct because they do not prune).
The fix collapsed it to **twice**: `TreeRenderer.SubtreeHasEffect` in C#, and `subtreeHasEffect` in
`wwwroot/components.js` (the server ships every edge; that prune is the browser's own).

Collapsing to once means the **server answering the question in the DTO** — a `reachesEffect` flag per node
in `Web/WebContracts.cs` — and the SPA reading it instead of recomputing.

## What already shipped

The four-to-two collapse and the fix itself. Record:
[pruned-elided-edges-drop-their-guards](../done/pruned-elided-edges-drop-their-guards.md).

## Why it is held rather than scheduled

`TreeNodeDto` is a shared contract, and a payload-shape change means bumping the tree artifact's `*Schema`
constant, which flushes every warm disk cache and every browser's IndexedDB (the client's
`derivationVersion` moves automatically because `DerivationSchemaToken()` folds in all the `*Schema`
constants). Paying that invalidation for a de-duplication alone is a poor trade — hence "next time
`TreeNodeDto` changes anyway".

Note the neighbouring card [W2 — service lens on tree nodes](../todo/web-tree-service-lens.md) also adds a
`TreeNodeDto` field. If W2 is scheduled, that is the "next time", and this should ride along.

## If it is agreed

- One field on `TreeNodeDto`, the SPA prune deleted rather than left beside it.
- Schema bump with its `// vN->vM: <why>` trail.
- The SPA and CLI still agree on which elided edges survive, which is the invariant the parent card asserted
  across all five surfaces at once.
