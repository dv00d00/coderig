# Allocation mechanisms still uncovered: initializers, collection expressions, async state machines

**Status:** todo · **Family:** performance analysis · detector coverage
**Extracted from:** [alloc-effect-detector](../done/alloc-effect-detector.md) (shipped record), 2026-09-02
**Triage:** needs-triage

## The gaps, as the shipped detector discloses them

[alloc-effect-detector](../done/alloc-effect-detector.md) names these as explicit, deliberate omissions
rather than silent inferences:

1. **Field and auto-property initializer allocations are omitted** until their owner can be mapped to
   `.ctor` / `.cctor`. This is the same ownership gap `CLAUDE.md` records for effects generally: an effect
   keyed to a `P:` or `F:` id is never a call-graph node, so it shows in `rig derive` totals and never
   surfaces from any caller. ~24 effects estate-wide on the effect side; the allocation side has the
   identical constraint.
2. **Collection expressions** are not covered.
3. **Async state machines** are not covered (the iterator state machine IS — `iterator_state_machine` is a
   shipped mechanism, and async is its deliberate negative control today).

Attribute metadata stays excluded on purpose: its object/array-shaped arguments do not execute at the usage
site.

## What counts as finishing

- Each gap either covered with its own mechanism token in the shipped vocabulary, or re-recorded as a
  deliberate exclusion with the reason — not left ambiguous.
- The initializer arm keys the fact to `.ctor` / `.cctor`, so the resulting effect's enclosing id is a real
  call-graph node and the allocation is reachable from a caller.
- Negative controls kept: constant-folded strings, omitted `params`, `AsSpan`, attribute metadata.
- Re-index the evaluation target; this is extraction work.
- Calibrate with `--intrinsic` or `--only alloc`, never a bare `derive`.
