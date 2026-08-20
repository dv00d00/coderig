# Fact-store patch audit — what "replace project P's fact slice" costs today

> Read-only audit, 2026-08-20. Question: for a resident process with a live background index, what does it
> take to re-extract ONE project and patch its fact slice into `.rig/<store-id>/rig.db` (or into an
> in-memory overlay), and what breaks. Every claim is `file:line` or a query actually run against
> `C:\Git\meddbase-analysis\.rig\ae2cdb64e1cb\rig.db` (3.9 GB, read-only). Inferences are labelled
> **INFERRED**.

## Measured baseline (the store this has to work on)

`.rig/ae2cdb64e1cb/rig.db` — 3,907,465,216 bytes, `page_size=4096`, `page_count=953,971`,
`journal_mode=delete` (**not** WAL), `meta = (0, index_schema 5, graph_schema 1)`.

| table | rows |
| --- | --- |
| `symbol_facts` | 445,163 (421,953 distinct `SymbolId`) |
| `reference_facts` | 2,437,000 |
| `allocation_facts` | 246,051 |
| `dispatch_facts` | 30,067 |
| `type_relation_facts` | 17,993 |
| `source_files` | 12,093 (226 distinct `ProjectName`) |
| `assemblies` / `solution_membership` | 220 / 220 |
| `di_registrations` | 204 |
| `runs` | **1** |
| `call_edges` / `dispatch_edges` / `nodes` | 631,376 / 10,496 / 300,911 |
| `symbol_fts` / `ref_target_fts` | 421,953 / 285,628 |
| `entry_point_sites` | 9,590 |

Fact-mass skew (computed by joining `reference_facts.EnclosingSymbolId` → `symbol_facts.DefiningAssembly`):

| assembly | symbols | % | refs | % |
| --- | --- | --- | --- | --- |
| MedDBase.DataAccessTier | 212,445 | 50.3 | 934,470 | 38.3 |
| MedDBase.Pages | 67,884 | 16.1 | 667,739 | 27.4 |
| MedDBase.ServiceLayer | 19,970 | 4.7 | 132,262 | 5.4 |
| …218 others | — | 28.9 | — | 28.9 |

Median project: **154 symbols**. Two projects hold **66% of symbols and 66% of references**. This number
governs the whole design: "patch one project" is a small edit for the median project and a
one-third-of-the-store rewrite for the two projects a MedDBase dev actually edits.

Cross-project coupling, same join:

- in-source reference edges: 1,777,675, of which **642,147 (36.1%) cross an assembly boundary**
- `call_edges`: 631,376, of which **255,002 (40.4%) cross an assembly boundary** (29,286 have an endpoint
  with no `symbol_facts` row)
- `dispatch_edges`: 10,496, of which **4,517 (43.0%) cross an assembly boundary** (10,214 `roslyn`, 282
  `heuristic`)
- `dispatch_facts`: 30,067, of which 6,379 cross-assembly with both ends known and **14,297 have an endpoint
  that is not in `symbol_facts` at all** (external/binary-referenced members)
- `type_relation_facts`: 17,993, of which 2,000 cross-assembly and **8,208 have an unknown endpoint**

---

## 1. Write path — is an in-place patch even expressible?

**How rows are written.** `Writes.SaveAsync` (`src/Rig.Storage/Queries/Writes.cs:46`) is the only fact
writer. It:

1. `EnsureCreatedAsync`, then applies `StorageProbes.Profile.BulkWrite`
   (`Writes.cs:60`, pragma set at `src/Rig.Storage/Queries/StorageProbes.cs:50-51`:
   `journal_mode=OFF; synchronous=OFF; mmap_size=4GB; locking_mode=EXCLUSIVE`).
2. Writes the `runs` header + `source_files` + `di_registrations` through EF (`Writes.cs:83-87`).
3. `SaveFactsBatchedAsync` (`Writes.cs:295`) — raw ADO, one reused prepared `INSERT` per table stepped once
   per row (`InsertRows`, `Writes.cs:555-605`), all five fact tables inside **one** transaction
   (`Writes.cs:315-538`).
4. Upserts the assembly registry (`WriteAssemblyRegistryAsync`, `Writes.cs:114`).
5. Stamps `meta.index_schema_version` and resets `graph_schema_version` to NULL (`Writes.cs:105`).

**Publish model.** `IndexCommands.RunIndexAsync` decides at `src/Rig.Cli/Commands/IndexCommands.cs:365-366`:

```
var appendMode = identity is not null || merge;   // mine, or --merge into an existing store
var atomicPublish = !appendMode;                  // replace-via-rename for a standalone index
```

A standalone `rig index` writes to `rig.db.tmp` (`IndexCommands.cs:378`), deletes the published file and
`File.Move(..., overwrite: true)` (`IndexCommands.cs:428-432`). So the default publish IS whole-file. But
**the append mode already writes the live `rig.db` in place** (`IndexCommands.cs:378` picks `finalDbPath`
directly), and `rig graph` mutates the published store in place unconditionally
(`GraphMaterializer.cs:115-121`, and the comment at `GraphMaterializer.cs:166-169` says so explicitly).
`Writes.AssertAppendableAsync` (`Writes.cs:34`) exists purely to guard that in-place path.

So: **in-place mutation is not architecturally forbidden — it is an existing, exercised mode.** What does
not exist is *replacement*.

**The gap: there is no delete or update path for facts, anywhere.** `rg 'DELETE FROM|ExecuteDelete|RemoveRange'`
over `src/` returns exactly three hits — `GraphMaterializer.cs:115`, `:116` (derived edge tables) and
`QueryCache.cs:49` (the cache). Every fact table is **insert-only**. `--merge`/`--identity` *accumulate*;
nothing has ever removed a fact row. A patch therefore needs new machinery: a scoped `DELETE` (item 2) plus
a re-insert, in one transaction, on a connection that must NOT use the `BulkWrite` profile (`journal_mode=OFF`
+ `locking_mode=EXCLUSIVE` on the live published file would make a crash mid-patch leave a corrupt *published*
store rather than a discarded temp — the exact hazard the comment at `Writes.cs:13-18` says the profile is
only safe against because the target is a throwaway).

**Concurrency.** The published store is `journal_mode=delete` (measured). Readers open `Mode=ReadOnly`
(`RigDbContext.cs:51-54`, `TraversalGraphLoader.cs:30-40`) but that is not a lock exemption: with a rollback
journal a write transaction takes an EXCLUSIVE lock over the whole file, so **every concurrent reader gets
`SQLITE_BUSY` for the duration of the patch**. WAL would fix reader concurrency — and would break cache
invalidation (item 5).

**Minimal path (A):** a new `Writes.ReplaceProjectSliceAsync(context, assemblyName, AnalysisResult)` that (a)
opens with a journaling profile, (b) `DELETE`s the slice per item 2, (c) re-inserts via the existing
`InsertRows` helper, (d) does NOT drop the secondary indexes (item 3), (e) re-stamps `meta` with
`graph_schema_version = NULL` so `SchemaGate.GraphAvailableAsync` (`SchemaGate.cs:76`) degrades readers to
the slow path until the graph is re-baked. Roughly one new file; no schema change.

**Verdict:** the publish model is whole-file only by *default*; in-place writing already exists for
append/graph, so patching is expressible — but replacement (delete + reinsert) is entirely new code, must
abandon the `BulkWrite` pragma profile, and will lock out readers for the patch's duration under the
current `journal_mode=delete`.

---

## 2. Row ownership — can you name project P's rows?

`RunId` is useless for this. There is **one run for the whole 220-assembly solution** (measured), and every
fact table's PK is `(RunId, <table>Index)` (`RigDbContext.cs:74,82,90,105,118,126,133`) — a dense per-run
ordinal, not an identity. Worse, the read side is deliberately run-agnostic: `RunId` appears in **zero**
query paths (`rg RunId src/Rig.Cli src/Rig.Domain` → nothing; `runs` is read only by `rig runs` at
`Reads.cs:85`), and the composite `(RunId, …)` indexes were deliberately removed because "nothing filters by
RunId" (`RigDbContext.cs:95-99, 110-112`). So run-granular replacement is not a hook that exists.

Per table:

| table | project column? | ownership route | cost |
| --- | --- | --- | --- |
| `symbol_facts` | **yes** — `DefiningAssembly` (`SymbolFactEntity.cs:18`) | direct | full scan, no index on the column: **0.1 s** measured (`count(*) where DefiningAssembly='MedDBase.ServiceLayer'` → 20,577) |
| `source_files` | **yes** — `ProjectName` (`SourceFileEntity.cs:9`) | direct | trivial (12k rows) |
| `assemblies` / `solution_membership` | **yes** — keyed by `AssemblyName` (`AssemblyEntity.cs:9`, `SolutionMembershipEntity.cs:9`) | direct | trivial; note `solution_membership` is **write-only** in the whole codebase (read nowhere outside `Writes.cs:185-190`'s own dedup) |
| `reference_facts` | **no** — `TargetAssembly` (`ReferenceFactEntity.cs:10`) is the *callee's* assembly, not the owner's | join `EnclosingSymbolId` → `symbol_facts.SymbolId`; precedent exists at `Reads.cs:1443-1447` | **0.2 s** measured for one assembly (uses `IX_reference_facts_EnclosingSymbolId`). **9,028 rows (0.37%) have no resolvable owner** (8,994 NULL `EnclosingSymbolId` + 34 dangling) |
| `allocation_facts` | **no** — `EnclosingSymbolId` + `FilePath` (`AllocationFactEntity.cs:9-10`) | same join | cheap; **0 unresolvable** measured |
| `type_relation_facts` | **no** — only `TypeSymbolId`/`RelatedSymbolId`/`RelationKind` (`TypeRelationFactEntity.cs`) | join `TypeSymbolId` | **8,208 / 17,993 (46%) have an endpoint with no `symbol_facts` row** — external base types; owner is inferable only from the *other* end |
| `dispatch_facts` | **no** — `SourceMember`/`TargetMember`/`Kind` (`DispatchFactEntity.cs`) | ambiguous by construction | **14,297 / 30,067 (48%) have an endpoint outside `symbol_facts`**; 6,379 of the rest are cross-assembly. An `impl` row for "P's class implements B's interface" belongs to *both* P and B |
| `di_registrations` | **no** — `FilePath` only (`DiRegistrationEntity.cs:17`) | FilePath → project | 204 rows, so free — but see the FilePath finding below |

**FilePath-prefix ownership is unsound on the real target — measured, not theoretical.** Deriving each
project's common source-file directory from `source_files`:

- 226 projects; **`MedDBase.Pages` has NO common directory root at all**;
- `MedDBase.DataAccessTier`'s common root collapses to `…\src` — the repo source root — which then
  *contains* **222 other projects' roots**;
- **222 nested/ambiguous root pairs** total.

So "the affected project = the changed file's directory prefix" cannot be made correct here. Conversely
`(FilePath → ProjectName)` via `source_files` IS a function: 12,093 rows / 12,093 distinct `FilePath` / **0
paths owned by more than one project**. That table is the right file→project oracle; directory prefixes are
not.

One more wrinkle: `SymbolId → DefiningAssembly` is *nearly* a function but not quite — **42 `SymbolId`s appear
under more than one `DefiningAssembly`** (multi-targeted / partial-class duplicates). A delete keyed on
"symbols of assembly P" will therefore occasionally touch a row a sibling assembly also claims.

**Design gap, stated plainly:** four of the nine tables carry **no project/assembly column** and three of
those four cannot be attributed to one project even in principle (`dispatch_facts` and 46% of
`type_relation_facts` are relations *between* projects; ~0.4% of `reference_facts` have no owner). The
minimal fix is to add a `DefiningAssembly`-style owner column at extraction time to `reference_facts`,
`allocation_facts`, `type_relation_facts` and `di_registrations` (a `SchemaVersion.Index` bump, so every
store is re-indexed once) and to define `dispatch_facts` ownership as **the source member's assembly** with
the explicit consequence that patching P must also re-mine every project that declares a base/interface P
implements.

**Verdict:** ownership is *derivable today* for `symbol_facts`/`source_files`/`assemblies` and cheaply
derivable by join (0.2 s) for `reference_facts`/`allocation_facts` — but `dispatch_facts` and half of
`type_relation_facts` have no single owner even conceptually, FilePath-prefix ownership is measurably broken
on this repo, and the missing owner columns are a real design gap that needs a schema bump before a patch
path can be honest.

---

## 3. Indexes and FTS — incremental or rebuild?

**The 8 fact indexes.** `SaveFactsBatchedAsync` calls `DropSecondaryIndexesAsync`
(`Writes.cs:313` → `:611`) which drops every index with non-NULL `sql` on the five tables in
`FactTableNames` (`Writes.cs:276-283`) and re-runs the captured `CREATE` statements at
`Writes.cs:542-549`. The 8 are exactly: `IX_symbol_facts_SymbolId`, `IX_symbol_facts_Name`,
`IX_reference_facts_TargetSymbolId`, `IX_reference_facts_EnclosingSymbolId`,
`IX_type_relation_facts_TypeSymbolId`, `IX_type_relation_facts_RelatedSymbolId`,
`IX_dispatch_facts_SourceMember`, `IX_allocation_facts_EnclosingSymbolId` (confirmed against
`sqlite_master`).

**Latent bug worth flagging:** the comment at `Writes.cs:290-294` says the in-place append path "keeps its
indexes: the table already holds prior writers' rows, so a drop + global rebuild would be slower and is
unsafe under concurrent writers" — and it also refers to a `fastBulkWrite` parameter that **no longer exists
in the signature**. The call at `Writes.cs:313` is **unconditional**. So today `--merge` already pays a full
global index rebuild, and any patch path naively reusing `SaveFactsBatchedAsync` inherits that. A patch must
NOT drop indexes: a scoped delete+reinsert touches ~1% of rows for the median project, and maintaining 8
indexes over ~20k rows is far cheaper than rebuilding them over 3.1M.

Cost of the rebuild if you do pay it (measured on a scratch DB, `journal_mode=off`, 400 MB cache, with the
two hot 2.4M-row `reference_facts` columns as a proxy — the real table has 29 columns, so this is a **floor**):
`CREATE INDEX (EnclosingSymbolId)` **3.5 s**, `CREATE INDEX (TargetSymbolId)` **2.9 s**. Call the 8 together
**~7-15 s**, INFERRED.

**FTS5.** `symbol_fts` and `ref_target_fts` are `DROP TABLE IF EXISTS` + `CREATE VIRTUAL TABLE` + full
repopulate on every graph build (`GraphMaterializer.cs:198-251`). There is no incremental path in the code
at all. Measured rebuild cost, reading the real store's rows and inserting into a scratch trigram FTS5
(journal off, so again a floor): **`symbol_fts` 421,953 rows in 8.2 s; `ref_target_fts` 285,628 rows in
4.3 s; 422 MB written.** So **~12.5 s and ~420 MB of the 28.7 s / 910 MB graph phase is FTS alone.**

FTS5 itself supports incremental `INSERT`/`DELETE` fine — both tables are content-owning, not contentless.
The blocker is *finding* the rows: `symbol_fts.assembly` is declared `UNINDEXED`
(`GraphMaterializer.cs:205`), so `DELETE FROM symbol_fts WHERE assembly = 'P'` is a full scan of
`symbol_fts_content` (measured **0.2 s** warm — acceptable). `ref_target_fts` is worse: it holds
`DISTINCT TargetSymbolId` across the whole store (`GraphMaterializer.cs:248`), including BCL/external
targets, so a target that P was the last referencer of must be removed and one still referenced elsewhere
must not — that is a global refcount, not a per-project set. **INFERRED:** the honest incremental rule for
`ref_target_fts` is "insert P's new targets; never delete" (monotonic over-inclusion, which only costs
false hits in `rig refs` substring search), with a full rebuild on the periodic re-snapshot.

**Verdict:** nothing about the index/FTS layer is incremental today — it is drop-and-rebuild by construction,
~12.5 s + ~420 MB for FTS and ~7-15 s for the 8 fact indexes, i.e. **the rebuild alone exceeds any plausible
per-keystroke budget**. Both *can* be made incremental (`symbol_fts` cleanly; `ref_target_fts` only as
insert-only over-inclusion), and the patch path must stop dropping the 8 fact indexes — which also fixes an
existing unconditional-drop bug on the `--merge` path.

---

## 4. Derived edge tables — is a partial re-bake sound?

Today: total rebuild. `GraphMaterializer.BuildFromGraphAsync` does
`DELETE FROM call_edges; DELETE FROM dispatch_edges;` then re-inserts everything in one transaction
(`GraphMaterializer.cs:112-121`), `DROP TABLE`s and rebuilds `nodes` (`:334-352`) and both FTS tables
(`:198-251`), then `PRAGMA analysis_limit=400; ANALYZE` (`:146-147`), then stamps
`graph_schema_version` (`:154`). `EnsureSchemaAsync` even `DROP TABLE IF EXISTS call_edges` on every run
(`:363`) deliberately, so the table's *shape* can evolve.

Is a per-project re-bake sound? Split by table:

**`call_edges` — yes, mostly.** These are a near-mechanical projection of `graph.CallEdges`
(`FactPathFinder.AllCallEdges`, `FactPathFinder.cs:1110-1129`), i.e. of `reference_facts`, so an edge's row
is owned by its `FromSym`'s project. Two shaping passes break pure locality, both baked in deliberately:
`RewriteGenericFactories` (`GraphMaterializer.cs:86`) rewrites `caller → Factory<X>` into `caller → X.Target`
— the caller and `X` can be in different projects — and `AddDeliveryEdges` (`:100`) joins publishers to
handler(s) across the whole store via `Reads.LoadDeliverySitesAsync`. So a patch to P can legitimately need
edges whose `FromSym` is in Q. **INFERRED but well-grounded:** delete+reinsert `call_edges WHERE FromSym ∈ P`,
then re-run the two shaping passes over the union of P plus any publisher/factory site whose resolution
mentions a P symbol. Measured coupling: 40.4% of `call_edges` already cross assemblies, so the
"which other projects does this touch" set is not small.

**`dispatch_edges` — no, not soundly, not per-project.** `AllDispatchEdges` (`FactPathFinder.cs:1082-1090`)
is `BuildIndex(graph, narrowDispatch: false)` over the **whole** `FactGraphData`, then `DispatchTargets` for
**every node**, receiver-blind. `BuildIndex` (`FactPathFinder.GraphIndex.cs:283`) constructs whole-program
lookups: `MethodsByStrippedType`, `ImplsByInterface`, `ImplsByErrorInterfaceName` (an interface **simple-name**
bucket for unresolved `!:` error types — `GraphIndex.cs:201-206`), `MinedDispatchBySource`, and
`StrippedBaseEdges` + a transitive-descendant closure (`GraphIndex.cs:217-226`). Three consequences:

1. Adding a type in P that implements `IFoo` declared in B **adds rows whose `FromSym` is in B**. Scoping the
   delete by `FromSym ∈ P` would never remove or add them. Measured: **43.0% of `dispatch_edges` already
   cross assemblies.**
2. The `!:`-simple-name recovery bucket is keyed by an unqualified name, so renaming a type in P can perturb
   dispatch for every project sharing that simple name — a blast radius that isn't even expressible in the
   reference graph.
3. `dispatch_edges` is documented as the **sound superset that bounds the SQL reachability load**
   (`FactPathFinder.cs:1077-1081`, `SqlReachability.cs:15,75`). Under-populating it does not merely lose
   precision — the bounded subgraph stops containing every edge a narrowed traversal could visit, so
   `reaches`/`tree` silently **under-report**. A partial re-bake that misses an edge is a false negative in
   the product's core claim.

`nodes` is a `UNION` over both edge tables plus every `Kind='method'` symbol (`GraphMaterializer.cs:342-346`)
and is `INSERT OR IGNORE` into a `WITHOUT ROWID` PK table — trivially patchable for additions, but a
*removed* method needs a reverse check ("is this sym still an endpoint anywhere?"), which is two indexed
lookups. Cheap. `ANALYZE` is whole-store and cheap enough to skip on a patch (stale `sqlite_stat1` costs plan
quality, not correctness).

Correctly-scoped `dispatch_edges` re-bake set, **INFERRED**: all projects declaring a base type or interface
that any type in P inherits/implements, transitively upward; plus all projects declaring a type whose simple
name collides with a renamed/added P type; plus P itself. On MedDBase, with `MedDBase.DataAccessTier` as the
root of most hierarchies, that set is close to the whole solution for any edit to a DAL type.

**Verdict:** `call_edges` and `nodes` are patchable with a modest cross-project fringe; **`dispatch_edges` is
whole-program by construction and a per-project re-bake is NOT sound** — it would produce silent false
negatives in `reaches`/`tree`, which is worse than being slow. Either re-bake `dispatch_edges` wholesale on
every patch (it is the cheap table: 10,496 rows, sub-second once the graph is in RAM — the expense is the
`FactGraphData` build, not the insert) or hold it in memory and never write a partial version to disk.

---

## 5. Cache invalidation — the decisive finding

The three-axis hedge in `CLAUDE.md` is real on the **server**: `QueryCacheKeys.StoreKey`
(`src/Rig.Cli/Caching/QueryCacheKeys.cs:56-67`) is `$"{info.Length}:{info.LastWriteTimeUtc.Ticks}"`, folded
into `EpCacheKey`, `TreeCacheKey`, `HazardEffectsCacheKey`, `GraphHazardFindingsCacheKey`, `ImpactCacheKey`
(`:71-158`) and into `WarmStore`'s in-process LRU key (`WarmStore.cs:63,77,108`). An in-place patch changes
size+mtime, so every server-side artifact for the store misses. **That is correct behaviour, not
over-invalidation** — a patch genuinely changes the facts every one of those artifacts is a function of, and
the store is the whole unit those keys describe. There is no per-project key to be more precise with, and
inventing one would mean proving which artifacts a project's facts can't affect — which item 4 just showed
is false (40%+ of edges cross projects).

Three things make it worse than a miss, though:

**(a) It is not a miss, it is a purge.** `QueryCache.Open` runs
`DELETE FROM artifact_cache WHERE store_key <> $sk` (`src/Rig.Storage/Queries/QueryCache.cs:47-52`) on every
open. The first query after a patch **destroys the store's entire `cache.db`** — 15,499,264 bytes on the
MedDBase store — including impact diffs that cost minutes (`ImpactCacheKey` comment, `QueryCacheKeys.cs:150-155`)
and the >1 MB forests. A live index at file-save granularity means this happens on every save. That is
precisely the failure mode the MVID hedge was removed for on 2026-07-06 (`QueryCacheKeys.cs:16-20`), only
worse: MVID moved on recompiles of *rig*, this would move on every edit of the *target*.

**(b) The web client does NOT invalidate on store content at all.** `/api/meta`'s `derivationVersion` is
`hash(DerivationSchemaToken() + "|" + rulesHash)` — `src/Rig.Cli/Web/RigApiEndpoints.cs:336-343`. **No store
identity.** The client key is `version + "|" + key` (`src/Rig.Cli/wwwroot/api.js:78-90`) where `key` is e.g.
`tree|${storeId}|${from}|…` (`api.js:111-115`) and `storeId` is the store **directory id string** from
`/api/runs`. So the client's only content signal is the store-id *name* changing. `StoreLayout.NewStoreId`
returns the short sha, or sha + `-dirty` (`StoreLayout.cs:87-96`) — **stable across re-indexes of the same
working state**. A live/mutable store keeps one id forever, so **every browser would serve stale trees,
effects, hazards and impact diffs indefinitely after a patch, with no signal and no schema bump to blame.**
This is a latent bug today (re-indexing the same dirty commit already hits it) that a live index converts
from rare-and-benign into permanent-and-wrong. Any live-index work must fix it — the fix is small: fold
`StoreKey(dbPath)` of the resolved store into `DerivationVersion`, accepting that it purges the client cache
on every reindex.

**(c) The mtime signal is fragile under the fix for reader concurrency.** Item 1 noted `journal_mode=delete`
locks readers out during a patch. The obvious remedy is WAL. But in WAL mode a committed write lands in
`rig.db-wal` and the main file's size+mtime need not change until checkpoint — so `StoreKey` would **stop
moving on a committed change**, and every server cache would serve stale results. WAL and the store-identity
cache key are mutually exclusive as currently written. (`WarmStore.cs:20-24` states the dependency
explicitly: "a stale entry can never be served, with no file watcher in the correctness path.")

**Recommendation for this item:** do not patch the published file. An **in-memory overlay leaves the base
`rig.db` byte-identical**, so `StoreKey` does not move, `cache.db` is not purged, and the web client keeps its
(correct-for-the-base) entries. The overlay then needs its own identity — an overlay generation counter
folded into the cache key next to `StoreKey`, and into `derivationVersion` — which is *additive* and lets you
choose the invalidation granularity instead of inheriting the file's.

**Verdict:** server-side invalidation on a patch is **correct but catastrophically coarse and destructive**
(it purges, not misses, a 15 MB artifact cache holding minutes-expensive diffs), the **web client does not
invalidate on store content at all** (a currently-latent bug a live store makes permanent), and the
mtime-based key is incompatible with the WAL mode a concurrent in-place writer would need. This is the
strongest single argument for an overlay over an in-place patch.

---

## 6. `rig impact` implications

`impact` requires **both** `--base` and `--head` as store refs (`ImpactCommand.cs:122-127`), resolves both
through `StoreLayout.ResolveReadStoreDir` (`ImpactEngine.cs:41-43`), takes `StoreKey` of each
(`:44-45`), opens the artifact cache **in the HEAD store dir** (`:46`) and keys the artifact on both store
identities + rules + mode (`ImpactCacheKey`, `QueryCacheKeys.cs:149-158`). The comment at
`QueryCacheKeys.cs:140-148` states the premise out loud: "the artifact is a pure function of the TWO
**immutable** per-commit stores".

A live/mutable head store falsifies that premise in four ways:

1. **The artifact is no longer a function of an addressable input.** `--head <sha>-dirty` names a directory
   whose contents change under the reader. Two `rig impact` runs with identical arguments legitimately
   produce different answers, and neither is reproducible or citable. For a tool whose value proposition is
   "proven blast radius of this branch", that is a product-level regression, not a caching detail.
2. **Every patch invalidates the most expensive artifact and purges the cache it lives in.** `headStoreKey`
   moves → key misses; and because the cache lives in the head dir, `QueryCache.Open`'s purge
   (`QueryCache.cs:49`) also deletes every *other* store-pair's diff cached there. A cold diff loads and
   derives **both** stores.
3. **`WarmStore` cannot help.** Its LRU default capacity is 4 (`WarmStore.cs:42`) and `impact` is
   deliberately not routed through it because it wants two graphs at once (`WarmStore.cs:29-32`). A live head
   store evicts its own entry on every patch anyway.
4. **Store-ref resolution gets ambiguous in practice.** `ResolveStoreDirByRef` (`StoreLayout.cs:134-172`)
   matches on sha prefix after stripping `-dirty`, keeping the **last** match in enumeration order unless the
   id matches exactly. The MedDBase `.rig` already holds pairs like `32f4dac9dc7b` and `32f4dac9dc7b-dirty`,
   so `--base 32f4dac` is already nondeterministic between them. A live store makes `<sha>-dirty` the
   permanent default (`LatestStoreDir`, `StoreLayout.cs:59-80`, plus `WriteLatestPointer` at
   `IndexCommands.cs:435`), so this collision becomes the normal case rather than an edge case.

The layout consequence: `StoreLayout`'s contract is "store-id = commit, therefore immutable, therefore
addressable" (`StoreLayout.cs:6-11, 84-96`). A live index needs a **third** category alongside
`<sha>` and `ts-<stamp>`: a mutable working store that is explicitly NOT commit-addressable — e.g. a
single `.rig/live/` that `impact` **refuses** as a `--base` and accepts as `--head` only with an explicit
"unpinned" acknowledgement, and that is snapshot-promoted to a real `<sha>` store on commit. Keeping
`<sha>-dirty` as the live store instead conflates "an index of a dirty tree at one moment" (today's meaning,
already reproducible-ish) with "a store that mutates while you read it".

**Verdict:** `impact`'s base/head model rests on both stores being immutable and content-addressable; a live
head store breaks reproducibility of the flagship artifact, recomputes the most expensive diff (minutes,
both stores) on every patch while purging its neighbours' cached diffs, and turns an existing `-dirty`
store-ref ambiguity into the default path. A live store must be a **new, explicitly non-addressable layout
category**, excluded from `--base` and snapshot-promoted on commit — not another `<sha>-dirty` directory.

---

## Recommendation

**(B) In-memory overlay on an immutable base store, re-snapshot occasionally.**

Concretely: the base `.rig/<sha>/rig.db` stays byte-identical and read-only. The resident process holds, per
changed project, the re-extracted `AnalysisResult` plus a tombstone set (the owned `SymbolId`s / edge keys
being replaced), and every read is served from `base ∖ tombstones ∪ overlay`. `dispatch_edges` and the
`GraphIndex` are rebuilt **in memory, whole-program** on each patch (10,496 rows; the cost is the
`FactGraphData` build, which a resident process already caches — `WarmStore.GraphAsync`,
`WarmStore.cs:55-66`). Cache keys gain an overlay generation counter beside `StoreKey`, in both
`QueryCacheKeys` and `DerivationVersion`. When the overlay exceeds a threshold, or on commit, publish a
normal full `rig index` snapshot through the existing atomic-rename path and drop the overlay.

**Three strongest reasons:**

1. **It is the only option that does not break correctness on day one.** An in-place patch (A) purges a
   15 MB `cache.db` per save (`QueryCache.cs:49`) and — because `derivationVersion` carries no store
   identity (`RigApiEndpoints.cs:336-343`) while the client keys on the store-id *string*
   (`api.js:111-115`, `StoreLayout.cs:87-96`) — leaves every browser serving stale trees forever. An overlay
   leaves `StoreKey` untouched, so both layers keep serving *correct* base-store answers and the overlay's
   own invalidation is a new, deliberately-scoped axis rather than an inherited file mtime.
2. **`dispatch_edges` is whole-program and cannot be partially re-baked soundly** (`AllDispatchEdges` →
   `BuildIndex` over the entire graph, `FactPathFinder.cs:1082-1090`, `GraphIndex.cs:283`; 43% of rows already
   cross assemblies; the simple-name `!:` bucket has no reference-graph blast radius). It is the documented
   sound superset that bounds the SQL walk (`SqlReachability.cs:15,75`), so a partial version on disk means
   silent under-reporting in `reaches`/`tree`. Rebuilding it in RAM per patch is both sound and cheap; writing
   a partial one to disk is neither.
3. **The disk work a patch would force is 20-30 s, i.e. the whole latency budget.** Measured floors on real
   data: FTS rebuild **12.5 s / 420 MB** (`GraphMaterializer.cs:198-251` is drop-and-rebuild, no incremental
   path), 8 fact indexes **~7-15 s** (`Writes.cs:611`, dropped unconditionally at `:313`), plus a reader
   lockout for the duration under `journal_mode=delete` (measured) — and the WAL fix for that lockout would
   silently break the mtime cache key. An overlay pays none of it; it pays RAM.

**Single biggest risk of (B): overlay/base divergence — a correctness bug with no on-disk artifact to
inspect.** Every read path must be taught the overlay, and the ones that bypass EF are the dangerous ones:
`SqlReachability`'s recursive CTEs over `call_edges`/`nodes` (`SqlReachability.cs`), the FTS `MATCH` paths
(`Reads.cs:105-130`), `EntryPointSiteStore.LoadAsync` (a `(FilePath,Line)` table with its own rules-hash
stamp), the `Reads.cs:1443` assembly-usage joins. Any one of them left un-overlaid serves base-store facts
inside an otherwise-patched answer — a *mixed* result that is wrong in a way neither store is, and that
cannot be reproduced from any file on disk. Mitigation: gate the overlay behind an equivalence oracle —
after each patch, in the background, run the full `rig index` snapshot and assert the overlay's derived
output (EP set, per-EP effect sets, `dispatch_edges`) is identical; ship the overlay only for read paths that
pass, and degrade the rest to "overlay present → recompute from the snapshot". The two-store diff machinery
in `ImpactEngine` already computes exactly that comparison and can be reused as the oracle.
