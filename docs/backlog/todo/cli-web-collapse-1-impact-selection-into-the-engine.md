# Impact selection moves into the engine as one view

**Status:** todo · designed 2026-09-02, no code written · **Family:** query correctness / CLI-web parity
**Triage:** ready-for-agent
**Decision:** D1-rev, 2026-09-03 — **option B**. Selection moves server-side into the shared view, reached by
`?only=&exclude=&intrinsic=`. D1's intent stands; its client-side mechanism is withdrawn. See
"D1's mechanism — resolved" below for the measurement that withdrew it.

Shared rationale, inventory and the three renderer rules are on
[the wayfinder map](./cli-web-collapse-map.md). This card carries scope, ownership, verification and status only.

## Scope

Add a single `ImpactEngine.Select(art, only, exclude, includeIntrinsic) → ImpactView` returning filtered
`PerEp`, `HiddenIntrinsic`, `BehavioralEpCount`, filtered `GuardConditions`, cause-classified `StructuralOnly`
and `AffectedEpCount`. `RenderImpact` and the CI gates consume only the view; `ImpactMapper` consumes only the
view. The optional statics `FilterPerEpEffects` (`ImpactEngine.cs:530`), `EffectChangedEpCount` (`:545`) and
`ClassifyStructuralCause` (`:1464`) stop being optional.

Per D1-rev, also:

- **`/api/impact` accepts `only`, `exclude` and `intrinsic`** and applies them through `Select` — the
  convention five other endpoints already use (`RigApiEndpoints.cs:293`, `api.js:142-166`). Unknown tokens
  warn rather than silently emptying the view, derived from the vocabulary present in this diff.
- **Memoize `ImpactMapper.LoadUniqueLocationsAsync` per store identity.** Not an optimisation to defer: it is
  the whole per-toggle cost of the chosen mechanism.
- **The client IndexedDB key gains the filter signature.** `impact|${base}|${head}|${asyncWalk}` becomes
  filter-aware (`api.js:176-179`), or a payload cached under one filter is served under another.

## Owns

`Impact/ImpactEngine.cs`, `Commands/ImpactCommand.cs`, `Web/ImpactMapper.cs`, `Web/ImpactContracts.cs`,
`Web/RigApiEndpoints.cs`, `wwwroot/api.js:176-179`, `wwwroot/main.js:622`, and the impact view in
`components.js`.

This is the ONLY slice in the family that touches `RigApiEndpoints.cs`. That file changed on 2026-09-02
(`/api/meta` now folds the store key into `DerivationVersion`), so it is read current rather than from a
remembered shape.

## The third count this slice settles

Cross-links to
[`rig impact` reports two different behavioral-EP counts](./impact-reports-two-different-behavioral-ep-counts.md),
which owns the decision.

| output | anchor | counts |
| --- | --- | --- |
| `impact_summary behavioral_eps` | `ImpactCommand.cs:973` | `diff.PerEp.Count` |
| the human header | `ImpactCommand.cs:774` | `EffectChangedEpCount` |

They agree under the default filter only because `FilterPerEpEffects` drops hazard-only EPs
(`ImpactEngine.cs:553`, `:566-569`), which contradicts the engine's stated intent that hazard-only EPs surface
per-EP. With `--intrinsic` they diverge.

Recommendation, not a decision: `Select` keeps EPs with any hazard, amplification or guard delta in `PerEp`,
and reports `BehavioralEpCount` separately. One definition, three surfaces.

## D1's mechanism — resolved 2026-09-03 as B

Moving selection server-side honours D1's intent (intrinsics hidden by default; the unfiltered artifact
cached; the filter never in `ImpactCacheKey`, `QueryCacheKeys.cs:297-305`) and contradicts D1's stated
MECHANISM, which is client-side.

| option | change | cost |
| --- | --- | --- |
| A — client-side, D1 as written | zero server change; toggle is instant | a third implementation of the selection in JS: token grammar, `IntrinsicProviders` (`EffectDerivation.cs:359`, already hand-copied at `store.js:293-294`), `NamesIntrinsic`, the hazard-only exclusion. Parity tested, never structural |
| B — server-side post-cache via the shared view, `?only=&exclude=&intrinsic=` | the convention five other endpoints already use for `intrinsic` (`RigApiEndpoints.cs:293`, `api.js:142-166`) | a toggle becomes a warm request: an `ImpactCacheKey` hit plus `ImpactMapper.LoadUniqueLocationsAsync` (two `SymbolFacts` scans, `ImpactMapper.cs:108-144`; seconds, not the cold minutes D1 cited). The client IndexedDB key gains the filter signature, as `tree|…|intrinsic` already does |

**B is TAKEN.** One implementation, and five precedents against A's one — the web tree does filter
client-side (`RigApiEndpoints.cs:281-283`, `store.js:178`), which is A's only precedent.

D1 chose A on the premise that a refetch would cost the minutes the base+head derivation costs. That premise
is wrong: the diff artifact is cached and filter-independent (`QueryCacheKeys.ImpactCacheKey`,
`QueryCacheKeys.cs:297-305` keys on the two store identities, the rules fingerprint and the traversal mode,
never the filter), so a toggle hits that cache and never re-derives.

**Measured 2026-09-03, store `409c330b99dd` vs `aae396ea7e8e` (MedDBase, ~3.9 GB each).** The only
per-request work left after the cache hit is `ImpactMapper.LoadUniqueLocationsAsync`, which materializes every
method symbol to attach file and line: **222,094 rows per store**, against **716 ms** for a bare sqlite
`COUNT` over the identical predicate — so seconds, not minutes. Base and head run concurrently
(`ImpactMapper.cs:34-35` starts both tasks before awaiting either), so wall clock is one scan, not the two the
option table claimed. Not measured: EF's materialization cost on top of that floor.

**B carries one added obligation — memoize the location map.** It is keyed on store identity alone and the
filter cannot affect it, so recomputing it per toggle is the entire remaining cost of B. Memoized, a toggle is
a cache hit plus a filter pass, which is faster than A as well as being one implementation instead of three.
`verify:` whether `relevantStems` (`ImpactMapper.cs:34-35`) is filter-independent — if it is, the memo can
hold the full per-store map, which is strictly simpler than a stem-keyed one.

The client IndexedDB key gains the filter signature, as `tree|…|intrinsic` already does.

## Verification

On `playgrounds/EntryPointEffects`: `rig impact --format tsv` `impact_summary behavioral_eps` equals the human
header count equals `/api/impact` `behavioralEpCount` — under the default filter AND `--intrinsic`, cold and
warm. `ImpactReviewLocationTests` green. No `ImpactSchema` bump: neither the derivation nor the cached payload
shape changes.

## Absorbs

- [CLI/web `impact` behavioral count differs by one](./cli-web-parity-2-impact-behavioral-count.md) — its root
  cause is this card's optional-statics problem. The attribution of the extra `echoactor …Inbox` row stays
  unsettled there.
- [Web Impact has no effect filter](./impact-web-effect-filters-client-side.md) — the same omission from the
  web side. Option A above is that card as written; option B replaces it.
