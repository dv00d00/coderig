# `--time` on `derive`/`entrypoints`, and `--no-cache` on the cached derive/EP paths

**Status:** needs-review — value not agreed; both flags are additive plumbing with no established demand, so
neither scheduled nor declined. · **Family:** CLI surface
**Extracted from:** [cli-tier1-global-flags](../done/cli-tier1-global-flags.md) (its "Remaining work"), 2026-09-02
**Triage:** needs-info

## The two items

- **`--time` on `derive` / `entrypoints`.** The `QueryTiming` helper already exists — rich per-phase
  `PhaseTimings` plus an OS/proc CPU/disk/RAM sampler plus `TimingReport` — and is wired on
  `tree` / `callers` / `reaches` / `path` / `dispatch-fans` / `effects-diff` (`6a713836`), and on `impact`
  (`d2c71d1b`). It is absent on `derive` and `entrypoints`. Additive wiring only.
- **`--no-cache`.** Exposed today by `tree` and `impact` only, while effect and entry-point derivation also
  use `QueryCache`. Threading an opt-out through those command paths is the work; commands with no cache
  need no flag.

## What already shipped

`--format text|tsv` on every query command via `CommonOptions.Format()`, `--limit` with truncation footers
(including `tree --limit` bounding tree NODES through `BuildTree`'s `maxNodes` budget, part of
`TreeCacheKey`), the `--max-depth` spelling with `--maxdepth`/`--depth` compatibility, `symbols --format
tsv|json`, and `impact --time`. Record: [cli-tier1-global-flags](../done/cli-tier1-global-flags.md).

## Why the value is not settled

`--time` on the traversal verbs paid for itself immediately — it attributed the reverse-query ~8s floor to
**graph load (disk IO, 1.5 GB read per query, CPU-idle), not traversal**, which is the finding behind
[warm-graph-across-queries](../done/derivation-cache-5-warm-graph-across-queries.md). Nothing comparable is
outstanding on `derive`/`entrypoints`, and `--no-cache` on those paths is a debugging affordance with no
named consumer. Both are cheap; neither is asked for.

## If it is agreed

- Each flag lands independently; no breaking changes, no re-mine.
- `--no-cache` must bypass the cache, not invalidate it — the same semantics `tree` and `impact` already
  have.
- The rig skill's REFERENCE.md matrix ("Which commands accept which global options") is updated in the same
  change.
