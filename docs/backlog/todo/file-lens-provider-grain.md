# File lens: provider-grain effects without rebuilding the graph per provider

**Status:** todo · **Family:** file lens / effect vocabulary
**Triage:** ready-for-agent

## Problem

The shared CLI/web/Rider read model is family-grain. `--only llblgen` must widen to all `db` providers, and the
UI cannot distinguish provider-specific effects inside a family. The merged MedDBase rules contain 66 effect
providers, while `ReachedByLabelledSeeds` currently supports at most 64 labels in one bitmask pass.

## Accepted design

- Chunk inside `ReachedByLabelledSeeds`: build graph indexes and reverse maps once, then walk deterministic
  chunks of at most 64 labels.
- Add `Provider` to the aggregate; retain `Family` as grouping metadata so the default eight-family view stays
  compact and clients can expand it.
- Preserve nearest depth, looped, and dispatch-only basis independently per provider.
- Provider:operation grain is explicitly not part of this slice; its hundreds of labels need a different
  payload and interaction decision.

## Acceptance

- A synthetic 66-provider fixture proves deterministic chunking and no dropped or cross-labelled reach.
- Family-collapsed output remains equivalent to the current model.
- Capture MedDBase cold time, peak memory, method aggregate count, and payload size before/after; do not ship an
  unbounded memory regression merely because traversal time stays flat.
- Bump `FileEffectsSchema` and verify CLI/web/resident outputs against real `rig annotate` output.

## Out of scope

- Provider-specific glyph artwork.
- Operation/resource filters.
- Changing repository rule vocabulary such as `bus` versus `echo`.
