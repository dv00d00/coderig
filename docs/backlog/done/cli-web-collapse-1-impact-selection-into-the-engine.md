# Impact selection moves into the engine as one view

**Status:** todo · designed 2026-09-02, no code written · **Family:** query correctness / CLI-web parity
**Triage:** ready-for-agent
**Decision:** D1-rev, 2026-09-03 — **option B**. Selection moves server-side into the shared view, reached by
`?only=&exclude=&intrinsic=`. D1's intent stands; its client-side mechanism is withdrawn. See
"D1's mechanism — resolved" below for the measurement that withdrew it.

Shared rationale, inventory and the three renderer rules are on
[the wayfinder map](../todo/cli-web-collapse-map.md). This card carries scope, ownership, verification and status only.

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

This is the only slice in the COLLAPSE family that touches `RigApiEndpoints.cs`, but it is no longer the only
slice touching it at all: that file changed on 2026-09-02 (`/api/meta` folds the store key into
`DerivationVersion`) and again on 2026-09-03 (`/api/providers` gained the family→provider grouping, per
[family-list-comes-from-rules](./family-list-comes-from-rules-not-a-client-hardcode.md)). Read it current;
a remembered shape will be wrong.

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

**D4, 2026-09-03 — decided, and it is no longer a recommendation.** `Select` keeps every EP with any hazard,
amplification or guard delta in `PerEp`, and reports `BehavioralEpCount` separately. One definition, three
surfaces. Hazard-only EPs stop being dropped by `FilterPerEpEffects`, which is what the engine's stated
intent always said should happen.

**This changes a number on the wire.** `impact_summary behavioral_eps` will differ under `--intrinsic` from
what it prints today, so the verification below must capture the new value deliberately rather than assert
byte-identical output the way [child 2](../todo/cli-web-collapse-2-callers-engine.md) could. Under the DEFAULT
filter the two counts already agree, so the default-filter output is expected to stay unchanged — that part
is still a byte-identical check, and a diff there means the collapse changed something it should not have.

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

## Shipped 2026-09-03

`ImpactEngine.Select(diff, only, exclude, includeIntrinsic) → ImpactView` landed; `RenderImpact`, both CI
gates and `ImpactMapper` consume only the view. `/api/impact` accepts `?only=&exclude=&intrinsic=`,
`ImpactResponseDto` gained `BehavioralEpCount` (it had none — the card's verification section assumed one),
and `api.js`'s client key gained the filter signature mirroring `QueryCacheKeys.EffectFilterSignature`.
`QueryCacheKeys.cs` untouched and `ImpactSchema` still 8, as designed: the cached artifact stays unfiltered.

**The slice found a silent total false negative, and that is the bigger result.** `PrepareFilterTokens` ran
AFTER `FilterPerEpEffects`, so its family expansion arrived too late and every FAMILY token selected nothing:

```
rig impact --only db    before:  behavioral_eps=0  effect_added=0  effect_removed=0
                        after:   behavioral_eps=38 effect_added=88 effect_removed=719
```

719 effect removals reported as "no behavioural change" — precisely the failure `ImpactCommand.cs:257-260`
says the filter exists to prevent. The guard against silent false negatives was itself silently
false-negative for every family token. Fixed by preparing tokens before `Select`, on both surfaces.

**D4 verified on real data, under `--only`** (stores `a1d65d423431` → `a0b279cf7e85`), which is how it was
exercised without new stores — the default and `--intrinsic` runs cannot show it (see below):

| | before | after |
| --- | --- | --- |
| `ep_delta` rows | 19 | 34 (+15, all `+0 -0 ~0`, **0 lost**) |
| `ep_hazard_removed` | 84 | 101 |
| `ep_amplification_removed` | 19 | 30 |
| `ep_effect_added` / `_removed` | 19 / 135 | 19 / 135 (identical) |
| `behavioral_eps` | 19 | 19 |

**The card's claim that `behavioral_eps` "will differ under `--intrinsic`" does not hold on this pair.**
Measured before dispatch: default and `--intrinsic` both report 38, and the UNFILTERED `PerEp` is also 38 —
every EP in this diff has an effect delta, so D4's retention rule adds none of them. Both TSV and human
outputs are byte-identical before/after under default AND `--intrinsic`. The fixture tests carry the
verification burden for D4, not the real store.

**D5, 2026-09-03 — the guard-retention arm is deliberately ABSENT (option O2).** `HasGuardDeltaOnSharedMutation`
reads `GuardEffectDelta`, which reads only `Added`/`Removed` filtered to `lock`/`async_lock`. So a guard delta
IS an effect entry, and an explicit arm could only be harmful or dead: on the FILTERED lists it is strictly
subsumed by the effect arm (dead code implying a rule that never fires); on the UNFILTERED delta it retains an
EP under an `--only` that strips `lock`, but both renderers evaluate the predicate on the filtered delta, so
the row prints `ep_delta … +0 -0 ~0` with no `ep_guard_delta` and no ⚠ line — an information-free husk.
`lock` is not intrinsic, so under the default filter a guard delta always survives and its EP is kept by the
effect arm; the arm only ever mattered when the reader had explicitly filtered locks out. Verified inert:
removing it left both the default and the `--only llblgen:write` output byte-identical.

**Deviation from the brief, accepted.** The endpoint's unknown-token warning derives its vocabulary from the
RULE SET (via the shared `PrepareFilterTokens`) rather than from the vocabulary present in the diff, as the
brief said. Diff-derived would warn on a valid provider that merely has no delta — different semantics from
the CLI in a parity slice — and could not expand family tokens, which is the very bug above.

**Also shipped, user-visible:** the web Impact view now hides intrinsics by DEFAULT (14,059 → 2,314 effect
entries on the MedDBase pair), matching the CLI and the five endpoints that already share the flag.
`?intrinsic=true` reproduces the previous payload.

Verified independently of the agent's report: `0 Warning(s) / 0 Error(s)`, full default lane **1409/1409**,
byte identity re-checked against a baseline captured BEFORE dispatch, and the D4 row-level check re-run
(15 retained / 0 lost / effect rows identical).

**Not done, each now its own card:**

- [The impact location memo trades a cold fast path for the warm hit](../wont-do/impact-location-memo-cold-cost.md) — accepted as-is
- [Two web surfaces still call `perEp.length` "behavioral"](../todo/web-impact-mislabels-per-ep-count-as-behavioral.md)

The four selection statics (`FilterPerEpEffects`, `EffectChangedEpCount`, `ClassifyStructuralCause`,
`FilterGuardConditions`) remain `internal` rather than private: four existing test classes call them
directly. Every PRODUCTION caller now goes through `Select`.
