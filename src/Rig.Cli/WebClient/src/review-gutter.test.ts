import assert from "node:assert/strict";
import test from "node:test";
import { changeForSide, laneHeaderCells, semanticLaneSide } from "./review-gutter.ts";
import { buildMethodDeltaIndex } from "./effect-delta.ts";
import { canSuppressBaseGutter } from "./review-presentation.ts";

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

// The gutter renderer composes these two rules: canSuppressBaseGutter decides semanticLaneSide's
// duplicateAcrossSides argument, and its counts are of CHANGED aggregates. A method declaration row
// carries a method aggregate but no per-line insight, and neither rule may consult insight.
const laneSideForRow = (
  viewType: "unified" | "split",
  changeType: "normal" | "insert" | "delete",
  gutterSide: "old" | "new",
  row: { identical: boolean; changedBase: number; changedHead: number },
) => semanticLaneSide(viewType, changeType, gutterSide, canSuppressBaseGutter(row.identical, row.changedBase, row.changedHead));

test("an unchanged method aggregate still owns a lane, drawn once", () => {
  // A method lane states reach, not delta, so the row keeps a lane. In split view both panes would draw
  // the identical aggregate, so the base pane is suppressed and the head pane carries it.
  const unchanged = { identical: true, changedBase: 0, changedHead: 0 };
  assert.equal(laneSideForRow("unified", "normal", "new", unchanged), "new");
  assert.equal(laneSideForRow("split", "normal", "new", unchanged), "new");
  assert.equal(laneSideForRow("split", "normal", "old", unchanged), null);
  // Only a context row de-duplicates; a deleted line's base lane is the only place its reach appears.
  assert.equal(laneSideForRow("split", "delete", "old", unchanged), "old");
});

test("a changed method aggregate keeps a lane in both panes", () => {
  // Restating the reason canSuppressBaseGutter refuses here: a removed last effect leaves no head mark,
  // so dropping the base pane would hide the removal.
  const changed = { identical: true, changedBase: 1, changedHead: 1 };
  assert.equal(laneSideForRow("split", "normal", "old", changed), "old");
  assert.equal(laneSideForRow("split", "normal", "new", changed), "new");
  assert.equal(laneSideForRow("unified", "normal", "new", changed), "new");
});

// From two revisions of one method through to the class its slot takes: the pairing is real
// (buildMethodDeltaIndex), so this pins that an unchanged aggregate never reaches the delta styling.
const aggregate = (line: number, name: string, effects: Array<{ family: string; nearestDepth: number; viaDispatchOnly: boolean; looped: boolean }>) =>
  ({ id: `M:Search.${name}`, name, signature: `Search.${name}`, line, endLine: line + 2, effects });
const slotDelta = (
  base: ReturnType<typeof aggregate>,
  head: ReturnType<typeof aggregate>,
  family: string,
) => {
  const index = buildMethodDeltaIndex([base], [head], true);
  const comparison = index.headById.get(head.id) || index.baseById.get(base.id)!;
  return {
    kind: comparison.effects.get(family)?.kind,
    old: changeForSide(comparison.effects.get(family)?.kind, "old"),
    new: changeForSide(comparison.effects.get(family)?.kind, "new"),
  };
};

test("an unchanged aggregate is an ordinary mark, and a changed one is still a delta", () => {
  const db = { family: "db", nearestDepth: 6, viaDispatchOnly: false, looped: false };
  const unchanged = slotDelta(aggregate(333, "get_Range", [db]), aggregate(333, "get_Range", [db]), "db");
  assert.deepEqual(unchanged, { kind: "same", old: "same", new: "same" });

  const nearer = slotDelta(aggregate(333, "get_Range", [db]), aggregate(333, "get_Range", [{ ...db, nearestDepth: 3 }]), "db");
  assert.deepEqual(nearer, { kind: "changed", old: "changed", new: "changed" });

  const gained = slotDelta(aggregate(333, "get_Range", []), aggregate(333, "get_Range", [db]), "db");
  assert.deepEqual(gained, { kind: "added", old: "same", new: "added" });

  const lost = slotDelta(aggregate(333, "get_Range", [db]), aggregate(333, "get_Range", []), "db");
  assert.deepEqual(lost, { kind: "removed", old: "removed", new: "same" });
});

const cellShape = (viewType: "unified" | "split", diffType: "add" | "delete" | "modify", sides: Array<"old" | "new">) =>
  laneHeaderCells(viewType, diffType, new Set(sides)).map((cell) => `${cell.kind}${cell.side ? ":" + cell.side : ""}${cell.lane ? "+key" : ""}`);

test("the unified lane key labels the one gutter that carries the lane", () => {
  assert.deepEqual(cellShape("unified", "modify", ["new"]), ["gutter:old", "gutter:new+key", "code"]);
});

test("the split lane key labels each pane's own lane", () => {
  assert.deepEqual(cellShape("split", "modify", ["old", "new"]), ["gutter:old+key", "code", "gutter:new+key", "code"]);
});

test("a base lane suppressed on every row gets no orphan key group", () => {
  assert.deepEqual(cellShape("split", "modify", ["new"]), ["gutter:old", "code", "gutter:new+key", "code"]);
});

test("an added or deleted file has one split column pair, on the side its changes live", () => {
  assert.deepEqual(cellShape("split", "add", ["new"]), ["gutter:new+key", "code"]);
  assert.deepEqual(cellShape("split", "delete", ["old"]), ["gutter:old+key", "code"]);
  assert.deepEqual(cellShape("unified", "add", ["new"]), ["gutter:old", "gutter:new+key", "code"]);
});
