# The file lens showed only the first derivative, and had no filters at all

**Status:** done (slices A and C) · **Completed:** 2026-09-01 · Remaining provider-grain and lazy-witness work
was split into independent backlog cards on 2026-09-02. ·
**Family:** file lens

## What was wrong

Two gaps, both of which made the overlay quieter than the truth.

1. **Only the first derivative rendered.** rig's findings catalog is explicitly tiered
   (`HazardKinds`): tier 1 hazards, tier 2 `looped_effect` (AMPLIFICATION — the effect runs once per
   iteration), tier 3 `cross_method_amplification` (the loop and the I/O are in different frames). The lens
   showed none of them, so an effect executed 500 times per request rendered identically to one executed
   once. Tier 2 was the galling case: it already rode on `DerivedEffect.Observations`, into the read model,
   and was thrown away there.
2. **`/api/file-effects` took `file` and `store` and nothing else** — no filter surface, while every sibling
   endpoint has one (`/api/reaches` `async`/`intrinsic`, `/api/hotspots` `sort`/`top`/`noLambdas`,
   `/api/hazards` `amplification`/`crossMethod`, `/api/effects-diff` `only`). `rig annotate` had none either.
   On a file like `WriteDischargeDetail.cs` (44 methods, 131 marked lines) there was no way to ask a
   narrower question.

## The decision that shaped the fix

**Filters are a predicate over the PROJECTION, not a change to the DERIVATION.** The solution-wide reverse
closure is keyed on (store, rules, schema) and costs ~47s cold on MedDBase; a filter that entered that key
would re-pay it per combination. Filtering after the merge instead makes arbitrary client-driven combinations
free, and gives a property worth more than the speed: **a surviving badge's number is identical to its
unfiltered value**, so two views of one file can never disagree.

Two flags deliberately did NOT get this treatment, because they genuinely change the closure and each needs
its own cache key: `--intrinsic` (adds alloc/throw — ~91% of all effects, a far larger artifact) and
`--async` (a different reverse edge set). `--max-depth` LOOKS like a closure bound but is exact as a
predicate: nearest-depth is monotone, so filtering `<= N` off an unbounded closure equals bounding the walk.

## Delivered — slice A

- `FileEffectAggregate.Looped` carries the amplification tier, stamped at **depth 0 only** and at two grains:
  a LINE is looped when the effect sits on it and that site is inside a loop (keyed on enclosing method +
  line), a METHOD is looped when its own body performs one. Propagating it up the reverse closure was
  rejected — that answers the weaker "something looped exists somewhere below", which is tier 3's job, with
  a witness and a confidence. Both merges (`Best`, `FileEffectLens.Merge`) OR repetition only across the rows
  at the WINNING distance, so a looped row further away cannot lend its mark to a nearer row that runs once.
- Badge grammar is now family + distance + repetition + basis: `db!`, `db!*`, `db:5`, `db:5?`. Documented in
  one place (`FileEffectLens.LensBadge.Label`) so terminal, browser and editor read identically.
- `FileEffectLens.LensFilter` (`only`/`exclude`/`min-depth`/`max-depth`/`direct`/`looped`/`no-dispatch`) plus
  `LensFilterDisclosure`, wired to `rig annotate` flags and to `/api/file-effects` query params.
- `FileEffectFilterTokens` resolves the provider-grain vocabulary the rest of rig speaks onto the lens's
  family grain, and REPORTS every widening (`--only llblgen` → family `db`, naming the nine sibling providers
  it also matched) and every unknown token.
- Disclosure is mandatory and lives in the HEADER, above the data (`--summary` renders no footer, and a
  caveat that arrives after the table has been read is not a caveat): `FILTERED: 176 badge(s) hidden, 0
  method row(s) and 1 line(s) dropped — this is NOT the whole file`.
- The web response's SITE rows are the raw read model (the client merges per line), so they are filtered by
  **what survived on that LINE** rather than badge-by-badge — otherwise `minDepth` would resurrect a distance
  the merged line badge had already lost and the browser would render a number the terminal does not.
- `FileEffectsSchema` 3 → 4.

### A defect the real store caught, that the unit tests would not have

`--only nosuchthing` printed "matches nothing in this store" and then rendered **all 20 methods**: the
resolver returned an empty family set, and the filter read empty as "no `--only` given". NULL now means the
reader did not ask and EMPTY means the reader asked and nothing resolved — an unresolvable `--only` matches
nothing. Pinned by `An_only_that_resolves_to_nothing_matches_nothing`.

### Real-store confirmation

```
CompanyToChamber.cs
  221  CreatePersonStatusRecords  cache:6? db!* echo:6? io:12 rpc:12?
  245  CopyBankHoliday            cache:5? db!  echo:5? io:11 rpc:11?    # sibling, not looped
  344  FindAppointmentTypePrimaryKeys  cache!* db:3 io:13
  567  CopyRoles                  cache:2? db!* echo:8? io:11 rpc:10?

  CopyRoles, line grain:
    572          foreach (var srcRole in roles)
    db!*  587              destRole.Save();
    db!*  590              memberProfiles.Fill(0, null, true, …);
```

A save and a fetch per role, with a nested loop at 594 — a genuine n+1 that the pre-change overlay rendered
as two ordinary `db!` lines. `--looped --summary` reduces that file from 20 method rows to the 4 that repeat.

Tests: `FileEffectAmplificationTests` (5, incl. the negative "a distant looped effect never marks the calling
method"), `FileEffectLensFilterTests` (13). Full suite 1332/1332.

## Extracted follow-up — provider grain

The independently shippable provider-grain design and its 66-label calibration gate now live in
[file-lens-provider-grain](../todo/file-lens-grain-2-provider-grain.md). This completed delivery record no longer owns
that open scope.

## Delivered — slice C (tiers 1-3 are real data)

The UI slice shipped tier 1-3 rendering against a checked-in fixture (`filelens-findings.mock.json`, 404 KB of
reshaped `rig derive` rows). That fixture was line-anchored, valid for exactly one store, and — because
`PackAsTool` packs the PUBLISH OUTPUT — it shipped inside the installed tool, where `Pack="false"` cannot stop
it. It is now deleted, along with its regeneration script and the store-identity gate that made it safe:

- **`FileFindingsQueryService`** derives tiers 1-3 for ONE file, reusing the artifacts `derive` and
  `/api/hazards` already share (`LoadOrDeriveHazardEffectsAsync` on disk, graph + invocations in the resident
  `WarmStore`). Cheaper than `/api/hazards`, which must build a call tree first just to know which methods to
  keep — here the filter is the file path.
- One decision worth stating: tier 3 is derived over the WHOLE effect set and filtered on the ANCHOR, never on
  the input. The anchor is in this file but its witness is by definition in another frame, usually another
  file, so filtering the input first would delete exactly the evidence the tier exists to find.
- **`GET /api/file-findings?file=`**, separate from `/api/file-effects` on purpose: a different derivation with
  a different cost, fetched in parallel so the badges and the source never wait on it, and a findings failure
  degrades to "no marks" rather than "no lens".
- `Findings.CrossMethodDerived` records whether tier 3 RAN. An empty anchor list otherwise means two different
  things — nothing found here, or no `crossMethodAmplification` section in the rules — and the overlay must be
  able to say `TIER 3 OFF` rather than let silence read as safety.
- `FileEffectsEndpoint.ToFindingsResponse` is extracted and pinned by `FileFindingsWebContractTests` (4 tests).
  The reason is the rename: the finding record calls them `Reason` and `Context`, the wire calls them `subtype`
  and `key`, and swapping those two would be invisible in review (both short lowercase strings) while making
  every tooltip in the overlay wrong.

Measured on the MedDBase store, `CompanyToChamber.cs`: **3 hazards, 11 amplifications, 3 anchors** — the same
counts the fixture carried, derived independently. Cold 71s (the hazard pass), warm **2.7s**. Rendered live:

```
514  ⚠n+1  ⟳●db                     policy in policies — an n+1 and the repeated write on one line
587        ⟳●db                     destRole.Save() inside foreach (var srcRole in roles)
590  ⚠n+1  ⟳●db
170  ⟳↓db0  ●db  ○4↓5?              tier 3 (high) + a proven direct write + the folded distant fan-out
```

### Also fixed in the same pass

- **Light-theme contrast.** Marks do not sit on white: a lens row carries a severity tint and a hazard pill
  paints its own 12% danger background on top. Composited (measured by painting the stack into a canvas, not by
  reading the token) the dispatch chip was **3.52:1** and the hazard pill **3.91:1** — both under AA. Light
  `--muted`/`--warn`/`--danger` darkened and dark `--danger` lightened; every mark now measures **4.73-12.9 in
  light and 5.41-11.1 in dark**.
- **Ten NU5118 pack warnings** on every `dotnet pack`: `PackAsTool` already packs wwwroot via the publish
  output, so the explicit `Pack`/`PackagePath` metadata added every file a second time. Removed; package
  verified to still carry all 9 wwwroot entries and `builtin-rules.json`.
- **Every indexed store read `-dirty`** because `meddbase-main-application-2` has an untracked `.rig/`.
  Added to that checkout's `.git/info/exclude` (local, no tracked file touched), so the next index is
  attributable.

## Extracted follow-ups

- Lazy witness resolution: [file-lens-lazy-witness-path](../todo/file-lens-grain-5-lazy-witness-path.md).
- Rider finding visuals: [Rider plugin minimal product](../done/rider-plugin-minimal-product.md).

## Original slice C notes, kept for the record

- Tier 3 `cross_method_amplification` already produces exactly what a lens line needs:
  `AnchorFinding(Caller, FilePath, Line, IterationKind, WitnessProvider, WitnessOperation, WitnessResource,
  WitnessDepth)` with a depth-tiered `Confidence` (high ≤1, medium ≤4, low). It is a join on (file, line)
  onto rows the lens already emits. The semantics differ from tier 2 and the mark must too: the loop is on
  this line, the I/O is NOT — it is somewhere below the call.
- Tier 1 hazards (n_plus_1, race_window, sync_over_async, dual_write, cache_coherence) are a different
  signal class again; a gutter/margin is likely right rather than another badge.
- Both are blocked on the presentation grain, which the UI design pass owns: a line can now carry four
  orthogonal facts (family, distance, basis, repetition) and adding two more must not make it unreadable.
- The web and Rider surfaces receive `Looped` and the filter block on the wire but render neither yet.
