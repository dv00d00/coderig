# Web Review: open added, deleted, copied, renamed, and non-indexed files

**Status:** todo · **Family:** web review / Git diff contract
**Triage:** ready-for-agent

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

## Out of scope

- Remote GitHub/GitLab transport and authentication.
- Comments, approvals, merge controls, or uploading review state.
- Re-indexing a missing side merely to make its annotations available.
