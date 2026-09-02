# Serialization-sink typing — "type X flows into sink Y"

**Status:** todo · **Family:** reviewer-invokable queries · type flow
**Extracted from:** [reviewer-invokable-queries](../done/reviewer-invokable-queries.md) (ranked item 5), 2026-09-02
**Triage:** needs-triage

## The question

Flag a serializer-unsafe value — `Option<T>`, `Int64` reaching JavaScript, a discriminated or delimited
encoding — reaching a persist / JS / URL sink. It serves the FR-6 corpus cluster (#1646, #1359, #617, #1252,
#1781).

## Feasibility, as the parent card tiers it

Items 1–4 of that card build on the existing reachability + effect graph. **This one extends type-arg
capture**: rig already captures type arguments, so the new work is the **value → sink edge**, not a new fact
family from scratch. Record: [reviewer-invokable-queries](../done/reviewer-invokable-queries.md).

## The boundary to disclose

The parent card is explicit that some neighbours of this are out of even rig's extended reach and must be
disclosed rather than claimed: swallowed-`Either`-before-a-security-effect (#850) and "model field set in
code but never mapped to a DB write" (#145) need value/dataflow rig does not have. This card must not drift
into either.

## What counts as finishing

- A value→sink edge derived from existing type-argument capture, with the sink set as rules data.
- `Option<T>` into `object_store:write` (#1646's shape) is detected on a fixture and, if present, on the
  real store.
- A finding names the flowing type, the sink, and `file:line` for both ends.
- Precision is calibrated on the real store before it is on by default — a structurally-true detector that
  fires 179× is still noise.
