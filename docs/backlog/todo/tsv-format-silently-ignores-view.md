## `--format tsv` silently ignores `--view` — `--view effects --format tsv` returns the whole tree

**Status:** todo
**Source:** demo prep, 2026-07-31 — hit while scripting per-EP effect counts; produced wrong numbers for
several minutes before the discrepancy was noticed
**Triage:** needs-info — choose rejection of `--view` with TSV or a real view-aware TSV projection; warning-only
is explicitly insufficient.

### Repro

```powershell
cd C:\Git\meddbase-analysis
rig tree "AI.SmartLetter.SendMessage" --view effects --store de69fd2ffc6b | head -1
#   From: AI.SmartLetter.SendMessage  (311 effectful method(s), source order)

rig tree "AI.SmartLetter.SendMessage" --view effects --format tsv --store de69fd2ffc6b | wc -l   # 7986
rig tree "AI.SmartLetter.SendMessage" --view full    --format tsv --store de69fd2ffc6b | wc -l   # 7986
```

The human-readable projection reports **311 effectful methods**; `--format tsv` with the SAME `--view
effects` emits **7,986 rows** — byte-identical to `--view full`. The view is dropped on the floor.

### Why this is worse than it looks

It fails **silently and plausibly**. A script that counts tsv rows to get "effects reachable from this EP"
gets the reachable-node count instead — a number that is wrong by ~25× here, but still monotonic in EP size,
so it looks like a believable ranking. Nothing in the output signals the view was ignored. In the case that
found it, the effects column of a per-EP ranking table came out exactly equal to the node column for every
row, which is the only reason it got caught.

Same trap applies to `--only` / `--exclude` effect filters if they're likewise render-side.

### Is it by design?

Partly, and that's the problem. The `--format` help says:

> `tsv` — machine-readable DFS rows … `llm` and `llm-ids` compose with `--view paths/full/effects` only.

So the help enumerates view-composition for `llm`/`llm-ids` and pointedly omits `tsv` — the behaviour is
arguably documented by omission. This mirrors the web API, where `RigApiEndpoints` returns one canonical tree
plus all effects and applies `view`/`only`/`exclude` **client-side** (deliberate — it avoids refetching on a
projection change). tsv is presumably the same canonical-rows contract.

But "documented by omission" is not a contract a scripter can discover, and `--view` is silently *accepted*
rather than rejected.

### Fix — pick one, don't leave it silent

1. **Reject the combination** (cheapest, honest): `--view effects|summary|hazards` + `--format tsv` errors up
   front the way the mutually-exclusive tree modes already do, pointing at `--format llm` for a projected
   machine format. Matches the existing precedent that `summary`/`hazards` are already rejected with
   `--format llm/llm-ids`.
2. **Honour the view in tsv** (most useful): project the rows, and add an `effect` column / emit only
   effect-bearing rows for `--view effects`, so tsv means what the flag says.
3. **At minimum**, warn on stderr and state in `--format`'s help text that tsv always emits canonical DFS
   rows regardless of `--view`.

(1) or (2). (3) alone still lets a piped script get a wrong number with no signal.

### Also

Counting rows is the obvious way to answer "how many effects does this EP reach", and there's currently no
cheap machine-readable route to that number at all — `rig derive --format tsv` emits whole-store `effect`
rows and per-EP `entrypoint` rows with **no reach or effect counts** on them, so ranking all 10,090 EPs by
reach means one `tree` invocation each (~8 s → ~22 h). A per-EP `symbols`/`effects` count in `derive`'s
`entrypoint` row would make "which endpoints are the wildest" a single query instead of an overnight job.
