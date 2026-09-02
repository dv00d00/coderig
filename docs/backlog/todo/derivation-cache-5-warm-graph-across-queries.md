# Warm graph across queries in `rig serve` (bounded, demand-gated)

**Status:** TODO / DESIGN-GATED · **Found:** 2026-06-26 from `callers --time` measurements · **Family:** performance

**Blocked by:** [Baseline a `rig serve` query batch](./derivation-cache-6-rig-serve-batch-baseline.md) — the
measurements below are from 2026-06-26 and predate later graph and storage changes; that card captures the
current batch cost, and its result decides whether this one proceeds at all.

## Measured problem

On the MedDBase store, a one-shot reverse query paid an approximately 8-second floor; one representative
`--time` run attributed 5.7 seconds and 1.5 GB of disk reads entirely to graph loading, with traversal itself
negligible. A 35-query review batch therefore spent about 5.2 minutes repeatedly materializing the same graph.

The companion [dispatch calibration](../done/dispatch-precision-substrate.md) showed why this is not a
cheap SQL problem: dispatch expands a representative bounded closure from 157 to 41,626 methods. Even after
deferring effect inputs, the recursive edge walk retained an approximately 4.7-second / 1.1-GB floor. A static
connection does not help, and the additive `~mono` persistence proposal was rejected.

## Current direction

Keep one-shot CLI commands stateless. If repeated-query demand justifies state, reuse a shaped graph inside the
existing, explicitly started `rig serve` process. Do not introduce an MCP server or a forever daemon for this
feature.

The cache must be bounded:

- Default to one warm store; an optional small LRU needs an explicit memory budget.
- Key by the same store identity and rule fingerprint used by query caches.
- Detect store replacement/reindex and rules changes before every lookup; evict rather than answer stale.
- Release the graph when `rig serve` exits; an idle eviction may reclaim it sooner.
- Share one immutable shaped graph safely across concurrent queries; keep per-query traversal state separate.

At MedDBase scale one warm graph was measured near 1.7 GB, so multi-store retention is an opt-in cost, never an
unbounded dictionary.

## What this card does NOT subsume

A resident graph is a graph-LOADING optimisation, so this card subsumes **nothing** else in the caching
family. It never computes the cross-method amplification correlation and it never derives entry points, so
[the `/api/hazards` cache](./derivation-cache-2-cross-method-hazards-cache.md) and
[the live EP memos](./derivation-cache-3-live-ep-derivation-is-per-query-not-per-generation.md) are separate
artifacts that survive any outcome here.

[Memoizing the event-handoff rewrite](./derivation-cache-4-event-handoff-rewrite-breaks-the-graph-index-memo-across-queries.md)
is this card's **precondition**, not its subordinate: `MarkEventSubscriptionHandoffs` returns
`graph with { CallEdges = rewritten }` at all seven traversal entry points, so a resident graph handed
through it yields a fresh `FactGraphData` per query and would rebuild its traversal index every query anyway.

Warm-versus-lazy is a per-artifact call made on the measured number, following the `hazardEffects` precedent
(3.4s, deliberately left lazy). Nothing here presumes a warmed artifact.

## Acceptance

- Query 1 may pay the normal graph load; identical queries 2..N reuse it without re-reading the graph.
- Reindexing, switching stores, or changing rules produces a miss and a fresh graph.
- Concurrent queries are output-equivalent to fresh-process CLI queries.
- Memory stays within the configured single-store/LRU bound and is released on eviction or process exit.
- MedDBase A/B reports cold latency, warm latency, retained memory, and break-even query count.
