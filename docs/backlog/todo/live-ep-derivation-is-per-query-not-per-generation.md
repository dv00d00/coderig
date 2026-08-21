# Live EP derivation runs a full graph projection on EVERY query — per-query memo, and invisible to the instrument

**Status:** todo · **Priority: MEDIUM-HIGH** (a multi-second per-query cost on the main real-world target,
whenever `deployments.json` is configured — which it is on `meddbase-analysis`; and it is absent from the one
instrument that would show it) · **Found:** 2026-08-21, reviewing the `tree` migration ·
**Family:** live index / performance

## The bug

`LiveQueryFactSource` memoizes its entry-point derivation in two instance fields, `_entryPoints` and
`_epSiteKind`, whose comments say "memoized for this fact generation". They are not: **`LiveQueryRunner`
constructs a fresh `LiveQueryFactSource` per query**, so both memos live and die with a single query.

What that memo was hiding is not small:

```csharp
// LiveQueryFactSource.EntryPointSets
var derived = FactEntryPointDeriver.Derive(epData ?? Source.EpData, rules.EntryPoints, rules.ClassInheritance);
var edges = FactGraphProjection.FromAnalysis(Source.Facts, handoffRules: rules.Handoff, redirectRules: rules.Redirect).CallEdges;
var classifiedHandoffs = HandoffClassifier.HandoffEntryPoints(edges, rules.Handoff)…
```

`FactGraphProjection.FromAnalysis` builds the whole call graph from every reference fact — on MedDBase, ~2.4M
refs. The measured cost of the equivalent artifact (`traversalGraph`) is **2.3-3.3s**. So a live query pays
roughly that, again, every time.

It is reached whenever deployments are configured: `BuildEpContextAsync` short-circuits only on
`deployments.IsEmpty`, and `deployments.json` exists in `c:/git/meddbase-analysis`. The EP chip in a real live
answer (`⟦3 svcs: MedDBase (iis), MedDBase.DataServer (iis), MedDBase.PACS (iis)⟧`) is the proof it ran.

## Why it went unnoticed — the same trap twice

It is **not in `BuildTimes`**, so the `live: derived layer built this generation: …` line does not mention it and
the background warmer cannot warm it. This is exactly how the unmemoized `eventSubscriptionSites` hid until it
was found by reading the code rather than the instrument (fixed 2026-08-21). **An artifact that is not on
`LiveFactSource` is invisible to every measurement this program has built** — which makes "is it on
`LiveFactSource`?" the real invariant, not "is it memoized?".

## Fix

Move both memos onto `LiveFactSource`, next to `ArtifactMemo`, built through the timed `Memo` helper so they
appear in `BuildTimes` and become warmable.

**The key needs a decision, which is why this is not a two-line change.** `TreeCommand` reloads the `RuleSet` per
query, so a *different instance with identical content* arrives each time — keying on `ReferenceEquals(rules)`
would produce zero hits and silently preserve the bug. Key on the rules FINGERPRINT
(`RulesFingerprint.ComputeFromPaths`, already computed for the tree memo key) rather than on identity. Note
`DeriveEntryPointsAsync` currently receives only `rules`, with no working directory or extra-rules list, so the
fingerprint has to be threaded or hoisted.

Fixing this also narrows the pre-existing rules-staleness gap documented on `SameShapingAsMemo`, which compares
three rule-slice *sizes* rather than content.

## Acceptance

1. Two successive live queries in one generation derive the EP set ONCE; the second reports no build cost.
2. The cost appears in `BuildTimes` (so it is visible and warmable) — and the warm/don't-warm call is made on
   the measured number, as it was for `hazardEffects` (3.4s, left lazy).
3. A rules change within a session produces a fresh derivation rather than a stale memo.

## Related

- Same class as the unmemoized `eventSubscriptionSites` (fixed, `88f8a40a`) — including the reason it hid.
- `hazardEffects` is the precedent for the warm/don't-warm decision: measure first, warm only if the marginal
  cost does not blow the warm window.
