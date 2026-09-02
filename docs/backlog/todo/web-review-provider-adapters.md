# Web Review: accept a remote pull request through provider adapters

**Status:** todo — provider and authentication policy not chosen. · **Family:** web review / integrations
**Triage:** needs-info

## Goal

Let the focused Review surface start from a pull-request identity instead of requiring the user to choose two
local commit stores manually. The local Git/store contract remains the semantic source of truth; a provider
adapter supplies PR metadata and patch/file navigation.

## Decision required

Choose the first provider and deployment boundary:

- GitHub or GitLab;
- public/API-token/local CLI authentication;
- local-only adapter in `rig serve` or a separately hosted service.

Do not design a universal provider abstraction before one adapter has exercised the contract. The minimum
portable shape is PR identity, base/head commit, changed-file records, patch hunks, and provider deep links.

## Acceptance for the first adapter

- A PR URL/number resolves base/head commits and changed files without scraping provider HTML.
- CodeRig maps those commits to immutable stores or clearly requests indexing; it never annotates remote lines
  from a different local commit.
- Local text/semantic rendering is byte-for-byte equivalent to opening the same base/head pair directly.
- Missing credentials and unavailable commits fail with a useful recovery action.

**Unblocked by:** [Two-path file diffs](../done/web-review-two-path-file-diffs.md), so the provider adapter
does not inherit a local changed-file blind spot.

## Out of scope

- Posting comments, approvals, merge status, checks, or write access to the provider.
- Supporting GitHub, GitLab, and Bitbucket in the first slice.
