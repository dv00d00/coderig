# Rider plugin — minimal local product

## Goal

Ship the validated file-effect read model as a reproducible, locally installable Rider plugin before adding
interaction or configuration surface.

## Shipped slice

- Plugin identity is `dev.coderig.rider`, version `0.4.0`, targeting Rider 2026.2.
- One visible-file request renders Code Vision, a true gutter mark, and an inline call-site hint from the same
  semantic rows.
- SQL and file-system effects are separate read-model families. A method can render both, with Rider's
  database-query and folder glyphs rather than the original recursion placeholder.
- Rider never opens the CodeRig SQLite store and never waits synchronously for the resident host.
- Missing, stale, unindexed, and ambiguous host answers fail closed.
- A JVM frontend adds a status-bar indicator for exact/stale/missing/restarting/error and exposes refresh and
  graceful restart through the indicator, Tools menu, and Find Action.
- Restart is acknowledged over the existing current-user pipe before the old host shuts down; the frontend
  waits for that host's returned PID to exit, then starts `rig watch` and writes its output to
  `.rig/rider-watch.log`.
- `scripts/build-rider-plugin.ps1` creates `artifacts/rider/CodeRig-0.4.0.zip` and can install the same staged
  bytes into a selected Rider profile.
- The packaged plugin was loaded from the normal Rider 2026.2 profile. On `CliApplication.cs`, the resident
  host returned four method and four call-site rows spanning both families; the daemon projected 13 UI
  highlightings without CodeRig registration or rendering errors.

## Product boundary still open

- Replace nearest `.git` / `.rig` host discovery with explicit solution-to-host association.
- Carry project/compilation identity so linked and multi-target files need not fail as ambiguous.
- Add an automated Rider SDK daemon test for Code Vision and gutter registration.
- Add a lazy witness-path interaction rather than transporting paths for every method.
- Replace first-root-solution restart discovery with an explicit solution-to-host association.
- Move the backend source out of `experiments/` once compatibility policy and release cadence are chosen.
