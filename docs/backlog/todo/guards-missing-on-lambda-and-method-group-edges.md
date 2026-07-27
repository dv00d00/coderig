# Guards were missing on every non-invocation call-graph edge — conditional effects read as MUST-RUN

**Status:** ✅ FIXED 2026-07-27 (extraction; requires a re-index) · **Priority: was HIGH** (a soundness gap in
the misleading direction — rig asserted "always happens" for conditionally-created work) · **Found:**
2026-07-27, designing [[impact-guard-delta-for-predicate-only-changes]] · **Family:** extraction / branch-aware-effects
**Related:** [[impact-guard-delta-for-predicate-only-changes]] (the item this unblocks),
[[guard-set-direct-vs-transitive-control-dependence]] (the composition step that consumes this),
[[guard-condition-renderer-divergence-tsv-llm]] (the polarity fix, same session)

## The defect

Control-dependence guards were attached only to `invocation` (and static field read/write) references. The two
other kinds of **call-graph edge** carried none:

| ref kind | edges in the MedDBase store | carrying a guard |
|---|---|---|
| argument lambda (`~λN`) | 65,450 | **0** |
| all `methodGroup` | 71,690 | **0** |
| `invocation` (control) | 755,756 | 81,709 (10.8%) |

Zero out of 65,450. Two independent causes:

1. **`FactExtractor.ProcessLambda`** built the lambda's `ReferenceFact` without `EnclosingGuards`, so it
   defaulted to null.
2. **The main reference loop** derived the guard root from `structuralRoot`, which is deliberately null for a
   method group. The rationale in the comment — "no effect consumes it" — is true of *structural context* and
   false of *guards*: a method-group conversion IS an edge the tree/reaches/path walks traverse.

## Why it mattered more than a missing annotation

A `() => …` literal inside an `if` makes everything the lambda body reaches conditional. With the edge
unguarded, **every effect in that lambda's entire subtree read as must-run** — rig claiming "this always
happens" where the truth is "only when the branch is taken". Unsound in the direction that misleads a
reviewer, across an estimated ~7,000 edges (65,450 at the invocation base rate).

It was also the real cause of two things previously attributed elsewhere:

- **`impact` saw no delta for MR !11025** (the motivating case for
  [[impact-guard-delta-for-predicate-only-changes]]). The suppressed audit sits in
  `TransactionDependency.Call(() => … AuditLog … .Log())` inside the tightened `if`. The condition was
  captured faithfully on the `Save → TransactionDependency.Call` edge, but the `Save → Save~λ3` edge carrying
  the audit was unguarded — so the guard was on a *sibling* of the audit's path, not on it.
- **`tree --guards` pruning under `--only`** (item 6 of [[cli-surface-and-help-refresh-2026-07]]), which was
  closed against `guard-set-direct-vs-transitive-control-dependence` as needing cross-method composition. Part
  of it was just this.

## The fix

`EncodedGuardsFor(lambda, …)` at the lambda's creation site, and a `guardRoot` widened past `structuralRoot`
for method groups (to the member access — `BlockOf` needs an exact operation-syntax match and the bare
identifier is not itself an operation node).

The guard resolves in the CFG that **contains the literal**, which `BuildGuardGraphs` already collects
pre-order (parent before nested), so:

- a lambda nested in another lambda is guarded relative to its **own** enclosing body, not the outer lambda's
  creation site — no double-counting when a consumer composes along a path;
- the effects *inside* a lambda body stay unguarded. **The guard lives on the EDGE.** Any consumer answering
  "under what condition does this effect fire" must compose along the path — see
  [[guard-set-direct-vs-transitive-control-dependence]].

Tests: `tests/Rig.Tests/Analysis/GuardedCallGraphEdgeTests.cs` (6; 5 fail pre-fix). In-memory extraction, no
playground or index needed — same harness as `GuardPolarityTests`.

## Follow-ups this leaves open

- **Guard text carries trivia.** The stored condition is raw source: newlines, original indentation, and any
  interleaved comment (the MR !11025 guard is 230 chars and contains
  `// no auditing for documents anymore, …`). Fine for display; **not** safe for diffing, where a comment-only
  edit would register as a condition change. Any classifier must normalise per-conjunct — see the guard-delta
  item.
- **Initializer-owned lambdas still get no guard.** `EncodedGuardsFor` resolves the owner via
  `BaseMethodDeclarationSyntax or AccessorDeclarationSyntax`, so a lambda in a field/auto-property initializer
  returns null. Conservative (no false guard) and analogous to the known `F:`/`P:` initializer gap in
  CLAUDE.md's effect↔reachability section.
- **Any store indexed before 2026-07-27 has both this and the polarity bug.** Re-index before trusting
  `--guards` output or building anything on it.
