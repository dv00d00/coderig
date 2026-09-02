# `rig peers <ep>` — sibling entry-point discovery

**Status:** todo · **Priority: HIGH** (the reviewer's hard part; it feeds the parity diff) · **Family:** reviewer-invokable queries
**Extracted from:** [reviewer-invokable-queries](../done/reviewer-invokable-queries.md) (ranked item 2), 2026-09-02
**Triage:** needs-info (the peer relation is a product decision: which relations count as sibling)

## The problem

The dominant in-scope defect pattern in the 500-issue MedDBase corpus is **effect/guard divergence across
paths** — "two paths to one write, the set differs", roughly 35 of the 65 in-scope issues. The diff for that
is already shippable (`rig effects-diff <a> <b>`, and the guard sets ride along as `permission:assert`
effects). What a reviewer cannot do is **know which parallel path to compare**: UI vs EAPI, manual vs import,
add vs edit, save vs save-as.

The corpus's #1 meta-heuristic is literally "find the second path the change didn't touch". Today that is
guesswork.

## What already shipped

`rig effects-diff` plus per-row `provider:op` kind labels in the human view and a new TSV column — validated
on the store against SmartLetter `SaveLetter` vs `PrintLetter` (guard divergence via `--only permission`,
write divergence via `--only llblgen:bulk_write/audit`). Deliberately NOT renamed to `parity` and given no
baked-in preset; rig stays a composable primitive. Full record:
[reviewer-invokable-queries](../done/reviewer-invokable-queries.md).

## What the command has to answer

Given an EP, surface its peers: other EPs writing the same table/entity, the import/bulk counterpart of a UI
action, add/edit pairs.

## The decision this needs first

Which relations count as "peer", and whether they are rules data. Resource-identity peering leans on the
`resource` field, which for `llblgen` rules is `receiver_type` — the entity CLASS name, with **377 distinct
effect sites behind `AppointmentEntity`** and **16.6% of llblgen effects carrying no entity resource at all**
(`LinqMetaData` 2,195, `CommonEntityBase` 554, `int` 359). So resource-identity peering is coarse; see
[telemetry-join-to-effect-sites](../todo/telemetry-join-to-effect-sites.md) for the measured cardinality and
[quoted-query-resource-attribution](../todo/quoted-query-resource-attribution.md) for the missing-resource half.

## What counts as finishing

- Peer relations are configured, not hardcoded — no project vocabulary in core (see
  [core-purity-project-vocabulary](../done/core-purity-project-vocabulary.md)).
- Output feeds `effects-diff` directly: a peer list whose entries are usable as its second argument.
- Validated against named corpus pairs (FR-8 import-vs-manual: #557 / #766 / #775 / #1542 / #1548 / #558).
- Coarse peers are disclosed as candidates, never as "the sibling".
