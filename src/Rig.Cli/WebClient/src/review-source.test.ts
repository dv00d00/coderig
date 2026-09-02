import assert from "node:assert/strict";
import test from "node:test";
import { canHighlightSource, matchesReviewSource, reviewSourceIdentity, sourceHunk, type ReviewSource } from "./review-source.ts";

test("source coordinates preserve blank lines, CRLF and final newline without patch interpretation", () => {
  const input = "@@ not a patch\r\n+source\r\n\r\n-last\r\n";
  const hunk = sourceHunk(input)!;
  assert.deepEqual(hunk.changes.map(line => line.content), ["@@ not a patch", "+source", "", "-last"]);
  assert.deepEqual(hunk.changes.map(line => line.type === "normal" && [line.oldLineNumber, line.newLineNumber]), [[1, 1], [2, 2], [3, 3], [4, 4]]);
  assert.equal(input, "@@ not a patch\r\n+source\r\n\r\n-last\r\n");
  assert.equal(sourceHunk("last")!.newLines, 1);
  assert.equal(sourceHunk("last\n")!.newLines, 1);
  assert.equal(sourceHunk("\n")!.newLines, 1);
  assert.equal(sourceHunk(""), null);
  assert.equal(sourceHunk("unterminated\r")!.changes[0].content, "unterminated\r");
});

test("source request identity survives findings updates but not navigation or revision changes", () => {
  const model = { file: "a.cs", base: { store: "base", commit: "aaa" }, head: { store: "head", commit: "bbb" } };
  assert.equal(reviewSourceIdentity(model), reviewSourceIdentity({ ...model, head: { ...model.head } }));
  assert.notEqual(reviewSourceIdentity(model), reviewSourceIdentity({ ...model, file: "b.cs" }));
  const source: ReviewSource = { file: "a.cs", side: "base", store: "base", commit: "aaa", path: "old.cs", state: "available", content: "", byteLength: 0, language: "csharp", reason: null };
  assert.equal(matchesReviewSource(source, model, "base"), true);
  assert.equal(matchesReviewSource(source, model, "head"), false);
  assert.equal(matchesReviewSource(source, { ...model, file: "b.cs" }, "base"), false);
});

test("large source keeps every line but skips expensive highlighting", () => {
  const content = "x\n".repeat(6000);
  assert.equal(sourceHunk(content)!.changes.length, 6000);
  assert.equal(canHighlightSource(content, 6000), false);
  assert.equal(canHighlightSource("x".repeat(200001), 1), false);
  assert.equal(canHighlightSource("class C {}\n", 1), true);
});
