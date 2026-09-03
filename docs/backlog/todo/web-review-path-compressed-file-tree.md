# Web Review: path-compressed file tree with semantic rollups

**Status:** todo · **Family:** web review / file inventory
**Triage:** ready-for-agent
**Extracted from:** [the shipped Web Review delivery record](../done/web-review-effect-gutter-and-delta.md),
2026-09-03.

## Problem

Folder disclosure now works, but a deeply nested review still spends one row per single-child directory and
the folder row carries no useful rollup. On a 104-file, six-level review the rail remains longer and less
scannable than the changed-file set warrants.

## Accepted interaction

- Compress maximal single-child directory chains into one path-labelled row without changing file identity.
- Preserve disclosure state by the full uncompressed path, so compression does not merge same-named folders or
  forget nested state.
- Show restrained descendant file counts. Semantic changed counts come from the set-wise inventory on
  [effect-aware changed files](./web-review-effect-aware-file-inventory.md), never one file-effects request per
  row.
- Search temporarily reveals matching branches and clearing search restores ordinary disclosure state, as the
  shipped tree already does.

## Acceptance

- Single-child, branching, same-name, rename, search, List/Tree, Viewed, and nested-collapse fixtures are pinned.
- Folder toggles make no semantic API request and do not navigate.
- A large review performs a bounded number of inventory queries independent of file count.

## Out of scope

- Virtualizing the file rail before measurement requires it.
- Full per-file witness payloads in folder rows.
