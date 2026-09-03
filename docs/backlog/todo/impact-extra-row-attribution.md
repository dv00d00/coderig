# Attribute the historical extra Web Impact EP as intrinsic-only or hazard-only

**Status:** todo · measurement only · **Family:** impact / parity provenance
**Triage:** ready-for-human
**Extracted from:** [the closed behavioral-count divergence](../done/cli-web-parity-2-impact-behavioral-count.md),
2026-09-03.

## Question

The retired store pair had one extra raw web row,
`echoactor MedDBase.Pathways.Processes.Admin.Catalogues.Inbox`. The shared-selection bug is fixed, but the row's
historical cause was never established: intrinsic-only delta or hazard-only delta.

## Measurement

On any live MedDBase store pair with a raw-versus-selected count difference:

1. Count `ep_delta` rows from `rig impact --base A --head B --format tsv`.
2. Repeat with `--intrinsic`.
3. Compare both with `/api/impact?base=A&head=B` and its raw/selected counts.
4. Record whether the additional rows are intrinsic-only, hazard-only, or a third class.

## Acceptance

- The exact store pair and commands are recorded.
- The row-class attribution is evidence-backed; absence of a reproducing pair is reported as such rather than
  inferred from the lost historical stores.

## Out of scope

- Changing selection or behavioral counts; that shipped in `cacb5d92`.
