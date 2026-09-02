# The whole-store EP derivation is still uncached on four other surfaces

**Status:** DONE 2026-09-02 — found ALREADY FIXED by inspection; the premise below no longer holds ·
**Found:** 2026-08-24, while caching the `callers --entrypoints` path · **Family:** performance / query cache

## Resolution 2026-09-02 — already fixed, closed by inspection

Every EP-record call site is already on the cached `LoadOrDeriveEntryPointRecordsAsync` path:

| call site | surface |
| --- | --- |
| `CallersCommand.cs:581` | `rig callers --entrypoints` |
| `CallersQueryService.cs:200` | the web `--entrypoints` lens (`/api/callers`) |
| `EntryPointService.cs:32` | the web entry-point listing (`/api/entrypoints`) |
| `EntryPointsCommand.cs:71` | `rig entrypoints` |

The only remaining raw `DeriveEntryPointsAsync` calls are the derive-on-miss branch inside
`LoadOrDeriveEntryPointRecordsAsync` itself (`EntryPointContext.cs:195`), the persist path (`:365`), and
`ImpactEngine`/`AmplifyCommand`, which carry their own rules-hashed caches.

Caveat on the win: the 3.5-4.9s per call this card measured was **NOT re-measured** after the fix, so the
saving is inferred from the routing, not observed. If the number matters, re-time a cold and a warm
`/api/entrypoints` before quoting it.

The body below is the original card, kept for its anchors and its measured baseline.

## What was fixed, and what was not

`rig callers <x> --entrypoints` now memoizes the whole-store entry-point RECORD set through the source's
artifact cache (`EntryPointContext.LoadOrDeriveEntryPointRecordsAsync`, keyed by
`QueryCacheKeys.EpRecordsCacheKey` = store identity + rule fingerprint + `EpSchema`). Measured on
`c:/git/meddbase-analysis`, the `entry points` phase went 3.6s -> 0.07s (and 1.5 GB of disk reads -> 0).

The SAME derivation — `Reads.LoadFactEntryPointDataAsync` + `FactEntryPointDeriver.Derive` +
`Reads.DeriveHandoffEntryPointsAsync` + `MethodDocIdBySite` — is still run uncached by four other callers
(`rig callers EntryPointContext.DeriveEntryPointsAsync` names them):

| caller | surface |
| --- | --- |
| `Services/CallersQueryService.BuildEntryPointsAsync` | the WEB `--entrypoints` lens (`/api/callers`) — the exact same question, minus the cache |
| `Services/EntryPointService.ListAsync` | the web entry-point listing |
| `Commands/EntryPointsCommand.RunAsync` | `rig entrypoints` |
| `Commands/DeriveCommand.RunAsync` | `rig derive` (already pays a whole-store derivation, so the marginal win is smaller) |

The web pair is the sharp one: `/api/callers?entrypoints` answers the identical question a cached CLI run now
answers in 0.07s, and pays the full 3.5s per request because it derives straight from a `RigDbContext` rather
than through the `IQueryFactSource` / `IQueryArtifactCache` seam.

## Fix

Route the two `Services/*` consumers through `LoadOrDeriveEntryPointRecordsAsync` (they can borrow a source
via `StoreQueryFactSource.Borrowing`, which HazardsService already does for the same reason), and give `rig
entrypoints` the same entry. They share the derivation, so they should share the entry — no new key, no new
schema axis. `rig derive` is skipped: it already pays a whole-store derivation, so its marginal win is
smaller.

### The alleged `EntryPointRecord` shape mismatch is resolved, not open

`EntryPointService.EntryPointView` is `(Kind, Route, Fqn, File, Line)` (`EntryPointService.cs:15`), and
`Fqn` resolves from the record's pre-resolved `DocId` with a Route fallback. `DisplayName`/`Method` live on
`DerivedEntryPoint`, which NEITHER web consumer projects. So the cached `EntryPointRecord` does not need
widening and the web projection does not need to rebuild anything: **no `EpSchema` bump**.

## Also outstanding, same family

- `rig callers` gained `--no-cache` (the bypass its new cache needs). It is NOT in `README.md`'s command
  table or `.claude/skills/rig/REFERENCE.md`, both of which list the flag for `tree`/`impact` only.
- `docs/backlog/todo/derivation-cache-3-live-ep-derivation-is-per-query-not-per-generation.md` is now PARTLY addressed: the
  `callers --entrypoints` arm is memoized per GENERATION (the artifact cache's live arm is
  `LiveFactSource.ArtifactMemo`), keyed on the rules fingerprint exactly as that item prescribes. The
  `_epSiteKind` half — the EP chip on `reaches`/`tree`/`path` — is still per-query, and neither half appears
  in `BuildTimes`, which that item argues is the real invariant.

## Implementation (specified 2026-09-02 — `ready-for-agent`)

The precedent is in the repo and correct. `src/Rig.Cli/Commands/CallersCommand.cs:579-585` opens an artifact
cache and calls `LoadOrDeriveEntryPointRecordsAsync` with the rules fingerprint, under a comment reading
**"ONE code path and ONE key (see EntryPointContext.LoadOrDeriveEntryPointRecordsAsync)"**.

The comment claims one code path. There are three others still taking the raw path:

| call site | surface |
| --- | --- |
| `src/Rig.Cli/Services/CallersQueryService.cs:184` | the web `--entrypoints` lens (`/api/callers`) |
| `src/Rig.Cli/Services/EntryPointService.cs` | the web entry-point listing (`/api/entrypoints`) |
| `src/Rig.Cli/Commands/EntryPointsCommand.cs:68` | the CLI `rig entrypoints` |

All three call `DeriveEntryPointsAsync` directly. (`Commands/DeriveCommand.RunAsync` is the fourth in the
table above; it already pays a whole-store derivation, so its marginal win is smaller and it is out of this
slice's acceptance.)

### Measured cost

~**4.9s per request**, paid on every `/api/entrypoints` call and every reverse-nav "who can trigger this"
click. `src/Rig.Cli/Commands/EntryPointContext.cs:164-180` records that this derivation was **3.5s of 9.7s**
and **3.9s of 6.6s** on the hottest queries, before the cached path was built for the CLI on 2026-08-24.

### Acceptance

- All three call sites route through `LoadOrDeriveEntryPointRecordsAsync`.
- A warm second request to `/api/entrypoints` is dramatically faster than the first and **byte-identical**
  to it.
- **No `*Schema` bump.** This adds a cache lookup; it does not change an answer. Stated explicitly so nobody
  bumps one reflexively — a bump here would flush every warm store and every browser's IndexedDB for
  nothing. The shape question above is settled: `EntryPointRecord` is not widened, so there is no
  payload-shape change that could justify one.

### Note for whoever picks it up

Verify with a **locally built** `serve` on a spare port: the installed global tool will be stale, and
`rig serve` on 5050 must be left alone.
