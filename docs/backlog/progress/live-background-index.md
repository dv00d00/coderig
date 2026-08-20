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
