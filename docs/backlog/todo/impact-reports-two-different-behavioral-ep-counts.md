# `rig impact` reports two different behavioral-EP counts from one run

**Status:** todo · **Found:** 2026-09-02 by inspection · **Family:** impact / count definitions
**Triage:** needs-info (which definition is the product's "behavioral" is a decision, not a bug fix)

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
  on [the CLI/web collapse map](./cli-web-collapse-map.md).
