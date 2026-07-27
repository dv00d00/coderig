# CLI surface + help/docs refresh — inconsistent globals, fatal-on-stale-store, undocumented `llm` format

**Status:** todo · **Priority: MEDIUM** (each item is small; together they are most of the friction in a real review session) · **Found:** 2026-07-27 (MedDBase MR !11025 review session) · **Family:** CLI / docs
**Related:** [[cli-ux-file-paths-and-boundaries]], [[pattern-resolution-divergence-tree-vs-reaches]], [[impact-usability-parity-filter-and-alloc-noise]]

Framing from the owner: rig is moving from toy project to actually-useful tool; **help and skill docs are
outdated and the API surface is overloaded.** These are the concrete instances hit in one session.

## 1. `--rules` is not a real global option

```bash
rig symbols "DocumentPreviewBuilder" --kind method --rules ./rig.rules.json
#   Unrecognized command or argument '--rules'
```
`index`/`derive`/`reaches`/`tree`/`callers`/`impact` accept it; `symbols` does not. Either make it global or
document per-command support. Same audit needed for `--store`, `--format`, `--no-cache`.

## 2. `rig runs` aborts on the FIRST stale-schema store instead of listing the healthy ones — ✅ FIXED 2026-07-27

```bash
rig runs
#   Runs (7 store(s) in C:\Git\meddbase-analysis\.rig)
#     store 25c5b5df3394  … symbols=439075 references=2408506 di=202
#     store 3312a82f2614
#   Store schema v1, this rig expects v3 — re-index …          ← exit 2, output truncated
```
One old store makes the **health-check command** unusable — and health-check is step 1 of the documented
workflow. It should mark stale stores (`⚠ schema v1 — re-index`) and continue. Consider `rig runs --prune`.

## 3. Stale stores silently poison `impact` selection — ✅ FIXED 2026-07-27

`rig impact --base 57d16d0afc4f …` failed the same way. Since `impact` is the review command and old base
stores accumulate, surface schema compatibility **in the store listing** and fail with the fix
("`rig index <sln>` at that commit") rather than a generic message.

### Fix for 2 + 3

Both were one root cause with two symptoms: the schema gate throws at store-open, and `runs` had the open
inside its enumeration loop with no per-store recovery, so the first stale store aborted the listing (exit 2)
and the healthy stores past it were never printed.

- **`runs` is resilient per store** (`FactCommands.BuildRuns`): `RigStoreException` is caught per store, the
  store is marked `⚠ unreadable — <message>`, and enumeration continues. A trailing summary reports the
  scale (`5 of 9 store(s) unreadable: …`) for a reader who only sees the tail. **Exit 0** even with stale
  stores present — the listing succeeded and reported them; the non-zero exit is what broke the health check.
- **Every schema failure now NAMES the store** (`SchemaGate.AssertReadableAsync`) via `connection.DataSource`.
  This is item 3: `impact` opens TWO stores and other paths resolve one implicitly from `.rig/LATEST`, so a
  bare "store schema v1, expects v3" left the user guessing which to re-index — and CommandGuard's fallback
  attribution reported the DEFAULT store path rather than the one actually opened. The `EnclosingGuards` drift
  probe already named its store; the version gate did not.

**Verified on the real MedDBase workspace** (which has 5 genuine v1 stores): pre-fix `rig runs` died after 2
stores; now all 9 list, the 5 stale ones are marked with their exact db path, exit 0 — and it reveals the 4
usable stores, including both MR stores, which the abort had been hiding.

**Regression tests:** `tests/Rig.Tests/Cli/RunsStaleStoreTests.cs` — playground index + synthetic unreadable
stores whose ids sort FIRST (the ordering that killed the listing). Verified red→green: the resilience and
store-naming tests fail pre-fix; a third negative-control test asserts a fully-healthy set emits NO warning,
so the marker can't decay into noise.

Not done: `rig runs --prune`. Deleting store dirs is destructive and the summary now tells you exactly which
ones, so the manual `rm` is a one-liner — left as a deliberate non-goal rather than an oversight.

## 4. `--format llm` is undocumented and has no legend

The LLM-oriented tree format is the best output mode for agent use — compact, columnar, greppable — but it
appears in neither the skill nor `--help` beyond a bare mention, and emits **no header row**, so the columns
must be reverse-engineered:

```
4	TransactionDependency.Call	3	1	alloc:object,throw:raise*3
1	TransactionDependency.Call	3	1	alloc:object	seen	!!IsPersonMerge
```
Asks: document the column contract; add a `#` header line (or `--legend`); state the `seen` marker's
meaning; note that `--guards` appends a trailing column. Same for `llm-ids`.

## 5. `callers --entrypoints` cannot distinguish "unreachable" from "dynamic boundary"

```bash
rig callers "DocumentPreviewBuilder.GetUnsafe" --entrypoints
#   No rule-detected entry points reach 'DocumentPreviewBuilder.GetUnsafe'.
```
…while plain `callers` returns a correct **18-method** chain. The chain simply tops out at
`PersonEventHtmlService.ToString` / `DocumentView.PreviewBody`, which reach real EPs only through
**Dom/template interpolation** (`{MedicalRecord.Documents}` resolved reflectively). The zero is *correct* but
reads as "dead code".

Ask: report the frontier — `no EP edge; chain tops out at: PersonEventHtmlService.ToString,
DocumentView.PreviewBody (suspect dynamic dispatch / template interpolation)`. This is the reverse-direction
twin of the D4 boundary-marker item in [[cli-ux-file-paths-and-boundaries]], and it cost a wrong conclusion
mid-review (I attributed the gap to lambdas; rig in fact models lambdas fine — `~λ0` nodes appear in the
chain).

## 6. `--only <provider>` silently hides the edge you need when hunting guards

`rig tree X --guards --only audit` dropped the `TransactionDependency.Call` edge that **carried** the guard,
because that edge's own effect is `alloc:object`. Correct filtering, wrong outcome: the user asked for guards
and the filter removed them. Ask: when `--guards` is present, retain guarded edges on the path to matched
effects, or warn (`--only may hide guarded edges; N guarded edges suppressed`).

## 7. `deployments.json` resolution failure prints on every command

```
deployments.json: host project not found for 'MedDBase.LoggingService': src/logging/…csproj
```
Emitted on each `impact` run, interleaved into TSV output (it is line 1 of a machine-readable stream —
breaks naive parsers). Ask: send to stderr, warn once, and validate at load with a summary.

## 8. `rig dead` still advertised while disabled

SKILL/REFERENCE and help still surface `dead`; invoking it errors `'dead' was not matched`. Either hide it or
have it exit with an explicit "temporarily disabled — approximate via `callers <m> --roots`".

## 9. `dtb-cache` is path-keyed — document it (or content-key it)

Indexing the *same* solution from a second checkout path got **0% build-cache reuse**: `C:\Git\mdb-wt-4702`
(fresh worktree) = **10m33s**, versus the same commit range re-indexed from the original clone path
`C:\Git\meddbase-main-application-2` = **3m58s**. A ~2.7× penalty for using a `git worktree` — which is
exactly what the review workflow recommends for not disturbing the primary checkout.

Ask: document prominently ("index base and head from the SAME path to reuse `dtb-cache`"), and consider
keying cache entries by project content hash rather than absolute path.
