import assert from "node:assert/strict";
import test from "node:test";
// @ts-expect-error The zero-build browser module is plain JavaScript, shared directly with this runner.
import { collapsedReviewFolders, reviewTreeRows, setReviewFolderSearch, toggleReviewFolder } from "../../wwwroot/review-tree.js";

const initial = () => ({
  reviewBase: "036a954facf3",
  reviewHead: "904674e12dc1",
  reviewFileSearch: "",
  reviewFolderCollapse: { normal: {}, search: {} },
});
type State = ReturnType<typeof initial>;
type File = { path: string };
type Row = { kind: string; file: File; path: string; expanded: boolean };

// Actual paths from GET /api/review-files?base=036a954facf3&head=904674e12dc1.
const files = [
  { path: ".claude/skills/rig/REFERENCE.md" },
  { path: "src/Rig.Cli/Caching/QueryCacheKeys.cs" },
  { path: "src/Rig.Cli/Caching/WarmStore.cs" },
  { path: "src/Rig.Cli/CommandLine/Root.cs" },
  { path: "tests/Rig.Tests/Cli/AnnotateCommandContractTests.cs" },
];
const rows = (s: State, input = files): Row[] => reviewTreeRows(input, collapsedReviewFolders(s));
const visible = (s: State, input = files) => rows(s, input).filter((row) => row.kind === "file").map((row) => row.file.path);
const toggle = (s: State, path: string): State => ({ ...s, ...toggleReviewFolder(s, path) });
const search = (s: State, value: string): State => ({ ...s, ...setReviewFolderSearch(s, value) });

test("review folders initially expose every file from the inventory", () => {
  assert.deepEqual(visible(initial()), files.map((file) => file.path));
  assert.equal(rows(initial()).find((row) => row.path === ".claude/skills/rig")?.expanded, true);
});

test("closing a folder removes all descendants but preserves independent nested collapses", () => {
  let s = toggle(initial(), "src/Rig.Cli/Caching");
  s = toggle(s, "src");
  s = toggle(s, "tests");
  assert.deepEqual(visible(s), [files[0].path]);
  assert.equal(rows(s).some((row) => row.path === "src/Rig.Cli"), false);
  s = toggle(s, "src");
  assert.deepEqual(visible(s), [files[0].path, files[3].path]);
  assert.equal(rows(s).find((row) => row.path === "src/Rig.Cli/Caching")?.expanded, false);
  assert.equal(rows(s).find((row) => row.path === "tests")?.expanded, false);
});

test("same-name nested and sibling folders have independent normalized full-path identities", () => {
  const input = [
    { path: "src/common/common/One.cs" },
    { path: "src/common/Two.cs" },
    { path: "tests\\common\\Three.cs" },
  ];
  let s = toggle(initial(), "src/common/common");
  assert.deepEqual(visible(s, input), [input[1].path, input[2].path]);
  s = toggle(s, "tests\\common");
  assert.deepEqual(visible(s, input), [input[1].path]);
  assert.equal(rows(s, input).find((row) => row.path === "tests/common")?.expanded, false);
});

test("search reveals matches and permits temporary collapse without destroying the normal state", () => {
  let s = toggle(initial(), "src");
  const matching = files.filter((file) => file.path.includes("QueryCache"));
  s = search(s, "QueryCache");
  assert.deepEqual(visible(s, matching), [files[1].path]);
  s = toggle(s, "src");
  assert.deepEqual(visible(s, matching), []);
  s = search(s, "QueryCacheKeys");
  assert.deepEqual(visible(s, matching), [files[1].path]);
  s = search(s, "");
  assert.deepEqual(visible(s), [files[0].path, files[4].path]);
});

test("collapse state is isolated by ordered review pair and restored on return", () => {
  let s = toggle(initial(), "src");
  const original = s;
  s = { ...s, reviewHead: "another-head" };
  assert.deepEqual(visible(s), files.map((file) => file.path));
  s = toggle(s, "tests");
  s = { ...s, reviewHead: original.reviewHead };
  assert.deepEqual(visible(s), [files[0].path, files[4].path]);
  s = { ...s, reviewBase: original.reviewHead, reviewHead: original.reviewBase };
  assert.deepEqual(visible(s), files.map((file) => file.path));
});

test("selection, viewed, file modes, and queue filters do not reset folder state", () => {
  const s = {
    ...toggle(initial(), "src/Rig.Cli/Caching"),
    reviewFile: files[1].path,
    reviewViewed: [files[1].path],
    reviewFileMode: "list",
    reviewFileFilter: "unreviewed",
  };
  assert.deepEqual(visible(s, files.filter((file) => !s.reviewViewed.includes(file.path))), [files[0].path, files[3].path, files[4].path]);
  assert.deepEqual(visible({ ...s, reviewFileMode: "tree", reviewFileFilter: "all" } as State), [files[0].path, files[3].path, files[4].path]);
});

test("folder state transitions and projection do not mutate their inputs", () => {
  const before = initial();
  const after = toggle(before, "src");
  const snapshot = structuredClone(after);
  const searched = search(after, "Cache");
  toggle(searched, "src/Rig.Cli");
  assert.deepEqual(before, initial());
  assert.deepEqual(after, snapshot);
  const input = [...files].reverse();
  rows(after, input);
  assert.deepEqual(input, [...files].reverse());
});
