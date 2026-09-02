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
| F4 | `rig amplify` ranking table + `actor:tell` constant | `Rig.Cli/Commands/AmplifyCommand.cs` | DONE (`1419ff1a`, pre-branch) — verified clean; the residual `FireAndForget` bucket identifier renamed to `Separate` |
| F5 | shipped `builtin-rules.json` carries the MedDBase overlay (Echo.Process, `meddbase.echo.spawn`, llblgen/entity_cache, LanguageExt) | `Rig.Cli/builtin-rules.json` | DONE |
| F6 | `event_cycle` hardcodes the `actor_tell` delivery tag + its low-confidence semantics | `Rig.Domain/Functions/FactCycleDeriver.cs:41-47` | DONE |
| F7 | core logic coupled to builtin-GENERIC provider names (`lock`, `shared_state`, `async_block`) | TreeRenderer / ImpactEngine / FactHazardDeriver | OUT OF SCOPE (see below) |
| F8 | skill/docs present MedDBase vocabulary as rig behavior | `.claude/skills/rig/SKILL.md`, `docs/hazards.md` | DONE |

## What each fix added to the rules schema

| Section | Shape | Merge | Absent ⇒ |
|---|---|---|---|
| `cacheCoherence` | `+ anchor`, `+ companion`, `+ anchorStripSuffix`, `+ companionStripSuffix`, `+ discoveryRead {provider, operation, stripSuffix}` | whole object, last writer wins (restate it whole in an overlay) | anchor+companion missing ⇒ detector OFF; `discoveryRead` missing ⇒ declared entities only |
| `dualWrite.systemClassMap` | flat `provider:operation` (or bare `provider`) → system class | **per key** (an overlay ADDS providers; a restated key wins) | dual_write OFF |
| `deliveryRules[n].cycleDelivery` / `.joinConfidence` | `bool` / `"low"\|"high"` | list concat (per rule) | no `event_cycle` findings; a cycleDelivery rule with no joinConfidence = exact join |
| `observations.resourceSpan[n].id` | optional string | **replace by id**, else append | unchanged (append, as before) |

`resourceSpan` needed a new merge mode because `excludeProviders` is a SUPPRESSION list: an appended rule can
never subtract, so both rules would fire and the un-suppressed one would annotate anyway. Replace-by-id is the
only additive-safe way for a project to extend a negative list.

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

## MedDBase overlay — and the ORDER trap

The vocabulary removed from core lands in the MedDBase ruleset, NOT in the shipped builtin. The relocated
content is accumulated in one overlay file to be folded into `c:/git/meddbase-analysis/rig.rules.json`; layer
it with a second `--rules` until then.

**Effect matching is FIRST RULE WINS in cascade order, so the relocated `actor:*` effect rules must be spliced
at the TOP of that file's `effects` list — before its `echo_publish` rules.** They used to load from
builtin-rules.json, i.e. ahead of everything project-side. Appended at the end (which is also what `--rules`
does — extras always load last) they lose every `Echo.Process` tell/ask to `echo_publish`: measured on the real
store, **718 effect rows + 219 cross_method_amplification rows reclassify and 17 actor amplification findings
vanish**. Spliced at the top, `rig derive --format tsv` and `rig amplify` are byte-identical to the pre-change
output.

## Cache invalidation

`GraphHazSchema` 3→4 (cache_coherence + event_cycle are same-input/different-output now). `HazardEffectsSchema`
deliberately NOT bumped: `builtin-rules.json` itself changed in the same commits, and the rules fingerprint is
computed over the loaded rule FILES, so every warm entry misses anyway. The bump is the honest per-artifact
signal for the graph tier regardless.

## Validation

- **Real-store parity (MedDBase, store `2f944e739e47-dirty`)**: `rig derive --format tsv` (18.5 MB) and
  `rig amplify` are **byte-identical** (SHA-256) to the `cb780b68` baseline when the relocated rules are in
  place — the actor effect rules spliced at the top of `rig.rules.json`, everything else layered.
- **Neutral degradation (same store, relocated rules ABSENT)**: exit 0, no crash, no fallback —
  cache_coherence 4→0, dual_write 8→0, n_plus_1 155→10, amplification rows 647→216, event_cycle 24→24 (every
  MedDBase cycle is an exact C#-event one; the heuristic-join arm has fixture coverage only), race_window /
  lazy_init_race / thread_local_context / static_init_capture unchanged.
- **Suite**: `scripts/mini-ci.ps1 -SkipToolInstall` green — csharpier format, build, main suite (1209), shared
  integration (84), 23 independent-integration classes, live integration, pack. The global tool reinstall was
  SKIPPED on purpose: this is a worktree, and the installed `rig` must keep matching `main`.

## F7 — deliberately out of scope

`lock`/`acquire`/`release` (TreeRenderer held-lock rendering), `{lock, async_lock}` (ImpactEngine guards),
`shared_state` (race_window read/write providers), `async_block` (sync_over_async) are **builtin-generic**
vocabulary, not project vocabulary — and some of it (`alloc`, `throw`, `iteration:fanout`) is emitted BY core,
so it is legitimately core's own. The tightening would be a `wellKnownProviders` section in the builtin JSON
projected into core; low urgency, and it trades a real coupling for a level of indirection in five places.
Left as a judgment call for a later pass.

## Remainder extracted

Moved `progress/` -> `done/` on 2026-09-02 when `progress/` was unbundled into a shipped record plus its
tail. Everything above is unchanged. The open items now live on their own cards:

- [Fold the relocated vocabulary into the MedDBase ruleset](../todo/fold-relocated-vocabulary-into-the-meddbase-ruleset.md)
  — carries the first-rule-wins ORDER trap measured above.
- [F7 — well-known provider vocabulary projected from the builtin rules](../needs-review/wellknown-provider-vocabulary-from-builtin-rules.md)
  — this card's own "left as a judgment call for a later pass".
