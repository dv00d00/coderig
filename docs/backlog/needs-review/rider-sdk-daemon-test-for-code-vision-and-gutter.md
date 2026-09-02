# Rider plugin — no automated SDK daemon test for Code Vision and gutter registration

> **PARKED 2026-09-02** - the Rider plugin experiment is deprioritised in favour of the web view, by the product owner's explicit decision. Reopen if that decision reverses.

**Status:** todo · **Family:** rider plugin / test coverage
**Extracted from:** [rider-plugin-minimal-product](../done/rider-plugin-minimal-product.md) (open boundary bullet), 2026-09-02
**Triage:** needs-triage

## The gap

Registration and rendering are currently verified **by hand**. The parent card's evidence is a manual
dogfood: the packaged plugin loaded from the normal Rider 2026.2 profile, and on `CliApplication.cs` the
resident host returned four method and four call-site rows spanning both families while the daemon projected
13 UI highlightings with no CodeRig registration or rendering errors.

That is a real check, but it is not repeatable in CI, so a registration regression — the class of failure
that makes the plugin silently render nothing — has no gate.

## What already shipped

The reproducible local product plus `scripts/build-rider-plugin.ps1`, which creates
`artifacts/rider/CodeRig-0.4.0.zip` and can install the same staged bytes into a selected Rider profile.
Record: [rider-plugin-minimal-product](../done/rider-plugin-minimal-product.md).

## What counts as finishing

- An automated Rider SDK daemon test covering **Code Vision and gutter registration**.
- It asserts the same shape the manual dogfood did: method rows and call-site rows from one visible-file
  request, both families, and no registration or rendering error.
- It runs against the staged bytes the build script produces, so what is tested is what ships.
- It does not open the SQLite store and does not wait synchronously for the host — the plugin's two standing
  prohibitions must survive the test harness.
