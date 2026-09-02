# `reaches`/`tree`/`path` silently lose every lexical-scope observation — the bounded SQL loader never selects `EnclosingScopes`

**Status:** SHIPPED 2026-08-21 · **Priority: HIGH** (silent under-reporting of hazard context in rig's most-used commands; the
SQL fast path and the whole-store path disagree about the SAME store, so the answer depends on which internal
loader ran) · **Found:** 2026-08-21, on MedDBase, by comparing a live-served `reaches` answer against the
store-served one ([live-background-index](../done/live-background-index.md) slice 6b) ·
**Family:** query correctness / effects

## The bug

`SqlReachability.LoadReachInputsAsync` — the bounded reach-input loader behind the `reaches`/`tree`/`path` SQL
fast path — does not select `EnclosingScopes`, and says so in a comment:

```csharp
// src/Rig.Storage/Queries/SqlReachability.cs:275-290
SELECT r.TargetSymbolId, r.EnclosingSymbolId, r.FilePath, r.Line, r.ReceiverType,
       r.FirstArgumentTemplate, r.FirstArgumentType, r.EnclosingLoopKind, r.EnclosingLoopDetail,
       r.EnclosingInvocations, r.EnclosingCatchTypes, r.TypeArguments, r.FirstArgumentName,
       r.ArgumentTemplates, r.ArgumentNames, r.EnclosingGuards, r.EnclosingLoopElementType,
       r.EnclosingLoopBindType, r.InExpressionTree
…
// Positional through FirstArgName (index 12); the new nth-argument lists are
// appended as NAMED args because EnclosingScopes (param 13) is skipped on this path.
```

`FactInvocation.EnclosingScopes` therefore arrives **null for every invocation** on that path. And it is exactly
what the scope observations are derived from:

```csharp
// src/Rig.Domain/Functions/FactEffectDeriver.cs:210
enclosingScopes: FactStructuralContext.DecodeScopes(inv.EnclosingScopes),
```

So every observation keyed on a lexical scope — `lock_held_across_effect`, `transaction_spans_effect` (the
`ordering` rule section, `scopeKind: lock` / `transaction`) — **can never fire** on `reaches`/`tree`/`path`.
`Reads.LoadInvocationRefsAsync` (whole-store, what `derive` and the EF-fallback path use) DOES select the
column, so those commands report the observations correctly.

**Two store paths, same store, different answers.** Which one you get depends on whether the SQL fast path or
the EF fallback ran.

## Measured evidence (MedDBase, one store, one rule set)

`MMS.AssemblyCache.LoadFile` takes a `Monitor.Enter` at line 21 and calls `File.ReadAllBytes` at line 35 while
holding it — a real lock-held-across-IO hazard. The store facts DO carry the scope
(`reference_facts.EnclosingScopes = 'lock\x1fSystem.Collections.Generic.Dictionary<TKey, TValue>'` for that
invocation), so this is a query-path loss, not an extraction gap.

| query, same store, same rules | result |
|---|---|
| `rig derive --format tsv` (whole-store) | `effect io read System.IO.File … AssemblyCache.cs 35 lock_held_across_effect` |
| `rig reaches DebtorOverride.SaveIncludedServices` (bounded) | `d11  io read  IO.File  <- AssemblyCache.LoadFile` — **no `⚠ lock-held-across`** |
| the same query served from the live in-memory index | `d11  io read  IO.File  <- AssemblyCache.LoadFile  ⚠ lock-held-across` |

The live path is right by accident of construction: `LiveReads.InvocationRefs` projects every field of
`FactInvocation`, having been written to mirror `LoadInvocationRefsAsync`.

Ruled out before concluding: rules (`--rules rig.rules.json` on both sides — no change), extraction era (the
column is populated in the store), and tree drift (`AssemblyCache.cs` is byte-identical between the store's
commit and the working tree; and `derive` vs `reaches` above is a same-store comparison, which removes tree
drift entirely).

## Fix

Add `r.EnclosingScopes` to the SELECT and pass it through. The comment about positional-vs-named arguments
documents the omission as if it were a constraint; it is just an ordering artefact of `FactInvocation`'s
parameter list. Check the same loader for any OTHER `FactInvocation` field it skips — the same class of silent
loss applies to each, and the audit is one read of the two projections side by side.

## Acceptance

1. `rig reaches` on a pattern reaching `AssemblyCache.LoadFile` shows `⚠ lock-held-across` on the `io read`
   effect, matching `rig derive` on the same store.
2. A test asserting the bounded and whole-store loaders produce field-EQUAL `FactInvocation` records for the
   same enclosing set — the general form, so the next skipped field fails a test instead of shipping.
   `SqlReachabilityTests.Bounded_graph_reproduces_full_graph_reach_in_both_modes` covers the GRAPH but nothing
   covered the INPUTS, which is why this survived.

## Related

- Same failure class as [`/api/meta` derivationVersion](cli-web-parity-3-api-meta-derivation-version-lacks-store-identity.md):
  two surfaces silently disagreeing about one store.
- Found by the live/store equality gate in `tests/Rig.Tests/Live/LiveReachesTests.cs`. That gate compares
  playground answers, where the divergence does NOT reproduce (no playground has a lock held across IO) — it
  took MedDBase to surface it, which is the fourth instance in this program of a gate being too small to host
  the defect under test.

## SHIPPED 2026-08-21

Fixed by single-sourcing the mapping rather than by adding the missing column, so the bug CLASS goes with it:

- `src/Rig.Domain/Functions/FactInvocationProjection.cs` — the one column set (`enum Column`, declaration order
  IS the ADO ordinal set) and the one `row -> FactInvocation` mapping.
- `src/Rig.Storage/Queries/ReferenceFactRows.cs` — the store-side row supply: SELECT list, EF projection and
  raw-ADO reader, all generated from / indexed by that enum. `(int)Column.X` is the ordinal precisely because
  the SELECT list is emitted in enum order, so a new field cannot be skipped on one path only.
- All three read paths (EF whole-store, raw-ADO bounded, in-memory `LiveReads`) now funnel through `Project`.
  The bounded loader's 19-column SELECT and its 19 hard-coded `reader.GetString(n)` calls are gone.

Verified on the real MedDBase store, the repro from this item:

```
BEFORE  d11  io read  IO.File  <- AssemblyCache.LoadFile
AFTER   d11  io read  IO.File  <- AssemblyCache.LoadFile  ⚠ lock-held-across
```

`rig derive` output on a playground store is byte-identical before/after — the whole-store path never had the
bug and did not move.

**Acceptance #2 shipped as `tests/Rig.Tests/Storage/ReachInputProjectionTests.cs`**: bounded vs whole-store
records compared by REFLECTION over every public property (so a newly-added field is covered automatically),
with anti-vacuity guards that the compared set is non-empty and that at least one record carries a non-null
`EnclosingScopes` — without which the test would pass on the very bug it exists to catch. 204 records compared
across 3 patterns, 19 carrying scopes. Mutation-checked: forcing `EnclosingScopes` back to null fails the
general reflection comparison, not merely the specific assertion.

**`TreeSchema` bumped 2 -> 3.** `tree` caches `DerivedEffect`s *including* their observations, so a warm v2 blob
would serve pre-fix effects forever — and, through `DeriveCommand.HazardFindings`, the mis-tiered
`race_window` / `lazy_init` classification those span observations drive. Verified empirically that a warm
pre-fix cache did serve stale before the bump. `EpSchema`/`HazardEffectsSchema`/`GraphHazSchema`/`ImpactSchema`
deliberately NOT bumped: all derive from the whole-store loader, which already carried the column, so bumping
them would flush the expensive impact diff for nothing.

### Corrected while fixing it

This item's fix plan assumed no playground held a lock across IO, so a new fixture would be needed.
**Wrong** — `playgrounds/LegacyNet48Web/Background/LockZoo.cs:31-34` already holds `lock (_gate)` across a SOAP
call, and `TransactionZoo.SubmitInsideTransaction` wraps one in a transaction `using`. Both were already pinned
on the WHOLE-STORE path by `FactDerivationTests`, which is exactly what made the bounded path's silence a
divergence rather than a missing fact. What the gates lacked was not the code shape but **pattern coverage**:
`LiveReachesTests` compares live-vs-store on DeepChain and EntryPointEffects, neither of which takes a lock. No
playground content was added and no existing test moved.

### Still open, same class

~60 index-based `reader.GetString(n)` reads remain in `Reads.cs` / `SqlReachability.cs` (call edges, symbol
facts, allocation facts) / `Writes.cs` / `EntryPointSiteStore.cs`, ordinal-mapped against hand-written SELECT
lists. `ReferenceFactRows` generalizes directly to them. `Writes` was deliberately left alone: its INSERT
covers all 29 columns, a wider list than the invocation subset, and it cannot silently DROP a column from a
query path (it writes every one).

The ctor/throw `SymbolRef` projections are still three hand-written copies each — fenced by a second test in the
same file rather than single-sourced, because sharing the 20-column invocation hydrator would widen their
4-5-column SELECTs (including the whole-store throw scan on `derive`) to remove a much smaller drift risk.
