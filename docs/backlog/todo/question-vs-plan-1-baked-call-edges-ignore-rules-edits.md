# A `handoffDispatchers` edit without a re-index changes `derive` but NOT `reaches`/`tree`/`path` — and can half-apply inside ONE command

**Status:** todo · **Priority: HIGH** (same store, same rules, two different answers — and no signal to the user;
the reach set itself moves, not just an annotation) · **Found:** 2026-08-21, while single-sourcing the fact
projections ([live-background-index](../done/live-background-index.md)) · **Family:** cache-invalidation /
query correctness

## The bug

Graph SHAPING is baked into the materialized `call_edges` table at index time. `IndexCommands.MaterializeGraphAsync`
feeds `GraphMaterializer` a graph from `FactGraphProjection.FromAnalysis(result, rules.Handoff, rules.Redirect)`,
so `call_edges.Kind` already carries `handoff` where the *then-current* rules said so.

`Reads.LoadFactGraphAsync` — the whole-store query-time path — instead re-runs
`HandoffClassifier.Classify(callEdges, handoffRules)` against the rules in force NOW.

So after editing `handoffDispatchers` in `rig.rules.json` **without** re-running `rig index` / `rig graph`:

- `reaches` / `tree` / `path` / `callers` take the bounded SQL path over `call_edges` and serve the **baked**
  classification — the rule edit has no effect;
- `derive`'s shaped graph **re-classifies** and honours it.

## Measured evidence

On `playgrounds/LegacyNet48Web`, which has a `RunNow(SyncTransform)` site — a method group handed to a
NON-dispatcher, deliberately a sync edge, whose callback performs an `llblgen:fetch`. A rules overlay added one
dispatcher with `consumerPatterns: [".SchedulerZoo.RunNow"]` — a rules change with **no re-index** — then the
same `tree … --view full --no-cache` was run against two stores holding IDENTICAL facts:

| store | path taken | rule honoured? |
|---|---|---|
| graph-materialized (normal) | bounded SQL over `call_edges` | **No** — `SchedulerZoo.SyncTransform` and its `📥 llblgen:fetch` stay synchronously reachable; output byte-identical to the no-rule run |
| `--no-graph` | EF fallback → `HandoffClassifier` at query time | **Yes** — the edge is reclassified `handoff`, sync-cut, and both the method and its effect disappear |

Control: with no overlay the two stores produce identical output, so the difference is the classification
SOURCE, not the facts.

**Worse than a clean split: one command can be half-applied.** Within a single `derive` run, the handoff-EP
listing reads the baked `call_edges.Kind` (via the `GraphAvailableAsync` fast path) while hazards and
event-cycles use the re-classified EF graph. So one report can mix both classifications.

## Why nothing catches it

The store stamps only a graph SCHEMA version (`SchemaMeta` / `SchemaGate.GraphAvailableAsync`). `RulesFingerprint`
exists but is used **only** in query-cache keys (`QueryCacheKeys`), which do not gate `call_edges` — the table is
index OUTPUT, not a cache, so the rules axis of the documented three-axis hedge does not reach it. No documented
requirement to re-run graph materialization after a rules edit.

Same reasoning applies to `redirectRules`, `factoryRules` and the delivery edges — all baked at materialize time.

## Open question — the fix shape

The primitive is already there — `RulesFingerprint`, unused outside `QueryCacheKeys` (see *Why nothing catches
it* above). What is not chosen is the shape:

- **O1 — fail closed.** Stamp the rules fingerprint into `SchemaMeta` at graph build and make `HasGraphAsync` /
  `GraphAvailableAsync` return false when it does not match the rules in force. The bounded path then degrades
  to the EF fallback — slower, but correct — instead of silently serving a stale shape. Cost: the fast path is
  unavailable after any rules edit until the graph is re-materialized. Reuses the existing `RulesFingerprint`
  primitive and matches how the disk cache already treats a rules edit.
- **O2 — make re-materialization the documented answer.** Make `rig graph` cheap enough that re-running it is
  the recommended response to a rules edit, and say so in the rules docs. Cost: correctness then rests on the
  user doing it — nothing detects the stale shape, so the half-applied `derive` report above stays possible.
- **O3 — both.** Fail closed for correctness, cheap re-materialization so the degraded arm is short-lived.
  Cost: the largest of the three slices.

## Acceptance

1. Edit a `handoffDispatchers` rule on an indexed store without re-indexing. `rig tree`/`reaches` reflect the
   edit (or explicitly disclose that the graph is stale) rather than silently serving the baked classification.
2. `rig derive` cannot mix baked and re-classified edges within one report.
3. A test that pins it: same facts, a rules edit, bounded and fallback paths agree.

## Related

- Third instance of the same family in two days: [`/api/meta` derivationVersion](../done/cli-web-parity-3-api-meta-derivation-version-lacks-store-identity.md)
  (client vs server) and [bounded reach inputs dropping `EnclosingScopes`](../done/bounded-reach-inputs-drop-enclosing-scopes-shipped.md)
  (bounded vs whole-store). Each is "two surfaces, one store, different answers"; the common cause is a
  derivation input that one path folds in and another does not.
- CLAUDE.md's cache section documents store identity / rules fingerprint / `*Schema` as the whole hedge. This is
  a case where the RULES axis is missing from a non-cache artifact that behaves like one.
- [CLI/web collapse onto one engine per question](./cli-web-collapse-map.md) — relates only. That family
  collapses duplicated query-side compute so a derivation input is folded in one place; the baked-graph axis
  here is index output and is unaffected by it.
