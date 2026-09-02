# Web source navigation — go-to-declaration, find-usages and tree-from-any-symbol over rendered source

**Status:** todo · **Found:** 2026-09-02, auditing what the web surface can already answer · **Family:** web / navigation
**Triage:** needs-info (the ambiguity-picker UX is undecided; everything else is in place)
**Blocked by:** [call-site-facts-no-column-same-line-calls-collapse](./call-site-facts-no-column-same-line-calls-collapse.md)
— for the **exact-column** option only. The agreed line+token approach below is not blocked by it.

## The ask

Rendered source in the browser should behave like source: click a symbol and go to its declaration, list its
usages, or open a tree rooted at it.

## Everything needed already exists — except one thing

- `symbol_facts` carries `SymbolId` / `FilePath` / `Line`, so a **declaration** is a direct lookup.
- `reference_facts` is indexed on `TargetSymbolId` (**2.44M rows**), so **find-usages** is an indexed read,
  not a graph walk.
- `/api/tree?from=` is now disk-cached, so **tree-from-any-symbol** is already affordable.
- Full-file source rendering shipped 2026-09-02 (`GET /api/review-source`, revision-native, Git-blob exact) —
  see [web-review-effect-gutter-and-delta](../progress/web-review-effect-gutter-and-delta.md).

The missing piece is **resolving which symbol was clicked**, because `reference_facts` has **no `Column`**.

## Agreed approach

**Line + token-text match, falling back to a line picker when ambiguous.** Never the match alone: a browser
that silently jumps to the wrong overload is worse than one that asks.

Rider's own fallback does not port. It resolves the PSI reference
(`RigEffectDaemonStage.cs:193,217-227`) — there is **no semantic model in the browser**, so that path has no
web equivalent. The picker is the substitute for the semantic model, not a convenience.

## What is undecided

The picker's UX: when a line's token text matches more than one candidate, what the reader is shown and how a
choice is remembered (if at all). That is the reason this card is `needs-info` rather than
`ready-for-agent`.

## The exact-column option, and why it is not the plan

Recording a column on call-site facts would make click resolution exact and would also fix same-line call
collapse — see
[call-site-facts-no-column-same-line-calls-collapse](./call-site-facts-no-column-same-line-calls-collapse.md).
It is a write-side change and implies a re-index, so it is the better long-term answer and the worse next
step.

## What counts as finishing

- Go-to-declaration, find-usages and tree-from-any-symbol all work from rendered source.
- Ambiguity is always disclosed and never silently resolved.
- Find-usages reads through the `TargetSymbolId` index; it does not traverse the call graph.
- Nothing here requires a re-index.
