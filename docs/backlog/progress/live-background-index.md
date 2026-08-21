# Live background index — facts current in seconds, not a 4-minute re-index

**Status:** PROGRESS — spike pool dispatched 2026-08-20. The architecture fork is RESOLVED (below); the
remaining gate is whether rig's own extraction is fact-identical over an incrementally-updated compilation.
· **Family:** index performance / architecture · **Supersedes the approach in**
[docs/incremental-indexing.md](../../incremental-indexing.md) (see "What the spike killed")

## Why — the workflow that makes this the target

Agents need rig **right after they finish editing**, which is exactly when the store is stale. Today any edit,
however small, costs a full re-index. So the tool is least useful at the moment it is most wanted.

Measured on MedDBase (227 projects) 2026-08-20:

| | cost | paid |
|---|---|---|
| full index, cold | **253s** (after `--restore` opt-in, `eb6480ff`; was 524s) | per commit — and per EDIT today |
| dtb phase | ~50s of that | " |
| rig's own pipeline (compile+read / extract / save / graph) | ~150s of that | " |
| query, warm disk cache | 5.5s, of which **4.6s was graph load** | per question (fixed today, see below) |

**SLO for this program:** after a single-file edit, facts are current in **seconds**; a full re-index happens
only when a `.csproj` or an imported `.props`/`.targets` changes.

## Already landed today (query side — the bonus, not the target)

`WarmStore` + `WarmStoreWatcher`: a process-lifetime in-memory memo for the two whole-store loads (shaped
graph, invocation refs), keyed on the SAME axes as the disk cache (rig.db size+mtime, rules fingerprint) so a
stale entry is unservable without any watcher in the correctness path. Wired into `serve`, `derive`,
`HazardsService`.

Measured, `/api/hazards` on MedDBase: **25.0s per repeat request → 8.5s**, cold prewarm 18s moved off the
request path. Fresh seeds hit too. Directly addresses
[cross-method-hazards-cache](../todo/cross-method-hazards-cache.md).

Trap worth recording: the first cut used an LRU capacity of 1 while caching TWO artifact kinds, so graph and
invocations evicted each other and **every request missed**. Capacity must be ≥ (artifact kinds × stores
resident).

## What the spike killed — the metadata partition

`docs/incremental-indexing.md` §"Execution model" specifies: rebuild stale projects as source, load unchanged
projects **as metadata DLLs from their on-disk bins**. Tested 2026-08-20 on `playgrounds/DeepChain` (7
projects, 103 lines, purpose-built for cross-project binding hazards) via a new `rig index --from X
--no-closure` flag that collapses the source set to one project:

| arm | source set | result |
|---|---|---|
| oracle | all 7 as source | 34 symbols, **42 references**, all cross-project bindings resolved |
| doc's model | `Business` alone, deps as metadata | 5 symbols, **2 references**, 43 errors, first is `System.Object` is not defined |

**Root cause is not the duplicate-assembly-identity hazard the loader comments warn about.** It is simpler:
`bin/` is EMPTY for all 7 projects, because rig's design-time builds run the `Compile` target and never emit
output. That is exactly what makes indexing fast, and what `--restore` doubled down on.

So the doc's execution model has an unstated precondition — built output DLLs for every unchanged project —
that **does not hold in rig's own flow, by design**. The two strategies are in direct tension:

- (a) build unchanged projects → re-adds the cost `--restore` just removed. Rejected on its own terms.
- (b) emit metadata-only skeletons ourselves → reimplementing a compiler feature.
- (c) **keep dependencies as live compilations in a resident workspace** → no DLLs needed. ← the shape we are
  pursuing. (An earlier draft of this item also claimed Roslyn's skeleton-reference machinery removes the need
  for a hand-rolled public-surface cascade. That was WRONG and is struck — see "CORRECTED" below. Fork (c)
  survives on the no-DLLs merit alone.)

## The tier split (the frame)

rig has two questions with opposite requirements, and one mechanism serving both today:

| | "what did I just break" | "how does HEAD differ from base" |
|---|---|---|
| about | the working tree | two commits, one not checked out |
| needs | freshness | two materialized states |
| durability | **none** — the tree is the source of truth | **required** |
| share of agent traffic | almost all | occasional (`impact`, MR review) |

The cold commit-scoped store currently answers both, which is why the first one is bad: it pays the second
one's durability bill (35s save, 3.1GB write, then a graph load per question) to answer something that never
needed to be durable. Tier 2 (commit-scoped SQLite) stays exactly as it is — `impact` requires it.

Supporting structural fact: **the in-memory engine is already the reference implementation.**
`FactPathFinder`/`ShapeGraph` operate on in-memory `FactGraphData`; the SQL recursive-CTE path is an
optimization with an equivalence test (`Bounded_graph_reproduces_full_graph_reach`). That SQL path exists
*because the process dies* — it is load-shedding for cold starts.

## Open questions — spike pool dispatched 2026-08-20

Five parallel workstreams. The first is the GATE; the other four are shape-independent groundwork that holds
regardless of its verdict.

1. **GATE — incremental extraction equivalence.** Does re-extracting one project over an incrementally-updated
   Roslyn solution produce facts identical to a cold full index of the same tree? Protocol: retain the
   `AdhocWorkspace`, `Solution.WithDocumentText`, re-extract, diff against a cold index of the mutated tree, on
   DeepChain. A negative result is a valid outcome and redirects to what must change in extraction.
2. **Roslyn incrementality mechanics.** Does `AdhocWorkspace` support the path (it is not `MSBuildWorkspace` —
   deliberately, per `SolutionSourceLoader.cs:70-77`)? Do skeleton references work there, and when do they
   regenerate? Do source generators re-run? What happens on a `.csproj` change? Retained-Solution memory levers.
3. **IDE/LSP prior art, mapped to rig.** salsa firewall queries vs rig's `QueryCacheKeys` `*Schema` constants;
   clangd's background-index + staleness marker; LSP document versioning and its push→pull diagnostics move;
   SCIP interop. Actionable mapping only.
4. **Extraction granularity audit.** What is whole-solution-coupled and would corrupt a per-project
   re-extract: `RunId` semantics, `ReferenceFactIndex` ordering, the deliberate `GetDiagnostics` binding
   warm-up (~289s CPU/index), DI registrations, generators, cross-project extractor state.
5. **Fact-store patch path audit.** Can a store be patched in place at all (publish is atomic-rename)? Is
   project row ownership even expressible? Cost of FTS5 + 8 fact index rebuilds; whether a partial graph
   re-bake is SOUND at all given whole-program CHA dispatch; and the `StoreKey` mtime problem — an in-place
   patch busts every cached artifact store-wide. Recommendation: patch-in-place vs in-memory overlay.

## Pool results (all five reported 2026-08-20) — the determined shape

**GATE PASSED.** Incremental re-extraction over a retained workspace is **fact-identical** to a cold
full-solution index. Verified independently of the agent: full suite 1003 passed / 0 failed / 1 skipped
(pre-existing ClientPage flake), and a DeepChain re-index with the spike build reproduced the pre-spike
oracle exactly (34 sym / 42 ref / 4 call edges / 2 dispatch edges). Spike test asserts on `BodyHash` +
`EndLine`, runs the incremental arm BEFORE the disk write (so facts can only come from
`WithDocumentText`), and carries three anti-vacuity guards.

**The shape:** resident process. Base = the immutable commit store loaded once (3.5 GB — `WarmStore`).
Overlay = facts for dirty files, re-extracted **per FILE** over a retained Roslyn `Solution`. Derived
graph rebuilt in RAM. Never write to the store on the hot path; snapshot to a new commit-scoped store
on demand.

Why per-FILE and not per-project: **two projects hold 66% of the 2.44M references**
(`MedDBase.DataAccessTier` 38.4%, `MedDBase.Pages` 27.5%) and they are exactly where edits land — so
"re-extract the changed project" is re-extracting a third of the codebase. Per-file is viable because
`FactExtractor.Extract` is a **pure function of one file** (audited: no static mutable state in
`Extraction/`; `EnclosingSymbolId` walks only syntactic ancestors within the tree).

**Why the overlay matters:** the two showstoppers both live in the WRITE/BAKE tail — (1) run-scoped
append with run-blind reads (`Reads.cs:103` "all runs, no latest-run concept") doubles every effect, and
(2) the index tail re-bakes derived views from the whole-solution in-memory result. An overlay never
writes, so both are **avoided rather than solved**, and neither requires touching `FactExtractor`.

### CORRECTED: Roslyn does NOT give us the surface-hash cascade

Roslyn is pinned at **5.6.0** (the comment at `SolutionSourceLoader.cs:74` claiming 5.3.0 is stale).
Skeleton references (`GetOrBuildSkeletonReferenceAsync`) are **cross-language ONLY** — same-language
project references take `compilation.ToMetadataReference()` on the live compilation. rig hard-codes
`language: LanguageNames.CSharp` for every project (`SolutionSourceLoader.cs:871`), so the skeleton path
**never executes**. Any plan that assumed Roslyn provides public-surface invalidation is wrong.

Consequence, and it INVERTS the earlier reading of this repo's own design doc: Roslyn's invalidation
cascade is dependency-shaped and **surface-blind**, so the `public-API surface hash + cascade` gate in
[incremental-indexing.md](../../incremental-indexing.md) is a **genuine win Roslyn will not give us**, not
a redundant reinvention. `symbol_facts.BodyHash` shows the machinery is half-built already.

### The measured constraint that sizes the work

Transitive-dependent distribution over 187 in-source assemblies (1,572 edges, lower bound on the
ProjectReference graph): median **6**, mean 24, p90 **68**, max **164**. 47% of projects have <=5
transitive dependents; **23% have 51+**.

So plain Roslyn incrementality hits the SLO for the COMMON edit. A **hub** edit (`Echo.Process`,
`MedDBase.NewTypes`, `MMS.CommonInterfaces`, `MMS.Standard`) cascades to a near-full re-extract. That is
precisely where the surface-hash gate earns its keep: a hub edit that changes only a method BODY must not
cascade at all. The gate is not a nice-to-have — it is what makes hub edits survivable. Fallback for a
genuine surface change: an explicit "this will take a while" path.

### Prerequisites promoted by the pool

- **Stop retaining every `SemanticModel`** (`memory-optimization-strategies.md` A1) moves from
  optimization to **prerequisite**: `SolutionSourceSet.IndexedSources` pins a `SemanticModel` + red root
  per file per project. Today that dies with the process; resident, it does not. This also resolves the
  apparent conflict between "live workspace" and "release eagerly": retain the **Solution** (project graph
  + trees), release the **SemanticModels** (the ~9 GB of bound-node caches). Different objects.
- **Use `OnDocumentTextChanged`, not `TryApplyChanges`** (which diffs the whole solution and discards the
  fork), and pass `SourceText`, not `TextLoader` — that flag alone selects incremental vs full reparse.
- `AdhocWorkspace` is `sealed`; a small `RigWorkspace : Workspace` is needed for both the text-change path
  and `OnProjectReloaded` (the `.csproj`-change path).
- **Two silent-failure hazards must be fixed before going resident**: `EmitCompilationToTempAsync`'s
  silent `null` and `RunSourceGeneratorsAsync`'s blanket `catch -> []` turn a one-index flake into a
  process-lifetime one. Also never `RemoveProject` a referenced project — the dangling reference is
  dropped silently, the same recall-loss class as the `--no-closure` finding.
- Source generators: rig bypasses Roslyn's incremental generator path with a fresh stateless
  `CSharpGeneratorDriver.Create` per call, so generators run **twice per project** with zero
  incrementality; the ClientPage generator is a v1 `ISourceGenerator`, which caps the ceiling anyway.

### Lead on the long-standing ClientPage flake (INFERRED, not reproduced)

The generator opens with `GetTypeByMetadataName("MMS.Web.UI.ClientPage"); if (null) return;`. That API
returns null when the name is **ambiguous across two referenced assemblies** — the dual-identity condition
that flips with whether a playground `bin/` was populated by a concurrent build. Matches the signature
exactly (fails in the full suite, passes in isolation). Cheap confirmation: log
`GetTypesByMetadataName(...).Length` in a failing run; `>1` proves it. See
[flaky-clientpage-proxy-extraction](../todo/flaky-clientpage-proxy-extraction.md).

## Pre-existing bugs surfaced as by-products (none caused by this work)

1. **`/api/meta` `DerivationVersion` carries NO store identity** (`RigApiEndpoints.cs:336-343`) — it is
   `hash(schemaToken + rulesHash)` only, and the client keys on the store DIRECTORY NAME, which is stable
   for a given working state. So re-indexing the same commit leaves **every browser serving stale
   trees/hazards/impact indefinitely**. Live bug today; a live index makes it permanent. Fix is small:
   fold `StoreKey` into `DerivationVersion`.
2. **`--merge` pays a full global 8-index rebuild** — the drop at `Writes.cs:313` is unconditional despite
   the comment at `:290-294` claiming the append path keeps its indexes, and that comment references a
   `fastBulkWrite` parameter no longer in the signature.
3. FTS5 trigram rebuild is drop-and-recreate: **12.5 s / 420 MB**, ~44% of the graph phase, no incremental
   path (`GraphMaterializer.cs:198-251`).
4. `ReferenceFactIndex` is a global per-run dense sequence over a FilePath sort. Measured on the real
   store: 224 projects across **226 contiguous blocks** — near-perfectly contiguous, so this is NOT the
   corruption risk it first appeared. But contiguity is an accident of path sorting (`MedDBase.Pages` has
   no common source root), so nothing may depend on it.

## DECISION 2026-08-20 — the coarse-vs-gated binary is premature; make it a policy swap

The slice-4 design (`docs/spikes/slice4-surface-hash-gate-design.md`) argues coarse invalidation is NOT good
enough and the surface-hash gate must ship as one indivisible slice. Its measurements over 564 real `.cs`
edits are the best data we have, and two of them correct this document:

- **The median edit is not cheap under coarse.** "median cascade 6 of 187 assemblies" counts PROJECTS; those 6
  projects are a median **3,366 files — 27% of the codebase** — because **79.1%** of cascades pull in
  `MedDBase.Pages` (2,595 files) or `DataAccessTier` (2,475). Gated: median **1** file.
- **The hub rationale was the weaker half.** Only 8.7% of edits land in a 51+-dependent assembly and only
  26.5% of those are body-only, so the gate erases a 50+-project cascade for just **13 of 564 edits (2.3%)**.
  The gate's value is the MEDIAN, not the tail. This document previously said the opposite.

Also load-bearing, and a correction to EVERY cascade measurement in this program including my own: the
dependents graph derived from `reference_facts` is a **lower bound** (218 assemblies / 1,846 edges) — a csproj
reference with no observed use produces no edge. The real graph is MSBuild's `ProjectReferences`.

### Why neither branch is the answer yet

Both branches assume **file count is the cost driver**. It is not, and nothing in the program has measured the
thing that is. From the cold-index telemetry: the whole `extract` phase is **36.6s for all ~12,369 files**
(~3ms/file), while `compile+read` is 48.4s wall / 646s CPU for 224 projects (~2.9s CPU per project). So the
cost of a cascade is dominated by **how many project COMPILATIONS must re-bind**, not by how many files are
re-extracted. A 3,366-file coarse cascade may well be ~10-15s of mostly-parallel work — slower than gated, but
nowhere near the "quarter of the codebase" framing implies, and possibly inside budget for a BACKGROUND task.

Nobody has converted the file counts into seconds. The resident-workspace trial
(`tests/Rig.Tests/Analysis/ResidentWorkspaceTrial.cs`) measures exactly that: warm-workspace re-extract of the
whole solution, from which per-project re-bind and per-file extract costs both fall out.

### The decision

1. **Build slice 3 with a PLUGGABLE dirty-set policy** — an interface taking changed file paths and returning
   the documents to re-extract. Coarse (project + transitive dependents, over the MSBuild reference graph) is
   the first implementation; the surface-hash gate becomes a second implementation of the same interface. The
   coarse-vs-gated choice becomes a **policy swap decided by measurement**, not an architectural fork decided
   by argument. This costs one interface.
2. **Converging overlay + disclosure, so cascade latency never blocks an answer.** On an edit: re-extract the
   edited file(s) IMMEDIATELY (correct by construction — the retained Solution holds every dependency as live
   source, so the edited file's own binding is fully resolved), serve queries at once while DISCLOSING which
   projects are not yet reconciled, and run the cascade in the background until the disclosure clears. This
   converts cascade cost from a latency problem into a disclosure problem, which is what rig already does for
   `~heuristic` dispatch and the "NOT a real call" fan-out. It is also why slice 5 is load-bearing architecture
   rather than a nicety.
3. **Decide the default policy on the trial numbers**, then ship the surface hash as the optimization that
   shrinks background work and removes the disclosure window — with the four widenings the design identifies
   (declaration-text hash, MSBuild reference graph, `IsIterator`, assembly attributes) and `--verify-cascade-gate`.

Note the design's own data supports the converging shape: **59.4% of file-edits are body-only**, so in the
majority of cases the background cascade will find nothing changed — the disclosure window is usually a false
alarm, which is exactly the cheap-to-tolerate failure mode, and exactly what the surface hash later removes.

## MEASURED 2026-08-20 — the resident-workspace trial on MedDBase (226 projects)

`tests/Rig.Tests/Analysis/ResidentWorkspaceTrial.cs`, warm dtb cache, `excludeTests: true`, one trivia-only
edit, both arms in ONE process:

| arm | wall | facts | working set |
|---|---|---|---|
| 1 — cold load + extract, retaining the workspace | **245.0s** | 442,619 sym / 2,427,005 ref / 18,026 rel / 30,069 disp | 10.70 GB (managed 8.33) |
| 2 — one edit, whole-solution re-extract over the WARM workspace | **157.4s** | identical, all four counts | **17.38 GB** |
| | **87.6s saved (1.6x)** | fact counts IDENTICAL (trivia property holds) | **+6.7 GB** |

245.0s corroborates the 253s `rig index` baseline, so the arms are comparable.

### Three conclusions, one of them a re-prioritisation

1. **A resident workspace ALONE is worth only 1.6x.** It deletes the design-time builds (~50s) and workspace
   assembly (~14s) and nothing else — 87.6s of a 245s budget, close to the sum of those phases. This is the
   ENABLER, not the win. Anyone hoping "keep it resident" is the answer should read this row.
2. **Per-file scoping (slice 3) is therefore the whole game.** 157.4s is the number slice 3 has to beat, and it
   should beat it by orders of magnitude: whole-solution re-extract is 12.7ms/file across 12,369 files
   *including re-binding all 226 compilations*, so the per-project re-bind dominates and a one-file edit should
   touch one compilation.
3. **SLICE 2 IS PROMOTED — it is now a blocker, not an optimisation.** Working set went 10.70 GB -> **17.38 GB**
   across a single re-extract and did not come back. That is `IndexedSources` retaining a `SemanticModel` per
   file per project for TWO generations at once, plus the workspace holding compilations for both solution
   snapshots. A long-lived process doing repeated edits GROWS by ~6.7 GB per generation. On this 64 GB box that
   is roughly two edits before trouble. "Release the SemanticModels" is the difference between a resident
   process that survives a work session and one that OOMs — so it lands BEFORE the surface-hash gate (slice 4),
   whose value is throughput rather than survival.

Note B2's warning about measuring this: working set is a ServerGC/DATAS artifact as much as a live-set signal.
But for a RESIDENT process the OS-level footprint is exactly what constrains co-residency, so the +6.7 GB is
the number that matters here regardless of how much of it is uncompacted heap.

### Harness caveat found in the same run

The edit-target auto-pick chose `src/components/obj/Debug/netstandard2.0/components.AssemblyInfo.cs` — a
GENERATED file under `obj/`, because the heuristic picks the project with the fewest documents. Harmless for a
whole-solution timing, wrong for per-file measurement. Fixed: `obj/`/`bin/` paths are now excluded from the
auto-pick.

## RESULT 2026-08-21 — the resident live index WORKS. Measured on MedDBase (226 projects).

```
edit -> servable        0.75s   (0.02s re-extract + 0.74s merge)
cold index            258.4s
whole-solution warm   201.9s
cascade reconcile     150.3s   (was 476s — batched, now BETTER than a cold index)

facts vs cold, compared as SETS:
  type relations   missing=0  extra=0
  dispatch         missing=0  extra=0
  DI registrations missing=0  extra=0   (204, on the store that HAS xml descriptors)

working set   10.79 GB at boot -> 19.11 GB after one edit + full reconcile
```

**~340x faster than a cold index for a single-file edit, with facts exactly equal to a cold index.** That is
the program's thesis, measured rather than argued.

`rig watch <solution>` is the host. Real output:

```
watch: cold boot in 1.3s — 7 project(s), workspace retained
live: facts current as of 0 file(s) applied | all projects reconciled
watch: watching for .cs saves (obj/ and bin/ excluded) — press Ctrl+C to stop.
live: facts current as of 1 file(s) applied | 3 project(s) unreconciled | last edit 0.07s
live: facts current as of 1 file(s) applied | all projects reconciled | last edit 0.07s | reconcile 0.01s
```

### What shipped

| slice | state |
|---|---|
| 1 — `RigWorkspace` + incremental text-change path | DONE (`14af4594`) |
| 2 — stream extraction, release `SemanticModel`s per project | DONE (`7721b37a`) — live set -1.26 GB, working set unmoved |
| 3 — `ResidentIndex` converging overlay + pluggable dirty-set policy | DONE (`7e6531de`, `ff936c3a`) |
| 5 — staleness disclosure | DONE, as the `rig watch` status line |
| host + batched reconcile | DONE (`034b3d9d`) |
| 4 — surface-hash gate | NOT BUILT — designed, `docs/spikes/slice4-surface-hash-gate-design.md` |

### THREE MEASUREMENT LESSONS — these cost more time than any code defect

Every wrong conclusion in this program came from the instrument, never the code. Recorded so the next person
does not repeat them:

1. **Console output is not a measurement.** TUnit does not surface `Console.WriteLine` in its default output
   mode; an 8-minute MedDBase run produced a duration and zero numbers. Measurements write to a FILE, appended
   as they go.
2. **Whole-solution re-extract is not a proxy for a per-file edit.** It holds two complete fact sets alive
   (~2.4M refs each) so its memory growth is a harness artefact, and it re-binds all 226 compilations so its
   time is an upper bound nothing in the resident path pays. Arms 1-2 measured 1.3x and looked like failure;
   arm 3 measured the real path at 340x.
3. **Counts are not sets.** The cold store holds 30,069 dispatch rows but only 22,876 DISTINCT ones — the same
   edge is emitted from several files (partial types, shared bases). Comparing counts produced a confident
   "24.5% of dispatch facts are LOST" that was pure duplication. Only a symmetric difference distinguishes a
   real loss from base-internal duplication. The harness now compares sets permanently.

A fourth, about GATES rather than instruments: **`playgrounds/DeepChain` was repeatedly too small to host the
defect class under test.** It had 2 dispatch facts and 2 type relations, so the equivalence gate could not see
a dispatch bug; it has no XML DI descriptors, so no gate could see a DI regression from the `XmlDiMiner`
bypass. Both were caught only by measuring MedDBase. DeepChain has since been given cross-project impls, an
override chain, an inherited implementation and delegate binds (oracle 34 -> 51 symbols, 2 -> 8 dispatch).

### Open, ranked

1. **Query serving from the live index.** `rig watch` maintains correct live facts that nothing can yet query.
   This is the gap between "the loop works" and "the tool is useful", and it is the next slice.
2. **Memory growth.** 10.79 -> 19.11 GB across one edit + reconcile. Slice 2 released the live set but working
   set does not return (ServerGC/DATAS keeps segments). A long session needs either a GC policy decision or a
   periodic re-boot. Unresolved and the biggest risk to a genuinely long-lived process.
3. **Slice 4 (surface-hash gate).** Reconcile is 150.3s; **59.4% of real edits are body-only** and would need
   NO cascade at all under the gate. This is where the remaining order of magnitude is.
4. **Emitter `FilePath` on `DispatchFact`/`TypeRelationFact`** — kills the deliberate ghost window in the merge.
   Write-side schema change, needs a re-index.
5. **New files** are not in the retained workspace (no add-document path); `rig watch` discloses rather than
   silently skipping. **Generated documents** are not re-extracted per file (the generator pass is skipped), so
   generator-emitted facts stay at base until a cold boot.
6. **23.9% of `dispatch_edges` rows in the cold store are duplicates** (7,193 of 30,069), plus 7.8% of type
   relations. Pure storage waste, and it inflates every count read off those tables. Unrelated to this program
   but found by it.

## PLAN — sequenced build slices

Each slice is independently useful, has its own acceptance check, and does not depend on the next one landing.
Slices 1 and 2 are prerequisites for 3 and are worth having regardless.

### Slice 1 — `RigWorkspace : Workspace` + the text-change path
`AdhocWorkspace` is `sealed`, so a minimal subclass is required for both `OnDocumentTextChanged` and (later)
`OnProjectReloaded`. Use `OnDocumentTextChanged`, NOT `TryApplyChanges` (which diffs the whole solution and
discards the fork), and pass `SourceText`, not `TextLoader` — that flag alone selects incremental vs full
reparse.
**Acceptance:** the shipped spike test (`IncrementalExtractionSpikeTests`) passes against `RigWorkspace`
instead of the raw retained `AdhocWorkspace`, unchanged in its assertions.

### Slice 2 — stop retaining every `SemanticModel`
`SolutionSourceSet.IndexedSources` pins a `SemanticModel` + red root per file per project; that is the ~9 GB
peak (`memory-optimization-strategies.md` A1). Today it dies with the process; resident, it does not. Stream
per project: get compilation -> extract -> emit facts -> drop that project's models.
**Acceptance:** peak working set on a full MedDBase index falls materially from the 12.1 GB baseline, and
`rig index` output is fact-identical (symbol/reference/edge counts unchanged).

### Slice 3 — per-file re-extraction into an in-memory overlay
Base = immutable commit store (already resident via `WarmStore`). Overlay = facts for dirty files only.
Derived graph rebuilt in RAM; `dispatch_edges` rebuilt WHOLE, never partially (whole-program CHA, and it is
the sound superset bounding the SQL walk). Nothing written to the store on the hot path.
**Acceptance:** edit one file in a real checkout, ask a query, and the answer reflects the edit in seconds —
while a cold full index of the same tree returns the SAME answer (the equivalence gate, extended from the
spike to the overlay).

### Slice 4 — the surface-hash gate (what makes hub edits survivable)
Roslyn's cascade is dependency-shaped and surface-BLIND, so it re-binds all transitive dependents on any edit:
median 6, but p90 68 and max 164, and 23% of projects have 51+. Gate on public surface — a body-only edit must
not cascade at all. `symbol_facts.BodyHash` is half of the machinery already.
**Acceptance:** a body-only edit to a hub project (`MMS.Standard` / `NewTypes` / `CommonInterfaces`)
re-extracts only the edited file; a signature change to the same file cascades to its dependents. Both arms
fact-identical to a cold index.

### Slice 5 — staleness disclosure
Every resident-mode answer states which files are dirty relative to the indexed commit (cheap:
`git diff --name-only`). Silently answering about pre-edit code is the failure this program exists to remove,
and disclosure is already rig's identity (`~heuristic` dispatch, "NOT a real call" fan-out).
**Acceptance:** an answer computed over a dirty tree names the dirty files; a clean tree adds no noise.

### Pre-work that must land before ANY resident slice ships
Two silent-failure paths turn a one-index flake into a process-lifetime one:
`EmitCompilationToTempAsync`'s silent `null` and `RunSourceGeneratorsAsync`'s blanket `catch -> []`. Also never
`RemoveProject` a referenced project — the dangling reference is dropped silently
(`FinalizeCompilationWorkerAsync`), the same recall-loss class as the `--no-closure` finding.

### Explicitly OUT of scope
- Writing partial derived tables to disk (unsound for `dispatch_edges`; ~20-30 s of unavoidable FTS5 + index
  rebuild per patch anyway).
- Making the commit-scoped store mutable — `impact` is documented as a pure function of two IMMUTABLE stores.
- Keystroke-level `didChange` buffering: agents write whole files between tool calls, so `didSave` granularity
  is the right unit.
- Converging the fact store on LSIF/SCIP (no vocabulary for effects/hazards/dispatch-basis). A one-way
  exporter is an unrelated follow-on.

## Constraints any design must respect

- The two-stage split (Roslyn extraction / Roslyn-free derivation) is load-bearing and not up for
  renegotiation.
- `rig impact` diffs two commit-scoped stores, so durable snapshots cannot go away.
- One agent builds at a time in this repo — concurrent builds clobber `bin/`.
- Whatever ships must disclose which tree state produced an answer. Silently answering about pre-edit code is
  the failure mode this whole program exists to remove, and rig's identity is disclosing its own limits.

## DESIGN 2026-08-21 — query serving: the seam is the fact source, not the transport

The live index maintains correct facts that nothing can query. Closing that is three slices, and the shape of
the first one is already fixed by a precedent in this repo.

### The seam already exists, half-built

`FactGraphProjection.FromAnalysis` is a PRODUCTION in-memory twin of `Reads.LoadFactGraphAsync` — `rig index`
uses it to build the graph from facts still in RAM instead of re-reading 3.8 GB back off disk. Its header says
the two projections must stay field-for-field identical and names the parity test that enforces it. That is
exactly the contract the live index needs, extended from one loader to the query-side loader set:
`LoadShapedGraphAsync`, `LoadFactEntryPointDataAsync`, `LoadInvocationRefsAsync`, the static-field/threadStatic/
volatile/async feeds, delivery sites, event-subscription sites.

So slice 6a is not new architecture — it is completing an existing pattern. `LiveReads` (pure, over
`AnalysisResult`) + `LiveFactSource` (the memoized per-generation bundle: shaped graph, EP data, hazard
effects) + a set-equality parity gate against a store written from the same `AnalysisResult`.

### Why NOT the two obvious alternatives

- **Materialize the live facts into a store so every command works unchanged.** This is the tempting one and it
  is dead on the numbers: the save tail is 35 s / 3.1 GB on MedDBase plus a 12.5 s FTS5 rebuild, against a
  0.75 s edit→servable budget. In-memory SQLite halves neither the time nor the memory, and a `mode=memory`
  database is not reachable from the one-shot CLI process anyway — which is the entire use case. Also re-opens
  both showstoppers the overlay AVOIDS (run-scoped append double-counting, whole-solution re-bake).
- **Return typed JSON artifacts and render client-side.** Duplicates every renderer, or forces the renderers
  host-side regardless. The transport should carry rendered output; see 6c.

### The three slices

**6a — `LiveReads` + `LiveFactSource` + parity gate.** In flight. Nothing consumes it yet; the gate is the
deliverable. Delivery-site projection (~200 lines of real logic) is extracted to a shared pure core rather than
duplicated — the other twins are short enough that duplication with a parity test is the idiomatic trade here.

**6b — `IFactSource`, and migrate the queries onto it.** Two implementations: `StoreFactSource` (delegating to
`Reads`, keeping the `WarmStore` memoization) and `LiveFactSource`. Commands move from taking a `RigDbContext`
to taking an `IFactSource`, one at a time, each migration verified by its EXISTING tests passing with
byte-identical output. `reaches` first (highest-value agent query), then `tree`, `callers`, `path`, `derive`.

This refactor is worth doing even if the resident process were cancelled tomorrow: it makes a query command
testable without a store, which today it is not.

The live surface will be a disclosed SUBSET. `rig impact` is explicitly never live — it is defined as a pure
function of two IMMUTABLE stores, which is the tier-2 half of the split this program opened with. Query-cache
entries do not apply on the live path either: facts change per edit, so memoization is per-generation inside
`LiveFactSource` rather than in `cache.db`.

**6c — transport: the resident host answers one-shot CLI invocations.** The host binds 127.0.0.1 only, with a
token, and publishes `{pid, port, token, solution, workingDir}` to `.rig/live.json`. The protocol carries the
COMMAND, and the host runs it in-process against `LiveFactSource`, returning stdout/stderr/exit — so
`--format tsv` and every rendering flag work for free and the output is byte-identical to the store path.

**Routing default: ON when a live host is running for this working directory, always disclosed, `--no-live` to
opt out.** The reflex is to make this opt-in because "silently answering from another process" sounds unsafe.
That has it backwards: when a live host is running, the STORE is the stale answer — it is pinned to an older
commit, while the live index reflects the tree as it is now. Routing to live is strictly more correct, and the
failure this whole program exists to remove is silently answering about pre-edit code. So the honest default is
live-when-available plus a mandatory source line (`live: facts from resident index — 1 file applied,
3 project(s) unreconciled`), not a flag agents will never pass.

### The risk this design exposes: 0.75s is FACTS, not ANSWERS

`edit -> servable 0.75s` measures the fact overlay. A query needs the DERIVED layer on top — shaped graph
(monomorphization over 442k symbols), EP data, hazard effects. Cold from SQL those cost 4.6 s (graph) and ~18 s
(hazard prewarm) on MedDBase. From memory they should be cheaper, but **nobody has measured them, and a
memoized-lazy bundle means the FIRST query after each edit pays the whole rebuild.** If that is 15 s, the
program's headline number is honest about facts and misleading about answers.

Three responses, in the order they should be tried:

1. **Measure it first** — per-generation `LiveFactSource` build cost on MedDBase, broken down by artifact. This
   is the calibration gate for 6b and it must run before the default routing of 6c ships.
2. **Rebuild the derived layer eagerly in the background** after the eager apply, exactly as the cascade already
   does — so a query arriving later hits a warm bundle and only a query racing the edit pays. Cheap, and the
   converging-overlay pattern already in place.
3. **Incremental derivation** if (2) is not enough. Effects are per-method facts keyed to an enclosing symbol,
   so re-deriving only the changed files' symbols and splicing is tractable; the graph is a filter+map over refs
   and could be spliced per file too. `dispatch_edges` must still be rebuilt WHOLE (whole-program CHA) — the
   same constraint slice 3 already respects.

## RESULT 2026-08-21 (later) — `reaches` is served from the live index, byte-identical to the store

Slice 6a (`afe3b308`) built the projection layer; 6b made it answer.

```
LiveReads          11 pure twins of the query-side Reads loaders, over an AnalysisResult
LiveFactSource     per-generation memo: traversalGraph | epData | invocations | throwRefs | effects
                                        + shapedGraph / hazardEffects for the derive-shaped consumers
IQueryFactSource   6 members — the seam ReachesCommand now takes instead of a RigDbContext
                   StoreQueryFactSource (delegates to today's code) | LiveQueryFactSource
rig watch --query  "reaches <pattern>", plus a stdin query loop
```

**Measured, live vs store, same tree, real CLI on the store side: stdout byte-identical on all 14 patterns**
across DeepChain and EntryPointEffects, exit codes equal, deployment/EP chips equal. Parity of the projections
themselves: 52 set comparisons, `missing=0 extra=0` on every one.

And the claim the program exists to make, as a test rather than an assertion —
`Reaches_reflects_a_disk_edit_the_pre_edit_answer_did_not`:

```
BEFORE  live: facts current as of 0 file(s) applied | all projects reconciled
        Direct effects (real call paths): 2      1 efcore pending_write   1 efcore commit
AFTER   live: facts current as of 1 file(s) applied | 1 project(s) unreconciled | last edit 0.16s
        Direct effects (real call paths): 3      1 efcore read  1 efcore pending_write  1 efcore commit
```

The pre-edit answer is asserted NOT to contain the new effect, so the test cannot pass against a source that
always reported it.

### Three findings from the comparison, none of them predicted

1. **`ShapedGraph` is the WRONG graph for `reaches`, and serving off it would have diverged.**
   `LoadShapedGraphAsync` (what `derive` uses) additionally runs `AddDeliveryEdges`, which CREATES
   producer→handler edges; the traversal loader stops after `ShapeGraph`. Answering `reaches` off the
   derive-shaped graph would have added delivery reach the store path never walks — a live/store divergence
   with nothing to do with liveness. Hence a separate `TraversalGraph` artifact. This is the second time in
   this program that two nearly-identical loaders differed in a load-bearing way; the lesson is to mirror the
   loader the CONSUMER uses, never the one with the most similar name.
2. **A real pre-existing bug: the `--intrinsic` hint is counted BEFORE the reachability filter**, so `reaches`
   can claim it withheld effects that were never in the answer. It diverged live-vs-store on 3 of 7 patterns
   — but neither side is right; the store is merely more precise by accident of SQL bounding, and its bounded
   closure is still a reach superset, so it can raise the same false hint. Filed as
   [intrinsic-hint-counted-before-reachability-filter](../todo/intrinsic-hint-counted-before-reachability-filter.md);
   pinned in the test with the exemption documented, not papered over.
3. **The bounded-vs-whole-store worry did not materialise.** The store derives effects from a SQL-bounded input
   set and live derives over everything, and the answers still agree because the command filters on
   `reachable.ContainsKey`. The extra `BaseEdgeTuples` hazard I flagged before dispatching turned out not to
   fire here. Worth re-testing on MedDBase scale before trusting it generally.

### The derived-layer cost — the question I said would gate routing

| tree | traversalGraph | epData | effects | total |
|---|---|---|---|---|
| EntryPointEffects, first query | 4.3ms | 0.2ms | 8.6ms | **14.4ms** |
| EntryPointEffects, generation after an edit | 0.9 | 0.2 | 0.5 | **1.7ms** |
| coderig itself (5 projects, 709 reachable methods) | 34.6 | 10.2 | 31.6 | **84.6ms** |

A second query in the same generation costs **nothing** (asserted as a test). Playground numbers are
JIT-dominated; the MedDBase figure is the one that decides 6c and is measured separately. `shapedGraph` and
`hazardEffects` correctly never build on the reaches path.

Rendered in MILLISECONDS deliberately: the first cut printed seconds to 3 decimals and reported every artifact
as `0.000s`. An instrument whose resolution hides what it measures is not an instrument.

### One regression caught in review, not by a test

The stdin query loop treated EOF as "exit". `ReadLineAsync` returns null immediately when stdin is closed or
attached to the null device — which is how a daemon or a background launcher starts a process — so
`rig watch` would have terminated instantly and silently for anyone not sitting at a terminal. EOF now stops
READING, not watching; `quit`/`exit` and Ctrl+C remain the ways out. No test covered this, which is why the
diff review is the gate and not the suite.

### MedDBase real-data check (226 projects) — and the bug it found

`rig watch <MedDBase.slnx> --once --query "reaches DebtorOverride.SaveIncludedServices"`:

```
watch: cold boot in 161.3s — 226 project(s), workspace retained
live: facts current as of 0 file(s) applied | all projects reconciled
From: DebtorOverride.SaveIncludedServices  ⟦3 svcs: MedDBase (iis), MedDBase.DataServer (iis), MedDBase.PACS (iis)⟧
Reachable methods: 526 | Direct effects: 18 | dispatch fan-out: 14 effects
live: derived layer built this generation: traversalGraph 2198.3ms | invocations 242.1ms
      | epData 371.4ms | throwRefs 147.3ms | effects 1038.0ms
```

**The derived layer costs ~4.0s for the FIRST query in a generation, and zero for every query after it.** That
is the number the 6c routing default was waiting on. It is acceptable but not free, and it makes response (2)
from the design note — warm the derived layer in the background right after the eager apply, exactly as the
cascade already does — the obvious next move: it takes the user-visible cost to ~0 without any new machinery.

Cold boot 161.3s vs a 258.4s cold index, as expected: no store is written.

**The comparison against the store answer found a real bug — in the STORE path.** 39 lines each, and the live
answer carried `⚠ lock-held-across` on the `io read` at `AssemblyCache.LoadFile` where the store answer did not.
Isolating it mattered more than noticing it, because three explanations were live at once:

| candidate | how it was ruled out |
|---|---|
| rules differed (`--rules` on one side only) | re-ran the store query WITH the same rules — no change |
| older extraction era (facts lack the scope) | queried the store directly: `EnclosingScopes` IS populated for that invocation |
| tree drift (store is a different commit) | `AssemblyCache.cs` byte-identical across the two commits — and then `derive` vs `reaches` on ONE store removed tree drift entirely |

What remained was decisive: on the SAME store with the SAME rules, `rig derive` (whole-store inputs) attaches
`lock_held_across_effect` and `rig reaches` (bounded inputs) does not. Cause:
`SqlReachability.LoadReachInputsAsync` never SELECTs `EnclosingScopes`, so it is null for every invocation on
the bounded path, and `FactEffectDeriver` derives every lexical-scope observation from exactly that field. So
`reaches`/`tree`/`path` have been silently dropping `lock_held_across_effect` and `transaction_spans_effect`,
while `derive` reports them — **two store paths disagreeing about one store.** Filed HIGH as
[bounded-reach-inputs-drop-enclosing-scopes](../todo/bounded-reach-inputs-drop-enclosing-scopes.md).

Note what found it: not the live index being clever, but a PARITY comparison having a second implementation to
disagree with. The live projection is right by construction (it mirrors `LoadInvocationRefsAsync` field for
field), so the store path's omission became visible the moment two paths answered the same question. That is an
argument for the parity gates themselves, independent of the resident process.

And it is the FIFTH instance in this program of a gate too small to host the defect: the playground comparison
passed on all 14 patterns because no playground holds a lock across IO.

## DECISION 2026-08-21 — fix the fact-mapping shape BEFORE migrating more commands

Raised after slice 6b: `LiveReads` looks like a second implementation of the query layer, and the
`SqlReachability` comment *"EnclosingScopes (param 13) is skipped on this path"* looks like a design smell. Both
readings are right, and they are the SAME defect seen from two ends.

### What is actually duplicated — narrower than it looks

The **derivation logic is single-sourced**: `FactEffectDeriver`, `FactPathFinder`, `FactEntryPointDeriver` have
exactly one implementation and `LiveReads` calls them. What is duplicated is only **table → record mapping**.
That is worth stating plainly, because "we forked the engine" would justify a much bigger intervention than the
real problem needs.

The real problem, measured:

| | |
|---|---|
| `FactInvocation` | **21 parameters, 17 optional** — `EnclosingScopes` at positional **13** |
| `ReferenceFactEntity` | 29 columns |
| hand-maintained mappings of that ONE table | **4** — EF whole-store, raw-ADO bounded, in-memory twin, positional insert |
| index-based `reader.GetString(n)` calls | **85**, across 4 files |

Each of those 85 is a latent instance of the bug that shipped.

### Why sequence the shape fix first

The mechanical work (migrating `tree`/`callers`/`path`/`derive` onto `IQueryFactSource`) routes MORE traffic
through these mappings. Done first, it triples the duplication and each migrated command inherits the ordinal-
mapping hazard. Done second, it is mechanical against one projection layer — which is what "mechanical" should
mean.

### The pattern to generalize is already in-repo, from this program

Slice 6a's delivery-site extraction set it: **the store does the SCAN, a shared pure core does the
PROJECTION** (`Reads.LoadDeliverySitesAsync` → `DeliverySiteProjection` → also feeds `LiveReads.DeliverySites`).
Generalized, `LiveReads` stops being a second implementation and becomes the same core fed from memory instead
of from SQL. That is the answer to "is this a second set of logic": it should not be, and the fix is to finish
a pattern rather than to invent one.

### Split into hazard and width — they are separable

- **The hazard** — ordinal/positional mapping maintained by hand in N places. Fix: single-source the column set;
  the bounded reader stops mapping by hard-coded index. This removes the bug CLASS. Doing it for
  `reference_facts` only, deliberately: one fact table done properly beats four done shallowly.
- **The width** — 21 parameters, 17 optional, is genuine reading pain but is NOT the cause (the cause is
  positional mapping in raw ADO, which a narrow record would still allow). Grouping the structural context into
  a sub-record touches the derivers and their tests, so it is its own slice.

### The direction, named but not taken yet

In a resident world the bounded SQL path is **load-shedding for cold starts that no longer happen**. The end
state is one loader (`rig.db` → facts) plus one projection layer — which is what `LiveReads` already is. Not
now: the one-shot CLI still pays cold start, so the bounded path still earns its keep. But the shape fix moves
toward that end state instead of away from it, which is the tie-breaker for doing it before the migration.

## CLEANUP DONE 2026-08-21 — the fact-mapping shape, and three findings it surfaced

Single-sourcing generalized from `FactInvocation` to every remaining place where two paths built the same wide
record from the same source. Four `// MUST stay field-for-field identical` comments are now shared code:

| new projection | mapping | callers |
|---|---|---|
| `CallEdgeProjection` | `ReferenceFact -> CallEdge` (16 fields, + the redirect callee override) | `Reads.LoadFactGraphAsync` (main + redirect scans), `FactGraphProjection.FromAnalysis` |
| `SymbolFactProjections` | `SymbolFact -> MethodRef / MethodSymbol / TypeSymbol / MethodMeta`, plus the one `IsGeneratedPath` | 7 call sites across `Reads`, `LiveReads`, `FactGraphProjection`, `SqlReachability` |
| `FactFieldAccessProjection` | `ReferenceFact -> FactFieldAccess` (9 fields incl. `EnclosingScopes`) | both `Reads` field-access loaders + `LiveReads` |

No SELECT widened (each row expression fills only what its mapping reads and passes constants for the rest).

**A THIRD copy of `MethodRef` turned up that I had not counted** — `SqlReachability.LoadReachInputsAsync` builds
it from hand-written ADO ordinals over the same 6 `symbol_facts` columns. Exactly the shape that drifted for
`FactInvocation`, and it had no gate. Now enum-driven like the rest, with
`Bounded_method_refs_are_field_equal_to_the_whole_store_loader` fencing it.

`SymbolRef` deliberately NOT shared, and the reasoning is worth keeping: 8 sites, none wider than 5 columns,
against a shared row record with 27 ctor params. The decisive argument is semantic rather than performance —
`EnclosingGuards` is deliberately ABSENT on the ctor/field-access shapes and present on throw/library-call, so
one shared mapping would either start populating it where it was null (a real behaviour change in EP data and
impact leaves) or take a flag that re-encodes the decision each SELECT already makes. Both pairs are already
fenced by reflection-based field-equality tests.

### The gap the parity gates structurally cannot cover

Three parity gates compare two PATHS — so none of them can catch a field dropped from the ONE shared projection
they both now call. `FactProjectionSharingTests` closes that: a fully-populated source row in, then a reflection
sweep asserting no target property came back at its default. Mutation-checked (drop `EnclosingGuards` from
`CallEdgeProjection` -> the sweep fails naming the field). Consolidation moves the risk; it does not delete it.

### Perf: measured, and the first attempt really was a regression

`LoadFactGraphAsync` handles ~2.4M rows on the cold-start path, so the per-row intermediate this pattern
introduces is a real cost. First shape (stream + trailing `.Distinct().ToList()`) cost **+0.5s / ~3.5%** —
outside a ±0.1s baseline spread. Shipped shape pays for the intermediate by deleting the trailing dedup pass:
dedup on insert (`HashSet.Add` guard then append) instead of `Distinct`/`GroupBy…First`, which is exactly
equivalent (both yield first occurrences in order) but never stores the duplicates and never builds the second
list.

Verified by me, INTERLEAVED A/B on the MedDBase store: base 14.4 / 14.5 / 14.6 / 14.7s vs after
14.3 / 14.5 / 14.4 / 14.5s — no regression.

**A measurement lesson to add to the three already recorded: sequential A-then-B is not an A/B.** Running all
the baseline samples and then all the after samples gave 14.4-14.5s vs **17.0-23.5s** and looked like a
catastrophic regression; the same after-binary had measured 14.4s ten minutes earlier. It was memory pressure
from a build plus back-to-back 2.4GB processes. Interleave, or do not compare.

### Behaviour: byte-identical

MedDBase `derive --format tsv --intrinsic`: **377,583 lines identical** before/after, both forced cache-cold via
distinct inert overlay rule files (`RulesFingerprint` hashes path AND content, so each run computes live).
`dispatch-fans --format tsv` identical by sha256. Seven playground outputs identical. No `*Schema` bump needed —
nothing derived moved.

### Three findings filed

1. **[A rules edit does not reach the baked graph](../todo/baked-call-edges-ignore-rules-edits.md)** — HIGH, and
   proven by experiment: after a `handoffDispatchers` edit with no re-index, `derive` honours the new rule and
   `reaches`/`tree`/`path` serve the classification baked into `call_edges` at index time. Worse, ONE `derive`
   run can mix both, because its handoff-EP listing reads the baked table while hazards use the re-classified
   graph. The rules axis of the documented three-axis cache hedge does not reach `call_edges`, because that
   table is index output rather than a cache — but it behaves like one.
2. **[Redirect rules applied asymmetrically](../todo/redirect-rules-applied-asymmetrically-across-graph-paths.md)**
   — LOW/latent: the in-memory builder redirects EVERY ref, the store loader only `!TargetInSource` ones. Today's
   rules only name external overloads so the sets coincide. This is the FILTERING half — now the only
   hand-maintained invariant left between the two graph paths, and worth its own audit.
3. Two dead-ish loaders (`LoadStaticFieldWriteRefsAsync`/`LoadStaticFieldReadRefsAsync` have no production
   callers; their comments still describe a `tree --hazards` path that now serves the cached whole-store set).

Findings 1 and 2 are both "two surfaces, one store, different answers" — the same family as the
`EnclosingScopes` bug and the `/api/meta` one. That is three in two days, all from one cause: a derivation input
that one path folds in and another does not. The pattern is worth naming as a standing review question rather
than being rediscovered a fourth time.

## RECORD WIDTH 2026-08-21 — `FactInvocation` 21 -> 9, and what the refactor actually bought

Three `readonly record struct` groups (struct, not record class — at ~2.4M invocation facts, reference-type
groups would add millions of heap objects; a struct of string refs embeds inline):

| group | members |
|---|---|
| `FactLoopContext` (`Loop`) | Kind, Detail, ElementType, BindType |
| `FactCallSiteNesting` (`Nesting`) | Invocations, CatchTypes, Scopes, Guards |
| `FactCallArguments` (`Args`) | Receiver, FirstTemplate, FirstType, FirstName, Templates, Names |

Left flat: Target, Enclosing, FilePath, Line, TypeArguments, InExpressionTree. **21 -> 9.**

Naming: `Nesting` rather than any `Enclosing*` (taken by the enclosing symbol id) or `*Context` (that reads as
`FactStructuralContext`, which is the DECODER for these four encoded strings, not their container). The four are
ancestor walks frozen at the call site, which is what `FactCallSiteNesting` says.

`ReferenceFact` stays flat and wide on purpose: it is the row record mirroring 29 columns 1:1, constructed inside
EF `Expression<Func<..>>` trees where nested struct construction is a translation risk. A row is not a domain
model.

`CallEdge` DEFERRED with numbers rather than by feel: 470 references across 78 files (`FactPathFinder` + 4
partials, `GraphMaterializer`, `TraversalGraphLoader`, `HandoffClassifier`, `RedirectClassifier`,
`GenericMonomorphizer`, `FactCycleDeriver`, `DeadCodeFinder`, ~50 test files constructing edges positionally) —
~15x this slice's read-site count, turning a ~300-line diff into 1500+. Its own slice.

### Verification

Independently re-run: MedDBase `derive --format tsv --intrinsic` = **377,583 lines, md5
ee290713076243a8643d412bd0ac0da5**, identical to the pre-change baseline. Interleaved A/B `graph load`: after was
equal-or-faster in all 4 rounds (means 15.48s -> 14.63s). Suite 1030/1029/0/1, unchanged. No `*Schema` bump —
nothing derived moved.

The risk here was never omission (every missed site is a compile error) but MIS-mapping — `Loop.Detail` where the
original read `Loop.ElementType` compiles and is wrong. Checked by reading the diff's -/+ pairs at the two sites
where the distinction is subtle (`FactIterationFanoutDeriver` passes both `Detail` (source text) and
`ElementType` (resolved) to `IterationContext.Of`; `KeyOf` reads the indexed lists and the unindexed fast path 25
lines apart) — all pairs correspond.

### The payoff was not readability

The refactor immediately exposed a gate defect that had been invisible:
**[`tests/Rig.Tests/Fixtures/FactProjection.cs` builds `FactInvocation`s production never produces](../todo/test-fixture-invocation-mapping-is-not-field-complete.md)**
— a FOURTH hand-written copy of the mapping, missing `Nesting.Guards`, `Loop.ElementType`, `Loop.BindType` and
`InExpressionTree`. Six test files derive effects through it, so any arm gated on those four cannot fire there,
and an asserted ABSENCE may hold only because the fixture withheld the field.

It was undetectable while the members were flat: four omitted OPTIONAL parameters look exactly like deliberate
defaulting. Grouped, the fixture visibly constructs a 4-member `FactLoopContext` with two members and a 4-member
`FactCallSiteNesting` with three. **Making an incomplete construction LOOK incomplete is what the width refactor
actually bought** — more than the reading ergonomics that motivated it.

Also strengthened in passing: `ReachInputProjectionTests.Rendered<T>` now recurses into group structs instead of
leaning on their generated `ToString`, which folds `null` and `""` into the same rendering — the exact
distinction that gate exists to see. A grouping change would otherwise have quietly weakened it.

## API COMPAT slice 1 2026-08-21 — `path` + `callers` live, derived layer warmed

`IQueryFactSource` 6 -> 10 members (`LoadShapedTraversalGraphAsync`, `SymbolExistsAnywhereAsync`,
`LoadEntryPointDataAsync`, `DeriveEntryPointsAsync`). Store path a pure move; `rig watch --query` now takes
`reaches`, `path` and `callers`.

**`callers` is byte-identical to the store on stdout AND stderr, all 10 patterns across two playgrounds —
including the REVERSE direction, which nothing had exercised.** The whole in-memory graph narrows to the same
answer as the SQL-bounded reverse closure. `callers --entrypoints` matches too, which was the riskiest new member
(live handoff-EP derivation vs the store's materialized `call_edges` arm).

`path` diverges on exactly two lines, and the divergence is a STORE bug, proven by the store disagreeing with
itself across its own two loader arms — filed as
[path-disclosures-computed-off-the-loaded-subgraph](../todo/path-disclosures-computed-off-the-loaded-subgraph.md).

### Three capabilities deliberately NOT added to the interface

The seam's value is that nothing on it throws. FTS symbol search, the materialized `entry_point_sites` table and
the `cache.db` query cache are store-only; the live path mirrors the **LIKE arm** of symbol search in memory
(the arm a `--no-graph` store already uses), derives EP sites per generation, and replaces the query cache with
per-generation memoization. A member whose live implementation throws would have been the wrong answer.

### Warming works: the first query after an edit no longer pays the derived layer

Started after the eager apply, cancelled by the next edit — the reconcile task's pattern. One deliberate
difference: the worker AWAITS a cancelled reconcile (single-writer `ResidentIndex` requires it) but does NOT
await a cancelled warm, because warming only forces `Lazy` fields on an immutable value — awaiting it would be
exactly the trade warming must lose. Only the query artifacts are warmed; `shapedGraph`/`hazardEffects` are
derive-shaped and no live query path reads them.

MedDBase, 227 projects: boot-generation first query paid **2607ms** of derived layer (the old post-edit
behaviour); after an edit the layer is warmed off-path in 2.52s and the post-edit query reports **no cost line at
all**. coderig's own solution: 30.1ms -> 0.

Two review fixes of my own on top: `eventSubscriptionSites` was the one artifact NOT memoized, so every
non-`--raw` query re-projected it over the whole reference-fact set for the life of a generation — invisible
because it was absent from `BuildTimes`. Now memoized and warmed (it shows as `eventSites 93.4ms` on MedDBase).
And the usage banner had been contorted to keep a verbatim-pinned test assertion intact; reworded properly and
updated the one string literal, which is a reviewer's call, not the builder's.

## CRITICAL 2026-08-21 — the real-data run that matters most

Booting `rig watch` on a fresh, **unrestored** MedDBase clone:

```
watch: cold boot in 73.2s — 227 project(s), workspace retained
live: facts current as of 0 file(s) applied | all projects reconciled
Methods that reach 'SmartLetter.SaveLetter': 0
```

Clean, fast, complete-looking. The same run emitted **2,387,334 compiler error lines**, 1,793,241 of them CS0518
`Predefined type 'System.Object' is not defined` — no references resolved, every compilation effectively empty.
**That `0` is not "nothing calls this". It is "there was no code" — and nothing in the output distinguishes the
two.** The status line asserts the opposite: `all projects reconciled`.

Filed CRITICAL as
[live-index-serves-confident-answers-from-a-broken-compilation](../todo/live-index-serves-confident-answers-from-a-broken-compilation.md),
with three defects: no failed-compilation disclosure (the approved-but-unimplemented spec), 528 MB of uncapped
error output on raw stdout interleaved with the answer, and no `--restore` on `rig watch` at all (the analyzer
accepts the flag; the command never passes it, so a fresh clone cannot be booted correctly).

**This reorders the plan.** The remaining migrations (`tree`, `derive`) add surface to a tool that currently
cannot tell you its facts are worthless. Disclosure goes first.

## DISCLOSURE SHIPPED 2026-08-21 — the live index no longer serves a confident answer from a broken tree

Implements the approved subset of `docs/spikes/failed-compilation-disclosure-spec.md`: capture compilation health
as structured data and disclose it ON THE ANSWER. The store-side half (the `source_files`/`runs` schema columns,
the per-line `~compile-error` chip joined on FilePath, `rig files --compile-errors`) is a follow-on that needs a
re-index; its vocabulary is reserved, not spent.

```
CompilationHealth(Files, PartialProjects, UnlocatedErrorCount)
FileCompileHealth(FilePath, ErrorCount, ErrorCodes, FirstMessage)   // codes deduped/sorted, cap 8 then +N
ProjectCompileFailure(ProjectName, Reason)                          // no_compilation | generator_emit | generator_run
```

On `AnalysisResult`, so it flows wherever facts flow. **`ResidentIndex` merges it by the same replace-per-file
rule as the facts** — and the clearing mechanism is that a re-extracted CLEAN file contributes an EMPTY list,
which drops its base row. Keyed on `document.FilePath`, not the diagnostic's reported path, because the overlay
replaces by that exact string and any other key would leave a stale flag nothing could clear. In a one-shot
index a stale flag is impossible; resident, it would survive the process lifetime — hence
`Broken_then_fixed_then_broken_over_one_retained_workspace`, which passes with identical evidence on both broken
phases.

### Calibrated in the PRODUCTION configuration, which is the only number that counts

`rig watch <MedDBase.slnx> --rules rig.rules.json` on the restored clone:

```
live: facts current as of 0 file(s) applied | 3 of 11938 indexed file(s) had compile errors
stderr: 4 lines total — the 3 named diagnostics + one note.
```

**3 of 11,938 = 0.025%, and it names the three files.** Quiet enough to leave on by default, and decisively
distinguishable from the `!:` partial-binding floor (7.3% of files), which it is never derived from (spec §3.3).

An intermediate measurement said 8 in-set + **41 outside-set** files and looked like a noise problem. It was an
artifact of running WITHOUT the rules overlay: those 41 are `obj/` `AssemblyInfo` files in projects that clone
had never built, which the production rules exclude. **Calibrate in the configuration you ship, or you will tune
against noise you invented** — and the same run exposed a real leak worth its own ticket:
[`--exclude-tests` matches on project NAME only](../todo/test-project-exclusion-is-name-only-and-leaks.md), so
`MedDBase.QA.Automation.Setup` under `tests/ui/` gets indexed and contributed 24,309 of that run's 24,545
diagnostics — one leaked project nearly monopolising the signal.

### A ratio bug the real data caught, worth recording as a pattern

The first cut printed `10648 of 10565 indexed file(s)` — **numerator larger than denominator** — because Roslyn
reports diagnostics in files rig never indexed (`obj/` AssemblyInfo, classifier-skipped) while the denominator
counted only indexed rows. Same defect class as the intrinsic-effects count that had to be dropped for
overstating by 8x: **a ratio whose halves are drawn from different populations.** Both halves now derive from one
`IndexedFileSet`, making it impossible by construction, and outside-set files are disclosed separately rather
than folded in or hidden. Pinned by `Files_outside_the_indexed_set_are_disclosed_separately_and_never_break_the_ratio`.

### Output volume

| | before | after |
|---|---|---|
| stdout | 2,387,334 lines / **528 MB**, interleaved with the answer | **7 lines / 476 bytes** |
| stderr | — | capped: 5 per project + a truthful per-project total |
| retained in memory | every error string (~528 MB of `ConcurrentBag<string>`) | <=5 strings/project + one counter |

### `rig watch --restore`

The analyzer already accepted `restore`; the command never passed it, so a fresh clone could not be booted
correctly at all. Verified end to end on a deliberately unrestored tree: without it,
`24 of 26 indexed file(s) had compile errors` (CS0518 `Predefined type 'System.Int32' is not defined`) and
`Reachable methods: 7`; with it, `all projects reconciled` and `Reachable methods: 8`. The disclosure and the
flag corroborate each other.

### Still open, deliberately

Generated documents' diagnostics are not observed (`RunSourceGeneratorsAsync` builds a compilation nobody calls
`GetDiagnostics` on, and the driver's `diagnostics: out _` still discards them) — spec row 7a, out of scope.
Diagnostics go to `Console.Error` directly rather than the CLI's injected error writer, so in tests they land on
the real process stderr. And the per-line `~compile-error` chip remains the follow-on: this slice gives
COMPLETENESS (it fires whenever any file or project is affected, including the blind spots a chip cannot
cover — a lost dispatch edge has no file to flag); the chip gives LOCALITY.

## API COMPAT slice 2 2026-08-21 — `tree` live, and the cache question answered

`tree` (the largest command, ~921 lines, and the only CACHED one) now answers from the resident index.
**Byte-identical to the store on stdout across 24 comparisons** on two playgrounds, covering the whole
view/format matrix — default, `--view full`, `--view hazards`, `--view effects`, `--view summary`,
`--format tsv`, node-budget truncation, depth truncation, the no-match/exit-1 case, and the EP-chip case.
Truncation was exercised deliberately (`⋯elided` asserted present in three cases) because `TruncationCause` is
cached state, so a cache-key mistake surfaces exactly there.

### The cache seam: a replacement, not a no-op

The slice added a SECOND seam next to `IQueryFactSource` — `IQueryArtifactCache`, i.e. *where a query memoizes
derived artifacts*: `StoreQueryArtifactCache` over `.rig/cache.db` (same `QueryCache.Open`, same codecs, same key
material) and `LiveQueryArtifactCache` over a per-generation bounded memo (objects, not blobs). That is the
honest shape: the live arm is not a disabled cache, it is a different cache with the same contract. A new
`AnalysisResult` means a new `LiveFactSource` means a new empty memo — **stronger than a store-identity token,
because two generations cannot share a slot at all.**

The live memo reuses `QueryCacheKeys.TreeCacheKey` — the same function — keeping every axis except store
identity: schema, rules fingerprint, pattern, depth, node budget, mode, raw. The collision proof is a test that
predicts the key from OUTSIDE the command, asserts the memo empty before and populated under exactly that key
after, then asserts a `--limit`-only variant lands in a DIFFERENT slot and produces a measurably smaller forest
(1 node vs 3, `⋯elided` present in one answer and absent in the other). Plus a test that a live query never
touches `.rig/**/cache.db`, with the store query as positive control.

### `HazardEffects` in memory: 3.4s, and the recommendation is DON'T warm

Measured on the restored clone (445,235 symbols / 2,437,358 refs) via a new opt-in harness:

| artifact | in-memory |
|---|---|
| `hazardEffects` | **3.4s** (362,368 effects) — the store path's ~18s SQL-cold artifact |
| `shapedGraph` | 3.9s |
| `graphHazardFindings` | 5.0s (forces `shapedGraph`; ~1.1s of classification on top) |
| the already-warmed query set | 6.2s |

So the SQL read *was* most of the 18s. But the marginal warm cost is ~8.4s, which would more than double the warm
window (6.2s -> ~14.6s) for an arm most queries never touch — and warming is bounded by what the worker's next
apply must not queue behind, with a `Lazy` factory being uninterruptible once entered. **Left lazy, cost disclosed
where it is paid** (`hazardEffects 3423.5ms | shapedGraph 3859.0ms | graphHazardFindings 4952.1ms`), and every
later hazards query in the generation pays nothing. Measure-then-decide, not switch-on-because-available.

### The one divergence is a CONFIRMATION, not a new finding

`tree` has the already-filed
[intrinsic-hint](../todo/intrinsic-hint-counted-before-reachability-filter.md) bug — and in EVERY view, not just
hazards, which that item explicitly asked someone to check. Same pattern as the `reaches` measurement named
(`TeamRepository.AddAsync`), 1 of 5, stderr only. The item now records the confirmed blast radius: `reaches` and
`tree` (all views); `path`/`callers` do not emit the hint.

### A per-query cost found by reading the code, not the instrument

[Live EP derivation is per-QUERY, not per-generation](../todo/live-ep-derivation-is-per-query-not-per-generation.md):
`LiveQueryRunner` constructs a fresh `LiveQueryFactSource` per query, so its `_entryPoints`/`_epSiteKind` memos
die with the query — and what they memoize includes a full `FactGraphProjection.FromAnalysis` over every
reference fact (~2.4M on MedDBase, ~2.3-3.3s by the equivalent artifact's measurement). It runs whenever
`deployments.json` exists, which it does on the real analysis dir.

This is the SECOND artifact to hide this way (after `eventSubscriptionSites`), and the reason is identical: it is
not on `LiveFactSource`, so it is not in `BuildTimes`, so no measurement this program has built can see it.
**The real invariant is "is it on `LiveFactSource`?", not "is it memoized?"** — worth stating as a rule, because
reading the instrument would never have found either one.

Filed rather than fixed in review: the key must be the rules FINGERPRINT, not the `RuleSet` instance, because
`TreeCommand` reloads rules per query and reference-keying would silently preserve the bug at zero hit rate.
