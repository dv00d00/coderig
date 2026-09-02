import assert from "node:assert/strict";
import test from "node:test";
import { semanticLaneSide } from "./review-gutter.ts";

test("unified review renders one head lane for context and inserted rows", () => {
  assert.equal(semanticLaneSide("unified", "normal", "old"), null);
  assert.equal(semanticLaneSide("unified", "normal", "new"), "new");
  assert.equal(semanticLaneSide("unified", "insert", "old"), null);
  assert.equal(semanticLaneSide("unified", "insert", "new"), "new");
});

test("unified review puts deleted-row semantics in its one visible lane", () => {
  assert.equal(semanticLaneSide("unified", "delete", "old"), null);
  assert.equal(semanticLaneSide("unified", "delete", "new"), "old");
});

test("split review retains the native semantic side for both panes", () => {
  assert.equal(semanticLaneSide("split", "normal", "old"), "old");
  assert.equal(semanticLaneSide("split", "normal", "new"), "new");
});

test("split review suppresses the base lane only when a context annotation is identical", () => {
  assert.equal(semanticLaneSide("split", "normal", "old", true), null);
  assert.equal(semanticLaneSide("split", "normal", "new", true), "new");
  assert.equal(semanticLaneSide("split", "delete", "old", true), "old");
  assert.equal(semanticLaneSide("split", "insert", "new", true), "new");
});
