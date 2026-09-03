# Baseline a `rig serve` query batch before committing to a resident graph

**Status:** todo · measurement, not implementation · **Opened:** 2026-09-02, extracted from the warm-graph
card's inline implementation gate · **Family:** performance / query cache
**Triage:** ready-for-human

**Blocked by:** [Memoize the event-handoff rewrite](./derivation-cache-4-event-handoff-rewrite-breaks-the-graph-index-memo-across-queries.md)

## What to measure

A `rig serve` batch baseline on the MedDBase store at `c:/git/meddbase-analysis`: drive a representative
multi-query review batch at ONE server process and report, per query, the wall time and how much of that
wall time was graph LOADING versus traversal. The load/traversal split is the whole point — a batch total
that does not separate the two cannot answer the decision below.

## Why it exists

The numbers that argue for keeping a shaped graph resident are from **2026-06-26** and predate later graph
and storage changes:

- a one-shot reverse-query floor of ~**8s**;
- one `--time` run attributing **5.7s** and **1.5 GB** of disk reads to the graph load alone, with traversal
  itself negligible;
- a **35-query** review batch spending ~**5.2 minutes** re-materializing the same graph;
- one warm MedDBase graph measured at ~**1.7 GB**.

Retaining a graph that size is a real memory commitment, and it should not be spent on 2026-06 evidence.

## The decision it feeds

Keep and productionise the shipped
[warm graph across queries](../done/derivation-cache-5-warm-graph-across-queries.md) only if BOTH hold on the fresh
numbers:

1. repeated queries still spend most of their wall time in identical graph loads; and
2. the expected review workflow issues enough queries to amortize ~1.7 GB of retained memory.

The alternative on the table is heavy receiver-narrowed dispatch persistence at graph time. That attacks
cold single-shot latency instead of repeat-query latency, and it carries context-sensitive edge/schema
complexity and store blow-up risk. Decide between the two from these measurements, not from the old
additive materialization design.

## Ordering caveat — this baseline is only meaningful after the event-handoff rewrite is memoized

Measure AFTER
[the event-handoff rewrite](./derivation-cache-4-event-handoff-rewrite-breaks-the-graph-index-memo-across-queries.md)
lands. `FactPathFinder.BuildIndex` / `BuildReverseMaps` are memoized on graph OBJECT identity, and
`MarkEventSubscriptionHandoffs` currently returns `graph with { CallEdges = rewritten }` — a new
`FactGraphData` object on every query, at all seven traversal entry points. A batch measured before that fix
charges a resident graph for an index rebuild it would not otherwise pay, so it would understate residency's
benefit for a reason that has nothing to do with residency.

## Out of scope

- Implementing the warm graph, its bound, or its eviction. That stays on the warm-graph card.
- Any change to the traversal path. This card produces numbers only.
