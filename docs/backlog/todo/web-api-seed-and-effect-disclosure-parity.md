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

## Acceptance

- A pattern matching nothing renders as an explicit "no symbol matches" state in the web UI, never as 0 results.
- A `P:`/`F:`/`E:` pattern renders the not-a-node explanation plus the accessor hint.
- A matched leaf renders as "resolved, makes no in-solution calls" — distinct from both of the above.
- Effect visibility agrees between `rig <cmd>` and the corresponding `/api/*` for the same store, or the UI
  discloses the difference.
- Whichever way #2 goes, a toggle cannot serve a payload cached under the other setting (server disk cache AND
  client IndexedDB).
