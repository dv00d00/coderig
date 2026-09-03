# `reaches` bucket classification becomes part of the result

**Status:** todo · designed 2026-09-02, no code written · **Family:** query correctness / CLI-web parity
**Triage:** ready-for-agent

Shared rationale and the three renderer rules are on [the wayfinder map](./cli-web-collapse-map.md). This card
carries scope, ownership, verification and status only.

## Scope

Bucket classification is CLI-only at `ReachesCommand.cs:238-240`, while the data it needs is already on
`ReachInfo.HandoffVia` / `DispatchVia` and `ReachesQueryService.cs:60-71` discards it. The classification moves
onto `ReachesComputation`, the command projects it, and the endpoint gains it.

`reaches` already shares a compute core (`ReachesCommand.cs:145` → `ReachesQueryService.ComputeAsync`), so this
slice is small and optional. It is the cheapest demonstration that a classification belongs to the result.

## Owns

`Services/ReachesQueryService.cs`, `Commands/ReachesCommand.cs`, `Web/ReachesEndpoint.cs`,
`Web/ReachesContracts.cs`.

## Verification

CLI `rig reaches` output byte-identical before and after on a playground, and `/api/reaches` carries the same
bucket per row as the CLI prints.

## Sequencing

Disjoint from children 1, 2, 3 and 4, so it can run in parallel with any of them. No `FactPathFinder*` edit.
