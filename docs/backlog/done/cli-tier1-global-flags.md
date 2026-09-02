## CLI Tier-1 global flags — uniform across all query commands

**Status:** PROGRESS — `--format`, `--limit`, the `--max-depth` alias, and `impact --time` are shipped. Remaining work is
`--time` on `derive`/`entrypoints` and `--no-cache` on the cached derive/entry-point paths.
**Source:** extracted from `docs/rig-review-issues.md`, 2026-06-25 (#10 / E2 Tier-1 deferred section)

### Shipped slices (verified against code 2026-07-02)

- **`--format text|tsv` ✅ SHIPPED (`94bfd886`, 2026-06-16)** — on *every* query command via
  `CommonOptions.Format()` (callers/path/reaches/tree/dispatch-fans/effects-diff/derive/entrypoints/
  impact/dead), with real TSV emitters (e.g. `PathCommand.cs:154-168`, `CallersCommand.cs:339-350`).
  NOTE: this had already shipped when the card was written 2026-06-25 — the card's "exists ad-hoc on
  3 of N commands" was a stale snapshot.
- **`--limit` ✅ SHIPPED** — `94bfd886` put it (with truncation footers) on `callers`
  (`CallersCommand.cs:57`, footers `:266`, `:365`) and `reaches` (`ReachesCommand.cs:33`), plus
  entrypoints/impact/derive/dead/facts. `tree --limit` shipped 2026-07-02: bounds tree NODES via
  `BuildTree`'s existing `maxNodes` budget (absent = the 50k safety cap, NOT unbounded — deliberate
  divergence from the flat listings); the node hitting the cap renders `⋯elided` / `budget-capped`;
  the limit is part of `TreeCacheKey` (a capped forest is a different tree, not a rendering). Tests:
  `TreeNodeBudgetTests` (incl. the fencepost: budget N fully expands N−1 nodes — the final unit's
  node is conservatively capped).
- **agent-facing discovery + depth spelling ✅ SHIPPED (2026-08-23)** — the centralized depth option now
  accepts preferred `--max-depth` plus compatible `--maxdepth` / `--depth`; `symbols --format tsv|json`
  exposes exact IDs, signatures and source locations; and ambiguous `show` fails closed unless `--all` is
  explicit. The remaining raw fact readers (`refs`/`files`/`di`) are still text-only.
- **`--time` ✅ PARTIAL (`6a713836`, 2026-06-26)** — rich index-style instrumentation (per-phase
  `PhaseTimings` + OS/proc CPU/disk/RAM sampler + `TimingReport`, via a disposable `QueryTiming` helper)
  on `tree`/`callers`/`reaches`/`path`/`dispatch-fans`/`effects-diff`. It paid off immediately —
  attributed the reverse-query ~8s floor to **graph load (disk-IO, 1.5 GB read/query, CPU-idle), not
  traversal** (see [derivation-cache-5-warm-graph-across-queries.md](../todo/derivation-cache-5-warm-graph-across-queries.md)).
- **`impact --time` ✅ SHIPPED (`d2c71d1b`, 2026-07-06)** — uses the same `QueryTiming`/
  `TimingReport` model and telemetry CSV as indexing. The finer phase split remains separately tracked in
  [web timing unification](aaa-web-timing-unification-ui.md).
  **Still absent on `derive`/`entrypoints`.**

### Remaining work

- `--time` on `derive`/`entrypoints` — the `QueryTiming` helper exists; additive wiring.
- `--no-cache` — today exposed only by `tree` and `impact`, while effect and entry-point derivation also use
  `QueryCache`. Thread an opt-out through those command paths; commands with no cache need no flag.

The broader E2 flag-surface audit (dead aliases, mode-group validation, rename deprecations) was DONE
2026-06-14. This item is specifically the **Tier-1 generalization** that was explicitly deferred as
"additive, can land incrementally" (register: E2 Deferred section, `docs/rig-review-issues.md:165-168`).

### Effort

Additive per-command plumbing. Each flag can land independently. No breaking changes. No re-mine.

---

## 2026-07-27 — audit findings from the CLI-surface sweep

A per-command audit of `--rules` / `--store` / `--format` (for
[[cli-surface-and-help-refresh-2026-07]] item 1) produced two things for this card:

- **NEXT SLICE: the remaining FACT READERS have no `--format`.** `symbols --format tsv|json` shipped
  2026-08-23, so exact agent/script discovery no longer parses human output. `refs` / `files` / `di` remain
  text-only; add formats only when a concrete machine workflow needs them.
  The full matrix is documented in the rig skill's REFERENCE.md § "Which commands accept which global options".
- **`--rules` is deliberately NOT going global.** Only commands whose OUTPUT IS A FUNCTION OF THE RULES accept
  it; the fact readers ignore rules entirely, so a no-op `--rules` there would be WORSE than the current
  `Unrecognized command or argument` — it would imply rules shaped a result they never touched. Recording it
  here so a future "make Tier-1 flags uniform" pass does not treat it as an oversight to fix.
- Minor staleness: the shipped-slice list names `dead` as carrying `--format`. `dead` is now a registered
  DISABLED stub (it ran on the all-hops dispatch superset the one-hop engine no longer matches).

## Remainder extracted

Moved `progress/` -> `done/` on 2026-09-02 when `progress/` was unbundled into a shipped record plus its
tail. Everything above is unchanged. The open items now live on their own cards:

- [`--time` on `derive`/`entrypoints`, and `--no-cache` on the cached derive/EP paths](../needs-review/cli-tier1-time-and-no-cache-on-derive-and-entrypoints.md)
  — held in `needs-review/`; the value of both flags is not agreed.
- [`refs`/`files`/`di` still have no `--format`](../needs-review/fact-readers-refs-files-di-have-no-format.md) — this
  card's own "NEXT SLICE", explicitly gated on a concrete machine workflow needing it.

`--rules` is deliberately NOT going global (recorded above), so nothing is extracted for it.
