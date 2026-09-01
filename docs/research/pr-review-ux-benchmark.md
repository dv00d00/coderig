# PR review UX benchmark: GitHub, GitLab, Bitbucket Cloud

Date: 2026-09-01  
Scope: desktop-first changed-file navigation and single-file diff review. Mobile is a graceful-degradation
constraint, not a separate product surface.

## Executive take

The mature products do not treat the changed-file rail as a raw list. It is the reviewer's work queue:
find a file, understand the shape of the change, remember what has been reviewed, and move to the next useful
item. CodeRig already has the hard and differentiating part — a fast hunk-only diff with semantic effect marks —
but its navigation still reads like a prototype.

The smallest high-leverage slice is:

1. a sticky file search plus compact filters (`All`, `Unreviewed`, `Effects changed`);
2. `Tree` / `List` display modes;
3. filename-first list rows (basename primary, trailing parent path secondary, no wrapping), with full path on
   hover;
4. local per-base/head `Viewed` state, visible both in the file header and the rail;
5. a file header that leads with filename and `+/-`, then gives CodeRig's effect delta, with commit SHAs demoted.

This would make the screen feel like a review tool without trying to reproduce hosting, conversations, approvals,
or merge workflows.

## What the top three converge on

| Concern | GitHub | GitLab | Bitbucket Cloud | Useful synthesis for CodeRig |
|---|---|---|---|---|
| Changed-file navigation | Searchable file tree; filter menu can narrow by extension, viewed state, ownership, and other facets. The current tree is resizable and carries comment/error/warning indicators. ([file-tree launch](https://github.blog/changelog/2022-03-16-pull-request-file-tree-beta/), [2026 Files changed update](https://github.blog/changelog/2026-01-22-improved-pull-request-files-changed-page-on-by-default/)) | File browser can switch between nested tree and flat list; it can be toggled with `F`. `Cmd/Ctrl+P` jumps to a file, while `j/k` or `[/]` move between files. ([Changes docs](https://docs.gitlab.com/user/project/merge_requests/changes/), [keyboard shortcuts](https://docs.gitlab.com/user/shortcuts/)) | Persistent file tree; file-path filtering supports inclusion and `!` exclusion. A separate search replaces the tree with matching changed lines and jumps to the selected result. Tree/List is a saved preference. ([review docs](https://support.atlassian.com/bitbucket-cloud/docs/review-code-in-a-pull-request/)) | Always-visible path search; Tree/List toggle; small filter popover. Do not build changed-code search yet. |
| Review progress | `Viewed` on the file header collapses the file and is cleared when that file changes. File filters can hide viewed files. ([review docs](https://docs.github.com/en/pull-requests/how-tos/review-pull-requests/reviewing-proposed-changes-in-a-pull-request), [filter update](https://github.blog/changelog/2021-09-27-improved-pull-request-file-filtering/)) | File header has `Viewed`; viewed files stay out of the work set until their content changes or the reviewer clears the flag. ([Changes docs](https://docs.gitlab.com/user/project/merge_requests/changes/)) | `Viewed` collapses the file, persists when returning to the PR, and resets when a new commit changes the file. The top-level diff can also be limited to changes since the last review. ([review docs](https://support.atlassian.com/bitbucket-cloud/docs/review-code-in-a-pull-request/)) | Local viewed state keyed by base SHA + head SHA + path is enough for the first slice. Show `N / total viewed`; offer `Unreviewed`; move to the next unreviewed file. |
| File header hierarchy | The official current screenshot puts the path first, then compact change magnitude, `Viewed`, comment, and overflow actions. The page-level toolbar holds review-wide controls. ([2026 Files changed update](https://github.blog/changelog/2026-01-22-improved-pull-request-files-changed-page-on-by-default/)) | The documented header contains full path, `+/-` line counts, `Viewed`, file comment, and options; context expansion lives in the diff gutter. ([Changes docs](https://docs.gitlab.com/user/project/merge_requests/changes/)) | File-level viewing/actions live in the file header's overflow; the review-wide settings live at the PR level. ([review docs](https://support.atlassian.com/bitbucket-cloud/docs/review-code-in-a-pull-request/)) | Filename/path and `+/-` are the identity layer. CodeRig's effect delta is the next layer. Commit SHAs and derivation-loading prose are supporting metadata, not the headline. |
| Diff layout and whitespace | Unified/Split is a reusable preference; hide-whitespace is per PR and remembered. ([review docs](https://docs.github.com/en/pull-requests/how-tos/review-pull-requests/reviewing-proposed-changes-in-a-pull-request)) | Inline/Side-by-side and whitespace visibility live under one Preferences control. ([Changes docs](https://docs.gitlab.com/user/project/merge_requests/changes/)) | Global settings include unified/side-by-side, whitespace, word diff, syntax highlighting, annotations, tab size, and all-at-once/individual file loading. ([review docs](https://support.atlassian.com/bitbucket-cloud/docs/review-code-in-a-pull-request/)) | CodeRig's single settings menu containing Unified/Split and Hide whitespace is already on-pattern. Keep it; do not invent a custom segmented widget. |
| Large reviews | GitHub's current large-review experiment virtualizes diff content to reduce DOM nodes/listeners and explicitly discloses the trade-off with browser find/copy/print. ([2026 Files changed update](https://github.blog/changelog/2026-01-22-improved-pull-request-files-changed-page-on-by-default/)) | Files with many changes are collapsed for performance; one-file-at-a-time is a persistent preference with next/previous navigation. Rapid Diffs optimizes time to first file. ([Changes docs](https://docs.gitlab.com/user/project/merge_requests/changes/)) | Diff limits trigger individual file loading; navigation remains available from the tree and header. Users can choose individual loading by default. ([diff limits](https://support.atlassian.com/bitbucket-cloud/docs/limits-for-viewing-content-and-diffs/)) | CodeRig's one-file, hunk-only payload is already the right default. Avoid all-files DOM and full-source rendering. Virtualize only the file rail if real PRs make it necessary. |

## Long names and wrapping

The strongest common answer is hierarchy, not wrapping. A tree removes repeated parent paths; a list must preserve
the discriminating suffix. GitHub's official file-tree screenshot shows compact one-line folder/file rows, while
GitLab's own file-navigation design explicitly separated **filename on the first line** from **path on the second**
and placed ellipsis at the beginning of an overlong path. ([GitHub file-tree launch](https://github.blog/changelog/2022-03-16-pull-request-file-tree-beta/),
[GitLab file-nav design issue](https://gitlab.com/gitlab-org/gitlab-foss/-/issues/36687))

Recommended list row:

```text
M  FileEffectsQueryService.cs
   …/src/Rig.Cli/Services
```

- Keep both lines single-line. Never `overflow-wrap:anywhere` a navigation row.
- Give the basename the stronger weight and preserve it for as long as possible.
- Left-truncate the parent path so the nearest directories survive; expose the full repo-relative path in
  `title`/tooltip and to assistive text.
- For renames, keep the new basename primary and render the old path as compact secondary metadata rather than
  concatenating two complete paths with an arrow.
- In Tree mode, show one-line folder and file labels with end ellipsis and full-path tooltip; repeated directories
  should exist once, as structure.

For code lines, do **not** enable wrapping by default. Split diffs especially need stable line-to-line geometry;
horizontal scrolling is less surprising than soft-wrapped halves with mismatched heights. A later `Wrap long lines`
preference is reasonable, but it is not part of the top-three shared minimum and should not displace navigation or
review progress.

## Current CodeRig audit

### Already strong

- The browser receives a single file's patch hunks rather than full large source files. This aligns with the
  individual-file/large-review strategies above.
- Unified/Split plus Hide whitespace already live in one restrained gear menu
  ([`file-diff.tsx`](../../src/Rig.Cli/WebClient/src/file-diff.tsx)).
- The diff has syntax highlighting and line-level effect/hazard/amplification annotations — the distinctive product
  value the hosts do not provide as a general call-chain effect lens.
- The desktop layout gives the file rail and diff independent scrolling regions.

### Where it visibly trails

1. **The rail is not a work queue.** `ReviewFileList` is only `files.map(...)`: no search, filter, hierarchy,
   reviewed state, or keyboard progression ([`components.js`](../../src/Rig.Cli/wwwroot/components.js)).
2. **Path scanning is backwards.** Each row renders the entire path with `overflow-wrap: anywhere`, creating uneven
   heights while hiding the basename at the far end ([`index.html`](../../src/Rig.Cli/wwwroot/index.html)). This is
   the clearest “prototype” signal.
3. **The count is ambiguous.** `reviewable / total` looks like review progress but actually exposes an implementation
   capability boundary. Mature products use this area for viewed progress.
4. **Added/deleted/renamed files are present but disabled.** This is more damaging than a styling gap: a reviewer
   expects every changed file to be inspectable. The opacity treatment also makes the list look broken. Two-path
   diff support should be the next contract slice even if it does not fit the visual pass.
5. **The file header leads with machinery.** Full path, base/head hashes, `base marks`, `head marks`, and tier-loading
   prose all compete on one line. It omits the universally useful `+/-` summary and has no Viewed action
   ([`file-diff.tsx`](../../src/Rig.Cli/WebClient/src/file-diff.tsx)).
6. **The rail width is fixed by CSS (`minmax(220px, 18vw)`).** Long Java/C# solution paths cannot buy more space;
   current GitHub explicitly makes its file tree resizable. Resizing is valuable, but lower priority than presenting
   the right content inside the existing width.
7. **Semantic annotations consume a very wide gutter.** The 12rem gutter preserves effect pills but can starve code,
   especially in Split. The semantic signal should stay scannable while details expand on demand.

## Prioritized gap analysis

### P0 — make navigation feel intentional

- Sticky rail header: `Changed files`, true viewed progress, search field, filter button, Tree/List toggle.
- Search repo-relative paths client-side, case-insensitive; `/` or `Cmd/Ctrl+P` focuses it.
- Filters: `All`, `Unreviewed`, `Effects changed`; status/family filters can live in the popover.
- Filename-first two-line list rows as specified above; fixed row rhythm, no wrapping.
- Tree view derived client-side from the existing flat paths; remember the chosen view locally.
- Viewed checkbox in the file header, viewed indicator in both modes, `Next unreviewed` after marking.

`Effects changed` is the key CodeRig addition: it should mean an effect family/site delta between base and head,
not merely “this file has an effect somewhere.” If that delta is not yet available in the list payload, label the
interim filter honestly as `Has effects`.

### P1 — sharpen the diff hierarchy

- Header line 1: basename + subtle parent path; `+N -N`; Viewed; settings/overflow.
- Header line 2: compact effect delta, for example `SQL +1` / `filesystem introduced` / `2 hazards`; click opens the
  existing semantic detail. Replace base/head mark counts and tier-loading prose with stable skeletons or quiet
  status text.
- Keep SHAs in a tooltip, copy affordance, or review-wide base/head bar rather than repeating them as primary file
  metadata.
- Make the rail collapsible/resizable and preserve its width.
- Add previous/next-unreviewed keyboard navigation.

### P2 — credibility gaps after the visual pass

- Review added, deleted, copied, and renamed files through a two-path contract.
- Add small per-file change magnitude (`+/-`) and semantic indicators to the rail without turning every row into a
  badge cloud.
- Consider `Wrap long lines`, tab-size, and accessible diff colours only as secondary preferences.
- If thousands of files are demonstrated in real stores, window the rail; do not pre-emptively complicate it.

## Recommended implementation slice

One contained pass can make a materially better first impression:

1. Extend review UI state with `fileQuery`, `fileView: tree|list`, `fileFilter: all|unreviewed|effects`, and a
   base/head-scoped local viewed set. No server persistence.
2. Refactor `ReviewFileList` into a small navigator module with pure path-to-tree and filtering helpers. Render a
   sticky toolbar and either tree nodes or filename-first list rows.
3. Add Viewed and `+/-` to the file header. Keep the existing gear unchanged. Collapse the semantic summary into
   effect-family deltas; keep detailed findings in the existing line widgets.
4. Preserve the current one-file/hunk-only data path. Do not render all files, full source, comments, or hosting
   controls.
5. Treat two-path A/D/R support as the next explicit slice; until then, keep these rows readable and explain the
   limitation without making them resemble disabled application chrome.

Success should be judged on a 50–200-file synthetic review with deliberately duplicated basenames and deep paths:
the reviewer can find `FileEffectsQueryService.cs`, distinguish two `Program.cs` files, isolate effectful unreviewed
files, mark one viewed, and advance without the rail changing height or the diff reloading unnecessarily.

