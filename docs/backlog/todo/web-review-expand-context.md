# Web Review: expand Git hunk context on demand

**Status:** todo · **Family:** web review / diff navigation
**Triage:** ready-for-agent

## Problem

The one-file renderer intentionally receives Git hunks rather than both full source blobs. That keeps large
files fast, but a reviewer cannot reveal nearby unchanged code when the fixed context window is insufficient.

## Accepted interaction

- Each hunk boundary can request more context above, below, or the whole gap to the adjacent hunk.
- The server reads the requested revision slice or asks Git for a larger bounded patch; it does not ship both
  complete files by default.
- Expanded rows retain old/new line coordinates so existing base/head semantic marks continue to join exactly.
- Repeated expansion coalesces overlapping ranges and does not duplicate rows.

## Acceptance

- Synthetic adjacent, overlapping, file-start, and file-end hunks expand correctly in Unified and Split.
- A large-file fixture proves initial payload/DOM size is unchanged and expansion cost is proportional to the
  requested range.
- Semantic marks remain attached to their original revision lines after expansion.

## Out of scope

- Rendering every changed file at once.
- Browser-find guarantees across source that has not been loaded.
- Soft-wrapping code by default.
