# `rig impact` reports two different behavioral-EP counts from one run

**Status:** todo · **Found:** 2026-09-02 by inspection · **Family:** impact / count definitions
**Triage:** ready-for-agent
**Decision:** D4, 2026-09-03 — **`PerEp` keeps every EP with any hazard, amplification or guard delta, and
`BehavioralEpCount` is reported separately.** One definition, three surfaces (CLI, CI gates, web). Hazard-only
EPs stop being silently dropped, which restores the engine's stated intent. Accepted cost: the
`impact_summary behavioral_eps` value **changes under `--intrinsic`** for anything parsing `--format tsv`.
Implemented as part of [cli-web-collapse-1](./cli-web-collapse-1-impact-selection-into-the-engine.md), which
owns the `Select` this definition lives in.

## What happens

One command prints two numbers for the same quantity, from two different definitions:

| output | anchor | what it counts |
| --- | --- | --- |
| `impact_summary behavioral_eps=…` | `src/Rig.Cli/Commands/ImpactCommand.cs:973` | `diff.PerEp.Count` — every per-EP row |
| the human header | `src/Rig.Cli/Commands/ImpactCommand.cs:774` | `ImpactEngine.EffectChangedEpCount(diff)` — only EPs with `Added \| Removed \| Amplified > 0` |

`EffectChangedEpCount` excludes hazard-only EPs, by its own comment (`ImpactEngine.cs:525-531`).

## Why they usually agree, and why that is the problem

Under the default filter the two numbers agree **by accident**: `FilterPerEpEffects` drops hazard-only EPs
(`ImpactEngine.cs:553`, `:566-569`), so `PerEp` happens to contain nothing that `EffectChangedEpCount` would
have excluded.

That drop also contradicts the engine's stated intent, which is that hazard-only EPs DO surface in the per-EP
section.

With `--intrinsic` the early return at `ImpactEngine.cs:553` keeps hazard-only EPs, `PerEp` grows, and the two
CLI numbers diverge — the machine-readable summary and the human header disagree in the same output.

## The decision this needs

"Behavioral" has to mean one thing. Two coherent options:

| option | meaning | consequence |
| --- | --- | --- |
| O1 | behavioral = effect delta only | hazard-only EPs are excluded everywhere, including from `PerEp`; the engine comment about surfacing them is retracted |
| O2 | behavioral = effect delta OR hazard delta | `EffectChangedEpCount` widens to include hazard-only EPs; the header rises; `FilterPerEpEffects` stops dropping them under the default filter |

O2 matches the engine's written intent and keeps `--intrinsic` from changing the definition. Recorded as a
RECOMMENDATION, not a decision.

Either way, one selection helper computes the count and both the header and the summary read it.

## Testing expectations

- A diff fixture with one hazard-only EP: the summary count and the header count are equal, with and without
  `--intrinsic`.
- The same fixture asserted against the chosen definition, so the option is pinned by a test rather than by a
  comment.
- `--expect-no-effect-change` agrees with whichever count is printed — the gate and the report cannot disagree
  about whether the run is clean.

## Related

- [CLI/web `impact` behavioral count differs by one](./cli-web-parity-2-impact-behavioral-count.md) — the web
  side of the same vocabulary problem. That card settles which surface is right about the number; this one is
  about the CLI disagreeing with itself.
- [Impact selection moves into the engine as one view](./cli-web-collapse-1-impact-selection-into-the-engine.md)
  — the slice that has to consume whichever definition wins here, across three surfaces. Its recommendation,
  recorded as a recommendation: `Select` keeps EPs with any hazard, amplification or guard delta in `PerEp` and
  reports `BehavioralEpCount` separately, which is O2 above. The decision stays on this card. Family rationale
  on [the CLI/web collapse map](../todo/cli-web-collapse-map.md).

## Resolved 2026-09-03 — O2, implemented and verified

Shipped as part of [cli-web-collapse-1](../done/cli-web-collapse-1-impact-selection-into-the-engine.md).
`ImpactEngine.Select` returns one `ImpactView`; `ImpactView.BehavioralEpCount` (effect-delta only, over the
FILTERED set) is what the human header (`ImpactCommand.cs:774`) AND `impact_summary behavioral_eps`
(`:973`, previously `diff.PerEp.Count`) now both read. `PerEp` retains every EP with any effect, hazard or
amplification-tier delta, so hazard-only EPs stop being silently dropped and the engine's stated intent
(`ImpactEngine.cs:523-529`) holds. That is **O2** from the table above.

A guard delta needs no retention arm of its own — it IS a `lock`/`async_lock` effect entry, so the effect arm
covers it whenever the reader has not explicitly filtered locks away. Recorded as D5 on the shipped card.

**The accepted cost did not materialise on real data.** This card and the collapse card both warned that
`impact_summary behavioral_eps` would CHANGE under `--intrinsic` for anything parsing `--format tsv`. On
`a1d65d423431` → `a0b279cf7e85` it does not: 38 before, 38 after, under both filters, because the unfiltered
`PerEp` on that pair is also 38 (every EP has an effect delta). The divergence is real in principle and is
pinned by fixtures in `tests/Rig.Tests/Cli/ImpactSelectViewTests.cs`, not by the store. Anyone parsing that
column should still treat the change as breaking — the number moves the first time a diff contains a
hazard-only EP.

Testing expectations from this card, all met: a hazard-only fixture where the summary and header agree with
and without `--intrinsic`; the chosen definition pinned by test rather than comment; `--expect-no-effect-change`
reads the same count the report prints (both now come from the view).
