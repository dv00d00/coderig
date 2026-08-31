# Decompile first-party binary-referenced assemblies (deferred)

**Status:** todo, DEFERRED by decision on 2026-08-31 · **Family:** extraction / graph model

This is the L3 arm of `external-library-code-no-graph-representation.md`, split out so the parent card can
close on L2. The general form — index the internals of every referenced assembly — is **rejected**, not
deferred. What is deferred is the one narrow form that pays for itself.

## The narrow case

`CLAUDE.md` records that paket/binary-referenced solution projects (`src/dfs` in MedDBase) tag effects at
their call sites but their internals are not traversable: no bodies, so no call edges through them. After L2
those calls at least become leaf nodes, but a reach that PASSES THROUGH such an assembly still stops dead.

Unlike a framework assembly, this code is:
- ours, so its semantics are business logic that no effect rule covers or ever will;
- small, so decompiling it is not a framework-closure explosion;
- already an acknowledged hole rather than a hypothetical improvement.

## Shape if it is ever built

An explicit whitelist in `rig.rules.json` naming assemblies to index from metadata, plus a decision on the
reader: IL via a metadata reader, or decompiled C# re-fed through extraction (`ilspycmd` is already available
on this machine). Decompiled C# reuses the whole extraction path; raw IL means a second, structurally
different notion of "a call" living in the same store — async state machines, closures, iterators and
generic instantiation all differ from the syntax-derived graph. That divergence is the main design risk and
the reason this is not a small task.

## Why NOT the general form

- Duplicates the rules. `SaveChangesAsync` = a write is one line of `builtin-rules.json`; the equivalent graph
  is hundreds of nodes inside EF that must then be folded back into "a write" anyway.
- Framework closure is tens of thousands of types; decompilation plus extraction per assembly is comparable
  to indexing the solution itself.
- IL fidelity, as above.

## Trigger to revisit

A concrete question that needs a reach THROUGH a first-party binary dependency — most likely a `src/dfs` path
in MedDBase that `reaches`/`path` currently cannot answer.
