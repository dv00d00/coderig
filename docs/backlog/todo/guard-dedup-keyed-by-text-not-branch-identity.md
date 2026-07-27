# Guard sets dedup on predicate TEXT, so two distinct decision points sharing a variable render as a contradiction

**Status:** todo · **Priority: LOW** (rare — 1 edge in 48,272 on the MedDBase store; renders as visible nonsense rather than a plausible wrong answer, so it misleads far less than the polarity bug did) · **Found:** 2026-07-27 (the single residual case after fixing [[guard-condition-renderer-divergence-tsv-llm]]) · **Family:** effect-precision / control-dependence

## The observation

After the guard-polarity fix, a corpus-wide sweep of the MedDBase store for self-contradictory guard sets
(the same predicate carrying BOTH polarities on one edge) went 397 → **1**. The survivor:

```
DocumentManager.Core/ApplicationService.cs:312  ->  ITempFileService.SaveFile(string,string,bool)
   guards: retry=F , retry=T          # renders as  ⎇ [!retry && retry]
```

## Why it is not a polarity bug

The method has **two sequential `do { … } while (retry);` loops reusing the same variable**:

```csharp
do { retry = false; … if (dialogResult.IsFail) { … retry = true; } } while (retry);   // loop 1
…
do { retry = false;
     var saveResult = _tempFileService.SaveFile(tempFilePath, localFilePath, overwrite);  // ← the edge
     … } while (retry);                                                               // loop 2
```

The call is control-dependent on **two different branch blocks**: loop 1's `while (retry)` (it is reached only
by EXITING loop 1, i.e. `retry == false`) and loop 2's `while (retry)` (reached again on a retry iteration,
`retry == true`). Both are correct. They are distinct DECISIONS that happen to share predicate text.

`EncodedGuardsFor` dedups on `(text, polarity)` — deliberately, because that is what collapses the lowered
operands of ONE short-circuit condition into one guard. But text is not a decision identity, so two unrelated
branch blocks with the same source text cannot be told apart, and AND-joining them yields `!retry && retry`.

## Options (not yet chosen)

1. **Suppress** — when one edge carries the same predicate text under both polarities from DIFFERENT branch
   blocks, the conjunction is vacuous; drop both entries. Cheapest, and arguably most honest: the guard set
   genuinely constrains nothing. Loses the (weak) signal that a loop is involved.
2. **Disambiguate** — key the dedup on `(BranchBlock, text, polarity)` and render repeats distinctly (e.g.
   `retry@L304`). Preserves information but makes the common case noisier, and block ordinals are not
   stable across re-index.
3. **Leave it** — 1 edge in 48k, and `!retry && retry` is self-evidently not a real firing condition.

Option 1 is the recommendation if this is ever worth touching; the dedup already exists, so it is a
few lines in the same loop.

## Acceptance (if taken)

- A fixture with two sequential `do … while (flag)` loops over one variable, with a call in the second loop,
  produces no contradictory guard pair.
- The short-circuit-operand dedup (`if (a || b)` → ONE guard) is unaffected — regression-fenced by
  `FactExtractorCaptureTests.Guard_predicate_is_the_full_source_condition_not_the_lowered_branch_operands`
  and `GuardPolarityTests`.
