# Allocation-returning library APIs need measured, core-owned summaries

**Status:** todo · **Family:** performance analysis · detector coverage
**Extracted from:** [alloc-effect-detector](../done/alloc-effect-detector.md) (shipped record), 2026-09-02
**Triage:** needs-triage

## The problem

Some framework/library APIs allocate despite having no allocation syntax at the call site, so the shipped
detector — which reads allocation from C#/Roslyn semantics at the source site — cannot see them. They are
named as a follow-on in [alloc-effect-detector](../done/alloc-effect-detector.md) and are still absent.

The parent card is explicit about the trap: **do not infer allocation merely because an API returns `string`
or another reference type**, and do not ship an upfront API catalogue. The earlier rule-only LINQ experiment
(18 sites across the whole solution, six of them in build tooling) is kept as discarded calibration precisely
because a curated method list is the wrong shape.

## Constraints carried from the parent

- Summaries stay **core-owned**: `rig.rules.json` must not define what counts as an allocation, and
  `builtin-rules.json` must not carry a framework catalogue pretending to be allocation semantics.
- Every summary is **measurement-backed** and added from a measured MISS, not from reading documentation.
- IL scanning stays a development/calibration backstop for lowerings that cannot be attributed soundly from
  Roslyn source semantics — never a required indexing stage.

## What counts as finishing

- A summary mechanism whose entries each cite the measurement that justified them.
- At least one measured miss from the AngleSharp parser/tokenizer evaluation closed by it, with the before
  and after counts recorded.
- No summary that fires purely on a reference-type return.
- Evaluation runs with `--intrinsic` or `--only alloc`; a bare `rig derive` shows zero allocation effects by
  design since 2026-07-27.
