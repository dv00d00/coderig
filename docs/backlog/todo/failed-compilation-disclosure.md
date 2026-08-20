# Facts from a tree that does not compile are presented as sound — no query-time disclosure

**Status:** todo — spec written, ready to dispatch:
[docs/spikes/failed-compilation-disclosure-spec.md](../../spikes/failed-compilation-disclosure-spec.md) ·
**Priority: HIGH** (silent wrong/incomplete answers at exactly the moment rig is most used — right after an
edit; the failure is a confident "nothing calls this", not an error) · **Found:** 2026-08-20, raised by the
[equivalence-test-matrix](../../spikes/equivalence-test-matrix.md) § "Non-compiling tree" while specifying the
[live-background-index](../progress/live-background-index.md) spike pool · **Family:** disclosure / extraction
provenance

**Decision:** the **product owner chose the prefix-with-disclosure approach on 2026-08-20** — facts derived
from partial/failed binding must be prefixed with a disclosure rather than silently presented as sound, and the
behaviour must be stress-tested. The design work below settles the mechanics; the approach itself is decided.

## The gap

Agents ask rig for help *right after editing*, which is exactly when the tree may not compile. Today that case
is silent at query time. The only signal is a per-index stderr line that scrolls past during `rig index` and is
never persisted:

```csharp
// src/Rig.Analysis/Inventory/SolutionSourceLoader.cs:212
ReportProgress(progress, $"Warning: {errorCount} compilation error(s) — analysis will be partial for affected files");
```

Three defects in that one path:

- the diagnostic's **file location is discarded** — the bag at `:174` holds `$"{project.Name}: {diagnostic}"`
  (`:246`);
- **nothing is persisted**, so a `rig reaches` an hour later cannot know the store came from a broken tree;
- `:247` `Console.WriteLine`s **every** error to raw stdout, uncapped — a firehose on a 226-project index that
  also corrupts stdout parsing of `rig index`.

Two failure classes are worse still, because they produce **no located diagnostic at all**: a project whose
compilation is unavailable (`:238-241`) contributes zero facts *and* zero `source_files` rows, and a source
generator that fails (`:1443`, `:1456`, `:1468`, `:1539`) drops its generated documents. Both now warn on the
progress channel — neither is recorded, so at query time "nothing declares this" and "this project didn't
build" are indistinguishable. Generator diagnostics are discarded outright (`:1502`, `diagnostics: out _`).

## Why HIGH

A dropped call edge reads as **"nothing calls this"** and a dropped dispatch edge reads as **"unreachable"** —
plausible, confident, wrong, and used as evidence for deletion. rig's identity is disclosing its own
approximations (`~heuristic` dispatch, the `reaches` "dispatch fan-out (NOT a real call)" bucket,
`SourceRenderer`'s dirty-store marker), so this is a **hole in an existing convention**, not new policy.
It is also a hard prerequisite for the resident-index program: `live-background-index` Slice 5 says the same
thing in one line — *"Silently answering about pre-edit code is the failure this program exists to remove."*

## Decided design (details + evidence in the spec)

- **Marker:** `~compile-error` per rendered line (placed exactly where `~heuristic` is —
  `ReachesCommand.cs:267`, `TreeRenderer.cs:246`), machine token `compile_error`, plus an **unconditional
  stderr footer note** (`EffectDerivation.cs:405-407` channel) whenever the store has any compile error or
  partial project.
- **Attaches to:** a **per-file flag in `source_files`** (`SourceFileEntity.cs` — already per-file, already
  written for every file incl. generated, already read at query time by `rig files --skipped`) plus a
  **per-run channel on `runs`** for the location-less failures. Joined at render time on `FilePath`, which
  every renderable fact already carries.
- **NOT the symbol id.** Prefixing a stored DocID changes the join key. `!:` already proves the cost: it
  needed a whole parallel simple-name index (`FactPathFinder.GraphIndex.cs:201-206`, built `:351-355`), a
  permanently-heuristic dispatch arm (`Dispatch.cs:551-560`), a documented "no mined edge by definition"
  (`Dispatch.cs:398-400`), and a bespoke prefix parser (`FactPathFinder.cs:1288`). A second prefix class
  would fragment the graph, the FTS tables, the `reachable.ContainsKey(EnclosingSymbolId)` effect join, and
  `impact`'s symbol-id diff — turning a disclosure into a recall regression.
- **Distinct from `!:`.** `!:` = "this one reference didn't bind" and is **routine**: measured on the current
  clean-commit MedDBase store, 13,642 refs across **879 of 12,093 files (7.3 %)**, of which 13,640 are
  `typeUse` and **zero** are invocations. `~compile-error` = "this FILE had ≥1 error diagnostic". Neither
  implies the other; never render one as the other.
- **Contamination scope: per-FILE, no propagation** (plus the per-project channel). Roslyn re-reports at every
  site where binding actually failed, so its diagnostic set is already the closure for *wrong* bindings —
  deleting `Domain.IBookingService.Book` errors in `ApiGateway/BookingController.cs`, not in the file that was
  edited. Propagating to dependents is measurably fatal (MedDBase transitive dependents: median 6, p90 68,
  max 164); propagating to siblings is too (two projects hold 66 % of the 2.44 M references).
- **Schema:** bump `SchemaVersion.Index` 5 → 6. A pre-v6 store read as "everything compiled" is the exact lie
  being removed, and a tri-state `unknown` would fire on every existing store. Cost: one re-index per store
  (~253 s on MedDBase).
- **Cache:** join at render time, bake into **no** cached blob → no existing `*Schema` bump. Add
  `DisclosureSchema = 1` folded into `DerivationSchemaToken()` so the web client can't serve pre-feature
  responses (the `FindingViewSchema` precedent). No MVID, no build timestamp.

## Web slice — IN SCOPE, not a follow-on

`/api/meta` gains a `compileErrors: { files, total, projects[] }` block; the tree/reaches/hazards/path/impact
DTOs gain `bindingHealth`, mirroring `SourceResponseDto.StoreDirty` (`SourceContracts.cs:33-37`); the SPA
renders a banner. Shipping CLI-only would make the web UI the surface that **hides** the disclosure — the same
defect as [web-api-seed-and-effect-disclosure-parity](web-api-seed-and-effect-disclosure-parity.md).

## Stress test — the point of the item, not a garnish

13 rows in the spec § 5. Highest-value five:

1. **Deleted member others still call** (DeepChain, delete `IBookingService.Book`) — the flagged file is a
   *different* file from the one edited; an implementation that flags "what I touched" fails only here.
2. **A file fails while its dependents do not** (body-local CS0103 in `Foundation/Db.cs`) — the
   must-not-cascade negative control: four dependent projects must stay unflagged and the answer must be
   byte-identical.
3. **broken → fixed → broken over one retained workspace** — does the disclosure **clear**? Catches a flag
   accumulated in a resident set that sticks forever (lies "safe", which is worse — it trains the reader to
   ignore the marker) and diagnostics memoized per path instead of recomputed.
4. **A project that fails entirely** (corrupt `Contracts.csproj`) — zero facts, zero `source_files` rows, no
   located diagnostic. Proves the project channel is not optional; a per-file-only implementation is silent.
5. **Ambiguous type across two assemblies** (new runtime fixture: a second `Contracts.PatientDto` in
   `Foundation`) — the one case that yields a **wrong** answer with nothing missing. Same
   duplicate-assembly-identity signature the spike already hit (43 errors, first `System.Object is not
   defined`), and the regression a resident workspace can reintroduce; assert on
   `ReferenceFact.TargetAssembly`/`TargetInSource`.

Hosts: `DeepChain` covers 10 of 13 rows and needs exactly one new fixture (written by the test into the temp
copy — **never** checked into `playgrounds/`, which `AnalyzedPlaygrounds` shares session-wide);
`LegacyNet48Web` covers both generator rows (real `ISourceGenerator`, `RequestResponseProxyGenerator.cs:12`,
analyzer-wired at `LegacyNet48Web.csproj:15-17`); `EntryPointEffects` hosts the noise arm. `CoreAllocations` is
deliberately unused (single project, no cross-project binding).

Trap: **do not call `TempPlayground.BuildAsync()`** in these tests — it asserts exit code 0
(`TempPlayground.cs:120`), so a deliberately-broken tree fails the fixture, not the assertion.

## The risk, and the calibration gate

**This design errs NOISY, and that is the thing to watch.** rig's design-time builds routinely error on
healthy trees — `SolutionSourceLoader.cs:205-210` says so in its own comment, and `--restore` is opt-in since
`eb6480ff` — and the measured `!:` floor is already 7.3 % of files on a clean commit. A chip on 7 %+ of lines
is above the ignore threshold, at which point the disclosure is *worse than nothing* because it launders the
genuinely-broken case.

Mitigations, in order: (1) **measure before shipping** — one MedDBase index, `rig files --compile-errors | wc
-l` plus the per-project histogram, recorded here; the chip does not go on by default until that number is
known; (2) a **project-saturation rollup** — above ~50 % of a project's indexed files (floor 10), suppress
per-line chips and emit one project-level footer line instead (threshold provisional, set it from the
histogram); (3) the real fix is **baseline-relative** firing in the resident tier — flag files whose error set
is *new* relative to the indexed commit, which is [live-background-index](../progress/live-background-index.md)
Slice 5 and is exactly what these columns are the baseline for.

Known false negatives, accepted and covered by the unconditional footer rather than by widening the scope: a
lost `DispatchFact` has no `FilePath` to chip (`DispatchFactEntity.cs`, `TypeRelationFactEntity.cs`);
location-less project failures have no rows; Roslyn's cascade suppression under-states site damage (INFERRED).

## Bonus this unlocks

The project channel gives the long-standing **ClientPage generator flake** a recorded diagnosis channel — a
generator run that fails today is invisible in the store, which is why
[flaky-clientpage-proxy-extraction](flaky-clientpage-proxy-extraction.md) can only be chased through test
flakiness. After this slice, `runs.PartialProjects` names it.

---

## ORCHESTRATOR CORRECTION 2026-08-20 — the "it will be noisy" finding is a CONFLATION; feature is unblocked

The spec concludes the disclosure "errs NOISY, decisively", citing **879 of 12,093 files (7.3%)** on a clean
MedDBase commit. That number measures files carrying at least one **`!:` partial-binding reference** — which is
a DIFFERENT condition from "this file has a Roslyn error diagnostic". `!:` is routine on healthy net48
cross-assembly code (and the spec's own breakdown says 13,640 of 13,642 are `typeUse`, with **zero
invocations**, so the condition is common but benign for reachability).

The condition this marker actually keys on is Roslyn error diagnostics. Measured directly on the live
MedDBase tree during the resident-workspace trial (2026-08-20, warm dtb cache, `--restore` off):

```
[cold] Warning: 3 compilation error(s) — analysis will be partial for affected files
```

**3 files, not 879 — 0.025%, not 7.3%.** Two orders of magnitude below the ignore threshold.

The deeper problem is that the noisiness question was posed against the wrong tree state. The feature exists
for the case where the tree does NOT compile (an agent mid-edit). Framed correctly:

- **Clean tree → effectively silent** (3 files on the largest real target we have).
- **Broken tree → fires in proportion to the actual breakage**, which is the INTENT. That is signal, not noise;
  a marker that stayed quiet while the tree was broken would be the bug.

So: the design ships as specified. The spec's proposed mitigations are re-ranked accordingly —
**measure-before-shipping is already satisfied at 3**; the project-saturation rollup is **deferred, not needed
for v1** (revisit only if a real broken-tree measurement exceeds ~50% of a project's files); baseline-relative
firing stays a Slice 5 concern.

Everything else in the spec is ACCEPTED as designed, in particular:
- `~compile-error` per rendered line, placed exactly where `~heuristic` sits, plus an unconditional stderr
  footer for location-less failures.
- The flag lives on `source_files` (per-file) + a per-run channel — **never on the symbol id**. The `!:`
  precedent is decisive: prefixing a DocID needed a parallel simple-name index, a permanently-heuristic
  dispatch arm and a bespoke prefix parser, and a second prefix class would break `impact`'s symbol-id diff
  and the `reachable.ContainsKey(EnclosingSymbolId)` effect join. Fatal, not awkward.
- Per-FILE contamination scope keyed on Roslyn's own diagnostic locations, on the grounds that Roslyn
  re-reports at every site where binding actually failed — so its diagnostic set already IS the closure. No
  propagation to dependents.

Two by-product bugs the spec found, to fix in the same slice: `SolutionSourceLoader.cs:247` writes every error
diagnostic to raw stdout uncapped, and `:1502` (`diagnostics: out _`) discards generator diagnostics entirely.
The latter also gives the ClientPage generator flake its first real diagnosis channel.
