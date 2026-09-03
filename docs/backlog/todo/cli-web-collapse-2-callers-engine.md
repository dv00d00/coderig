# `callers` gets one engine, and the command becomes a renderer

**Status:** todo · designed 2026-09-02, no code written · **Family:** query correctness / CLI-web parity
**Triage:** ready-for-agent

Shared rationale, the divergence inventory and the three renderer rules are on
[the wayfinder map](./cli-web-collapse-map.md). This card carries scope, ownership, verification and status only.

## Scope

`ComputeAsync` moves out of the command into the service; `BuildAsync` becomes thin. Two divergence sites
collapse: roots forward-verify (`CallersCommand.cs:340-357` versus `CallersQueryService.cs:148-165`) and the
EP touching-set dedup plus forward-verify (`CallersCommand.cs:588-596,753-777` versus
`CallersQueryService.cs:202-210,217-243`). The default depth-tagged lens (`CallersCommand.cs:420-505`) and the
async-hint probe with its frontier (`CallersCommand.cs:620-720`) become fields on the result.

Deleted: `CallersQueryService.BuildRoots` (`:134-166`), `BuildEntryPointsAsync` (`:172-261`) and the graph
load in `BuildAsync` (`:74-116`) — roughly 190 lines, and the `StoreQueryFactSource.Borrowing` use at `:104`
goes with them. The compute halves of `CallersCommand.RunAsync` (`:200-505`) and `RunEntryPointsAsync`
(`:520-800`) move rather than disappear. Line counts here are approximate, read from ranges.

Today three policies exist for one partition: web roots mode keeps reverse-only rows flagged
(`CallersEndpoint.cs:338`), web EP mode drops them (`CallersQueryService.cs:242`), the CLI hides both by
default. After this slice the partition is one field and each surface projects it.

## Owns

`Services/CallersQueryService.cs`, `Commands/CallersCommand.cs`, `Web/CallersEndpoint.cs`,
`Web/CallersContracts.cs`. EP rows gain `ForwardConfirmed`; the response gains `AsyncReachableEpCount` and
`Frontier`.

`FactPathFinder` is used through its existing API only — no edits there.

## Verification

- `LivePathCallersTests.Live_callers_*` and `Live_callers_entrypoints_equals_the_store_answer` — byte-equality,
  so moved code does not reorder output.
- `EntryPointCacheRoutingTests.Callers_entry_point_lens_*`.
- `CallersLambdaLabelTests`.
- New: `/api/callers?mode=entrypoints` returns the same set as
  `rig callers --entrypoints --format tsv --include-reverse-only`.

## Sequencing

Lands after the reverse-walk base-seed expansion of 2026-09-02 (`FactPathFinder.cs` `SeedsFor`,
`FactPathFinder.GraphIndex.cs` `InstantiationsByBase`), otherwise the byte-equality baselines move twice. That
fix is already in the working tree.

Disjoint from children 1, 3, 4 and 5, so it can run in parallel with any of them.
