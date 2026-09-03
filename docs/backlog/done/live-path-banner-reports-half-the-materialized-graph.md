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

## Resolved 2026-09-03 — the oracle was wrong, the code was never broken

**Root cause: the assertion compared two different FACT MODELS.**

```
oracle 87 = new LiveFactSource(await host.GetCurrentFactsAsync(), rules).TraversalGraph.CallEdges.Count
banner 44 = the banner from `path`, which reports LiveQueryFactSource.MaterializedGraph(shapedRules)
```

`WatchHost.GetCurrentFactsAsync` returns `_index.CaptureSnapshot().FlattenedFacts`
(`src/Rig.Cli/Commands/WatchCommand.cs:947`) — the FLATTENED compatibility facts. So the oracle built the
legacy whole-graph projection: the very arm that `Flattened_fact_compatibility_is_explicitly_diagnosed_as_legacy_fallback`
in the same file pins as `LegacyWholeGraphFallback`, and that `LiveQueryFactSource.cs:99` documents WatchHost
never takes. The banner meanwhile reports the KEYED resident snapshot's materialized graph.

There is no superset relation between the two, so `materialized > flattened` was never a valid claim. The
keyed projection is receiver-narrowed where the flattened model fans out via CHA, so the precise model
carrying FEWER edges is the expected direction, not a regression.

**Vintage: wrong since it was written — never a regression, which is why the numbers were bit-identical on
every run (44 and 87, four runs, two builds).** `97c1d3ae perf: materialize the live call graph once per
generation` rewrote this assertion when materialize-once landed, on the assumption that both sides shared a
fact model. No bisect was needed to establish that; the two sources answer it outright. This card's
"establish the vintage first" acceptance criterion is met by reading, not by `git bisect`.

**Replaced with the invariant the 2026-08-24 comment actually described**, which needs no cross-model oracle:

```csharp
var banners = new[] { dispatch, payment, syncEvent, asyncEvent }.Select(r => FactGraphCallEdgeCount(r.Out)).ToArray();
banners.ShouldAllBe(count => count > 0);
banners.Distinct().Count().ShouldBe(1, $"materialize-once means one graph per generation; got {…}");
```

Strictly stronger than what it replaced. Three different seed pairs and both traversal modes must now report
ONE number: a regression to per-query projection (the pre-2026-08-24 behaviour) makes them diverge
immediately, because each seed expands a different closure. `--async` agreeing is the second half of the
claim — delivery edges are folded in unconditionally rather than costing a second materialization. It passes,
so 44 held across all four; materialize-once is real and is now actually tested, where before it was asserted
against the wrong graph.

`DemandLivePathTests` 3/3 and the whole LIVE lane **33/33** (run twice), so
`scripts/mini-ci.ps1 -FullTests` reaches pack/install again.
