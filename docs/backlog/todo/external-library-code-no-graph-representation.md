# External and library code has no graph representation

**Status:** todo · **Found:** 2026-08-31 · **Family:** extraction / graph model

## What happens

An effect observed at a call into a referenced assembly — for example `DbConnection.BeginTransactionAsync`,
`DbTransaction.CommitAsync`, `HttpClient.SendAsync` — is keyed to the **enclosing in-solution method**; the
external target itself gets no node and the call gets no edge. Consequences:

- (a) No per-line call-site row for those lines in the Rider read model — the caller-level summary is the
  only signal that survives.
- (b) `callers`/`reaches` can never be asked about a library member, because it is not a node.
- (c) Nothing transitive is known about library internals, so the effect rules ARE the only semantics at the
  boundary — there is no fallback graph to consult when a rule doesn't cover a case.
- (d) A related, already-documented gap: paket/binary-referenced solution projects (`src/dfs` in MedDBase)
  tag effects at call sites but their internals are not traversable either — see the `CLAUDE.md` MedDBase
  section ("`--from` … paket/binary-referenced solution projects … silently drop out; their effects still tag
  at call sites but their internals aren't traversable"). That is the same shape of gap, one hop closer to
  first-party.

This is a different mechanism from `docs/backlog/done/external-virtual-override-orphans.md` (which is about a
call to an external base method silently dropping the edge that would have reached a first-party override).
Here there is no missing edge to a first-party target — the target genuinely has no first-party representation
at all.

## Decisions taken 2026-08-31

- **L1: done** (commit `50827d03`). Live effect: `Writes.cs` went 25 → 35 projected call sites; the
  `BeginTransactionAsync` (line 338) and `CommitAsync` (line 499) lines are marked at last.
- **L2: approved and being implemented, with no cost measurement gate.** The premise below that L2 needs an
  extraction change, a schema bump and a reindex is **WRONG** — `src/Rig.Storage/Queries/Reads.cs:320-327`
  states the store already keeps every method-call ref including BCL/library targets, and it is the call
  GRAPH that filters them with `TargetInSource`. So L2 is query-side: no reindex, no store growth, and the
  admission policy stays data rather than something baked into the store. Admission is the union of
  rule-mentioned declaring types (which is how BCL types like `System.Data.Common.DbConnection` get in) and
  non-framework assemblies. External nodes are LEAVES in this pass.
- **L3: deferred**, split out into `decompile-first-party-binary-references.md`. The general form is
  rejected outright; only the first-party binary-reference whitelist survives as a deferred item.
- Follow-on found while designing L2, not yet carded: dispatch through an EXTERNAL interface declaration
  (`IMediator.Send` and friends) resolves to nothing today, because the declaring member is external and the
  edge is dropped. L2 makes it a leaf, which is still not the first-party handler. That is plausibly a
  larger recall win than L2 itself.

## Candidate levels — the options as they stood before the decision above

**L1 — query-side (hours).** Project a call-site row straight from the effect's own `FilePath` + `Line`, with
no target id at all. This directly fixes consequence (a) above. It is being implemented separately; cross-
reference it here as the immediate mitigation, not as a fix for the underlying model — (b) and (c) remain
open under L1.

**L2 — extraction, the principled fix.** Mint a leaf node and an edge for a metadata call target using the
DocID Roslyn already resolves at extraction time (no new resolution work, just persisting what's already
computed). Needs, before it can go on by default:
- a target-admission policy — likely rule-relevant providers and/or non-framework assemblies only, or the
  node/edge count explodes into the whole BCL/framework closure;
- a measured store-growth number against that policy;
- a store schema bump and a full reindex (same cost shape as the column-mining fix in
  `call-site-facts-no-column-same-line-calls-collapse.md`).

**L3 — decompilation, opt-in only.** Read or decompile referenced assemblies to index their internals.
Argue the default is **NO**: it duplicates what the effect rules already encode at the boundary, explodes the
store with the framework closure, and raises IL-level fidelity questions the fact model was never designed to
answer. The one narrow case that plausibly pays for itself: an explicit whitelist of FIRST-PARTY
binary-referenced assemblies — which is exactly the `src/dfs` gap in (d) above, not the general framework
surface.
