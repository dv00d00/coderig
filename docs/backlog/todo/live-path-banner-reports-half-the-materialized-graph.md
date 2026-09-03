# The live `path` banner reports 44 call edges where the whole-graph oracle has 87

**Status:** todo · **Found:** 2026-09-03, incidentally, while shipping an unrelated change ·
**Triage:** needs-info
**Family:** live query / materialize-once

## What fails

`Rig.Tests.Live.DemandLivePathTests.Generic_interface_and_async_delivery_live_paths_match_store`
(`tests/Rig.Tests/Live/DemandLivePathTests.cs:63`, LIVE lane) asserts the live `path` banner's call-edge
count exceeds the generation's whole-graph oracle:

```
materializedCallEdges.ShouldBeGreaterThan(fullGraphOracle.CallEdges.Count);
//  44                should be greater than  87
```

The banner is parsed out of `path`'s own output (`FactGraphCallEdgeCount`, `:165-171`); the oracle is
`new LiveFactSource(await host.GetCurrentFactsAsync(), rules).TraversalGraph.CallEdges.Count`.

## Not flaky, and not caused by the change it was found under

- **Deterministic.** 44 and 87 on every run — four runs, two builds. A flake would vary.
- **Fails at `7eb8fa8f` with the working tree stashed**, so it is not the evidence-tier change it surfaced
  under (that touched the findings codec, the anchor DTO and the web client; nothing live, nothing graph).
- The other two tests in the class pass, so the host starts and answers.

**Vintage unknown, and worth pinning down before diagnosing.** The assertion was last rewritten by
`97c1d3ae perf: materialize the live call graph once per generation` — the commit that changed its meaning
from "the banner reports a PARTIAL slice" to "the banner reports at least the whole traversal graph". Its
comment (`:52-59`) is explicit that the new claim depends on the ruleset configuring delivery, and that this
playground raises a real C# event so it does. Either that premise stopped holding, or the materialization
regressed to a per-query slice. `git bisect` over the LIVE lane answers which; nothing here does.

## Why it was not caught earlier today

The `f48e82e7` verification run reported the MAIN lane (1387 tests) and installed the tool — consistent with
a run WITHOUT `-FullTests`, which never reaches the LIVE lane. So "green" that morning did not cover this
test, and this card is not evidence of same-day drift.

## Acceptance

- The vintage is established (the commit at which the assertion first fails), before any fix.
- Either the banner reports the materialized graph the comment at `:52-59` describes, or that comment and the
  assertion are corrected to the behaviour that is actually intended — not left disagreeing.
- The LIVE lane is green, so `scripts/mini-ci.ps1 -FullTests` reaches pack/install again.
