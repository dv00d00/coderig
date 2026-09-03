# CLI/web parity — wayfinder

**Status:** wayfinder map · child 1 remains open; children 2 and 3 shipped · **Opened:** 2026-09-02, consolidating three cards
plus a CLI-vs-web audit of all 22 subcommands · **Family:** web / disclosure-parity
**Decision:** superseded 2026-09-03; child 1 remains independently tracked in `todo/`.

## Shared root cause

The two front ends share the *engines* but not the *renderers*, the *parameters*, or the *cache axes*: a
disclosure, a filter or an invalidation axis added on the CLI does not reach `/api/*` unless someone carries it
across by hand. So one store, one question, two answers — and the browser is the surface that reads as
authoritative.

## Children, in dependency order

1. [Seed, effect and filter disclosure on `/api/*`](../todo/cli-web-parity-1-web-api-seed-and-effect-disclosure-parity.md)
   — the largest gap: seed-resolution states, the intrinsic axis, guard-condition deltas, the `/api/impact`
   filter/`--structural` gap folded in 2026-09-02, and four verified audit defects.
2. [The behavioral-EP count differed by one](./cli-web-parity-2-impact-behavioral-count.md) — shipped via
   the shared Impact selection view; historical row attribution is now a separate measurement card.
3. [`/api/meta` `derivationVersion` lacked store identity](./cli-web-parity-3-api-meta-derivation-version-lacks-store-identity.md)
   — shipped in `b59b6aba`; same-commit reindexes now invalidate the client cache.

## Historical measurements and shipped outcomes

- Intrinsic effects measured **91.3% of all effects** on the MedDBase store. That evidence drove the shared
  filter shipped in `cacb5d92`; Web Impact now hides intrinsics by default and accepts `only`, `exclude`, and
  `intrinsic` through the shared engine view.
- The historical behavioral count differed by one row. Shared selection/count semantics shipped; attribution
  of the unavailable row was explicitly discarded as unactionable archaeology on 2026-09-03.
- Server disk keys already folded in `QueryCacheKeys.StoreKey`; `b59b6aba` added the same selected-store axis
  to `/api/meta`'s `derivationVersion`, closing the browser's same-commit reindex hole.
- `--structural` remains outside the shipped filter slice because its per-EP reach roster is server-side and
  `/api/impact/reach` already exposes it on demand.

## Undecided

- ~~Child 1's §2 fork.~~ **DECIDED 2026-09-02 and revised 2026-09-03:** match the CLI — intrinsics hidden by
  default — but apply selection server-side after the filter-independent cache through the shared engine view.
  The measured warm path withdrew D1's original client-side mechanism; the complete reasoning is on
  [the terminal Impact filter record](./impact-web-effect-filters-client-side.md).
  **Still open for the five endpoints where `intrinsic` IS a server parameter** folded into
  `EffectFilterSignature` (`/api/tree`, `/api/reaches`, `/api/path`, `/api/callers`, `/api/hotspots`): there a
  toggle genuinely must not serve a payload cached under the other setting, in the disk cache **or** in
  IndexedDB.
- The historical extra EP's intrinsic-only versus hazard-only attribution remains unestablished; the source
  store pair is unavailable and the measurement-only card was removed on 2026-09-03. Selection/count
  correctness is closed.
- Whether the File Lens `intrinsic`/`async` toggles earn a real cache-key axis, or stay gone. **They were
  REMOVED 2026-09-02** (controls, filter fields, URL params and the unreachable refetch branch), so the UI no
  longer lies; the axis itself is still open, as §7 of child 1.
- Whether unknown query params should become a 400 or a disclosed warning (child 1).

## Related

- [Two surfaces, one store, disagree on a derivation input](../todo/question-vs-plan-map.md) — child 3 is a fourth
  instance of that family and is filed here for audience reasons: it is a client-cache bug a web reader hits.
- `done/impact-usability-parity-filter-and-alloc-noise.md` — the CLI half of child 1's folded-in filter ask,
  and the source of the 91.3% measurement.
- [CLI/web collapse onto one engine per question](../todo/cli-web-collapse-map.md) — the structural family, designed
  2026-09-02: one engine per question returns a complete result and renderers project. It removes the
  mechanism by which the gaps on this map appear; child 2 here is absorbed by its child 1. The individual
  disclosures child 1 here names are still their own work.

## Closure (2026-09-03)

Two of three children are terminal and the sole open child is independently discoverable in `todo/`. Keeping
this cross-lifecycle map active would make it a second index, so the map is retained only as historical
wayfinding.
