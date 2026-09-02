# Web Review: open added, deleted, copied, renamed, and non-indexed files

**Status:** shipped 2026-09-02 · **Family:** web review / Git diff contract

## Problem

`/api/review-files` inventories every Git-changed path, but `/api/file-diff` accepts one physical file that must
exist in both immutable stores. Review therefore disables added, deleted, copied, renamed, and text-only files.
That is the largest remaining credibility gap: a changed-file navigator must be able to inspect every row it
shows, even when CodeRig has semantic facts for only one side or neither side.

## Accepted contract

- Identify a review target by nullable `oldPath` and `newPath`, not one shared absolute path.
- Ask Git for the textual patch independently of semantic availability.
- Load base annotations only when the old path maps to an indexed base file; load head annotations only when the
  new path maps to an indexed head file.
- Return explicit per-side semantic states (`available`, `not-indexed`, `not-present`) rather than fabricating an
  empty effect model.
- Renames use the old path for base coordinates and the new path for head coordinates. Copies retain both paths.
- Non-C# and otherwise unindexed files render as honest text-only diffs. Syntax highlighting may fall back to
  plain text, but the row must open.

## Acceptance

- Synthetic Git fixtures cover M/A/D/R/C and an unindexed text file.
- Every `/api/review-files` row opens a patch; available semantic marks stay on the correct old/new side.
- Dirty or unattributable stores still fail closed for semantic coordinates.
- The current stable-path C# response remains compatible or has an explicit versioned migration.

## Shipped

- `/api/file-diff` validates one repo-relative changed-file identity against the exact Git inventory, then
  resolves nullable old/new paths and independently joins each side to its indexed physical file.
- M/A/D/R/C and unindexed text files share one response contract. Each side reports `available`, `not-indexed`,
  or `not-present`; unavailable effects remain `null` rather than pretending analysis returned an empty set.
- Review navigation, search, viewed progress, list/tree modes, and j/k traversal now include every changed file.
  The Semantic-ready filter remains the narrower both-sides-indexed capability.
- The immutable base/head inventory is shared for the lifetime of `serve`, so opening N files does not repeat
  the repository-wide rename/copy scan N times; a failed load is evicted and may be retried after repair.
- The React diff renders plain text without C# tokenization when appropriate, discloses Git status and path
  changes, and requests findings only for semantic sides. Existing absolute File-view deep links still resolve.
- A synthetic Git fixture proves all six representative rows (M/A/D/R/C plus unindexed Markdown) open with a
  non-empty patch and correctly attributed side semantics; arbitrary unchanged repository paths are rejected.
- Dogfooded through the installed package against CodeRig's own 37-file diff: all rows opened, an indexed C#
  file rendered both semantic sides, text-only and added files disclosed their unavailable sides, and the browser
  console remained clean. Local release gate: 1349/1349 tests.

## Out of scope

- Remote GitHub/GitLab transport and authentication.
- Comments, approvals, merge controls, or uploading review state.
- Re-indexing a missing side merely to make its annotations available.
