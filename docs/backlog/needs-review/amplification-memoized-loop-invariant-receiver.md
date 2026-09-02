# Amplification FP class 3 — memoized / loop-invariant receiver

**Status:** todo · **Family:** amplification / precision
**Extracted from:** [amplification-context-propagation](../done/amplification-context-propagation.md)
("What remains" item 2), 2026-09-02
**Triage:** needs-info (the proposed confidence signal needs Dmytro's call before anything is built)

## The problem

The last structural false-positive class of the cross-method amplification surface: `??=` fields and LLBLGen
lazy navigations on an entity captured **outside** the loop. The anchor is a real looped call site and the
witness is a real IO effect, but the receiver is memoized, so the effect fires once, not per iteration. It
was ~4 of 24 FPs in the 2026-08-03 40-site audit and is one of the residual classes named after the v5 fix.

Unlike FP classes 1 and 2 — monadic comprehensions (fixed by `reference_facts.EnclosingLoopBindType`) and
expression-tree clauses (fixed by `reference_facts.InExpressionTree`) — this one is **path-sensitivity**, so
there is no equivalent fact-layer gate.

## What already shipped around it

The v5 fact-layer fixes plus the anchor-grain finding surface: 93% precision (TP + TP-weak) on the fresh
14-site stratified audit of the v5 surface, up from 40%. Full record:
[amplification-context-propagation](../done/amplification-context-propagation.md).

## The candidate signal, and why it is not built

Proposed: **"anchor receiver derives from the iteration variable"** as a confidence signal rather than a
gate. It is adjacent to the killed key-classifier, which is why the parent card says it needs Dmytro's call
before building. This card exists so that decision is visible, not so it can be taken by an implementer.

## What counts as finishing

- A decision recorded on whether the receiver-derivation signal is built at all, and whether it gates or
  only annotates.
- If built: the audit's memoized-receiver FPs demoted or excluded without losing the spot-checked TPs, on
  the same store, with before/after counts.
- Residual classes still disclosed rather than silently suppressed: PK-bounded loops and
  switch-over-loop-variable remain.
