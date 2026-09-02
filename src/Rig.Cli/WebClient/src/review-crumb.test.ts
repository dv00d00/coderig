import assert from "node:assert/strict";
import test from "node:test";

// store.js reaches filelens.js, which registers a document click listener at module scope. The crumb helpers
// under test are pure state math, so the stub only has to survive that import — hence the dynamic import,
// which a hoisted static one would run before the stub exists.
(globalThis as { document?: unknown }).document ??= { addEventListener() {} };
// @ts-expect-error The zero-build browser module is plain JavaScript, shared directly with this runner.
const { isReviewPivot, reviewCrumbPatch, reviewCrumbState } = await import("../../wwwroot/store.js");

// Actual paths and store ids from GET /api/review-files?base=036a954facf3&head=904674e12dc1.
const viewing = (file: string) => ({
  appMode: "review",
  reviewBase: "036a954facf3",
  reviewHead: "904674e12dc1",
  reviewFile: file,
  reviewLine: 0,
  reviewSide: "head",
});

test("a review crumb carries the whole review position", () => {
  const state = { ...viewing("src/Rig.Cli/Web/FileDiffEndpoint.cs"), reviewLine: 412, reviewSide: "base" };
  assert.deepEqual(reviewCrumbState(state, "src/Rig.Cli/wwwroot/main.js"), {
    base: "036a954facf3",
    head: "904674e12dc1",
    file: "src/Rig.Cli/wwwroot/main.js",
    line: 412,
    side: "base",
  });
});

test("a pushed review crumb restores the same view it was recorded from", () => {
  const before = { ...viewing("src/Rig.Cli/wwwroot/main.js"), reviewLine: 412, reviewSide: "base" };
  const crumb = reviewCrumbState(before, before.reviewFile);
  const restored = { ...viewing("src/Rig.Cli/Web/FileDiffEndpoint.cs"), ...reviewCrumbPatch(crumb), reviewFile: crumb.file };
  assert.deepEqual(restored, before);
  assert.deepEqual(reviewCrumbState(restored, crumb.file), crumb);
});

test("re-selecting the file already open is not a history entry", () => {
  const state = viewing("src/Rig.Cli/wwwroot/main.js");
  assert.equal(isReviewPivot(state, "src/Rig.Cli/wwwroot/main.js"), false);
  assert.equal(isReviewPivot(state, "src/Rig.Cli/wwwroot/store.js"), true);
  assert.equal(isReviewPivot(state, ""), false);
});

test("a restored selection cannot push the entry it just restored", () => {
  const crumb = reviewCrumbState(viewing("src/Rig.Cli/wwwroot/store.js"), "src/Rig.Cli/wwwroot/main.js");
  const restored = { ...viewing("src/Rig.Cli/wwwroot/store.js"), ...reviewCrumbPatch(crumb), reviewFile: crumb.file };
  assert.equal(isReviewPivot(restored, crumb.file), false);
});
