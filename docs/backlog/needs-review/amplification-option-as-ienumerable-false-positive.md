# Amplification FP — `from w in option …` binds through `System.Linq.Enumerable`

**Status:** todo · **Priority: LOW** (one FP in a 14-site audit of the v5 surface) · **Family:** amplification / precision
**Extracted from:** [amplification-context-propagation](../done/amplification-context-propagation.md)
("What remains" item 3), 2026-09-02
**Triage:** needs-triage

## The problem

The single false positive in the fresh 14-site stratified audit of the v5 amplification surface
(2026-08-04): `from w in option …` binds through `System.Linq.Enumerable` because LanguageExt's `Option<A>`
IS `IEnumerable<A>`. It is a **≤1-cardinality enumerable** that the `EnclosingLoopBindType` gate correctly
passes, since that gate's job is to admit real enumerables.

This is a new, smaller class than the monadic-comprehension FP the v5 fix retired (query syntax over
Validation/Either/first-party Tal binds, ~13 of 24 FPs, now zero).

## The fix the parent card already specified

Record the **primary from-clause source's RESOLVED TYPE** as a fact, then deny-list bounded-cardinality
enumerables (`Option`, `Nullable`). Data-driven and defensible: cardinality is a property of the type, not of
the call site. A deny-list of *monad* types was rejected for the earlier class because Tal proves the monad
set is open; a bounded-cardinality enumerable list is a different, closed shape.

## What counts as finishing

- A from-clause resolved-type fact (extraction change → re-index).
- The bounded-cardinality deny-list is rules data, not a literal in core C# — the core-purity rule applies
  (see [core-purity-project-vocabulary](../done/core-purity-project-vocabulary.md)).
- The audited `Option` site no longer produces an anchor; the 9 TP / 4 TP-weak sites of that audit are
  retained.
