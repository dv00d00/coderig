# Next chunk — the entry point for the next session

**Status:** wayfinder map · 5 items, in order; items 1 and 2 are closed, so item 3 is next · **Opened:**
2026-09-02 from the day's audits · **Family:** wayfinding

Every item below is a VERIFIED finding with its anchors carried, not a proposal. This is a map, not a second
index: each item is a normal card and the directory listing is still the index. No `**Triage:**` line —
items 3-5 still carry product decisions.

## Decide first

Each open decision lives on the TICKET it gates, with its options and their costs; each cluster map carries a
short block naming what is settled and what is only recommended. Resolve them there, not here.

1. ~~CLI/web parity — child 1's §2 fork.~~ **DECIDED 2026-09-02 (D1): match the CLI — intrinsics hidden by
   default, and the filter applied CLIENT-SIDE.** The Impact slice is now its own `ready-for-agent` card,
   [impact-web-effect-filters-client-side](./impact-web-effect-filters-client-side.md). The same question for
   the five endpoints where `intrinsic` IS a server parameter stays open in §2 of
   [cli-web-parity-1](./cli-web-parity-1-web-api-seed-and-effect-disclosure-parity.md).
2. ~~[Caching and live derivation](./derivation-cache-map.md) — ordering and subsumption.~~ **RESOLVED
   2026-09-02 by reading the code:** the warm-graph umbrella subsumes nothing, child 4 is its precondition,
   order 4 → 2 → 3 → (5, gated on a fresh batch baseline, now
   [its own card](./derivation-cache-6-rig-serve-batch-baseline.md)).
3. [File-lens grain](./file-lens-grain-map.md) — whether `provider:operation` grain gets a slice at all, now
   [child 6](./file-lens-grain-6-provider-operation-grain.md), `needs-info` pending a measurement. Provider
   grain itself was never blocked and is specced.
4. [Two surfaces, one store, disagree](./question-vs-plan-map.md) — which of the two graph builders applies
   redirect rules correctly. Both options and their costs are on
   [child 3](./question-vs-plan-3-redirect-rules-applied-asymmetrically-across-graph-paths.md); O1 is
   recommended, the card stays `needs-info` until it is taken.

## The chunk, in order

1. ~~**Hide the dead File Lens `intrinsic`/`async` toggles**~~ — **DONE 2026-09-02** (uncommitted at time of
   writing). Both controls, their filter-state fields, their URL params and the now-unreachable refetch branch
   are gone, and the false comment at `filelens.js:254` is corrected. The real fix — a cache-key axis for
   `intrinsic`/`async` on the file-effects projection — remains §7 of
   [cli-web-parity-1](./cli-web-parity-1-web-api-seed-and-effect-disclosure-parity.md).
2. ~~**Route the web EP consumers through the existing cache**~~ — **RETIRED 2026-09-02**, found already fixed
   by inspection and moved to
   [`done/`](../done/derivation-cache-1-ep-derivation-uncached-outside-callers.md). All four EP-record call
   sites (`CallersCommand.cs:581`, `CallersQueryService.cs:200`, `EntryPointService.cs:32`,
   `EntryPointsCommand.cs:71`) already route through `LoadOrDeriveEntryPointRecordsAsync`. The card's
   3.5-4.9s per call was not re-measured, so the win is inferred, not observed. **The next actionable item in
   this chunk is item 3.**
3. **[Web Impact effect filters, client-side](./impact-web-effect-filters-client-side.md)** — *why third:*
   intrinsics are **91.3% of all effects** (243,391 `alloc` + 79,508 `throw` against ~30,619 for the other 49
   providers), so the web Impact view shows roughly 9% signal on a large merge request. D1 resolved the §2
   fork on 2026-09-02 — match the CLI, filter client-side — and the card is now `ready-for-agent`.
4. **[Compute the effect-severity distribution](./effect-severity-mark-compute-the-distribution-first.md)** —
   *why fourth:* already decided — measure the family-breadth distribution across the real store before
   choosing a mark or a threshold. The measurement IS the work; the rendering choice follows it.
5. **[Web source navigation](./web-source-navigation.md)** — *why last:* the biggest capability step and the
   only one with an open UX question. `symbol_facts` has `SymbolId`/`FilePath`/`Line`, `reference_facts` is
   indexed on `TargetSymbolId` (2.44M rows) and `/api/tree?from=` is disk-cached, so everything exists
   EXCEPT resolving which symbol was clicked — `reference_facts` has no `Column`. Agreed approach: line +
   token-text match with a line-picker fallback when ambiguous, never the match alone. Rider's PSI-reference
   fallback (`RigEffectDaemonStage.cs:193,217-227`) does not port; there is no semantic model in the browser.

## Not in this chunk, and why

- **Telemetry join to effect sites** — parked with a known trigger, held in
  [`todo/telemetry-join-to-effect-sites.md`](./telemetry-join-to-effect-sites.md) rather than archived so the
  trigger stays visible. Reopen when source-generated proxies emitting a span per method call reach MedDBase
  prod; until then there is no join key and nothing to schedule.
- **New web surfaces for `di` / `dispatch-fans` / `amplify` / `symbols` / `refs <pattern>`** — D5, not yet.
  The `amplify` slice has its own card ([amplify-web-slice](../needs-review/amplify-web-slice.md)) and needs its own
  cache-key thinking first.
- **Anything requiring a reindex.** Excluded by construction: every item above is query-side or render-side.
