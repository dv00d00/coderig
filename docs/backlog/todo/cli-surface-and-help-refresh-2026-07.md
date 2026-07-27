# CLI surface + help/docs refresh — inconsistent globals, fatal-on-stale-store, undocumented `llm` format

**Status:** todo · **Priority: MEDIUM** (each item is small; together they are most of the friction in a real review session) · **Found:** 2026-07-27 (MedDBase MR !11025 review session) · **Family:** CLI / docs
**Related:** [[cli-ux-file-paths-and-boundaries]], [[pattern-resolution-divergence-tree-vs-reaches]], [[impact-usability-parity-filter-and-alloc-noise]]

Framing from the owner: rig is moving from toy project to actually-useful tool; **help and skill docs are
outdated and the API surface is overloaded.** These are the concrete instances hit in one session.

## 1. `--rules` is not a real global option — ✅ RESOLVED (documented) 2026-07-27

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

## 4. `--format llm` is undocumented and has no legend — ✅ NOT REAL (header IS emitted; contract WAS documented) 2026-07-27

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

## 7. `deployments.json` resolution failure prints on every command — ✅ NOT REAL (already stderr) 2026-07-27

```
deployments.json: host project not found for 'MedDBase.LoggingService': src/logging/…csproj
```
Emitted on each `impact` run, interleaved into TSV output (it is line 1 of a machine-readable stream —
breaks naive parsers). Ask: send to stderr, warn once, and validate at load with a summary.

## 8. `rig dead` still advertised while disabled — ✅ FIXED 2026-07-27

SKILL/REFERENCE and help still surface `dead`; invoking it errors `'dead' was not matched`. Either hide it or
have it exit with an explicit "temporarily disabled — approximate via `callers <m> --roots`".

## 9. `dtb-cache` is path-keyed — document it (or content-key it) — ✅ DOCUMENTED 2026-07-27

Indexing the *same* solution from a second checkout path got **0% build-cache reuse**: `C:\Git\mdb-wt-4702`
(fresh worktree) = **10m33s**, versus the same commit range re-indexed from the original clone path
`C:\Git\meddbase-main-application-2` = **3m58s**. A ~2.7× penalty for using a `git worktree` — which is
exactly what the review workflow recommends for not disturbing the primary checkout.

Ask: document prominently ("index base and head from the SAME path to reuse `dtb-cache`"), and consider
keying cache entries by project content hash rather than absolute path.

---

## Resolutions (2026-07-27)

### 1 — documented, not made global (deliberate)

Audited the fact readers: `--rules` is accepted only by commands whose OUTPUT IS A FUNCTION OF THE RULES
(`index`/`derive`/`reaches`/`tree`/`callers`/`path`/`impact`/`entrypoints`/`dispatch-fans`). `symbols`/`refs`/
`files`/`runs`/`show`/`di`/`profile` read stored facts, so rules cannot affect their output.

**Adding a no-op `--rules` there would be WORSE than the error** — it would imply the rules shaped a result
they never touched. The full matrix (`--rules`, `--store`, `--format`) is now in REFERENCE.md § "Which commands
accept which global options", with the rule of thumb: *if the command derives, it takes `--rules`; if it just
reads stored facts, it does not.*

Genuine gap found while auditing and recorded there rather than fixed: `symbols`/`refs`/`files`/`di` have **no
`--format`**, so there is no TSV mode for the fact readers.

### 4 — NOT REAL: the header is emitted, and the contract was already documented

Verified against the installed binary:

```
$ rig tree "…PersonEventEntity.Save" --format llm --guards
depth	name	arity	calls	effects	flags	guards          ← header IS present, guards column included
0	PersonEventEntity.Save	2	1	llblgen:write,entity_cache:read*2,audit:write
```

`LlmSummaryRenderer` emits the header for all variants (6-col `paths`/`full`, 7-col `effects`, 8-col
`llm-ids`, `+guards`), and REFERENCE.md § "exact column contract" already specified every column plus the
`seen`/`depth-capped`/`budget-capped` semantics. The report's sample must have come from a capture that
dropped line 1.

**The real defect was the SKILL doc**, which asserted "No header row is emitted and the column contract is
undocumented" — actively misinforming, and the reason the false report was filed. Corrected in both skill
copies to state the header exists and point at the contract.

### 7 — NOT REAL: already on stderr

Only `derive` and `impact` pass a log writer to `LoadDeploymentsAsync`, and both pass `io.TextOutput.Error`.
The other five callers pass none (silent). So the warning never reaches stdout and cannot corrupt a TSV
stream. Confirmed in a live `impact` run: the message appeared in stderr, not in the 7,959-row stdout.

Left alone deliberately: "warn once + validate at load" is real but low-value (one line per unresolved
service; one service on MedDBase). Worth noting for later that five of seven callers swallow deployment-map
misconfiguration **silently** — arguably the more interesting inconsistency, but fixing it would ADD noise to
`reaches`/`tree`/`callers`/`path`/`entrypoints`, so it needs a decision rather than a patch.

### 8 — fixed with an explaining stub

`dead` was commented out in `Root.cs`, so it failed with System.CommandLine's `'dead' was not matched` —
reads like a typo or a broken install, while SKILL (1 mention) and REFERENCE (4) still pointed at it. It is
now a registered `DisabledCommand` stub: `[DISABLED]` in `--help`, and invoking it exits 2 with the reason
(all-hops dispatch superset no longer matched by the one-hop engine) plus the workaround
(`rig callers <m> --roots`). It accepts-and-ignores old arguments so an invocation copied from stale docs
still reaches the explanation instead of dying earlier on an unrecognized option.

Same disclosure principle as the intrinsic effect filter: a suppressed capability must teach its own escape
hatch rather than fail opaquely. Tests: `tests/Rig.Tests/Cli/DisabledCommandTests.cs`.

### 9 — documented with the measured penalty

REFERENCE.md § "Env gotchas" now leads with it, carrying the numbers (10m33s worktree vs 3m58s original path,
~2.7x) and the reason it is a trap: "use a worktree so you don't disturb your checkout" is good advice
generally and the expensive choice for indexing. Content-keying the cache is NOT done — that is a real change
to build-cache identity and wants its own item.

### Still open

- **5** (`callers --entrypoints` cannot distinguish unreachable from dynamic boundary) — needs frontier
  reporting; the largest remaining piece here and it caused a wrong conclusion mid-review.
- **6** — ✅ PARTIALLY FIXED (warning added) 2026-07-27. VERIFIED to interact with the new intrinsic default,
  and worse than the original report: `--view paths` keeps only paths reaching a surviving effect, so an edge
  whose own effect was filtered is PRUNED along with its `⎇` annotation. Measured on MedDBase
  `PersonEventEntity.Save`: **73 guarded edges with `--intrinsic`, 42 without** — the default alloc/throw
  hiding costs 31, because a call whose only effect is `alloc:object` (a delegate/closure allocation) is
  exactly the kind of edge that carries an interesting guard. So this now fires on the DEFAULT, not only under
  an explicit `--only`.

  `tree --guards` with any active effect filter now emits a stderr note naming the interaction and the escape
  hatch (`--intrinsic`). Deliberately unquantified — an honest count means building the forest twice, and
  naming the interaction is what prevents the wrong conclusion (same reasoning as the intrinsic note).

  Still open, the stronger fix from the original ask: RETAIN guarded edges on the path to matched effects
  instead of pruning them. That changes pruning semantics, so it wants its own design pass.

  Note for the record: an earlier check of `--format tsv` showed identical row counts (2,936 either way) and I
  read that as "structure unchanged". It is true for tsv, which does not prune, and false for the pretty
  renderer, which does.
