# CLI/web parity — wayfinder

**Status:** wayfinder map · 3 children, none started · **Opened:** 2026-09-02, consolidating three open cards
plus a CLI-vs-web audit of all 22 subcommands · **Family:** web / disclosure-parity

## Shared root cause

The two front ends share the *engines* but not the *renderers*, the *parameters*, or the *cache axes*: a
disclosure, a filter or an invalidation axis added on the CLI does not reach `/api/*` unless someone carries it
across by hand. So one store, one question, two answers — and the browser is the surface that reads as
authoritative.

## Children, in dependency order

1. [Seed, effect and filter disclosure on `/api/*`](./cli-web-parity-1-web-api-seed-and-effect-disclosure-parity.md)
   — the largest gap: seed-resolution states, the intrinsic axis, guard-condition deltas, the `/api/impact`
   filter/`--structural` gap folded in 2026-09-02, and four verified audit defects.
2. [The behavioral-EP count differs by one](./cli-web-parity-2-impact-behavioral-count.md) — a shared-selection
   bug, not a renderer bug; it can make a reviewer and `--expect-no-effect-change` disagree.
3. [`/api/meta` `derivationVersion` carries no store identity](./cli-web-parity-3-api-meta-derivation-version-lacks-store-identity.md)
   — the client cache is missing the store axis, so a browser can serve pre-reindex answers indefinitely.

## Already measured

- Intrinsic effects are **91.3% of all effects** on the MedDBase store: 243,391 `alloc` + 79,508 `throw`
  against ~30,619 for the other 49 providers combined. They are hidden by default on the CLI since 2026-07-27
  and cannot be hidden at all in the browser, so the web Impact view renders roughly 9% signal on a large MR.
- `/api/impact` accepts only `base`, `head`, `async` (`src/Rig.Cli/Web/RigApiEndpoints.cs:130-131`): no
  `--only`/`--exclude`, no intrinsic toggle, and permanently `--structural`-off.
- The behavioral count differs by exactly one row — `echoactor
  MedDBase.Pathways.Processes.Admin.Catalogues.Inbox` — present in the web payload, absent from the CLI list.
  The CLI's arithmetic is self-consistent (32 + 543 = 575); that is weak evidence, not a verdict.
- Server disk keys fold in `QueryCacheKeys.StoreKey`; the client folds in only rules + schema, and its other
  half is the store **directory name**, which is stable across a re-index of one commit.

## Undecided

- ~~Child 1's §2 fork.~~ **DECIDED 2026-09-02 (D1): match the CLI — intrinsics hidden by default.** And the
  cache-key constraint does not arise where the filter is CLIENT-SIDE: the CLI itself filters render-side
  (`ImpactCommand.cs:238-252`), `ImpactCacheKey` caches the unfiltered artifact, and the response payload
  already carries `Provider`/`Operation` per effect row (`ImpactContracts.cs:10`). The Impact slice is now
  [impact-web-effect-filters-client-side](./impact-web-effect-filters-client-side.md), `ready-for-agent`.
  **Still open for the five endpoints where `intrinsic` IS a server parameter** folded into
  `EffectFilterSignature` (`/api/tree`, `/api/reaches`, `/api/path`, `/api/callers`, `/api/hotspots`): there a
  toggle genuinely must not serve a payload cached under the other setting, in the disk cache **or** in
  IndexedDB.
- Which surface is right about the extra EP in child 2. Not established; both selections need reading side by
  side before either count is changed.
- Whether the File Lens `intrinsic`/`async` toggles earn a real cache-key axis, or stay gone. **They were
  REMOVED 2026-09-02** (controls, filter fields, URL params and the unreachable refetch branch), so the UI no
  longer lies; the axis itself is still open, as §7 of child 1.
- Whether unknown query params should become a 400 or a disclosed warning (child 1).

## Related

- [Two surfaces, one store, disagree on a derivation input](./question-vs-plan-map.md) — child 3 is a fourth
  instance of that family and is filed here for audience reasons: it is a client-cache bug a web reader hits.
- `done/impact-usability-parity-filter-and-alloc-noise.md` — the CLI half of child 1's folded-in filter ask,
  and the source of the 91.3% measurement.
- [CLI/web collapse onto one engine per question](./cli-web-collapse-map.md) — the structural family, designed
  2026-09-02: one engine per question returns a complete result and renderers project. It removes the
  mechanism by which the gaps on this map appear; child 2 here is absorbed by its child 1. The individual
  disclosures child 1 here names are still their own work.
