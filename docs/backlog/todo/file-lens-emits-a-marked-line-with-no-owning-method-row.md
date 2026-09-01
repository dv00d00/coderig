# The file lens can emit a marked line whose owning method has no method row

**Status:** done · **Triage:** ready-for-agent · **Found:** 2026-09-01 by a probe agent
auditing `rig annotate` · **Family:** file lens (read model)

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
- not a monomorphization redirect keyed off this call — the `ShapeGraph` mono seam is driven by rules
  `genericFactories` (`Entity.New` / `Entity.Find`), which do not name `Fill``1`.

## Remaining suspects and the cheapest next step

`Rig.Domain/Functions/FileEffectReadModelIndex.cs` builds the two row sets from DIFFERENT joins:

```
:170-176   method rows  ← reached.TryGetValue(symbol.SymbolId)      // the per-family reverse closure
:296       call-site rows ← effect.EnclosingSymbolId ∈ fileMethodIds // the effect's own owner
```

The two row sets come from DIFFERENT joins, and only one of them canonicalises:

```
:170-176   method rows    ← reached.TryGetValue(symbol.SymbolId)        // raw closure keys
:279-287   call-site rows ← edge canonicalised (MonomorphizedNodeId.BaseOf on BOTH ends) first
:296       …then effect.EnclosingSymbolId ∈ fileMethodIds
```

`CollapseInstantiations` (`:198`) rewrites the closure's KEYS to base ids after the walk, and
`BuildCallSiteKeys` canonicalises the edge before looking a key up — so the site join tolerates any id shape
the walk produced. The method-row loop does neither: it asks the collapsed dictionary for a raw declared id.
Any place where the WALK visited a node under a different id than the one the file declares therefore yields a
correct line badge and no method row. That is the shape of this bug even though the specific rewrite has not
yet been identified.

Cheapest next step — stop probing through the 47-second store path and reproduce at unit level:

1. A `FileEffectReadModelIndex.Build` test with a hand-built 3-node graph: `caller → genericCallee(seed)`,
   caller declared in the file under test. Assert the caller gets a method row at depth 1. If it passes, add
   the shaping seams (`FactPathFinder.ShapeGraph` with factory/cut/context/mono args) one at a time until the
   method row disappears — that isolates the rewrite.
2. Compare the graph the file lens walks (`WarmStore.GraphAsync` → `LoadShapedTraversalGraphAsync`) against the
   one `reaches` walks; they load through different call sites and only the latter is known to find this path.
3. Only then choose the fix: canonicalise on BOTH sides of the method-row join, or stop rewriting ids the
   closure will be queried with.

## Testing expectations

- Read-model test: a method owning a direct effect and having NO incoming or outgoing call edges still produces
  a method row at depth 0, and its own call-site row.
- A method-table/line-badge consistency invariant test: every marked line's enclosing method has a method row
  whose families are a superset of that line's families. This invariant is worth asserting permanently — it is
  exactly what the probe agent used to catch this.
- Real-store check: the `LocationsHandler.cs` case above.
- Bump the file-effects cache schema
  ([no constant today](./file-effects-artifact-has-no-cache-schema-constant.md)).

## Related

- [The lens deletes a line's depth-0 effect](./file-lens-drops-depth-zero-effect-when-the-line-also-has-a-targeted-call.md)
  — the mirror-image inconsistency (method row right, line row wrong).
- [Lambda-owned effects are omitted](./file-lens-omits-effects-owned-by-lambdas.md) — a third case of the same
  declared-methods-only filtering.
