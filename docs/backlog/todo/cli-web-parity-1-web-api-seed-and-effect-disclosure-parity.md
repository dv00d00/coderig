# Web `/api/*` lacks the seed + effect disclosures the CLI just gained

**Status:** todo · **Priority: MEDIUM** (the CLI's own version of this cost a full session of misdiagnosis; the web view can now silently disagree with `rig` on the same store) · **Found:** 2026-07-27 (review of the seed-resolution fix) · **Family:** web / disclosure-parity
**Related:** [[pattern-resolution-divergence-tree-vs-reaches]] (the CLI fix this is the web half of), [[impact-usability-parity-filter-and-alloc-noise]] (the intrinsic axis)

## Why this exists

Two disclosures shipped CLI-side on 2026-07-27, and the web path shares the *engines* but not the *renderers*,
so it did not inherit either. The design note in `ImpactCommand` says `rig impact` and `/api/impact` "cannot
diverge" because they call one `ImpactEngine.DiffAsync` — true of the DIFF, but the disclosures live above the
engine, in the command. So the two surfaces can now answer the same question differently.

## 1. Seed resolution — `ReachesQueryService` / `PathQueryService`

`reaches`/`path` now distinguish four outcomes (no match / matched-leaf / real-but-not-a-node / ambiguous)
instead of reporting a bare empty result. The web services still return the empty result, so a pattern naming
nothing renders as **"0 reachable"** — the exact "it reads as *this method does nothing*" failure that made a
stale-store mix-up look like a resolver bug for a session.

Cheap, because the plumbing is already there: `PathQueryResult` exposes `Matched` / `FromMatches` / `ToMatches`.
The work is (a) surfacing those in the API payload, (b) the `reaches` equivalent, (c) rendering all four states
distinctly in `wwwroot/`.

The non-node case (3) matters most here: a user clicking a `P:` property in a web view gets "0 reachable" with
no hint that properties are never call-graph nodes and the accessor is what they want.

## 2. Intrinsic effects — the axis is CLI-only

`alloc`/`throw` are hidden by default in `derive`/`reaches`/`tree`/`impact` and restored by `--intrinsic`. The
web has no equivalent, so **the same store renders ~91% more effects in the browser than in the terminal**
(243,391 + 79,508 vs ~30,619 for the other 49 providers). Either is defensible in isolation; disagreeing is not.

Needs a decision rather than a patch:
- **Match the CLI** (hidden by default, a toggle to restore) — consistent, and the browser is where the volume
  hurts most. But a stateful toggle needs a cache-key story: `QueryCacheKeys.EffectFilterSignature` already
  folds `intrinsic` in server-side, and the client keys IndexedDB by `/api/meta`'s `derivationVersion`, so a
  per-request toggle must not be allowed to serve a cached payload built under the other setting.
- **Stay unfiltered in the web** and say so in the UI — the browser can afford volume a terminal cannot, and a
  visible legend is cheaper than a correct cache key.

Recommendation: match the CLI. A tool whose two front ends report different effect sets for one store is the
kind of inconsistency that manufactures false bug reports — three of today's four HIGH items were exactly that.

**DECIDED (D1, 2026-09-02): match the CLI — intrinsics hidden by default, a toggle to restore.** And the
cache-key story the fork worried about does not arise, because the filter belongs on the CLIENT: the CLI
itself filters render-side (`ImpactCommand.cs:238-252`), so no cache key on either layer takes the filter as
an input, and the response payload already carries `Provider`/`Operation` per effect row
(`ImpactContracts.cs:10`). A filter that refetched would also make toggling intrinsics cost a full re-derive.
The Impact slice of this decision is now its own `ready-for-agent` card —
[impact-web-effect-filters-client-side](../done/impact-web-effect-filters-client-side.md). This section retains the
question for the OTHER endpoints (`/api/tree`, `/api/reaches`, `/api/path`, `/api/callers`, `/api/hotspots`),
where `intrinsic` IS a server parameter folded into `EffectFilterSignature` and the cache-key constraint is
real.

### 2.1 `/api/impact` has no effect filter axis at all (added 2026-09-02; EXTRACTED 2026-09-02)

**This section has moved to its own card:**
[impact-web-effect-filters-client-side](../done/impact-web-effect-filters-client-side.md) (terminal). What
follows is the original finding, kept for provenance; the card supersedes it, including its cache-key
reasoning, which D1 replaced with a client-side filter.



Same argument, one command further. `impact-usability-parity-filter-and-alloc-noise` shipped `--only`/
`--exclude` and intrinsic-hidden-by-default **on the CLI** on 2026-07-27; `/api/impact` accepts only `base`,
`head` and `async` (`src/Rig.Cli/Web/RigApiEndpoints.cs:130-131`). So the web Impact view has no effect
filters, cannot reveal or hide intrinsics, and is permanently stuck in `--structural`-off, count-only mode.
With intrinsics at 91.3% of all effects (the numbers above), a web reviewer reading a large merge request is
looking at roughly **9% signal** with no way to change that.

The ask, in the CLI's own grammar:

- `only` / `exclude` on `/api/impact`, same token grammar and same unknown-token warning as the CLI (a typo'd
  token that filters everything out reads as "no behavioural change" — the silent false negative the CLI card
  was about).
- `intrinsic` on `/api/impact`, defaulting to hidden if #2 resolves that way, with the same cache-key
  constraint as #2.
- `structural` on `/api/impact`, so the browser can reach the full per-EP reach roster instead of only the
  aggregate counts.

CLI filtering is render-side, so no `ImpactSchema` bump — but the web slice needs the toggle-versus-cache-key
answer from #2 before the client can key a filtered payload. This gap existed because the shipped CLI card
never captured its web slice, which is precisely what CLAUDE.md requires of a report/diff/graph feature.

## 3. Guard-condition deltas are CLI-only (added 2026-07-27)

`impact` gained a fourth signal — `guard_condition_delta` rows plus `guard_narrowed`/`guard_widened`/
`guard_changed` in `impact_summary` — for call edges whose gating predicate moved while the call and its
effects stayed put. The deltas ARE computed in `ImpactEngine` and DO ride in the cache artifact
(`ImpactCachePayload.GuardConditions`), so `/api/impact` already has the data; only the payload mapping and
the UI are missing. This is the same renderer-vs-engine split as #1 and #2, and the note in `ImpactCommand`
about the two surfaces being unable to diverge is wrong for exactly this reason.

Worth doing because this signal is the *only* output a predicate-only change produces: a web reviewer looking
at an audit-suppression MR currently sees a completely empty impact view. It is also the most naturally
web-shaped of the three — a narrowed/widened/changed verdict with base→head conditions side by side is a diff
view, which is what a browser is good at.

## 4. `/api/tree` always traverses unbounded (newly found 2026-09-02)

The server accepts a `depth` query param (`src/Rig.Cli/Web/RigApiEndpoints.cs:293`); the client never sends it
(`src/Rig.Cli/wwwroot/api.js:142-146`), so every web tree is an unbounded walk where the CLI takes a depth.
`maxNodes` is separately hardcoded to `FactPathFinder.DefaultTreeNodeBudget`
(`src/Rig.Cli/Services/TreeQueryService.cs:88`) even though `TreeCacheKey` already carries the axis, so a node
cap cannot be varied per request either. Depth is trivial — wire the existing param; the node cap is a wider
change because the budget is currently a constant on the compute side.

## 5. Unknown query params are silently ignored rather than rejected (newly found 2026-09-02)

Live-verified across every endpoint: `GET /api/impact?…&only=llblgen:write&intrinsic=true&exclude=throw`
returned **200 with all three dropped**. Anyone building a URL from CLI muscle memory — exactly what §2.1
invites — believes a filter applied and reads a filtered answer that is not filtered. Either reject unknown
params or disclose the ones that were ignored; silence is the one option that manufactures a wrong reading.

## 6. `/api/file-effects` has no disk cache — `FileEffectsCacheKey` is dead (newly found 2026-09-02)

`QueryCacheKeys.FileEffectsCacheKey` (`src/Rig.Cli/Caching/QueryCacheKeys.cs:150-154`) has **zero production
callers**; only pinning tests reference it (`tests/Rig.Tests/Cli/FileEffectsCacheSchemaTests.cs`,
`FileFindingsCacheSchemaTests.cs`). So the whole-solution file-effects projection is fast only via the
process-lifetime resident LRU (`src/Rig.Cli/Caching/WarmStore.cs:93-99`), and **every `rig serve` restart
re-pays it**. Either route the endpoint through the disk cache the key was written for, or delete the key and
document the resident-only choice — but not both states at once, where a pinning test guards a cache nothing
uses.

## 7. The File Lens `intrinsic` and `async` toggles are dead controls that claim to work (newly found 2026-09-02)

Both are labelled "CHANGES THE QUERY, refetches" (`src/Rig.Cli/wwwroot/filelens.js:805-806`) and the filter
comment asserts that only these two change what the server computes and that "the UI says so"
(`filelens.js:254`). `/api/file-effects` has neither parameter (`src/Rig.Cli/Web/FileEffectsEndpoint.cs:77-87`,
whose own comment says both would need their own cache key first) and `api.js:207-211` sends neither. Because
the client cache key omits them, the "refetch" returns the identical payload instantly and the control appears
to have worked.

Agreed immediate action: **hide the toggles**. A control that lies is worse than a missing one, and hiding is
minutes. That stopgap is now its own card —
[hide-the-dead-file-lens-toggles](../done/hide-the-dead-file-lens-toggles.md), and it also owns
correcting the `filelens.js:254` comment. **This section keeps the real fix**: a cache-key axis for
`intrinsic`/`async` on the file-effects projection, which the endpoint's own comment anticipates. It is the
follow-on, and it shares the toggle-versus-cache-key answer #2 needs.

## Acceptance

- A pattern matching nothing renders as an explicit "no symbol matches" state in the web UI, never as 0 results.
- A `P:`/`F:`/`E:` pattern renders the not-a-node explanation plus the accessor hint.
- A matched leaf renders as "resolved, makes no in-solution calls" — distinct from both of the above.
- Effect visibility agrees between `rig <cmd>` and the corresponding `/api/*` for the same store, or the UI
  discloses the difference.
- Whichever way #2 goes, a toggle cannot serve a payload cached under the other setting (server disk cache AND
  client IndexedDB).
- `/api/impact` surfaces guard-condition deltas with the same verdicts and base→head conditions as
  `rig impact`, so a predicate-only MR is not an empty view in the browser.
- `rig impact --only <tokens>` and `/api/impact?only=<tokens>` return the same effect set for one store pair,
  and `structural` reaches the same per-EP roster the CLI prints under `--structural`.
- A URL carrying a param the endpoint does not implement is rejected or discloses what it ignored; it never
  returns 200 as though the param applied.
- The web tree honours a requested depth; a request without one is documented as unbounded rather than
  silently so.
- The File Lens shows no toggle that does not change the answer.

## Related

- [CLI/web collapse onto one engine per question](./cli-web-collapse-map.md) — the structural family, designed
  2026-09-02. It relates rather than supersedes: it removes the mechanism that lets a disclosure exist on one
  surface only, while the specific disclosures listed above remain this card's work. Its child 1 absorbs the
  §2.1 filter slice.
