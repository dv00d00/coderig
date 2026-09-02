# CLI/web collapse onto one engine per question — wayfinder

**Status:** todo · wayfinder map · 6 children, none started · **Designed:** 2026-09-02, no code written ·
**Family:** query correctness / CLI-web parity

## Shared root cause

Six open bugs are one cause: the CLI and the web API each implement the same question separately, and then
they disagree.

The design verdict, stated precisely: **one engine per question returns a COMPLETE result, with every
partition, classification and disclosure carried as data; renderers project, never select or compute.**
Parity failed wherever a renderer was allowed to *decide* — filter, verify, cache-route — instead of project.

The thesis "CLI and web as pure renderers" was tested against the code and came back slightly too strong. A
renderer legitimately chooses WHICH parts of a complete result to show. The line that holds is between
choosing parts and computing them.

## Inventory — established by reading code, 2026-09-02

| question | shared compute core | anchors |
| --- | --- | --- |
| `tree` | yes | `TreeCommand.cs:410` → `TreeQueryService.ComputeAsync` |
| `reaches` | yes | `ReachesCommand.cs:145` → `ReachesQueryService.ComputeAsync` |
| `callers` | none | `CallersCommand.RunAsync` (`:189-505`) never references `CallersQueryService` |
| `path` | none | `PathCommand.RunAsync` (`:107-285`) never references `PathQueryService` |
| `impact` | yes — the in-repo precedent done right | `ImpactCommand.cs:217` and `ImpactQueryService.DiffAsync` (`:31`) both call `ImpactEngine.DiffAsync` (`ImpactEngine.cs:69`) |

`CallersQueryService.BuildAsync` (`:61-117`) and `PathQueryService.BuildAsync` (`:48-149`) are second,
independently written implementations of graph load plus event-subscription marking plus traversal. Their only
consumers are `CallersEndpoint.cs:42` and `PathEndpoint.cs:34`.

## Four divergence sites, ~150-160 lines, all accidents

| site | CLI | web | note |
| --- | --- | --- | --- |
| 1. callers roots forward-verify | `CallersCommand.cs:340-357` | `CallersQueryService.cs:148-165` | ~18 lines each |
| 2. callers EP touching-set dedup + forward-verify | `CallersCommand.cs:588-596,753-777` | `CallersQueryService.cs:202-210,217-243` | three policies for one partition |
| 3. tree cache routing | `TreeCommand.cs:314-433`, ~120 lines, has a partial-hit branch | `TreeQueryService.cs:100-162`, ~63 lines, no partial-hit branch | the forest+`:loc` hit/miss decision is identical |
| 4. path over two separately loaded graphs | `PathCommand.cs:127-143` uses `LoadShapedTraversalGraphAsync` / `LoadDemandForwardPathGraphAsync` | `PathQueryService.cs:75` uses `LoadEffectReachInputsAsync` | same `FactPathFinder.Find`, two loaders that can disagree |

Site 2 is already inconsistent WITHIN the web: roots mode keeps reverse-only rows flagged
(`CallersEndpoint.cs:338`), EP mode drops them (`CallersQueryService.cs:242`), and the CLI hides both by
default.

Site 3's present effect: a web forest-hit with a `:loc` miss recomputes the whole forest instead of reloading
the graph. Both surfaces share `RenderSidecarKey.Locations()`, so a CLI cold run already warms the web's full
hit. The CLI's filter-keyed seam sidecar (`:seam:<sig>`, `TreeCommand.cs:349,810`) is a genuine CLI-only
render artifact and stays.

Site 4 is literally
[`path` disclosures computed off the loaded subgraph](./question-vs-plan-2-path-disclosures-computed-off-the-loaded-subgraph.md).

## One-sided asymmetries, classified

**Accidents — the engine already has the data, or computes it anyway:**

| asymmetry | anchor | why it is an accident |
| --- | --- | --- |
| `reaches` bucket classification | CLI-only, `ReachesCommand.cs:238-240` | the data is on `ReachInfo.HandoffVia` / `DispatchVia`, and `ReachesQueryService.cs:60-71` discards it |
| impact `--only` / `--exclude` / `--intrinsic` | CLI-only, `ImpactCommand.cs:241-253` | selection sits above a filter-agnostic cached artifact |
| `ClassifyStructuralCause` | `ImpactEngine.cs:1464` | already on the engine; the mapper never calls it |
| callers default depth-tagged lens | `CallersCommand.cs:420-505` | the engine computes `ReachedBy` regardless |
| async-hint probe + frontier | `CallersCommand.cs:620-720` | disclosures computed from the answer, so they belong in the result |

**Legitimate, kept:** `--structural` per-EP roster answered on demand at `/api/impact/reach`
(`RigApiEndpoints.cs:198-220`) rather than as a 575-row list; path effects-per-step as a render difference
(the engine takes a `withEffects` flag and the CLI passes false); TSV and LLM formats; deployment chips;
`--time` phase rows; the seam sidecar; `--include-reverse-only` as a CLI flag where the web shows the flag on
the row.

**Genuine today but temporary:** the CLI answers off the resident live index (`LiveRoute.TryAnswerAsync`)
while the web is store-only, because `CallersQueryService` and `PathQueryService` are written on
`RigDbContext` plus the static `TraversalGraphLoader` (`CallersQueryService.cs:79-88`,
`PathQueryService.cs:65-75`) and cannot go live. It becomes a one-line change once the engine takes
`IQueryFactSource`.

## Target shape

```
Services/<Q>QueryService.cs
  internal static Task<<Q>Computation> ComputeAsync(IQueryFactSource source, RuleSet rules, RuleSet shaped, …question params…)
      // ENGINE: load → traverse → forward-verify → classify → disclosures-as-data; owns cache routing
  public static Task<<Q>Result> BuildAsync(workingDirectory, storeRef, …)   // ~8 lines
Commands/<Q>Command.RunAsync(opts, io, openSource)   → RENDER only
Web/<Q>Endpoint                                      → BuildAsync → DTO
```

Three rules make parity structural rather than tested:

1. A renderer references none of `FactPathFinder.`, `TraversalGraphLoader.`, `Reads.`, `cache.Get` or
   `cache.Put`.
2. Selection results are FIELDS, not inputs. A complete result returns `Confirmed` AND `ReverseOnly`,
   `HiddenIntrinsic`, `FromMatches` / `ToMatches`, `AsyncReachableEpCount`, `Frontier`. A renderer that hides
   something hides a field it was given.
3. Cache routing lives in `ComputeAsync`. Precedent: `ImpactEngine.DiffAsync` → `ResolveStoresAndCache`
   (`ImpactEngine.cs:88-98`).

`ImpactEngine` is the precedent for the engine-owns-the-cache part only. Its selection shape is the thing to
avoid copying: `FilterPerEpEffects` (`:530`), `EffectChangedEpCount` (`:545`) and `ClassifyStructuralCause`
(`:1464`) are OPTIONAL statics the web never calls, and optional selection IS the parity bug. `DiffAsync`
taking `RigDbContext` rather than `IQueryFactSource` is acceptable for a two-store diff and is left alone.

## What gets deleted

| removed | anchor |
| --- | --- |
| `CallersQueryService.BuildRoots`, `BuildEntryPointsAsync`, the graph load in `BuildAsync` — ~190 lines | `:134-166`, `:172-261`, `:74-116`; the `StoreQueryFactSource.Borrowing` use at `:104` goes with it |
| `PathQueryService.BuildAsync` body, except the effects stage | `:61-149` |
| the compute halves of `CallersCommand.RunAsync` and `RunEntryPointsAsync` — they MOVE into the service | `:200-505`, `:520-800` |
| the compute half of `PathCommand.RunAsync` — moves into the service | `:107-205` |
| two cache-routing bodies collapsing into one `LoadOrComputeAsync` | `TreeCommand.cs:314-433`, `TreeQueryService.cs:100-162` |
| `ImpactMapper`'s direct `art.Diff` reads | `ImpactMapper.cs` |
| `perEp.length` as a behavioral count | `main.js:622` |
| the hardcoded `["alloc","throw"]` | `store.js:293-294` |

## Children, in dependency order

1. [Impact selection into the engine](./cli-web-collapse-1-impact-selection-into-the-engine.md) — one
   `ImpactEngine.Select` consumed by the CLI, the CI gates and `ImpactMapper`. Carries the open D1 mechanism
   decision.
2. [Callers engine](./cli-web-collapse-2-callers-engine.md) — divergence sites 1 and 2, plus the depth lens,
   async probe and frontier as fields.
3. [Path engine](./cli-web-collapse-3-path-engine.md) — divergence site 4; one loader, one ambiguity
   computation.
4. [Tree cache routing](./cli-web-collapse-4-tree-cache-routing.md) — divergence site 3, one
   `LoadOrComputeAsync`.
5. [Reaches buckets](./cli-web-collapse-5-reaches-buckets.md) — small, optional; the classification stops
   being discarded.
6. [Renderer-purity test](./cli-web-collapse-6-renderer-purity-test.md) — the grep-level assertion that stops
   the regression. Lands last.

## Sequencing

- Child 1 is the only slice touching `RigApiEndpoints.cs`.
- Children 2-5 are pairwise disjoint and can run in parallel with each other and with child 1.
- Child 6 lands last, so it is green on arrival.
- Children 2 and 3 land after the reverse-walk base-seed expansion of 2026-09-02 (`FactPathFinder.cs`
  `SeedsFor`, `FactPathFinder.GraphIndex.cs` `InstantiationsByBase`), otherwise their byte-equality baselines
  move twice. That fix is in the working tree, so the condition is satisfied; it is recorded because it is
  the reason the order mattered.
- No slice needs a `FactPathFinder*` edit.

## Supersedes versus relates

**Absorbed by child 1** — same defect, two viewpoints. Both cards stay where they are, with the cross-reference
added in both directions:

- [CLI/web `impact` behavioral count differs by one](./cli-web-parity-2-impact-behavioral-count.md) — its root
  cause is child 1's optional-statics problem.
- [Web Impact has no effect filter](./impact-web-effect-filters-client-side.md) — the same omission seen from
  the web side.

**Relates, not superseded:**

- [CLI/web parity — wayfinder](./cli-web-parity-map.md) and
  [Seed, effect and filter disclosure on `/api/*`](./cli-web-parity-1-web-api-seed-and-effect-disclosure-parity.md)
  — this family removes the mechanism by which those gaps appear; the individual disclosures it names are
  still their own work.
- [`/api/meta` `derivationVersion` carries no store identity](./cli-web-parity-3-api-meta-derivation-version-lacks-store-identity.md)
  — a client-cache axis, untouched here. That file changed on 2026-09-02, so it is read current.
- [Two surfaces, one store, disagree on a derivation input — wayfinder](./question-vs-plan-map.md) and its
  children [1](./question-vs-plan-1-baked-call-edges-ignore-rules-edits.md),
  [2](./question-vs-plan-2-path-disclosures-computed-off-the-loaded-subgraph.md),
  [3](./question-vs-plan-3-redirect-rules-applied-asymmetrically-across-graph-paths.md) — child 3 makes
  question-vs-plan-2's symbol-universe fix a one-site change; it does not make it.
- [`rig impact` reports two different behavioral-EP counts](./impact-reports-two-different-behavioral-ep-counts.md)
  — the third count child 1 has to settle.

## Confidence

| claim | standing |
| --- | --- |
| the thesis verdict, and the accident/legitimate/temporary classification | verified against read code |
| line-count estimates on child 2 | approximate, read from ranges |
| the extra `echoactor …Inbox` row's attribution, intrinsic-only versus hazard-only | UNSETTLED; the procedure is on [cli-web-parity-2](./cli-web-parity-2-impact-behavioral-count.md) |
