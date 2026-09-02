# `rig reaches <effect> --without-guard <pred>` — the guard-on-path query

**Status:** todo · **Family:** reviewer-invokable queries · guards
**Extracted from:** [reviewer-invokable-queries](../done/reviewer-invokable-queries.md) (ranked item 3), 2026-09-02
**Triage:** needs-triage

## The question it answers

"Which entry points reach this write **without** passing through guard G?" — the merged-patient check, the
deleted-status filter, `AssertRight`. It serves the guard-divergent and authz-before-write corpus clusters
(#1718, #1742, #290, #851 / #852) and is a question a diff-local reviewer structurally cannot answer.

## What already shipped

Guards are already captured as effects (`permission:assert`) and `rig effects-diff` already diffs them with
per-row `provider:op` kind labels, so *comparing two paths* works today. What is missing is the
**negative** query over one effect: reach filtered by absence of a predicate on the path. Record:
[reviewer-invokable-queries](../done/reviewer-invokable-queries.md).

## What it needs

A small guard-shape model — the parent card names `AssertRight`, `IsNone` and status checks as the call
shapes to model. That model is rules data, not core C#.

## The model boundary to disclose, not paper over

Guard sets are **direct** (one-hop) control dependence, so the `⎇` annotation is "the nearest gating
predicate", not the complete firing condition; an early-return chain attaches its guards up the chain, not
onto the call site. That is a decided wontfix — see
[guard-set-direct-vs-transitive-control-dependence](../done/guard-set-direct-vs-transitive-control-dependence.md).
Consequence for this card: `--without-guard` can honestly answer "no edge on this path carries predicate G",
and must not claim "G cannot hold". ANDing predicate *text* across methods is also semantically loose
(`name` here ≠ `name` elsewhere).

## What counts as finishing

- The flag answers over the existing reachability + effect graph, with no new extraction facts.
- Guard shapes come from rules data.
- The disclosure above is in the output, not only in this card.
- Fixtures: an EP that passes the guard, one that does not, and one where the guard sits on a lambda edge
  (that class was fixed by `guards-missing-on-lambda-and-method-group-edges` and must stay fixed).
