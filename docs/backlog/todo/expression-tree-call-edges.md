# Quoted references still produce call-graph edges

Schema v5 marks references inside QUOTED code (`reference_facts.InExpressionTree`: an `Expression<>`
lambda or an IQueryable clause) and the derivers now skip them for invocation EFFECTS and iteration
ANCHORS — but the references still materialize into `call_edges`, so `tree`/`reaches`/`path`/`impact`
still walk phantom calls that never execute as C# (a nav getter in a `where` clause is a SQL join).

Left in deliberately: pruning edges changes the whole reach surface and needs its own validation run
(per-EP effect-set diff against an unpruned store, plus a hand-check that no LEGIT edge dies — e.g.
provider client-evaluation of final projections, compiled expression trees). The fact is already
stored; the change is confined to GraphMaterializer (or the loaders' WHERE clauses).

Known related limits, same fact, smaller:
- `FactFieldAccess` does not carry `InExpressionTree`/`EnclosingLoopBindType`, so a static-field READ
  inside a monadic comprehension still derives `looped_effect` (fails open). Wire the two columns
  through the field-access loader when touching this.
- `foreach` over a single-value monad is not gated (only `query` contexts are) — needs the foreach's
  GetEnumerator declaring type as a fact if it ever shows up in calibration; it did not in the
  2026-08-03 40-site audit.
- A client-evaluated method call in a final IQueryable projection is now a false NEGATIVE (the whole
  clause is marked quoted). Rare; the sound direction. Constructor effects are exempt from the gate
  precisely because materialization executes them.
