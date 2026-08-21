# `rig watch` boots on a tree that does not compile, reports "all projects reconciled", and answers 0 — plus 2.4M error lines to stdout

**Status:** todo · **Priority: CRITICAL** (the tool's entire contract is that it states facts and discloses its
limits; here it states a confident wrong answer with a clean bill of health) · **Found:** 2026-08-21, real-data
run on a fresh MedDBase clone · **Family:** disclosure / resident index

## What happened

`rig watch <MedDBase.slnx> --once --query "callers SmartLetter.SaveLetter"` on a freshly-cloned, **unrestored**
checkout (`meddbase-main-application-3`):

```
watch: cold boot in 73.2s — 227 project(s), workspace retained
live: facts current as of 0 file(s) applied | all projects reconciled
Methods that reach 'SmartLetter.SaveLetter': 0
live: derived layer built this generation: traversalGraph 1367.6ms | eventSites 93.4ms | epData 247.2ms
```

That reads as a clean, complete, fast answer. It is not. The same run emitted **2,387,334 compiler error lines**:

| count | code | meaning |
|---|---|---|
| 1,793,241 | CS0518 | `Predefined type 'System.Object' is not defined` |
| 317,203 | CS0246 | type or namespace not found |
| 95,090 | CS0103 | name does not exist in the current context |
| 71,342 | CS1061 | member does not exist |
| 53,999 | CS0234 | namespace member missing |

1.79M CS0518s mean **no references resolved at all** — the clone was never restored, so every compilation is
effectively empty. The `0` is not "nothing calls this method". It is "there was no code".

**Nothing in the output distinguishes those two.** The status line actively asserts health:
`all projects reconciled`.

## The three defects

1. **No failed-compilation disclosure.** The boot proceeds, the status line claims all projects reconciled, and
   every subsequent answer is served as though the facts were sound. This is the disclosure work specced in
   `docs/spikes/failed-compilation-disclosure-spec.md` and filed as
   [failed-compilation-disclosure](failed-compilation-disclosure.md) — it was approved and never implemented.
   **This run is the argument for doing it before any further query-surface work:** every live answer on a tree
   that does not build is currently untrustworthy and silent about it.
2. **Error output is uncapped and unrouted.** 2,387,334 lines / **528 MB** written to raw stdout
   (`SolutionSourceLoader.cs:247`), interleaved with the answer. It drowns the result, it is not on stderr, and
   there is no cap or summary. A per-project first-N + a total count belongs here.
3. **`rig watch` cannot restore.** `SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync` accepts `restore` (default
   `false`) but `WatchCommand` never passes it and exposes no flag, so there is no way to boot a resident index
   on an unrestored checkout. `rig index` has `--restore` (opt-in since `eb6480ff`). The parity gap is silent.

## Why the disclosure has to be per-project, not global

227 projects booted; the failures are concentrated in specific ones. A global "something failed" banner would be
useless noise on a partially-broken tree, and a per-FILE marker is what the spec already argues for. The
resident case adds a requirement the one-shot case did not have: the disclosure must **persist across
generations** until the compilation actually recovers, and it must be attached to the ANSWER, not only to the
boot log — a boot banner scrolled past 30 seconds ago is not a disclosure on this answer. `rig watch` already
established that principle for staleness (`N project(s) unreconciled` is prefixed to every answer); compile
health belongs in the same line.

## Acceptance

1. On the unrestored clone above, the status line does NOT say "all projects reconciled", and the `callers`
   answer carries a marker that its facts come from projects that failed to compile — naming how many.
2. `rig watch --restore` exists and, used on that clone, produces a clean boot with no disclosure.
3. Error output is capped with a total count, and goes to stderr, so the answer is readable.
4. A test with a deliberately-broken project asserts the disclosure appears, and disappears once fixed (the
   broken -> fixed -> broken cycle the disclosure spec already calls for, which the resident case makes
   load-bearing because a stale flag would persist for the process lifetime).

## Related

- [failed-compilation-disclosure](failed-compilation-disclosure.md) — the design; this item is the real-data
  evidence and the resident-specific requirements.
- The recurring family this program keeps hitting: a disclosure computed from an artefact of the query plan
  rather than from the answer ([intrinsic hint](intrinsic-hint-counted-before-reachability-filter.md),
  [baked call_edges](baked-call-edges-ignore-rules-edits.md),
  [path disclosures](path-disclosures-computed-off-the-loaded-subgraph.md)). This one is worse in kind: the
  disclosure is not merely mis-scoped, it is absent while a health claim is made in its place.
