# `tree --full` suppresses a distinct call leaf that shares a line with an effect

**Status:** todo · **Found:** 2026-09-02 by inspection · **Family:** CLI rendering / extraction grain
**Depends on shipped substrate:** [call-site column facts](../done/call-site-facts-no-column-same-line-calls-collapse.md)
**Triage:** ready-for-agent

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
both is noise. It is only the KEY that was wrong. The blocking column fact shipped with store schema v8 in
[its terminal card](../done/call-site-facts-no-column-same-line-calls-collapse.md), so the two keys can now be
widened exactly.

This is now an unblocked renderer follow-on: both keys at `:707` and `:708` widen to include the column and
the suppression becomes exact.

## Testing expectations

- A fixture with two distinct effectful calls on one line, one of which is an effect leaf: `tree --full`
  renders both.
- A fixture with the same target called twice on one line: `tree --full` renders two leaves, not one.
- Both tests must pass against a schema-v8 store fixture.

## Related

- [Call-site column facts](../done/call-site-facts-no-column-same-line-calls-collapse.md) — the shipped
  extraction substrate. This card owns the renderer keys and their regression fixtures.
