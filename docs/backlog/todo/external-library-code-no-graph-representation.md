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

## Candidate levels — record as options, not decisions

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
