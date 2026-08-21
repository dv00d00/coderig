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
