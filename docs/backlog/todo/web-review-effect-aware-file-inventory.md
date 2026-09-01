# Web Review: effect-aware changed-file inventory

**Status:** todo · **Family:** web review / semantic inventory
**Triage:** ready-for-agent

## Problem

The file rail can currently filter only by review state and by `Semantic-ready`, which means “the same indexed
path exists in both stores.” It does not know whether a file gained, lost, or changed any effect site. Calling
that filter `Effects` would be false, and issuing one file-effects query per row would create an obvious N+1 on
large reviews.

## Accepted contract

- Extend the changed-file inventory with compact per-file textual counts (`additions`, `deletions`) and semantic
  summaries for each side.
- The semantic summary is computed set-wise from store facts/read-model indexes, never by calling the per-file
  endpoint in a loop.
- Distinguish “has effects” from “effects changed.” The latter compares stable identities at the finest reliable
  grain available without pretending line moves are behavioral changes.
- Expose enough data for an `Effects changed` filter and one restrained row indicator; do not put full method or
  witness payloads in the inventory.

## Acceptance

- A 200-file synthetic review performs a bounded number of store queries independent of file count.
- Added, removed, unchanged, and family-changed semantic examples are pinned.
- Renames do not become a semantic delete+add solely because the path changed when stable symbol identity exists.
- The rail can isolate effect-changing unreviewed files without loading each file diff.

## Out of scope

- Full per-file call trees or witness paths.
- Treating a textual line move as an effect change.
- Virtualizing the rail before a real measurement requires it.
