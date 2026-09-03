# The test fixture builds `FactInvocation`s that production never produces — 4 fields silently absent

**Status:** done 2026-09-03 · **Priority: MEDIUM** (not a product bug; a GATE bug — several derivation tests run against
facts that differ from what the real pipeline emits, so an arm can look covered when it is unreachable, and an
absence can be asserted that only holds because the fixture dropped the field) · **Found:** 2026-08-21, exposed
by grouping `FactInvocation`'s members · **Family:** test infrastructure / fact projections
**Triage:** ready-for-agent

## The gap

`tests/Rig.Tests/Fixtures/FactProjection.cs:137` hand-writes its own `ReferenceFact -> FactInvocation` mapping —
a FOURTH copy of the mapping that commits `379c25ba` and `c666d8bb` single-sourced everywhere else — and it is
not field-complete. Compared with the production mapping (`FactInvocationProjection.Project`) it omits:

| omitted | production source | why it matters |
|---|---|---|
| `Nesting.Guards` | `r.EnclosingGuards` | 27 read sites in `Rig.Domain/Functions` alone; feeds guard-aware effect derivation |
| `Loop.ElementType` | `r.EnclosingLoopElementType` | the RESOLVED element type the iteration-fanout deriver pairs with `Loop.Detail` |
| `Loop.BindType` | `r.EnclosingLoopBindType` | iteration fanout |
| `InExpressionTree` | `r.InExpressionTree` | the query-bind / expression-tree arm |

Any test deriving effects through this fixture therefore sees those four as `null`/`false` **always**.

Consumers: `ExternalVirtualOverrideOrphanTests`, `FactCacheCoherenceCorpusTests`, `FactDerivationTests`,
`MultiLineHandoffTests`, `SqlReachabilityTests`, `Fixtures/ProductionFixCorpus`.

## Two distinct failure modes

1. **Unreachable coverage** — a deriver arm gated on any of the four cannot fire through this fixture, so a test
   that looks like it exercises the arm never does.
2. **False green on an absence** — a test asserting "no such effect/observation" may pass only because the
   fixture withheld the field, and would fail against real facts.

Neither shows as a failure, which is what makes it worth a ticket rather than a note.

## How it surfaced (worth recording)

Invisible until `FactInvocation`'s 21 flat members were grouped into `FactCallArguments` / `FactLoopContext` /
`FactCallSiteNesting`. Before, the four were omitted OPTIONAL parameters — indistinguishable from deliberate
defaulting. After, the fixture visibly constructs a `FactLoopContext(Kind, Detail)` with two of four members and
a `FactCallSiteNesting` with three of four. **The grouping refactor's real payoff was not readability, it was
making an incomplete construction look incomplete.**

## Fix

Point the fixture at `FactInvocationProjection.Project` — the same shared mapping production uses — so the
fixture cannot drift again.

**This is a behaviour change to the tests and must be done deliberately, not mechanically:** the four fields
start arriving populated, so any test that was (knowingly or not) relying on their absence will move. Expect
some assertions to need updating, and treat each one as a question — "was this test asserting real behaviour, or
fixture behaviour?" — rather than a value to re-baseline. That is the whole value of the ticket.

## Acceptance

1. `FactProjection.Invocations` delegates to `FactInvocationProjection.Project` and hand-writes nothing.
2. Full suite green, with every changed assertion explained as real-behaviour-not-fixture-behaviour.
3. Ideally a guard: a test asserting the fixture's output equals the production projection's for the same rows —
   the same reflection-sweep shape `FactProjectionSharingTests` already uses, which would have caught this.

## Related

- Fourth copy of the mapping single-sourced by
  [bounded reach inputs dropping EnclosingScopes](../done/bounded-reach-inputs-drop-enclosing-scopes-shipped.md).
  The first three were in production; this one is in the tests that are supposed to catch exactly this class of
drift, which is the part worth being uncomfortable about.

## Verification

- `FactInvocationFixtureProjectionTests`: 1/1 passed with every projection input non-default.
- Main suite: 1,417/1,417 passed; no existing assertion depended on the incomplete fixture behavior.
- Release build completed with 0 warnings and 0 errors.
