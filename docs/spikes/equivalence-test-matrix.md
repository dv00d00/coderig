# Equivalence test matrix — incremental extraction vs cold full index

Spec only, no code. Written against `live-background-index` @ 2026-08-20, grounded in
`tests/Rig.Tests/Analysis/IncrementalExtractionSpikeTests.cs` (the one proven shape) and the
playgrounds actually on disk (`playgrounds/DeepChain`, `playgrounds/LegacyNet48Web`,
`playgrounds/EntryPointEffects`).

## What the existing test proves, precisely

`IncrementalExtractionSpikeTests.Incremental_reextraction_over_retained_workspace_matches_cold_full_index`
(`tests/Rig.Tests/Analysis/IncrementalExtractionSpikeTests.cs:20-123`) does exactly one edit: insert a
new statement `Foundation.Db.Query("audit: booking attempt");` before an existing line in
`Business/BookingService.cs` (line 56-63). That is:

- **additive** (no line deleted, no signature touched)
- **body-local** (inside an existing method, no new member)
- **crosses a project boundary Business didn't have a direct `ProjectReference` to** (Foundation, only
  transitively via Contracts→DataAccess) — the one dimension it exercises well
- in a **leaf-ish** project (`Business` has exactly one direct dependent, `ApiGateway`)

It does NOT touch: any signature, any type relation (interface/base), any file add/delete, any
partial type, any generator, any hub (>2 dependents) project, any `.csproj`/`.props`, sequencing, or
revert. Every row below is a gap this test structurally cannot see.

## The matrix

| # | Shape | Where (playground / project / file) | Expected fact delta (one sentence) | What it catches that the existing test cannot | Playground work needed | Priority |
|---|---|---|---|---|---|---|
| 1 | Body-only edit, no signature change (negative control for the surface-hash gate) | DeepChain `DataAccess/PatientRepository.cs`, inside `GetById` | Zero `SymbolFact`/`TypeRelationFact`/`DispatchFact` delta anywhere except `BodyHash` + line-shift on the edited symbol; no new/changed `ReferenceFact` outside the file | That the Slice-4 surface-hash gate does NOT cascade a re-extract to `DataAccess`'s dependents (`Business`, then `ApiGateway`, `Web`) — this is the *must-not-fire* case; today's test never exercises "the gate says skip" | None — file exists | **P1** |
| 2 | Add a public method to an existing type | DeepChain `Business/BookingService.cs`: add `public string Cancel(int patientId)` | New `SymbolFact` (new `M:` DocID) + new `ReferenceFact`s inside it only; zero delta to any OTHER file's facts | Surface-hash gate must cascade here (public surface grew) even though the edit is still one file — distinguishes "body changed" from "surface changed" for a same-file edit | None | P2 |
| 3 | Remove a public method others call | DeepChain: delete `BookingController.Book` while `Web/HomePage.cs` still calls it | Tree does not compile — see row 7 for the product question this forces | Same as row 7 | None | fold into 7 |
| 4 | Rename a public method | DeepChain `Domain/IBookingService.cs` + `Business/BookingService.cs`: `Book` → `BookAppointment` (rename both interface member and impl) | Old `M:…Book(System.Int32)` `SymbolFact` disappears, new `M:…BookAppointment(System.Int32)` appears; every call site's `ReferenceFact.TargetSymbolId` in `ApiGateway/BookingController.cs` and `Web/HomePage.cs` changes; `DispatchFact` (impl edge) re-keys | Whether Roslyn's own invalidation (dependency-shaped, not surface-shaped — see progress doc "CORRECTED" section) actually re-binds every *caller's* `ReferenceFact`, or whether a caller re-extracted from a stale cached `SemanticModel` still resolves the OLD DocID. This is the single most direct test of the "stale-binding bug" the task brief warns about | None | **P1** |
| 5 | Change a parameter type | DeepChain `Contracts/IPatientRepository.cs` + `DataAccess/PatientRepository.cs`: `GetById(int id)` → `GetById(long id)` | `SymbolFact.SymbolId` changes (DocID embeds param types) on both interface and impl; `DispatchFact` re-keys; `ReferenceFact.TargetSymbolId` at the one call site in `Business/BookingService.cs` changes; `Business.Book`'s own call to the old int-arg `GetById` must NOT still resolve (would be a stale-binding false negative) | Cross-project signature change propagating through an INTERFACE, not just a concrete method — exercises dispatch-fact re-keying, which row 4 (same-project rename) does not | None | P2 |
| 6 | Change a return type only | DeepChain `Foundation/Db.cs`: `Query(string sql)` → returns `Result<string>` instead of `string` (the `Result<T>` struct already exists in `Foundation/Db.cs:11-16` for exactly this) | `SymbolFact.SymbolId` (DocID) is **unchanged** — Roslyn method DocIDs do not encode return type — but `Signature` and `BodyHash` change; the one caller (`DataAccess/PatientRepository.cs`) now has a type error unless also edited | **The comparison function's current blind spot**: `CanonicalFacts` (`IncrementalExtractionSpikeTests.cs:147-179`) never emits `Signature`. Two runs that differ ONLY in return type currently look different solely because `BodyHash` happens to hash the whole declaration span — an accident, not a designed signal. See "Comparison function" below | None | **P1** |
| 7 | Delete the last member of a type / delete a whole type others reference (non-compiling tree) | DeepChain: delete `Contracts/IPatientRepository.cs` entirely (or just its one member) while `DataAccess/PatientRepository.cs` and `Business/BookingService.cs` still reference it | Compilation errors in ≥2 downstream projects; Roslyn still produces a `Compilation` (it always does) but many `ReferenceFact`s bind to `!:`-prefixed "candidate" DocIDs (see `FactExtractor.cs:142` — "partial binding (`!:` DocIDs)") instead of resolving | **This is the case that matters most in practice** (an agent mid-edit asks rig about code that doesn't compile yet) and it is completely unexercised today. See "Non-compiling tree" callout below — flagged for the human, not decided here | None | **P1** |
| 8 | Add an overload | LegacyNet48Web `Dispatch/DispatchZoo.cs`: add a THIRD `Register` overload to `IDispatchWorkflows`/`WorkflowRegistry` (same file already models same-arity-overload dispatch, `DispatchZoo.cs:14-53`) | New `SymbolFact`, new `DispatchFact` (impl edge for the new overload only); existing `WorkflowCaller.RegisterController`'s `ReferenceFact.TargetSymbolId` must NOT move to the new overload | Whether adding a sibling overload perturbs an unrelated EXISTING call site's overload-resolution binding — this is a real name+arity-CHA failure mode the repo already documents (`DispatchZoo.cs:6-8`, "the real-world `IWorkflows.Register` bug") | Single-project only today (no cross-project overload set) — good enough to prove the mechanic; a cross-project variant would need DeepChain to grow an overload, which it currently has none of | P2 |
| 9 | Add an interface implementation | DeepChain: make `Business/BookingService` additionally implement a new `IAuditable` interface (add the interface + one trivial member) | New `TypeRelationFact` (`RelationKind` = implements); new `SymbolFact`+`DispatchFact` for the new member; NO other file's `ReferenceFact`s should change unless something already calls through `IAuditable` | `TypeRelationFact` carries no `FilePath` (`TypeRelationFactEntity.cs:3-10`) — this is the first shape where the SET-comparison and the LOCALITY view can disagree (see "Comparison function" §2) | None | P2 |
| 10 | Remove an interface implementation | DeepChain: remove `Domain.IBookingService` from `Business/BookingService`'s base list (keep the method, now a plain concrete method) | `TypeRelationFact` disappears; the ONE `DispatchFact` (impl edge) for `Book` disappears; callers going through `IBookingService` (`ApiGateway/BookingController._bookings`) now fail to compile — this shape is a special case of row 7 unless the caller is also fixed | Whether losing a `DispatchFact` is detected when the removed relation is exactly the one the reachability chain in the spike's own baseline assertions depends on (`IncrementalExtractionSpikeTests.cs:37-41`) | None | P2 |
| 11 | Change a base class | Needs a CLASS (not interface) hierarchy with ≥2 levels — DeepChain has none (every relation in DeepChain is interface-based). `LegacyNet48Web/Dispatch/DispatchZoo.cs:82-95` already has `AlertBase`→`EmailAlert`→`PagerAlert`; re-target `PagerAlert : EmailAlert` to `PagerAlert : AlertBase` directly (skip a level) | `TypeRelationFact` (base-class edge) changes; `DispatchFact` override-chain edge re-keys (`PagerAlert.Raise` now overrides `AlertBase.Raise` directly, not `EmailAlert.Raise`); `AlertCaller.Fire`'s dispatch fan-out set is unchanged (still both overrides reachable from the base) but the EDGE STRUCTURE changed | Override-chain re-keying on a skip-level reparent — a shape name+arity CHA and the one-hop dispatch rule (progress doc / CLAUDE.md "Dispatch is ONE HOP") both have to get right; also single-project only in LegacyNet48Web, so doesn't prove CROSS-project base-class change | Cross-project variant needs a new 2-project fixture (base class in one project, derived in another) — not present anywhere today | P3 |
| 12 | Add a whole new file | DeepChain: add `DataAccess/PatientAuditRepository.cs`, a new class, no callers yet | New `SymbolFact`s + `ReferenceFact`s confined to the new file; the `ChangedFiles` locality helper (`IncrementalExtractionSpikeTests.cs:183-221`) must report exactly that one file, with a BEFORE-set of nothing for it | Whether `RigWorkspace`'s document-ADD path (not `WithDocumentText` — a genuinely new document, `AddDocument`) behaves the same as a cold reload; the shipped spike only ever edits an EXISTING document | None (creating the file IS the edit) | **P1** |
| 13 | Delete a whole file | DeepChain: delete `Web/HomePage.cs` (the sole entry point) — leaves `Web` project with zero types | All `SymbolFact`/`ReferenceFact` rows whose `FilePath` was that file disappear; zero rows should appear for ANY other file | Whether a `RemoveDocument` (not just edited-to-empty) is handled identically incrementally vs cold — and, since this is also the entry point, whether `rig entrypoints`/reachability derivation (query-side, not this test's concern) degrades gracefully. NOTE: CLAUDE.md's `RemoveProject` warning (never remove a REFERENCED project — dangling ref drops silently) is a distinct, sharper hazard than removing a leaf file; call it out separately, don't conflate | None | P2 |
| 14 | Rename a file (no content change) | DeepChain: `Business/BookingService.cs` → `Business/BookingServiceImpl.cs`, byte-identical content | Zero fact delta except `FilePath` on every `sym`/`ref`/`alloc` row that was in that file | Whether rig's incremental path treats a rename as delete+add (correct) or leaves stale facts keyed to the old path (the failure mode) — this is a pure loader-mechanics test, not an extraction-correctness one, so it is really testing `RigWorkspace`/`OnDocumentTextChanged` plumbing (Slice 1) more than `FactExtractor` | None | P3 |
| 15 | Edit inside a `partial` type split across two files | **No playground has a partial type today.** Needs: split `Business/BookingService` into `BookingService.cs` (ctor + `Book`) and `BookingService.Audit.cs` (a new partial method/member), OR add a fresh 2-file partial fixture | Editing file B of the pair must produce the SAME `SymbolFact` for the type (one `SymbolId`, unchanged `ContainingSymbolId` linkage) with `FilePath`/`Line` pinned to whichever file declares which member; `ChangedFiles` locality must report ONLY file B, even though the type's declaration technically spans two files | This is the sharpest test of the per-FILE (not per-type, not per-project) overlay design (Slice 3's own framing, progress doc line ~124-127): does re-extracting file B in isolation still correctly resolve members declared in file A of the same partial type? A retained-`Solution` re-extraction that is secretly type-scoped instead of file-scoped would pass every other row and fail only here | **Needs a new fixture file** — cheapest gap to close (add one file to DeepChain) | **P1** |
| 16 | Edit that changes a source-generated result | LegacyNet48Web: add a new `ClientPage`-derived class (mirroring `Pages/Proxies/InvoiceEditProxy.cs`) so `ProxyGenerator/RequestResponseProxyGenerator.cs` emits two new generated files (`*_RequestProxy.g.cs`, `*_ResponseProxy.g.cs`) | New generated `SymbolFact`s (the generated proxy type + its `Show`/`ShowDialog`/`Redirect` members) that don't correspond to any file the incremental edit touched directly | Whether the incremental path re-runs `RunSourceGeneratorsAsync` at all after `WithDocumentText`, and whether it does so exactly once (progress doc: "generators run TWICE per project with zero incrementality" today, "blanket `catch -> []`" silent-failure hazard flagged as pre-work). This is the one shape where "which file changed" is a **generator input**, not the generated output — locality assertion must be written in terms of the SOURCE file, not the `.g.cs` output | None — `LegacyNet48Web` + `ProxyGenerator` already wired exactly for this (`LegacyNet48Web.csproj:15-17`, analyzer-referenced) | **P1** |
| 17 | Edit to a file in a HUB project (many dependents) vs a leaf project | **No playground has a hub today.** DeepChain's max direct fan-in is 2 (`Contracts` ← `DataAccess`, `Domain`; confirmed by reading all 7 `.csproj` files — every other project has exactly one incoming `ProjectReference`). Needs either (a) grow DeepChain with N new leaf projects all referencing `Contracts` (or `Foundation`), or (b) a dedicated hub fixture sized to reach the real p90/max the progress doc measured (68 / 164 transitive dependents) | A body-only edit to the hub file must (per row 1's gate) touch ONLY that file's facts even though Roslyn's OWN dependency-shaped invalidation would re-bind every dependent's compilation; a SIGNATURE edit to the hub file must cascade to every one of the N dependents' `ReferenceFact`s | This is the scenario the whole progress doc says the surface-hash gate exists to survive ("a hub edit that changes only a method BODY must not cascade at all... The gate is not a nice-to-have"). Nothing today exercises fan-out > 2 | **Real gap** — needs either an enlarged DeepChain (cheap: N trivial leaf `.csproj` + one-line callers, mechanical) or a purpose-built `HubChain` playground | **P1** |
| 18 | `.csproj` / imported `.props` change | DeepChain: add a `Directory.Build.props` at `playgrounds/DeepChain/` (none exists today) defining e.g. a shared `<LangVersion>`, then bump it; separately, edit `DataAccess/DataAccess.csproj` to add then remove a `ProjectReference` | A `.csproj`/`.props` change is explicitly OUT of the incremental path per the SLO ("a full re-index happens only when a `.csproj` or an imported `.props`/`.targets` changes") — so the acceptance criterion is NOT fact-equivalence via the incremental path, it's that `RigWorkspace.OnProjectReloaded` (Slice 1, not yet built) is correctly detected as the trigger to fall back to a full/project-scoped rebuild rather than silently serving stale facts | Confirms the FALLBACK boundary is real and detected, not that incremental facts match (they're not supposed to be produced this way at all). Wrong result here is a SILENT fallback failure — the disclosure obligation in CLAUDE.md's "Constraints any design must respect" | None | P2 |
| 19 | Two edits in sequence, no intervening cold index | DeepChain: edit 1 = row 2 (add `Cancel`), edit 2 = a further edit to the SAME file or a different one, both applied via `WithDocumentText` on the SAME retained `Solution` before any re-extract of edit 2 | `F2` (after both edits, incremental) must equal a cold index of the tree with BOTH edits applied; `F1`-only facts (from edit 1, if edit 2 doesn't touch that member) must still be present in `F2` — i.e., overlay accumulation doesn't drop or duplicate | Whether resident-process state drifts over N edits — duplicate facts from a naive append, or facts from edit 1 getting silently dropped when edit 2's re-extraction re-touches the same file. Directly tests the overlay model's "facts for dirty files" bookkeeping (Slice 3), which a single-edit test cannot expose by construction | None | **P1** |
| 20 | An edit that is then reverted | DeepChain: apply row 2's edit, re-extract, then `WithDocumentText` back to the ORIGINAL text, re-extract again | `F2` (post-revert) must be SET-EQUAL to `F0` (pre-edit) — not just "close", exactly equal, same `BodyHash`, same `Line`/`EndLine` (no residual line-shift, no leaked synthetic ids from the intermediate state) | The property test of the whole model: overlay state has no memory beyond current document text. Catches ID-allocation-by-side-effect bugs (e.g., a synthetic lambda id like `~λN` that increments an ordinal counter rather than being purely a function of tree shape) that a single-edit test cannot reveal because there's nothing to revert TO within the test | None | P2 |

### Non-compiling tree — flagged for the human, not decided here

Row 7 is, per the task brief, arguably the most important real-world case: an agent asks rig about a
tree it just broke and hasn't fixed yet. Roslyn will still produce a `Compilation` — it never refuses —
but bindings that used to resolve will come back as Roslyn's own "error" symbols, and
`FactExtractor.cs:142` already has a documented path for this: a partial/candidate binding surfaces as a
`!:`-prefixed DocID rather than the normal `M:`/`T:`/etc. prefix. So the MACHINERY has a hook already;
what's undecided is **product behavior**:

- Does rig answer with whatever partially resolves (some call edges present, others silently absent —
  today's presumed behavior, and it fails the disclosure principle in CLAUDE.md: "whatever ships must
  disclose which tree state produced an answer")?
- Does rig detect the tree doesn't compile and say so explicitly before answering (extra machinery: check
  `Compilation.GetDiagnostics()` severity, surface a "N files have compile errors, answer may be
  incomplete" banner)?
- Does the SAME `!:`-prefixed-DocID disclosure convention already used for candidate bindings extend
  naturally here, or does a non-compiling tree need a different signal because the volume of `!:` edges
  is qualitatively different from today's rare partial-binding case?

This is a product/UX decision (what rig SHOULDS say), not a test-design decision — raising it rather
than picking one.

## 1. The comparison function

**Not sufficient as-is for several rows above.** Concretely:

- **Add `Signature` to the `sym` canonical line.** Row 6 (return-type-only change) is invisible to
  `SymbolId` (Roslyn method DocIDs never encode return type) and is currently caught only as a side
  effect of `BodyHash` hashing the full declaration span (`FactExtractor.cs:1122-1124`) — which is an
  accident of implementation, not a designed signal, and would silently stop catching this class of edit
  the moment `BodyHashOf` is scoped to just the body block instead of the whole node span (a change
  someone could make for an unrelated reason). `Signature` is the field whose whole job is to carry this.
- **Add `Modifiers` and `IsOverride`.** Neither is in `CanonicalFacts` today (`IncrementalExtractionSpikeTests.cs:151-153`).
  A `sealed`/`virtual`/`override` keyword flip on an unrelated member changes reachability-relevant
  semantics (progress doc's own dispatch-fact machinery keys off exactly this) without moving `SymbolId`,
  `FilePath`, or line range.
- **Add `TargetAssembly` and `TargetInSource` to the `ref` canonical line.** This is the highest-value
  addition. These two `ReferenceFact` fields (`ReferenceFactEntity.cs:10-11`) are precisely what the
  spike's OWN prior investigation flagged as the failure signature of a duplicate-assembly-identity bug
  ("doc's model" arm: 43 errors, first `System.Object is not defined`, progress doc "What the spike
  killed" table). The current canonical `ref` line (`TargetSymbolId`/`RefKind`/`EnclosingSymbolId`/
  `FilePath`/`Line`) would show IDENTICAL strings for a reference that silently rebound to a same-named
  symbol in a SECOND assembly identity, because `TargetSymbolId` alone doesn't disambiguate assembly. A
  resident workspace that regresses into exactly that bug would sail through today's comparison.
- **`DefiningAssembly` on `sym`** for the same reason, symmetric case.
- `ReceiverType` and `NonVirtual` (both on `ReferenceFact`) are worth adding specifically for rows 9-11
  (interface/base changes) — they're the fields that would reveal a dispatch-narrowing regression that
  doesn't change `TargetSymbolId` itself.

**`TypeRelationFact`/`DispatchFact` carry no `FilePath`** (confirmed: `TypeRelationFactEntity.cs:3-10`,
`DispatchFactEntity.cs:6-13`), which the spike report already flagged as a splice blocker. Concretely for
this matrix: the `ChangedFiles` locality helper (`IncrementalExtractionSpikeTests.cs:183-221`) explicitly
**skips** `rel`/`disp` lines when building its per-file view (`:190-193`, `"no FilePath on these fact
kinds"`). That means:

- The SET-comparison (`onlyIncremental`/`onlyCold`, the actual pass/fail assertion) still sees `rel`/`disp`
  facts and would catch a WRONG interface/base/dispatch edge.
- But the LOCALITY assertion — the per-file overlay claim, which is the entire architectural point of
  Slice 3 — is **blind** to which file a `rel`/`disp` change came from. Rows 9-11 (interface add/remove,
  base class change) are exactly the shapes whose primary fact delta lives in these two kinds. If such an
  edit incidentally perturbed a `TypeRelationFact`/`DispatchFact` "belonging" to some OTHER file (plausible
  under whole-program CHA, which is explicitly not per-file), the locality assertion would report "no
  files changed outside the edit" and be simply WRONG — not because the assertion failed, but because it
  structurally cannot see the fact kind that regressed. For rows 9-11, the locality claim can only be
  made honestly by a DIFFERENT signal: e.g., asserting the full `rel`/`disp` SET rather than a per-file
  partition, or tracking file membership by JOINING `TypeSymbolId`/`SourceMember` back to the `sym` row
  that owns that DocID's `FilePath`. Spec that join explicitly rather than pretend the existing helper
  covers it.

## 2. The playground requirement

| Playground | Can host as-is | Needs additions | Verdict |
|---|---|---|---|
| `playgrounds/DeepChain` (7 projects, chain-shaped, confirmed via all 7 `.csproj`: `Web→ApiGateway→Business→{Domain,DataAccess}→Contracts→Foundation`) | Rows 1, 2, 4, 5, 6, 7, 9, 10, 12, 13, 14, 18, 19, 20 | Row 15 (partial type — add one file), row 17 (hub — needs N new leaf projects; current max direct fan-in is 2, at `Contracts`) | Workhorse; cheap, mechanical additions cover most gaps |
| `playgrounds/LegacyNet48Web` (`LegacyNet48Web.csproj` + `ProxyGenerator/ProxyGenerator.csproj` referenced as `OutputItemType="Analyzer"`, confirmed `.csproj:15-17`) | Row 8 (overload — `Dispatch/DispatchZoo.cs:14-53` already has the exact same-arity-overload fixture), row 11 within-project (`AlertBase`/`EmailAlert`/`PagerAlert`, `DispatchZoo.cs:82-105`), row 16 (source generator — confirmed real `[Generator] : ISourceGenerator`, `RequestResponseProxyGenerator.cs:12`, wired via `Compile Remove` + Analyzer reference) | Row 11's CROSS-project variant (base class in one project, derived in another) — `LegacyNet48Web` is functionally one project (the generator project doesn't emit a referenceable assembly) | Confirmed: **yes, has a source generator**, exactly as the task brief asked to check |
| `playgrounds/EntryPointEffects` (single project `EntryPointEffects.Api`, has `Generated/GeneratedEndpoint.g.cs`) | Nothing extra for this matrix | N/A | Not used above — its `.g.cs` file is checked in directly, not emitted by a wired analyzer/generator project (no generator `.csproj` found under `EntryPointEffects/`), so it does NOT exercise the generator re-run question the way `LegacyNet48Web` does. Don't use it for row 16. |
| A NEW fixture | — | Row 17's hub, if not solved by extending DeepChain; row 11's cross-project base-class variant | Only truly missing shapes; both are additive, not architecturally new work |

## 3. One parameterized test vs separate tests

Given TUnit and the rule that agent-authored tests go in a NEW file (never
`tests/Rig.Tests/CliApplicationTests.cs`):

- **One file, `IncrementalExtractionEquivalenceTests.cs`** (new — do not add to the shipped spike file,
  which stays as the historical GATE record per its own doc comment).
- **Parameterize rows that share the SAME playground, SAME arms (incremental vs cold), and SAME
  comparison shape** — i.e., rows 1, 2, 4, 5, 6, 9, 10 all run: cold F0 on DeepChain → apply one text edit
  → incremental F1 vs cold F1 → assert set-equal + locality. That's one `[Test]` with a
  `[MethodDataSource]`/`[Arguments]` table of (editedFile, editDescription, editFunc, expectedChangedFiles)
  — TUnit's data-driven attributes fit this exactly, and it keeps the diagnostic report format
  (`Console.WriteLine` of the symmetric diff, per the existing test's pattern) identical across rows.
- **Keep SEPARATE, non-parameterized tests for:**
  - Row 7 (non-compiling tree) — the assertion shape is entirely different (no fact-equality target
    exists; assert on `!:`-prefixed DocIDs / diagnostic surfacing instead).
  - Row 12/13 (add/delete a whole file) — different Roslyn API (`AddDocument`/`RemoveDocument`, not
    `WithDocumentText`), so the "arrange" step doesn't fit the shared harness.
  - Row 15 (partial type) — the locality assertion needs bespoke wording ("only file B changed" while
    file A's facts for the SAME type must still be present and unchanged) that doesn't fit the generic
    "changedFiles == [oneFile]" shape.
  - Row 16 (source generator) — different playground (`LegacyNet48Web`), different arms (must also assert
    generator ran exactly once / diagnose the double-run), doesn't share DeepChain's harness at all.
  - Row 17 (hub) — needs its own playground/fixture size and a SEPARATE assertion axis (body-edit arm:
    zero cascade; signature-edit arm: full cascade) — two arms per edit, not one.
  - Row 18 (`.csproj`/`.props`) — asserts fallback-triggered, not fact-equality; wrong shape for the shared
    harness.
  - Row 19 (sequence) and row 20 (revert) — each needs multiple sequential re-extractions over the SAME
    retained `Solution` object across the test body, which the parameterized single-edit harness doesn't
    support without restructuring it into a list-of-edits shape. Could eventually fold into a "sequence of
    N edits, assert final state" harness that subsumes both the single-edit rows AND 19/20, but that is a
    harness-design decision for whoever builds Slice 3's tests, not decided here.

So: **1 parameterized test covering rows {1,2,4,5,6,9,10}, 8 separate tests for the rest** (7, 8, 11, 12,
13, 15, 16, 17-hub, 18, 19, 20 — several of which may also be a small parameterized pair, e.g. 12+13 as
add/delete variants of one "whole-file" test, and 19+20 as two arms of one "multi-edit sequence" test).

## 4. Priority order — if only 4 ship

1. **Row 1 — body-only edit must not cascade.** This is the negative control the ENTIRE surface-hash gate
   (Slice 4) exists to satisfy; without it there's no evidence the gate does what it's for, only that it
   compiles.
2. **Row 4 — rename a method used across ≥2 dependent files.** This is the most direct test of the
   "stale-binding bug" the task brief opens with — a caller re-extracted from a workspace that hasn't
   actually re-bound the rename would keep resolving the OLD DocID, which is exactly the failure this whole
   program is built to avoid and is silent (no exception, just a wrong-but-plausible answer).
3. **Row 17 — hub edit, body vs signature.** Nothing today exercises fan-out beyond 2; the progress doc's
   own measured distribution (p90 68, max 164 transitive dependents, 23% of projects at 51+) says this is
   where the SLO is actually won or lost, not the common case DeepChain already models well.
4. **Row 15 — partial type split across two files.** Sharpest available test of whether the overlay is
   truly per-FILE (the architecture's own stated design point) rather than accidentally per-type or
   per-symbol; cheapest gap to close (one new fixture file) for the highest-value distinction it draws.

Runner-up, only bumped out by being slightly less novel relative to what row 4 already covers: row 6
(return-type-only change), because it's the one row that exposes today's comparison function is
currently RIGHT BY ACCIDENT rather than by design (see §1) — worth fixing the harness for even before
adding more rows.
