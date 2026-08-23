# The `--intrinsic` hint was counted BEFORE the reachability filter, so `reaches` could claim it withheld effects it never had

**Status:** SHIPPED 2026-08-23 (Slice 7B1) · **Priority: was MEDIUM** (a disclosure line that can be false; no wrong FACTS, but rig's whole
contract is that its disclosures are trustworthy) · **Found:** 2026-08-21, measured while building live query
serving ([live-background-index](../progress/live-background-index.md) slice 6b) · **Family:** disclosure / reaches

## The bug

`ReachesCommand` derives effects, then filters them to the reachable set:

```csharp
var effects = await source.DeriveEffectsAsync(inputs, graph, rules);
var selection = SelectEffects(effects, only: …, exclude: …, includeIntrinsic: opts.Intrinsic);
…
var hits = effects.Where(e => e.EnclosingSymbolId is not null && reachable.ContainsKey(e.EnclosingSymbolId))…
```

`SelectEffects` computes `HiddenIntrinsic` — the count behind *"note: intrinsic effects (alloc, throw) are
hidden by default — pass --intrinsic to include"* — over the **input** effect set, BEFORE
`reachable.ContainsKey` narrows it to the answer. So the note fires when an `alloc`/`throw` effect exists
anywhere in the derivation inputs, whether or not any of them is reachable from the queried entry point.

## Shipped result — 2026-08-23

`ReachesCommand` now restricts derived effects to its reachable method set before `SelectEffects`.
`TreeCommand` collects the rendered forest's method ids once, restricts ordinary and hazard effect inputs to
that set, and only then applies selection for TSV and every human/LLM view. The unfiltered tree cache payload is
unchanged; this is render selection over the query-local forest, so no cache payload/schema migration is needed.

The live/store parity gates removed `WithoutIntrinsicHint` entirely and now compare stderr byte-for-byte:
`LiveReachesTests` passes 4/4 and `LiveTreeTests` passes 9/9 on macOS. Slice 7B1's dirty forward-query planner can
therefore skip disjoint ordinary-project debt without allowing an unrelated alloc/throw to change the answer.

Told to a user, the note means "there is more to see here, pass `--intrinsic`". Passing it can then add
nothing, because nothing withheld was ever in the answer.

## How it surfaced

Comparing a live-served `reaches` answer against the store-served one on `playgrounds/EntryPointEffects`:
**stdout byte-identical on all 14 patterns across two playgrounds, but the hint diverged on 3 of 7**
(`TeamRepository.AddAsync`, `SavePublisher.Raise`, `CycleFixture.MutualA`) — live emitted it, the store stayed
silent.

The divergence is not a live-path defect. The store path derives effects from SQL-BOUNDED inputs
(`SqlReachability` narrows to the pattern's closure) while the live path has no SQL and derives over the whole
fact set. For those three patterns the bounded closure happens to contain no `alloc`/`throw`, so the store's
count is 0 by accident of bounding — and the bounded closure is still a reach SUPERSET, so **the store path
can raise the same false hint on other patterns**. Neither side is right; both count the wrong set.

## Fix

Count the withheld intrinsics AFTER the reachability filter — i.e. over `hits`, the effects that actually
reached the answer — and disclose that number. Then the note means what it says on both paths, and the
live/store asymmetry disappears as a by-product rather than being papered over.

Check `tree --hazards` and any other `SelectEffects` caller for the same pre-filter ordering.

## Acceptance

1. [x] `rig reaches <pattern>` on a pattern with no reachable `alloc`/`throw` emits no intrinsic note; adding
   `--intrinsic` changes nothing.
2. [x] A pattern with a reachable intrinsic still emits the note and `--intrinsic` reveals it.
3. [x] `LiveReachesTests` and `LiveTreeTests` compare stderr without a `WithoutIntrinsicHint` exemption.

## Related

Originally pinned by `tests/Rig.Tests/Live/LiveReachesTests.cs`; the exemption was removed by the shipped fix.

## CONFIRMED for `tree` — 2026-08-21, all views

This item's Fix section asked someone to "check `tree --hazards` and any other `SelectEffects` caller for the
same pre-filter ordering". Checked while migrating `tree` onto the live seam: **it has the bug, and in EVERY
view, not just hazards.** `SelectEffects` computes `HiddenIntrinsic` over what the derivation produced, before
`tree`'s own reachable-method filter.

Measured the same way as `reaches`: 1 of 5 patterns diverges live-vs-store on `tree`
(`TeamRepository.AddAsync`, EntryPointEffects — the SAME pattern the `reaches` comparison named), with stdout
byte-identical across all 24 view/format comparisons and only this stderr line differing. The store is silent
because its SQL-bounded closure happens to contain no alloc/throw; the live path derives over the whole fact set.
Neither side is right.

So the fix's blast radius is now known: `reaches` and `tree` (all views), and the acceptance test should cover
both. `path` and `callers` do not emit the hint.
