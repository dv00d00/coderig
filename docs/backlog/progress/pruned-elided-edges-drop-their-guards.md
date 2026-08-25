# The effect prune deleted "⋯elided" call EDGES, so an unconditional call rendered as conditional

**Status:** ✅ FIXED (pretty tree, llm/llm-ids, SPA) · open sub-items below · **Priority: HIGH** (a reviewer
reads a must-run call as branch-gated; guard-based conclusions become unsound) · **Found:** 2026-08-25
· **Family:** output-fidelity / control-dependence
**Related:** [[guard-condition-renderer-divergence-tsv-llm]] (polarity, extraction-side; this is the prune),
`fa1cc1ce` (the display-vs-`--format tsv` invariant this violated a second time)

## The observation

`WorkflowControllerExtensions.FormatDelegateList` (MedDBase, store `2f944e739e47-dirty`). Its loop body is an
if/else whose BOTH arms call `member.GetDisplayName(t)` and `SecureAccessors.GetChamberSuffix(…)`:

```csharp
if (member.MemberTypeId == "PR")
    builder.AppendFormat("\"{0}{1}\"", member.GetDisplayName(t), SecureAccessors.GetChamberSuffix(…));
else {
    builder.Append(member.GetDisplayName(t));
    builder.Append(SecureAccessors.GetChamberSuffix(…));
}
```

`--format tsv` emitted four depth-1 rows — each callee twice, guarded `member.MemberTypeId == "PR"` and
`!(member.MemberTypeId == "PR")`. The pretty tree, `--format llm`, `--format llm-ids` and the SPA each showed
`GetChamberSuffix` **once**, guarded `[member.MemberTypeId == "PR"]`, and no direct depth-1 `GetDisplayName`
edge at all. The two guards are complementary, so both calls in fact run on every iteration: the display
stated the opposite of the truth, with no `×N calls` and no `⋯elided` marker to hint at a loss.

## Root cause — the prune, not the tree

The `TraceNode` forest was always right. `FactPathFinder.BuildTree`'s sibling-edge collapse already keys on
`EnclosingGuards`, so `×N calls` never merges differing conditionality, and `rig tree --view full --guards`
(same forest, prune off) rendered both edges with their own predicates all along.

The defect was in the **effect prune** — `TreeRenderer.SubtreeHasEffect`. A repeated callee renders as a
`Truncated` ("⋯elided") placeholder whose `Children` is empty by construction; the rule answered from that
empty list, concluded "no effect below", and dropped the node. Its comment argued this was lossless —
"the effects under the method's real subtree are printed under its first (expanded) occurrence, so nothing is
lost". Effects were not lost. The **edge** was, and an edge carries what no other occurrence can supply: its
`⎇` guard, its `×N` count, and the plain fact that this caller calls that callee. Whichever occurrence the DFS
happened to expand first kept its guard; the rest were deleted.

The same rule was **reimplemented four times**, so the same bug appeared on four surfaces:

| surface | prune site | before |
| --- | --- | --- |
| pretty tree | `Rendering/TreeRenderer.cs` `SubtreeHasEffect` | dropped |
| llm / llm-ids | `Rendering/LlmSummaryRenderer.cs` private copy ("duplicated here to keep the renderer self-contained") | dropped |
| SPA | `wwwroot/components.js` `subtreeHasEffect` | dropped (client-side) |
| web DTO | `Web/TreeMapper.cs` — does not prune | correct |
| `--format tsv` | does not prune | correct |

## The fix

Semantics chosen: **one rendered edge per distinct guard.** A guard is per-edge information, so neither the
sibling collapse nor the prune may merge or delete an edge whose guard differs from its neighbour's — and an
unconditional call must never render as guarded.

- `ElidedEffectScope` (`Rendering/TreeRenderer.cs`): symbol-level "does this callee reach an effect?" over the
  rendered forest — a caller-edge map plus a backward BFS from every effect-bearing symbol. Order-independent
  and deterministic, unlike "whichever occurrence the DFS expanded first".
- `SubtreeHasEffect` takes an optional scope: a `Truncated` node answers from it; expanded nodes keep the exact
  per-position walk. Passing no scope preserves the old answer, so the renderer's unit tests are untouched.
- `LlmSummaryRenderer` deletes its private copy and calls `TreeRenderer.SubtreeHasEffect` — one implementation.
- `wwwroot/components.js` gets the same rule (the server ships every edge; that prune is the browser's own).

No `*Schema` bump: the cached payload (forest + effects + locations + seam union) is unchanged — this is a
render-side rule, applied after the cache read. Verified: a warm-cache run renders the corrected tree.

Cost on the real store: the effect-pruned tree grows ~10–25% in lines (`PersonEventEntity.Save` 311 → 382,
`FormatDelegateList` 69 → 82, `CertificateEntity.GetAllRights` 21 → 24). Every added line is a real call edge
to a method that reaches an effect, and several carry a guard that was previously invisible — e.g.
`ProfileCache.New ⎇ [!CertificateCache.TryGetRights(…)] ⋯elided`.

Tests: `tests/Rig.Tests/Cli/TreeElidedGuardedEdgeTests.cs` — three tests, all failing before the fix.

## Open sub-items

1. **`TreeCommand`'s root filter still asks the old question.** `TreeCommand.cs:839` calls
   `SubtreeHasEffect(root, effectsByMethod)` with no scope, before that root has been observed, so a root whose
   only effects sit behind an elided edge is still skipped whole. Same-as-before behaviour, not a regression;
   fixing it means giving the renderer the forest rather than one root at a time (`RenderTreeNode` is called
   per root, and `FoldSingleImplHops` is applied per root too).
2. **Root ORDER is load-bearing in the pretty renderer.** It accumulates the scope root by root, which is
   sound only because `BuildTree` expands roots in the same order it renders them (so the expanded occurrence
   is always observed no later than the elided edges naming it). The llm renderers observe the whole forest up
   front and have no such dependency. Fold the pretty path onto a forest-level entry point and the caveat goes.
3. **The rule now lives twice** (C# + JS) instead of four times. Collapsing to once means the server answering
   the question in the DTO (a `reachesEffect` flag per node, `Web/WebContracts.cs`) and the SPA reading it.
   Worth doing the next time `TreeNodeDto` changes for another reason.

## Acceptance (met)

- A fixture whose if/else arms BOTH call one callee renders two edges, one per polarity, and never attributes
  a single branch's guard to a call that runs on both paths.
- The depth-1 edge set agrees across the pretty tree, `--format tsv`, `--format llm`, `--format llm-ids` and
  the web DTO — the `fa1cc1ce` invariant, now asserted on every surface at once.
- An elided edge that reaches NO effect stays pruned (the fix is not "never prune an elided edge").
