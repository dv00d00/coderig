# `rig path`'s ambiguity note and `Fact graph:` line depend on WHICH loader ran — the same store disagrees with itself

**Status:** todo · **Priority: MEDIUM** (a DISCLOSURE that appears or vanishes based on an internal loader
choice; the path answer itself is unaffected, but "matched 6 distinct symbols" is exactly the kind of line a
reader trusts to be a property of the question, not of the query plan) · **Found:** 2026-08-21, by the
live-vs-store equality gate while migrating `path` onto `IQueryFactSource` · **Family:** disclosure / query paths

## The bug

`rig path` emits two loader-dependent lines:

- `Fact graph: N call edges, M implements edges, K methods` — a load diagnostic;
- `note: pattern '<x>' matched N distinct symbols (…)` — an ambiguity DISCLOSURE.

Both are computed from whatever subgraph the loader happened to produce, not from the pattern's resolution
scope. So they change with the loader arm, on one store, for one question.

Demonstrated on `playgrounds/DeepChain` — the same facts, indexed twice, differing only in whether the derived
graph was materialized:

```
rig index --no-graph DeepChain.slnx && rig path Db.Query Book
  note: pattern 'Book' matched 6 distinct symbols (…)
  Fact graph: 5 call edges, 4 implements edges, 17 methods

rig graph && rig path Db.Query Book
  Fact graph: 0 call edges, 4 implements edges, 1 methods      <- and NO note
```

The bounded SQL arm reports `0 call edges … 1 methods` and stays silent about the ambiguity; the EF-fallback arm
reports 5/4/17 and discloses it. **The store disagrees with itself**, which is what makes this a bug rather than
a live-path artifact — the live answer is byte-identical to the EF-fallback arm.

## Why it is the same class as two already-filed bugs

Third instance in two days of *a disclosure computed off a derivation INPUT SET rather than off the answer*:

1. [the `--intrinsic` hint counted before the reachability filter](../done/intrinsic-hint-counted-before-reachability-filter.md)
   — counts withheld intrinsics over the input effects, not the reachable ones.
2. [a rules edit not reaching the baked graph](question-vs-plan-1-baked-call-edges-ignore-rules-edits.md) — classification read from
   `call_edges` on one path, recomputed on another.
3. This one — ambiguity resolved over the loaded subgraph rather than over the symbol universe.

The shared shape: **a fact that is a property of the QUESTION is being derived from an artefact of the query
PLAN.** Worth a standing review question rather than a fourth rediscovery: *when this line is emitted, is it
computed from the answer, or from whatever the loader happened to hand us?*

## Fix

Resolve pattern ambiguity independently of the loaded graph — over the symbol universe, which is what the user's
pattern is actually ambiguous against.

This **changes the STORE path's output**: the bounded SQL arm starts disclosing ambiguity it currently hides. So
the slice owes its own before/after — the two arms above, captured again after the change — and that is why it
is its own card rather than a rider on another fix.

## Open question — does `Fact graph: …` belong in the answer body at all?

It is a load diagnostic sitting in the middle of an answer, and it already reports two different values for the
same store depending on whether `rig graph` ran (`5 call edges, 4 implements edges, 17 methods` on the EF
fallback, `0 call edges, 4 implements edges, 1 methods` on the bounded SQL arm). Two answers:

- **Move it behind `--time`.** It is a query-plan diagnostic, not part of the answer, so it stops reading as a
  property of the question. Cost: anything reading it out of the answer body has to move with it.
- **Keep it in the body and make it consistent.** Then it has to become a property of the question too, which
  is strictly more work than the ambiguity note alone.

Either way the before/after slice above has to cover this line, since both arms print it.

## Acceptance

1. `rig path A B` emits the same ambiguity disclosure regardless of whether the store has a materialized graph.
2. `LivePathCallersTests` can drop its per-case exclusion for these two lines — deleting that exclusion is the
   regression test, and its comment says so.

## Related

Pinned (not fixed) by `tests/Rig.Tests/Live/LivePathCallersTests.cs`, which compares stdout/stderr byte-for-byte
except these two lines, asserts the asymmetry is one-directional (live may add the note, never drop it), and
scopes the exclusion PER CASE so every other case still compares exactly.

- [`path` gets one engine over one loaded graph](./cli-web-collapse-3-path-engine.md) — the two-loaders half of
  this card is that slice's divergence site: `PathCommand.cs:127-143` and `PathQueryService.cs:75` load
  different graphs for one `FactPathFinder.Find`. After it, the ambiguity note and the `Fact graph:` line are
  computed in one place, so the symbol-universe fix this card owns becomes a one-site change. It stays a
  separate card because it needs a new `IQueryFactSource` member and therefore `LiveQueryFactSource`, which
  that slice does not touch. Family rationale on [the CLI/web collapse map](./cli-web-collapse-map.md).
