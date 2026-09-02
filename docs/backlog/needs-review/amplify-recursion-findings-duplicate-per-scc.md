# `rig amplify` — 186 recursion findings over 146 distinct heads

**Status:** todo · **Priority: LOW** · **Family:** amplification / finding grain
**Extracted from:** [nonlinear-amplification-degree](../done/nonlinear-amplification-degree.md) (follow-up), 2026-09-02
**Triage:** needs-triage

## The problem

The recursion section reports **186 findings across 146 distinct heads** on `2f944e739e47-dirty`: an anchor
that merely REACHES a cycle is reported as unbounded alongside the cycle's own head. The claim is not wrong —
depth is a runtime property, so anything reaching the cycle inherits unbounded degree — but the same cycle is
announced repeatedly.

## What already shipped

The recursion arm itself: a method self-reachable in a call-graph SCC of size > 1 (or carrying a self-edge)
that reaches an in-scope effect is reported in its own RECURSION section rather than given a number. Ground
truth: `TemplateEntity.HtmlService.ExpandSection` ↔ `Sections` (structured-template expansion, width ×
depth). The iterative Tarjan SCC and condensation already exist in `FactAmplificationDegreeDeriver`.
Record: [nonlinear-amplification-degree](../done/nonlinear-amplification-degree.md).

## The fix the parent card names

**Dedupe by SCC.** The SCC computation is already in the deriver, so this is a grouping change at emission,
not new analysis.

## What counts as finishing

- One finding per SCC, naming the reaching anchors it absorbed rather than dropping them.
- Both recursion ground-truth items still rediscovered.
- Before/after counts on the same store.
