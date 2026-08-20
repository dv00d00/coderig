# IDE-architecture prior art -> rig's resident-index design

> Status: research spike, no code changed. Written 2026-08-20 to ground the move from a batch
> CLI (`rig index` then a stack of one-shot query commands) to a resident process with a live
> background index. Thesis: we are building an IDE-class incremental index; steal from the systems
> that already solved this instead of re-deriving it. This is a mapping onto rig's ACTUAL code
> (file/line cited), not a literature survey.

## rig's starting position, restated precisely

- **Two-stage split is fixed**: Roslyn extraction (`rig index`) -> immutable facts in SQLite ->
  Roslyn-free query-time derivation (`derive`/`reaches`/`tree`/`impact`). Not up for renegotiation
  (`CLAUDE.md` "Two-stage design + dispatch model").
- **The derivation chain is already a query graph**: facts -> effects (`FactEffectDeriver`) ->
  hazards -> findings, hand-memoized in `src/Rig.Cli/Caching/QueryCacheKeys.cs`. Every cache key is
  built from three orthogonal axes: **store identity** (`StoreKey` = rig.db size+mtime, one value
  for the *entire* multi-hundred-project store), **rules fingerprint** (hash of the loaded
  `rig.rules.json` files), and a **per-artifact schema int** (`EpSchema`/`TreeSchema`/
  `HazardEffectsSchema`/`GraphHazSchema`/`ImpactSchema`/`FindingViewSchema`) bumped by hand on any
  same-input-different-output logic change. `DerivationSchemaToken()` folds all schema ints into one
  token that also drives the web client's IndexedDB key.
- **A resident PoC already exists and already independently reinvented pieces of this literature**:
  `src/Rig.Cli/Caching/WarmStore.cs` (in-process LRU over the shaped call graph + invocation-ref
  table, keyed identically to the disk cache) and `WarmStoreWatcher.cs` (`FileSystemWatcher` on
  `.rig/`, debounced 2s, re-warms on `rig.db`/`LATEST` change). Both are explicitly marked PoC and
  both watch **rig's own output**, not the source tree — there is no source-file watcher yet.
- **`docs/incremental-indexing.md`** (design-intent-only, not built) already derived, from first
  principles, something structurally identical to salsa's "firewall query": cache per-project facts,
  key invalidation on `hash(P.sources) + hash(public-API surface of every transitive dependency)`,
  so a body-only edit does **not** cascade to dependents (only a public-surface change does).
- **Measured costs that constrain every idea below**: full index 253s / 227 projects; in-memory
  fact graph 3.47 GB; index peak RAM 12.1 GB (`docs/memory-optimization-strategies.md`: ~9 GB peak is
  *structural* — every project's `SemanticModel` is retained until the whole extract phase finishes,
  `SolutionAnalyzer.cs:51,125`); a warm-cache query still paid 4.6s of graph load before `WarmStore`
  existed. `rig impact` diffs two **immutable, commit-scoped** stores (`.rig/<short-sha>/rig.db` +
  `LATEST` pointer, `StoreLayout.cs`) — durable on-disk snapshots cannot go away.

---

## 1. salsa (rust-analyzer)

**What it is.** salsa is a demand-driven query framework: you declare computations as memoized
functions of other memoized functions; on an input change, salsa doesn't blindly recompute — it
walks the dependency DAG and only recomputes a downstream query if a query it actually read produced
a *different* value than last revision ("red-green" marking). Two refinements matter here:
**durability** (tag an input/query LOW/MEDIUM/HIGH — a HIGH-durability input is assumed to change so
rarely that salsa can skip re-validating whole subgraphs below it until it actually needs the exact
value) and the informal **"firewall query"** pattern — a query whose *output* is far more stable than
its *inputs* (e.g. "does this item's public signature change" vs. the raw source text), so an edit to
a function body never propagates past the firewall to that function's callers.

**Concrete rig equivalent.** rig's `QueryCacheKeys` axes are a *hand-rolled, binary* version of the
same idea: `StoreKey` is durability collapsed to one bit (the whole 3.47 GB store is either
identical or it isn't — no partial credit), `rulesHash` is a second, genuinely HIGH-durability input
(rules change rarely, and correctly get their own axis so a rule edit doesn't force a reindex). The
`*Schema` ints are salsa's revision-bump made manual: "the query's *logic* changed" instead of "an
input changed." **`docs/incremental-indexing.md`'s public-API-surface-hash cascade is, unknowingly,
exactly salsa's firewall-query pattern**, applied at project granularity: a project's own source hash
is the LOW-durability input; its dependencies' surface hash is the firewalled signal that only moves
on a genuine binding-relevant change, not on every edit anywhere in the transitive closure.

**What it would replace/subsume.** Not the artifact-level `QueryCacheKeys` (those five named
artifacts are coarse-grained by design and don't need a general query engine). What it *would*
replace is the **binary `StoreKey`** — salsa's real generalization is that "the store changed" should
not be a single fact but a per-project (or per-file) revision counter, so a resident process can
answer "is the shaped graph stale *with respect to file X*" instead of only "stale, full stop."

**Cost of adopting it in C#.** There is no salsa-equivalent crate in the BCL or a mainstream NuGet
package with comparable maturity, so "adopt salsa" really means **port the pattern, not the library**.
The closest *already-in-C#* prior art to study instead of porting Rust: Roslyn's own
`IIncrementalGenerator` pipeline (`IncrementalValueProvider`/`.Select`/`.Combine`, backed by
`IncrementalGeneratorTransformNode`/`DriverStateTable`) is salsa-shaped — a DAG of memoized,
comparer-gated transform stages — and ships in the compiler rig already depends on. Building a
general salsa-like engine for rig's ~6 named artifacts is over-engineering; rig doesn't have
rust-analyzer's query-count problem (tens of thousands of fine-grained queries per keystroke). The
right-sized adoption is narrow: replace the single `StoreKey` bit with a **per-project revision
vector** (feeding `docs/incremental-indexing.md`'s surface-hash cascade), and let the existing five
artifact caches key off "did any input this artifact actually reads change" rather than "did the
whole store's mtime change." That's a data-modeling change to `QueryCacheKeys`/`WarmStore`, not a new
engine.

## 2. clangd

**What it is.** clangd keeps a background thread pool continuously re-indexing files at low priority
(`BackgroundIndex`), persists per-translation-unit index shards to disk keyed by content so a restart
doesn't reindex unchanged files, and caches a **preamble** (the precompiled prefix of a single file up
to the first "interesting" point) so incremental edits to the *tail* of a file reuse the parsed head.
Crucially, it will happily **serve the previous index while a rebuild is in flight** rather than block
the editor.

**Concrete rig equivalent.** rig has no unit smaller than "a project" (no per-file preamble — the
extraction unit is the whole compilation), so the preamble-caching half doesn't transfer as-is: rig's
"preamble" would be a project's *unchanged dependency compilations*, which is exactly
`docs/memory-optimization-strategies.md`'s A2 idea (keep dependency `Compilation`s resident, drop them
only once every dependent has extracted) — same shape, already scoped, already flagged High effort /
Medium-High risk there. The **serve-stale-with-a-marker** half transfers cleanly and is the highest-
value single idea in this document, because it is not a compromise for rig — it is rig's existing
identity. rig already discloses its own approximations (`~heuristic` dispatch tag, the "dispatch
fan-out (NOT a real call)" bucket in `reaches`) instead of pretending precision it doesn't have.
Staleness is the same kind of fact and should be disclosed the same way, not hidden behind a spinner.

**What rig's staleness marker should contain (concrete proposal).** For a resident process, every
response should be able to carry:
- `indexedCommit` / `indexedAt` — which store answered the query (rig already has this: the
  commit-scoped `.rig/<sha>/` directory name and the db mtime).
- `dirtyFiles` — paths that have changed on disk (or, if wired to the agent harness, been written by
  a tool call) since `indexedCommit` was extracted, cheaply computed as `git diff --name-only
  <indexedCommit>` — no new source-watching infrastructure needed for a first cut.
- `affectedNodes` (best-effort) — whether the *specific* answer being returned (a reaches-path, an
  effect list for one entry point) touches a dirty file, vs. the store being globally dirty but this
  particular answer untouched. This is the same firewall idea as §1: most edits don't affect most
  queries, and disclosing *which* queries are safe is strictly more useful than a global dirty bit.

This is cheap (git diff, not a new indexer) and is a pure win to ship early — it doesn't require any
of the harder incremental-extraction work to already exist.

## 3. LSP itself (document versioning, pull vs push, didOpen/didChange)

**Document versioning.** Every LSP response is computed against a specific `TextDocument.version`,
so a client can discard a response that arrived after a newer edit superseded it. **rig's current
"document version" is the whole-store `StoreKey`** (size+mtime) — one version number for a
multi-hundred-project solution. That granularity is *correct* for the batch model (there's only ever
one store) but is the direct cause of the staleness problem in §2: rig has no way to say "this
particular answer's version is file X's version," only "the store's version." Adopting LSP's idea
means introducing a **per-project (matching rig's own extraction unit) version**, not per-file — rig
already reasons in project-shaped units everywhere else (extraction, `dispatch_facts`, the
incremental-indexing design doc), so a per-file version would be finer than anything else in the
system already provides and wouldn't compose with the rest of the model.

**Pull vs push.** LSP shipped push diagnostics first (server proactively re-pushes on every
`didChange`) and moved to pull (`textDocument/diagnostic`, client asks when it wants an answer)
because push wasted CPU re-validating files nobody was looking at. **rig's derivation layer is
already pull** — `derive`/`reaches`/`tree`/`impact` compute on demand, exactly the model LSP arrived
at, and arguably ahead of where plain push-based tooling starts. The place rig is still closer to
"push" is the *whole `rig index` invocation itself*: it is an all-or-nothing eager re-extraction of
every in-scope project, triggered explicitly rather than continuously. The right synthesis for the
resident design — and the one clangd already validates — is **push at the extraction layer, pull at
the derivation layer**: a background thread incrementally re-extracts *changed projects only* as
edits land (cheap relative to a full 253s run once incremental extraction exists), while the
expensive artifacts (effects/hazards/impact, all of which cost real CPU/RAM per §"measured costs")
stay computed lazily on the first query that needs them, exactly as today. This requires the
incremental extraction from `docs/incremental-indexing.md` to actually be built — it is the
prerequisite, not an alternative to it.

**didOpen/didChange text sync — does NOT transfer, and here's why.** LSP's delta-buffer model exists
to serve **sub-second keystroke-level** reparse latency for a human typing. rig's actual clients are
coding agents that make **discrete file writes between tool calls** — there is no keystroke stream to
buffer, and no human waiting on 16ms round-trips. Building an in-memory edit-buffer/diff-sync layer
to mirror `didChange` would be solving a latency problem rig's usage pattern doesn't have, at real
implementation cost (incremental range-based text patching, buffer/disk reconciliation). The unit
that actually matters for rig is closer to `didSave`/`didClose`: "a tool call finished writing these
N files" — a discrete, coarse-grained notification, not a fine-grained delta stream. Recommendation:
give the agent harness (or a file watcher as a fallback) a way to say "these files just changed,
re-extract them," and stop there.

## 4. LSIF / SCIP

**What they are.** LSIF (Language Server Index Format, JSON) and its successor SCIP (Sourcegraph
Code Intelligence Protocol, protobuf — SCIP is ~8x smaller and ~3x faster to process than LSIF, and
Sourcegraph has fully deprecated LSIF ingestion since 4.6) are persisted, cross-session index formats
for pure code **navigation**: definitions, references, hovers, documents, occurrences. They have a
broad multi-language indexer ecosystem and Sourcegraph's cross-repo UI consumes them directly.

**Is rig "LSIF-with-effects"?** Only in the sense that both are "facts extracted once, queried many
times." The resemblance stops at the schema: LSIF/SCIP model *occurrences of symbols in documents* —
there is no vocabulary for an **effect** (IO/DB/cache/messaging observation), a **hazard**
(TOCTOU/race-window/dual-write), an **entry point**, a **dispatch resolution basis**
(`roslyn` vs `~heuristic`), or a **reachability-gated** call graph. Those are rig's actual value —
the whole point of `FactEffectDeriver`/`FactHazardDeriver`/`FactPathFinder` — and none of it has a
home in SCIP's symbol/occurrence model. Converging the *core* store on SCIP would mean either (a)
dropping everything that makes rig rig to fit a format designed for go-to-definition, or (b) forking
SCIP with rig-specific extensions until it's a different format wearing SCIP's name — neither buys
anything over rig's own SQLite schema, which is already purpose-built and already fast (query-time
derivation in seconds, not the whole point of a portable interchange format).

**Where interop *would* be a genuine, low-risk win — and where it's a distraction.** A one-way SCIP
**exporter** (definitions + references + hovers only, dropping effects/hazards entirely) as a side
artifact of `rig index` would let rig's already-extracted facts feed Sourcegraph-style cross-repo nav
for free — additive, no schema compromise, because it's a lossy *projection out*, not a shared source
of truth. That is a legitimate, separable backlog item. It is **not relevant to the resident-index
redesign** this document is about and should not be entangled with it — converging the fact store
itself on an external format would constrain exactly the effect/hazard extensibility rig needs to
keep growing detectors, for a format whose actual consumers (cross-repo code nav UIs) aren't a rig
use case today.

**Verdict: stay proprietary for the fact+derivation store; a SCIP exporter is a fine, unrelated,
low-priority follow-on.**

## 5. Other prior art

**Roslyn's live `Workspace`/`Solution` model (Rider/ReSharper, OmniSharp, VS).** A resident language
service holds a `Workspace` whose `Solution` is an immutable snapshot; `Solution.WithDocumentText(...)`
produces a *new* `Solution` that structurally shares every unchanged project's `Compilation` by
reference, and Roslyn's own project-dependency-aware compilation cache handles per-project
incrementality — for free, using the exact same Roslyn APIs `SolutionSourceLoader` already calls in
extraction stage 1. This is the single most directly portable idea in this document, because it
requires zero new machinery: **hold a live Roslyn workspace resident across queries, and apply an
agent's edits via `WithDocumentText` instead of rig re-inventing per-project content-hash
invalidation** (the entire "MSBuild replication cost" problem `docs/incremental-indexing.md` flags as
the hard part — computing `key(P)` without a full design-time build). Roslyn already solved exactly
that problem for its own incrementality; rig re-deriving it by hand is the harder path.

**This directly collides with the memory strategy, and is a real fork to surface, not resolve here.**
`docs/memory-optimization-strategies.md`'s #1-ranked peak-RAM fix (A1) is to **release** every
project's `SemanticModel`s as soon as that project is extracted, because retained bound-node state is
the dominant contributor to the measured 9-12 GB peak. A resident live workspace does the *opposite*
on purpose — it keeps compilations (and their bound state) warm precisely so the next edit's
incremental re-bind is cheap. Freshness-via-live-workspace and peak-RAM-via-release-eagerly are in
direct, structural tension on the same resource. This is a genuine architecture fork for the resident
design (live Roslyn workspace vs. rig's own batch-then-drop model with a hand-rolled incremental
cache) and should be surfaced as an explicit decision, not defaulted either way here.

**Bazel/Buck-style content-addressed action caching.** Model each project's extraction as an action
keyed by a hash of its inputs (source file hashes + resolved reference DLL hashes + compiler flags) ->
action-cache lookup -> either replay the cached facts blob or run the action and store the result.
This is, almost exactly, `docs/incremental-indexing.md`'s own `key(P)` proposal — the contribution
here is naming it correctly and pointing at what that naming buys beyond the doc's current scope:
Bazel/Buck action caches are conventionally **shared** (keyed by content, not by machine or checkout),
which is the direct fix for the pain point `CLAUDE.md`'s MedDBase section already calls out — "a
fresh `git worktree` per index loses the [design-time-build] cache and forces a from-scratch build of
the whole monorepo." If the per-project action key is ever made computable (still the hard,
unsolved MSBuild-evaluation-replication problem — action caching doesn't remove that cost, only gives
a place to put the result once computable), storing it in one shared location instead of per-worktree
would let every dev machine, CI run, and agent worktree indexing the same commit benefit from each
other's already-extracted projects. Low novelty over the existing doc; the value-add is "share the
cache across worktrees/machines," which the doc doesn't currently scope.

---

## Top 3 to adopt, top 2 to reject

**Adopt:**
1. **Per-project revision/durability tracking feeding `QueryCacheKeys`/`WarmStore`** (salsa's
   firewall/durability principle, already half-invented in `docs/incremental-indexing.md`) — replace
   the binary whole-store `StoreKey` with a per-project signal so one edited file doesn't invalidate
   the entire resident warm cache.
2. **Explicit staleness disclosure on every resident-mode response** (clangd's serve-stale model,
   phrased in rig's own existing `~heuristic`-style disclosure idiom) — cheap to ship now via `git
   diff` against the indexed commit, no incremental-extraction prerequisite.
3. **Push incremental extraction, pull derivation** (the LSP push->pull lesson applied at the layer
   boundary rig already has) — background-reindex changed projects continuously; keep
   effects/hazards/impact computed lazily on query, unchanged from today.

**Reject:**
1. **LSP-style keystroke-level `didChange` delta-buffer sync** — solves a human-typing latency
   problem rig's agent clients don't have; a discrete `didSave`-granularity "these N files changed"
   notification is the right-sized unit, not an in-memory edit buffer.
2. **Converging the core fact/derivation store on LSIF/SCIP** — category mismatch; SCIP has no
   vocabulary for effects/hazards/dispatch-basis, which are rig's actual value. A one-way SCIP
   exporter for pure nav is a fine unrelated follow-on, not part of this redesign.

**Open fork to surface (not resolved here):** a resident live Roslyn `Workspace` (clean incremental
re-bind via `WithDocumentText`, the most directly portable idea in §5) trades directly against the
peak-RAM fix already ranked #1 in `docs/memory-optimization-strategies.md` (release `SemanticModel`s
eagerly). Freshness-via-live-workspace and peak-RAM-via-release-eagerly cannot both be maximized on
the same resident process; this needs an explicit call before the resident extraction layer is built.
