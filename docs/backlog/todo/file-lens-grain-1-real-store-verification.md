# Verify the monomorphized file-lens reverse-seed fix on MedDBase

**Status:** todo · verification only · **Family:** file lens / real-store gate
**Triage:** ready-for-human
**Extracted from:** [the shipped method-row join fix](../done/file-lens-grain-1-emits-a-marked-line-with-no-owning-method-row.md),
2026-09-03.

## What remains

The reverse expansion from a generic base seed to its concrete `~mono` instantiations and the synthetic
method/line invariant shipped in `b59b6aba`. The original real-store case was not rerun against that build.

From `c:/git/meddbase-analysis`, query `LocationsHandler.cs` with the current installed tool and confirm:

- `LocationsIdHandler.GetList` has the expected `db` method row;
- callers `GetData` and `ValueToJson` also carry the transitive `db` method rows;
- the line badge at the `Fill``1` call remains present;
- warm/resident and `--cold` answers agree.

## Acceptance

- Paste the actual `rig annotate --summary --format tsv` evidence and the selected store id.
- Any mismatch reopens a new implementation card with the observed output; do not edit the terminal delivery
  record into an in-flight card again.

## Out of scope

- Rebuilding the read model around one canonical evidence set; that remains a separate design on the
  [file-lens grain map](./file-lens-grain-map.md).
