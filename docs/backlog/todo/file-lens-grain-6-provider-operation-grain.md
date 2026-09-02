# Decide `provider:operation` grain on a measurement, not on the store-wide label count

**Status:** todo · **Opened:** 2026-09-02, splitting the deferred grain question out of the file-lens grain
wayfinder · **Family:** file lens / effect vocabulary
**Triage:** needs-info

**Blocked by:** [Widen to provider grain](./file-lens-grain-2-provider-grain.md) — the payload must carry
`Provider` before `provider:operation` pairs can be counted at all.

This card does **not** block child 2. Provider grain is already decided, specced and `ready-for-agent`;
nothing here gates it.

## The question

Does the effect vocabulary get a third grain, one step finer than provider: **`provider:operation`** —
`entity_cache:read` rather than `entity_cache`, or `cache`?

## Why the standing objection uses the wrong denominator

The objection to operation grain is "hundreds of labels". That is a STORE-wide count, and it is the wrong
denominator. What decides whether a third grain is usable is the number of distinct `provider:operation` pairs
**on one row and in one file**, because that is what has to fit the per-row mark budget and the outline. A
store with 66 providers can still have three in a given file.

That number has never been measured.

## The client is already built for two grains and already discloses the gap

This matters before deciding, because it makes the cost lower than the objection assumes:

- The lens filter bar already has a `grain` control that cycles `family | provider`
  (`filelens.js:268`, `:787-791`).
- When the payload cannot support the requested grain, the client renders an **honest gap** rather than
  inventing a label: `Badge` prints `family:?` with the tooltip *"provider unknown at this grain (the API
  returns family only)"* and an `fx-unknown` class, gated on a `providerKnown` flag
  (`filelens.js:119-120`, `:388-407`).
- The per-row mark budget is **already per grain** (`MARK_BUDGET[filter.grain]`, `filelens.js:584-589`), and
  both numbers were set by measuring the real store until the clipped-row count hit zero on `Controller.cs`
  and `WriteDischargeDetail.cs`.

So the missing half is the server payload, not the UI: `FileEffectAggregateDto` is
`(Family, NearestDepth, ViaDispatchOnly, Looped)` — `FileEffectsContracts.cs:11`.

## The measurement that decides it

Over the real MedDBase store, the distribution of distinct `provider:operation` pairs:

- per marked line,
- per method row,
- per file,

each reported as p50 / p95 / max. Child 2's acceptance already requires a MedDBase measurement pass (cold
time, peak memory, aggregate count, payload size), so this distribution is attached to that pass and arrives
as a by-product rather than as new work.

## The decision rule

- **p95 pairs per row is small** → operation grain is a third position on the `grain` control that already
  exists, reusing the `providerKnown` honest-gap mechanism wherever a payload cannot supply it. That needs no
  new interaction design, which is the whole premise of the "needs its own payload and interaction decision"
  objection.
- **p95 pairs per row is large** → the objection is confirmed with a number, and the answer is a per-badge
  drill-down (expand one badge into its operations), not a grain.

Either way the outcome is recorded against the distribution rather than against an assumption. This mirrors
the approach already agreed for the effect-severity mark: compute the distribution first, let the rendering
choice follow it ([compute the effect-severity distribution
first](./effect-severity-mark-compute-the-distribution-first.md)).

## Out of scope

- Widening child 2 to carry operation grain. Its out-of-scope boundary — operation/resource filters — stands.
- CLI operation-level `--only` filters ahead of the decision.

Whichever branch is taken, if it changes the projection or the payload it owes a `FileEffectsSchema` bump.
