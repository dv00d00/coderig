# `MarkEventSubscriptionHandoffs` mints a new graph per query, so the graph-index memo never survives a query boundary

**Status:** todo · **Priority: MEDIUM-HIGH** (it is the difference between "the resident host builds its
traversal index once per GENERATION" and "once per QUERY" — worth ~0.4-0.5s of pure CPU per live query on
MedDBase, on top of the graph the host already holds) · **Found:** 2026-08-24, while memoizing
`FactPathFinder.BuildIndex` / `BuildReverseMaps` on graph identity · **Family:** live index / performance

## What now works

`FactPathFinder.BuildIndex` and `BuildReverseMaps` are memoized on **graph object identity**
(`ConditionalWeakTable<FactGraphData, …>` in `FactPathFinder.GraphIndex.cs`). Every traversal over the SAME
`FactGraphData` instance reuses the derived adjacency / dispatch maps / reverse maps instead of rebuilding
them. Measured on the MedDBase store (bounded `Startup` graph, 147,672 call edges / 36,840 methods):

| repeat traversal over one graph | before | after |
| --- | ---: | ---: |
| `BuildTree`                     | 382 ms | 212 ms |
| `ReachedBy` (SyncCut)           | 523 ms |  21 ms |
| `ReachedBy` (AsyncExact)        | 458 ms |  21 ms |

## What does not

The live host holds ONE materialized graph per fact generation — `LiveFactSource.TraversalGraph` /
`LiveQueryFactSource.MaterializedGraph` are `Lazy<FactGraphData>`, so every query in a generation is handed
the same object. But every traversal COMMAND then does:

```csharp
// CallersCommand.cs:258, PathCommand.cs:151, TreeCommand.cs:399,
// ReachesQueryService.cs:122, TreeQueryService.cs:160, PathQueryService.cs:83, CallersQueryService.cs:94
graph = FactPathFinder.MarkEventSubscriptionHandoffs(graph, await source.EventSubscriptionSitesAsync());
```

and `MarkEventSubscriptionHandoffs` returns `graph with { CallEdges = rewritten }` whenever it reclassifies
at least one `+=` edge — a **new `FactGraphData` object on every query**. So the memo is warm across the
phases WITHIN one query (reverse closure → async probe → forward verify all share one index now), and
stone cold at the start of the next one, even though the facts have not moved.

`LiveQueryFactSource.DeriveEffectsAsync` already knows about this hazard and works around it by identity-
testing `graph.BaseEdges` instead of the graph ("the graph the command hands back has had
MarkEventSubscriptionHandoffs applied … BaseEdges is the SAME list instance"). That trick does not help the
index memo, which is keyed on the graph itself and legitimately depends on `CallEdges`.

## The fix

Memoize the rewrite, not just its inputs: a `ConditionalWeakTable<FactGraphData, (ISet<EventSubscriptionSite>, FactGraphData)>`
inside `MarkEventSubscriptionHandoffs` (or, equivalently, one more `Lazy<FactGraphData>` on `LiveFactSource`
for "traversal graph, event-marked"). The event-site set is itself a per-generation memo
(`LiveFactSource.EventSubscriptionSites`), so `(graph, eventSites)` is a stable pair per generation and a
reference-equality guard on both is sufficient — the same discipline `DeriveEffectsAsync` already uses.

Then every query in a generation gets the SAME marked graph object, and the index memo becomes what it was
meant to be: **built once per generation**, not once per query.

## Why it was not fixed in the memo change

`FactPathFinder.GraphShaping.cs` (which owns `MarkEventSubscriptionHandoffs`) and all seven command /
query-service call sites were outside the owned-files list for that task, and the fix is a behaviour-visible
sharing change on the live path — it wants its own diff and its own live/store parity run.

## Related

- `docs/backlog/todo/live-ep-derivation-is-per-query-not-per-generation.md` — the same shape of bug (a memo
  that says "per generation" but is scoped to a query) in the EP derivation.
