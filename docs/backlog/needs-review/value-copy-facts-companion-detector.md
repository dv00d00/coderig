# Value-copy facts — the struct-copy companion to the allocation detector

**Status:** todo · **Family:** performance analysis · extraction
**Extracted from:** [alloc-effect-detector](../done/alloc-effect-detector.md) (shipped record), 2026-09-02
**Triage:** needs-triage

## What already shipped

The allocation detector is end-to-end and calibrated: Roslyn extraction → a dedicated `allocation_facts`
table → a pure core deriver → the shared effect stream, with mechanism, cardinality, nullable shallow bytes,
confidence and an assumption-bearing basis on every fact. AngleSharp.Core recalibration produced 1,307 facts
(878 with x64 shallow-byte estimates). Full record and its evaluation protocol:
[alloc-effect-detector](../done/alloc-effect-detector.md).

That card's own closing recommendation is *"build value-copy facts next using the same size/evidence
vocabulary"*. This card is that item, and nothing else.

## The problem

Struct copying is closely related optimization evidence but it is **not allocation**, so it must not be
emitted as an `alloc` effect. It has no representation at all today, so a `ref struct` passed by value and
one passed with `in` look identical to every rig query.

## Scope, as the parent card specified it

- by-value arguments and returns;
- boxing, joined to the corresponding allocation effect;
- compiler defensive copies from readonly receivers;
- foreach / property / indexer / assignment copies where Roslyn can identify a source-level candidate;
- shallow value-type size estimates using the same `known` / `estimated` / `unknown` vocabulary.

## Regression target

AngleSharp's streaming API deliberately passes large `ref struct` callback values with `in`. The detector
must show the by-value equivalent as a copy and leave the current `in` sites alone. Machine-code copy elision
still requires disassembly to confirm, so the fact means "a source-level copy candidate", never "a copy
executed".

## What counts as finishing

- A dedicated value-copy fact family, not an `alloc:*` operation.
- Fixtures for each shape above plus an `in`-parameter negative control.
- The AngleSharp `in`-site negative control holds on a re-indexed store.
- Size evidence reuses the allocation estimator vocabulary and discloses its x64/pointer-size assumptions.
- This is an extraction change, so it requires re-indexing the evaluation target.
