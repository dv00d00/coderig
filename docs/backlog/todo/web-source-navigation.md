# Web source navigation — go-to-declaration, find-usages and tree-from-any-symbol over rendered source

**Status:** todo · **Found:** 2026-09-02, auditing what the web surface can already answer · **Family:** web / navigation
**Triage:** needs-info (column facts shipped; the ambiguity-picker and exact-column interaction are undecided)
**Depends on shipped substrate:** [call-site column facts](../done/call-site-facts-no-column-same-line-calls-collapse.md).

## The ask

Rendered source in the browser should behave like source: click a symbol and go to its declaration, list its
usages, or open a tree rooted at it.

## Everything needed already exists — except one thing

- `symbol_facts` carries `SymbolId` / `FilePath` / `Line`, so a **declaration** is a direct lookup.
- `reference_facts` is indexed on `TargetSymbolId` (**2.44M rows**), so **find-usages** is an indexed read,
  not a graph walk.
- `/api/tree?from=` is now disk-cached, so **tree-from-any-symbol** is already affordable.
- Full-file source rendering shipped 2026-09-02 (`GET /api/review-source`, revision-native, Git-blob exact) —
  see [web-review-effect-gutter-and-delta](../done/web-review-effect-gutter-and-delta.md).

Store schema v8 now records the reference column. The remaining product question is how exact-column
resolution and the ambiguity picker compose when source/token coordinates are stale or incomplete.

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

Recording a column on call-site facts was the long-term answer and shipped in store schema v8 — see
[call-site column facts](../done/call-site-facts-no-column-same-line-calls-collapse.md). Decide whether an exact
column match bypasses the picker or merely ranks its candidates; either way, ambiguity must still fail closed.

## What counts as finishing

- Go-to-declaration, find-usages and tree-from-any-symbol all work from rendered source.
- Ambiguity is always disclosed and never silently resolved.
- Find-usages reads through the `TargetSymbolId` index; it does not traverse the call graph.
- Nothing here requires a re-index.
