# W3 — reachable-from / path-highlight overlay

**Status:** todo · **Family:** web explorer / reverse navigation
**Extracted from:** [web-reverse-nav-wins](../done/web-reverse-nav-wins.md) (W3), 2026-09-02
**Triage:** needs-triage

## The ask

Like the existing diff overlay, but for reachability: pick an entry point (for example from the callers
drawer) and highlight the edges and paths in the current tree that reach the selected node — a visual "why
is this reachable from X".

## What already shipped

The backend it needs: `/api/path` and `/api/reaches` plus `/api/callers` reach sets (`8b37545c`). The SPA
side: node context menu, reverse-nav drawer with who-reaches and EPs-by-service, the P3 path and P4 reaches
drawer modes with a per-EP path affordance, and W1 crumbs (`c9699b60`). Record:
[web-reverse-nav-wins](../done/web-reverse-nav-wins.md).

## Constraints carried from the parent

- Backs onto `/api/path` (already shipped) and/or a reach set from `/api/callers` — no new endpoint is
  assumed.
- Pairs with W1: the highlighted path becomes a crumb.
- W2 and W3 both touch shared SPA files and should land **sequentially**, W2 first.

## What counts as finishing

- Selecting an EP highlights the reaching edges in the current tree without a re-root.
- The highlight is a crumb, so back/forward restores it.
- Path-insensitivity is disclosed the way the rest of the surface does it: a highlighted path is a reachable
  path, not a path this run takes.
- A nested REVERSE tree is explicitly NOT this card — it needs a parent-tracking reverse walker in
  `FactPathFinder` and is held as
  [callers-reaches-underreport-followups](../needs-review/callers-reaches-underreport-followups.md).
