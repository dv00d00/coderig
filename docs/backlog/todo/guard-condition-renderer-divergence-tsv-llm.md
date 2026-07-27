# Guard condition polarity is INVERTED when the source condition is negated

**Status:** ✅ FIXED 2026-07-27 (extraction; requires a re-index to take effect on an existing store).
**Retitled** — the original title claimed a renderer divergence; investigation showed there is none. See
*Investigation outcome* below for what was real and what was not.

**Original status:** todo · **Priority: HIGH** (a reviewer reads the branch condition backwards; guard-based conclusions become unsound) · **Found:** 2026-07-27 (reviewing MedDBase MR !11025, `PersonEventEntity.Save` audit suppression) · **Family:** effect-precision / control-dependence / output-fidelity
**Related:** [[guard-set-direct-vs-transitive-control-dependence]] (that item is about the guard set being INCOMPLETE but *correctly polarised*; this is a different defect — the same edge renders differently per format)

## The observation

One call edge, three different answers depending on `--format`.

Store: `de69fd2ffc6b-dirty` (MedDBase @ `de69fd2ff`). Repro:

```bash
# pretty
rig tree "MedDBase.DataAccessTier.EntityClasses.PersonEventEntity.Save" --guards --view paths
#   ├─ TransactionDependency.Call ⎇ [!(!IsPersonMerge && // no auditing for documents anymore,...] ⋯elided

# tsv  (guards column)
rig tree "…PersonEventEntity.Save" --guards --view paths --format tsv
#   1  M:MMS.Data.TransactionDependency.Call(…)  invocation  0  alloc:object  …  !!IsPersonMerge

# llm  (guards column) — same as tsv
rig tree "…PersonEventEntity.Save" --guards --view paths --format llm
#   1  TransactionDependency.Call  3  1  alloc:object  seen  !!IsPersonMerge
```

## The source

`src/main/MedDBase.DataAccessTier/MMSEntityClasses/PersonEventEntity.cs:187-219`:

```csharp
if (!IsPersonMerge &&
    // no auditing for documents anymore, we plan to stop using PERSON_EVENT for documents completely
    (!FkDocument.HasValue || !Settings.DocumentEventCommentsStopPersonEventAudits))
{
    …
    TransactionDependency.Call(                       // ← THE guarded edge
        () => WithGlobal.ImpersonateProfileSafe(…
                  AuditLog.Create("Patient Medical Record Changed", …).Log()),
        e => Logger.Get().Error(e, e.Message),
        Transaction);
}
```

True firing condition: `!IsPersonMerge ∧ (!FkDocument.HasValue ∨ !StopPersonEventAudits)`.
The guarded call is in the **then** arm.

## This violates the DOCUMENTED contract

`.claude/skills/rig/SKILL.md` § *Control-dependence guards* states:

> The condition is the full source predicate — a short-circuit `a || b` shows whole (**not split into
> operands**), **an else-arm is negated** `!(…)`

Observed output is both **split into operands** (`IsPersonMerge` alone, `&& (…)` dropped) and **negated on a
then-arm**. So this is a contract violation, not an undocumented design choice — which also means the
`--guards` feature cannot currently be trusted for exactly the review question it was built for.

## Two distinct defects

1. **Conjunct dropped (tsv/llm).** The stored/emitted guard is `!!IsPersonMerge` — the second conjunct,
   `(!FkDocument.HasValue || !Settings.DocumentEventCommentsStopPersonEventAudits)`, is gone. That conjunct
   is the entire semantic content of the change under review; a guard-driven review would miss it.
   The pretty renderer *does* carry it (before eliding), so the information exists upstream of formatting.
2. **Polarity looks inverted.** `!!IsPersonMerge` ≡ `IsPersonMerge`, i.e. the negation of the first operand —
   which is the condition for the arm that does **not** run the call. Pretty shows `!(!IsPersonMerge && …)`,
   also an outer negation. Since the call sits in the **then** arm, the expected render is
   `[!IsPersonMerge && (…)]` with no outer `!`.
   Hypothesis: `&&` is being decomposed into nested implicit ifs and the arm bookkeeping negates the wrong
   operand (or the else-arm negation from [[guard-set-direct-vs-transitive-control-dependence]] is applied
   to a then-arm edge).

## Suspected root cause

The predicate spans multiple source lines **and embeds a `//` comment between operands**. Candidate causes,
in order of likelihood:
- the condition-text extractor takes only the first operand's span when the expression is multi-line;
- the comment trivia terminates the span scan;
- tsv/llm serialise a normalised/simplified condition while pretty serialises the raw source span (two code
  paths → the observed divergence).

A multi-line `&&` with interleaved comments should be a fixture case either way.

## Why it matters now

rig is being used to review MRs. On this MR the *only* behavioural change to the audit path was this
predicate. A reviewer who trusts the guard column concludes "audit fires when `IsPersonMerge`" — backwards,
and blind to the document-row suppression that is the actual change.

## Investigation outcome (2026-07-27)

### Defect 1 — "conjunct dropped in tsv/llm" — NOT REAL. Store mix-up.

The three commands in the repro above did **not** read the same store. `.rig/LATEST` pointed at
`e8858aa90e02-dirty` — the **BASE** commit — so the bare `--format tsv` / `--format llm` runs answered from
the base store, where the source genuinely is `if (!IsPersonMerge)` with no second conjunct. The pretty run
had been given the head store. Pinning the store makes all three formats agree:

```bash
rig tree "…PersonEventEntity.Save" --guards --view paths --format tsv --store de69fd2ffc6b-dirty
#   1 | !(!IsPersonMerge && // no auditing for documents anymore,...   ← full condition, same as pretty
rig tree "…PersonEventEntity.Save" --guards --view paths --format tsv --store e8858aa90e02-dirty
#   1 | !!IsPersonMerge                                                ← correct for the BASE source
```

There is no renderer divergence and no operand splitting: `FactExtractor.FullCondition` already walks the
branch syntax up the `&&`/`||`/`!`/parens chain to recover the whole source condition, and truncation is
length-based and uniform. **The contract in SKILL.md was being honoured.**

**Second-order finding worth its own item:** a silently-stale `LATEST` producing a plausible-but-wrong
answer is what manufactured this false report. That is the `fatal-on-stale-store` ask in
[[cli-surface-and-help-refresh-2026-07]] — this incident is direct evidence for it.

### Defect 2 — polarity inverted — REAL, and fixed.

Confirmed against MedDBase source; polarity was **not** uniformly wrong, which is what made it hard to see:

| edge | source | rendered (pre-fix) | |
|---|---|---|---|
| `IApplication.GetSession` | `if (Core.SessionId == null) return null;` … call after | `!(Core.SessionId == null)` | ✅ |
| `CertificateEntity.AssertAnyRight` | `if (FkDocument.HasValue) { …call… }` | `FkDocument.HasValue` | ✅ |
| `TransactionDependency.Call` | `if (!IsPersonMerge) { …call… }` | `!!IsPersonMerge` | ❌ |

The discriminator is a **leading unary `!`**. Roslyn's CFG folds a `!` out of the branch value and inverts
`ConditionKind` instead — `if (!flag)` branches on `flag`. `FullConditionText` widened the guard TEXT back up
through that `!` (`FactExtractor.cs:1733`) while leaving `WhenTrue` relative to the inner operand, so the
negation was applied twice.

**Fix:** `FullCondition` now also reports whether an ODD number of `!` were crossed during the walk, and the
caller XORs that into `WhenTrue`. Ten lines, in extraction only; no renderer change.

Side effect: `if (!a || b)` previously emitted **two contradictory guards** (`!(!a||b) && (!a||b)`), because
the two lowered operands cross a different number of `!` on the way up. The flip makes them dedup to one.

**Regression tests:** `tests/Rig.Tests/Analysis/GuardPolarityTests.cs` — in-memory compile → real
extract, so it carries **no MedDBase dependency and no indexing cost** (runs in ~1.4s). Verified red→green:
the two negation tests fail pre-fix, and a third test fences the already-correct unnegated cases.

**Re-index required.** Guard text/polarity is frozen at index time, so an existing store keeps the old
values until re-indexed. This is an extraction change, so no `*Schema` cache-key bump applies.

### Real-store validation (2026-07-27)

Re-indexed MedDBase (`8cebdcf183e4`, ~29s graph build) and compared against the pre-fix store
(`e8858aa90e02-dirty`). Two corpus-wide signatures over `call_edges.EnclosingGuards`:

| | pre-fix | post-fix |
|---|---:|---:|
| guarded call edges | 48,372 | 48,272 |
| guard entries | 44,455 | 44,053 |
| double-negated (`!p` with polarity 0) | 1,750 | 1,685 |
| **self-contradictory edges** (same predicate, both polarities) | **397** | **1** |

The decisive edge now reads correctly — `!!IsPersonMerge` → **`!IsPersonMerge`** for the then-arm call.

Reading the numbers honestly:
- **Contradictions 397 → 1 is the real result.** A guard set asserting `!(P) && P` is never right; the
  polarity flip is what makes those operand pairs dedup to one. The entry count falling by 402 ≈ the 396
  contradictions collapsing corroborates it.
- **The double-negation count is a loose upper bound, not a bug count**, which is why it barely moves.
  `if (!a) {} else Foo();` legitimately yields `(!a, false)`. Sampling the 1,685 residue confirms it is that
  population — `!File.Exists(p)` at polarity 0 ("runs when the file DOES exist"), `!ValidateInputs()` at
  polarity 0, etc. All correct.
- The bug only bit where the `!` was an ANCESTOR of the CFG branch value (`if (!a)`, `if (!a && b)`,
  `if (!(a||b))`). Where Roslyn preserved the `!` IN the branch value there was nothing to flip, so the
  affected population is narrower than "every negated condition".
- Caveat: the two stores are **different commits**, so the edge-count deltas (−100 edges) include real source
  change and are not attributable to the fix. The contradiction and polarity findings are robust to that.
- The single survivor is a DIFFERENT defect, filed as
  [[guard-dedup-keyed-by-text-not-branch-identity]] — two sequential `do…while (retry)` loops sharing a
  variable, so two distinct decision points collide on predicate text.

## Acceptance

- ✅ One edge ⇒ identical condition text across `pretty | tsv | llm` (modulo the pretty renderer's `⋯elided`
  truncation, which should be length-based only and never drop operands). — was already true; the apparent
  divergence was a store mix-up.
- ✅ Polarity: then-arm edges render the condition unnegated; else-arm edges render `!(full condition)`.
- Fixtures: multi-line `&&` / `||` chains, with and without interleaved `//` and `/* */` trivia; nested
  mixed-polarity arms.
