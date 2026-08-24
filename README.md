# `rig` — Runtime Intelligence Graph

**Point it at a .NET solution. Get back a queryable map of what the code actually *does*.**

Not "who calls what" — *what happens*. Every DB write, cache read, HTTP call, blob upload, lock,
allocation and throw, attributed to the entry point that triggers it, with the branch condition that
gates it.

```powershell
rig index MySolution.slnx        # once — Roslyn + MSBuild → immutable SQLite facts
rig tree "CatalogApi.UpdateItem" # what does this endpoint touch?
rig callers "Cache.Invalidate" --entrypoints   # who can reach this?
rig impact --base <sha> --head <sha>           # what did my branch change, per endpoint?
rig serve                                      # …or click through all of it in a browser
```

Everything after `index` runs off the fact store — **no Roslyn, no rebuild, seconds not minutes.**

## Why you'd use it

- **"What breaks if I touch this?"** — reverse reachability from any method to the entry points that
  reach it, with the deployed service that runs them.
- **"What does this MR actually change?"** — `rig impact` diffs two indexed commits and reports the
  per-entry-point *behavioral* delta (effects added/removed), not just the file diff. `--expect-no-effect-change`
  is a CI gate for behavior-preserving refactors.
- **"Where are the landmines?"** — hazard detectors rank read-modify-write windows, dual writes, N+1
  reads in loops, `sync-over-async`, lazy-init races and cache-coherence gaps as *candidates* with
  disclosed confidence. Suspicion maps, never verdicts.
- **It's built for agents.** `--format llm` / `llm-ids` emit compact TSV with explicit parent/child
  linkage, so an LLM reviewer gets the call tree without 200 KB of box-drawing characters.

## Recent highlights

**🎛 Conditional compilation, resolved for real.** rig runs an actual MSBuild design-time build per
project and feeds Roslyn the *same* `DefineConstants` your compiler gets. `#if NET48` / `#if DEBUG`
branches are indexed exactly as they compile — not both arms, not neither.

**🎯 Multi-targeting that doesn't fall over.** Multi-TFM projects used to abort the index. Now rig
picks the first declared TFM that yields sources, or you pin one with `rig index --framework net10.0`.
The design-time-build cache is keyed per-TFM, so switching frameworks doesn't poison it.
*Honest limit:* one TFM per index — rig does not union all target-framework source sets.

**⎇ Control-flow graph — the must-run spine vs the guarded shell.** rig builds a Roslyn CFG for every
effect-bearing body and freezes each call site's **control-dependence guard set** into the facts.
`rig tree --guards` marks a conditional edge with `⎇ [predicate]`; unconditional edges carry none. So
"this endpoint reaches 54 effects" becomes "8 always run, 46 sit behind a branch — here's which."
Sugar-proof by construction: `a?.Save()`, `cond && Save()`, switch expressions and `when` patterns all
lower to the same branch shape, so nothing slips past a syntax walk. Cost: ~8% on top of binding.

**🌐 Web UI — `rig serve`.** The whole store, browsable: entry-point explorer with search, the effect
tree with hazard overlays, reverse-navigation drawers ("who reaches this", "EPs by service"), pivot
breadcrumbs, transparent refactoring-hotspot rankings, explicit entry-point behavior comparison, the
assembly-reference view, and a full impact-diff dashboard with live SSE progress on the cold diff.
Client-side IndexedDB cache keyed on the derivation version, so warm loads are instant.

**📦 Allocation effects.** Allocations — including compiler-lowered ones (closures, boxing, cached
delegates) — are first-class effects with shallow size and cardinality, so a `new` inside a `foreach`
shows up in the same tree as a DB write.

## Commands

`index` builds the store; everything else is **read-only** and runs from the directory holding `.rig/`.
Stores are **per-commit** — `--at <sha>` reads any previously indexed commit. `--rules <path>`
(repeatable) cascades extra rule files over the builtins.

| Command | What it does |
|---|---|
| `rig index <sln\|csproj>` | Roslyn+MSBuild → `.rig/<sha>/rig.db`. `--from <entry.csproj>` scopes to one project's closure · `--framework <tfm>` pins the TFM · `--include-tests` · `--merge` for multi-solution stores · `--time` for a per-phase breakdown · `--restore` runs the MSBuild Restore target per project (OFF by default — it dominated the build phase and rig indexes an already-built tree; pass it for an unrestored checkout). Builds internally — **no external pre-build needed.** |
| `rig graph` | Rebuild the derived call-graph views from facts — idempotent, no rescan |
| `rig runs` | What's indexed: per-commit stores, symbol/EP/effect counts |
| `rig entrypoints` | Rule-detected entry points, grouped by kind |
| `rig derive` | Re-derive effects + entry points from facts (no Roslyn). `--list-providers` dumps the effective vocabulary |
| `rig tree <pat>` | The call tree from an entry point. `--view paths\|full\|effects\|summary\|hazards` · `--guards` for branch conditions · `--async` for handoffs · `--format llm\|llm-ids\|tsv` |
| `rig reaches <pat>` | Flat list of effects reachable from an entry point |
| `rig callers <pat>` | Reverse reachability. `--entrypoints` (precise) or `--roots` (superset); `--no-cache` bypasses the cached entry-point derivation |
| `rig path <from> <to>` | One concrete call path between two symbols |
| `rig impact --base <sha> --head <sha>` | Two-store diff: per-EP effect + reach changes. `--structural` for the full ripple · `--expect-no-effect-change` as a CI gate |
| `rig effects-diff <a> <b>` | Symmetric difference of two entry points' effect sets |
| `rig hotspots` | Rank first-party methods by a chosen transparent metric: callers, callees, effects, density, hazards, amplification, or residual dispatch fan. No blended score |
| `rig serve` | The interactive web explorer (`--port`, default 5050) |
| `rig refs <pat>` | References to a symbol; or `--unused` / `--usage` for declared-but-unused assembly references |
| `rig symbols <pat>` | FTS-accelerated symbol search. `--format tsv\|json` exposes exact IDs and locations for scripts/agents |
| `rig show <pat>` | The **source** of one unambiguous declaration. Ambiguity fails closed and prints rerunnable exact names; `--all` opts into rendering matches, capped by `--limit <n>`. `--context <n>` adds surrounding lines. Reads the working tree only when it is provably the indexed revision (clean store, `SourceCommit` == HEAD, that file unmodified); otherwise reads the blob out of git and marks it `(from git <sha>)`. Never renders lines it cannot attribute — it prints the reason instead |
| `rig di` | MS DI registrations: service → implementation, lifetime, source |
| `rig dispatch-fans` | Diagnostic: dispatch hubs whose receiver failed to narrow the CHA fan-out, ranked by cause |
| `rig files --skipped` | Files excluded from analysis, and the rule that excluded them |
| `rig profile validate` | Validate the `rig.rules.json` profile for the current directory |

Patterns are case-insensitive substring matches over DocIDs (`M:Ns.Type.Method(args)`), exact-match-wins.
The depth options accept `--max-depth` (preferred), `--maxdepth`, or `--depth`.

Every immutable-store answer identifies the selected store and reports `current`, `STALE`,
`UNVERIFIABLE`, or `freshness unknown` on stderr. Machine-readable stdout remains pipe-safe. `rig runs`
already lists all stores and their provenance, so it does not repeat the per-answer disclosure.

## Effects

Effects are **rule data** (`rig.rules.json`), not baked-in code. The builtin set covers ~35 providers out
of the box: `efcore` · `db_command` / `db_connection` / `db_reader` / `db_transaction` · `redis` ·
`inproc_cache` · `http` · `soap` · `object_store` / `azure_blob` / `aws_s3` · `elasticsearch` /
`azure_search` · `rabbitmq` · `mediatr` · `actor` · `smtp` · `io` · `socket` · `process` · `lock` /
`async_lock` · `shared_state` · `app_state` / `session_state` · `config` · `parallel` · `alloc` ·
`throw` · and more.

Entry-point detectors are rule data too — `mvc` / `minapi` / `page` / `action`, plus classified async
handoff origins (`background` / `timer` / `actor` / `event`).

## Observations & guards

Structural context is appended to an effect line in brackets when a pattern is detected around the site:

| Observation | Trigger |
|---|---|
| `[looped_effect:foreach]` / `[looped_effect:parallel]` | Effect inside a loop / `Parallel.ForEach` |
| `[parallel_fanout:Task.WhenAll]` | Effect inside a `Task.WhenAll` |
| `[resilience_retry:…]` | Inside an EF Core `ExecutionStrategy` or Polly `ResiliencePipeline` |
| `[resource_span:using]` | Inside a `using` scope over a transactional resource |
| `[read_before_commit:before_commit]` | `SaveChanges*` preceded by a read in the same method — lost-update / TOCTOU candidate |
| `[concurrency_handled:DbUpdateConcurrencyException]` | `SaveChanges*` inside a concurrency catch — optimistic concurrency **is** handled |

`looped_effect` is additionally promoted to a **displayed finding tier of its own — "amplification"** — on by
default: `rig derive` prints an `Amplification (looped effects — structural inventory)` section after Hazards,
broken down by `provider:operation` (so a looped `http:POST` is as visible as a looped `llblgen:read`),
`rig tree --view hazards` marks it inline with `🔁`, and `--format tsv` emits it as its own `amplification` row
type. It is **not** a hazard: a looped effect is a structural FACT (the effect is lexically inside an iteration
context — no guess), whereas `n_plus_1` is a JUDGMENT about whether the key varies, so every hazard surface is
untouched. The displayed scope is rule data (`observations.amplification`) and ships staged to the
network-crossing providers, where ×N means N round trips; anything outside it stays a plain observation count.
`--no-amplification` turns the tier off. In `rig impact` it appears as the terse per-entry-point
`ep_amplification_added` / `ep_amplification_removed` rows — the only signal that a loop was wrapped around an
existing call, which leaves the effect set unchanged while multiplying the cost.

`tree --guards` adds the control-dependence layer on top: `⎇ [invoice.IsHealthcode]` on the guarded
edge, `!pred` for the else-arm, `&&`-joined for nested branches, nothing at all for the must-run spine.
Intra-method; guards stop at the source boundary rather than fabricating them for external frames.

## Example — eShop `PUT /items`

```
$ rig tree "CatalogApi.UpdateItem"

Callgraph: [12] minapi PUT /items (focused)
Nodes: 8 / 18 on effect paths
  CatalogApi.cs:93  minapi PUT /items
  └─ CatalogApi.cs:324  CatalogApi.UpdateItem
     ├─ CatalogAI.cs:28  CatalogAI.GetEmbeddingAsync
     │  └─ EFFECT ai_embeddings read  GenerateVectorAsync  IEmbeddingGenerator
     ├─ CatalogIntegrationEventService.cs:28  ...SaveEventAndCatalogContextChangesAsync
     │  ├─ ResilientTransaction.cs:11  ResilientTransaction.ExecuteAsync
     │  │  ├─ EFFECT db_transaction begin  BeginTransactionAsync  DbContext.Database  [resilience_retry:ExecutionStrategy]
     │  │  └─ EFFECT db_transaction commit  CommitAsync  IDbContextTransaction  [resilience_retry:ExecutionStrategy]
     │  └─ EFFECT efcore commit  SaveChangesAsync  CatalogContext
     ├─ CatalogIntegrationEventService.cs:11  ...PublishThroughEventBusAsync
     │  └─ EFFECT eventbus publish  PublishAsync  eShop.EventBus.Events.IntegrationEvent
     ├─ EFFECT efcore read  SingleOrDefaultAsync  CatalogContext.CatalogItems
     └─ EFFECT efcore commit  SaveChangesAsync  CatalogContext  [read_before_commit:before_commit]
```

That last `[read_before_commit]` is the point: the item is read with `SingleOrDefaultAsync` earlier in
the same method and written back with no concurrency token — a lost-update candidate that no call-graph
tool tells you about.

`rig reaches` gives the same information flat:

```
  efcore           read            CatalogContext.CatalogItems
  efcore           commit          CatalogContext  [x2]  [read_before_commit:before_commit]
  ai_embeddings    read            IEmbeddingGenerator<TInput, TEmbedding>
  db_transaction   begin           DbContext.Database  [resilience_retry:ExecutionStrategy]
  db_transaction   commit          IDbContextTransaction  [resilience_retry:ExecutionStrategy]
  eventbus         publish         eShop.EventBus.Events.IntegrationEvent
```

## Deployment attribution (`deployments.json`)

Opt-in. Drop a `deployments.json` next to `.rig/` and every rendered entry point gains a ▶ marker plus a
chip naming the deployed service(s) whose process runs it. Without the file, output is unchanged.

```jsonc
{
  "services": [
    { "name": "MedDBase",   "host": "src/main/MedDBase.Site/MedDBase/MedDBase.csproj", "kind": "iis", "provides": ["FrontEnd"] },
    { "name": "DataServer", "host": "src/data-server/MedDBase.DataServer/MedDBase.DataServer.csproj", "kind": "iis", "provides": ["DataServer"] },
    { "name": "PdfService2","host": "src/pdf2/PdfService2/PdfService2.csproj", "kind": "kube" }
  ]
}
```

- Query-side, **no re-index**: each service's entry csproj → transitive `<ProjectReference>` closure; an
  EP's source file → its owning csproj → the services whose closure contains it.
- **loaded-in vs active-in.** Closure membership is an upper bound — shared libraries fan out to every
  host. A **capability gate** refines it: a service declares opaque tokens it `provides`, an EP rule
  declares tokens it `requires`, and the EP is *active-in* a service iff the sets intersect. Ungated
  rules stay active wherever loaded, so the gate is strictly opt-in.
- Renders as `▶ echoactor SomeActor.Inbox  ⟦MedDBase (iis) · 1 linked-inactive⟧`, in `derive`,
  `entrypoints`, `callers`, `tree`, and the `reaches`/`path` From line, plus `service` / `activeService`
  TSV columns.

## Rules

Rules cascade — each layer merges over the previous:

1. Builtins (shipped with the tool)
2. `~/.rig/rig.rules.json` — user-global
3. `<solution-dir>/rig.rules.json` — solution-level
4. `<project-dir>/rig.rules.json` — per-project
5. `--rules <path>` — explicit, merged last (repeatable)

Teaching rig a new effect is a JSON object, not a code change:

```json
{
  "effects": [
    {
      "provider": "redis",
      "operation": "write",
      "methods": ["StringSetAsync", "KeyDeleteAsync"],
      "receiverTypes": ["StackExchange.Redis.IDatabase"],
      "resource": "receiver_type",
      "confidence": "high",
      "basis": "compilation+profile",
      "reason": "redis_write"
    }
  ],
  "files": {
    "exclude": [{ "id": "migrations", "glob": "**/Migrations/**", "reason": "db_migration" }]
  }
}
```

Predicates compose with `AND` — every clause present must match. Express `OR` as parallel rules.
`receiverTypes` walks `AllInterfaces`, so an interface rule fires for every implementer;
`declaringTypes` matches static/extension methods.

**`resource` values** control the effect's resource column:

| Value | Resolved as |
|---|---|
| `ef_dbset_receiver` | `DbContext.DbSetName` |
| `ef_context_receiver` | The `DbContext` type name |
| `argument_type` | Type of the first argument |
| `receiver_type` | Fully-qualified type of the receiver |
| `http_argument` | First argument (URL string) |
| `string_argument` | First argument when it is a string literal |

## Playgrounds

| Playground | Entry points | Effects | Index time |
|---|---|---|---|
| `EntryPointEffects` | 8 | ~23 | ~10 s |
| `eShop` | 41 | 100 | ~30 s |
| `OrchardCore` | 296 | 788 | ~5 min |

## Working on rig

```powershell
.\scripts\mini-ci.ps1    # format → build → all tests → pack → reinstall the global tool
.\scripts\format.ps1     # format only (-Check for verify-only)
```

Contributor conventions — TDD slices, rule-first extraction, the two-stage design — live in
[docs/contributing.md](docs/contributing.md). Vocabulary: [docs/ubiquitous-language.md](docs/ubiquitous-language.md).
Work tracking: [docs/backlog/](docs/backlog/README.md). Agent notes: [AGENTS.md](AGENTS.md) / [CLAUDE.md](CLAUDE.md).
