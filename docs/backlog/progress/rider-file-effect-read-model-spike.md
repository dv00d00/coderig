# Rider file-effect read-model spike

**Status:** PROGRESS — the generation-owned semantic file read model, official Rider contract research,
interactive daemon/cache lifecycle spike, exact-SDK compile, isolated manual Rider run, and CodeRig self-scale
falsification are completed; structured host transport remains.

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

## Implemented core slice — 2026-08-30

- `FileEffectSelector` names one semantic family and its exact provider/provider-operation predicates.
  All matching direct owners seed one `ReachedByAny` call.
- The first method row is deliberately only `symbolId + family + nearestDepth`. It answers task 1 — whether
  the declaration has a path to the selected effect family — without retaining every reachable effect or
  running a forward traversal per declaration. Operation/resource summaries remain a later, measured
  extension rather than a claim this slice cannot cheaply support.
- `LiveFactSource` owns a small two-selector memo for each immutable fact generation. Equivalent reordered
  predicate sets reuse one index; a new fact generation starts empty. The Rider artifact is not part of
  `WarmQueryArtifacts`: it is needed only when a Rider client connects, and self-calibration puts its lazy
  generation cost at about 100 ms.
- The source-file catalog distinguishes an indexed file with no methods (authoritative empty model) from an
  unknown file (`null`). Rig still transports no source coordinates.
- Focused domain/live tests pass, and the ordinary MSBuild-free suite passes 1,178/1,178 on macOS.

## CodeRig self-calibration — 2026-08-31

The reproducible manual harness is `RiderFileEffectReadModelSelfTrial`. It analyzes
`RuntimeIntelligenceGraph.slnx` without test projects, warms the normal resident query artifacts, and then
measures only the Rider reverse projection. Two consecutive runs (cold and warm design-time-build cache)
produced:

| Corpus / metric | Cold DTB cache | Warm DTB cache |
|---|---:|---:|
| Indexed files / symbols / references | 353 / 6,201 / 39,203 | same |
| Normal resident query-artifact warm | 190 ms | 190 ms |
| SQL effects / direct owners | 98 / 61 | same |
| File read-model build | 101.51 ms | 99.31 ms |
| Managed-memory delta | 5.91 MiB | 5.92 MiB |
| Positive files / methods | 59 / 222 | same |
| Warm file lookup (176,500 lookups) | 0.138 us average | 0.139 us average |
| Forward/reverse semantic sample | 24 positive + 24 negative, 0 mismatches | same |

The whole Roslyn/MSBuild setup was 38.06 s cold and 29.65 s warm, but that is resident-host startup/indexing,
not file-query latency. The Rider model itself satisfies the spike shape on the repository we can reproduce
and evolve with the code. Re-run this harness after traversal semantics or the model payload changes.

The executable and protocol-shaped notes live in
[`experiments/RiderEffectQuerySpike/README.md`](../../../experiments/RiderEffectQuerySpike/README.md). The actual
Rider extension-point research is
[`docs/spikes/rider-plugin-file-effects-contract.md`](../../spikes/rider-plugin-file-effects-contract.md), and the
interactive daemon/cache contract is
[`experiments/RiderFileEffectsContractSpike/index.html`](../../../experiments/RiderFileEffectsContractSpike/index.html).
The exact-SDK backend plugin and runtime transcript are in
[`experiments/RiderBackendEffectSpike/README.md`](../../../experiments/RiderBackendEffectSpike/README.md).

## Testing expectations

- Domain tracer test pins multi-source reverse projection, negative filtering, nearest depth, canonical method
  selection, and ready-model reuse.
- Interactive scenarios pin the intended lifecycle: cache miss, one async request, late-response rejection,
  daemon invalidation, PSI-owned ranges, focus races, and compilation-context races.
- The throwaway backend SDK spike manually proves that two returned DocIDs become two PSI-derived highlights
  after one file request and daemon invalidation. A product slice must pin that with
  `CSharpHighlightingTestBase` (or the current equivalent).
- Filter `IPsiSourceFile.Properties.IsGeneratedFile` and `IsNonUserFile` before host lookup; a real Rider run
  showed daemon passes for generated files under `obj/`.
- Keep the CodeRig self-trial anti-vacuous: it must continue to find selected effects, positive and negative
  methods, and zero sampled forward/reverse disagreements.

## Out of scope

- Rider/Kotlin frontend UI and custom RD protocol.
- SQLite access from a build-time Roslyn analyzer.
- Synchronous host I/O from a daemon pass or while holding a PSI read lock.
- Full witness paths in every file response.
- Product compatibility or migration promises for the prototype types.

## Exit

Move this card to `done/` as either validated or discarded after the structured host transport is proved.
The read-model scale/semantic gate is now the reproducible CodeRig self-trial above.
