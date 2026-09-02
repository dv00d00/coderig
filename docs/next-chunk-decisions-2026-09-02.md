# Next chunk — decisions to resolve before implementation

**Status:** D1-D9 answered 2026-09-02 (see *Resolutions* below). Not a backlog card. The open items become
cards under `docs/backlog/todo/` per `docs/agents/issue-tracker.md`, after which this file is deleted.

## Resolutions (Dmytro, 2026-09-02)

- **D1 — PARK telemetry, with a trigger.** Not "not useful" — **not yet**. Source-generated proxies emitting a
  span per method call are being introduced into MedDBase. When that lands, the join key this recon could not
  find (method-level runtime identity) exists, and the *product* option — real call count and p99 against an
  effect site — becomes available directly, without the entity/table heuristic and without O7's
  exception-path-only limit. **Revisit when per-method spans are in prod.** Graylog (O7) is feasible and
  correct but does not currently hit a nerve; keep it as the fallback if failure provenance is wanted before
  the proxies ship. O8 (table-level SQL) stays rejected — 377 effect sites per link.
- **D2 — hide the dead File Lens toggles now**, card the cache-key work. (Implied by F1 being a lying control;
  no objection raised.)
- **D3 — consolidate NOW.** All four wayfinder clusters, this chunk.
- **D4 — fold into `web-api-seed-and-effect-disclosure-parity` §2.** (No objection raised.)
- **D5 — not yet.** No new web surfaces for `di`, `dispatch-fans`, `amplify`, `symbols`, `refs <pattern>`.
- **D6 — A with B as fallback.** (No objection raised; recommendation stands.)
- **D7 — COMPUTE FIRST, then decide rendering.** Measure the severity distribution across the real store
  before choosing a mark. Rendering candidates Dmytro floated, from bold to boring: a **bold method name**, a
  **squiggly underline**, a blink (joke), or the dull-but-clear option — an **exclamation mark in its own
  dedicated gutter lane**. Threshold unset pending the distribution.
- **D8 — a-e as recommended.** (No objection raised.)
- **D9 — SINGLE commit**, not the seven-way split.

**Date:** 2026-09-02. **Context:** three read-only recon passes (backlog triage of all 65 open cards,
CLI-vs-web parity audit of all 22 subcommands, telemetry joinability) plus a day of shipped review-UI work
that is **still uncommitted**.

---

## Findings that need no decision, only a place in the queue

These are verified defects with no open question. Listed so they are not lost.

| # | Finding | Evidence | Cost |
|---|---|---|---|
| F1 | **File Lens `intrinsic`/`async` toggles are dead controls that claim to work.** The UI labels both "CHANGES THE QUERY, refetches" and a comment asserts "the UI says so". `/api/file-effects` has neither parameter; `api.js` sends neither. The cache key omits them, so the "refetch" returns the identical payload instantly and looks successful. | `filelens.js:254,268-269,805-806`; `api.js:207-211`; `FileEffectsEndpoint.cs:77-87` | stopgap trivial, real fix needs a cache-key axis (the endpoint's own comment says so) |
| F2 | **`/api/entrypoints` and `/api/callers?mode=entrypoints` bypass a caching fix the CLI already has.** 4.9s per request, measured, on every reverse-nav click. `CallersCommand.cs:579` carries the comment "ONE code path and ONE key" — there are two. | `CallersCommand.cs:579-585` (cached) vs `CallersQueryService.cs:184`, `EntryPointService.cs` (raw) | trivial-to-moderate wiring |
| F3 | **`DerivationVersion` omits store identity** → the browser can serve answers from a different store. Carded as HIGH and unaffected by today's two schema bumps, which are the schema axis, not the store axis. | `RigApiEndpoints.cs:350-357`; `todo/api-meta-derivation-version-lacks-store-identity` | small |
| F4 | **`/api/tree` always traverses unbounded.** The server accepts `depth`; the client never sends it. `maxNodes` is hardcoded to the default budget even though `TreeCacheKey` already has the axis. | `RigApiEndpoints.cs:293`; `api.js:142-146`; `TreeQueryService.cs:85` | depth trivial, node cap moderate |
| F5 | **`reaches`, `callers`, `path` have no server cache on either surface.** Recompute every call, CLI and web alike. Pre-existing, not a web regression. | `ReachesQueryService.cs:46-58` | design-gated (see cluster 2) |
| F6 | **Unknown query params are silently ignored, not rejected.** `GET /api/impact?...&only=...&intrinsic=true` returned 200 after 247s with all three extras dropped. Anyone building a URL from CLI muscle memory believes a filter applied. | live-verified | small diagnostic |
| F7 | **`FileEffectsCacheKey` is dead code** — zero production callers, only a pinning test. `/api/file-effects` has no disk cache; it is fast only via a process-lifetime LRU, so every `rig serve` restart re-pays the whole-solution projection. | `QueryCacheKeys.cs:150-154`; `WarmStore.cs:93-99` | small |
| F8 | **`FileDiffEndpoint` is a third, undocumented caching layer** — an unbounded per-process `ConcurrentDictionary`, distinct from both `WarmStore` and the disk `QueryCache`. | `FileDiffEndpoint.cs:23-37` | document, don't change |

---

## D1 — Telemetry: which option, or park?

Settled by data, not preference. rig has **no effects table** (derived at query time); every `llblgen` rule
sets `resource: receiver_type`, which resolves to the entity *class* name. No SQL text and no table name is
captured anywhere. Table name exists only as a PascalCase→SCREAMING_SNAKE convention that rig never
validates — LLBLGen resolves the real mapping at runtime.

Cardinality, measured on store `409c330b99dd`:

| entity resource | distinct effect sites |
|---|---|
| `AppointmentEntity` + `AppointmentCollection` → `APPOINTMENT` | **377** |
| `AccountEntity` | 161 |
| `PersonEntity` | 135 |
| `ServiceModuleEntity` | 13 |

And **3,108 of 18,768 llblgen effects (16.6%) carry no entity resource at all** (`LinqMetaData` 2,195,
`CommonEntityBase` 554, `int` 359) — already carded as `quoted-query-resource-attribution`, postponed.

- **O7 — failure provenance.** Graylog `StackTrace` carries method + file + line, verified live — the same
  grain as an effect site. An effect site links to "has this line thrown in prod, and when". Exact, no
  convention guesswork, no 377-site dilution. Cannot ever give call count or p99: exception path only.
- **O8 — table-level SQL link.** Service-grain wearing site-grain clothing. Also not novel: the table-set
  join has already been done by hand (`progress/amplification-context-propagation.md:87-89`, 71/79 → 66/79
  against an OTel oracle).
- **O9 — park.** Revisit if prod `db.query.text` turns out to be matchable on full statement text at
  interactive speed. A prior note recorded it timing out at 30s over 7 days.

**Recommendation: O7, and park O8.** O7 is honest at site grain and useful in review. O8 looks precise and
is not.

**Caveats:** the ClickStack half was reconstructed from a prior session's recorded recipe, not tested live
(VPN down). Local Graylog is errors-only, 90-day, with two exception types in the sample — the mechanism was
never exercised on a real LLBLGen failure. Prod PDB availability (needed for file:line in a stack trace) is
unknown.

---

## D2 — The File Lens dead toggles (F1): stopgap now, or wait?

Disabling or hiding them is minutes. The real fix needs its own cache-key axis, which the endpoint comment
already anticipates. A control that lies is worse than a missing one.

**Recommendation: hide them now, card the real fix.**

---

## D3 — Backlog consolidation: 65 → ~47?

Zero cards close on merit. The only available cut is structural: 4 wayfinder parents over 18 cards, per the
convention's wayfinding section (shared effort slug, children numbered in dependency order, blocking edges
as relative links).

1. **File-lens grain** (5) — `file-lens-emits-a-marked-line-with-no-owning-method-row`,
   `file-lens-provider-grain`, `file-lens-lazy-witness-path`,
   `annotate-method-badge-with-no-line-that-admits-it`, `annotate-verify-badges`.
   Order: fix the join → provider grain → verify-badges design → lazy witness last.
   `rider-plugin-minimal-product`'s open lazy-witness item should cross-link, not restate.
2. **Caching / live derivation** (5) — `ep-derivation-uncached-outside-callers` (cheapest first slice),
   `cross-method-hazards-cache`, `live-ep-derivation-is-per-query-not-per-generation`,
   `event-handoff-rewrite-breaks-the-graph-index-memo-across-queries`, `warm-graph-across-queries`
   (design-gated umbrella that may subsume the rest).
3. **CLI/web parity** (3 + the uncarded gap in D4).
4. **"Two surfaces disagree on a derivation input"** (3) — `baked-call-edges-ignore-rules-edits`,
   `path-disclosures-computed-off-the-loaded-subgraph`,
   `redirect-rules-applied-asymmetrically-across-graph-paths`. They already cross-reference each other.

**Recommendation: yes, all four.** Question: do you want it done as part of this chunk, or as its own pass?

---

## D4 — The `/api/impact` filter gap has no card. New card, or fold in?

`impact-usability-parity-filter-and-alloc-noise` shipped `--only`/`--exclude` and intrinsic-hidden-by-default
**on the CLI** (2026-07-27). `/api/impact` takes only `base`/`head`/`async`. The same card measured intrinsics
at **91.3% of all effects** (243,391 alloc + 79,508 throw vs ~30,619 for the other 49 providers), so a web
Impact view that renders them shows 9% signal.

The repo's own CLAUDE.md rule exists for exactly this: report/diff/graph output *"should get a web slice,
scoped as an explicit follow-on… capture the web slice in the backlog item so it isn't forgotten."* It was
not captured, so it was forgotten.

`web-api-seed-and-effect-disclosure-parity` §2 already argues the intrinsic axis for the web generally, citing
the same numbers, but never mentions `--only`/`--exclude`.

**Recommendation: fold into `web-api-seed-and-effect-disclosure-parity` §2** — same argument, extended.
Also missing from the web: `--structural` (the full EP list; web is permanently count-only).

---

## D5 — Five commands have no web surface. Which do you actually want?

CLAUDE.md says report/ranking/diff/graph output should get a web slice.

| command | output class | note |
|---|---|---|
| `di` | report | service→impl/lifetime table, a direct dump, cheapest of the five |
| `dispatch-fans` | ranking | same shape as `hotspots`, which already has a view |
| `amplify` | ranking | whole-store amplification leaderboard; partially visible via `/api/hazards` |
| `symbols` | report | only a 15-25 row autocomplete exists; no browsable table |
| `refs <pattern>` | report | only `--unused`/`--usage` shipped |

`profile validate`, `index`, `graph`, `watch` are genuinely CLI-only. `dead` is deliberately disabled
(`Root.cs:55-61`) pending a move onto the one-hop engine.

**Question: one parity card with five children, or only the ones you'd use?** My guess is `di` and
`dispatch-fans` earn their keep and `symbols`/`refs` do not, but that is your call, not mine.

---

## D6 — Web source navigation: how is a clicked symbol resolved?

Everything needed exists except one thing. `symbol_facts` has `SymbolId, FilePath, Line` (go to declaration);
`reference_facts` is **indexed on `TargetSymbolId`** (find usages, 2.44M rows); `/api/tree?from=<id>` already
renders a tree and is now cached. The gap is only *which symbol did I just click* — `reference_facts` has
`Line` but **no `Column`**.

Rider hit the identical problem and solved it client-side: `MatchOnLine`, then resolve the PSI reference and
match by target DocID when several candidates share an enclosing method (`RigEffectDaemonStage.cs:193,217-227`).
**That fallback does not port to the browser** — there is no semantic model there.

- **A** — line + token text match. No reindex, ships now. Can silently land on the wrong overload.
- **B** — line picker: click the line, choose from what it references. Never lies, one extra click.
- **C** — mine `Column` at extraction. Exact. Schema bump plus a reindex of every store (~4 min each for
  MedDBase); old stores stop being readable. Already carded as
  `call-site-facts-no-column-same-line-calls-collapse`.

**Recommendation: A with B as fallback** — jump when unique, picker when not. Never A alone. C when something
else forces a reindex anyway.

---

## D7 — O4/O5 severity signal: approved, but rendered where?

Approved: family breadth (**O4**) plus reachable-method count (**O5**); O6 (transitive effect-site count)
deferred to a card. Worked example, from the live payload:

| line | call | families | profile |
|---|---|---|---|
| 883 | `SetPersonContractId` | **6/8** | blob 11 · cache 4 · db 3 · echo 12 · io 17 · rpc 26 |
| 884 | `SetPersonCourseId` | **6/8** | identical at every depth |
| 886 | `Services.GetAppointmentService()` | 3/8 | cache 2 · db 5 · io 16 |

A property setter reaching a remote call 26 hops down. Note `HotspotsContracts.cs:3-4`: *"there is no blended
score whose weighting a client would have to reverse-engineer"* — so this must ride on one named metric, not
a composite.

**Question: where does the mark go** — a ninth lane slot, a separate gutter mark, a column in the review file
list, or all three? And what threshold counts as "heavy" (5+ families?).

---

## D8 — Small UI calls left open from today

| # | | Recommendation |
|---|---|---|
| D8a | Split context rows draw the method lane in the **head pane only** (base suppressed as an exact duplicate). Show in both? | keep as-is |
| D8b | Inline mode now shows the disclosure bolt on **every** method row, muted rather than teal. Keep, or delta-only? | keep — follows from "the lane means reach" |
| D8c | Breadcrumb trail wraps into many rows at ~108 crumbs and pushes `.review-layout` down (`index.html:587`). Cap, collapse behind a count, or suppress in review mode? | collapse behind a count |
| D8d | Back skips keyboard file changes — only the list click records a crumb, so `[`/`]` navigation is invisible to history. | add one `recordReviewCrumb` in `moveReviewFile` |
| D8e | `file=` in the URL still does not select a file (base/head do apply). Long-standing. | fix; it breaks every shared link |

---

## D9 — Commit split

Today's work is seven distinct concerns in one uncommitted tree, plus four untracked files. Proposed split,
one line per commit, no trailers, per house style:

1. `fix(web): pair renamed accessors instead of reporting removed-then-added`
2. `fix(web): drop the dead text payload from the effect lane slot`
3. `feat(web): show git changed-line counts and status glyphs in the review file list`
4. `perf(web): cache the tree forest and file findings on disk`
5. `refactor(domain): ViaDispatchOnly fires only for a polymorphic dispatch hop`
6. `feat(web): sticky per-lane effect header and a fixed lane origin`
7. `feat(web): review history and unconditional method reach lanes`

**Question: split as above, or one commit?** Nothing is committed without an explicit ask.

---

## D10 — What goes first?

My recommendation, given that zero cards closed and the parity audit found live defects:

1. **F1 stopgap** (a lying control) and **F2** (4.9s, fix exists two files away) — hours, not days.
2. **D4** — Impact web filters and intrinsic default. Highest reader-facing value: 9% signal today.
3. **D3** — the consolidation pass, so the backlog is readable before adding to it.
4. **D6** — source navigation, the biggest single change in what the tool *is*.
5. **D1/O7** — failure provenance, once D6 has made symbol→site navigation real.

Deliberately last: O8, the "no web surface" commands beyond `di`, and anything needing a reindex.
