# `tree --full` suppresses a distinct call leaf that shares a line with an effect

**Status:** todo · **Found:** 2026-09-02 by inspection · **Family:** CLI rendering / extraction grain
**Blocked by:** [call-site facts carry no column](./call-site-facts-no-column-same-line-calls-collapse.md)
**Triage:** needs-info (nothing to implement until the column fact lands)

## What happens

`rig tree --full` silently drops real call leaves. A library call that happens to sit on the same source line
as an effect is discarded as "already shown as an effect leaf", even when it is a genuinely different call.

## Where

| anchor | what it does |
| --- | --- |
| `src/Rig.Cli/Commands/TreeCommand.cs:693` | builds `effectSites` as `(EnclosingSymbolId, Line)` pairs — no column |
| `src/Rig.Cli/Commands/TreeCommand.cs:707` | filters a library call out when `effectSites.Contains((c.Enclosing, c.Line))` |
| `src/Rig.Cli/Commands/TreeCommand.cs:708` | compounds it with `.DistinctBy(c => (c.Enclosing, c.Target, c.Line))` |

Both keys are column-blind, so `Use(Read(), Fetch())` collapses: whichever of the two the effect owns
suppresses the other. The `DistinctBy` then removes any remaining same-target duplicate on the line.

The consequence is a false negative in the one view whose whole purpose is to show everything: a caller
reading `tree --full` sees fewer leaves than the code performs, with no disclosure that anything was dropped.

## Why it is not fixable in this file

The suppression is correct in intent — an effect leaf and its own call leaf are the same call, and showing
both is noise. It is only the KEY that is wrong, and the key cannot be widened because the facts carry no
column. Adding a column is an extraction change with a store schema bump and a full reindex, which is
[its own card](./call-site-facts-no-column-same-line-calls-collapse.md) and is in flight.

So this is a FOLLOW-ON, not an independent fix. Once call-site facts carry a column, both keys at `:707` and
`:708` widen to include it and the suppression becomes exact.

## Testing expectations

- A fixture with two distinct effectful calls on one line, one of which is an effect leaf: `tree --full`
  renders both.
- A fixture with the same target called twice on one line: `tree --full` renders two leaves, not one.
- Both tests are expected to FAIL until the column fact exists; land them with the column change, not before.

## Related

- [Call-site facts carry no column](./call-site-facts-no-column-same-line-calls-collapse.md) — the blocking
  extraction change. Same root cause, different surface: that card's symptom is in the Rider plugin's
  line-only match, this one's is in `tree --full`.
