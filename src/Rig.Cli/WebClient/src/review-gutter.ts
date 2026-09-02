import type { DiffType, ViewType } from "react-diff-view";
import type { EffectChangeKind } from "./effect-delta.ts";

export type ReviewGutterSide = "old" | "new";
export type ReviewChangeType = "normal" | "insert" | "delete";
export type ReviewLaneHeaderCell = { kind: "gutter" | "code"; side: ReviewGutterSide | null; lane: boolean };

// A method aggregate's delta is two-sided, but a gutter lane is revision-native: this decides whether a
// slot takes the `moved` delta styling. A method lane states that method's REACH, not that its reach
// changed, so an aggregate that did not change must read "same" on BOTH sides and render as an ordinary
// mark — otherwise every method declaration in a file would go teal and the delta signal would be lost.
export function changeForSide(kind: EffectChangeKind | undefined, side: ReviewGutterSide): EffectChangeKind {
  if (kind === "changed") return "changed";
  if (side === "old" && kind === "removed") return "removed";
  if (side === "new" && kind === "added") return "added";
  return "same";
}

// react-diff-view renders two gutters even in unified mode. A semantic lane in both gutters duplicates the
// same annotation on context rows and consumes 320px before code starts. Unified review owns one lane: the
// head side for context/inserts, and the base side for deletions. Split review keeps one lane per pane.
export function semanticLaneSide(
  viewType: ViewType,
  changeType: ReviewChangeType,
  gutterSide: ReviewGutterSide,
  duplicateAcrossSides = false,
): ReviewGutterSide | null {
  if (viewType === "split") {
    if (changeType === "normal" && duplicateAcrossSides && gutterSide === "old") return null;
    return gutterSide;
  }
  if (gutterSide === "old") return null;
  return changeType === "delete" ? "old" : "new";
}

// The lane key is a row INSIDE the diff table, so the browser's own column layout aligns each glyph group
// with the lane it labels. That only holds while the header row mirrors react-diff-view's column layout
// exactly: unified renders two gutters then one code column, split renders a gutter/code pair per side, and
// an added or deleted file ("monotonous") collapses split to the single pair its changes live on.
// `laneSides` is the set of gutter columns that actually rendered a lane, so a suppressed base lane never
// gets an orphan header group.
export function laneHeaderCells(
  viewType: ViewType,
  diffType: DiffType,
  laneSides: ReadonlySet<ReviewGutterSide>,
): ReviewLaneHeaderCell[] {
  const gutter = (side: ReviewGutterSide): ReviewLaneHeaderCell => ({ kind: "gutter", side, lane: laneSides.has(side) });
  const code = (): ReviewLaneHeaderCell => ({ kind: "code", side: null, lane: false });
  if (viewType === "unified") return [gutter("old"), gutter("new"), code()];
  if (diffType === "add" || diffType === "delete") return [gutter(diffType === "delete" ? "old" : "new"), code()];
  return [gutter("old"), code(), gutter("new"), code()];
}
