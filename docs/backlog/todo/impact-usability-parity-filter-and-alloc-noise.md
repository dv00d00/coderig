# `impact` is unusable without external filtering — 68k rows, `alloc:*` dominates, no `--only`

**Status:** todo · **Priority: HIGH** (the flagship review command needs `awk` to be readable; agents truncate output and silently lose the signal) · **Found:** 2026-07-27 (reviewing MedDBase MR !11025) · **Family:** CLI / impact / output-volume
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
