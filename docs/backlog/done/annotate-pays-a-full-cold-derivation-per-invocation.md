# `rig annotate` pays a full cold derivation per invocation — route it to a resident host

**Status:** done · **Completed:** 2026-09-01 · **Found:** 2026-09-01, probe agent measured 30 files ·
**Family:** performance / CLI transport · **Decision:** route to a resident host, "what the web does"

## Measured problem

Every `rig annotate` call costs **34.7–51.3 s wall, ~14.3 GB read, ~5 GB peak RAM**, flat: a 0-method
interface file costs the same as a 42-method page with 131 marked lines. The `file effects` phase is ~98% of
wall time. A 30-file audit sweep therefore spent ~20 minutes almost entirely re-deriving identical facts.
(Absolute numbers are from a run with two probe agents contending on the same store, so treat them as an upper
bound; the flat floor was present on the first uncontended calls.)

Why, from `Rig.Cli/Services/FileEffectsQueryService.cs`:

```
:86  WarmStore.GraphAsync         → whole-solution graph load
:87  WarmStore.InvocationsAsync   → every invocation fact
:88  EP data + throw refs + allocation facts
:96  QueryEffectDerivation.ForReach(rules, inputs, graph)   ← full effect derivation, whole store
:98  FileEffectReadModelIndex.Build(...)  → per-family reverse closure, then ONE file's projection
```

Everything above the last line is store-wide and identical for every file. There are three cache layers in
rig and `annotate` benefits from the weakest one:

| layer | lifetime | used by `annotate` |
|---|---|---|
| `.rig/cache.db` disk artifacts (`StoreQueryArtifactCache`, `QueryCacheKeys`) | across processes | **no** — no key, no `*Schema` |
| `WarmStore` (`Caching/WarmStore.cs:37`) — in-process list + 32-entry file-effects LRU | one process | yes, and it dies with the process |
| web client IndexedDB | browser | web only |

`rig serve` and the Rider `rig watch` host are fast for exactly one reason: they are long-lived, so they pay
this once. A one-shot CLI process has no resident state to inherit.

## Chosen approach: ask a resident host, exactly as the browser does

The browser is fast because it queries `/api/file-effects` on a warm `rig serve`. Give the CLI the same
transport, with a disclosed fallback chain:

1. `--host <url>` when passed explicitly.
2. A **discovery marker** written by `rig serve`: `.rig/serve.json` = `{ port, pid, storeRef, started }`,
   removed on shutdown. `annotate` reads it, ignores (and deletes) a marker whose pid is dead, and confirms
   store identity via `/api/meta` before trusting the answer.
3. The live pipe (`RiderFileEffectTransport`, verb `file-effects`) when a `rig watch` host serves THIS working
   directory. Note the cwd-keying trap in
   [a running `rig watch` is undiscoverable](../todo/live-host-endpoint-is-undiscoverable-from-the-checkout.md):
   a host launched from the solution directory will not be found from the analysis directory, which is where
   `annotate` is run. Fix that card first or expect this arm to miss in the common setup.
4. Cold in-process path (today's behaviour).

The mapping back is already free: `FileEffectsResponseDto` is 1:1 with `FileEffectReadModel`, and
`FileEffectsEndpoint.ToResponse` now projects through `FileEffectLens` — so the HTTP arm rebuilds the read model
from the DTOs, calls `FileEffectLens.Project`, and renders IDENTICALLY to the cold arm. No wire change.

Source text stays LOCAL: `SourceRenderer` reads the working tree or the git blob directly, so only facts cross
the transport. File-path resolution also stays local (a `SourceFiles` substring query, ~0.7 s).

## Requirements

- The header discloses the transport and its provenance: `via rig serve http://localhost:5057` /
  `via live host (working tree)` / `cold (start \`rig serve\` for warm calls)`. Never silently mix arms
  across a single render.
- `--time` gains a `transport` phase.
- `--cold` forces the in-process arm (needed to A/B the arms and to debug the host).
- Any host failure — unreachable, non-200, store mismatch, malformed payload — falls back to cold with ONE
  stderr note, never a crash and never a partial render.
- `--store <ref>` is forwarded to the host so both arms answer from the same store; a host that cannot serve
  that store is a miss, not a silent substitution.
- `rig annotate` must NOT auto-start a host. A background process the user did not ask for is a surprise; the
  cold-path hint is the nudge instead.

## Acceptance

- With `rig serve` running in the same directory: first `annotate` call and the 30th are both < 3 s
  (post-warm), and their output is byte-identical to `--cold` output for the same file and store.
- With no host: unchanged behaviour, one hint line, same exit codes.
- Stale marker (host killed) → falls back to cold, marker removed, no error beyond the note.
- A 30-file sweep A/B: report cold total vs warm total on the MedDBase store.


## Delivered 2026-09-01 — measured against the acceptance criteria

`rig serve` writes `.rig/serve.json` (`port`, `url`, `pid`, `workingDirectory`, `startedUtc`) and `rig annotate`
discovers it; `--host <url>` and `--cold` both landed (`AnnotateResidentTransport`).

| criterion | measured |
|---|---|
| warm latency | **0.7s** total, 0.1s transport (cold baseline 47.2s) — 67x |
| a DIFFERENT file on the same host | also 0.7s, so the shared graph is reused rather than a per-file cache |
| warm output == `--cold` output | identical, 75 tsv rows, `LocationsHandler.cs` |
| transport disclosed | `transport: rig serve http://localhost:5061` in the header |
| stale marker (host killed) | one note, `transport: cold (start \`rig serve\` for warm calls)`, marker deleted, correct answer |
| no auto-start | respected — hint only |
| first call after host boot | 37-50s, paid INSIDE the host (CLI peak RAM 84MB) |

A 15-file re-audit then ran at a **929ms median** across ~35 probes, which is what made per-badge verification
affordable at all — the transport's real payoff is auditability, not just speed.

Remaining, deliberately not chased here: the warm call's cost is now dominated by the LOCAL file-path lookup
(0.5-0.6s of the 0.7s, a `SourceFiles` substring query). Batch input and disk-caching the closures stay open as
the complementary items below.

## Delivered 2026-09-01 — measured against the acceptance criteria

`rig serve` writes `.rig/serve.json` (`port`, `url`, `pid`, `workingDirectory`, `startedUtc`) and `rig annotate`
discovers it; `--host <url>` and `--cold` both landed (`AnnotateResidentTransport`).

| criterion | measured |
|---|---|
| warm latency | **0.7s** total, 0.1s transport (cold baseline 47.2s) — 67x |
| a DIFFERENT file on the same host | also 0.7s, so the shared graph is reused rather than a per-file cache |
| warm output == `--cold` output | identical, 75 tsv rows, `LocationsHandler.cs` |
| transport disclosed | `transport: rig serve http://localhost:5061` in the header |
| stale marker (host killed) | one note, `transport: cold (start \`rig serve\` for warm calls)`, marker deleted, correct answer |
| no auto-start | respected — hint only |
| first call after host boot | 37-50s, paid INSIDE the host (CLI peak RAM 84MB) |

A 15-file re-audit then ran at a **929ms median** across ~35 probes, which is what made per-badge verification
affordable at all — the transport's real payoff is auditability, not just speed.

Remaining, deliberately not chased here: the warm call's cost is now dominated by the LOCAL file-path lookup
(0.5-0.6s of the 0.7s, a `SourceFiles` substring query). Batch input and disk-caching the closures stay open as
the complementary items below.
## Complementary, not chosen instead

- **Batch input** (`rig annotate <file...>`) amortises the derivation inside ONE cold process — an
  independent, much smaller change that helps a sweep even with no host running. Worth doing; not this card.
- **Disk-caching the shared closures** in `.rig/cache.db` (keyed store + rules + `FileEffectsSchema`) would fix
  the cold floor itself rather than routing around it. Needs
  [the missing schema constant](./file-effects-artifact-has-no-cache-schema-constant.md) first.
- [Warm graph across queries](./derivation-cache-5-warm-graph-across-queries.md) is the same underlying problem for
  `callers`/`reaches`; this card deliberately reuses the resident host it already concluded with instead of
  introducing a daemon.

## Implementation note — 2026-09-01

The first production slice now routes `annotate` through an explicitly selected or `.rig/serve.json`-
discovered `rig serve`, with `--cold`, store/checkout identity checks, lossless declaration transport and a
fail-closed cold fallback. The resident host builds one solution-wide `FileEffectReadModelIndex` per
store/rules identity during prewarm; changing files is a dictionary lookup rather than another whole-store
effect derivation/reverse closure. The live-pipe arm remains a follow-on.

Self-store A/B across three **different** files (alternating Release runs, each cold/resident pair had
identical stdout): **2.44→1.40 s**, **2.49→1.34 s**, **2.31→1.26 s** end-to-end. The solution index took
**743 ms** to build once; direct HTTP for the three files was **173 ms** for the first Kestrel/JIT request,
then **9 ms** and **5 ms**. RSS was ~**308 MiB** after prewarm and ~**316 MiB** after all three files, versus
the previous per-file cache growing from ~202 MiB to ~334 MiB over the same sweep. The remaining local floor
is file lookup/process startup. The MedDBase 30-file sweep remains the real-scale acceptance check.
