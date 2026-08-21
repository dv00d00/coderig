# The `--intrinsic` hint is counted BEFORE the reachability filter, so `reaches` can claim it withheld effects it never had

**Status:** todo · **Priority: MEDIUM** (a disclosure line that can be false; no wrong FACTS, but rig's whole
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

1. `rig reaches <pattern>` on a pattern with no reachable `alloc`/`throw` emits NO intrinsic note; adding
   `--intrinsic` changes nothing (today: note emitted, `--intrinsic` adds nothing).
2. A pattern WITH a reachable intrinsic still emits the note and `--intrinsic` still reveals exactly that many.
3. `LiveReachesTests` can then compare stderr WITHOUT the `WithoutIntrinsicHint` exemption — deleting that
   exemption is the regression test, and its header comment says so.

## Related

Pinned (not fixed) by `tests/Rig.Tests/Live/LiveReachesTests.cs`, which compares stdout byte-for-byte and
stderr with this one line stripped from both sides, asserting the asymmetry is confined to it.
