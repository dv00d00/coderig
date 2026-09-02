# Live background index — the final gate and the disposable-clone release workout

**Status:** todo · **Family:** index performance / architecture · release gate
**Extracted from:** [live-background-index](../done/live-background-index.md) (the "Next:" in its status line), 2026-09-02
**Triage:** ready-for-human (needs a disposable MedDBase clone and a long-lived host on this machine)

## What already shipped

Slices 0–7B3, completed locally 2026-08-23: deterministic scale/trial baselines, emitter provenance,
immutable snapshot generations, a streaming composite fact view, atomic dirty-only watcher batches, the v7
surface-fact substrate, per-origin lazy surface refinement, cascade verification with coarse fallback, the
emitter-aware immutable graph-fact substrate and keyed symbol catalog, demand-shaped generic adjacency, live
`path`, query-triggered exact forward refinement for `path`/`reaches`/`tree`, keyed reverse topology and
exact refinement for `callers`, delivery-aware resident projection for all four traversal verbs, then the
agent dogfood and its CLI/freshness/hotspot/consistency corrections. Full record:
[live-background-index](../done/live-background-index.md).

## What remains

The program's own named next step: **the final gate plus a disposable-clone release workout.** That card is
otherwise a shipped record, so this is the only item that closes the program rather than extending it.

## What counts as finishing

- A run on a **disposable clone** (not the primary checkout), so the design-time-build cache cost of a fresh
  location is measured rather than assumed — a fresh `git worktree`/clone loses `.rig/dtb-cache` and forces a
  from-scratch build of the whole monorepo, which is the honest release condition.
- The full local release gate green, recorded with its test count.
- Boot, edit, reconcile and query numbers captured on that clone, alongside the existing MedDBase
  (226/227-project) numbers, so the release claim is not made from the warm primary checkout.
- The three measurement lessons in the parent card are respected: interleave the arms, and do not let your
  own load contaminate the arm you are measuring.
