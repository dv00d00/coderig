# Failed-compilation disclosure — implementation spec

**Status:** SPEC, ready to dispatch. No code written; nothing built; nothing run.
**Decision:** the product owner decided on **2026-08-20** that facts derived from a tree that does not
compile must be **prefixed with a disclosure** rather than presented as sound, and that the behaviour must be
**stress-tested**. That decision is not re-litigated here — this document only settles *what* the prefix is,
*where* it attaches, *how far* it spreads, and *how* it is abused in tests.
**Branch:** `live-background-index`. **Family:** disclosure / extraction provenance.
**Raised by:** [equivalence-test-matrix.md](equivalence-test-matrix.md) § "Non-compiling tree — flagged for the
human, not decided here" (row 7 of its matrix).
**Depends on / feeds:** [live-background-index](../backlog/progress/live-background-index.md) Slice 5
(staleness disclosure). This spec is the **cold-store half** that Slice 5's baseline-relative mode needs.

---

## 0. Why this matters more than its size suggests

Agents ask rig for help *right after editing*, which is exactly when the tree may not compile. Today that case
is **silent at query time**: the only signal is a per-index stderr warning
(`src/Rig.Analysis/Inventory/SolutionSourceLoader.cs:212`) that scrolls past during `rig index` and is never
persisted, so a `rig reaches` run an hour later cannot know the store was built from a broken tree. A dropped
call edge then reads as "nothing calls this" — a confident, plausible, wrong answer, which is the exact failure
mode the whole disclosure convention exists to prevent
(`CLAUDE.md` § "Two-stage design"; `progress/live-background-index.md` § "Constraints any design must respect").

---

## 1. What already exists — read this before proposing anything

### 1.1 The `!:` candidate-DocID path (extraction)

`src/Rig.Analysis/Extraction/FactExtractor.cs:138-149`, in the per-simple-name reference pass:

```csharp
// Fall back to a candidate symbol when Roslyn can't fully bind. Under net48 cross-assembly
// partial binding (`!:` DocIDs) a real, in-source call often resolves only to a CandidateSymbol
// (CandidateReason.OverloadResolutionFailure et al.) — dropping it silently loses effect-bearing edges …
var symbolInfo = model.GetSymbolInfo(name);
var target = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
```

The `!:` prefix itself is **Roslyn's**, not rig's: `GetDocumentationCommentId()` returns `!:Name` for an error
symbol, and rig stores that string verbatim (`FactExtractor.cs:1203`, via
`Extraction/SymbolStringCache.cs:30`). So `!:` is *inherited*, not chosen — a point that matters in §3.

### 1.2 What already happens to a `!:` fact downstream — and what it cost

`!:` ids **do not join**. Every join they participate in had to be rebuilt by hand:

| machinery | file:line | what it is |
|---|---|---|
| `ImplsByErrorInterfaceName` | `src/Rig.Domain/Functions/FactPathFinder.GraphIndex.cs:201-206` (declared), `:351-355` (built) | a **parallel simple-name index**, populated only from `ImplementsEdges` whose `InterfaceType.StartsWith("!:")`, because the normal `ImplsByInterface` DocID lookup can never match them |
| the "always-on" recovery arm | `src/Rig.Domain/Functions/FactPathFinder.Dispatch.cs:551-560` | dispatch step 2, gated on method name, consulting only error-type edges |
| "no mined edge by definition" | `FactPathFinder.Dispatch.cs:398-400` | a `!:IFoo` edge can carry no Roslyn-mined dispatch fact, so it is *permanently* heuristic |
| bespoke simple-name parser | `src/Rig.Domain/Functions/FactPathFinder.cs:1288` — `// "T:Ns.IFoo`1" / "!:IFoo" -> "IFoo".` | strips the prefix to get something matchable |

**That is the evidence base for §2 Option B.** A prefixed DocID is not a label — it is a *different key*, and
rig has already paid, twice, to un-fragment one.

### 1.3 Measured prevalence of `!:` on the real store (calibration, this session)

Read-only `sqlite3` over `c:/git/meddbase-analysis/.rig/ae2cdb64e1cb/rig.db` (`.rig/LATEST`, clean commit,
3.9 GB, indexed 2026-08-18):

| measure | value |
|---|---|
| `source_files` rows | **12,093** (11,938 `Status='indexed'`), 226 distinct `ProjectName` |
| `reference_facts` rows | **2,437,000** |
| refs with `TargetSymbolId LIKE '!:%'` | **13,642** (0.56 %) |
| distinct `FilePath` carrying ≥1 `!:` ref | **879** (**7.3 %** of files) |
| `RefKind` breakdown of those 13,642 | `typeUse` **13,640**, `nameof` **2**, `invocation` **0** |
| `symbol_facts` with `!:` id | **0** |
| `type_relation_facts` with `!:` related id | **0** |

Three consequences, all load-bearing:

1. **`!:` is routine, not exceptional** — 7.3 % of files in a store built from a *clean commit* carry one.
2. **`!:` today means "an unresolved TYPE NAME", not "a broken call"** — 13,640 of 13,642 are `typeUse`, the
   CS0246/CS0234 family. There are **zero** `!:` invocations in this store, so the `CandidateSymbol` arm of
   `FactExtractor.cs:148` is contributing nothing measurable here.
3. **The `!:` dispatch-recovery machinery is idle on this store** — `type_relation_facts` has zero `!:`
   entries, so `ImplsByErrorInterfaceName` (§1.2) is built empty. The machinery exists for stores where
   interface edges fail to bind; this one has none.

### 1.4 The existing per-index warning (coarse, unpersisted)

`SolutionSourceLoader.cs:203-222`. Errors are collected into a `ConcurrentBag<string>` at `:174`, appended as
`$"{project.Name}: {diagnostic}"` at `:246`, then reported as one line:

```
Warning: {errorCount} compilation error(s) — analysis will be partial for affected files   // :212
```

…top-10 listed, `"... and N more (set --verbose to see all)"`. Three defects worth naming because the fix
lands in the same code:

- The diagnostic's **file location is thrown away** — the bag holds a project-qualified string.
- Nothing is **persisted**, so the query side cannot see it.
- `:247` does a bare `Console.WriteLine($"{project.Name}: {diagnostic}")` for **every** error, uncapped and
  straight to stdout — a firehose on a 226-project index that also corrupts any stdout parsing of `rig index`.

### 1.5 The two location-less failure classes (already reported, still unpersisted)

Both were hardened on this branch — they now warn on the progress channel — but neither is recorded:

| class | file:line | consequence |
|---|---|---|
| project compilation unavailable | `SolutionSourceLoader.cs:238-241` (`compilationErrors.Add($"{project.Name}: compilation unavailable"); return;`) | the project contributes **zero** facts and **zero `source_files` rows**; no diagnostic has a file location |
| generator emit / run failure | `:1443`, `:1456`, `:1468`, `:1539` (`WARN: …generated types will be missing`) | generated documents silently absent |
| generator **diagnostics discarded** | `:1502` — `diagnostics: out _` | a generator that reports an error and emits *partial* output is completely invisible |

### 1.6 The disclosure conventions to match (house style)

| convention | file:line | shape |
|---|---|---|
| `~heuristic` chip | `src/Rig.Cli/Commands/ReachesCommand.cs:267`, `:304`; `src/Rig.Cli/Rendering/TreeRenderer.cs:246` | a tilde-prefixed lowercase token appended to a rendered line |
| "dispatch fan-out … NOT a real call" | `ReachesCommand.cs:299` | a section header that states the doubt in words |
| `note:` on **stderr** | `src/Rig.Cli/Effects/EffectDerivation.cs:400-414` (`IntrinsicNote`), `:405-407` (`WriteIntrinsicNote`) — *"Stderr (not stdout) so it never corrupts tsv/llm parsing while staying visible to a human on every format"* | the footer channel |
| fires **on a cache hit** | `src/Rig.Cli/CommandLine/AmbiguityNotice.cs:19-21` | a notice class deliberately built to work without loading the graph |
| deliberately **unquantified** when the count would lie | `EffectDerivation.cs:392-399` | an inflated number is a defect in the safety mechanism itself |
| store-provenance bit → render marker | `src/Rig.Storage/Storage/RunEntity.cs` (`SourceDirty`), `src/Rig.Cli/Rendering/SourceRenderer.cs:144`, `src/Rig.Cli/Web/SourceContracts.cs:33-37` (`StoreDirty`) | **the closest precedent to this feature**: a bit recorded at index time, joined and rendered at query time, mirrored into the web DTO |

Existing tilde tags, for collision-checking: `~heuristic`, `~changed`, `~mono`, `~amplified`. No occurrence of
`uncompiled` / `compile-error` / `non-compiling` anywhere in `src/` or `tests/`.

### 1.7 The per-file table that already exists

`src/Rig.Storage/Storage/SourceFileEntity.cs` — `RunId, FileIndex, ProjectName, FilePath, Status, Confidence,
Basis, Reason, Evidence`. Written for **every** file, indexed and skipped alike
(`SolutionSourceLoader.cs:1290-1300`), including generated ones with `Basis="generated"` (`:1338-1350`), by
`src/Rig.Storage/Queries/Writes.cs:647-660` (called at `:84`). Read at query time by
`src/Rig.Storage/Queries/Reads.cs:47-74` → `src/Rig.Cli/Commands/FactCommands.cs:183-191` (`rig files
--skipped`). Domain record: `src/Rig.Domain/Data/SourceFileInfo.cs`.

**This is the disclosure's natural home and it is already plumbed end to end.**

---

## 2. Decision 1 — what exactly gets the prefix

| # | option | verdict |
|---|---|---|
| A | a column on the **fact rows** (`reference_facts` / `symbol_facts`) | **Rejected.** Derivable from `FilePath` joined against a 12,093-row table; storing it per fact adds a column to a **2,437,000-row** table (§1.3) for zero information. |
| B | prefix the **symbol id / DocID** (`!!:M:Foo.Bar`, `M:!Foo.Bar`, …) | **FATAL — see below.** |
| C | prefix only the **rendered output line**, store nothing | **Rejected as the sole mechanism, adopted as the presentation half.** |
| D | a **per-file flag** in `source_files` + a **per-project/run flag**, joined at query time | **RECOMMENDED.** |

### 2.1 Why B is fatal, not merely awkward

Prefixing a stored DocID changes the **join key**. The evidence is §1.2: rig already carries one such prefix
(`!:`, inherited from Roslyn) and every join it touches had to be re-implemented — a whole parallel
simple-name index (`FactPathFinder.GraphIndex.cs:201-206, 351-355`), a permanently-heuristic dispatch arm
(`Dispatch.cs:551-560`), a documented impossibility of ever carrying a mined fact (`Dispatch.cs:398-400`), and
a bespoke prefix-stripping parser (`FactPathFinder.cs:1288`). A **second** prefix class would fragment, at
minimum:

- `GraphIndex.Adjacency`, `MethodsByStrippedType`, `ImplsByInterface`, `MinedDispatchBySource`,
  `StrippedBaseEdges` (`FactPathFinder.GraphIndex.cs:190-220`);
- the `nodes` / `call_edges` / `dispatch_edges` tables and both FTS indexes (`symbol_fts`, `ref_target_fts`);
- the effect↔reachability join, which is literally `reachable.ContainsKey(e.EnclosingSymbolId)`
  (`CLAUDE.md` § "Effect ↔ reachability model"; `src/Rig.Cli/Commands/ReachesCommand.cs`) — a re-keyed
  enclosing id is **silently orphaned from reachability**, exactly the `P:`/`F:` failure class CLAUDE.md
  documents;
- `rig impact`, which diffs two commit-scoped stores **by symbol id**. A base/head pair where only one side
  compiled would report every symbol in the affected files as removed **and** added.

So B converts a disclosure into a **recall regression** — the opposite of its purpose. Not acceptable at any
scope. **Do not prefix a stored id.**

### 2.2 Why C alone is insufficient

A render-time-only marker cannot be recovered from a store (so a query on a warm cache or in a later session
renders clean), cannot be filtered by a `--format tsv` consumer, and cannot reach `/api/*`. `rig`'s cached
artifacts are served from disk and from the client's IndexedDB (`CLAUDE.md` § "Cache invalidation"), so a
disclosure that lives only in the renderer is a disclosure that disappears precisely when the answer is
cheapest to get.

### 2.3 Option D in detail (RECOMMENDED)

**Storage — a provenance bit at the granularity Roslyn actually reports at.**

`source_files` gains three columns (mirroring the existing `Reason`/`Evidence` idiom):

| column | type | content |
|---|---|---|
| `CompileErrorCount` | `INTEGER NOT NULL DEFAULT 0` | error diagnostics whose primary location is in this file |
| `CompileErrorCodes` | `TEXT NOT NULL DEFAULT ''` | deduped, ordinal-sorted, capped at 8 then `+N` (e.g. `CS0103,CS0246,+3`) |
| `CompileErrorFirst` | `TEXT NOT NULL DEFAULT ''` | the first diagnostic's message, capped ~200 chars |

`runs` gains three, for the location-less classes of §1.5:

| column | type | content |
|---|---|---|
| `CompileErrorFiles` | `INTEGER` | count of `source_files` rows with `CompileErrorCount > 0` |
| `CompileErrorTotal` | `INTEGER` | total error diagnostics seen |
| `PartialProjects` | `TEXT` | comma-joined `Name:reason` for projects with no compilation / a failed generator emit / a failed generator run (`no_compilation`, `generator_emit`, `generator_run`) |

**Query side — join on `FilePath`, which every renderable fact already carries:**

| fact/DTO | `FilePath` at |
|---|---|
| `SymbolFactEntity` | `src/Rig.Storage/Storage/SymbolFactEntity.cs` |
| `ReferenceFactEntity` | `src/Rig.Storage/Storage/ReferenceFactEntity.cs` |
| `DerivedEffect` | `src/Rig.Domain/Data/Facts.cs:823` |
| `PathStep` | `src/Rig.Domain/Data/Facts.cs:468` |
| `tree` nodes | **not on `TraceNode`** — via the `Locations` map, `src/Rig.Cli/Services/TreeQueryService.cs:27` |

**The one blind spot, stated up front:** `TypeRelationFactEntity` and `DispatchFactEntity` carry **no
`FilePath`** (`src/Rig.Storage/Storage/TypeRelationFactEntity.cs`,
`src/Rig.Storage/Storage/DispatchFactEntity.cs`) — the same limitation the equivalence matrix flags for its
locality assertion (`equivalence-test-matrix.md` § 1). So a **lost dispatch edge can never be chipped**, and
a lost dispatch edge is the single most consequential fact loss (it is what disconnects a reach). This is why
the design needs **two channels, not one** — see §4.1.

**Why D is precedent, not invention:** it is the same shape as `runs.SourceDirty` → `SourceRenderer` →
`SourceResponseDto.StoreDirty` (§1.6, last row): a bit recorded at index time, joined at query time, rendered
as a marker, mirrored into the web DTO.

---

## 3. Decision 2 — the disclosure vocabulary

### 3.1 The marker

| channel | token |
|---|---|
| per-line chip (human + `llm`) | **`~compile-error`** |
| machine token (tsv column value, JSON enum) | **`compile_error`** (vs `ok`) — snake_case, matching `async_handoff` / `cross_thread` / `shared_state` |
| new tsv row kind | **`compile_error`** |
| project-level reasons | `no_compilation` \| `generator_emit` \| `generator_run` |

`~compile-error` is placed exactly where `~heuristic` is placed (`ReachesCommand.cs:267`, `:304`;
`TreeRenderer.cs:246`) and follows the same tilde-chip grammar as the four existing tags (§1.6). It is
greppable (`grep '~compile-error'` — zero collisions in the repo today) and it survives `--format llm`
because the chip is part of the line, not a stderr note.

Rejected alternatives, for the record: `~uncompiled` (misstates — the file *was* compiled, with errors);
`~broken` (unclear whose brokenness); `~unbound` (collides semantically with `!:`); `~partial` (already
overloaded by "partial binding" and by `partial` types).

### 3.2 The footer note (stderr, per `EffectDerivation.cs:405-407`)

```
note: this store was built from a tree that did not fully compile — 3 of 11,938 indexed file(s) had compile
      errors, so facts from them may be MISSING or WRONG (lines marked ~compile-error). Full list:
      rig files --compile-errors
note: 1 project(s) produced NO facts at all (Contracts: no_compilation) — anything declared there is absent
      from this store, so "no callers" / "unreachable" is NOT evidence for those symbols.
```

Rules for the wording, each one earning its place:

- **"may be MISSING or WRONG", never "is wrong."** §5 case 12 is a file that errors while the queried answer
  is byte-identical to the clean tree. The chip is a *doubt* marker; overclaiming makes it a liar in the other
  direction.
- **The second line is a separate note** and is phrased as a recall warning, because a project with zero facts
  makes an *absence* argument unsound — a strictly worse failure than a doubtful presence.
- **The file count IS quantified**; the *fact* impact is not. Contrast `EffectDerivation.cs:392-399`, which
  dropped a count precisely because it was taken over a different population than the display. Here `N of M
  indexed files` is exact and taken over the same population it names.
- **`rig files --compile-errors`** is named so the note teaches its own escape hatch, mirroring
  `IntrinsicNote`'s `--intrinsic` and `AmbiguityNotice`'s "qualify the pattern".

### 3.3 How this stays distinguishable from `!:` — mandatory

They mean different things and must never be rendered as one another:

| | `!:` (existing) | `~compile-error` (new) |
|---|---|---|
| means | **this one reference** did not bind; Roslyn returned an error symbol or a candidate (`FactExtractor.cs:141-149`) | **this FILE** had ≥1 error diagnostic at index time |
| granularity | one `ReferenceFact` | one `source_files` row |
| origin | Roslyn's `GetDocumentationCommentId()` | rig's own bucketing of `Compilation.GetDiagnostics()` |
| prevalence on a clean-commit MedDBase store | 13,642 refs / 879 files (7.3 %), 13,640 of them `typeUse` (§1.3) | unmeasured — the calibration gate, §6 |
| in a tree that compiles | **common** (net48 cross-assembly partial binding) | must be **zero** |

Neither implies the other. A file can compile cleanly and still emit `!:` refs (a type in an unrestored
external assembly, reported from elsewhere); a file with a syntax error can emit zero `!:` refs (§5 case 1).
**Do not derive one from the other, do not render `!:` as `~compile-error`, and do not fold the `!:` count
into the footer.** If a future slice wants to disclose `!:` too, that is a separate marker with a separate
calibration.

---

## 4. Decision 3 — scope of contamination

**Chosen: per-FILE, keyed on the file location of Roslyn's own error diagnostics, plus a per-PROJECT channel
for the location-less failures of §1.5. No propagation to dependents. No propagation to sibling files.**

### 4.1 The argument from how Roslyn binding actually degrades

**Roslyn re-reports at every site where binding actually failed.** That is the load-bearing observation, and
it makes the diagnostic set *already* the contamination closure for *wrong* bindings. Worked through on
DeepChain (all seven sources read; `playgrounds/DeepChain/`):

- Delete `Book` from `Domain/IBookingService.cs`. The interface file itself is **legal** (an empty interface
  compiles). The error lands in `ApiGateway/BookingController.cs:14` (`_bookings.Book(dto.Id)` → CS1061) —
  i.e. **in the dependent, not in the edited file.** Per-file scope flags exactly the file whose facts
  changed.
- Break a body-local in `Foundation/Db.cs` (`Query` references an undefined local → CS0103). The public
  surface is intact, so `DataAccess` / `Business` / `ApiGateway` / `Web` all compile. Per-file scope flags one
  file; propagation would have flagged five projects for an error that changed nothing outside one method.
- Break the *declaration* (`Contracts.PatientDto` renamed). Errors appear in `DataAccess/PatientRepository.cs`,
  `ApiGateway/BookingController.cs:14` and `Web/HomePage.cs:12` — all three, independently, because each
  mentions the type. Again: the compiler enumerated the closure for us.

**Propagating to dependents is measurably fatal.** Transitive-dependent distribution over MedDBase's 187
in-source assemblies: median **6**, mean 24, p90 **68**, max **164**; 23 % of projects have 51+
(`progress/live-background-index.md` § "The measured constraint that sizes the work"). One broken hub file
would flag most of the store.

**Propagating to sibling files in the project is also fatal.** Two projects hold **66 %** of MedDBase's 2.44 M
references (`MedDBase.DataAccessTier` 38.4 %, `MedDBase.Pages` 27.5 %), and they are exactly where edits land
(*ibid.*, § "The shape"). "One file broke → flag the project" flags a third of the codebase on a single typo.

### 4.2 Where per-file under-reports — the residual gap, and its mitigation

Per-file scope is *nearly* complete for wrong bindings but **not** complete for silent fact **loss**:

1. **A dropped `DispatchFact` has no file.** Break `DataAccess/PatientRepository.cs` so it no longer
   implements `Contracts.IPatientRepository`. `Business/BookingService.cs` compiles fine (it depends on the
   *interface*), and `Web/HomePage.cs` compiles fine — but the impl dispatch edge
   `IPatientRepository.GetById → PatientRepository.GetById` **vanishes**, so `rig reaches` from
   `Web.HomePage.Show` no longer reaches `Foundation.Db.Query`. **No line in that answer carries a chip.**
2. **Location-less project failures** (§1.5) produce no `source_files` rows at all, so the per-file channel is
   structurally blind.
3. **Cascade suppression.** Roslyn suppresses some follow-on diagnostics once a symbol is an error type, so a
   file may mis-bind at more sites than its diagnostic count suggests. *(INFERRED — not verified against
   Roslyn 5.6.0's suppression tables; it is why `CompileErrorCount` is treated as **evidence**, not as a
   measure of damage, and why the chip is boolean.)*

**Mitigation, and the reason for two channels:** the **footer note is UNCONDITIONAL** — it fires on every
command whose answer is derived from a store with any compile error or any partial project, whether or not a
single rendered line carries a chip. The chip gives *locality*; the footer gives *completeness*. Neither
alone is honest.

### 4.3 What is explicitly NOT contaminated

- **Warning-severity diagnostics.** The existing filter is `d.Severity == DiagnosticSeverity.Error`
  (`SolutionSourceLoader.cs:244`). It stays. A warning does not degrade binding.
- **Disabled `#if` regions.** Roslyn does not bind disabled text, so no error diagnostic is produced from it.
  *(INFERRED for Roslyn 5.6.0 — pinned at `Directory.Packages.props:12-13`; asserted by §5 case 10, which is
  in the suite precisely so a naive text-scanning implementation fails.)*
- **Dependents and sibling files** (§4.1).

---

## 5. THE STRESS TEST

### 5.0 Harness constraints — read these first, they are traps

1. **Every broken-source fixture is written by the TEST at runtime into the TEMP copy.** Never check a
   non-compiling file into `playgrounds/` — `tests/Rig.Tests/Fixtures/AnalyzedPlaygrounds.cs` builds each
   playground **once per session and shares it**, so a checked-in broken file poisons every unrelated test
   (that shared-fixture fragility is already the diagnosed shape of
   [flaky-clientpage-proxy-extraction](../backlog/todo/flaky-clientpage-proxy-extraction.md)).
   `DeepChainPlayground.CreateAsync()` (`tests/Rig.Tests/Fixtures/DeepChainPlayground.cs:25-38`) and
   `TempPlayground.CreateAsync` (`tests/Rig.Tests/Fixtures/TempPlayground.cs:32-45`) both copy to a temp dir —
   mutate **there**.
2. **Never call `TempPlayground.BuildAsync()` in these tests.** It asserts `ExitCode == 0`
   (`TempPlayground.cs:120`), so a deliberately-broken tree fails the *fixture*, not the assertion. Rely on
   the `dotnet restore` that `CreateAsync` already runs (`:42`) and let rig's own design-time build do the
   compiling.
3. **`dotnet restore` is unconditional in both fixtures**, so the "unrestored project" arm (case 11) must
   delete `obj/project.assets.json` after create, or index the checked-in playground in place.
4. **New test file**: `tests/Rig.Tests/Analysis/FailedCompilationDisclosureTests.cs`. Never
   `CliApplicationTests.cs` (CLAUDE.md § Orchestration). Run it with
   `dotnet run --project tests/Rig.Tests --no-build -- --treenode-filter "/*/*/FailedCompilationDisclosureTests/*"`.
5. **Assert against real output.** Any rendering assertion must be written against a pasted real `rig` run,
   not against imagined formatting (CLAUDE.md: the recurring review failure).
6. **`CoreAllocations` is not used here** and that is deliberate: it is a single project with no cross-project
   binding (`playgrounds/CoreAllocations/CoreAllocations/{AllocationScenarios,CompilerLoweredScenarios,Program}.cs`),
   so it cannot exercise any contamination question. `EntryPointEffects` is used only for case 11 — its
   `Generated/GeneratedEndpoint.g.cs` is checked in, not generator-emitted, so it cannot host the generator
   cases (same finding as `equivalence-test-matrix.md` § 2).

### 5.1 The matrix

Every row: the abuse, the host, the expected **disclosure**, the expected **fact delta**, and the specific
failure it catches.

| # | Abuse | Host / edit | Expected disclosure | Expected fact delta | Catches |
|---|---|---|---|---|---|
| **1** | **Syntax error mid-method** | DeepChain `Foundation/Db.cs`: insert a stray `}` inside `Query`'s body, so `Result<T>` (`Db.cs:11-16`) falls outside `Db` | `source_files[Foundation/Db.cs].CompileErrorCount ≥ 1`, codes include a `CS1xxx` parse code; **and** `DataAccess/PatientRepository.cs` flagged (its `Db.Query` call at `:15` now errors). Footer: 2 files. Chip on both files' lines | `T:Foundation.Result\`1` and/or `M:Foundation.Db.Query(System.String)` missing or re-parented; the `Db.Query` invocation ref from `DataAccess` gone or `!:`-targeted | That parse recovery, not just binding, is covered — and that a same-project sibling gets its **own** diagnostic (the §4.1 argument, in the one case where it is least obvious) |
| **2** | **Missing type** | DeepChain `DataAccess/PatientRepository.cs`: `PatientDto` → `PatientDtoo` | flagged set == exactly `{DataAccess/PatientRepository.cs}` (CS0246, plus CS0535 for the now-unimplemented interface). Footer: 1 file | `M:DataAccess.PatientRepository.GetById(System.Int32)`'s `Signature` changes (error return type); the `new PatientDto{…}` typeUse becomes `!:PatientDtoo`; **the impl `DispatchFact` `IPatientRepository.GetById → PatientRepository.GetById` disappears** | The headline case, **and** the §4.2(1) blind spot in the same fixture: `rig reaches "Db.Query"` from `Web.HomePage.Show` loses the hop while `Web/HomePage.cs` carries **no chip** — assert the footer fires anyway |
| **3** | **Ambiguous type across two assemblies** | DeepChain, **new runtime fixture**: write `Foundation/DuplicateDto.cs` declaring `namespace Contracts; public sealed class PatientDto {…}`. `DataAccess` sees both `Contracts` and (transitively) `Foundation` | CS0433/CS0104 in every file resolving `PatientDto` while seeing both identities — expect `DataAccess/PatientRepository.cs` and `Business/BookingService.cs`; footer ≥ 2 files | `ReferenceFact.TargetAssembly` for the `PatientDto` typeUse becomes the wrong/ambiguous identity, or the ref becomes `!:PatientDto` | **The one case where a WRONG-but-plausible answer is possible without any fact going missing.** This is the duplicate-assembly-identity signature the spike already hit (`progress/live-background-index.md` § "What the spike killed": 43 errors, first `System.Object is not defined`) and the regression a resident workspace can reintroduce. `TargetAssembly`/`TargetInSource` (`ReferenceFactEntity.cs`) are the fields to assert on — the equivalence matrix (§ 1) names them as its highest-value comparison addition for exactly this reason |
| **4** | **Deleted member others still call** | DeepChain `Domain/IBookingService.cs`: delete `Book` (leaves a legal empty interface) | flagged set == exactly `{ApiGateway/BookingController.cs}` (CS1061). **`Domain/IBookingService.cs` itself must NOT be flagged.** Footer: 1 file | `M:Domain.IBookingService.Book(System.Int32)` gone; its impl `DispatchFact` gone; `ApiGateway.BookingController.Book`'s invocation ref gone or error-targeted; `Web.HomePage.Show` no longer reaches `Db.Query` | The **asymmetry**: the file the agent edited is clean, the flagged file is a different one. An implementation that flags "the file I edited" passes every other row and fails here |
| **5** | **A file fails while its dependents do not** (must-not-cascade) | DeepChain `Foundation/Db.cs`: inside `Query`, reference an undefined local (CS0103). Public surface untouched | flagged set == exactly `{Foundation/Db.cs}`. **Zero chips anywhere in `DataAccess`/`Business`/`ApiGateway`/`Web`**, four projects unflagged | Everything outside `Db.cs` **byte-identical** to the clean baseline; `rig reaches "Db.Query"` output byte-identical except the footer/chip | The negative control for §4.1. Mirrors the role row 1 of the equivalence matrix plays for the surface-hash gate: proof the scope *declines* to fire, which nothing else in the suite tests |
| **6a** | **A project that fails ENTIRELY (no compilation)** | DeepChain: corrupt `Contracts/Contracts.csproj` into invalid XML so the design-time build yields no compilation (`SolutionSourceLoader.cs:238-241`) | **No `source_files` rows exist for `Contracts` at all** → the per-file channel sees nothing. `runs.PartialProjects` must contain `Contracts:no_compilation`, and the footer must emit the second note of §3.2 | Zero facts from `Contracts`; `T:Contracts.PatientDto` and `M:Contracts.IPatientRepository.GetById` absent; dependents' refs to them unresolved | **The highest-severity case**: zero facts read as "nothing declares/calls this". Proves the project channel is not optional. An implementation with only the per-file channel is **silent** here |
| **6b** | **A project's whole surface removed** | DeepChain: delete `Contracts/PatientDto.cs` outright | 4-5 files flagged across `Contracts`/`DataAccess`/`Business`/`ApiGateway`/`Web`; footer names the count | `T:Contracts.PatientDto` gone; every signature mentioning it re-keys; `M:ApiGateway.BookingController.Book(Contracts.PatientDto)` DocID changes | Multi-project fan of *located* diagnostics — the contrast arm to 6a, and equivalence-matrix row 7's "delete a whole type others reference" |
| **7a** | **Partially-generated generator output** | `LegacyNet48Web`: break `Pages/Proxies/InvoiceEditProxy.cs` (the checked-in `ClientPage` subclass the generator keys on) with CS0246 | The **source** file flagged; the generated `*_RequestProxy.g.cs` / `*_ResponseProxy.g.cs` for that page absent or malformed. **Generated `source_files` rows (`Basis="generated"`, `SolutionSourceLoader.cs:1338-1350`) must never be silently dropped**; a diagnostic located in a generated document flags that generated row **and** rolls up to the project channel | The generated proxy type + its `Show`/`ShowDialog`/`Redirect` members missing | The case where "which file changed" is a **generator input**, not the generated output. Note `diagnostics: out _` at `:1502` discards generator diagnostics today — this row is the reason to stop discarding them |
| **7b** | **Generator run fails entirely** | `LegacyNet48Web`: force `RunSourceGeneratorsAsync`'s `catch` (`:1533-1541`) — e.g. delete `Stubs/MedDBaseStubs.cs`'s `MMS.Web.UI.ClientPage` so `GetTypeByMetadataName` returns null (`RequestResponseProxyGenerator.cs:22-23`), or make the generator throw | `runs.PartialProjects` contains `LegacyNet48Web:generator_run`; footer emits the recall note. **No per-file signal exists** (no diagnostic, no rows) | **Zero** generated documents; `LoginProxy` and every other proxy type absent from the store | The other location-less class. **Bonus:** this is also a diagnosis channel for the long-standing ClientPage flake — the `GetTypeByMetadataName`-returns-null-on-ambiguity lead in `progress/live-background-index.md` § "Lead on the long-standing ClientPage flake" and [flaky-clientpage-proxy-extraction](../backlog/todo/flaky-clientpage-proxy-extraction.md). Today that failure is *invisible in the store*; after this slice it is a recorded fact |
| **8** | **broken → fixed → broken across successive incremental edits** | DeepChain over **one retained `RigWorkspace`**, via the existing seams `SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync` (`src/Rig.Analysis/SolutionAnalyzer.cs:81`) + `ExtractFromSolutionAsync` (`:126`) + `Solution.WithDocumentText`, exactly as `tests/Rig.Tests/Analysis/IncrementalExtractionSpikeTests.cs:66-71` does. E0 clean → E1 break `Foundation/Db.cs` (CS0103) → E2 revert to the **original text** → E3 break again | E0: ∅. E1: `{Foundation/Db.cs}`. **E2: ∅** — the flag must CLEAR. E3: `{Foundation/Db.cs}` with codes identical to E1 | E2's fact set **SET-EQUAL** to E0 (same `BodyHash`, same `Line`/`EndLine`, no residual line shift) — the same revert property as equivalence-matrix row 20. E3 == E1 | **Two distinct sticking bugs.** (a) The flag accumulated in a resident set and never cleared → sticks forever; that lies "safe", which is worse, because it trains the reader to ignore the marker (§6). (b) Diagnostics memoized per file path rather than recomputed from the **current** compilation. Neither is visible to a single-edit test by construction |
| **9** | **Warnings only must NOT fire** | DeepChain `Web/HomePage.cs`: add an unused local / `#warning` | `CompileErrorCount == 0`; no chip; **no footer at all** | none | Guards `d.Severity == DiagnosticSeverity.Error` (`SolutionSourceLoader.cs:244`) against being loosened to `>= Warning`, which on a real index would flag nearly everything |
| **10** | **Error inside an excluded `#if` region must NOT fire** | DeepChain `Foundation/Db.cs`: wrap syntactic garbage in `#if NEVER_DEFINED … #endif` | no flag, no chip, no footer | none | A naive "does the file contain something that looks broken" implementation. Also pins the INFERRED claim in §4.3 to a test rather than to a belief |
| **11** | **The NOISE arm — a whole project unrestored** | `EntryPointEffects` (has its own `NuGet.config` + `Directory.Packages.props`), created via `TempPlayground.CreateEntryPointEffectsAsync()` then `obj/project.assets.json` deleted (see §5.0(3)); index without `--restore` (the default since `eb6480ff`) | **Near-100 % of the project's files flagged** (mass CS0246/CS0234). Acceptance is a **policy** assertion: above the saturation threshold (§6.2) the per-line chip is **SUPPRESSED for that project** and replaced by ONE project-level footer line — `project 'EntryPointEffects.Api' did not compile at all (N of M files) — chips suppressed` | large, uninteresting | The false-positive worst case, and the only row that tests the *ignore-threshold* mitigation rather than the signal. Without it, the chip is on every line of a real MedDBase index |
| **12** | **Error present, ZERO fact loss** (precision) | DeepChain `Web/HomePage.cs`: add a **new** method whose body has `int x = "s";` (CS0029), leaving `Show()` (`HomePage.cs:12`) untouched | `{Web/HomePage.cs}` flagged; chip appears on `Web.HomePage.Show` lines | `M:Web.HomePage.Show`'s facts and the **entire reach to `Db.Query`** byte-identical to the clean baseline | That the chip is a **doubt** marker, not a claim of wrongness — pins the §3.2 wording ("may be MISSING or WRONG"). Also the honest over-firing demo: a sound answer wearing a chip |
| **13** | **Real-store calibration** (not a unit test) | one `rig index <MedDBase.slnx> --rules rig.rules.json` from `c:/git/meddbase-analysis`, then `rig files --compile-errors` | the firing rate and its per-project histogram | n/a | The **gate** before the chip goes on by default (CLAUDE.md: FP-calibrate on the real store; "a structurally-true detector that fires 179× is still noise"). See §6.2 |

### 5.2 Playground coverage summary

| host | rows | new fixture needed |
|---|---|---|
| `playgrounds/DeepChain` (7 projects, `Web→ApiGateway→Business→{Domain,DataAccess}→Contracts→Foundation`; all 7 sources read) | 1, 2, 4, 5, 6a, 6b, 8, 9, 10, 12 | **row 3 only**: `Foundation/DuplicateDto.cs`, a second `Contracts.PatientDto` — written by the test into the temp copy, never checked in (§5.0(1)) |
| `playgrounds/LegacyNet48Web` (+ `ProxyGenerator` as `OutputItemType="Analyzer"`, `LegacyNet48Web.csproj:15-17`; real `[Generator] : ISourceGenerator` at `ProxyGenerator/RequestResponseProxyGenerator.cs:12`) | 7a, 7b | none — the wiring already exists |
| `playgrounds/EntryPointEffects` | 11 | none, but delete `obj/project.assets.json` post-create (§5.0(3)) |
| `playgrounds/CoreAllocations` | — | not used, deliberately (§5.0(6)) |
| MedDBase real store | 13 | n/a |

### 5.3 Test file layout

One new file, `tests/Rig.Tests/Analysis/FailedCompilationDisclosureTests.cs`:

- **One parameterized test** over rows {1, 2, 4, 5, 9, 10, 12} — they share the host (DeepChain), the arms
  (index the broken temp tree, read `source_files`), and the assertion shape (`flaggedFiles` set + a fact-delta
  set-difference against a clean baseline). `[MethodDataSource]` table of
  `(editedFile, mutate, expectedFlaggedFiles, expectedFactDelta)`. Print the symmetric difference before
  asserting, exactly as `IncrementalExtractionSpikeTests.cs:96-112` does, so evidence survives a red run.
- **Separate tests** for rows 3 (needs the extra fixture written first, and asserts on
  `TargetAssembly`/`TargetInSource`), 6a (asserts on `runs`, not `source_files`), 6b, 7a, 7b (different
  playground, different arms), 8 (multi-edit over one retained workspace — different harness shape entirely),
  and 11 (asserts a *suppression policy*, not a flag).
- Row 13 is a **doc'd manual gate**, not a test.

---

## 6. The false-negative / false-positive risk

### 6.1 Which way this design errs: **NOISY**, decisively

Not a guess — three measurements point the same way:

1. **rig's design-time builds routinely produce errors on healthy trees.** `SolutionSourceLoader.cs:205-210`
   says so in its own comment: *"Design-time builds commonly miss code-generated types (proxy generators, T4
   templates, source generators). The semantic model is still valid for code that doesn't reference the
   missing types."* And `--restore` is **opt-in** (default `restore: false`, `SolutionSourceLoader.LoadAsync`;
   made opt-in in `eb6480ff` to cut 524 s → 253 s), so unrestored projects are an expected, deliberate state.
2. **A measured floor of 7.3 %.** 879 of 12,093 files in the current clean-commit MedDBase store already carry
   ≥1 `!:` reference, and 13,640 of those 13,642 refs are `typeUse` — the CS0246/CS0234 family (§1.3). Those
   unresolved type names came from *somewhere*; wherever the corresponding diagnostic is located is a file
   this feature will flag on every index. **≥7.3 % of files is the floor for the firing rate on a tree the
   developers consider fine.**
3. **A chip on 7 %+ of lines is above the ignore threshold.** The repo has already learned this shape twice:
   the intrinsic-effects count was removed for overstating by 8× (`EffectDerivation.cs:392-399`), and
   CLAUDE.md records "a structurally-true detector that fires 179× is still noise".

The failure mode is therefore **not** "the disclosure misses the broken tree" — it is "the disclosure is on so
often that the agent stops reading it", at which point it is *worse than nothing*, because it launders the
genuinely-broken case. Row 11 and row 8(a) are in the suite specifically to catch the two ways that happens.

### 6.2 Calibration plan

Three knobs, in the order they should be turned:

1. **Measure first, ship second (the gate).** Row 13: one MedDBase index, then
   `rig files --compile-errors | wc -l` and a per-project histogram. Record the number in the backlog item.
   The chip does **not** go on by default until that number is known — CLAUDE.md's FP-calibration rule.
2. **Project saturation rollup** (row 11). When a project's flagged-file ratio exceeds a threshold, suppress
   per-line chips for that project and emit one project-level footer line instead. **Proposed initial
   threshold: 50 % of the project's indexed files, floor 10 files** — chosen so a genuinely-half-broken
   project reads as one line rather than thousands of chips, while a project with a handful of broken files
   keeps per-line locality. Treat the number as provisional and re-set it from the row-13 histogram; the
   *shape* (a rollup exists) is the decision, the *value* is calibration.
3. **Baseline-relative mode** (the real fix, and the reason this spec exists on this branch). In the resident
   tier, the base commit store is known-good, so the high-signal question is not "does this file have errors"
   but "does this file have errors it did **not** have at the indexed commit". Firing rate then ≈ the number
   of files the agent just touched — the actual question, at the actual granularity. That is
   [live-background-index](../backlog/progress/live-background-index.md) **Slice 5**, and this spec's
   `source_files` columns are exactly the baseline it diffs against. **Do not build Slice 5's mode here**; do
   make the columns sufficient for it (they are: per-file count + codes).

### 6.3 Where it errs SILENT, stated for the record

Three known false negatives, all from §4.2: a lost `DispatchFact` has no file to chip; location-less project
failures have no rows; Roslyn's cascade suppression means the count under-states site damage (INFERRED). All
three are covered — imperfectly but honestly — by the **unconditional footer**, which is why the footer must
never be made conditional on a chip having been rendered.

---

## 7. Where it surfaces — per surface

| surface | needs it? | how |
|---|---|---|
| `rig index` | **yes** — upgrade | persist per-file + per-project; keep the `:212` summary but name the partial projects; **fix the uncapped `Console.WriteLine` firehose at `:247`** (route through `ReportProgress`, cap like `:214-221`) |
| `rig files --compile-errors` | **yes — NEW** | exact sibling of `--skipped` (`FactCommands.cs:170-191`); columns: project, file, count, codes, first message |
| `rig runs` | **yes** | one `partial=…` line per run beside the existing `commit=…+dirty` line (`FactCommands.cs:98-103`) — store-level provenance belongs on the same channel as dirty |
| `rig derive` | **yes** | footer notes; `compile_error` **row kind** in tsv (mirroring how `amplification` was added as its own kind, `DeriveCommand.cs:262`, `:300`); `bindingHealth` as the **trailing** column of the `effect` row (`DeriveCommand.cs:285`) |
| `rig reaches` | **yes** | chip beside `~heuristic` (`ReachesCommand.cs:267`, `:304`) + footer. Highest-stakes surface: reach loss is the silent answer |
| `rig tree` | **yes** | chip via the `Locations` map (`TreeQueryService.cs:27`; `TreeRenderer.cs:246` for placement) + footer |
| `rig callers` | **yes, footer only** | "nothing calls X" is a lie when X's callers didn't compile. A chip is optional here (`CallersCommand.cs` renders symbols, not sites) |
| `rig path` | **yes** | chip per `PathStep` (`Facts.cs:468` has `FilePath`) + footer. "No path found" is the dangerous output |
| `rig impact` | **yes, footer — and it must name WHICH SIDE** | a broken head diffed against a clean base is mostly noise. Slot: beside `ImpactCommand.SyncModeDisclosure` (`src/Rig.Cli/Commands/ImpactCommand.cs:745`, `:761`) |
| `rig show` | **no new channel** | it renders source text; `SourceRenderer`'s origin marker (`SourceRenderer.cs:138-146`) already discloses provenance |
| `rig dead` | **n/a** | disabled (`CommandLine/Root.cs`; CLAUDE.md § "Two-stage design") |
| `/api/meta` | **yes** | a `compileErrors: { files, total, projects: [{name, reason}] }` block — the client needs it for the banner |
| `/api/tree`, `/api/reaches`, `/api/hazards`, `/api/path`, `/api/impact` | **yes** | a `bindingHealth` field on the per-node / per-effect DTOs plus the store-level block, mirroring `SourceResponseDto.StoreDirty` (`SourceContracts.cs:33-37`). The web UI must not be the surface that hides it |
| `--format tsv` | **yes, stdout-safe** | new row kind + trailing column only; the **footer stays on stderr** (`EffectDerivation.cs:405-407`) |
| `--format llm` | **yes** | the chip rides in the line |

**Channel decision:** the footer belongs in the **existing stderr-notice family**
(`AmbiguityNotice`, `SeedResolutionNotice`, `EffectDerivation.WriteIntrinsicNote`) as a **new**
`src/Rig.Cli/CommandLine/CompilationHealthNotice.cs`, called from the same sites. It needs its own class
rather than folding into an existing one for one specific reason: **it must fire on a FULL CACHE HIT**, where
no graph and no facts are loaded — the same requirement that shaped `AmbiguityNotice` (see its comment at
`AmbiguityNotice.cs:19-21` and the `distinctTargets` overload built for it). A notice that reads the graph to
decide whether to warn is a notice that goes silent exactly when the query is cheapest.

**Web slice** (per CLAUDE.md's CLI-vs-web design gate): the `/api/meta` block + DTO fields + the banner are
**in scope for this feature**, not a follow-on. Serving the disclosure only on the CLI would make the web UI
the surface that hides it, which is the same defect as
[web-api-seed-and-effect-disclosure-parity](../backlog/todo/web-api-seed-and-effect-disclosure-parity.md).

---

## 8. Storage schema + cache invalidation

### 8.1 Bump `SchemaVersion.Index` 5 → 6

`src/Rig.Storage/SchemaVersion.cs:21`. Add the trail comment in the existing style:

```
// v5->v6: persist per-file compile-error provenance (source_files.CompileErrorCount/Codes/First) and the
//         per-run partial-project roll-up (runs.CompileErrorFiles/Total, runs.PartialProjects). A pre-v6
//         store carries no error data, and reading its absence as "everything compiled" is precisely the
//         silent lie this feature exists to remove.
```

**Why bump rather than probe.** The alternative is `StorageProbes.ColumnExistsAsync` + a tri-state
`unknown`, per the drift note at `src/Rig.Storage/Queries/SchemaGate.cs:50-62`. Rejected: an `unknown`
disclosure would fire on **every** pre-existing store — the fires-on-everything failure of §6.1, introduced by
the very mechanism meant to prevent it. The repo's stated philosophy is tripwire-not-migration
(`SchemaVersion.cs:3-6`: *"a TRIPWIRE, not a migration system"*), and the add-without-bump path is documented
as the trap that already bit them (`SchemaGate.cs:50-56`). **Cost, stated honestly:** every store must be
re-indexed once — ~253 s on MedDBase (`progress/live-background-index.md` § "Why").

### 8.2 Cache axes

- **The DATA needs no new axis.** `source_files` lives in `rig.db`, so the store-identity axis (size+mtime,
  `QueryCacheKeys.StoreKey`, `src/Rig.Cli/Caching/QueryCacheKeys.cs:53-60`) already invalidates on re-index.
- **Do NOT bake the flag into any cached blob** — not the tree payload, not the `Locations` sidecar
  (`TreeCommand.cs:315-322`, `:769-776`), not the hazard-effects blob. Join it at render/response time from a
  fresh `source_files` projection (12,093 rows on the real store — trivial). Consequence: **no existing
  `*Schema` constant needs bumping**, and every warm cache stays warm. That is the point.
- **The client DOES need one bump**, because it caches whole API responses keyed on
  `/api/meta`'s `derivationVersion` (`src/Rig.Cli/Web/RigApiEndpoints.cs:336-343`): a browser with a warm
  entry would keep rendering pre-feature responses, i.e. **stale silence**. Add
  `internal const int DisclosureSchema = 1;` to `QueryCacheKeys` and fold it into
  `DerivationSchemaToken()` (`QueryCacheKeys.cs:47-48`), with the same rationale comment
  `FindingViewSchema` carries (`:29-36` — *"No SERVER key uses this constant … it exists to move
  DerivationSchemaToken, which is the client's only invalidation signal"*). Bump it whenever the disclosure
  vocabulary or its firing rule changes.
- **Do NOT re-introduce an MVID / build-timestamp / app-version axis** (CLAUDE.md § "Cache invalidation";
  removed 2026-07-06). Nothing in this design needs one.
- Note for the reviewer: `/api/meta`'s `derivationVersion` **already** lacks a store-identity axis — a
  separate live bug, tracked at
  [api-meta-derivation-version-lacks-store-identity](../backlog/todo/api-meta-derivation-version-lacks-store-identity.md).
  This feature does not fix it and does not depend on it, but a browser on a re-indexed same-commit store will
  serve a stale disclosure until it lands. **Say so in review; do not silently rely on the bump above to cover
  it.**

---

## 9. Owned files + ordered steps

**Owned (the coding agent may edit these and only these):**

```
src/Rig.Analysis/Inventory/SolutionSourceLoader.cs
src/Rig.Analysis/RoslynAnalysisModels.cs
src/Rig.Analysis/SolutionAnalyzer.cs
src/Rig.Domain/Data/SourceFileInfo.cs
src/Rig.Domain/Data/AnalysisResult.cs
src/Rig.Domain/Data/RunSummary.cs
src/Rig.Storage/Storage/SourceFileEntity.cs
src/Rig.Storage/Storage/RunEntity.cs
src/Rig.Storage/SchemaVersion.cs
src/Rig.Storage/Queries/Writes.cs           (AddSourceFiles / the run row)
src/Rig.Storage/Queries/Reads.cs            (new loaders)
src/Rig.Cli/CommandLine/CompilationHealthNotice.cs        (NEW)
src/Rig.Cli/Commands/FactCommands.cs        (files --compile-errors, runs partial= line)
src/Rig.Cli/Commands/DeriveCommand.cs
src/Rig.Cli/Commands/ReachesCommand.cs
src/Rig.Cli/Commands/TreeCommand.cs
src/Rig.Cli/Commands/PathCommand.cs
src/Rig.Cli/Commands/CallersCommand.cs
src/Rig.Cli/Commands/ImpactCommand.cs
src/Rig.Cli/Rendering/TreeRenderer.cs
src/Rig.Cli/Rendering/SourceFileRenderer.cs
src/Rig.Cli/Caching/QueryCacheKeys.cs       (DisclosureSchema only)
src/Rig.Cli/Web/RigApiEndpoints.cs
src/Rig.Cli/Web/WebContracts.cs, PathContracts.cs
src/Rig.Cli/wwwroot/*                       (banner)
tests/Rig.Tests/Analysis/FailedCompilationDisclosureTests.cs   (NEW)
```

**Explicitly NOT owned:** `FactExtractor.cs` (no extraction change is needed — that is a deliberate property
of Option D), `FactEffectDeriver.cs`, `FactHazardDeriver.cs`, `builtin-rules.json`, `FactPathFinder*.cs`,
`CliApplicationTests.cs`, anything under `playgrounds/` (§5.0(1)), any `docs/` file other than this spec's
backlog sibling.

**Ordered steps:**

1. **Bucket the diagnostics.** In `SolutionSourceLoader.ProcessProject` (`:228-262`), keep the existing
   `Severity == Error` filter (`:244`) but bucket each diagnostic by
   `diagnostic.Location.GetLineSpan().Path` (fall back to `Location.SourceTree?.FilePath`). Diagnostics whose
   `Location.Kind` is not a source file, or whose path is not one of the project's documents, go to the
   **project** bucket. Pass the per-file map into `LoadProjectSourcesAsync` (`:1261`) — it is called from the
   same method, so no new plumbing crosses a boundary. Route the `:247` `Console.WriteLine` through
   `ReportProgress` with the same cap style as `:214-221`.
   *Both the cold path and the incremental path go through `ReadSolutionSourcesAsync` (`:130`), so the
   incremental seam (`SolutionAnalyzer.cs:126`) gets this for free — do not add a second collection site.*
2. **Carry it out.** `SourceFileInfo` (+3 fields, defaulted, additive); `SolutionSourceSet`
   (`RoslynAnalysisModels.cs:12`) gains a project-health list; `AnalysisResult` carries it to the writer.
3. **Persist.** 3 columns on `SourceFileEntity`, 3 on `RunEntity`; write in `Writes.AddSourceFiles`
   (`Writes.cs:647`) and the run row; bump `SchemaVersion.Index` 5→6 with the trail comment (§8.1).
4. **Read.** `Reads.LoadCompileErrorFilesAsync` (mirror `LoadSkippedSourceFilesAsync`, `Reads.cs:47-74`) and a
   `LoadCompilationHealthAsync` rollup usable **without** loading the graph (§7's cache-hit requirement).
5. **`CompilationHealthNotice`.** Footer wording per §3.2, the saturation rollup per §6.2(2), stderr only.
6. **Chips.** `~compile-error` in `TreeRenderer`, `ReachesCommand`, `PathCommand`, `DeriveCommand`.
7. **Machine formats.** `compile_error` row kind + `bindingHealth` trailing column in `derive` tsv.
8. **CLI surface.** `rig files --compile-errors`; `rig runs` `partial=` line.
9. **Web.** `/api/meta` block, DTO fields, banner, `DisclosureSchema = 1` folded into
   `DerivationSchemaToken()`.
10. **Tests.** `FailedCompilationDisclosureTests.cs` per §5.

**Hard constraints for the agent:** do NOT commit; do NOT touch `playgrounds/`; do NOT edit
`CliApplicationTests.cs` (flag it if an existing test pins old behaviour); do NOT run csharpier (mini-ci
formats on publish); verify every rendering assertion against a **pasted real `rig` run**.

---

## 10. Acceptance checks (runnable)

```pwsh
# 1. The new suite (TUnit treenode filter — `dotnet test --filter` does NOT work here)
dotnet run --project tests/Rig.Tests --no-build -- --treenode-filter "/*/*/FailedCompilationDisclosureTests/*"

# 2. Full suite green (no regression in the 1003-test baseline; ClientPage flake is pre-existing)
dotnet test
```

```bash
# 3. Broken-tree arm, on a scratch copy of DeepChain with Foundation/Db.cs broken (CS0103, row 5)
rig index DeepChain.slnx
rig files --compile-errors                       # exactly 1 row: Foundation/Db.cs
rig derive 2>&1 >/dev/null | grep -c 'did not fully compile'      # >= 1
rig derive --format tsv | grep -c '^compile_error'                # == 1
rig derive --format tsv > broken.tsv             # stdout must stay parseable

# 4. MUST-NOT-CASCADE: the answer itself is unchanged (row 5)
rig reaches "Db.Query" --format tsv > broken.reach.tsv
# ... revert Foundation/Db.cs, re-index ...
rig reaches "Db.Query" --format tsv > clean.reach.tsv
diff broken.reach.tsv clean.reach.tsv            # empty — stdout identical; only stderr differs

# 5. The flag CLEARS (row 8)
rig files --compile-errors                       # zero rows after the revert
rig derive 2>&1 >/dev/null | grep -c 'compile'   # 0 — no note on a clean tree

# 6. Project-level channel (row 6a) — corrupt Contracts.csproj, re-index
rig runs | grep partial=                         # names Contracts:no_compilation
rig derive 2>&1 >/dev/null | grep -c 'produced NO facts'          # >= 1
```

```pwsh
# 7. THE CALIBRATION GATE (row 13) — real store, before the chip goes on by default
cd C:\Git\meddbase-analysis
rig index <MedDBase.slnx> --rules rig.rules.json
rig files --compile-errors | Measure-Object -Line          # record this number in the backlog item
rig files --compile-errors --format tsv | ForEach-Object { ($_ -split "`t")[0] } | Group-Object | Sort-Object Count -Descending | Select-Object -First 20
```

---

## 11. Explicitly out of scope

- **Re-running the compiler at query time.** The two-stage split (Roslyn at index, no Roslyn at derive) is
  load-bearing and not negotiable (CLAUDE.md § "Two-stage design"). The disclosure is a *recorded fact*, like
  every other fact in the store.
- **Per-symbol or per-site error attribution.** Roslyn reports per location; rig records per file. Finer
  granularity buys nothing the chip can render and re-opens the join-fragmentation question of §2.1.
- **Fixing any of the errors**, and any `--fail-on-compile-errors` exit code. This is disclosure, not a gate.
- **The baseline-relative mode** — that is Slice 5 of
  [live-background-index](../backlog/progress/live-background-index.md); this spec supplies the columns it
  diffs against (§6.2(3)).
- **Disclosing `!:` prevalence.** A separate marker with a separate calibration (§3.3). Do not fold it in.
- **Fixing `/api/meta`'s missing store-identity axis** — separate backlog item, §8.2.
- **Un-discarding generator diagnostics beyond what row 7a needs** (`SolutionSourceLoader.cs:1502`) — capture
  them for the project channel; a full generator-diagnostic surface is its own item.
