# The file lens can emit a marked line whose owning method has no method row

**Status:** REOPENED 2026-09-01 (partially fixed) · cause CONFIRMED 2026-09-02, fix in flight and NOT yet
verified · **Found:** 2026-09-01 by a probe agent auditing `rig annotate` · **Family:** file lens (read model)
**Triage:** ready-for-agent

**Terminal note — 2026-09-03:** the reverse seed expansion and invariant coverage shipped in `b59b6aba`.
The one remaining acceptance step is now its own human-run card:
[verify the LocationsHandler case on the MedDBase store](../todo/file-lens-grain-1-real-store-verification.md).

## Outcome

The projection now seeds isolated direct owners and derives a method-family row from every emitted call-site,
then min-merges duplicate `(method, family)` evidence. A synthetic invariant asserts that every marked line's
families are a subset of its owning method row, alongside the original monomorphized join coverage.

## What happens

```
rig annotate "…\MedDBase.DataPathMap\Handlers\Lists\Common\LocationsHandler.cs" --summary
→ 0 effectful method(s), 1 marked line(s)        # no method rows at all

rig annotate "…\LocationsHandler.cs" --format tsv
→ site   64   db!   TypedListExtension.Fill``1   # the line row exists and is correct
```

Source, `LocationsIdHandler.GetList()` (private) at line 64:

```csharp
list.Fill(0, LocationFields.Name, true, LocationFields.FkSite == pkSite, Transaction);
```

`GetList` is called by `GetData()` and `ValueToJson()` in the same file, so the expected table is `GetList db!`
plus two `db:1` callers. Instead the whole method subtree is missing from the table while the line badge is
present. Other implicit-private methods in the audit set (e.g. `PersonModelCacheService.ResolveChamberId`)
appear normally, so this is not a visibility filter.

## Reopened 2026-09-01 — the fix reached the owner, not its callers

A projection fallback now synthesises a method row from each call-site row (`site depth + 1`), so `GetList`
correctly reads `db:1`. The underlying closure defect is untouched, and it is still visible one level out:

```
rig annotate "…\Lists\Common\LocationsHandler.cs" --summary
      60  GetList  db:1          # present
      55  GetData                # ABSENT, should be db:2 (calls GetList at line 57)
      68  ValueToJson            # ABSENT, should be db:2 (calls GetList at line 70)
```

`rig reaches` confirms both callers reach the `llblgen read` at a real `d2` (not fan-out). They have no method
row AND no line badge, because the fallback only fires where a call-site row already exists, and a call-site row
needs the callee in the family closure — which `GetList` still is not.

So the fallback is a good safety net and should stay; it is not the fix. The isolation step below is still the
work. Add a regression assertion for the two callers, not just for the owner.

## Verified 2026-09-01 (facts, not hypotheses)

- **Reproduced first-hand**: `rig annotate <path> --summary --format tsv` → exactly one row, `site 64 db!
  TypedListExtension.Fill``1`, and zero `method` rows.
- **The node is in the graph.** `nodes` contains `M:…LocationsIdHandler.GetList`; `call_edges` gives it 5 out
  edges (ctor line 62, `get_Session` 63, `Fill``1` + `get_Name` + `get_FkSite` all on 64) and 2 in edges
  (`GetData` 57, `ValueToJson` 70). The edge to `Fill` is `Kind=invocation`, no handoff, `NonVirtual=0`,
  `ReceiverType=LocationTypedList`.
- **Ids match byte-for-byte** between `symbol_facts` and `call_edges` (both `…LocationsIdHandler.GetList`, no
  parameter list), so the `fileMethodIds` join cannot be missing it — and the site row proves it isn't.
- **The forward oracle sees the effect**: `rig reaches "LocationsIdHandler.GetList"` →
  `d1  llblgen read  ORMSupportClasses.TypedListBase<T>  <- TypedListExtension.Fill``1`. The expected method
  row is therefore `db:1`.
- **The seed exists in the closure.** The overload `GetList` calls is the 6-parameter
  `Fill``1(TypedListBase{``0},Int64,EntityField,Boolean,IPredicate,ITransaction)` at
  `MedDBase.DataAccessTier/MMSHelperClasses/TypedListExtension.cs:64-76`; annotating THAT file yields
  `method 64 76 Fill db!` — depth 0, i.e. it is a db seed. (There are 11 `Fill` overloads; all 11 are seeds.)

So the reverse closure holds the seed at 0 and does not hold the seed's direct caller.

Ruled out by inspection:

- not a missing graph node (above);
- not a traversal cut — the only cuts configured are `ServiceHelper.CreateService`, `IService.Startup`,
  `ProvideService``1` (`meddbase-analysis/rig.rules.json` `traversalCuts`), and `Predecessors`
  (`FactPathFinder.GraphIndex.cs:231-267`) gates on nothing else;
- not a missing reverse edge — `BuildReverseMapsCore` (`:164-184`) adds `rev.Callers[Callee] += Caller` for
  every non-handoff edge, unconditionally;
- the rules' `genericFactories` (`Entity.New` / `Entity.Find`) do not name `Fill``1`, so the `ShapeGraph`
  factory seam is not what redirects this call. **This bullet originally concluded "not a monomorphization
  redirect" — that conclusion was wrong.** The redirect is driven by the method-type-argument instantiation
  inventory, not by the factory rules; see the confirmed cause below.

## Confirmed cause 2026-09-02 — monomorphization, unit-reproduced

The earlier diagnosis on this card, an id-shape asymmetry where "the walk visited a node under a different id
than the one the file declares", is **DISPROVEN**. On the real store (`meddbase-analysis/.rig/409c330b99dd/rig.db`)
`call_edges.ToSym` for the line-64 invocation in `GetList` and `reference_facts.EnclosingSymbolId` for the
effect owner are the same string, byte for byte:

```
M:MedDBase.DataAccessTier.TypedListClasses.TypedListExtension.Fill``1(SD.LLBLGen.Pro.ORMSupportClasses.TypedListBase{``0},System.Int64,SD.LLBLGen.Pro.ORMSupportClasses.EntityField,System.Boolean,SD.LLBLGen.Pro.ORMSupportClasses.IPredicate,SD.LLBLGen.Pro.ORMSupportClasses.ITransaction)
```

The real cause: the seed is a BASE id, the graph the walk sees carries only the CONCRETE instantiation, and the
reverse seed set is never widened from one to the other. Four steps, each anchored:

| step | anchor | what happens |
| --- | --- | --- |
| the edge is redirected | `Rig.Domain/Functions/GenericMonomorphizer.cs:129-138` | `Materialize` REPLACES the callee of every incoming edge whose generic binding is fully concrete with the `~mono⟨…⟩` node; its comment reasons that "keeping both would still reach base M". The store path always materializes (`Rig.Storage/Queries/Reads.cs:573-580`, via `WarmStore.GraphAsync`). |
| the seed is a base id | `FileEffectReadModelIndex.cs:136-146` | `ownersPerFamily` is seeded from `effect.EnclosingSymbolId`, which is always a base id because effects derive on the base (`MonomorphCollapse.cs:10`). |
| the walk stops at depth 0 | `FactPathFinder.cs:1228-1243` (`ReachedByLabelledSeeds`) | it seeds only the exact id it was given, while `BuildReverseMapsCore` (`FactPathFinder.GraphIndex.cs:184-189`) keys `rev.Callers` on the SHAPED callee. So `rev.Callers[Fill``1]` holds no concrete caller and there is nothing to expand. |
| the line still marks | `FileEffectReadModelIndex.cs:546`, `:585` | `BuildCallSiteKeys` tests `reached.ContainsKey(BaseOf(edge.Callee))`, and base `Fill``1` IS in `reached` — as the seed. Hence a correct line badge beside an empty method table. |

Store corroboration:

- `GetList`'s line-64 edge carries `MethodTypeArgBinding = ["C:MedDBase.DataAccessTier.TypedListClasses.LocationRow"]`
  — fully concrete, so it is redirected.
- All ten bound `Fill``1` overloads have ≤29 distinct instantiations, under the 50 cap at
  `GenericInstantiationInventory.cs:35`, so none of them falls back to CHA and escapes the redirect that way.

## Fix in flight — implemented, NOT yet verified

A writer is adding the reverse twin of the forward `MonomorphCollapse`: `GraphIndex`
(`FactPathFinder.GraphIndex.cs:308`) gains `InstantiationsByBase`, populated where `Nodes.Add` runs (`:452-453`,
`:538`); `ReachedByLabelledSeeds` and `ReachedByAny` then seed `seed ∪ InstantiationsByBase[seed]`.

The semantics that justify it: an instantiation's body is a clone of the base body, so it performs the base's
effects. Anything reaching the instantiation reaches the base's effects.

This is unverified at the time of writing — neither the unit reproduction's inverse nor the
`LocationsHandler.cs` real-store check has been re-run against the change. Do not treat the card as closed on
the strength of the diff.

## Testing expectations

- Read-model test: a method owning a direct effect and having NO incoming or outgoing call edges still produces
  a method row at depth 0, and its own call-site row.
- A method-table/line-badge consistency invariant test: every marked line's enclosing method has a method row
  whose families are a superset of that line's families. This invariant is worth asserting permanently — it is
  exactly what the probe agent used to catch this.
- Real-store check: the `LocationsHandler.cs` case above.
- Bump the file-effects cache schema
  ([no constant today](../done/file-effects-artifact-has-no-cache-schema-constant.md)).

## Related

- [The lens deletes a line's depth-0 effect](../done/file-lens-drops-depth-zero-effect-when-the-line-also-has-a-targeted-call.md)
  — the mirror-image inconsistency (method row right, line row wrong).
- [Lambda-owned effects are omitted](../done/file-lens-omits-effects-owned-by-lambdas.md) — a third case of the same
  declared-methods-only filtering.
