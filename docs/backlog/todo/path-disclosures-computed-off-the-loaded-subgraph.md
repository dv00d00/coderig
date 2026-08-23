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
2. [a rules edit not reaching the baked graph](baked-call-edges-ignore-rules-edits.md) — classification read from
   `call_edges` on one path, recomputed on another.
3. This one — ambiguity resolved over the loaded subgraph rather than over the symbol universe.

The shared shape: **a fact that is a property of the QUESTION is being derived from an artefact of the query
PLAN.** Worth a standing review question rather than a fourth rediscovery: *when this line is emitted, is it
computed from the answer, or from whatever the loader happened to hand us?*

## Fix

Resolve pattern ambiguity independently of the loaded graph — over the symbol universe, which is what the user's
pattern is actually ambiguous against. That changes the STORE path's output (it starts disclosing ambiguity it
currently hides on the fast arm), so it needs its own slice and its own before/after.

Separately: `Fact graph: …` is a load diagnostic sitting in the middle of an answer, and it already reports two
different values for the same store depending on whether `rig graph` ran. It probably belongs behind `--time`
rather than in the answer body.

## Acceptance

1. `rig path A B` emits the same ambiguity disclosure regardless of whether the store has a materialized graph.
2. `LivePathCallersTests` can drop its per-case exclusion for these two lines — deleting that exclusion is the
   regression test, and its comment says so.

## Related

Pinned (not fixed) by `tests/Rig.Tests/Live/LivePathCallersTests.cs`, which compares stdout/stderr byte-for-byte
except these two lines, asserts the asymmetry is one-directional (live may add the note, never drop it), and
scopes the exclusion PER CASE so every other case still compares exactly.
