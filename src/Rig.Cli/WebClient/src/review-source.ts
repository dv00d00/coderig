import type { HunkData } from "react-diff-view";

export type ReviewSource = {
  file: string;
  side: "base" | "head";
  store: string;
  commit: string;
  path: string | null;
  language: "csharp" | "text";
  state: "available" | "not-present" | "binary" | "too-large" | "unavailable";
  content: string | null;
  byteLength: number | null;
  reason: string | null;
};

export function reviewSourceIdentity(model: { file: string; base: { store: string; commit: string }; head: { store: string; commit: string } }): string {
  return JSON.stringify([model.file, model.base.store, model.base.commit, model.head.store, model.head.commit]);
}

// Project exact source directly into normal lines, never through a made-up patch. A terminal newline
// terminates the last line; it does not create a phantom extra source coordinate. CRLF is display-only
// normalized here; the endpoint's original content is retained unchanged.
export function sourceHunk(content: string): HunkData | null {
  if (!content.length) return null;
  const lines = content.split("\n");
  if (lines.at(-1) === "") lines.pop();
  return {
    content: "",
    oldStart: 1,
    newStart: 1,
    oldLines: lines.length,
    newLines: lines.length,
    changes: lines.map((text, index) => ({
      type: "normal" as const,
      isNormal: true,
      oldLineNumber: index + 1,
      newLineNumber: index + 1,
      content: text.endsWith("\r") && (index < lines.length - 1 || content.endsWith("\n")) ? text.slice(0, -1) : text,
    })),
  };
}

export function canHighlightSource(content: string, lineCount: number): boolean {
  return content.length <= 200_000 && lineCount <= 5000;
}

export function matchesReviewSource(source: ReviewSource, model: { file: string; base: { store: string; commit: string }; head: { store: string; commit: string } }, side: "base" | "head"): boolean {
  return source.file === model.file && source.side === side && source.store === model[side].store && source.commit === model[side].commit;
}
