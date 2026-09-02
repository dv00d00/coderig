# Rider plugin — replace nearest-`.git`/`.rig` host discovery with an explicit solution-to-host association

> **PARKED 2026-09-02** - the Rider plugin experiment is deprioritised in favour of the web view, by the product owner's explicit decision. Reopen if that decision reverses.

**Status:** todo · **Family:** rider plugin / product boundary
**Extracted from:** [rider-plugin-minimal-product](../done/rider-plugin-minimal-product.md) (two of its open boundary bullets), 2026-09-02
**Triage:** needs-triage

## The problem

Two bullets on the parent card, the same defect from two directions:

- host discovery walks up to the nearest `.git` / `.rig`;
- restart discovery uses the **first root solution** it finds.

Both guess. On this machine that guess is demonstrably wrong-able: MedDBase has multiple clones
(`meddbase-main-application`, `-2`, `-3`) and its indexed store lives in a *different* directory
(`meddbase-analysis`) from the source it indexes. A nearest-marker walk cannot tell which store a given
solution's answers should come from.

## What already shipped

The reproducible local product: plugin `dev.coderig.rider` 0.4.0 on Rider 2026.2, Code Vision plus a true
gutter mark plus an inline call-site hint from the same semantic rows, SQL and file-system families rendering
side by side, and — importantly for this card — Rider **never** opens the SQLite store and never waits
synchronously for the host, with missing / stale / unindexed / ambiguous answers failing closed. Restart is
acknowledged over the current-user pipe before the old host shuts down. Record:
[rider-plugin-minimal-product](../done/rider-plugin-minimal-product.md).

## What counts as finishing

- An explicit association from a Rider solution to a host (and therefore to a store), persisted per
  solution.
- Discovery may still *propose* a host, but the association is what is used.
- A mismatch fails closed with a message naming both sides, consistent with the existing fail-closed states
  — never a silent answer from the wrong store.
- The same association drives restart, so the first-root-solution rule disappears rather than being fixed.
- Interacts with the host's own single-instance rule: a second `rig watch` in one directory refuses to boot
  (decided 2026-08-22), so the plugin must surface that refusal rather than retrying.
