# Web Impact still labels raw `perEp.length` as behavioral in two surfaces

**Status:** todo · **Family:** web impact / vocabulary parity
**Triage:** ready-for-agent
**Extracted from:** [the shipped shared Impact selection](../done/cli-web-collapse-1-impact-selection-into-the-engine.md),
2026-09-03.

## Problem

`ImpactResponseDto.BehavioralEpCount` now carries the shared engine's behavioral definition, and the summary
uses it. Two web strings still call raw `perEp.length` behavioral:

- `src/Rig.Cli/wwwroot/main.js` reports `impact: N behavioral change(s)` from `d.perEp.length`;
- `src/Rig.Cli/wwwroot/components.js` labels a filtered `perEp` list as EPs with a behavioral effect change.

`perEp` deliberately retains hazard-, amplification-, and guard-only rows, so its length means “EPs with any
delta”, not “behavioral EPs”. The shared selection fix made that distinction explicit; these strings erase it.

## Acceptance

- Every web behavioral count reads `behavioralEpCount`.
- Every raw/filtered `perEp.length` count is labelled “with any delta” (or equivalent), never behavioral.
- A fixture with one hazard-only EP pins the distinction in both surfaces.
- Render-only wording/count selection owes no `*Schema` bump.

## Out of scope

- Changing which EPs the engine retains.
- Re-attributing the historical `echoactor …Inbox` row.
