# Transaction-scope facts — is this effect inside the ambient transaction, or does it escape?

**Status:** todo · **Priority: LOW** (heaviest of the reviewer-query set: new extraction-time facts) · **Family:** extraction / reviewer-invokable queries
**Extracted from:** [reviewer-invokable-queries](../done/reviewer-invokable-queries.md) (ranked item 6), 2026-09-02
**Triage:** needs-triage

## The questions

Tag an effect as **inside the ambient transaction or escaping it** — #1784 / #716 (tx-escaping read), #536
(a throw inside a transaction rolls back the intended write), #436 (nested transaction) — and as **wrapped in
retry / idempotent** (FR-11: #1546 / #351 / #850).

## Why it is last in its family

The parent card's feasibility tiers: items 1–4 build on the existing reachability + effect graph, item 5
extends type-arg capture, and **item 6 needs new extraction-time facts**. So this one implies a write-side
change and a re-index of the evaluation target. Record:
[reviewer-invokable-queries](../done/reviewer-invokable-queries.md).

## What it interacts with

- The guard machinery is adjacent but not the same thing: a transaction scope is a lexical/dynamic region,
  not a control-dependence predicate. Do not encode it as a guard.
- `llblgen:tx_commit` already exists as ruleset vocabulary on the MedDBase side; the scope fact must not
  hardcode it in core (see [core-purity-project-vocabulary](../done/core-purity-project-vocabulary.md)).

## What counts as finishing

- A transaction-scope fact per effect site, with the scope's opener identified, and the nesting depth where
  the source shows one.
- Retry/idempotency wrapping recorded as a separate signal, not folded into the scope fact.
- Fixtures for escape, nesting, and throw-inside-scope.
- Re-index MedDBase and report how many existing effects gain a scope, before any detector is turned on by
  default.
