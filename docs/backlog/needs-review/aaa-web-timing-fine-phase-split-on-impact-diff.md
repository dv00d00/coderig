# Fine-phase split on `ImpactEngine.DiffAsync`

**Status:** needs-review — value not agreed. The parent card calls it "a precision nice-to-have (better
attribution in the breakdown/CSV), not urgent", so it is neither scheduled nor declined. · **Family:** timing / observability
**Extracted from:** [web timing unification](../done/aaa-web-timing-unification-ui.md) (its C0 remainder), 2026-09-02
**Triage:** needs-info

## The item

`rig impact --time` wraps a real `QueryTiming` / `PhaseTimings` / `ResourceSampler` scope, but it is fed by
only **four coarse ticks** — provenance / head-load / branch-compute / base-assemble — the same boundaries
the SSE progress log already had. The originally scoped FINER phases were: resolve+open, head-load,
head-derive, branch reach-sets, footprints, hazards, base-side, assemble. `ImpactEngine.DiffAsync` itself was
untouched by `d2c71d1b`, so promoting the four ticks into those eight is the whole remaining slice.

## What already shipped

C1 (live progress via `/api/impact/stream`), C2 (the unified `--time` viz: `/api/impact/telemetry` returns
the sampled cold-diff CSV, `wwwroot/telemetry.html` loads it via `?csv=`, reskinned to the explorer theme,
linked from `ImpactView`), and C0 in its coarse form (`TimingReport.WriteBreakdown` plus
`rig-impact-telemetry.csv` beside the head store). Terminal and web share one timing model and one viz,
verified against MedDBase main↔MR-10840. Record:
[web timing unification](../done/aaa-web-timing-unification-ui.md).

## The question to answer before it is scheduled

Is finer phase attribution worth touching `ImpactEngine.DiffAsync`? The coarse breakdown already localises
cost, and the on-disk diff cache makes a warm re-diff instant, so this only improves the first cold run's
observability. It is additive and low-risk; it is not obviously worth the edit.

## If it is agreed

- Eight named phases on the master clock, so `ResourceSampler` attributes CPU/mem/disk to each `[Start,End)`.
- The SSE progress log and the `--time` breakdown stay fed by the SAME tick boundaries — they diverged once
  already, which is how the coarse form shipped.
- Verify against a real MedDBase cold diff, not a fixture.
