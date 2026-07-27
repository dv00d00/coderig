# `impact` is blind to predicate-only changes — a guard tightened around an unchanged effect shows ZERO delta

**Status:** todo · **Priority: HIGH** (this is the single most reviewable class of change in a real MR, and the flagship diff command reports nothing) · **Found:** 2026-07-27 (MedDBase MR !11025 — audit suppression for document rows) · **Family:** impact / behavioral-diff
**Related:** [[design-impact-behavioral-diff]], [[guard-condition-renderer-divergence-tsv-llm]], [[impact-usability-parity-filter-and-alloc-noise]]

## The case

MR !11025 suppressed an audit by **tightening a predicate**, changing no call and no effect:

```diff
-if (!IsPersonMerge)
+if (!IsPersonMerge &&
+    // no auditing for documents anymore …
+    (!FkDocument.HasValue || !Settings.DocumentEventCommentsStopPersonEventAudits))
 {
     TransactionDependency.Call(() => … AuditLog.Create("Patient Medical Record Changed", …).Log() …);
 }
```

Runtime consequence (source-verified): deleting or restoring a document event from the patient medical
history — `History.DeleteEventResult` → `PersonEventEntity.SetStatus` → `Save()` — **now produces no audit at
all**, because no `DocumentEntity.Save` runs on that path to emit the replacement document audit.

## What impact reported

```bash
rig impact --base e8858aa90e02-dirty --head de69fd2ffc6b-dirty --format tsv
# ep_effect_removed … audit …  →  exactly ONE EP: MedDBase.Processes.AppStartupProcesses.Startup
#                                  (unrelated — the deleted Echo actor registration)
```

`History.DeleteEventResult` shows **no audit delta**. Confirmed independently:

```bash
rig reaches "MedDBase.Pages.Patient.Medical.History.DeleteEventResult" --only audit
#   d7  audit write  Audits.AuditLog  <- PersonEventEntity.Save     ← still reachable on HEAD
```

Correct by the current model — the call site is unchanged, so the *effect set* is unchanged — and therefore
**`impact --expect-no-effect-change` would PASS this MR**, despite a real, user-visible audit regression.
For a tool positioned as an MR-review gate, that is the gap that matters most.

## Ask: promote guard conditions to first-class diff content

`ep_guard_delta` already exists but only tracks **presence** of guard-ish effects (`lock:acquire`,
`permission:assert`) — on this MR it fired once, for an unrelated lock. Extend it to **control-dependence
conditions** on the edges leading to an effect:

```
ep_guard_delta  action  Patient/Medical/History.DeleteEventResult  audit:write  Audits.AuditLog
                base:  !IsPersonMerge
                head:  !IsPersonMerge && (!FkDocument.HasValue || !StopPersonEventAudits)
                verdict: NARROWED     ← effect now fires on strictly fewer paths
```

Design notes:
- Classify each condition change as `NARROWED` / `WIDENED` / `CHANGED` (incomparable) / `UNCHANGED`.
  Cheap and sound: syntactic containment (head ≡ `base && X`) covers the common tighten-a-guard case without
  a solver; fall back to `CHANGED` otherwise.
- **`NARROWED` on an `audit:write` / `permission:assert` / `llblgen:write` is the review headline** — an
  effect that silently stops firing for a subset of inputs. Worth its own gate flag, e.g.
  `--expect-no-guard-narrowing`, which would have failed this MR.
- ~~Depends on the guard condition being captured faithfully — blocked in practice by
  [[guard-condition-renderer-divergence-tsv-llm]] (the condition currently serialises as `!!IsPersonMerge`,
  dropping the very conjunct that encodes the change).~~ **UNBLOCKED 2026-07-27.** The conjunct was never
  dropped (that reading came from comparing the base store against the head store); the real defect was
  inverted polarity on negated conditions, now fixed. The full condition text `!IsPersonMerge && (…)` is
  captured faithfully and with correct polarity, so the `NARROWED`/`WIDENED` classification below has a
  sound input. Requires a re-index of any existing store.
- Also depends on [[guard-set-direct-vs-transitive-control-dependence]] for completeness: a narrowing in an
  *outer* frame is invisible while only the innermost condition is recorded.

## Design settled 2026-07-27 (read before building — two of the notes above are now wrong)

Calibrated against the real store before writing any code. Three findings changed the shape:

**1. The prerequisite was a soundness bug, now fixed: [[guards-missing-on-lambda-and-method-group-edges]].**
0 of 65,450 argument-lambda edges carried any guard. The MR !11025 condition was captured faithfully — but on
the `Save → TransactionDependency.Call` edge, while the audit rides `Save → Save~λ3`, a *sibling*. Fixed;
**re-index before building on guard facts.**

**2. Key the diff on call EDGES, not on (EP, effect) pairs.** Even post-fix the audit effect's *own* guard set
is empty — the condition is an ancestor edge's. Effect-keyed therefore needs full transitive guard composition
(the expensive open item); edge-keyed needs only "which reviewable effects are reachable from this callee",
which the per-EP footprints already give. Edge-keyed reports *"the condition gating this edge narrowed, and
this edge leads to `audit:write`"* — the review headline, without the composition dependency. Sketch:

- per store, map guarded edge `(caller, callee)` → condition text (ignore the line, so a pure line shift is
  not a change);
- diff by that key; for each changed edge compute the reviewable effects reachable from `callee`;
- classify, then attribute to the EPs reaching `caller`.

**3. Classify by CONJUNCT SET, not syntactic containment.** The stored text is raw source — newlines, original
indentation, and the interleaved `// no auditing for documents anymore, …` comment (230 chars for this guard).
String containment on that is fragile and a comment-only edit reads as a condition change. Split the top-level
`&&` conjuncts and normalise each (strip trivia, collapse whitespace), then compare as sets:

```
base {!IsPersonMerge}  ⊂  head {!IsPersonMerge, (!FkDocument.HasValue || !…StopPersonEventAudits)}  →  NARROWED
```

Still syntactic and over-approximate — disclose it as such; `CHANGED` stays the honest fallback.

**Validation cost:** both MR stores are `-dirty` and predate the polarity fix, so they cannot be reused or
reproduced by checkout alone. End-to-end validation needs two fresh indexes. FP-calibrate the row volume on
the real store before `--expect-no-guard-narrowing` goes on by default.

## Why this is the priority item for the "useful tool" phase

The existing model answers *"can this EP still reach effect E?"*. Review needs
*"under what conditions does E fire, and did those conditions change?"*. Adding/removing effects is the easy
half and already covered; conditions are where audit suppression, permission bypasses, feature-flag gating,
and kill-switch regressions all live — and all four are currently invisible to `impact`.
