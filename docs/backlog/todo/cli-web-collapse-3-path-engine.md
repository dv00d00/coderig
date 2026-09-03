# `path` gets one engine over one loaded graph

**Status:** todo · designed 2026-09-02, no code written · **Family:** query correctness / CLI-web parity
**Triage:** ready-for-agent

Shared rationale, the divergence inventory and the three renderer rules are on
[the wayfinder map](./cli-web-collapse-map.md). This card carries scope, ownership, verification and status only.

## Scope

Today one question runs over two separately loaded graphs: `PathCommand.cs:127-143` uses
`LoadShapedTraversalGraphAsync` / `LoadDemandForwardPathGraphAsync`, `PathQueryService.cs:75` uses
`LoadEffectReachInputsAsync`. Same `FactPathFinder.Find`, two loaders that can disagree.

`ComputeAsync` owns the load, the traversal and the disclosures, and takes a `withEffects` flag; the CLI passes
false, which is the render difference the map classifies as legitimate. The `PathQueryService.BuildAsync` body
(`:61-149`) is deleted except the effects stage; the compute half of `PathCommand.RunAsync` (`:107-205`) moves
into the service.

Ambiguity and the `Fact graph:` line are computed in ONE place after this slice.

## Owns

`Services/PathQueryService.cs`, `Commands/PathCommand.cs`, `Web/PathEndpoint.cs`.

Not owned, deliberately: `LiveQueryFactSource` and any new `IQueryFactSource` member. The symbol-universe fix
for
[`path` disclosures computed off the loaded subgraph](./question-vs-plan-2-path-disclosures-computed-off-the-loaded-subgraph.md)
needs both, so it stays its own card. This slice makes that fix a one-site change instead of a two-site one.

## Verification

- `LivePathCallersTests.Live_path_*`.
- `DemandLivePathTests`.
- `LivePathDemandPreparationTests`.
- New: `/api/path` node ids equal the `symbolId`s from `rig path --format tsv`.

## Sequencing

Lands after the reverse-walk base-seed expansion of 2026-09-02 (`FactPathFinder.cs` `SeedsFor`,
`FactPathFinder.GraphIndex.cs` `InstantiationsByBase`), otherwise the byte-equality baselines move twice. That
fix is already in the working tree.

Disjoint from children 1, 2, 4 and 5, so it can run in parallel with any of them. No `FactPathFinder*` edit.
