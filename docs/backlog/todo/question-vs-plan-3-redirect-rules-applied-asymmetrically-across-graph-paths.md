# The two graph builders apply redirect rules to different row sets — latent `call_edges` divergence

**Status:** todo · **Priority: LOW** (latent: today's redirect rules only target EXTERNAL overloads, so the two
row sets coincide; it becomes a live divergence the moment a rule names a method that resolves in-source) ·
**Found:** 2026-08-21, while single-sourcing the fact projections · **Family:** query correctness / rules

**Triage:** needs-info — fully specified apart from the O1/O2 call below; it becomes `ready-for-agent` the
moment a predicate is picked. O1 is a recommendation, not a decision taken.

## The asymmetry

Both graph builders apply `RedirectClassifier.Redirect`, but to different rows:

- `FactGraphProjection.FromAnalysis` (in-memory; what `rig index` bakes into `call_edges`) applies it to
  **every** reference and rewrites the callee — so an **in-source** call matching a redirect rule IS redirected.
- `Reads.LoadFactGraphAsync` (query-time, whole-store) only redirects rows where `!TargetInSource`; an in-source
  ref matching a rule keeps its own target.

So a rule whose method resolves in-source makes the materialized `call_edges` disagree with the query-time EF
graph, for the same store and the same rules.

## Why it is latent, and why that is not reassuring

The shipped `redirectRules` all target external convenience overloads (the external-virtual-override-orphan
fix), so `!TargetInSource` is true for every row a rule currently matches and the two sets coincide.
`FactGraphProjectionParityTests` runs with the playground's rules and therefore cannot see it.

It survived the 2026-08-21 projection consolidation intact and deliberately: that slice single-sourced the row
→ record MAPPINGS, while this is row FILTERING, which is still written twice on purpose (see the rewritten
header of `FactGraphProjection`).

## Decision — which of the two predicates is correct

Not "is this worth fixing": it is one predicate. The decision is which one is right, because whichever loses
gets changed to match the winner.

- **O1 — the in-memory builder is right; redirect every row (recommended).** Redirecting an in-source call is
  either meaningful (the rule author wanted that call re-pointed, and their intent does not change because the
  target happens to be in-source) or a no-op. The `!TargetInSource` guard has no stated rationale, applies a
  restriction the rule grammar never advertises, and makes rule behaviour depend on whether the target's
  assembly happens to be indexed — a property of the STORE, not of the question.
  Cost: relax the predicate in `Reads.LoadFactGraphAsync`. Query-time graphs for stores whose rules name an
  in-source method start matching the baked graph; since no shipped rule names one, observable behaviour today
  does not move.
- **O2 — the query-time builder is right; restrict the in-memory one.** Defensible only if redirect is meant
  narrowly as "rewrite calls that leave the solution" — a boundary-mapping feature rather than a general
  call-rewriting one. If that is the intent it belongs in the rule documentation, because nothing currently
  says it. Cost: the same one predicate on the other side, but it also permanently forecloses in-source
  redirects, which is the larger commitment of the two.

Worth deciding while it is still latent and cheap; the day a rule names a method that resolves in-source it is
a wrong-answer bug.

## Fix — what it owes either way

**A parity test with a redirect rule that matches an IN-SOURCE method**, extending
`FactGraphProjectionParityTests` — the arm no gate currently covers. That test is the actual deliverable here;
the predicate change itself is one line. Without it the two builders can silently drift apart again the next
time either is touched.

## Related

- The filtering half of what [bounded reach inputs dropping `EnclosingScopes`](../done/bounded-reach-inputs-drop-enclosing-scopes-shipped.md)
  fixed for mappings. Worth an audit pass over the remaining twice-written FILTER predicates + dedup keys, which
  are now the only hand-maintained invariant between the two graph paths.
- [CLI/web collapse onto one engine per question](./cli-web-collapse-map.md) — relates only, and it is the same
  defect class one layer up: a question implemented twice drifts. That family collapses the query-side
  duplicates; the two graph BUILDERS named here are outside it.
