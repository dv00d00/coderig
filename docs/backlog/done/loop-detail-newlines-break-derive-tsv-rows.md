# Multi-line `LoopDetail` leaks raw newlines into `derive --format tsv` and breaks row parsing

**Status:** done 2026-09-03 · **Family:** output-fidelity / TSV
**Extracted from:** [nonlinear-amplification-degree](../done/nonlinear-amplification-degree.md) ("Notes"), 2026-09-02
**Triage:** ready-for-agent

## The defect

Multi-line LINQ query text in `LoopDetail` leaks **raw newlines** into `rig derive --format tsv`, so a single
logical row spans several physical lines and breaks row parsing. Found while baselining the amplification
sweep; visible as stray `.Where(...)` lines in a row-type histogram of the TSV.

`rig amplify` collapses whitespace in loop detail because it had to. The parent card records that **the
existing `derive` rows want the same fix** and it was not done there:
[nonlinear-amplification-degree](../done/nonlinear-amplification-degree.md).

## Why it matters beyond tidiness

TSV is the machine surface — the calibration histograms, the byte-identical parity baselines
(`rig derive --format tsv`, 18.5 MB, SHA-256 compared in
[core-purity-project-vocabulary](../done/core-purity-project-vocabulary.md)) and every agent script read it.
A row that silently splits corrupts a count without erroring.

## What counts as finishing

- Whitespace collapsed (or the field escaped) wherever `LoopDetail` reaches a TSV emitter, so every TSV row
  is exactly one physical line.
- A fixture with a multi-line LINQ query asserting the row count, not just the content.
- No change to the human renderer's text; this is an emitter fix.
- No `*Schema` bump: TSV rendering is not a cached derivation payload.

## Verification

- `DeriveTsvLoopDetailTests`: 2/2 passed, including physical-row count and 9/11-column contracts.
- Full Release build: 0 warnings, 0 errors.
- Main suite: 1,416/1,416 passed; full integration matrix passed apart from its one documented skip.
