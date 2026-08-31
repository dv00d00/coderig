# One unidentified flaky test in the suite

**Status:** todo · **Found:** 2026-08-31, immediately after the Rider-plugin merge · **Family:** test infra / mini-ci

## What happens

One test out of 1221 failed on a mini-ci run right after the Rider-plugin merge. The rerun was 1221/1221
green. The failing test's name was lost because the test report from the failing run was overwritten by the
rerun's report before anyone looked at it — `scripts/mini-ci.ps1` invokes `dotnet test` for
`tests/Rig.Tests`, `tests/Rig.IntegrationTests`, and `tests/Rig.LiveIntegrationTests` (lines 41, 47, 52) with
no `--report-trx`/output-path argument, so each run writes to the same default location and a rerun clobbers
the prior run's report.

Nothing else is known about the failure. Do not speculate about which test or which project — there is no
surviving evidence to speculate from.

## Fix

Preserve per-run test reports so the next occurrence is identifiable instead of lost: give each `dotnet test`
invocation in `scripts/mini-ci.ps1` a timestamped (or run-id-suffixed) report output path, so a failing run's
report survives even when a rerun immediately follows it.
