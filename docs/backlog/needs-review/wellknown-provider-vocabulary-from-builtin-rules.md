# F7 — core still couples to builtin-GENERIC provider names in five places

**Status:** todo · **Priority: LOW** (the parent card scoped this out deliberately) · **Family:** core purity
**Extracted from:** [core-purity-project-vocabulary](../done/core-purity-project-vocabulary.md) (finding F7), 2026-09-02
**Triage:** needs-info (a judgment call: it trades a real coupling for a level of indirection)

## The finding, quoted from the audit

F7: core logic is coupled to builtin-generic provider names —

- `lock` / `acquire` / `release` — `TreeRenderer` held-lock rendering;
- `{lock, async_lock}` — `ImpactEngine` guards;
- `shared_state` — `race_window` read/write providers;
- `async_block` — `sync_over_async` in `FactHazardDeriver`.

These differ from F1–F6: they are **builtin-generic** vocabulary rather than one project's stack, and some of
it (`alloc`, `throw`, `iteration:fanout`) is emitted BY core, so it is legitimately core's own. That is why
[core-purity-project-vocabulary](../done/core-purity-project-vocabulary.md) marked F7 out of scope and left it
"as a judgment call for a later pass".

## The proposed shape

A `wellKnownProviders` section in the builtin rules JSON, projected into core — the same mechanism F1–F6 used
(vocabulary in rules data, generic operator in core, absent section degrades neutrally and never falls back
to a literal).

## The cost that makes it a judgment call

Five call sites gain a level of indirection for a coupling that is arguably core's own. Low urgency.

## What counts as finishing

- A decision recorded either way. If built: the new section ships with a **cascade-survival test** —
  `RuleSetLoader.MergeObservations`/`Merge` enumerate sections explicitly, so a section not added there is
  silently dropped the moment an overlay declares its parent key, and four features have already hit that
  trap (pattern: `Amplification_categories_survive_the_cascade_merge`).
- The two remaining hardcodes named by the amplify sweep are the same family and were fixed as F1/F3
  (`EffectDerivation.cs:257`, `FactHazardDeriver.cs:333-335`) — do not re-open them here.
