# `tree`/`reaches` cold cost is one 46k-node CHA closure, reloaded per seed, and `--depth` cannot reduce it

**Status:** todo · **Priority: HIGH** (13-17s per unseen seed in the web UI; the surface reads as broken) ·
**Found:** 2026-09-03, RCA of the web tree lens · **Triage:** ready-for-agent
**Family:** performance / traversal graph loading

## The observation

The web tree lens is ~40-60x slower on an unseen seed than a seen one. Measured through `rig serve` against
store `aae396ea7e8e-dirty`:

| seed | cold | warm | payload |
| --- | --- | --- | --- |
| `AppointmentBookingBase.GetProposedTimeSlots` | (pre-warmed) | 431 / 426 ms | 5.4 MB |
| `MembershipSchemeService.RaiseMembershipSchemeInvoices` | **13.6 s** | 339 ms | 4.3 MB |
| `ReferralControllerBase.SetupReportDeliveryMethod` | **13.4 s** | 259 ms | 123 KB |
| `AppointmentQuestionnaireEntityStaticMetaData.#ctor` | **2.3 s** | — | 89 KB |

Cost tracks neither payload (123 KB costs the same as 4.3 MB) nor `call_edges` reach (577 costs the same as
2,179).

## Root cause

The cost is **the closure, not the walk**, and the closure is the same one every time.

Node closure per seed, over `call_edges` alone versus `call_edges ∪ dispatch_edges` (the receiver-blind CHA
superset the SQL fast path bounds on), against 306,160 store nodes:

| seed | `call_edges` | `+ dispatch_edges` | share of store |
| --- | --- | --- | --- |
| `BuildServicePredicate` | 1,195 | **45,989** | 15% |
| `RaiseMembershipSchemeInvoices` | 2,179 | **45,989** | 15% |
| `SetupReportDeliveryMethod` | 577 | **45,989** | 15% |
| `StaticMetaData.#ctor` | 100 | 100 | 0% |

Three unrelated seeds converge on **exactly 45,989**. That is the signature of one giant strongly-connected
CHA component: reach any interface the data tier dispatches through and you reach all of it, and it reaches
back. Every seed that touches the data tier — which is most real MedDBase code — lands in the same fixed
point and pays to load the same 46k-node subgraph. The `#ctor` seed avoids the component entirely, which is
the whole reason it costs 2.3 s.

**And no query bound can shrink it**, because the closure is computed before the walk ever sees a bound.
`SqlReachability.LoadBoundedGraphAsync` and `LoadReachInputsAsync`
(`src/Rig.Cli/Graph/TraversalGraphLoader.cs:99-105`, `:152-158`) take a pattern and a direction — **no
`maxDepth`, no `maxNodes`**. `--depth` and `--limit` are applied during the in-memory walk, after the load.

Four bisection arms on one seed, all zero-code-change, all confirming it:

| arm | time | reads as |
| --- | --- | --- |
| `reaches` (graph + walk, **no** effects) | 16,390 ms | effect derivation is ~free — it is bounded to the same already-loaded closure |
| `tree` (graph + walk + effects) | 16,134 ms | — |
| `tree --raw` (bypass `ShapeGraph`) | 17,285 ms | shaping is not the cost; it runs after the load |
| `tree --max-generic-work 1000` | 16,065 ms | monomorphization is not the cost; it is inside `ShapeGraph` |
| `tree --depth 3` | 16,143 ms | **the bound is not reaching the closure** |

`--time` cannot see any of this: it reports one row, `compute (graph + BuildTree + effects): 14652 ms`.

## Fixes, in the order they pay

**Order: F3, then F2, then F1.** F2 is the fix that matches how the graph actually looks, but F3 is what
makes its win measurable rather than argued — and F3 is small enough to land first without ceremony.

- **F2 — route the traversal family through `WarmStore`.** `WarmStore` (`src/Rig.Cli/Caching/WarmStore.cs`)
  is already a process-lifetime cache for the whole-store shaped graph (~4.5 s) and the invocation refs, with
  an LRU cap and a `RIG_WARM_CAP` knob. Its consumers are Amplify, Derive, EffectsDiff, FileEffects,
  FileFindings, Hazards and Hotspots — **not** tree, reaches, path or callers, which load through
  `TraversalGraphLoader` (no caching anywhere inside it). The direction is clear — a per-seed "bounded" load
  of the same 46k nodes, paid 13-17 s every time, against one whole-store load amortized over every seed in
  the component, i.e. nearly every real query. Per-seed bounding is a false economy for any seed inside the
  giant component; it only wins for the rare `#ctor`-shaped seed.

  **The one-time cost is NOT verified.** `WarmStore`'s own comment quotes `Reads.LoadShapedGraphAsync` at
  ~4.5 s and `FactGraphData` at ~1.5 GB of disk reads, but that is a quoted figure, not one measured here.
  The only whole-store datapoint from this RCA is `rig hotspots --top 5` cold at **28.8 s**, which does not
  isolate the graph load — it also derives effects across the whole store, and a one-shot CLI process gets
  nothing from a process-lifetime cache. So F3 (below) is a genuine prerequisite: wire the timer split first,
  measure the load in isolation, then decide F2 on numbers rather than on this card's reasoning. `RIG_WARM_CAP`
  makes that an A/B once traversal is routed through it.
- **F1 — push `maxDepth` into the SQL closure.** `SqlReachability.ReachedWithDepthAsync` already takes a
  `maxDepth` and already materializes a `reach_depth` temp table, so the plumbing exists; the traversal
  loaders simply never pass a bound. This makes `--depth 3` genuinely cheap and removes a real surprise: a
  3-deep tree currently costs exactly what an unbounded one does. It does **not** help the default web case,
  where depth is unbounded — which is why F2 leads.
- **F3 — split the `--time` compute phase into graph-load / walk / effects.** Small, and the prerequisite for
  anyone measuring this again. Today the one lumped row hides the entire finding.

## F2's premise is validated by the live host — measured 2026-09-03

`rig watch` already does what F2 proposes, by a different mechanism (`LiveFactSource` holds facts in RAM
instead of reading the store), so it is a working proof that "one shared load, then cheap per-seed walks" is
the right shape. Two fresh seeds, both verified at closure 45,989, one-shot `rig tree` each:

| arm | seed | time | rows |
| --- | --- | --- | --- |
| store, `--no-live` | `FieldServe.UpdateFieldOldEventsStatus` | 42,582 ms | 4,456 |
| live host, FIRST traversal query | `PACSService.SynchronisePACSOrders` | 15,046 ms | 4,653 |
| live host, repeat | same seed | **1,352 ms** | 4,653 |
| live host, DIFFERENT unseen seed | `FieldServe.UpdateFieldOldEventsStatus` | **2,643 ms** | 4,421 |

The last row is the finding: an **unseen** seed costs 2.6 s once the host has answered any other traversal
query. So the ~15 s the host pays on its first query is **shared across seeds**, not per-seed — exactly the
amortization F2 is asking for on `rig serve`.

**Read the 42.6 s control with care.** It is contaminated: the resident host was live and holding GBs while
it ran, so it contends for CPU and memory. The uncontended store figure for the same closure is the
13.4-17.3 s measured earlier in this card. The honest comparison is **13-17 s → 1.4-2.6 s**, not 42 s → 1.4 s.

**A fidelity gap, not glossed:** the live answers are NOT identical to the store's. `UpdateFieldOldEventsStatus`
yields 4,456 rows from the store and 4,421 from the host — 35 fewer. The host disclosed the likely reason on
boot (*"26 of 11966 indexed file(s) had compile errors, plus 1 outside the indexed set"*), and it reads the
working tree while the store is pinned to a commit. Whichever is right, they disagree, and any plan that
routes agents to the host owes an equivalence check first.

**Host costs, for the record:** a multi-minute cold boot (design-time builds over the whole solution) and GBs
resident. And the endpoint is keyed on the **working directory** — `LiveQueryClient.PipeNameFor(workingDirectory)`
— so a host started in the wrong cwd serves nothing useful and the client silently falls back to the store.
A first attempt bound to `C:\Git\coderig` and would have answered MedDBase queries with coderig's own 1.7 KB
ruleset.

## Verify before shipping F1

Bounding the closure by depth must not change the answer. The risk is not the walk (its bound already
matches) but `ShapeGraph`: generic-factory monomorphization may need edges beyond the depth bound to resolve a
concrete construct, and a depth-bounded closure would hide them. There is already an equivalence test to
extend — `Bounded_graph_reproduces_full_graph_reach` — and it is the gate for F1, not an afterthought.

## Not the cause, ruled out by measurement

Effect derivation, `ShapeGraph`, generic monomorphization, payload size, and `call_edges` reach breadth. Each
has its own arm in the table above.

## Provenance

RCA 2026-09-03 on store `aae396ea7e8e-dirty` (446k symbols, 2.44M refs, 306,160 graph nodes). Seeds picked
by `call_edges` out-degree between 25 and 55 so none had been queried before; closure counts by recursive CTE
directly against `call_edges` / `dispatch_edges`.
