# `rig watch` timing status used the current locale

**Status:** done 2026-09-03 · **Family:** live host / output fidelity

## Defect

The live reconcile and status lines interpolated elapsed seconds with the current culture. Under a decimal-comma
locale they emitted `0,03s`, while the CLI contract and its live integration test expect stable dotted decimals.
The same issue existed in cold-boot and last-edit timings.

## Fix

All three `WatchCommand` timing lines now use invariant interpolation. This is presentation-only: scheduling,
reconciliation, and stored facts are unchanged.

## Verification

`WatchCommandTests` passed 8/8 in the live integration lane under the locale that previously reproduced the comma.
