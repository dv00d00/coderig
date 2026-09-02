# `tree` cache routing becomes one function

**Status:** todo · designed 2026-09-02, no code written · **Family:** query correctness / CLI-web parity

Shared rationale, the divergence inventory and the three renderer rules are on
[the wayfinder map](./cli-web-collapse-map.md). This card carries scope, ownership, verification and status only.

## Scope

The forest+`:loc` hit/miss decision exists twice: `TreeCommand.cs:314-433` (~120 lines, with a partial-hit
branch) and `TreeQueryService.cs:100-162` (~63 lines, without one). They collapse into one
`LoadOrComputeAsync(source, cache, key) → roots, effects, locations, graph?`.

Present effect of the divergence: a web forest-hit with a `:loc` miss recomputes the whole forest instead of
reloading the graph. Because both surfaces share `RenderSidecarKey.Locations()`, a CLI cold run already warms
the web's full hit.

The CLI's filter-keyed seam sidecar (`:seam:<sig>`, `TreeCommand.cs:349,810`) is a CLI-only render artifact and
stays in the command.

## Owns

`Services/TreeQueryService.cs`, `Commands/TreeCommand.cs`. The endpoint is unchanged.

## Verification

- `WebTreeCacheKeyTests`, `TreeFoldTests`, `TreeElidedGuardedEdgeTests`.
- CLI cold and warm runs byte-identical on a playground.
- After a CLI cold run, the web request is a full hit: asserted by the absence of a graph load in the phase
  timer.

## Sequencing

Disjoint from children 1, 2, 3 and 5, so it can run in parallel with any of them. No `FactPathFinder*` edit.
