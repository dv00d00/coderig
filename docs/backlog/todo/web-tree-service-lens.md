# W2 — service lens on TREE nodes

**Status:** todo · **Family:** web explorer / deployment attribution
**Extracted from:** [web-reverse-nav-wins](../done/web-reverse-nav-wins.md) (W2), 2026-09-02
**Triage:** ready-for-agent

## The gap

Deployment attribution rides only on the **callers EP list** (`EntryPointDto.Services`). Tree nodes carry
none, so a call crossing a deployment boundary — the dual-write smell from the context map — is invisible in
the tree.

## What already shipped

`/api/callers` (roots plus entry points, service-annotated), `/api/path`, `/api/reaches` and
`DeploymentAttributionLookup` (`8b37545c`); the SPA node context menu and reverse-nav drawer (`25355c65`);
and W1 pivot history / breadcrumbs (`c9699b60`) — crumbs across tree, callers, reaches, path, re-root and
impact, with browser back/forward. Record: [web-reverse-nav-wins](../done/web-reverse-nav-wins.md).

## The change, as that card specified it

Add `Services` to `TreeNodeDto` and thread `DeploymentAttributionLookup` through `TreeMapper.MapNode` using
`loc?.File` — the lookup is file-path based. Colour or badge nodes by owning service.

## Constraints carried from the parent

- Loaded-in is an **upper bound**; a node with no `File` returns `[]`, and that must read as "unattributed",
  not as "no service".
- Cost is a per-node lookup plus a `TreeNodeDto` / `TreeMapper` change. These are **shared contracts** — do
  it as its own change, not mixed with a fold edit.
- W2 and W3 are both SPA-heavy, touch shared SPA files, and should land **sequentially**.

## What counts as finishing

- A boundary-crossing call is visible at a glance in the tree.
- `TreeNodeDto` gains one field and nothing else changes shape.
- Cache thinking: the tree payload shape changes, so the tree artifact's `*Schema` constant is bumped with
  its `// vN->vM: <why>` trail — that is what invalidates warm disk caches and every browser's IndexedDB.
