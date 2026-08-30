# Rider file-effect read-model spike

**Status:** PROGRESS — throwaway core shape implemented and focused tracer test green; real resident-store
falsification remains.

## Problem

A Rider integration should highlight declarations in the current file that can reach a selected direct effect.
Walking forward from every declaration is quadratic in the wrong dimension and makes editor latency depend on
the number of symbols in the file.

## Accepted spike decisions

- Build one multi-source reverse closure from direct effect owners per immutable graph generation and effect
  selector.
- Project the closure into ready read models keyed by file path; editor requests never enumerate or traverse
  symbols.
- Keep document freshness in the Rider adapter (`documentStamp`) and graph freshness in the resident host
  (`graphGeneration`). A response is applicable only while both remain current.
- Return positive method spans and nearest effect depth. Resolve a witness path lazily on interaction.

The executable and protocol-shaped notes live in
[`experiments/RiderEffectQuerySpike/README.md`](../../../experiments/RiderEffectQuerySpike/README.md).

## Testing expectations

- Domain tracer test pins multi-source reverse projection, negative filtering, nearest depth, canonical method
  selection, and ready-model reuse.
- Before product work, measure reverse-build cost, eager/lazy projection cost, warm file lookup, and sampled
  forward/reverse semantic agreement on the MedDBase resident generation.

## Out of scope

- Rider/Kotlin UI and packaging.
- SQLite access from a build-time Roslyn analyzer.
- Full witness paths in every file response.
- Product compatibility or migration promises for the prototype types.

## Exit

Move this card to `done/` as either validated or discarded. Do not turn the prototype into a supported host
interface until the real-store falsification gate passes.
