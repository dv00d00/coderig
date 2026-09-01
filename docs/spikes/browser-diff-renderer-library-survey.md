# Browser diff renderer survey for Rig

Date: 2026-09-01

## Decision

For a production Rig diff view, start with a small **React island using
`react-diff-view`**. It is patch-native, MIT licensed, actively maintained, and—unlike the
HTML generators—has first-class `renderGutter`, per-line events/classes, decorations, and
widget rows. Those APIs map directly to effect glyphs, hover cards, links, and an expanded
“why does this line have this effect?” row. It is also mature enough (3.3.3, published in
March 2026) not to make a 0.x component the load-bearing UI dependency. Its documented
large-diff story is hunk/file lazy loading plus worker-side tokenization rather than built-in
DOM virtualization, so Rig should keep the API/file navigation paged and never mount an
entire monorepo diff at once. Sources: [repository and API](https://github.com/otakustay/react-diff-view),
[npm package](https://www.npmjs.com/package/react-diff-view),
[current package update](https://github.com/otakustay/react-diff-view/commit/776e11d00cad4b611600b9a93e783b15d4f0b5a4).

Two alternatives are worth retaining:

- **`diff2html` is the fastest throwaway/no-build spike.** It ships a browser bundle and CSS,
  accepts `git diff`/unified-diff text, and draws line-by-line or side-by-side. Its parsed
  `DiffLine` model carries old/new line numbers, but it has no first-class line/gutter/widget
  renderer. Rig would have to enrich generated DOM or replace the HTML renderer, which is an
  unattractive production seam. Sources: [README/browser distribution](https://github.com/rtfpessoa/diff2html),
  [`DiffLine` model](https://github.com/rtfpessoa/diff2html/blob/master/src/types.ts),
  [MIT license](https://github.com/rtfpessoa/diff2html/blob/master/LICENSE).
- **`@git-diff-view/react` has the best effect-overlay-shaped API**, including `extendData`
  keyed independently by old/new line number, `renderExtendLine`, widget rows and click
  callbacks. It also accepts Git hunks, supports workers, and has a range mode. It is the
  most compelling experiment if its 0.x API churn is acceptable; version 0.1.7 was released
  in July 2026 and development continued in August. Sources: [repository/feature overview](https://github.com/MrWangJustToDo/git-diff-view),
  [React API](https://github.com/MrWangJustToDo/git-diff-view/blob/main/packages/react/readme.md),
  [`extendData` implementation](https://github.com/MrWangJustToDo/git-diff-view/blob/main/packages/react/src/components/DiffView.tsx),
  [0.1.7 release](https://github.com/MrWangJustToDo/git-diff-view/releases/tag/v0.1.7).

In other words: `react-diff-view` is the safer product choice today; `@git-diff-view/react`
is the higher-upside spike; `diff2html` is the lowest-friction disposable prototype.

## Fit matrix

| Candidate | Input | Views / visual diff | Rig overlay seam | Large-diff behavior | Frontend fit | Status / license |
|---|---|---|---|---|---|---|
| [`react-diff-view`](https://github.com/otakustay/react-diff-view) | Git/unified patch via `parseDiff` | Unified + split; syntax tokenization and intraline edit marks | **Excellent:** `renderGutter`, widgets below a change, decorations around hunks, per-line classes/events, line-number lookup helpers | Demo includes lazy-loaded large diffs; tokenization can run in a worker; no documented viewport virtualization | Requires React and a bundle, but can live in one isolated mount | 3.3.3, updated 2026-03; MIT |
| [`@git-diff-view/react`](https://github.com/MrWangJustToDo/git-diff-view) | Git hunks plus old/new content, or two-file comparison | Unified + split; full-context syntax highlighting; FastDiff intraline mode | **Best-shaped:** old/new line-keyed `extendData`, `renderExtendLine`, widget rows/click callbacks | Worker support, optimized template mode, and line-range instances; no documented web viewport virtualization | Requires React and a bundle (Vue/Solid/Svelte variants also exist; no vanilla renderer) | 0.1.7, active Aug 2026; MIT |
| [`diff2html`](https://github.com/rtfpessoa/diff2html) | Git/unified patch | Line-by-line + side-by-side; highlight.js; line/word matching | **Weak:** useful old/new line numbers in parsed JSON, but no line renderer/gutter/widget extension API | Can stop rendering above `diffMaxChanges`; no virtualization | **Best no-build fit:** distributable browser JS/CSS bundle | 3.4.56, updated Jan 2026; MIT |
| [`react-diff-viewer-continued`](https://github.com/Aeolun/react-diff-viewer-continued) | Old and new strings/objects; computes its own diff | Inline + split; word diff; syntax via `renderContent` | **Good:** extra `renderGutter`, line-click/highlight hooks, fold renderer; no general per-line widget row | Built-in `infiniteLoading` viewport virtualization, on-demand word diffs, worker computation | Requires React, Emotion, and a bundle | 4.4.0, released Jul 2026; MIT |
| [Monaco Diff Editor](https://microsoft.github.io/monaco-editor/typedoc/interfaces/editor_editor_api.editor.IDiffEditorOptions.html) | Old/new text models; computes its own diff | Inline + side-by-side; syntax and intraline diff | **Excellent editor seam:** model decorations, glyph margin, injected text/content/overlay widgets | Mature editor viewport and worker architecture | No React, but a substantial editor runtime plus worker/bundler setup; AMD build is deprecated | 0.56.0, released Jul 2026; MIT |
| [CodeMirror 6 Merge](https://codemirror.net/docs/ref/#merge) | Old/new documents; computes its own diff | `MergeView` side-by-side + `unifiedMergeView`; change highlighting and language packages | **Excellent editor seam:** decorations, gutters, line markers and widgets through extensions | CodeMirror renders around the active viewport; unchanged sections can collapse | Framework-neutral ESM, but still introduces an npm module graph/build or import-map/vendor step | 6.12.2, updated Jun 2026; MIT |
| [GitLab’s diff frontend](https://docs.gitlab.com/development/merge_request_concepts/diffs/frontend/) | GitLab-specific diff-file and discussions payloads | GitLab’s actual inline/parallel review UI | Capable, but the seam is GitLab application state/components, not a library API | File batching, memoization and functional `diff_row` rendering | **Poor dependency fit:** a Vue application coupled to GitLab APIs/store/notes | Client-side JS is MIT, but this is source to study, not a published renderer |

Maintenance dates and versions above come from the projects’ official repositories/releases and npm
packages as observed on 2026-09-01. CodeMirror’s old GitHub mirror is archived because the repository
[moved to the maintainer’s forge](https://github.com/codemirror/merge); the npm package and official docs
remain current.

## Candidate notes

### `react-diff-view`: recommended baseline

This is the closest match to a code-review surface rather than a generic editor. Its documented change
key and old/new line-number helpers let Rig join a semantic read model to either side of the patch. A
gutter glyph can stay compact; a widget row can disclose paths, depth, hazards, and links without fighting
the red/green diff background. The library’s own example uses widgets for code comments, which is almost
the same interaction shape as expanding an effect explanation. See the official
[widgets and gutter documentation](https://github.com/otakustay/react-diff-view#add-widgets).

The cost is architectural: Rig currently serves plain ES modules and its own tiny DOM renderer from
[`wwwroot/main.js`](../../src/Rig.Cli/wwwroot/main.js) / [`wwwroot/components.js`](../../src/Rig.Cli/wwwroot/components.js),
with no package manifest or frontend build. The safest adoption is therefore not a rewrite: add one built
`diff-island.js` artifact mounted into the existing shell, keep state/fetching in the current controller,
and pass the island an immutable diff read model.

### `@git-diff-view/react`: best overlay API, young dependency

This component’s `extendData` is explicitly shaped as:

```ts
{
  oldFile: { [lineNumber]: { data: T } },
  newFile: { [lineNumber]: { data: T } }
}
```

and `renderExtendLine` receives the side, line number and data. That is an unusually exact fit for a Rig
effect read model. It also has framework packages for React, Vue, Solid, and Svelte, but “framework
agnostic” refers to multiple adapters; there is no documented vanilla DOM/Web Component renderer. Its
version and release history make it suitable for a time-boxed spike before accepting it as a core UI
dependency.

### Editor components: powerful but the wrong default abstraction

Monaco and CodeMirror expose the richest general decoration machinery. Monaco supports side-by-side/inline
diffs and model decorations; CodeMirror exposes merge views plus arbitrary `Decoration` and gutter
extensions. Both take complete old/new documents and compute their own diff. That loses exact Git hunk
selection unless Rig also constrains/collapses the result, and it makes a read-only review page inherit
editor lifecycle, selection, worker, accessibility and theming concerns.

CodeMirror is the better of the two if Rig later wants a framework-neutral, full-file semantic diff editor.
Monaco’s official integration guide requires explicit worker bundling for ESM, and the package’s own README
says mobile browsers are unsupported. Sources: [Monaco ESM integration](https://github.com/microsoft/monaco-editor/blob/main/docs/integrate-esm.md),
[Monaco decorations](https://microsoft.github.io/monaco-editor/typedoc/interfaces/editor_editor_api.editor.IModelDeltaDecoration.html),
[CodeMirror merge/decorations API](https://codemirror.net/docs/ref/#merge).

### GitLab source: UX reference, not vendorable component

GitLab’s official architecture document describes a Vue diff application whose top-level action waits for
metadata and batched-diff requests, then joins discussions to diff lines; `diff_row.vue` renders each line.
That is useful confirmation that “semantic facts keyed to diff side + line” is the right UI model. It is not
an independently packaged component: extracting it would also extract GitLab’s Vue/store/API assumptions.
GitLab licenses client-side JavaScript under MIT Expat, but license permissiveness does not remove that
coupling. Sources: [GitLab diff frontend overview](https://docs.gitlab.com/development/merge_request_concepts/diffs/frontend/),
[GitLab repository license](https://gitlab.com/gitlab-org/gitlab/-/blob/master/LICENSE).

## Rig read-model seam

Do not attach effects to DOM row order. The API should return Git coordinates and annotations separately,
so the renderer remains replaceable:

```json
{
  "file": "src/Rig.Cli/Web/FileEffectsEndpoint.cs",
  "base": { "store": "abc123", "content": "..." },
  "head": { "store": "def456", "content": "..." },
  "patch": "diff --git ...",
  "annotations": {
    "old": { "42": [{ "family": "SQL", "depth": 2, "target": "M:..." }] },
    "new": { "47": [{ "family": "SQL", "depth": 1, "target": "M:..." }] }
  }
}
```

The line-side distinction is load-bearing:

- deleted lines join effects from the **base** store;
- added lines join effects from the **head** store;
- context lines normally show head effects, with a delta marker only when base/head semantics differ;
- a renderer may omit unchanged lines outside a hunk, so annotations outside visible context must not be
  treated as “missing.”

That contract works unchanged with `react-diff-view`, `@git-diff-view/react`, a future Rider adapter, or a
hand-rendered HTML fallback. It also prevents a library choice from leaking into the semantic query layer.

## Proposed proof, not a frontend rewrite

Time-box the first implementation to one file and one known base/head pair:

1. Add a server endpoint returning the read model above, using Git’s patch as the source of hunk truth.
2. Build a React island with `react-diff-view`; render one effect glyph in `renderGutter`, one hover/click,
   and one expanded widget row containing the existing effect/path chips.
3. Exercise a deleted-effect, added-effect, unchanged-context effect, and a line with multiple effects.
4. Load a deliberately large single-file diff. If paging/hunk lazy loading is insufficient, repeat only the
   renderer slice with `@git-diff-view/react` and its range/worker path before committing to either package.

The outcome should answer the actual product question—whether effect/hazard semantics remain legible on a
real review diff—without first converting the whole Rig web UI to React.
