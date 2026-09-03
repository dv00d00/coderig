## Store does not record the rig build that produced it — cross-version `impact` diffs silently lie

**Status:** todo
**Source:** demo prep, 2026-07-31 — found while assembling a peer demo on the MedDBase store
**Triage:** ready-for-agent

### The problem

`impact` diffs two stores. If those stores were produced by rig builds whose **extraction** differs, the
guard-condition delta is dominated by tool-version artifacts rather than code changes — it reports guard
changes in files the branch never touched.

Proof it's the tool and not the code: `EditStockRecordType.cs` has an **identical blob hash**
(`5c1c770eb0b7ebee2422b975a78e36e4589f11d4`) at both `e8858aa90e02` and `8cebdcf183e4`, yet its derived
guards differ between stores indexed either side of the 2026-07-27 guard work
(`ed01bba8` polarity, `6a520524` lambda/method-group guards):

```
pre-fix store:   !X && type != Y^_1^^!X && type != Y^_0^^records.Any()^_0    <- same predicate, BOTH arms
post-fix store:  !X && type != Y^_0^^records.Any()^_0                        <- deduplicated
```

Both arms of one predicate is a contradiction (always false), so the pre-fix rows are simply wrong. On the
real MedDBase pair that produced **1472 guard-delta rows** (1 narrowed / 136 widened / 1335 changed) on a
67-file branch, almost entirely spurious.

### Why the existing guard doesn't cover it

There IS a heuristic warning (`WARNING: the two stores disagree on guarded lambda edges (N base vs M head)`)
and it fires correctly on some pairs. But it only probes the **lambda-guard axis**, so it MISSED the
`e8858aa90e02-dirty` → `8cebdcf183e4` pair, which is cross-version on the *polarity/dedup* axis while
agreeing on lambda-guard counts. A per-axis heuristic can't be complete — there will always be a next axis.

### The fix

Stamp the producing build into the store and compare it directly.

- `runs` currently has: `Id, CreatedAtUtcText, SolutionPath, SymbolCount, ReferenceCount,
  DiRegistrationCount, ProjectIdentity, SourceProjectPath, SourceCommit, SourceBranch, SourceDirty`.
  No tool-version column. `meta` holds only schema version (`3`).
- Add an **extraction version** — not the informational assembly version, but a constant bumped whenever
  `FactExtractor` output changes shape or content (same discipline as the `*Schema` constants in
  `QueryCacheKeys`, and with the same `// vN->vM: why` trail).
- `impact` compares the two stores' extraction versions up front and warns unconditionally on mismatch —
  replacing the per-axis heuristic with an exact check. Consider gating `--expect-no-guard-narrowing` on it
  outright, since that verdict is meaningless across versions.

Storing the informational build string (`0.1.1-ci.<stamp>+<sha>`) alongside is cheap and useful for
diagnostics, but must NOT be the comparison key — it changes on every recompile and would flag every pair
(the same trap that got the MVID hedge removed from the cache keys on 2026-07-06).

### Fingerprinting recipe (until the column exists)

```sql
SELECT COUNT(*) FROM call_edges
WHERE (FromSym LIKE '%~λ%' OR ToSym LIKE '%~λ%')
  AND EnclosingGuards IS NOT NULL AND EnclosingGuards <> '';
```

On the MedDBase store this cleanly separates the two clusters (~4.7k pre-fix vs ~11.9k post-fix). Only pair
stores within a cluster.
