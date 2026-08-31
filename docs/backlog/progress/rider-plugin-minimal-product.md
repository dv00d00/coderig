# Rider plugin — minimal local product

## Goal

Ship the validated file-effect read model as a reproducible, locally installable Rider plugin before adding
interaction or configuration surface.

## Shipped slice

- Backend-only plugin identity is `dev.coderig.rider`, version `0.1.0`, targeting Rider 2026.2.
- One visible-file request renders both Code Vision text and a true gutter mark from the same semantic row.
- Rider never opens the CodeRig SQLite store and never waits synchronously for the resident host.
- Missing, stale, unindexed, and ambiguous host answers fail closed.
- `scripts/build-rider-plugin.ps1` creates `artifacts/rider/CodeRig-0.1.0.zip` and can install the same staged
  bytes into a selected Rider profile.
- The packaged plugin was loaded from the normal Rider 2026.2 profile and projected 34 exact method rows into
  68 UI highlightings without CodeRig registration or rendering errors.

## Product boundary still open

- Replace nearest `.git` / `.rig` host discovery with explicit solution-to-host association.
- Carry project/compilation identity so linked and multi-target files need not fail as ambiguous.
- Add an automated Rider SDK daemon test for Code Vision and gutter registration.
- Decide the first interaction: a lazy witness path is preferable to transporting paths for every method.
- Move the backend source out of `experiments/` once compatibility policy and release cadence are chosen.
