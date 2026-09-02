# A renderer-purity test keeps the collapse from unwinding

**Status:** todo · designed 2026-09-02, no code written · **Family:** query correctness / CLI-web parity

Shared rationale and the three renderer rules are on [the wayfinder map](./cli-web-collapse-map.md). This card
carries scope, ownership, verification and status only.

## Scope

A new test reads `Commands/{Callers,Path,Tree,Reaches,Impact}Command.cs`, `Web/*Endpoint.cs` and
`Web/ImpactMapper.cs` as text, and asserts that none of them contains `FactPathFinder.`,
`TraversalGraphLoader`, `Reads.`, `cache.Get(` or `cache.Put(`.

Grep-level, no Roslyn. The test encodes rule 1 from the map, which is the rule that made the four divergence
sites possible in the first place.

## Owns

`tests/Rig.Tests/Cli/RendererPurityTests.cs`, new file. Nothing else.

## Verification

The test is green on arrival. That is the whole acceptance check, and it is why this slice lands LAST — before
children 1 to 5 it would fail by design.

## Sequencing

**Blocked by:** [child 1](./cli-web-collapse-1-impact-selection-into-the-engine.md),
[child 2](./cli-web-collapse-2-callers-engine.md), [child 3](./cli-web-collapse-3-path-engine.md),
[child 4](./cli-web-collapse-4-tree-cache-routing.md), [child 5](./cli-web-collapse-5-reaches-buckets.md).
