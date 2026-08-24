# `callers`/`reaches` on a PROPERTY answers "No symbol matches" — a false negative

**Status:** open bug. Found 2026-08-24 by an agent using rig on rig's own source during unrelated work,
which is the point worth keeping: this is the failure mode rig exists to prevent, hit by rig.

## The bug

A property pattern never resolves to its accessor, so the query reports the member as non-existent:

```
$ rig callers "FactSnapshot.ProjectedCallGraphBuild"
No symbol matches 'FactSnapshot.ProjectedCallGraphBuild'.
```

The symbol exists and has 12 transitive callers. `rig symbols` sees both rows:

```
method    M:Rig.Analysis.Inventory.FactSnapshot.get_ProjectedCallGraphBuild   FactSnapshot.cs:224
property  P:Rig.Analysis.Inventory.FactSnapshot.ProjectedCallGraphBuild       FactSnapshot.cs:222

$ rig callers "FactSnapshot.get_ProjectedCallGraphBuild"
Methods that reach 'FactSnapshot.get_ProjectedCallGraphBuild': 12
```

**Why it matters more than a missing feature.** "No symbol matches" does not read as *rig cannot express
this question* — it reads as *this member has no callers, or does not exist*. A reader acts on that. rig's
whole design premise is that an over-approximation must be DISCLOSED (the `~heuristic` tag, the dispatch
fan-out bucket, the `--entrypoints` frontier note); here the answer is silently WRONG in the recall
direction, with no disclosure at all.

## The related case: a property whose body holds a lambda

When the property body contains a lambda, the pattern DOES match — the lambda — and the result is
misleading in a second way:

```
$ rig callers "LiveFactSource.BuildTimes" --format tsv
0  P:Rig.Cli.Live.LiveFactSource.BuildTimes~λ0          true
1  M:Rig.Cli.Live.LiveFactSource.get_BuildTimes         true
2  M:Rig.Cli.Commands.WatchHost.AnswerQueryAsync(...)   true
```

The depth-0 "matched node" is the lambda, so the property's own accessor is reported as a d1 **caller of
itself**, and the headline count includes it. Someone asking "who calls `BuildTimes`" wants the 2 real
callers, not 12.

So the two shapes fail in opposite directions — one under-reports to zero, the other over-reports and
mis-parents — which makes property queries untrustworthy either way.

## Why this is the shape it is

`P:`/`F:`/`E:` symbols are never call-graph NODES; only their bodied accessors are (see the effect /
reachability model in CLAUDE.md). `FactExtractor.EnclosingSymbolId` already keys accessor-body effects to
`M:get_X`/`M:set_X` for exactly this reason. The pattern matcher used by `callers`/`reaches` never got the
companion rule, so it matches against a node universe the property is not in.

## Fix

Resolve a `P:Type.Name` pattern match to its bodied accessors (`M:get_Name` / `M:set_Name`), consistent with
what extraction already does. A property with both accessors should span both, the way an ambiguous pattern
already spans its matches.

**Minimum bar, if the full fix is deferred:** never emit `No symbol matches` when `rig symbols` WOULD match
a `P:`/`F:`/`E:` symbol. Say what was found and what to ask instead — e.g. "matched a property; query its
accessor `get_X`". A wrong answer with no disclosure is the one outcome the tool should not produce.

## Also worth checking when fixing

- `reaches` and `path`, which share the matcher — assume the same false negative until shown otherwise.
- Fields (`F:`) and events (`E:`): same node-universe reasoning, so probably the same bug. Not verified.
- Whether the `~λN` depth-0 match should be collapsed to its declaring member for display, which would fix
  the self-caller artefact independently of the accessor resolution.
