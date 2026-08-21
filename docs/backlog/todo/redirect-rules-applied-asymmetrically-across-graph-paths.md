# The two graph builders apply redirect rules to different row sets — latent `call_edges` divergence

**Status:** todo · **Priority: LOW** (latent: today's redirect rules only target EXTERNAL overloads, so the two
row sets coincide; it becomes a live divergence the moment a rule names a method that resolves in-source) ·
**Found:** 2026-08-21, while single-sourcing the fact projections · **Family:** query correctness / rules

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

## Fix

Decide which one is right — probably the in-memory one, since redirecting an in-source call is either meaningful
or a no-op — and make the other match. Then extend `FactGraphProjectionParityTests` with a redirect rule that
matches an IN-SOURCE method, which is the arm no gate currently covers.

## Related

- The filtering half of what [bounded reach inputs dropping `EnclosingScopes`](../done/bounded-reach-inputs-drop-enclosing-scopes-shipped.md)
  fixed for mappings. Worth an audit pass over the remaining twice-written FILTER predicates + dedup keys, which
  are now the only hand-maintained invariant between the two graph paths.
