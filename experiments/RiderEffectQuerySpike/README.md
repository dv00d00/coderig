# Rider file-effect query spike

> This first spike tests only the reverse-index projection. Its `Line`/`EndLine` fields are not the Rider
> transport contract. The Rider contract research and lifecycle prototype supersede that part of the shape:
> [`docs/spikes/rider-plugin-file-effects-contract.md`](../../docs/spikes/rider-plugin-file-effects-contract.md)
> and [`experiments/RiderFileEffectsContractSpike/index.html`](../RiderFileEffectsContractSpike/index.html).

Throwaway experiment. It answers one question:

> Can an IDE ask once per file which executable declarations reach a selected direct effect, without running
> one graph traversal per declaration?

The prototype implementation is `FileEffectReadModelIndex` in `Rig.Domain`; it is deliberately not wired to
Rider, SQLite, `rig watch`, or a build-time analyzer.

## Shape under test

Build one immutable index for a captured graph generation and one effect selector:

```text
direct effect owners (many seeds)
            |
            v
one receiver-narrowed multi-source reverse traversal
            |
            v
reachable method id -> nearest effect depth
            |
            v
project canonical method facts by FilePath
            |
            v
FilePath -> FileEffectReadModel
```

The IDE-facing request should be file-shaped rather than symbol-shaped:

```text
request  = { filePath, documentStamp, effectSelector }
response = { filePath, documentStamp, graphGeneration, methods[] }
method   = { symbolId, effectAggregates[] }
```

`documentStamp` belongs to the Rider adapter and is echoed so a late response cannot annotate a newer editor
buffer. `graphGeneration` belongs to the resident host and identifies the immutable fact snapshot used for the
answer. Neither belongs in the graph traversal itself. Exact editor ranges are produced later by the ReSharper
backend from current PSI declarations; rig does not transport source coordinates.

## What the spike establishes

- Index construction calls `FactPathFinder.ReachedByAny` once for all selected effect owners. It does not call
  forward reachability for every method in the requested file.
- `ReachedByAny` shares one traversal setup across all seeds: memoized graph-index variants, one reverse-map
  build, then one multi-source BFS. Its depth is the shortest distance to the nearest selected effect owner.
- With `narrowDispatch: true`, the reverse map is built by inverting receiver-narrowed forward call sites, so it
  retains current one-hop dispatch semantics instead of using the receiver-blind all-hops oracle.
- File lookup returns an already materialized read model. Its query cost is dictionary lookup plus payload
  transfer; graph traversal is generation work, not editor-request work.

## Limits deliberately left visible

- The result says that a method reaches at least one selected effect and gives nearest depth. It does not retain
  the witness path or identify every reachable effect. A path can be resolved lazily after a user clicks.
- Precision cannot exceed the existing fact graph: reflection, dynamic invocation, unresolved metadata bodies,
  and disclosed dispatch heuristics remain limits.
- The prototype uses the synchronous-cut traversal lens. Async handoffs and delivery edges would need a distinct
  traversal-mode/index key rather than being silently mixed into the same answer.
- The prototype consumes an already materialized `FactGraphData` and symbol/effect collections. It does not yet
  prove the memory or rebuild cost against the full MedDBase resident generation.
- The prototype eagerly creates a read model for every source file. A measured resident-store trial should decide
  whether that projection remains eager or becomes a lazy per-file cache over the one global reverse closure.
- No SQLite query belongs on Rider's UI thread or in a build-time `DiagnosticAnalyzer`. The intended host is an
  out-of-process resident index; Rider is a thin versioned adapter.

## Next falsification gate

On a real resident MedDBase generation, measure separately:

1. one-time reverse-closure construction for a broad direct-effect selector;
2. file projection memory and time, eager versus lazy;
3. warm lookup latency for a large source file;
4. agreement with exact forward one-hop reachability for a sampled set of positive and negative declarations.

If the reverse closure or its projection cannot be maintained within the resident host's normal generation
budget, discard this shape before building the Rider plugin.
