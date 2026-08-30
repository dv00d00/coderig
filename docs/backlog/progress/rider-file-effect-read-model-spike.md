# Rider file-effect read-model spike

**Status:** PROGRESS — reverse-index core shape, official Rider contract research, and interactive daemon/cache
lifecycle spike completed; an actual backend SDK compile/manual Rider run and real resident-store falsification
remain.

## Problem

A Rider integration should highlight declarations in the current file that can reach a selected direct effect.
Walking forward from every declaration is quadratic in the wrong dimension and makes editor latency depend on
the number of symbols in the file.

## Accepted spike decisions

- Build one multi-source reverse closure from direct effect owners per immutable graph generation and effect
  selector.
- Project the closure into ready read models keyed by file path; editor requests never enumerate or traverse
  symbols.
- Implement the first Rider slice as a ReSharper backend `CSharpDaemonStageBase`, without Kotlin/RD UI code.
- On a daemon cache miss, enqueue one cancellable host request for the whole file and commit no rig highlights.
  Host completion replaces immutable cache data, invalidates the daemon, and never mutates editor ranges.
- Return method DocIDs plus semantic effect aggregates. The next daemon pass joins those DocIDs to current PSI
  declarations and owns every exact `DocumentRange`; rig does not transport lines, columns, or offsets.
- Keep client snapshot freshness in the plugin, indexed-source and graph-generation freshness in the resident
  host, and compilation context in both. An empty result is authoritative only for an exact source snapshot.
- Treat `projectPath + compilationMoniker + filePath` as the portable external identity. A physical path alone
  is ambiguous for linked files, multi-target projects, and conditional compilation.
- Resolve a witness path lazily on interaction.

The executable and protocol-shaped notes live in
[`experiments/RiderEffectQuerySpike/README.md`](../../../experiments/RiderEffectQuerySpike/README.md). The actual
Rider extension-point research is
[`docs/spikes/rider-plugin-file-effects-contract.md`](../../spikes/rider-plugin-file-effects-contract.md), and the
interactive daemon/cache contract is
[`experiments/RiderFileEffectsContractSpike/index.html`](../../../experiments/RiderFileEffectsContractSpike/index.html).

## Testing expectations

- Domain tracer test pins multi-source reverse projection, negative filtering, nearest depth, canonical method
  selection, and ready-model reuse.
- Interactive scenarios pin the intended lifecycle: cache miss, one async request, late-response rejection,
  daemon invalidation, PSI-owned ranges, focus races, and compilation-context races.
- The backend SDK spike must use `CSharpHighlightingTestBase` (or the current equivalent) to prove two returned
  DocIDs become two PSI-derived highlights after one file request and daemon invalidation.
- Before product work, measure reverse-build cost, eager/lazy projection cost, warm file lookup, and sampled
  forward/reverse semantic agreement on the MedDBase resident generation.

## Out of scope

- Rider/Kotlin frontend UI and custom RD protocol.
- SQLite access from a build-time Roslyn analyzer.
- Synchronous host I/O from a daemon pass or while holding a PSI read lock.
- Full witness paths in every file response.
- Product compatibility or migration promises for the prototype types.

## Exit

Move this card to `done/` as either validated or discarded. Do not turn the prototype into a supported host
interface until the real-store falsification gate passes.
