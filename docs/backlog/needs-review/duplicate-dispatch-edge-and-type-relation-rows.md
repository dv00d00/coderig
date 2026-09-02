# 23.9% of `dispatch_edges` rows in the cold store are duplicates

**Status:** todo · **Priority: LOW** (storage waste, but it inflates every count read off those tables) · **Family:** storage
**Extracted from:** [live-background-index](../done/live-background-index.md) ("Open, ranked" #6), 2026-09-02
**Triage:** needs-triage

## The measurement

In the cold store, **7,193 of 30,069 `dispatch_edges` rows (23.9%) are duplicates**, plus **7.8% of type
relations**. Pure storage waste — and it inflates every count read off those tables, which matters because
those counts are quoted in calibration write-ups.

Found by the live-index program but unrelated to it, which is why it is extracted rather than left in that
card's ledger: [live-background-index](../done/live-background-index.md).

## The complication to respect

Duplicate semantic emissions from **distinct files** are deliberate and must be retained: Slice 1 made
`TypeRelationFact` and `DispatchFact` carry their exact `SourceModel.FilePath` emitter identity, and
`ResidentIndex` replaces only the rows owned by an overlaid file. **Graph projections alone collapse them.**
So this is not "add a DISTINCT" — a same-emitter duplicate is waste, a cross-emitter duplicate is
provenance.

## What counts as finishing

- The duplicate population classified: same-emitter (waste) versus distinct-emitter (provenance), with
  counts.
- Same-emitter duplicates eliminated at the write side, with the emitter-identity and deletion regressions
  from Slice 1 still green.
- The corrected row counts recorded, so later calibration numbers are comparable to earlier ones rather than
  silently shifting.
- Note this touches the write side, so it implies a re-index of any store whose counts are being compared.
