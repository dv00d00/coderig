# Two surfaces, one store, disagree on a derivation input — wayfinder

**Status:** wayfinder map · 3 children, none started · **Opened:** 2026-09-02, consolidating three open cards
that already cross-reference each other · **Family:** query correctness / disclosure

## Shared root cause

Stated on the children, in their words: **a fact that is a property of the QUESTION is being derived from an
artefact of the query PLAN.** Two paths over one store — baked `call_edges` versus the query-time EF graph,
bounded SQL versus fallback loader, in-memory projection versus `Reads.LoadFactGraphAsync` — fold a different
set of derivation inputs, and nothing tells the user which one answered.

Worth a standing review question rather than a fourth rediscovery: *when this line is emitted, is it computed
from the answer, or from whatever the loader happened to hand us?*

## Children, in dependency order

1. [A rules edit does not reach the baked graph](./question-vs-plan-1-baked-call-edges-ignore-rules-edits.md) —
   HIGH; the reach set itself moves, and one `derive` run can mix baked and re-classified edges.
2. [`path` disclosures computed off the loaded subgraph](./question-vs-plan-2-path-disclosures-computed-off-the-loaded-subgraph.md)
   — MEDIUM; the answer is unaffected, the ambiguity note and `Fact graph:` line are not.
3. [Redirect rules applied asymmetrically across the two graph builders](./question-vs-plan-3-redirect-rules-applied-asymmetrically-across-graph-paths.md)
   — LOW, latent today, live the moment a redirect rule names a method that resolves in-source.

## Already known

- Child 1 is measured, not argued: on `playgrounds/LegacyNet48Web`, one added `handoffDispatchers` rule with no
  re-index changed nothing on the graph-materialized store (output byte-identical to the no-rule run) and
  correctly sync-cut the edge on `--no-graph`. Control run with no overlay: identical output.
- Child 2 is measured on `playgrounds/DeepChain`, same facts indexed twice: the EF-fallback arm prints
  `5 call edges, 4 implements edges, 17 methods` **and** the ambiguity note; the bounded SQL arm prints
  `0 call edges, 4 implements edges, 1 methods` and stays silent.
- Child 3 is latent for one reason only: every shipped `redirectRules` entry targets an external convenience
  overload, so `!TargetInSource` holds for every matching row and the two row sets coincide.
  `FactGraphProjectionParityTests` runs the playground rules and therefore cannot see it.
- `RulesFingerprint` already exists but is used only in `QueryCacheKeys`; it does not gate `call_edges`, which
  is index output rather than a cache. The store stamps only a graph schema version.

## Recommended — not yet confirmed

- **D4 — redirect predicate.** Recommendation: the in-memory builder is right (redirect every row), so relax
  the `!TargetInSource` guard in `Reads.LoadFactGraphAsync`, with a parity test naming an in-source method.
  Still Dmytro's call; child 3 carries both options and their costs and is labelled `needs-info` until it is
  taken.
- Each child's own decision or open question now lives on that child's card, not here.

## Same family, filed elsewhere

- [`/api/meta` `derivationVersion` lacked store identity](../done/cli-web-parity-3-api-meta-derivation-version-lacks-store-identity.md)
  — a fourth instance (client cache versus server cache); it lives under the CLI/web parity wayfinder for
  audience reasons, since the reader who hits it is a web user.
- [The `--intrinsic` hint counted before the reachability filter](../done/intrinsic-hint-counted-before-reachability-filter.md)
  and [bounded reach inputs dropping `EnclosingScopes`](../done/bounded-reach-inputs-drop-enclosing-scopes-shipped.md)
  — the two shipped instances that established the pattern.
