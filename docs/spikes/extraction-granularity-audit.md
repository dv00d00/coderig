# Extraction granularity audit — what is whole-solution-coupled

**Workstream 4 of [live-background-index](../backlog/done/live-background-index.md).** Read-only audit,
2026-08-20. Question: if we re-extract only the project(s) whose files changed, over a resident
`AdhocWorkspace`, what breaks or silently corrupts facts?

**Scope note.** Line numbers are against the **working tree**, not `HEAD` —
`SolutionSourceLoader.cs` and `SolutionAnalyzer.cs` are modified by the concurrently-running GATE spike
(workstream 1), which added `AnalyzeRetainingWorkspaceAsync` / `ReadSolutionSourcesAsync`. That seam
re-runs the **whole-solution** compile+read+extract over a retained workspace; everything below is about
narrowing it to one project.

**The headline.** `FactExtractor` itself is clean — the blockers are all in the pipeline *around* it. Two
are showstoppers as-written; both are in the write/bake tail, not the extractor.

---

## 1. Fact append is run-scoped, reads span all runs, and the hot effect input does not dedup

**Severity: corrupts-facts. SHOWSTOPPER.**

`Writes.SaveAsync` always mints a fresh run (`Writes.cs:47` — `var runId = Guid.NewGuid().ToString("n")`)
and every fact row is keyed `(RunId, *FactIndex)` (`RigDbContext.cs:85,98,105,112,119,126`). There is no
"replace the rows for project P" path anywhere in `Rig.Storage`.

The read side is explicitly run-blind: `Reads.cs:103` — *"Stage-3 fact queries: cross-project (all runs),
DocID-keyed. **No latest-run concept.**"* So facts from two runs are UNIONed, and:

- **`LoadInvocationRefsAsync` — the primary effect-derivation input — has no dedup at all.**
  `Reads.cs:1049-1081` ends `return rows;`. Same for `LoadAllocationFactsAsync` (`Reads.cs:1088-1113`).
  `FactEffectDeriver` adds none either (no `Distinct`/`GroupBy` in the file). So a project whose facts sit
  in two runs yields **every effect in it twice** — `derive` totals, hazard candidate counts, N+1
  cardinality, all silently doubled. This is the quietest failure in the whole audit: the output still
  looks structurally correct.
- Some readers *do* dedup and would mask the problem inconsistently: the call graph dedups by the full
  `CallEdge` tuple incl. `FilePath`+`Line` (`Reads.cs:355`), DI by
  `(ServiceType, ImplementationType, FilePath, Line)` (`Reads.cs:40-41`), static-field access by
  `(FilePath, Line, Target)` (`Reads.cs:1275-1281`, `1338`). Note what that dedup key implies: a stale row whose
  **line moved** survives alongside the new one → a **ghost call edge from code that no longer exists**.
  Reachability becomes the union of pre- and post-edit code, which is precisely the failure this program
  exists to remove.
- `symbol_facts` duplicates also **fan out joins**: `LoadStaticFieldAccessRefsAsync` joins
  `reference_facts → symbol_facts` on `SymbolId` (`Reads.cs:1310-1336`); a duplicated symbol row
  multiplies rows before the dedup pass.

And the delete you would need is expensive by construction. There is **no index leading with `RunId`** on
any fact table — deliberately removed (`RigDbContext.cs:89-95`: *"nothing filters by RunId… Re-add a
leading-RunId index only if a run-scoped query is introduced"*). Verified on the live MedDBase store
(`.rig/ae2cdb64e1cb/rig.db`): the only fact-table indexes are
`IX_reference_facts_{TargetSymbolId,EnclosingSymbolId}`, `IX_symbol_facts_{SymbolId,Name}`, etc. So
`DELETE FROM reference_facts WHERE RunId = ?` is a **full scan of 2,437,000 rows**, and there is no
`FilePath` index either, so deleting by changed-file set scans too.

Also note `reference_facts` carries no owning-project column at all — only `TargetAssembly` (the *callee's*
assembly). Project ownership of a reference row is only recoverable via `FilePath`, or by joining
`EnclosingSymbolId → symbol_facts.DefiningAssembly`. `FilePath` works in practice: on MedDBase all 12,093
`source_files` rows are distinct, and distinct case-insensitively too — **no file belongs to two
projects**. That is not guaranteed by construction (`ReadSolutionSourcesAsync` never dedups across
projects — `SolutionSourceLoader.cs:225-226`), so a linked/shared `<Compile Include="..\Shared\X.cs">`
would make a per-project delete-by-path destroy a sibling project's facts.

**Minimal fix:** make the patch unit a **run** (one run per project, `ProjectIdentity`/`SourceProjectPath`
already exist on `RunEntity` and are written at `Writes.cs:71-72`), add a `DeleteRunAsync` plus a
`(RunId)`-leading index on the five fact tables, and delete-then-insert inside one transaction. Do not
ship the union-without-delete variant at all.

---

## 2. The index tail re-bakes the derived views from the in-memory whole-solution result

**Severity: corrupts-facts. SHOWSTOPPER — and trivially easy to hit by reusing the existing tail.**

`IndexCommands.MaterializeGraphAsync` builds the graph from `result` — the in-memory fact set of *this*
analysis — not from the store: `IndexCommands.cs:504`
(`FactGraphProjection.FromAnalysis(result, …)`), then feeds `result.Symbols` / `result.References`
straight into the FTS build (`IndexCommands.cs:512-513`).

Every derived table is a **whole-store DROP-and-rebuild**:

| table | statement |
|---|---|
| `call_edges` | `DROP TABLE IF EXISTS call_edges;` — `GraphMaterializer.cs:363` |
| `dispatch_edges` / `call_edges` rows | `DELETE FROM …;` — `GraphMaterializer.cs:115-116` |
| `nodes` | `DROP TABLE IF EXISTS nodes;` — `GraphMaterializer.cs:336-337` |
| `symbol_fts` | `DROP TABLE IF EXISTS symbol_fts;` — `GraphMaterializer.cs:198` |
| `ref_target_fts` | `DROP TABLE IF EXISTS ref_target_fts;` — `GraphMaterializer.cs:232` |
| `entry_point_sites` | `DROP TABLE IF EXISTS entry_point_sites;` — `EntryPointSiteStore.cs:34-36` |

So calling the existing index tail after a per-project extract drops the whole store's derived views and
rewrites them **from one project's facts**. On MedDBase that is 631,376 call edges → a few thousand,
300,911 nodes → a few hundred, 421,953 `symbol_fts` rows → a few hundred. No error, no warning.

Worse, a *partial* re-bake is unsound in principle, not just unimplemented: `dispatch_edges` comes from
`FactPathFinder.AllDispatchEdges` — whole-program CHA over the full `implements`/`base` closure. Adding one
implementer in project P changes the dispatch fan-out of an interface method declared in project A and
consumed in project B. There is no project-local subset of `dispatch_edges` to patch.

**Minimal fix:** on the incremental path, never call the in-memory `BuildFromGraphAsync(result…)` overload
— use the store-reading `GraphMaterializer.BuildAsync` (which is already idempotent and full-store) after
the fact patch commits, and measure it; on MedDBase the graph phase is the candidate long pole of an
otherwise-seconds patch. If that proves too slow, the honest alternative is the in-memory overlay
(no persisted graph at all for tier 1), not a partial bake.

---

## 3. A project's facts are not a function of its own source — changed-files-only is unsound

**Severity: corrupts-facts (stale bindings survive) / degrades-recall.**

This is already stated as design intent in `docs/incremental-indexing.md` §"Why local content-hashing is
necessary but NOT sufficient", and the code confirms every channel:

- **Target DocID + `TargetInSource`** are resolved through the current compilation:
  `FactExtractor.cs:1202-1210` — `resolved = (method.ReducedFrom ?? method).OriginalDefinition`,
  `inSource = resolved.Locations.Any(loc => loc.IsInSource)`, `assembly = resolved.ContainingAssembly?.Name`.
  Add an overload in A and B's byte-identical call site binds elsewhere.
- **Dispatch facts** are mined at the *implementing* type but resolve against interfaces that may live in
  another project: `FactExtractor.cs:987-1006` (`type.AllInterfaces`,
  `FindImplementationForInterfaceMember`). Adding a member to an interface in A requires re-extracting
  every implementer in B/C/D to emit the new `dispatch_facts` row.
- **Override edges**: `FactExtractor.cs:122,132` (`accessor.OverriddenMethod`, `IMethodSymbol.OverriddenMethod`).
- **Type relations**: `FactExtractor.cs:1157` (`type.BaseType`).
- **Generic monomorphization bindings** (`FactExtractor.cs:1224-1231`) and receiver/argument type displays
  are all computed off cross-project symbols.
- **`AllocationSizeEstimator`** shallow sizes read the layout of types that may be defined in other
  projects.

**Minimal fix:** stale-set = own-source change **∪ reverse-dependency closure of every project whose
public surface changed**, using the already-cheap XML `DependencyGraph` for the reverse edges. Ship the
coarse form first (`any dependency changed → dependent is stale`); it over-invalidates but is never wrong.
A method-body-only edit still invalidates just its own project, which is the common agent case.

---

## 4. `TargetInSource` requires every first-party project live as source in the SAME workspace

**Severity: corrupts-facts if the workspace is narrowed. Already empirically proven.**

Cross-project references only resolve as *source* because the loader converts every in-set project
reference — **transitively** — into a live Roslyn `ProjectReference` and drops the corresponding metadata
DLL: `SolutionSourceLoader.cs:1002` (`TransitiveInSetClosure`) with the rationale at
`SolutionSourceLoader.cs:989-1001` (a duplicate assembly identity *"silently dropping the call edge"*).
Narrow the source set and `inSource` flips false, and `Reads.LoadFactGraphAsync` drops the edge
(`Reads.cs:328-334`, `FactGraphProjection.cs:34`).

The GATE spike already measured this on DeepChain: all 7 projects as source → 42 references; `Business`
alone with deps as metadata → **2 references, 43 errors**
([live-background-index](../backlog/done/live-background-index.md) §"What the spike killed").

Good news: this is a constraint the resident-workspace plan already satisfies by construction — the whole
solution stays live, only the *read+extract* pass narrows. **This is a hard invariant to write down and
test, not a blocker to fix.**

**Minimal fix:** none needed; add an assertion/regression test that a per-project re-extract of P produces
byte-identical facts for P to a cold full index (the GATE's equivalence protocol, narrowed to P).

---

## 5. Source generators: additive wiring, non-unloadable analyzer DLLs, no driver reuse, known flake

**Severity: corrupts-facts (duplicate generated trees) + degrades-recall (nondeterministic file set).**

Four distinct problems, all in the generator path.

1. **`AddAnalyzerReference` is additive and `WireGeneratorAnalyzersAsync` is not idempotent across calls.**
   `SolutionSourceLoader.cs:1405`. Re-running it on a retained solution wires the same generator a second
   time → `RunSourceGeneratorsAsync` (`SolutionSourceLoader.cs:1458` —
   `project.AnalyzerReferences.SelectMany(ar => ar.GetGenerators(…))`) runs it twice → duplicate generated
   trees, duplicate hint paths, duplicate symbol facts for every generated type. On MedDBase that is
   **1,394 generated files** (`source_files` where `Basis='generated'`) at risk of doubling.
2. **The emitted analyzer DLL is loaded un-unloadably.** `HostRedirectingAnalyzerLoader.LoadFromPath` calls
   `Assembly.LoadFrom` into `AssemblyLoadContext.Default` (`SolutionSourceLoader.cs:1619-1623`), and each
   emit writes a fresh GUID-named temp DLL (`SolutionSourceLoader.cs:1433`) *"left for the process
   lifetime"* (`SolutionSourceLoader.cs:1421-1422`). In a resident process, editing a generator project N
   times leaks N assemblies + N temp DLLs and cannot replace the loaded generator.
3. **No generator incrementality.** A fresh `CSharpGeneratorDriver` is created and discarded per project
   per read pass (`SolutionSourceLoader.cs:1464`), so the `GeneratorDriver` state table — the entire point
   of incremental generators — is thrown away. Also, the generated trees' semantic models are bound
   against `generatedCompilation` (`SolutionSourceLoader.cs:1482`), a *different* `Compilation` instance
   from the one hand-written trees use, so retaining either pins both.
4. **The generator output is already nondeterministic.** `docs/backlog/needs-review/flaky-clientpage-proxy-extraction.md`
   — the ClientPage proxy generator intermittently fails to contribute, and
   `RunSourceGeneratorsAsync` swallows every exception (`SolutionSourceLoader.cs:1499-1503`). That makes
   "did project P change?" unanswerable from content alone: a flaked run silently drops P's generated file
   set, and under a patch model that flake becomes **persistent** rather than one bad run.

**Minimal fix:** make wiring idempotent (check `project.AnalyzerReferences` for the path before adding, or
rebuild the reference list rather than appending); load each generator into a dedicated
`AssemblyLoadContext(isCollectible: true)` and delete its temp DLL on unload; retain the `GeneratorDriver`
per project alongside the workspace; and **refuse to patch a project whose generator run reported an
exception** rather than persisting a zero-generated-file result.

---

## 6. `*FactIndex` is a global per-run dense sequence over a global path sort, project-interleaved

**Severity: corrupts-facts (PK collision / order-dependent patching). Confirms the suspicion.**

The suspicion in the brief is **CONFIRMED — it is a global sequence, not a per-project ordinal.**

Mechanism, end to end:
- `ReadSolutionSourcesAsync` collects per-project results into a `ConcurrentBag`
  (`SolutionSourceLoader.cs:174`) then flattens with **one global sort by full path**:
  `projectResults.SelectMany(r => r.Sources).OrderBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase)`
  (`SolutionSourceLoader.cs:226`).
- `SolutionAnalyzer` extracts into pre-allocated slots by input position — the comment names the coupling
  explicitly: *"keeps the output deterministic by input position — **which the FactIndex surrogate keys
  depend on**"* (`SolutionAnalyzer.cs:171-178`) — then concatenates the per-file lists in that order
  (`SolutionAnalyzer.cs:232-240`).
- `Writes.InsertRows` binds the index as the **list position**: `p[1].Value = i` (`Writes.cs:345`,
  and identically for allocations/references/relations/dispatch at `Writes.cs:394`, `460`, `505`, `526`).
  Same for `SourceFileEntity.FileIndex` (`Writes.cs:655`) and `DiRegistrationEntity.RegistrationIndex`
  (`Writes.cs:678`).

Verified on the live MedDBase store: `reference_facts` in `ReferenceFactIndex` order is exactly the
`OrdinalIgnoreCase` full-path sort (112 apparent inversions out of 2,437,000 are all
`StringComparer.OrdinalIgnoreCase` vs Python `lower()` differences on `_` vs letters — e.g.
`attributepopulationuk_immutable.cs` before `attributepopulation_immutable.cs`).

And projects are **not contiguous** in that order: 221 distinct `DefiningAssembly` values across
**2,047 contiguous blocks** in `SymbolFactIndex` order. So there is no per-project index range to
delete or overwrite; a project's rows are scattered ~9 blocks deep on average. Adding or removing one
file shifts every subsequent index in the run.

Practical consequence: the index column is a **dense surrogate for one run's list, nothing more**. In-place
patching inside an existing run is off the table (PK `(RunId, Index)` collides, and any renumbering
rewrites millions of rows). Per-project = per-run is the only sane read.

**Minimal fix:** treat `*FactIndex` as run-local and never patch within a run — i.e. exactly blocker 1's
fix. If you ever want in-run patching, the index has to become `(project ordinal, in-project ordinal)` or
a plain autoincrement rowid, which is a schema-shape change and a `SchemaVersion.Index` bump.

---

## 7. Run-global DI side-inputs are re-mined per analysis and never expire

**Severity: degrades-recall + perf-only.**

Code-detected DI is per-file and clean (`DiRegistrationExtractor.FindDiRegistrations` takes one
`SourceModel`; `BuildMethodNameSet` is a pure function of the rules). **The cross-project resolution the
brief suspected is not there** — `ServiceType`/`ImplementationType` come from the invocation's own type
arguments (`DiRegistrationExtractor.cs:45-67`), which are bound in the current project's compilation. So
that half is REFUTED.

But two DI inputs are **run-global, solution-scoped, and re-computed on every `AnalyzeAsync`**:
`XmlDiMiner.Mine(rules)` walks every configured XML directory recursively
(`SolutionAnalyzer.cs:246` → `XmlDiMiner.cs:27-38`), and `rules.StaticDiMappings` is appended wholesale
(`SolutionAnalyzer.cs:247`). Under per-project runs these get re-emitted per patch.

The reader tolerates the duplication — `LoadDiRegistrationsAsync` dedups by
`(ServiceType, ImplementationType, FilePath, Line)` (`Reads.cs:40-41`), and both side-inputs have stable
`FilePath`/`Line` (`Line: 0`, and `""` for static mappings). So no double-count. Two residual problems:
a per-project patch that *omits* them and then deletes the old run's rows **loses the XML/static DI
entirely**; and stale code-detected registrations for deleted lines never expire (the reader has no
notion of superseding).

**Minimal fix:** move XML + static DI out of the per-project extraction into a separate, idempotent
"solution-scope facts" run that is rewritten wholesale on its own trigger (XML dir mtime / rules
fingerprint), and never touched by a project patch.

---

## 8. The `GetDiagnostics` warm-up — mostly redundant, and a cheap public substitute exists

**Severity: perf-only.**

The call: `SolutionSourceLoader.cs:243` — `compilation.GetDiagnostics(ct).Where(d => d.Severity == Error)`.
The deliberate-ness is documented at `SolutionSourceLoader.cs:1314-1317`: *"Bind through the passed-in
compilation … so the model is built over the SAME instance ProcessProject warmed via GetDiagnostics —
same-tree binding hits that warmed cache instead of triggering a recompile."*

Verified against the pinned Roslyn 5.3.0 public surface
(`~/.nuget/packages/microsoft.codeanalysis.common/5.3.0/lib/netstandard2.0/Microsoft.CodeAnalysis.xml`),
`Compilation` exposes **four** diagnostics entry points, all public:

| API | doc summary |
|---|---|
| `GetParseDiagnostics(ct)` | *"produced during the parsing stage"* |
| `GetDeclarationDiagnostics(ct)` | *"produced during symbol declaration"* |
| `GetMethodBodyDiagnostics(ct)` | *"produced during the analysis of method bodies and field initializers"* |
| `GetDiagnostics(ct)` | *"all … including syntax, declaration, and binding"* |

So `GetDiagnostics` = parse + declaration + **every method body bound**. Three observations:

1. **The errors rig actually reports and acts on are declaration-phase.** The degraded-extraction
   signatures in this repo's own record are `'System.Object' is not defined`, missing references, CS0246
   — all symbol-declaration/metadata-import failures (see the DeepChain metadata-partition result in
   [live-background-index](../backlog/done/live-background-index.md)). The method-body third produces
   diagnostics that are counted and printed but never change behaviour.
2. **The method-body third is redundant with extraction.** *(INFERRED — Roslyn internals, not verified in
   this repo.)* `Compilation.GetDiagnostics` binds bodies through a throwaway compilation state; the bound
   bodies are not retained for a later `GetSemanticModel(tree)`, which builds a fresh
   `SyntaxTreeSemanticModel` and re-binds lazily. What *is* retained and genuinely shared is the
   declaration-stage work: the source assembly's member/declaration tables and the metadata imports of
   every reference — exactly what `GetDeclarationDiagnostics` forces. If that is right, most of the
   reported ~289s CPU/index buys nothing that extraction doesn't pay for again.
   **Falsifiable in one run:** swap to `GetDeclarationDiagnostics`, re-index MedDBase with `--time`, and
   compare `Σ diagnostics` + `Σ read` against the baseline — if the warm-up were load-bearing, `Σ read`
   would rise by roughly what `Σ diagnostics` falls. Also diff the fact set for identity.
3. **On an incremental re-extract of ONE project it is cheaper but not free.** The changed project's own
   declaration tables and bodies must be rebuilt; its unchanged dependencies' compilations are reused by
   Roslyn. So cost falls to roughly 1/N of the full-index figure (N ≈ 221 assemblies on MedDBase) — still
   the dominant per-patch item if the redundancy in (2) holds. **Still needed?** The *declaration* half is
   yes (it is the disclosure that extraction is degraded, which rig's identity requires). The body half is
   the part to drop.

Hygiene, same block: `Console.WriteLine($"{project.Name}: {diagnostic}")` at `SolutionSourceLoader.cs:246`
writes unconditionally from inside the parallel `ProcessProject` loop, bypassing the `progress` sink that
every other line in this file uses.

**Minimal fix:** replace `GetDiagnostics(ct)` with `GetDeclarationDiagnostics(ct)` on both paths, measure
with `--time`, and route the per-diagnostic write through `progress` instead of `Console`.

---

## 9. Cross-project state in the extractor — `FactExtractor` is clean; the shared cache is the leak

**Severity: perf-only (unbounded retention in a resident process).**

`FactExtractor.Extract(SourceModel, SymbolStringCache)` is a pure function of **one file** plus the memo:
every accumulator is a local (`FactExtractor.cs:30-50` — `symbols`, `references`, `enclosingCache`,
`cfgGuardCache`, `lambdaIds`, `lambdaOrdinalByMember`, `dispatchSeen`, …), and there is **no static mutable
state** in `Extraction/` (grep for `static readonly` / `ConcurrentDictionary` / `[ThreadStatic]` returns
only `SymbolStringCache`'s instance fields and `RoslynSymbolHelpers.MethodKeyFormat`, an immutable
`SymbolDisplayFormat`). `EnclosingSymbolId` only walks **syntactic ancestors within the file**
(`FactExtractor.cs:2578-2606`, `ComputeEnclosingId` 2608-2644) — it never reaches outside the current tree.
Lambda ordinals are per-file, per-member (`FactExtractor.cs:63`). Assembly attribution is per-compilation
(`FactExtractor.cs:64`, `1120`). **So the "cross-project extractor state" suspicion is largely REFUTED.**

The one shared object is `SymbolStringCache`, created per analysis at `SolutionAnalyzer.cs:166` and shared
across the whole parallel extraction. It is *correctness*-safe by construction — the header documents why
each memo is sound under `SymbolEqualityComparer.Default` (`SymbolStringCache.cs:11-22`, and the
`OriginalDefinition` keying at `SymbolStringCache.cs:56-64`). The problem is **lifetime**: the keys are
`ISymbol` / `INamespaceSymbol` / `ITypeSymbol` strong references, so a cache retained across re-extractions
pins the `Compilation`s those symbols came from — the exact retention `SolutionAnalyzer.cs:190-198` goes out
of its way to break in the one-shot flow (*"the compilations stay rooted via TWO independent paths"*).
Retain the cache in a resident daemon and you retain every superseded compilation with it.

**Minimal fix:** rebuild the `SymbolStringCache` per patch (cheap — it is a memo, not state), or key it on
the DocID string rather than the symbol. Do not carry the instance across an edit.

---

## 10. `assemblies` / `solution_membership` — write-only today, so the obligation is small

**Severity: perf-only (cosmetic drift), pending anything starting to read them.**

`WriteAssemblyRegistryAsync` (`Writes.cs:109-235`) is already **per-assembly upsert**: it folds
`result.Symbols`/`result.References` into a per-assembly order-independent digest
(`AssemblyAccumulator`, `Writes.cs:240-270`) and upserts one row per assembly present in the result
(`Writes.cs:191-233`). A per-project patch therefore touches exactly that project's row — the shape is
already right. Reference attribution goes through `symbolAssembly` built from **this run's** symbols
(`Writes.cs:129-133`) and skips references whose enclosing symbol isn't in it (`Writes.cs:165-175`),
which for a per-project result is correct by construction.

What a patch must still do:
- `solution_membership` (`Writes.cs:231`) only ever **adds**. A project removed from the solution leaves a
  permanent stale row.
- `AssemblyEntity.SymbolCount`/`ReferenceCount` become that assembly's counts, which is right — but a
  project deleted from the solution keeps its `assemblies` row forever.
- The divergent-content warning (`Writes.cs:200-206`) compares `SourceSolutionPath` — under per-project
  patching of the *same* solution it stays quiet, correctly.

Mitigating fact: **both tables are write-only.** Grep across `src/` finds no reader of
`solution_membership` at all, and the only `Assemblies` reads are the upsert's own dictionary load
(`Writes.cs:183`) plus a table-existence probe (`HasAssemblyRegistryAsync`, `Writes.cs:22-25`). The
`--solution` filter its comment advertises (`SolutionMembershipEntity.cs:3-6`) does not exist as a CLI
flag. `ProjectContentHash` (`src/Rig.Domain/ProjectContentHash.cs`) — the "skip an unchanged assembly"
primitive the `AssemblyEntity` header describes — is referenced **only by its own unit test**. So the
registry is provenance, not a mechanism, and drift here corrupts nothing today.

**Minimal fix:** on a per-project patch, upsert the one `assemblies` row and its membership row (already
the behaviour), and add a `DELETE` for the assemblies/membership of a project that has left the solution.
Wire the content-hash skip only if/when the registry gains a reader.

---

## 11. Minor / latent — worth knowing, not worth blocking on

- **Determinism rests on the path sort being a total order.** `ReadSolutionSourcesAsync` pulls from a
  `ConcurrentBag` (nondeterministic order) and relies on `OrderBy(…, OrdinalIgnoreCase)`
  (`SolutionSourceLoader.cs:225-226`); `OrderBy` is stable, so a **tie** would be resolved by the bag's
  arrival order → nondeterministic fact indices. Holds today (MedDBase: 12,093 files, 12,093 distinct
  case-insensitively; generated hint paths carry the generator's full name and the project name —
  `SolutionSourceLoader.cs:1487`), but it is an unenforced invariant.
- **`TargetAssembly` is not a reliable ownership key.** The delegate-slot invocation fact hardcodes
  `TargetAssembly: assemblyName` (the *current* project) and `TargetInSource: true`
  (`FactExtractor.cs:761-762`), as does the synthetic lambda methodGroup edge
  (`FactExtractor.cs:2718-2719`). Fine for the graph; wrong if a patch path ever keys ownership off
  `TargetAssembly`.
- **The bootstrap serialization** (`SolutionSourceLoader.cs:183-190` — first project processed alone
  *"let roslyn bootstrap without races"*) is a whole-solution warm-up that a single-project re-extract
  neither needs nor gets. *(INFERRED: harmless, since the resident workspace is already bootstrapped.)*
- **Store identity.** The store dir is the commit (`StoreLayout.NewStoreId`, `StoreLayout.cs:87-96`;
  `-dirty` suffix for an uncommitted tree). A patch of an edited working tree does not belong in the
  clean-commit store it was derived from. Cross-refs workstream 5 — including the `QueryCacheKeys`
  store-identity axis (rig.db size+mtime), which an in-place patch busts store-wide.
- **The `--merge` / `mine --identity` append path already exists** (`IndexCommands.cs:365-411`,
  `Writes.AssertAppendableAsync`) and is the closest precedent for a non-atomic in-place write. It is
  append-only by design and has the same no-delete gap; it skips the graph bake deliberately
  (`IndexCommands.cs:451-453`), which is the right instinct for a patch path too.

---

## Summary

| # | blocker | severity | minimal fix | size |
|---|---|---|---|---|
| 1 | Run-scoped append, run-blind reads, **no dedup on `LoadInvocationRefsAsync`** → doubled effects + ghost edges; no `RunId` index so run-delete is a 2.4M-row scan | **corrupts-facts** | one run per project + `DeleteRunAsync` + `(RunId)`-leading index; delete-then-insert in one txn | **M** |
| 2 | Index tail re-bakes `call_edges`/`dispatch_edges`/`nodes`/FTS/`entry_point_sites` **from the in-memory result** via DROP+rebuild; whole-program CHA makes partial bakes unsound | **corrupts-facts** | never call `BuildFromGraphAsync(result…)` on the patch path; full `BuildAsync` after commit, or an in-memory overlay | **S** (guard) / **L** (overlay) |
| 3 | Facts are not a function of a project's own source (target DocIDs, `TargetInSource`, dispatch, overrides, base edges, generic bindings) | **corrupts-facts** | stale-set = own change ∪ reverse-dependency closure; ship the coarse "any dep changed" form | **M** |
| 4 | `TargetInSource` needs every first-party project live as source in one workspace (DeepChain: 42 refs → 2) | **corrupts-facts** if violated | no code change — pin it as an invariant + a per-project equivalence test | **S** |
| 5 | Generators: additive `AddAnalyzerReference`, non-unloadable `Assembly.LoadFrom`, discarded `GeneratorDriver`, swallowed failures + the known flake | **corrupts-facts** | idempotent wiring; collectible ALC per generator; retain the driver; refuse to patch on a generator exception | **M** |
| 6 | `*FactIndex` is a global per-run dense sequence over a global path sort; projects interleave (221 assemblies / 2,047 blocks) | **corrupts-facts** on in-run patching | treat as run-local, never patch within a run (= fix 1); schema change if in-run patching is ever wanted | **S** |
| 7 | `XmlDiMiner` + `StaticDiMappings` are run-global and re-mined per analysis; stale DI rows never expire | degrades-recall | split into an idempotent solution-scope run with its own trigger | **S** |
| 8 | `GetDiagnostics` binds every method body; declaration-phase is the load-bearing part and `GetDeclarationDiagnostics` is public | perf-only | swap the call, measure with `--time`, route the print through `progress` | **S** |
| 9 | `SymbolStringCache` keys hold `ISymbol` → pins superseded `Compilation`s in a resident process | perf-only | rebuild per patch, or key on the DocID string | **S** |
| 10 | `assemblies`/`solution_membership` drift (add-only membership, orphan rows) — both tables are write-only today | perf-only | upsert the one row (already done) + delete on project removal | **S** |
| 11 | Path-sort tie-break determinism; `TargetAssembly` unreliable as ownership; bootstrap warm-up; commit-scoped store identity | perf-only / latent | document as invariants; store identity is workstream 5's | **S** |

**Showstoppers for per-project incremental extraction:** **1** and **2**. Both are in the write/bake tail,
both are silent, and both are fixable without touching `FactExtractor`. **3** is a correctness requirement
on the *scheduler*, not a bug — get it wrong and you serve stale bindings with a straight face. **4** the
resident-workspace design already satisfies; make it a test so it stays satisfied. Everything from **6**
down is tractable.
