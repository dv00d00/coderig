# File-lens grain — wayfinder

**Status:** wayfinder map · 6 children, none started · **Opened:** 2026-09-02, consolidating five open cards ·
**Family:** file lens (read model)

## Shared root cause

One read model (`Rig.Domain/Functions/FileEffectReadModelIndex.cs`) feeds CLI `annotate`, the web lens and
Rider, and it is not at the grain those surfaces need: its two row sets are built from two different joins over
TWO closures (`reachedPerFamily` and `deterministicPerFamily`), so the rows can disagree with each other; its
vocabulary is family-grain where the rules are provider-grain; and no row can yet be explained by a concrete
route.

Coherence between the two row sets IS enforced today — but case by case, each with its own comment: the
call-site→method backfill (`FileEffectReadModelIndex.cs:289-311`), the dispatch-basis harmonisation (`:264-273`),
and `Best`'s real-beats-dispatch precedence (`:490-503`). The defect class is that coherence is enforced
per-case rather than by construction, so a case that nobody enumerated stays uncovered — child 1 is exactly
such a case. Fix the join first; everything downstream reads this read model.

## Children, in dependency order

1. [Fix the method-row join](./file-lens-grain-1-emits-a-marked-line-with-no-owning-method-row.md) — a marked
   line whose owning method has no method row. Reopened; the fallback reached the owner, not its callers.
2. [Widen to provider grain](./file-lens-grain-2-provider-grain.md) — `Provider` on the aggregate, family kept
   as grouping metadata, chunked so 66 providers fit a 64-label bitmask pass.
3. [Assert the badge/line invariant](./file-lens-grain-3-method-badge-with-no-line-that-admits-it.md) — the
   audit check both probe agents ran by hand, as a read-model test and an optional `--strict` self-check.
4. [Design `annotate --verify`](./file-lens-grain-4-annotate-verify-badges.md) — one-command verification that
   does not compare unlike depth quantities.
5. [Resolve one witness path lazily](./file-lens-grain-5-lazy-witness-path.md) — needs a stable read model, so
   it is last.
6. [Decide `provider:operation` grain on a measurement](./file-lens-grain-6-provider-operation-grain.md) —
   follow-on to child 2, blocked on it; deferred until the pairs-per-row distribution exists.

## Already known

- Child 1's cause is confirmed, and it is **not** the id-shape asymmetry this bullet used to assert: the ids
  are byte-identical in the store. It is monomorphization. `GenericMonomorphizer.Materialize`
  (`GenericMonomorphizer.cs:129-138`) replaces the callee of a fully-concrete generic edge with the
  `~mono⟨…⟩` node, effects seed from a BASE id (`FileEffectReadModelIndex.cs:136-146`), and
  `ReachedByLabelledSeeds` (`FactPathFinder.cs:1228-1243`) seeds only the id it is given — so the reverse walk
  stops at depth 0 while the line still marks off `BaseOf` (`:546`, `:585`). Full evidence on child 1's card.
- Reproduced on `LocationsHandler.cs`: 0 method rows beside 1 correct marked line. The isolated-owner fallback
  now yields `GetList db:1`, but its two callers (`GetData`, `ValueToJson`) still have neither a method row nor
  a line badge, at a real `d2` per `rig reaches`.
- The mirror case is explained: after the dispatch-disclosure fix `ImageEdit.Save` reads `echo:2?`, disclosed as
  dispatch-only — no line carried it because no markable call proved it.
- The invariant that caught both defects: a marked line's families are a subset of its owning method row's, and
  a method's badge families are a subset of (its own lines) ∪ (families reached through calls the lens marks).
- 66 effect providers in the merged MedDBase rules against a 64-label ceiling in `ReachedByLabelledSeeds`.

## Decided

- The rebuild shape is settled by a design pass, 2026-09-02: **one canonical evidence set, with the method
  table and the line table as two folds over it.** That retires the call-site→method backfill, the
  dispatch-basis harmonisation and the isolated-owner seeding, and keeps `Best` as the single `Fold`. It is
  DESIGNED, not implemented — no code has moved.
- Provider grain (child 2) is decided and unblocked — its accepted design stands and nothing here gates it.
- `provider:operation` grain is deferred to a measurement and now carries its own card, child 6.
- Each child's own open questions live on that child's card, not on this map.
- The first two bullets are settled by reading the code, not by preference. Deferring child 6 rather than
  folding it into child 2 is a RECOMMENDATION on the card, still Dmytro's call.
- Standing: any child that changes the projection or the payload owes a `FileEffectsSchema` bump.

## Related

- [Rider plugin minimal product](../done/rider-plugin-minimal-product.md) — its open lazy-witness item
  points at child 5 rather than restating the contract.
