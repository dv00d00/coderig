# `impact` is unusable without external filtering — 68k rows, `alloc:*` dominates, no `--only`

**Status:** ✅ SHIPPED 2026-07-27 — all five asks, plus the axis was generalised beyond `impact` (see
*Resolution*). **Original status:** todo · **Priority: HIGH** (the flagship review command needs `awk` to be readable; agents truncate output and silently lose the signal) · **Found:** 2026-07-27 (reviewing MedDBase MR !11025) · **Family:** CLI / impact / output-volume
**Related:** [[impact-base-store-double-load]]

## The observation

A 33-file MR (9 commits, merge-base `e8858aa90e02` → head `de69fd2ff`) produced **68,261 TSV rows**:

| rows | type |
|---:|---|
| 25,496 | `ep_reach_-` |
| 14,680 | `ep_reach_+` |
| 9,152 | `ep_reach_inplace` |
| 6,194 | `ep_effect_removed` |
| 5,268 | `affected_ep` |
| 5,041 | `ep_effect_added` |
| 1,556 | `ep_effect_amplified` |
| 533 | `ep_delta` |
| 337 | `ep_reach_~` |
| **1** | `ep_guard_delta` |

The **entire actionable content** for a review is a few dozen rows: which EPs gained/lost
`object_store` / `llblgen:write` / `audit` / `permission`, plus the single `ep_guard_delta`. Everything else
is `alloc:boxing` / `alloc:object` churn from LLBLGen `InitClassMembers` collections and per-EP reach lists.

### Concrete failure this caused

First run was piped through `Select-Object -First 300` (a normal agent reflex against unbounded output).
297 of those 300 lines were `ep_reach_+` rows for **one** EP (`AppStartupProcesses.Startup`), so the capture
contained **zero** effect-delta rows and the diff read as "no behavioural change". The real answer — 27 EPs
newly reaching a DFS read, 190 EPs newly writing a new table — was past the cut. Silent, and the kind of
mistake the tool should make impossible.

## Asks

1. **`--only` / `--exclude` on `impact`**, same token grammar as `tree`/`reaches`, including the documented
   **`@parity` preset** (`permission`, `llblgen:write`, `llblgen:bulk_write`, `llblgen:delete`, `audit`).
   `effects-diff` already has this; `impact` — the command actually used for MR review — does not.
2. **Drop `alloc:*` from `impact` by default** (opt back in with `--only alloc`). Allocation sites are a
   perf lens, not a behavioural delta; they are ~80% of the rows and never change a review verdict.
3. **Separate the structural layer from the behavioural layer.** `ep_reach_±` at 49k rows should be behind
   `--structural` (which today only expands a *summary*), leaving the default output = `affected_ep` +
   `ep_effect_*` + `ep_guard_delta`.
4. **Lead with a machine-readable summary line** so a truncated read still tells the truth, e.g.
   `impact_summary  eps=5268  effect_added=5041  effect_removed=6194  guard_delta=1  parity_rows=63`.
5. **Order rows by signal, not by EP name** — parity-relevant deltas first. Truncation then degrades
   gracefully instead of catastrophically.

## Worked reference (what the useful output looked like, after `awk`)

```
# 27 distinct EPs newly reach a DFS read — none of them obvious from the diff:
Pathways/Components/DocumentReview.ScrollTabsLeft      object_store read  DFS
Pathways/Components/DocumentReview.ScrollTabsRight     object_store read  DFS
Workflows/ReferralShared/Collaboration.RefreshPreview  object_store read  DFS
Workflows/ReferralIncomming/Stages/WriteDischargeDetail.OnDocumentAttached  object_store read  DFS
# 190 distinct EPs newly perform  llblgen:write DocumentHistoryEntity
# 1 ep_guard_delta: PersonModelTransactions.Inbox  +lock:acquire,lock:release
```

That is a 4-line reviewer-grade answer buried in 68k rows.

---

## Resolution (2026-07-27)

All five asks shipped. The framing changed during design: what looked like an `impact` output problem is a
WHOLE-SURFACE default problem, so the fix landed one axis below this item.

### The reframe — `alloc`/`throw` are 91.3% of ALL effects, not just of impact's rows

Counted on the MedDBase store: **243,391 `alloc` + 79,508 `throw` vs ~30,619 for the other 49 providers
combined.** They scale with code VOLUME (every `new`, every `throw`); the other 49 scale with what the code
actually talks to. That is a structural distinction, not a taste one, which is what made it safe to build a
default on — and it means `derive`/`reaches`/`tree` had the same 1:10 signal-to-noise, not just `impact`.

So instead of an `impact`-local `--exclude alloc`, the two providers are now **hidden by default everywhere**
and restored by `--intrinsic` (naming one in `--only` implies it). Deliberately NOT a profile system: one
closed, hand-picked set of two, defined in rig rather than in rules. See `EffectDerivation.IntrinsicProviders`
for why the membership is closed (a "syntax-derived" predicate would also capture `shared_state`, which the
concurrency detectors need).

### Asks, one by one

1. ✅ **`--only`/`--exclude` on `impact`** — same token grammar as `tree`/`reaches`, with unknown-token
   warnings (a typo'd `--only llbgen:write` would otherwise filter everything out and read as "no behavioural
   change" — the same silent false negative this item is about). Render-side, so no `ImpactSchema` bump and no
   cache fragmentation across filter combos.
2. ✅ **`alloc:*` dropped by default** — via the intrinsic axis above, not an impact-local rule.
3. ✅ **Structural layer separated** — per-symbol `ep_reach_*` now require `--structural`, which was previously
   a **no-op for `--format tsv`**. `affected_ep` keeps the aggregate counts.
4. ✅ **`impact_summary` is row 1**, never capped by `--limit`, carrying `eps`/`behavioral_eps`/`effect_added`/
   `effect_removed`/`effect_amplified`/`guard_delta`/`intrinsic_hidden`.
5. ✅ **Ordered by signal** — summaries → behavioural deltas → structural roster, so truncation loses the
   least important rows first. Previously the effect deltas were emitted LAST, behind up to 49k reach rows,
   which is precisely how a 300-line capture ended up containing zero of them.

### Measured on the MR from the report (`e8858aa90e02-dirty` → `de69fd2ffc6b-dirty`)

| | before | after |
|---|---:|---:|
| total rows | 68,261 | **7,959** (−88%) |
| `ep_effect_added` | 5,041 | 1,237 |
| `ep_effect_removed` | 6,194 | 844 |
| `ep_effect_amplified` | 1,556 | 73 |
| `ep_reach_*` | 49,328 | 0 (behind `--structural`) |

`head -2` now yields the complete uncapped totals, so the original failure — a `Select-Object -First 300`
capture whose 300 lines were all `ep_reach_+` for ONE entry point — is structurally impossible.

Also validated on `reaches`: one EP went from 7,125 direct effects (top three rows `alloc object` 4,755,
`throw raise` 1,359, `alloc boxing` 479 — the first real effect buried under 6,593) to **471**, all meaningful.

### Decisions worth keeping

- **The `--expect-no-effect-change` gate counts the FILTERED set**, so what a reviewer reads and what CI
  decides can never disagree. Consequence, accepted deliberately: an alloc-only MR no longer trips the gate.
  `impact_summary.intrinsic_hidden` is the audit trail for that loosening.
- **No `@parity` preset.** The item asked for "the documented `@parity` preset", but it was never implemented
  for ANY command. It stays unimplemented: which effects count as behaviour-relevant is repo DOMAIN policy, so
  the token list lives in the rig skill (`--only permission,llblgen:write,llblgen:bulk_write,llblgen:delete,audit`)
  rather than hard-coded into a general-purpose CLI. `rig.rules.json` has no named-set concept, so that is not
  a cheaper home either — if it ever earns first-class status, a named filter set in the rules is the place.
- **The intrinsic disclosure is UNQUANTIFIED** in human output. It first carried the withheld count, but that
  count is over the derived effect set while `reaches`/`tree` display only what survives a reachability join —
  on a real seed it read "57,513 hidden" beside a visible drop of 6,654. An 8× overstatement in a safety
  disclosure is worse than no number; the exact figure lives in `impact_summary.intrinsic_hidden`.

### Known consequence, filed separately

Hiding intrinsic effects PRUNES paths in the pretty renderer, which takes guarded edges with them: on
`PersonEventEntity.Save`, `tree --guards` shows **42** guarded edges by default vs **73** under `--intrinsic`.
`tree --guards` now warns when an effect filter is active. The stronger fix (retain guarded edges rather than
prune) is item 6 of [[cli-surface-and-help-refresh-2026-07]].
