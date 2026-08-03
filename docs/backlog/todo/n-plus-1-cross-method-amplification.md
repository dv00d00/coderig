# `n_plus_1` is INTRA-METHOD: cross-method amplification (loop in caller, read in callee) is invisible

**Status:** TODO · **Found:** 2026-08-03, measuring FR-3 recall against preprod runtime data after shipping
[n-plus-1-iteration-contexts-beyond-loop-statements](../done/n-plus-1-iteration-contexts-beyond-loop-statements.md)
· **Family:** hazard-recall / FR-3 · **Tier:** graph (not effect-local)

## Evidence

The preprod dashboard "Fat Spans — Wall + SQL Roundtrip Triage" (`np-hyperdx`, tile "N+1 hotspots") gives
runtime-CONFIRMED per-endpoint read amplification — ground truth to measure static recall against. Top 8 by
total SQL seconds, checked against a full-solution store (`32f4dac9dc7b`, rig with the iteration-context fix):

| Endpoint | Hot query | calls/trace | total_sec | rig `n_plus_1`? |
|---|---|---|---|---|
| `Document/BrowserComponent/HtmlEdit2` | `OBJECT_HOLDER` | 1,099.8 | 86.2 | ✗ |
| `Account/Configuration/Main` | `DEPARTMENT_CODE` | 341.9 | 74.1 | ✓ (Main.cs:1039, in a lambda) |
| `Admin/Profile/Home2` | `PROFILE` | 2,347 | 35.7 | ✓ (Home2.cs:294/296) |
| `Workflows/ReferralOutbound/ListPane` | `OBJECT_HOLDER_INDEX` | 73.7 | 27.1 | ✗ |
| `Prescription2/Edit` | `sandbox.FDB` | 202.1 | 23.7 | ✗ |
| `Profile/StatusPanel` | `WORK_MEMBER`+ | 1,340.5 | 11.5 | ✗ |
| `Admin/CommonCatalogues2/Home` | `OBJECT_HOLDER` | 157.6 | 10.8 | ✓ (3 findings) |
| `Document/…/NewHtmlDocumentFromTemplate` | `OBJECT_HOLDER_INDEX` | 298 | 8.0 | ✗ |

**3 of 8.** The misses are NOT the iteration-context gap that was just fixed — that one was lexical and is
covered. `HtmlEdit2` was checked under full forward reachability, not just its own file:

- `rig tree HtmlEdit2 --view hazards` → **18 hazards** (lazy_init_race, thread_local_context, dual_write,
  race_window) and **zero n_plus_1**.
- The same reachable tree contains **20 loop-marked (`🔁`) nodes**.

So loops ARE present on the path and reads ARE present on the path, but no read is LEXICALLY inside a loop in
its OWN method. `FactObservationDeriver` derives `n_plus_1` from the effect's own `EnclosingLoopKind` +
argument surface, both intra-method facts, so `foreach (x in xs) Helper.Load(x)` where `Helper.Load` does the
read is structurally invisible: the loop is in the caller's frame, the read in the callee's.

## Why this is the next tier, not a tweak

Two sub-problems, neither effect-local:

1. **Loop propagation across edges.** "Is this effect reachable from a call site that is itself in an
   iteration context?" The substrate exists — `NearestLoopKind` over the BFS reach already drives the tree's
   `🔁` and is consumed by `ImpactEngine` — so the reachability half is largely solved. The finding would move
   from effect-attached to graph-tier, alongside `event_cycle`/`cache_coherence`.
2. **Key propagation, which is the hard half.** The varying-key discriminator is what makes `n_plus_1`
   precise rather than "any read under a loop". Across a call boundary the loop variable becomes a PARAMETER,
   so deciding the key still varies needs the loop variable → argument → parameter → read-key chain. Without
   it the finding degrades to `looped_effect`-under-reach, which on MedDBase would be enormous (109,825 query
   sites + 31,740 foreach sites, most of them calling something that eventually reads).

Cheap partial worth considering first: propagate one hop only, and only when the loop variable is passed
DIRECTLY as an argument at the call site. That covers the `foreach (x in xs) Helper.Load(x)` shape — likely
the common one — without a general dataflow pass.

## Also visible in the same measurement

All 175 current `n_plus_1` findings are `entity_cache:read`. Zero come from `llblgen:read`/`fetch`,
`db_command`, or `http`, even though all are in the `nPlusOne` provider gate. Consistent with the above: the
`*Cache.New(pk)` family is the one shape where the per-element key is a direct syntactic argument at the read
site, because the raw LLBLGen fetch sits one frame deeper inside the cache seam. Worth confirming that this is
the same root cause and not a second, independent gate problem.
