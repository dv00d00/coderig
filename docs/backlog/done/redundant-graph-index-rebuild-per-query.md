# Redundant `GraphIndex` rebuild per traversal — `impact` pays it 6× / run

**Status:** todo — re-verified against code 2026-07-19 · **Found:** 2026-06-28 · **Family:** perf / query-path-redundancy
**Related:** [[derivation-cache-5-warm-graph-across-queries]] (the across-command structural lever) · `perf-redundant-work-per-ep.md` (F1–F9, the already-mined micro-redundancy seam) · fed by [[alloc-effect-detector]] (the detector that would surface this class automatically)

## The finding (CONFIRMED against the code)
`FactPathFinder.BuildIndex(graph)` (`FactPathFinder.GraphIndex.cs:280`) is rebuilt on **every** traversal call — its ~13 callers are essentially the whole query surface (`BuildTree`, `Find`, `Reaches*`, `ReachableFromAll`, `ReachedBy*`, `EntryRootsReaching`, `DispatchFanReport`, `AllDispatchEdges`, `BuildReverseMaps`). Each rebuild does the full adjacency build + four-key sort of every adjacency list + `MethodsByStrippedType`/`ImplsByInterface`/`StrippedBaseEdges`/context-families/mined-dispatch construction.

**The cleanly-fixable redundancy: `rig impact` rebuilds the index 6× per run** over byte-identical graphs.
The current calls are in `ImpactEngine.cs:473/539/616`: `ComputeReachSets` → `ReachesFromEachSeed`,
`ComputeFootprints` → `ReachesInfoFromEachSeed`, and `ComputeHazardSets` → `ReachesFromEachSeed`. Each public
batch method constructs its own private `GraphIndex`, so the same graph pays three builds per side —
**3× × (HEAD + BASE) = 6×** on a cold diff; a cache hit runs none.

## Scope of the fix
- Add a narrow Domain traversal session/API that owns one `GraphIndex` and exposes both set-only and
  `ReachInfo` batch traversals; `GraphIndex` is currently private, so `ImpactEngine` should not construct it.
- Create one session per side in `ImpactEngine` and reuse it for reach sets, footprints, and hazards. The
  index is already safe to share across the parallel per-seed walks (`DescendantsCache` is concurrent).

## What this is NOT
- NOT the across-command cold-graph LOAD (`derivation-cache-5-warm-graph-across-queries.md`) — that's the ~5s/~1.5 GB-disk structural lever and dwarfs this. This card is the *within-`impact`* CPU waste of rebuilding the index over an already-loaded graph.
- NOT the seed micro-redundancies (duplicate graph/EP/rule loads in one command): the investigation confirmed those are **already fixed** (F1–F9 in `perf-redundant-work-per-ep.md`; the "3–4× LoadFactGraphAsync" was mutually-exclusive `⎇` branches, not repeats). No ROI left there.

## Needs measurement
Size the 6× `BuildIndex` vs the 2× graph load on the **MedDBase** graph (via `bench/Rig.Benchmarks` `gcloop` once builds are safe). The graph load likely dominates (per the warm-graph measurement), but a full adjacency-sort + descendant-closure build of a 41k-closure-inflated graph isn't free; structurally it IS 3×-per-side redundant regardless.

## Pre-size hygiene already applied
`BuildIndex` now `EnsureCapacity`s `Adjacency`/`Nodes` (`FactPathFinder.GraphIndex.cs`) — immaterial on rig's tiny self-graph (2443→2449 KB, noise) but scales with graph size; the resize churn was ruled out as the cost, confirming the per-call *content* (the dicts/sorts/lookups) is what repeats — which is what hoisting/caching the index eliminates.

## ✅ FIXED 2026-07-27 — and MEASURED (the answer: real, but the load dominates)

`FactPathFinder.OpenSession(graph)` returns a `TraversalSession` that builds the `GraphIndex` ONCE and serves
both batch shapes (`ReachesFromEachSeed` / `ReachesInfoFromEachSeed`) from it. The one-shot statics now
delegate to a session, so there is a single implementation. `GraphIndex` went `private` -> `internal` purely so
the nested session can take one in its ctor; it is still unreachable from `Rig.Cli`, so `ImpactEngine` holds a
session and cannot construct or inspect an index — the constraint this card asked for.

`ImpactEngine` opens ONE session per side: head serves reach sets + footprints + hazards + the new
guard-condition walk (**4 -> 1**), base serves reach sets + footprints + hazards (**3 -> 1**). So 7 builds per
cold diff became 2. (It was 6 before guard-condition deltas added a fourth head traversal.)

**Measured on the MedDBase pair `4cfb885a244b-dirty` -> `de69fd2ffc6b`, `--no-cache --time`, output verified
BYTE-IDENTICAL (md5 `1e1da581…`, 8,101 rows) before and after:**

| phase | before | after | Δ |
|---|---|---|---|
| head: load graph + derive effects | 33.2s | 33.3s | — |
| head: reach sets + footprints + hazards | 42.5s | **32.8s** | **−23%** |
| base: load + derive + diff | 1m16s | 1m16s | — (within noise) |
| total | 2m31s | **2m22s** | **−6%** |

So an index build is worth roughly 3s on this graph, and the head phase — which is mostly traversal — gave up
9.7s. The base phase did NOT move measurably: ~6s of index saving is inside the noise of a 76s phase dominated
by its 15.3 GB graph load + effect derivation.

**This card's own "Needs measurement" section called it correctly: the graph load dominates.** Two loads read
**~30 GB** of disk (15.1 + 15.3) and peak at 20.3 GB RAM. The remaining lever is therefore
[[derivation-cache-5-warm-graph-across-queries]] (and [[impact-base-store-double-load]], fixed in the same pass, which removed a
duplicate base EP read worth 1.9 GB of disk out of the head phase). Do not expect further wins from
per-query CPU redundancy — that seam is now closed.
