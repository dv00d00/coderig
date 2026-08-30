# Contributing to `rig`

Product overview and the command surface live in [../README.md](../README.md). This file is the
delivery convention: how a change gets built, and where the framework knowledge is allowed to live.

## Implementation workflow

Default to contract-first TDD. For each behavior slice:

1. Add or extend a playground fixture.
2. Hand-author the expected semantic output.
3. Run the test and see it fail.
4. Implement the smallest useful miner/rule/projection change.
5. Make the test green.
6. Do an explicit refactor pass.
7. Re-run relevant tests.
8. Commit the slice.

Tests should protect semantic contracts, not implementation details. Prefer expected observations,
facts, effects, callgraph edges, and CLI output over tests that mirror internal algorithms.

**Do not generate expected results from the same code path being tested.** Expected fixtures are
hand-authored and normalized for unstable values — absolute paths, timestamps, run IDs, generated IDs,
line endings.

Use short spikes for unfamiliar Roslyn/MSBuild behavior, but either delete the spike or turn the
learning into a failing fixture test before productizing it.

Keep the current slice small enough that a fresh-context agent can understand the failing contract, make
it green, refactor, and commit without rediscovering the whole project.

## Rule-first extraction

Prefer simple targeted rules and composition over bespoke detector code.

The scalable path is to express framework knowledge as **data** whenever the shape can be described with
existing primitives: type/namespace filters, inheritance filters, invocation filters, attributes,
route-builder calls, declaring types, receiver types, file/project filters, and small composed predicates.

Custom C# extraction logic is acceptable only when the pattern cannot be expressed cleanly by extending
the rule model. In that case, first ask whether a small reusable matcher primitive would make the rule
declarative. Avoid framework-specific one-off walkers — quick locally, but they do not scale across
packs, local conventions, or user profiles.

Rule predicates compose with `AND`: every optional predicate present on a rule must match before the rule
emits. Leave a predicate absent to avoid constraining that dimension. Express `OR` as parallel rules with
the same output shape; if multiple rules fire for the same location, keep that overlap visible as
evidence rather than hiding it inside detector code.

## Progress tracking

Work is tracked as files in [backlog/](backlog/README.md) — `todo/`, `progress/`, `done/`. The directory
listing is authoritative. One commit per green tested behavior slice.

Recommended slice template:

```text
Slice: HttpClient absolute URL effect
Status: red | green | refactor | verified | committed

Contract:
  - playground code contains HttpClient.GetAsync("https://billing.test/invoices")
  - expected effect is http GET billing.test /invoices
  - confidence=high basis=compilation+profile

Verification:
  - test name or command
  - commit hash when done
```

## Build, test, ship

```powershell
.\scripts\mini-ci.ps1              # format → build → ~30 s local release tests → pack → reinstall
.\scripts\mini-ci.ps1 -FullTests   # add shared, isolated-MSBuild, and resident/live integration tests
.\scripts\format.ps1 -Check    # verify-only format pass
```

Tests are **TUnit on Microsoft.Testing.Platform**, not vstest — `dotnet test --filter` does not work.
Run a subset with:

```powershell
dotnet run --project tests/Rig.Tests --no-build -- --treenode-filter "/*/*/<ClassName>/*"
```

Deeper working notes — the effect↔reachability invariant, the two-stage dispatch model, and the cache
schema-bump rule — are in [../AGENTS.md](../AGENTS.md) / [../CLAUDE.md](../CLAUDE.md).
