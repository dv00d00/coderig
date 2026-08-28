# Core purity — no project vocabulary in rig core

**Status:** PROGRESS — branch `core-purity-fixes`, based at `cb780b68`.

The principle, in the repo's own words (`README.md:98`): "Effects are **rule data** (`rig.rules.json`), not
baked-in code." Restated hardest in `src/Rig.Domain/Functions/AmplificationCategories.cs:9-15`: **no effect
name may appear in rig core C#**. Provider and operation tokens (`llblgen:read`, `actor:tell`,
`entity_cache:read`) are the vocabulary of a particular codebase's RULESET, so a ranking table, a category
grouping, an anchor predicate or a system-class map written in C# bakes one project's domain into the tool.

An audit (2026-08-28, HEAD `83b0b0b0`) catalogued eight findings. This item tracks fixing them.

## Findings

| # | What | Where | State |
|---|------|-------|-------|
| F1 | `cache_coherence` anchor/companion/normalizers hardcode the LLBLGen stack | `Rig.Cli/Effects/EffectDerivation.cs:253-267` | DONE |
| F2 | `cache_coherence` discovery tier hardcodes `entity_cache:read` + `*Cache`/`*Entity` strip | `Rig.Cli/Commands/DeriveCommand.cs:706-740` | DONE |
| F3 | `dual_write` `DefaultSystemClassMap` ships MedDBase providers, unpluggable | `Rig.Domain/Functions/FactHazardDeriver.cs:321-376` | DONE |
| F4 | `rig amplify` ranking table + `actor:tell` constant | `Rig.Cli/Commands/AmplifyCommand.cs` | DONE (`1419ff1a`, pre-branch) |
| F5 | shipped `builtin-rules.json` carries the MedDBase overlay (Echo.Process, `meddbase.echo.spawn`, llblgen/entity_cache, LanguageExt) | `Rig.Cli/builtin-rules.json` | DONE |
| F6 | `event_cycle` hardcodes the `actor_tell` delivery tag + its low-confidence semantics | `Rig.Domain/Functions/FactCycleDeriver.cs:41-47` | DONE |
| F7 | core logic coupled to builtin-GENERIC provider names (`lock`, `shared_state`, `async_block`) | TreeRenderer / ImpactEngine / FactHazardDeriver | OUT OF SCOPE (see below) |
| F8 | skill/docs present MedDBase vocabulary as rig behavior | `.claude/skills/rig/SKILL.md`, `docs/hazards.md` | DONE |

## The mechanism, in every case

A section of `rig.rules.json` carries the vocabulary; core implements only the generic operator over it; an
ABSENT section degrades NEUTRALLY (detector off / empty scope / no weighting) and never falls back to a
built-in literal. A fallback literal is the bug — it makes one project's stack the silent default for every
other codebase.

### The cascade trap (four features have now hit it)

`RuleSetLoader.MergeObservations` (and `Merge`) enumerate sections EXPLICITLY. A new rules section that is not
added there is **silently dropped** the moment any overlay declares its parent key — the feature keeps running
against builtin-only data with no error. Every new section therefore ships with a **cascade-survival test**
(pattern: `Amplification_categories_survive_the_cascade_merge`).

## MedDBase overlay

The vocabulary removed from core lands in the MedDBase ruleset, NOT in the shipped builtin. The relocated
content is accumulated in one overlay file (see the branch's commit messages for the path) to be folded into
`c:/git/meddbase-analysis/rig.rules.json`. Layer it with a second `--rules` until then.

## F7 — deliberately out of scope

`lock`/`acquire`/`release` (TreeRenderer held-lock rendering), `{lock, async_lock}` (ImpactEngine guards),
`shared_state` (race_window read/write providers), `async_block` (sync_over_async) are **builtin-generic**
vocabulary, not project vocabulary — and some of it (`alloc`, `throw`, `iteration:fanout`) is emitted BY core,
so it is legitimately core's own. The tightening would be a `wellKnownProviders` section in the builtin JSON
projected into core; low urgency, and it trades a real coupling for a level of indirection in five places.
Left as a judgment call for a later pass.
