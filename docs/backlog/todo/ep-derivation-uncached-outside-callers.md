# The whole-store EP derivation is still uncached on four other surfaces

**Status:** todo · **Priority: MEDIUM** (a measured 3.5-3.9s per call on `meddbase-analysis`, now paid by
every surface EXCEPT the one that was fixed) · **Found:** 2026-08-24, while caching the `callers
--entrypoints` path · **Family:** performance / query cache

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
via `StoreQueryFactSource.Borrowing`, which HazardsService already does for the same reason), and decide
whether `rig entrypoints` deserves the same. They share the derivation, so they should share the entry — no
new key, no new schema axis.

Note the shape mismatch to resolve rather than paper over: `EntryPointRecord` carries `(Kind, Route, File,
Line, Requires, DocId)`, and the web listing additionally renders `DisplayName`/`Method`. Either widen the
cached record (payload-shape change -> bump `EpSchema`) or have the web projection rebuild those two from
`Kind`/`Route`, which is how `PromoteHandoffOrigins` already synthesizes them.

## Also outstanding, same family

- `rig callers` gained `--no-cache` (the bypass its new cache needs). It is NOT in `README.md`'s command
  table or `.claude/skills/rig/REFERENCE.md`, both of which list the flag for `tree`/`impact` only.
- `docs/backlog/todo/live-ep-derivation-is-per-query-not-per-generation.md` is now PARTLY addressed: the
  `callers --entrypoints` arm is memoized per GENERATION (the artifact cache's live arm is
  `LiveFactSource.ArtifactMemo`), keyed on the rules fingerprint exactly as that item prescribes. The
  `_epSiteKind` half — the EP chip on `reaches`/`tree`/`path` — is still per-query, and neither half appears
  in `BuildTimes`, which that item argues is the real invariant.
