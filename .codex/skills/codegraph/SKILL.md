---
name: codegraph
description: Work on the codegraph Runtime Intelligence Graph repo. Use when modifying this repository's .NET 10 CLI, Roslyn/MSBuild analysis, declarative rules, immutable SQLite stores, live resident indexing, web explorer, playground fixtures, tests, backlog docs, or the in-repo rig operator skill.
---

# Codegraph

Read the repository-root `AGENTS.md` before acting. It contains the current orchestration, build, test,
dispatch, cache-invalidation, and MedDBase-calibration rules; do not duplicate or override them here.

## Sources of truth

Use current code and navigation, not archived milestone prose:

- CLI registration and command descriptions: `src/Rig.Cli/CommandLine/Root.cs`.
- Per-command options: each command's `Build` method or `dotnet run --project src/Rig.Cli -- <command> --help`.
- Product overview and concise command table: `README.md`.
- Documentation map: `docs/README.md`; vocabulary: `docs/ubiquitous-language.md`.
- Work state: files under `docs/backlog/{todo,progress,done}/`.
- Operator-facing `rig` skill: `.agents/skills/rig/{SKILL.md,REFERENCE.md}`.

When the sources disagree, treat `Root.cs` and the command builders as the executable API, then update
the docs or skill that drifted.

## Architecture

Preserve the two-stage model:

1. Roslyn/MSBuild extraction writes immutable, rule-agnostic facts.
2. Query-time derivation builds effects, observations, hazards, dispatch, and reachability without Roslyn.

Semantic binding belongs to Roslyn facts. Whole-program dispatch is an explicit over-approximation and
forward traversals use one-hop dispatch. Every derived effect's enclosing id must be a reachable graph
node; property, field, and event ids are not graph nodes.

The durable tier is the commit-scoped SQLite store. The live tier retains a Roslyn workspace and facts in
memory for working-tree queries; it does not replace immutable stores or `impact`.

## Current CLI surface

Registered command families are:

- Build/store: `index`, `graph`, `runs`.
- Fact/source inspection: `di`, `symbols`, `refs`, `show`, `files`, `profile`.
- Traversal: `path`, `tree`, `callers`, `reaches`, `dispatch-fans`.
- Derivation/diff: `derive`, `effects-diff`, `entrypoints`, `impact`.
- Hosts: `serve`, `watch`.
- `dead` is an explaining disabled stub until it uses the one-hop traversal engine.

Do not revive removed `mine`, `effects`, `trace`, or `callgraph` commands. Use `derive`,
`reaches`, `tree`, `callers`, and `path`.

`watch` currently serves live `reaches`, `path`, `callers`, and `tree` queries. `derive` remains
store-backed, and `impact` compares two immutable stores. Confirm the exact live subset in
`src/Rig.Cli/Live` before extending it.

## Repository map

- `src/Rig.Cli`: command routing, renderers, live host/client, web API, and browser assets.
- `src/Rig.Analysis`: solution loading, Roslyn extraction, rules, and resident workspace.
- `src/Rig.Domain`: dependency-light fact records and derivation/traversal functions.
- `src/Rig.Storage`: SQLite schema, focused reads/writes, and graph materialization.
- `tests/Rig.Tests`: TUnit tests and owned integration fixtures.
- `playgrounds/`: owned calibration fixtures and ignored external source checkouts.
- `docs/backlog/`: current work tracking.

Keep Roslyn dependencies out of `Rig.Storage`. Prefer declarative rules and shared derivation primitives
over framework-specific detector code.

## Change checks

- Extraction change: verify fact parity and re-index real calibration data.
- Derivation or cached payload change: bump the relevant `QueryCacheKeys.*Schema` constant and leave the
  one-line version trail described in `AGENTS.md`.
- Traversal/dispatch change: exercise one-hop dispatch and receiver narrowing tests.
- Live change: compare live answers with store-backed answers using anti-vacuous set equality; do not use
  counts alone.
- Browsable report, ranking, diff, or graph: decide and record the web follow-on at design time.

## Build and test

The test stack is TUnit on Microsoft.Testing.Platform:

```bash
dotnet build RuntimeIntelligenceGraph.slnx /p:UseSharedCompilation=false
dotnet test RuntimeIntelligenceGraph.slnx --no-build --no-restore /p:UseSharedCompilation=false
dotnet run --project tests/Rig.Tests --no-build -- \
  --treenode-filter "/*/*/<ClassName>/*"
```

Do not use `dotnet test --filter` for focused runs. Do not put MSBuild switches after the TUnit `--`.
Use `scripts/mini-ci.ps1` for the final format, build, full-test, pack, and global-tool reinstall flow.

Tests authored for a new feature belong in a new `<Feature>Tests.cs`, not the shared
`CliApplicationTests.cs`. Verify rendering assertions against actual `rig` output.

## Docs and skill maintenance

Update or add the relevant backlog card; the removed `docs/progress.md` and `docs/handover.md` are not
valid targets.

The canonical operator skill is `.agents/skills/rig`. Keep its command table aligned with actual help.
The installed `~/.codex/skills/rig` copy is disposable: reinstall by deleting the destination directory
and copying the canonical directory, as documented in `AGENTS.md`; never patch the installed copy.

Before committing, inspect `git status --short` and stage only the intended slice.
