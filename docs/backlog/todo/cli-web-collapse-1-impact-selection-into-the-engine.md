# Impact selection moves into the engine as one view

**Status:** todo · designed 2026-09-02, no code written · **Family:** query correctness / CLI-web parity
**Triage:** needs-info (blocked on the D1 mechanism question below)

Shared rationale, inventory and the three renderer rules are on
[the wayfinder map](./cli-web-collapse-map.md). This card carries scope, ownership, verification and status only.

## Scope

Add a single `ImpactEngine.Select(art, only, exclude, includeIntrinsic) → ImpactView` returning filtered
`PerEp`, `HiddenIntrinsic`, `BehavioralEpCount`, filtered `GuardConditions`, cause-classified `StructuralOnly`
and `AffectedEpCount`. `RenderImpact` and the CI gates consume only the view; `ImpactMapper` consumes only the
view. The optional statics `FilterPerEpEffects` (`ImpactEngine.cs:530`), `EffectChangedEpCount` (`:545`) and
`ClassifyStructuralCause` (`:1464`) stop being optional.

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

## Open decision — D1's mechanism

Moving selection server-side honours D1's intent (intrinsics hidden by default; the unfiltered artifact
cached; the filter never in `ImpactCacheKey`, `QueryCacheKeys.cs:297-305`) and contradicts D1's stated
MECHANISM, which is client-side.

| option | change | cost |
| --- | --- | --- |
| A — client-side, D1 as written | zero server change; toggle is instant | a third implementation of the selection in JS: token grammar, `IntrinsicProviders` (`EffectDerivation.cs:359`, already hand-copied at `store.js:293-294`), `NamesIntrinsic`, the hazard-only exclusion. Parity tested, never structural |
| B — server-side post-cache via the shared view, `?only=&exclude=&intrinsic=` | the convention five other endpoints already use for `intrinsic` (`RigApiEndpoints.cs:293`, `api.js:142-166`) | a toggle becomes a warm request: an `ImpactCacheKey` hit plus `ImpactMapper.LoadUniqueLocationsAsync` (two `SymbolFacts` scans, `ImpactMapper.cs:108-144`; seconds, not the cold minutes D1 cited). The client IndexedDB key gains the filter signature, as `tree|…|intrinsic` already does |

B is RECOMMENDED: one implementation, and five precedents against A's one — the web tree does filter
client-side (`RigApiEndpoints.cs:281-283`, `store.js:178`). It reverses D1's mechanism, so it is the product
owner's call and this card stays blocked on it. Recorded as recommended-not-taken.

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
