# Slice 2 — stop retaining every `SemanticModel`: implementation spec

**Status:** SPEC (read-only analysis; no code changed). Branch `live-background-index`. Implements
`docs/memory-optimization-strategies.md` §A1 as **Slice 2** of
[live-background-index](../backlog/progress/live-background-index.md). §A2 (sliding-window
`Compilation` lifetime) is explicitly OUT of scope here — see §6.

Roslyn is pinned at **5.6.0** (`Directory.Packages.props:12-13`). Every Roslyn claim below is from
`ilspycmd` over the pinned assemblies in `~/.nuget/packages/microsoft.codeanalysis.*/5.6.0/lib/net10.0/`,
cited as `(decompiled <Type>:<line>)`. Anything not directly citable is labelled **INFERRED**.

## Verdict up front

1. **No downstream consumer needs live semantic info after the load returns.** The load→extract boundary
   is a clean seam: exactly two members of `SolutionSourceSet` are read, one is the `SourceModel` list
   consumed only by `FactExtractor`/`DiRegistrationExtractor`, the other is plain `SourceFileInfo`
   records. `AnalysisResult` (`Rig.Domain/Data/AnalysisResult.cs:3-13`) exposes no Roslyn type. Streaming
   requires giving nothing else to anybody.
2. **Dropping a `SourceModel` genuinely releases its `SemanticModel`.** Verified, not assumed — see §1.1.
   This was the one way the whole slice could have been a no-op.
3. **Fact identity is preservable exactly**, provided the global `OrdinalIgnoreCase` FilePath sort is kept
   (§4). Confidence: high.
4. **The acceptance bar in the progress doc ("peak working set falls materially") is the wrong gate** and
   should be restated on *live set*. Reason and evidence in §5.4. This is a decision for the caller.

---

## 1. Exact retention map

Every site that holds a `SemanticModel`, `Compilation`, or red syntax root beyond the project that
produced it. "Scope" = how long the reference is reachable today.

| # | Site | What it pins | Scope today |
|---|---|---|---|
| R1 | `RoslynAnalysisModels.cs:23` `SourceModel(… SyntaxTree Tree, SyntaxNode Root, SemanticModel SemanticModel)` | one file's red root + `SyntaxTreeSemanticModel` → its `CSharpCompilation` | as long as the record is reachable |
| R2 | `RoslynAnalysisModels.cs:12` `SolutionSourceSet.IndexedSources` | **every** `SourceModel` of **every** project | whole run |
| R3 | `SolutionSourceLoader.cs:1604` `ProjectSourceLoadResult.Sources` | one project's `SourceModel`s | whole run (via R4) |
| R4 | `SolutionSourceLoader.cs:174` `projectResults` `ConcurrentBag<ProjectSourceLoadResult>` | accumulates R3 across the whole `Parallel.ForEachAsync` | until `:224-227` builds R2 |
| R5 | `SolutionSourceLoader.cs:1267` `sources` list + `:1317` `compilation.GetSemanticModel(tree)` + `:1319-1327` `sources.Add(new SourceModel(...))` | the per-file model, at creation | flows into R3 |
| R6 | `SolutionSourceLoader.cs:1482,1487-1495` generated-file `SourceModel`s bound to `generatedCompilation` | a **second, separate** `Compilation` per generator-bearing project (produced by `RunGeneratorsAndUpdateCompilation`, `:1465-1470`) | whole run, via R3→R2 |
| R7 | `SolutionSourceLoader.cs:234` `var compilation = await project.GetCompilationAsync(ct)` in `ProcessProject` | one `Compilation` | one `ProcessProject` call — **already correctly scoped**; it is R1/R6 that extend its life |
| R8 | `SolutionAnalyzer.cs:153` `var sources = sourceSet.IndexedSources` + the `Parallel.For` closure at `:171-184` | all of R2, captured in a display class left on parked thread-pool threads | whole run (documented at `:192-199`) |
| R9 | `SolutionAnalyzer.cs:166` `new SymbolStringCache()` | strong `ISymbol`/`ITypeSymbol`/`INamespaceSymbol` keys (`SymbolStringCache.cs:25-27`) → `SourceAssemblySymbol._compilation` → the whole `CSharpCompilation` | whole run |
| R10 | `SolutionAnalyzer.cs:280` `ProfilingPause.MaybePause("extract-peak (roslyn live)")` | nothing itself; it *holds the process* at the co-resident peak, by design | opt-in |

### 1.1 The load-bearing verification: is the `SemanticModel` reachable only from `SourceModel`?

If a workspace `Compilation` cached its `SemanticModel`s, dropping `SourceModel` would free nothing and
this slice would be worthless. It does not:

- `Compilation.GetSemanticModel` consults `Compilation.SemanticModelProvider` and falls back to
  `CreateSemanticModel` → `new SyntaxTreeSemanticModel(...)` — a **fresh instance, cached nowhere**
  (decompiled `CSharpCompilation:4011-4034`, `:4031-4034`). `SemanticModelProvider` is `internal` and set
  only via ctor / `WithSemanticModelProvider` (decompiled `Compilation:425,613-617,784`).
- **`Microsoft.CodeAnalysis.Workspaces.dll` 5.6.0 never references `SemanticModelProvider`** — 0 hits,
  case-insensitive, over 973,209 lines of decompiled IL. So compilations handed out by
  `Project.GetCompilationAsync` carry a **null** provider.
- The only implementation, `Diagnostics.CachingSemanticModelProvider`, *does* hold a strong
  `ConcurrentDictionary<SyntaxTree, SemanticModel>` (decompiled `CachingSemanticModelProvider:22-46`) —
  but it is reachable only through `CompilationWithAnalyzers`/`AnalyzerDriver`, which rig never uses
  (no reference anywhere in `src/`).
- `CSharpCompilation._binderFactories` / `_ignoreAccessibilityBinderFactories` are
  `WeakReference<BinderFactory>[]` (decompiled `CSharpCompilation:1969-1971,4046-4075`), so the binder
  caches the `GetDiagnostics()` warm-up (`SolutionSourceLoader.cs:243`) fills are not strongly held by
  the compilation either.

**Conclusion:** the retained `SyntaxTreeSemanticModel` (and its `MemberSemanticModel` bound-node maps) is
the strong root. Release `SourceModel` and it becomes unreachable. ✅

### 1.2 `SymbolStringCache` — confirmed cross-project pin; its lifetime must become per project

Its four maps are keyed by strong symbol references (`SymbolStringCache.cs:25-27`; `_modifiers` at `:28`
is keyed by `int` and pins nothing — `symbol` rides through `GetOrAdd`'s TArg, not a closure, `:70-71`).
A source symbol reaches its compilation directly: `SourceAssemblySymbol` holds
`private readonly CSharpCompilation _compilation` (decompiled `SourceAssemblySymbol:53,136,476`). Under
`CompilationReference` (rig wires the whole in-set closure as live `ProjectReference`s,
`SolutionSourceLoader.cs:1024-1066`), a file in project *B* binds symbols **owned by project *A*'s
compilation**, so one run-global cache pins essentially every compilation in the solution.

Required lifetime: **one instance per project, dropped with the project.** This is
*value*-neutral — every memo is a pure function of its key (the soundness argument is already written out
at `SymbolStringCache.cs:6-22,45-52`), so splitting the cache cannot change a single fact. It costs only
the cross-project half of the memo (a DocID recomputed once per project instead of once per run) and the
cross-project *string identity* sharing. §3 step 4 restores the latter with a run-global value interner.

Independent corroboration: `docs/spikes/extraction-granularity-audit.md:330-341` reached the same
conclusion from the resident-patching angle ("rebuild the `SymbolStringCache` per patch … do not carry the
instance across an edit").

### 1.3 Two stale comments that describe a release which no longer happens

`SolutionAnalyzer.cs:192-199` says the workspace is disposed and `IndexedSources` cleared after extraction.
`RoslynAnalysisModels.cs:8-11` says `SolutionSourceSet` carries the `Workspace` out so the caller can
dispose it. **Neither is true today.** Both were introduced by `10f81b48` ("Reduce post-extract retained
memory: dispose workspace + intern fact strings") and both mechanisms — the `Workspace` member and the
`StringInterner` — were removed by `9ad3ae1f` ("micro") when `SymbolStringCache` replaced the interner.
`SolutionSourceLoader.cs:64` records the current truth: *"LoadAsync never disposed it anyway."* Fixing
these comments is part of this slice; leaving them is how the next reader concludes A1 is already done.

---

## 2. What consumes `IndexedSources` after the load — the crux

Complete enumeration. `grep -rn "IndexedSources"` over `src/` + `tests/` returns exactly three hits, all
in `SolutionAnalyzer.cs` (`:153`, and `:277` in a comment). `SolutionSourceSet` is `internal` and has no
other reader; no test references it.

| Consumer | Reads | Needs live semantics? |
|---|---|---|
| `FactExtractor.Extract(SourceModel, SymbolStringCache)` — `FactExtractor.cs:20-24` | `source.SemanticModel`, `.Root`, `.Tree` | **YES** — but only while its own file is being extracted |
| `DiRegistrationExtractor.FindDiRegistrations(SourceModel, rules, names)` — `:17` | same `SourceModel` | **YES** — same one-file window |
| `SolutionAnalyzer.cs:213-242` — count pass + concat | `SourceExtractionResult` fact lists only | no |
| `SolutionAnalyzer.cs:246-260` — `XmlDiMiner.Mine(rules)` + static mappings | `rules` only; mines XML files off disk | no (run-global, but Roslyn-free) |
| `SolutionAnalyzer.cs:289-300` — `AnalysisResult` construction | `sourceSet.SourceFiles` (plain records) + the fact lists | no |
| `IndexCommands.cs:320` and everything after it (save, `GraphMaterializer`, `FactGraphProjection`) | `AnalysisResult` | no — `AnalysisResult` exposes no Roslyn type |
| `SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync` (`:75-103`, spike seam) | returns the `AdhocWorkspace`, **not** the source set | no (holds the `Solution`, which is §6's point) |

**So the crux resolves cleanly: the only consumers of live semantic state are the two per-file extractors,
and both are pure functions of one file.** Confirmed by audit: no static mutable state anywhere in
`src/Rig.Analysis/Extraction/` (grep for `static readonly` / `ConcurrentDictionary` / `[ThreadStatic]`
returns only `SymbolStringCache`'s instance fields), `EnclosingSymbolId` walks syntactic ancestors within
the tree only, and assembly attribution is per-compilation (`FactExtractor.cs:62`). Independently
established at `docs/spikes/extraction-granularity-audit.md:315-329`.

Nothing has to be handed anything else. Streaming is a pure lifetime change.

---

## 3. The refactor shape

Smallest shape that gets the release. Everything touched is `internal`; **the public
`SolutionAnalyzer.AnalyzeAsync` signature does not change.**

### Boundary

The new per-project boundary is `ProcessProject` (`SolutionSourceLoader.cs:229-265`): *get compilation →
warm diagnostics → read documents into `SourceModel`s → **extract** → return facts, drop models.* The
loader stays ignorant of extraction by taking a **per-project callback**; `SolutionAnalyzer` keeps
ownership of the `Parallel.For`, the `SymbolStringCache` lifetime, and the rules.

Callback granularity is per **project**, not per file. Per file would put the `SymbolStringCache` lifetime
back in the caller's closure (run-global again) and would serialize a project's files.

### Ordered steps

1. **`RoslynAnalysisModels.cs`** — add `internal sealed record ExtractedSource(string ProjectName, string
   FilePath, SourceExtractionResult Facts);`. Change `SolutionSourceSet` to
   `(IReadOnlyList<SourceFileInfo> SourceFiles, IReadOnlyList<ExtractedSource> ExtractedSources)`. Keep
   `SourceModel` — it becomes a short-lived per-file value. Replace the stale header comment (§1.3) with
   a note that the set is Roslyn-free by construction.
2. **`SolutionSourceLoader.cs`** — `ReadSolutionSourcesAsync` and `LoadAsync` each gain one required
   parameter:
   ```csharp
   // Per-PROJECT extraction sink, invoked while that project's Compilation is alive. Receives the
   // project's SourceModels in the loader's per-file order and returns one result per model,
   // POSITIONALLY. The loader drops every SourceModel the moment this returns, so the callee must not
   // retain one — that retention is exactly what this slice removes.
   Func<IReadOnlyList<SourceModel>, SourceExtractionResult[]> extractProject
   ```
   - `LoadProjectSourcesAsync` (`:1259`) takes it, builds `models` as today (real files `:1305-1327`,
     then generated `:1337-1351`), calls `extractProject(models)`, and returns
     `ProjectSourceLoadResult(sourceFiles, ExtractedSource[])`. **`models` must never escape.**
   - Change `ProjectSourceLoadResult` (`:1604`) to `(IReadOnlyList<SourceFileInfo> SourceFiles,
     IReadOnlyList<ExtractedSource> Extracted)`.
   - `:226` becomes `projectResults.SelectMany(r => r.Extracted).OrderBy(e => e.FilePath,
     StringComparer.OrdinalIgnoreCase).ToList()` — **the sort comparer and key must not change** (§4).
3. **`SolutionAnalyzer.cs`** — `ExtractFromSourceSet` loses the `Parallel.For` (`:171-184`), the `sources`
   local (`:153`), the run-global `symbolCache` (`:166`) and the `extract` phase record (`:186-190`);
   it becomes a pure assemble step over `sourceSet.ExtractedSources` (count pass + concat + XML DI +
   `AnalysisResult`). Add:
   ```csharp
   private static SourceExtractionResult[] ExtractProject(
       IReadOnlyList<SourceModel> models, RuleSet rules, IReadOnlySet<string> diMethodNames,
       StringInterner interner, int? parallelism)
   {
       // PER PROJECT: the keys are strong ISymbol refs (SymbolStringCache.cs:25-27), so a run-global
       // instance pins every Compilation in the solution. Values stay shared via `interner`.
       var symbolCache = new SymbolStringCache(interner);
       var results = new SourceExtractionResult[models.Count];
       Parallel.For(0, models.Count,
           new ParallelOptions { MaxDegreeOfParallelism = parallelism ?? Environment.ProcessorCount },
           i => results[i] = ExtractSource(models[i], rules, symbolCache, diMethodNames));
       return results; // symbolCache unreachable on return
   }
   ```
   All three entry points (`AnalyzeAsync` `:44`, `AnalyzeRetainingWorkspaceAsync` `:84`,
   `ExtractFromSolutionAsync` `:117`) build `diMethodNames` + one `StringInterner` up front and pass
   `models => ExtractProject(models, rules, diMethodNames, interner, parallelism)`.
4. **`src/Rig.Analysis/StringInterner.cs`** — restore verbatim from `git show
   10f81b48:src/Rig.Analysis/StringInterner.cs` (deleted by `9ad3ae1f`). Run-global, thread-safe
   `ConcurrentDictionary<string,string>`. `SymbolStringCache` gains a ctor param and routes the **values**
   of `DocId`, `NamespaceDisplay`, `TypeDisplay`, `Modifiers` through `Intern`/`InternNullable`. This
   keeps the "one shared string instance" peak-memory win the run-global cache used to provide
   (`SymbolStringCache.cs:9-11`) while the *keys* become per-project. Cost: one dictionary entry per
   distinct value (INFERRED ~1-2M entries ≈ 100 MB of overhead, against a multi-GB win); the strings
   themselves are retained by the facts regardless.
5. **`--time` phases.** `extract` no longer exists as a separate wall interval. Rename `:199`
   `timings!.Record("compile+read", …)` to `"compile+read+extract"`; add a 5th `ExtractSec` field to
   `perProjectCompile` (`:179-181`) and surface Σextract in `ReportCompileSummary` (`:726-752`), so the
   diagnostic value of the old row survives. Update the `--time` description string at
   `IndexCommands.cs:111`. Nothing asserts on phase names (`TimingReport` is generic over
   `PhaseTimings.Entries`; the only other hits are sample data baked into `wwwroot/telemetry.html`).

### Signature changes (all `internal`)

| Member | Change |
|---|---|
| `SolutionSourceSet` | `IndexedSources: IReadOnlyList<SourceModel>` → `ExtractedSources: IReadOnlyList<ExtractedSource>` |
| `ProjectSourceLoadResult` | `Sources: IReadOnlyList<SourceModel>` → `Extracted: IReadOnlyList<ExtractedSource>` |
| `SolutionSourceLoader.LoadAsync` | + `Func<IReadOnlyList<SourceModel>, SourceExtractionResult[]> extractProject` |
| `SolutionSourceLoader.ReadSolutionSourcesAsync` | same |
| `SolutionSourceLoader.LoadProjectSourcesAsync` (private) | same |
| `SymbolStringCache` ctor | + `StringInterner` |
| `SolutionAnalyzer.AnalyzeAsync` / `AnalyzeRetainingWorkspaceAsync` / `ExtractFromSolutionAsync` | **unchanged** |

**No public API changes.** `SolutionAnalyzer` is `public`; its three entry points keep their signatures.
`StringInterner` was `public` before deletion — restore it as `internal` unless something needs otherwise.

### Known risk in this shape: nested parallelism

The outer `Parallel.ForEachAsync` over projects (`:189-193`, DOP = `parallelism ?? ProcessorCount`) now
contains a `Parallel.For` over files at the same DOP. `Parallel` queues to the thread pool rather than
creating threads, so total concurrency is pool-bounded, not DOP² (**INFERRED**). Two things to watch in
§5: (a) `compile+read+extract` wall vs the before arm's `compile+read` + `extract` summed; (b) the tail —
the two projects holding 66% of all references (`MedDBase.DataAccessTier` 38.4%, `MedDBase.Pages` 27.5%)
now extract inside their own project task. If wall regresses, the lever is inner
`MaxDegreeOfParallelism = -1` (let the pool schedule) before touching the outer DOP.

---

## 4. Fact-identity risk

Ranked by how plausibly it bites.

### 4.1 Global emit order — the one that matters. Mitigated by construction.

`*FactIndex` is bound as the **list position at write time**: `p[1].Value = i` (`Writes.cs:345`, and
identically `:394`, `:460`, `:505`, `:526`), and it is the PK — `HasKey(new { RunId, SymbolFactIndex })`
etc. (`RigDbContext.cs:90,105,118,126,133`). Nothing queries or orders by it (grep: the only non-INSERT
hits are the entity property and the `HasKey` calls), so it is a dense run-local surrogate. Ordering is
therefore not a *correctness* requirement — but it **is** required for the byte-identical acceptance check,
and one derived value genuinely depends on it:

> `FactGraphProjection.cs:78-79` dedupes methods with `GroupBy(m => m.SymbolId).Select(g => g.First())`,
> and `Reads.cs:438` mirrors it over SQL rows read with **no `ORDER BY`** (so, rowid = insertion order).
> For a DocID emitted more than once, the surviving `MethodRef.FilePath`/`Line` is whichever came first in
> the merged list.

**Mitigation:** keep `OrderBy(FilePath, StringComparer.OrdinalIgnoreCase)` over the flattened set,
unchanged (`SolutionSourceLoader.cs:226`). Today's per-file order within a project is the same
`OrdinalIgnoreCase` document sort (`:1273`), and the sort key is a **verified total order** on the real
target — 12,093 files, 12,093 distinct case-insensitively
(`docs/spikes/extraction-granularity-audit.md:380-386`), so `OrderBy`'s stability never has to break a
tie and the `ConcurrentBag` arrival order (`:174`) is never observable. Result: **the streamed run
reproduces today's index order exactly.**

Latent, pre-existing, and now worth pinning: if a tie ever appeared (the same path linked into two
projects), fact indices would be nondeterministic *today*. The new test in §5.5 turns the invariant into
an assertion.

### 4.2 Cross-project symbol resolution during extraction — no risk

Extraction binds through `compilation.GetSemanticModel(tree)` on a compilation whose in-set dependency
closure is wired as live `ProjectReference`s (`SolutionSourceLoader.cs:1024-1066`). Those dependency
compilations are reachable from the retained `Solution` for the whole run
(`FinalCompilationTrackerState.FinalCompilationWithGeneratedDocuments` is a **strong** `readonly
Compilation` field — decompiled, and independently cited at
`docs/spikes/roslyn-incrementality-findings.md:366-380`). Streaming drops *models*, not the `Solution`,
so no bind that succeeds today can fail after. Recall cannot drop. (This is precisely why §A2 — dropping
dependency `Compilation`s — is a separate, higher-risk slice and is out of scope.)

### 4.3 `SymbolStringCache` cleared between projects — value-neutral

Every memo is a pure function of its key; the soundness argument is already spelled out in the file
header (`SymbolStringCache.cs:6-22`) and for the `OriginalDefinition` keying (`:45-52`). A per-project
instance therefore cannot change any emitted string. Only CPU (recompute per project) and string
identity (restored by step 4) move. **Zero fact-identity risk.**

### 4.4 Run-global DI mining — untouched

`BuildMethodNameSet(rules)` is a pure function of the rules (`DiRegistrationExtractor.cs:14`);
`FindDiRegistrations` takes one `SourceModel` (`:17`); `XmlDiMiner.Mine(rules)` and
`rules.StaticDiMappings` run once after extraction, off disk, Roslyn-free (`SolutionAnalyzer.cs:246-260`).
`DiRegistrationEntity.RegistrationIndex` is a list position over
`diRegistrations.Concat(xml).Concat(static)` (`:260`), so it inherits §4.1's ordering guarantee. No risk.

### 4.5 Source-generated files — the one place to be careful

Generated `SourceModel`s are bound to a **different** compilation (`generatedCompilation`,
`SolutionSourceLoader.cs:1465-1482`) produced by a fresh stateless `CSharpGeneratorDriver.Create` per call
(`:1464`). They must be built and extracted **inside** `LoadProjectSourcesAsync`, appended after the real
files exactly as today (`:1337-1351`), before `models` is dropped. Getting the order wrong here changes
the merged list (§4.1) and, if the generated models were dropped before extraction, would silently lose
the `clientpage_proxy` discriminator facts. `RunSourceGeneratorsAsync`'s blanket `catch → []` (`:1499-1503`)
means such a loss would be **silent** — do not touch that catch in this slice (it is listed as
resident-mode pre-work in the progress doc), but do cover the path with the §5.5 generator test.

### 4.6 `RunId`, assembly registry, `SourceFileInfo` — no change

`SourceFiles` is still flattened and sorted at `:225` with the same comparer; `FileIndex`
(`Writes.cs:655`) is unaffected. `WriteAssemblyRegistryAsync` folds into an order-independent
per-assembly digest.

---

## 5. Before/after measurement protocol

**Do not run this as part of authoring the change.** ~4 min per arm, mutates a real store, and the repo
allows one builder at a time.

### 5.1 Preconditions

- Build + install both arms via `scripts/mini-ci.ps1` (formats, builds, tests, packs, reinstalls global
  `rig`). Capture the BEFORE arm from `main`-equivalent HEAD **before** applying the change.
- `dotnet tool install -g dotnet-gcdump` (used via `RIG_PROFILE_DUMP`).
- Run every command from `c:/git/meddbase-analysis` (rules + store + deployment map come from cwd).
- Stores are commit-scoped and the same commit overwrites the same dir: after each arm, copy
  `.rig/<short-sha>/rig.db` and `rig-index-telemetry.csv` aside (both are overwritten by the next run).

### 5.2 Per-arm commands (identical for BEFORE and AFTER; `$arm` = `before` | `after`)

```pwsh
cd c:/git/meddbase-analysis
$out = "c:/git/coderig/.measure/$arm"; New-Item -ItemType Directory -Force $out | Out-Null
$env:RIG_PROFILE_DUMP = "gcdump"; $env:RIG_PROFILE_DIR = $out

rig index c:/git/meddbase-main-application/MedDBase.slnx --rules rig.rules.json --time *>&1 |
    Tee-Object "$out/index.log"

Remove-Item Env:RIG_PROFILE_DUMP, Env:RIG_PROFILE_DIR
Copy-Item rig-index-telemetry.csv "$out/telemetry.csv"
rig runs                    > "$out/runs.txt"
rig derive --format tsv     > "$out/derive.tsv"
```

Bare full-solution — **no `--from`** (the entry-scoped closure silently drops paket/binary-referenced
projects), no `--restore`, no external MSBuild pre-build. `RIG_PROFILE_DUMP=gcdump` auto-captures at both
instrumented points (`ProfilingPause.cs:26-56`), yielding `extract-peak-roslyn-live.gcdump` (the
co-resident peak, `SolutionAnalyzer.cs:276-280`) and `pre-save-roslyn-unrooted.gcdump`
(`IndexCommands.cs:352-358`).

### 5.3 What to record

| Signal | Where from |
|---|---|
| wall + `peakRAM` per phase | the `--time` breakdown table in `index.log` (`TimingReport.cs:17-43`) — rows `design-time-builds`, `workspace-assembly`, `wire-generators`, `compile+read`(+`extract` in BEFORE / fused in AFTER), `projections+xml-di`, `save`, `graph`, `total` |
| max working set; max managed heap; both restricted to the extract phase | `telemetry.csv` columns `ws_mb`, `heap_mb`, `phase` |
| **live set at the extract peak** + top-5 retained types | `extract-peak-roslyn-live.gcdump` in PerfView / VS / dotMemory |
| live set with Roslyn unrooted | `pre-save-roslyn-unrooted.gcdump` (the fact-array floor; should be ~unchanged) |
| `symbols` / `references` / `di` | `Analysis complete:` line in `index.log` (`SolutionAnalyzer.cs:271-274`) **and** `runs.txt` (`FactCommands.cs:106`) |
| full derived output | `derive.tsv` |

### 5.4 Pass / fail bar

**A. Fact identity — hard gate, must be exact.**
- `symbols`, `references`, `di` identical between arms (both sources agree).
- `diff before/derive.tsv after/derive.tsv` → **empty**. Byte-identical derived output is the practical
  proof that §4.1's ordering held (nothing exposes `*FactIndex` directly).
- `dotnet test` green: 1003 passed / 0 failed / 1 skipped (the pre-existing ClientPage flake).
- `IncrementalExtractionSpikeTests` green with its assertions **unchanged**.
- Any fact-count movement is an automatic FAIL, not a recalibration.

**B. Memory — judge on LIVE SET, not working set.**
- **PASS:** live set at `extract-peak (roslyn live)` drops by **≥ 3 GB** vs the before arm. The 12.1 GB
  peak decomposes as co-resident semantic state + growing fact arrays + graph
  (`docs/spikes/ide-architecture-steals.md:31-36`), with ~9 GB attributed to the semantic component
  (`memory-optimization-strategies.md:16-21,175-179`); the floor after the change should be
  "max single project + fact arrays".
- **Working set is a secondary observation, not the gate.** `memory-optimization-strategies.md:441-445`
  records the measured trap: a forced compacting `GC.Collect` at the extract→save seam left peak working
  set **unchanged (8.3 → 8.3 GB)** because ServerGC/DATAS does not return segments to the OS. A dropped
  live set with a flat working set still satisfies the resident prerequisite (Slice 3 needs the objects
  *collectable*, which is what live set measures).
- **INFERRED** — working set *should* also fall here, unlike the A6 experiment: streaming lowers the live
  set *during* extraction, so DATAS never sizes the heap to 12 GB in the first place, rather than freeing
  after the high-water mark is already set. If it does not, the documented follow-on arm is
  `DOTNET_GCConserveMemory=5` (`memory-optimization-strategies.md:263-277`), measured separately.
- **Decision for the caller:** the progress doc's Slice 2 acceptance says "peak working set … falls
  materially from the 12.1 GB baseline". On the evidence above that gate may fail for a change that is
  fully correct and fully unblocks Slice 3. Recommend restating it as the live-set bar, with working set
  reported.

**C. Wall time.**
- `compile+read+extract` (AFTER) ≤ `compile+read` + `extract` (BEFORE) + noise. Judge only on phase rows;
  `total` is noise-dominated by `compile+read` at ±~8 s run-to-run
  (`memory-optimization-strategies.md:394-397`). A regression beyond that band means the nested-parallelism
  risk (§3) bit — apply the stated lever and re-measure, do not ship it.

### 5.5 New tests (a NEW file, `tests/Rig.Tests/Analysis/StreamedExtractionTests.cs`)

Never `CliApplicationTests.cs`. TUnit on Microsoft.Testing.Platform — run a subset with
`dotnet run --project tests/Rig.Tests --no-build -- --treenode-filter "/*/*/StreamedExtractionTests/*"`
(`dotnet test --filter` prints help and runs zero tests).

1. `Fact_emit_order_is_the_global_path_sort` — over `playgrounds/DeepChain`, assert
   `result.Symbols.Select(s => s.FilePath)` and `result.References.Select(r => r.FilePath)` are
   non-decreasing under `StringComparer.OrdinalIgnoreCase`. This pins §4.1 — the invariant `*FactIndex`
   depends on, currently unasserted anywhere.
2. `Source_set_exposes_no_Roslyn_type` — reflect over `SolutionSourceSet`, `ProjectSourceLoadResult`,
   `SourceExtractionResult`, `ExtractedSource`: no property type (nor generic argument) may come from
   `typeof(Microsoft.CodeAnalysis.SemanticModel).Assembly`. Cheap architectural guard against the
   retention creeping back.
3. `Generated_documents_are_still_extracted` — over `playgrounds/LegacyNet48Web` (the proxy-generator
   playground), assert the generated proxy facts are present (§4.5). **Assert presence only** — this is
   the playground implicated in the long-standing ClientPage flake
   (`docs/backlog/todo/flaky-clientpage-proxy-extraction.md`); do not add a count assertion that the flake
   can trip.
4. `IncrementalExtractionSpikeTests` must pass **with its assertions untouched**. If it needs editing, the
   change has altered extraction semantics — stop and report, do not adjust the test.

Verify every assertion against **real output**: run the installed global `rig` on the playground and write
assertions against the pasted actual output, not against what the DocID "should" look like (the recurring
failure is asserting namespace-qualified names against `ShortName` output).

---

## 6. Interaction with the resident design

**The combination is coherent — retaining the `Solution` while releasing the `SemanticModel`s is exactly
what Roslyn 5.6.0 permits, and they are genuinely different objects.** Independently verified here and
consistent with `docs/spikes/roslyn-incrementality-findings.md:366-386`:

- A retained `Solution` holds each project's finalized `Compilation` **strongly** —
  `FinalCompilationTrackerState.FinalCompilationWithGeneratedDocuments` is a `public readonly Compilation`
  field, plus a lazily-built `RootedSymbolSet` over it (decompiled
  `SolutionCompilationState+RegularCompilationTracker:147-164`). There is **no public API to hold
  compilations weakly**; the only unit of release is the project, and re-realizing one cascades to its
  dependents.
- It does **not** hold `SemanticModel`s: workspace compilations carry a null `SemanticModelProvider`
  (§1.1), and `_binderFactories` are weak. So the ~9 GB of bound-node caches is rig's own retention, and
  Slice 2 is sufficient to remove it without giving up incrementality.
- `Workspace.CurrentSolution` is what roots the trackers, so `AnalyzeRetainingWorkspaceAsync` (`:75-103`)
  already delivers the right object — it hands back the workspace, never the source set.

### What the resident process must hold at steady state

| Held | Why | Slice |
|---|---|---|
| the `RigWorkspace` + `CurrentSolution`: `ProjectState`s, `DocumentState`s (green trees), one finalized `Compilation` per project + its reference manager / declaration table / `RootedSymbolSet` | this *is* the incrementality; no weak-compilation lever exists | 1 / 3 |
| generator `DriverStateTable` per generator-bearing project (`CompilationTrackerGeneratorInfo`) | Roslyn's own retention, not rig's | — |
| base fact store, ~3.5 GB (`WarmStore`) + the derived in-RAM graph | answers queries without a cold load | landed / 3 |
| the per-edit fact overlay | Slice 3 | 3 |
| **NOT** per-file `SemanticModel`s / red roots (R1, R2, R6, R8) | ← removed by this slice | **2** |
| **NOT** a run-global `SymbolStringCache` (R9) | pins every superseded `Compilation` across an edit | **2** |

`SourceText` is already held weakly and spills to temp storage under the default
`PreservationMode.PreserveValue` — so `WithDocumentText` must be passed a `SourceText`, never a
`TextLoader`, and never `PreserveIdentity` (`roslyn-incrementality-findings.md:387-400`).

**The residual open risk this slice does not close:** the permanent floor of every project's
`Compilation`, which today is transient. It is unmeasured, and it is the main open risk in the resident
plan. Slice 2 makes it *measurable* for the first time: index MedDBase, keep the workspace alive with the
extracted set already Roslyn-free, and take a `dotnet-gcdump`. Worth adding as an explicit follow-on
measurement once this lands — before Slice 3's architecture is locked.

---

## Brief summary for the implementing agent

**Owned files (touch nothing else):**
`src/Rig.Analysis/RoslynAnalysisModels.cs`, `src/Rig.Analysis/SolutionAnalyzer.cs`,
`src/Rig.Analysis/Inventory/SolutionSourceLoader.cs`,
`src/Rig.Analysis/Extraction/SymbolStringCache.cs`, `src/Rig.Analysis/StringInterner.cs` (NEW, restore
from `10f81b48`), `src/Rig.Cli/Commands/IndexCommands.cs` (line 111 description string only),
`tests/Rig.Tests/Analysis/StreamedExtractionTests.cs` (NEW).

**Do NOT touch:** `FactExtractor.cs`, `DiRegistrationExtractor.cs`, anything in `Rig.Domain` or
`Rig.Storage`, `CliApplicationTests.cs`, `IncrementalExtractionSpikeTests.cs`, the
`RunSourceGeneratorsAsync` blanket catch, docs other than this file.

**Hard constraints:** no public API change; full suite green before handing back; **do not commit**; do
not run csharpier (mini-ci formats on publish); if an existing test pins behaviour the change breaks,
**flag it, do not edit it**.

**Acceptance (runnable):**
`dotnet test` → 1003 passed / 0 failed / 1 skipped;
`dotnet run --project tests/Rig.Tests --no-build -- --treenode-filter "/*/*/StreamedExtractionTests/*"`
→ all green; `IncrementalExtractionSpikeTests` green with assertions unchanged. The MedDBase
before/after (§5) is the **orchestrator's** validation, not the agent's.

**Gotchas:** TUnit filter syntax above (not `dotnet test --filter`); named args in call sites; new test
file, never the shared one; assert against pasted real `rig` output, not imagined DocIDs; keep the sort
comparer at `SolutionSourceLoader.cs:226` byte-for-byte; extract generated documents **before** dropping
`models`.
