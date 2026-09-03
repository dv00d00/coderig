# Guard sets are DIRECT control dependence — the `⎇` annotation under-reports the full firing condition

**Status:** todo · **Priority: LOW** (correct-by-design; a disclosure-completeness gap, not a wrong answer) · **Found:** 2026-06-28 (dogfooding `tree` on `MMS.PerformanceLogger.Factory` → `ClassFactory.CreateInstance`) · **Family:** effect-precision / disclosure-completeness
**Decision:** wontfix — moved to `wont-do/` on 2026-09-03.
**Related:** [[branch-aware-effects-shipped]] (the feature this refines)

## The observation
`tree` rendered `ClassFactory.CreateInstance` (called from `PerformanceLogger.Factory`) with a single guard
`⎇ [!(name == null || name == "")]`. The source (`MMS.Diagnostics/PerformanceLogger.cs:14-34`) gates that
call on **three** conditions, not one:

```csharp
if (instance == null)                       // L14
{
    string assembly = ...AppSettings[...];
    if (assembly == null || assembly == "")  // L17  → early return
    { instance = new NullPerformanceLogger(); return instance; }
    string name = ...AppSettings[...];
    if (name == null || name == "")          // L23  → early return
    { instance = new NullPerformanceLogger(); return instance; }
    MMS.ClassFactory factory = new MMS.ClassFactory();          // L28
    instance = (IPerformanceLogger)factory.CreateInstance(...); // L29  ← THE call
    ...
}
```

True firing condition: `instance == null  ∧  !(assembly empty)  ∧  !(name empty)`. rig shows only the
innermost (`!(name empty)`). The single guard is **correct and correctly-polarised** (L23's true arm
early-returns, so the call runs on the false arm → rendered `!(…)`); it is just **incomplete**.

## Root cause — by design
`ControlDependence.ComputeGuards` (`Rig.Analysis/Extraction/ControlDependence.cs:231`) computes canonical
**FOW _direct_ (one-hop) control dependence**: a block carries only the branch(es) on whose post-dominator
region it _directly_ sits. For a guard-clause / early-return chain the guards attach as a **chain**, not all
onto the call site:

- `CreateInstance` block → `{ (L23, !name-empty) }`
- L23 branch block       → `{ (L17, !assembly-empty) }`
- L17 branch block       → `{ (L14, instance==null) }`

The full precondition is the **transitive closure** up that chain — which nothing computes. (Same phenomenon
as the `if(a){if(b)Foo();}` → `{b=T}` characterization the branch-aware-effects tests document: direct CD =
innermost only.)

## You CANNOT recover it by walking the rendered tree
The dropped guards are exactly the branches that emit **no call node** (early-returns / enclosing `if`s with
no callee), so there is nothing in the call tree to AND. The tree lets you AND the **direct guard of each
call EDGE** along a root→effect path — a useful _inter-procedural_ composition, strictly more than one node
shows — but at **each hop** it still under-reports by that frame's own intra-method transitive ancestors.
(And ANDing predicate _text_ across methods is semantically loose: `name` here ≠ `name` elsewhere — so even
the inter-procedural AND is a disclosure hint, not a sound formula.)

**Consequence for any fix:** the closure needs each intermediate branch block's _own_ guards, and those blocks
aren't facts (no call → nothing stored), so neither render nor derive can see them. It must be computed
**where the CFG is in hand = stage-1 extraction** (walk the `ComputeGuards` array up the `BranchBlock` chain,
freeze the transitive set into `EnclosingGuards`), or by storing the full per-method block→guard map. Either
way → **MedDBase re-index**. "Close it at render" is not an option.

## The blocker that makes this non-trivial (not just "union the ancestors")
The current flat `(predicate, polarity)` set **encodes disjunction by cardinality**: a ≥2-element set means a
short-circuit `||` (`a || b` → `{a=T, b=T}`, per the branch-aware-effects characterization test). Unioning
ancestor guards into the same set makes a 2-element set _also_ mean a **conjunction of ancestors** — the two
semantics become indistinguishable in the flat set. So a faithful "full condition" needs **structure**
(conjunction-of-(disjunctions)), not a bigger flat set — i.e. a small boolean-shape change to
`EncodeGuards`/`DecodeGuards` and the renderer, plus the De-Morgan note already in the characterization test
(the `else` arm of `a||b` is `{a=F,b=F}` meaning `!a ∧ !b`).

## Recommendation (LOW)
Leave as-is for now and read the `⎇` annotation as **"the nearest gating predicate"**, a disclosure hint —
NOT the complete precondition. The current behaviour is sound (never claims a false guard); it only
under-discloses, and the must-run⇔empty-guards invariant is unaffected (must-run blocks are control-dependent
on nothing, so their transitive set is empty too). If/when a consumer needs the full firing condition
(e.g. "does this effect _really_ always run from EP X", or CEP-over-effects), do it deliberately:
1. extraction-time transitive closure over the `ComputeGuards` array (the only place the CFG exists);
2. a structured encoding (conjunction-of-disjunctions) so `||`-sets stay distinguishable from `∧`-ancestors;
3. re-index MedDBase + a fixture pinning the 3-guard `PerformanceLogger.Factory` shape.
Until then this stays a documented model boundary of [[branch-aware-effects-shipped]].

## Update 2026-07-27 — two corrections, both lowering the cost

**1. The "blocker" in the section above is largely stale.** It assumes the flat set encodes disjunction by
CARDINALITY (`a || b` → `{a=T, b=T}`). That is no longer what is stored: `EncodedGuardsFor` widens each raw
branch guard to its FULL enclosing source condition and dedups, so `if (a || b)` collapses to ONE entry
(`"a || b"`, T) and multiple entries now mean **distinct decisions that AND-join correctly**. The
conjunction/disjunction ambiguity that made a structured encoding mandatory is therefore already resolved
inside each entry's text; a transitive closure can union ancestor entries into the same flat set without
becoming ambiguous. Step 2 of the recommendation (structured conjunction-of-disjunctions) can likely be
dropped — verify against the `a||b` else-arm De Morgan case (`{a=F,b=F}` → one entry `"a || b"`=F) before
committing to that.

**2. Item 6 of [[cli-surface-and-help-refresh-2026-07]] was reassigned here on partly wrong grounds.** That
item (guarded edges pruned away by `tree --only`) was closed against this one as needing cross-method
composition. Its actual dominant cause was
[[guards-missing-on-lambda-and-method-group-edges]] — the guarded edge into an argument lambda carried no
guard at all, so no amount of composition would have found it. That is now fixed. Re-check item 6 against a
re-indexed store before assuming any of it still belongs here.

Still genuinely owned here: the intra-method early-return/guard-clause CHAIN (the `PerformanceLogger.Factory`
shape above), and the inter-procedural AND along a root→effect path that
[[impact-guard-delta-for-predicate-only-changes]] deliberately routes around by keying on edges instead.

## Wontfix — 2026-09-02

Moved from `todo/` as a **wontfix** and migrated from `done/` to the dedicated `wont-do/` terminal stage on
2026-09-03. The reason is this card's own recommendation, quoted:

> **Recommendation (LOW)** — "Leave as-is for now and read the `⎇` annotation as **"the nearest gating
> predicate"**, a disclosure hint — NOT the complete precondition."

Nothing here is undecided, which is why it is not in `needs-review/`: the behaviour is sound (it never claims
a false guard), it only under-discloses, and the full-firing-condition closure needs extraction-time work plus
a MedDBase re-index. The three reopen steps stay recorded above for the day a consumer needs the full firing
condition; the two 2026-07-27 corrections (the structured-encoding blocker is largely stale; item 6 of
`cli-surface-and-help-refresh-2026-07` was reassigned here on partly wrong grounds) stay with them.
