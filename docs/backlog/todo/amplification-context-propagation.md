# Amplification as a propagated context (retire the pair grain)

## Problem — the pair model is a pit

`n_plus_1_cross_method` materializes (anchor × witness) rows. That forces a grain choice that is
wrong at every setting:

- `maxWitnessesPerAnchor: 0` — full cross product, 56,646 rows on MedDBase. A dataset, not a finding
  surface.
- `maxWitnessesPerAnchor: 1` — 6,931 rows for only **6,437 distinct call sites**: even truncated, CHA
  fan-out (one row per candidate callee of the same call site) leaks graph plumbing into the count.
  And "nearest witness" is an arbitrary truncation of something that isn't naturally pairwise.

The reviewer's own framing (2026-08-03): *"there is an amplification context; every child is
amplified — why do we need to stop at first?"* Correct. The loop edge is the fact; everything
reachable through it inherits ×N — exactly how reachability already propagates effects and how
`--guards` propagates conditions. Nothing about the analysis should stop at the first child.

## Design — store the fact once, derive the closure at query time

"Every child is amplified" must be **visible** everywhere, but never **materialized** per child
(that is the same 56k cross product entered from the other side, and it goes stale on every rule
change). Split:

### 1. Finding = one row per anchor CALL SITE

- Key: (anchor file, line) — deduped across CHA fan-out targets. 6,437 on MedDBase, not 6,931.
- Claim: "this loop amplifies everything beneath this call."
- Payload: loop kind/source, callee (or `×N candidates`), and a compact closure summary derived at
  emit time: count of in-scope effects reachable, min depth, one sample witness (evidence pointer,
  NOT the counting unit). `maxWitnessesPerAnchor` becomes irrelevant to display and can revert to a
  dataset-only knob (or be deleted once the tsv dataset consumers move over).

### 2. Tree/web = the per-child emission, computed

In `tree --view effects|hazards` (+ web tree), any effect whose path from the root crosses a loop
edge renders amplified — cross-method, nested loops compounding (`×N`, `×N²` conceptually; render
as loop-crossing count, we never know N). Mechanically this is a fold state bit(s) carried down the
existing tree walk, the same shape as guard propagation. `--raw`/opt-out flag mirrors
`--no-amplification`.

Caveat carried on the rendering, not used to truncate: reach is path-insensitive, so a deep child
may sit on a branch this chain never takes. Depth + `⎇` guard marks are the confidence signal.

### 3. What dies

- The pair rows as a *finding* surface (the tsv dataset can stay for offline cross-tabs).
- Any plan to port `CrossMethodNPlusOneDataset` rows into `HazardsService`/`/api/hazards` as-is;
  what goes to the API is the per-anchor finding + the tree-fold amplification bit.

## Gate before on-by-default — calibration result (2026-08-03, 40-site stratified audit)

Raw: **9 TP / 7 TP-weak / 24 FP** — unshippable as-was. The FPs were three classes, two of them now
FIXED at the fact layer (schema v5, see git log + `expression-tree-call-edges.md`):

1. **Monadic comprehensions** (~13/24): query syntax over Validation/Either/first-party Tal binds ≤1
   time. Fixed generically: `reference_facts.EnclosingLoopBindType` (the bind's declaring type) gated
   through the SAME `enumeratingMethods` allow-list that keeps `Option.Map` out of lambda contexts.
   A deny-list of monad types was rejected — Tal proves the monad set is open.
2. **Expression-tree clauses** (~5/24): getters in IQueryable where/select never execute as C#.
   Fixed: `reference_facts.InExpressionTree`; effects + anchors skip quoted refs (ctor effects
   exempt — materialization executes projections).
3. **Memoized / loop-invariant receiver** (~4/24): `??=` fields, LLBLGen lazy navs on an entity
   captured OUTSIDE the loop. NOT fixed — path-sensitivity. Candidate confidence signal: "anchor
   receiver derives from the iteration variable" (adjacent to the killed key-classifier — needs
   Dmytro's call before building).

Measured on the v5 store (2026-08-04, commit 3b4888d8e681): anchors 6,437 → **2,562**; zero
monadic element types remain; query-kind anchors 4,249 → 432 while foreach held; all 8 spot-checked
audit FPs eliminated, all 8 spot-checked TPs retained. OTEL coverage (identical mapper both sides):
combined 71/79 → 66/79 — of the 5 lost tables, ≥2 were FAKE coverage via monadic anchors, and the
real losses are the quoted-query table-touch class → see `quoted-query-resource-attribution.md`.
Fresh 14-site stratified audit of the v5 surface (2026-08-04): **9 TP / 4 TP-weak / 1 FP — 93%
precision** (TP+weak; strict TP 64%), vs 40% / 22.5% pre-fix. The one FP is a NEW, smaller class:
`from w in option …` binds through System.Linq.Enumerable because LanguageExt `Option<A>` IS
IEnumerable<A> — a ≤1-cardinality ENUMERABLE the bind-type gate correctly passes. Fix if it ever
matters: record the primary from-clause source's RESOLVED TYPE as a fact and deny-list bounded-
cardinality enumerables (Option/Nullable) — data-driven, cardinality is a type property. Residual
FP classes otherwise: memoized/loop-invariant receiver, PK-bounded loops, switch-over-loop-var.
At 93% the surface is defensible for on-by-default display at the anchor grain.

## Context

- Depth distribution (MedDBase, grain-1): 0:1112, 1:1277, 2:855, 3:899, 4:2252, 5:362, 6:174.
- Witness providers: llblgen:read 4019, entity_cache:read 1668, llblgen:fetch 852,
  object_store:read 188, inproc_cache:read 117, db_command 58, redis 16, http 11.
- Related: `amplification-scope-expansion.md` (entity_cache/shared_state default-scope question).
