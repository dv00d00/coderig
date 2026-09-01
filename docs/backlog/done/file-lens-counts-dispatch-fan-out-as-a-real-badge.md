# The file lens counts dispatch fan-out as a real badge, with no disclosure

**Status:** done · **Completed:** 2026-09-01 · **Found:** 2026-09-01 by a probe agent re-auditing the file
lens; reclassified after first-hand verification · **Family:** file lens / dispatch precision · **Severity:**
this is the lens's trust boundary — a badge can rest entirely on an over-approximation rig itself disclaims

## What happens

```
rig annotate "MedDBase.Pathways.Views\PathwayTreeNode.cs" --from 42 --to 51
    @ get_Task  cache:18 echo:6
                     43          public PathwayTask Task =>
                     44              Node.Match(
  cache:17 echo:5    45                  Right: s => s.Task,
                     46                  Left: t => t.Task);

rig reaches "PathwayTreeNode.get_Task" --only cache
  Direct effects (real call paths): 0
  --- dispatch fan-out (2 effects; reach is base-virtual/interface dispatch, NOT a real call — see A1) ---
    x1  redis write  via ICluster.Enqueue dispatch [fan-out of 2]
    x1  redis read   via ICluster.Exists  dispatch [fan-out of 2]
```

`reaches` reports **zero real call paths** to the cache family and puts both effects in the bucket it labels
"NOT a real call". The file lens shows the same reach as an ordinary `cache:18`, indistinguishable from
`PersonCoursesRepository.Save`'s `db!`.

The two neighbouring properties (`get_Task`, `get_Templates`) print identical badges because both
`Node.Match(...)` lambdas route into the same subsystem — that is a consequence, not a separate bug. (An earlier
report read this as the lambda fold cross-attributing effects between sibling methods; that theory is dead.
`Node` is `Either<PathwayTreeFutureTask, PathwayModelTaskState>`, so the lambdas call other types' getters, and
the fan-out route is shared.)

## Mechanism

`FileEffectReadModelIndex.Build` closes each family with:

```csharp
FactPathFinder.ReachedByLabelledSeeds(graph, ownersPerFamily, …, narrowDispatch: true, mode: SyncCut)
```

`Predecessors` (`FactPathFinder.GraphIndex.cs:231-267`) yields `rev.Callers` **and** `rev.ReverseDispatch`,
with no way to tell the two apart in the result: the closure returns one `depth` per node. `narrowDispatch: true`
only chooses receiver-NARROWED dispatch over the receiver-blind superset — it does not exclude dispatch.

Every other user-facing surface separates these. `reaches` has the explicit fan-out bucket; `tree` marks
dispatch edges; the CLAUDE.md two-stage notes call whole-program devirtualization "an over-approximation we
DISCLOSE". The file lens is the one surface that launders it into a plain fact.

## Fix: mark, do not drop

Dropping dispatch-derived reach would hide real effects behind interfaces (most of MedDBase's I/O is behind
one), so the badge must stay — it must just be *labelled*.

1. Add an opt-out for dispatch predecessors to the labelled-seeds walk (a parameter threaded to `Predecessors`,
   defaulting to today's behaviour so no other caller moves), and run each family twice: once with dispatch,
   once real-calls-only.
2. A node whose depth exists ONLY in the with-dispatch closure is dispatch-derived. Where both closures reach
   it, keep the real depth and treat the badge as real (the real path is the one a reader can follow).
3. Carry that as a flag on the aggregate — `FileEffectAggregate` gains a `ViaDispatchOnly` bool (or a small
   enum if a third basis appears) — through `FileEffectLens.LensBadge`, and render it: text `cache:18?`,
   web a distinct glyph/tint, Rider a dimmed adornment. The `?` reads as "may not be a real call", which is
   exactly what it means.
4. Footer/legend line stating it, next to the existing line-precision disclosure.

Cost: one extra reverse closure per family. It is a fraction of the derivation the query already pays, and with
the resident transport in place it is amortised across every file query in a session — measure and report it.


## Delivered 2026-09-01

Badges now carry their BASIS and render it as a trailing `?`.

- `FactPathFinder.Predecessors` / `ReachedByLabelledSeeds` gained `includeDispatch` (default true, so no other
  caller moved); `FileEffectReadModelIndex` runs the family closure TWICE — with dispatch and real-calls-only.
- `FileEffectAggregate.ViaDispatchOnly` is the basis; `Best` picks a REAL row over a dispatch-only one and then
  the shortest distance WITHIN the surviving basis, so a short dispatch guess can neither hide a real path nor
  lend it a number no real path supports.
- Two follow-through fixes the first implementation needed, both caught by rendering the real store:
  1. A site's basis now describes the route from its ENCLOSING method, not the callee's own luck — otherwise
     `PathwayTreeNode.cs:45` printed a real-looking `cache:18` beneath the method's `cache:19?`.
  2. `FileEffectLens.Merge` (the per-line collapse) rebuilt aggregates with the default flag, silently
     promoting a dispatch-only line badge to a fact. It now mirrors `Best`.
- `FileEffectsSchema` 2 -> 3; the flag is additive on the web wire and carried by the resident transport, so
  warm and cold output stay byte-identical.

Measured on the MedDBase store:

| | before | after |
|---|---|---|
| `annotate --summary` cold, `file effects` phase | 47.2s / 14.3GB / 4.9GB peak | 47.3s / 14.3GB / 5.1GB peak |

The second reverse closure is free in practice — the graph load and effect derivation dominate.

Real-store confirmation:

```
PathwayTreeNode.cs      get_Task  cache:19? echo:6      # cache is dispatch-only, echo is real
                site 45           cache:18? echo:5      # line and method now agree on basis
PersonCoursesRepository Save      cache:5? db! echo:9? io:12 rpc:11?
                                  # the db work is real; the cache/echo/rpc claims are dispatch guesses
ImageEdit.Save                    cache:3? db! echo:2? io:12 rpc:9?
```

That last line closes most of
[a method badge no line admits to](../todo/annotate-method-badge-with-no-line-that-admits-it.md): the `echo:1`
that no line admitted to was a dispatch-derived reach, and now says so.

Not done here, deliberately: the web and Rider surfaces receive the flag but still render it as plain text /
no adornment. Giving it a glyph and a tint is a presentation slice.

## Testing expectations

Shipped as `tests/Rig.Tests/Domain/FileEffectDispatchDisclosureTests.cs` (4 tests): dispatch-only is flagged;
a real call path is not; a real path at depth 2 beats a dispatch route at depth 1 (basis over distance); an
effect in the body's own line is never flagged, because every effect owner is seeded into both closures.
- Real-store checks: `PathwayTreeNode.get_Task` becomes `cache:18? echo:6?`; a repository `db!` stays unflagged.
  Then re-run the file set in the audit and report how many badges across it are dispatch-only — that number is
  the honest measure of how much the lens was over-claiming.

## Related

- [A method badge no line admits to](../todo/annotate-method-badge-with-no-line-that-admits-it.md) — same file lens,
  probably the same root cause; verify together.
- [`web-api-seed-and-effect-disclosure-parity`](../todo/web-api-seed-and-effect-disclosure-parity.md) — the same
  disclosure question for the web API's other endpoints.
- [dispatch-fans](./dispatch-precision-substrate.md) is the calibration substrate for how large the
  un-narrowed surface is.
