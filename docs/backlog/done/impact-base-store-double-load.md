## `rig impact` — base store entry-point data loaded twice

**Status:** todo — open perf bug, re-verified against `ImpactEngine.cs` on 2026-07-19.
**Source:** promoted from `docs/bugs/impact-base-store-ep-data-loaded-twice.md` (🟡 open); leave the bugs/
file in place as the detailed record. 2026-06-25.

### Summary

`rig impact` opens the base store in **two independent `RigDbContext` instances** that don't coordinate, so
`Reads.LoadFactEntryPointDataAsync` (reads all base-type edges, all interface edges, ~217k method symbols,
all type symbols, all ctor refs) runs **twice** against the base store on every `impact` run — roughly
doubling the base-store read. Not a correctness bug; output is identical.

Both the `--per-ep` and default paths are affected. See the full runtime trace in the bugs/ file.

### Fix direction

The two independent opens today: `ComputeEpDiffAsync` (`ImpactEngine.cs:346-350`) and
`ComputeBaseSideAsync` (`:767-774`) each open the base store and call
`Reads.LoadFactEntryPointDataAsync` → `DeriveEntryPointsAsync`. Both run on a cold diff:
the branch-side flow calls `ComputeEpDiffAsync` at `:217`, then assembly calls `ComputeBaseSideAsync` at
`:268`.

Open the base store **once** and share its `epData` (and the derived base EP set) across both
`ComputeEpDiffAsync` and `ComputeBaseSideAsync` — mirroring what the branch side already does with its
single load. Concretely: have both take a shared base `RigDbContext` and a shared `FactEntryPointData`
(loaded once by the caller), or fold the EP-diff into `ComputeBaseSideAsync` so its single base load feeds
the EP set-diff too (the base EP set the EP-diff needs is already derived there).

Net effect: base store opened 1×, `LoadFactEntryPointDataAsync` + `DeriveEntryPointsAsync` each run 1×.

### Test to add

Count base-store opens (or `LoadFactEntryPointDataAsync` invocations) for one `impact` run with a resolved
base store — assert 1, not 2 — for both the default and `--per-ep` paths. Natural home: the two-store
fixtures in `tests/Rig.Tests` for the behavioral-delta feature.

### Detailed record

`docs/bugs/impact-base-store-ep-data-loaded-twice.md` — full runtime trace with line-number references.

## ✅ FIXED 2026-07-27

`ComputeEpDiffAsync` (which opened its OWN base `RigDbContext` and re-ran
`LoadFactEntryPointDataAsync` + `DeriveEntryPointsAsync`) is gone. In its place:

- `DiffEntryPointSets(branchEps, baseEps)` — the PURE set-diff, no I/O;
- `ComputeBaseSideAsync` takes the branch EP set and computes the diff from the base EP set it was
  already deriving, so the base store is opened **once** per run.

`ComputeBranchSideAsync` lost its `baseDbPath` parameter entirely (and became synchronous — it no longer
touches a store), which is the structural proof the second open is gone rather than merely relocated.

Measured on the MedDBase pair, `--no-cache --time`, output byte-identical: the duplicate load lived in the
`head: reach sets + footprints + hazards` phase, whose **disk read went 1.9 GB -> 0 GB**. Wall-clock credit is
entangled with the index-session fix landed in the same pass — see the table in
[[redundant-graph-index-rebuild-per-query]] (head traversal phase 42.5s -> 32.8s, total 2m31s -> 2m22s).

`ImpactEpDiffTests` now derives both sides and calls the pure diff, so no store-opening variant survives
solely for a test to exercise.
