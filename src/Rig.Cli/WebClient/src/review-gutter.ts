import type { ViewType } from "react-diff-view";

export type ReviewGutterSide = "old" | "new";
export type ReviewChangeType = "normal" | "insert" | "delete";

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
