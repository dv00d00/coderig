# Rider plugin — move the backend source out of `experiments/` once compatibility policy and cadence are chosen

> **PARKED 2026-09-02** - the Rider plugin experiment is deprioritised in favour of the web view, by the product owner's explicit decision. Reopen if that decision reverses.

**Status:** todo · **Family:** rider plugin / release boundary
**Extracted from:** [rider-plugin-minimal-product](../done/rider-plugin-minimal-product.md) (open boundary bullet), 2026-09-02
**Triage:** needs-info (a compatibility-policy and release-cadence decision, not an implementation task)

## The item

The plugin's backend source still lives under `experiments/`. The parent card gates the move on two
decisions being taken first: **compatibility policy** and **release cadence**.

## What already shipped

Plugin identity `dev.coderig.rider` 0.4.0 targeting Rider 2026.2, a build script producing
`artifacts/rider/CodeRig-0.4.0.zip` that can install the same staged bytes into a selected Rider profile, and
a JVM frontend with a status-bar indicator (exact / stale / missing / restarting / error) plus refresh and
graceful restart from the indicator, Tools menu and Find Action. Record:
[rider-plugin-minimal-product](../done/rider-plugin-minimal-product.md).

## The decisions to take first

- **Compatibility policy:** which Rider versions a released plugin claims. Today it targets one (2026.2),
  and the JetBrains platform breaks API across majors, so "supported range" is a commitment, not a manifest
  field.
- **Release cadence:** whether the plugin versions with `rig` (the host protocol is shared, so a mismatched
  pair is a real failure mode the frontend already has states for) or independently.

## What counts as finishing

- Both decisions recorded.
- The backend source relocated out of `experiments/`, with the build script and the packaged artifact
  unchanged in behaviour.
- The host-protocol compatibility expectation stated somewhere a user can read, since the frontend already
  distinguishes stale from missing from error and those states become the contract.
