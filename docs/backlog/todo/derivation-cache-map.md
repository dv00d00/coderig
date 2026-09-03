# Caching and live derivation — wayfinder

**Status:** wayfinder map · 6 children, two terminal and four remain · **Opened:** 2026-09-02 ·
**Family:** performance / query cache / live index

## Shared root cause

A derived artifact is rebuilt **per query** instead of once per generation or once per store. Each child is one
artifact caught doing that — the whole-store EP set, the cross-method correlation, the live EP memos, the graph
index, and the now-shipped warm shaped graph — and in every case the cheaper scope already exists somewhere in the
program (`IQueryArtifactCache`, `LiveFactSource.ArtifactMemo`, `QueryCacheKeys`).

## Children, in dependency order

1. [Memoize the event-handoff rewrite](./derivation-cache-4-event-handoff-rewrite-breaks-the-graph-index-memo-across-queries.md)
   — first implementation ticket: one generation hands every query the same graph object, so the index memo
   survives a query boundary.
2. [Cache the `/api/hazards` cross-method correlation](./derivation-cache-2-cross-method-hazards-cache.md) —
   same shape as the hazard-effects cache that already exists beside it.
3. [Move the live EP memos onto `LiveFactSource`](./derivation-cache-3-live-ep-derivation-is-per-query-not-per-generation.md)
   — per-generation and, more importantly, visible in `BuildTimes`.
4. [Route the two `Services/*` EP consumers through the existing cache](../done/derivation-cache-1-ep-derivation-uncached-outside-callers.md)
   — closed 2026-09-02, found already fixed by inspection: both consumers already route through the cache, no new key, no new schema axis.
5. [Baseline a `rig serve` query batch](./derivation-cache-6-rig-serve-batch-baseline.md) — the measurement
   that decides the one below; only meaningful once 1 has landed.
6. [Warm the shaped graph across queries in `rig serve`](../done/derivation-cache-5-warm-graph-across-queries.md) —
   shipped bounded process cache; the baseline above now decides whether to keep and productionise it.

## Already measured

- Whole-store EP derivation: **3.5-3.9s per call**. `callers --entrypoints` went **3.6s → 0.07s** and 1.5 GB of
  disk reads → 0 once keyed on `EpRecordsCacheKey`; four other callers still pay it in full, the sharp pair
  being `/api/callers?entrypoints` and the web EP listing.
- `/api/hazards` cross-method correlation: `LoadInvocationRefsAsync` over ~2.4M rows, **~30-60s per request**,
  observed live 2026-08-04, uncached.
- Live EP derivation rebuilds the whole call graph from ~2.4M refs per query; the equivalent artifact
  (`traversalGraph`) measures **2.3-3.3s**, and it is absent from `BuildTimes`, so no instrument shows it and
  the background warmer cannot warm it.
- Graph-index memoization on graph identity already pays: repeat traversal `BuildTree` 382 → 212 ms,
  `ReachedBy` 523 → 21 ms. `MarkEventSubscriptionHandoffs` then mints a new `FactGraphData` per query, worth
  ~0.4-0.5s of CPU per live query on MedDBase.
- One-shot reverse query floor ~8s, of which one `--time` run attributed 5.7s and 1.5 GB of disk reads to the
  graph load alone; a 35-query review batch spent ~5.2 minutes re-materializing the same graph. One warm
  MedDBase graph measured ~1.7 GB.

## Decided — and what is only recommended

Cards are referenced by name below, because the list above is now ordered rather than numbered by card.

**Settled by reading the code, not by preference:**

- The warm-graph card subsumes NOTHING. A resident graph is a graph-LOADING optimisation, so it never
  computes the `/api/hazards` cross-method correlation and never derives entry points — those two cards
  survive any outcome for it.
- The event-handoff memo is the warm-graph card's PRECONDITION: `MarkEventSubscriptionHandoffs` mints a new
  `FactGraphData` object per query, so a resident graph would still rebuild its traversal index every query
  and would measure as a partial win for a reason unrelated to residency.

**Recommended, still Dmytro's call:** land the event-handoff memo, then the hazards correlation cache, then the
live EP memos; run the batch baseline after the event-handoff memo to decide whether the already-shipped warm
graph should be kept and productionised. The EP consumer routing and warm graph are already terminal records.
- Each child's own open questions live on that child's card, not here.
