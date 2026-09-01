import { useMemo, useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import {
  Diff,
  Hunk,
  getChangeKey,
  markEdits,
  parseDiff,
  tokenize,
  type ChangeData,
  type ViewType,
} from "react-diff-view";
import { refractor } from "refractor/core";
import csharp from "refractor/csharp";
import "react-diff-view/style/index.css";
import "./file-diff.css";

type FileEffect = {
  family: string;
  nearestDepth: number;
};

type FileEffectSite = {
  enclosingMethodId: string;
  targetMethodId: string;
  line: number;
  effects: FileEffect[];
};

type FileEffectMethod = {
  id: string;
  name: string;
  signature: string;
  line: number;
  endLine: number;
  effects: FileEffect[];
};

type FileEffects = {
  file: string;
  methods: FileEffectMethod[];
  sites: FileEffectSite[];
};

type Revision = {
  store: string;
  commit: string;
  content: string;
  effects: FileEffects;
};

export type FileDiffModel = {
  file: string;
  relativePath: string;
  patch: string;
  contextLines: number;
  base: Revision;
  head: Revision;
};

export type FileDiffCallbacks = {
  onOpenTree?: (symbolId: string) => void;
};

type Expanded = {
  key: string;
  side: "old" | "new";
  line: number;
  sites: FileEffectSite[];
};

const roots = new WeakMap<Element, Root>();
refractor.register(csharp);
// react-diff-view's tokenizer consumes the pre-refractor-4 array shape. Modern refractor returns a HAST
// Root, so keep the secure current package and adapt only its `children` at this narrow boundary.
const syntaxHighlighter = {
  highlight(value: string, language: string) {
    return refractor.highlight(value, language).children;
  },
};

function shortSha(value: string): string {
  return value.slice(0, 12);
}

function shortTarget(value: string): string {
  if (!value) return "external effect";
  const declaration = value.replace(/^[A-Z]:/, "").split("(", 1)[0];
  const tail = declaration.split(/[.:+]/).pop() || declaration;
  return tail.replace(/``\d+$/, "<T>");
}

function familyGlyph(family: string): string {
  const value = family.toLowerCase();
  if (value === "io" || value.includes("file") || value.includes("filesystem")) return "▱";
  if (value.includes("sql") || value.includes("db")) return "▰";
  if (value.includes("http") || value.includes("rpc")) return "↗";
  if (value.includes("cache")) return "◇";
  if (value.includes("message") || value.includes("queue")) return "▷";
  return "◆";
}

function changeLine(change: ChangeData, side: "old" | "new"): number | null {
  if (side === "old") {
    if (change.type === "insert") return null;
    return change.type === "delete" ? change.lineNumber : change.oldLineNumber;
  }

  if (change.type === "delete") return null;
  return change.type === "insert" ? change.lineNumber : change.newLineNumber;
}

function byLine(sites: FileEffectSite[]): Map<number, FileEffectSite[]> {
  const result = new Map<number, FileEffectSite[]>();
  for (const site of sites) {
    const current = result.get(site.line) || [];
    current.push(site);
    result.set(site.line, current);
  }
  return result;
}

function EffectMarks({ sites }: { sites: FileEffectSite[] }) {
  const effects = sites.flatMap((site) => site.effects);
  return (
    <span className="rig-diff-marks" aria-label={`${effects.length} effect annotations`}>
      {effects.map((effect, index) => (
        <span
          className={`rig-diff-mark depth-${Math.min(effect.nearestDepth, 3)}`}
          title={`${effect.family} · ${effect.nearestDepth === 0 ? "direct" : `depth ${effect.nearestDepth}`}`}
          key={`${effect.family}:${effect.nearestDepth}:${index}`}
        >
          {familyGlyph(effect.family)}
        </span>
      ))}
    </span>
  );
}

function EffectWidget({ expanded, callbacks }: { expanded: Expanded; callbacks: FileDiffCallbacks }) {
  return (
    <div className="rig-diff-widget">
      <strong>{expanded.side === "old" ? "base" : "head"}:{expanded.line}</strong>
      {expanded.sites.map((site, index) => {
        const target = site.targetMethodId || site.enclosingMethodId;
        return (
          <button
            key={`${target}:${index}`}
            type="button"
            className="rig-diff-path"
            onClick={() => callbacks.onOpenTree?.(target)}
            disabled={!target}
            title={target || "No symbol identity for this external effect"}
          >
            <span>{shortTarget(site.targetMethodId)}</span>
            {site.effects.map((effect) => (
              <span className="rig-diff-effect" key={`${effect.family}:${effect.nearestDepth}`}>
                {effect.family}{effect.nearestDepth === 0 ? "!" : `:${effect.nearestDepth}`}
              </span>
            ))}
            <span className="rig-diff-open">open tree ↗</span>
          </button>
        );
      })}
    </div>
  );
}

function FileDiffView({ model, callbacks }: { model: FileDiffModel; callbacks: FileDiffCallbacks }) {
  const [viewType, setViewType] = useState<ViewType>("unified");
  const [expanded, setExpanded] = useState<Expanded | null>(null);
  const files = useMemo(() => (model.patch.trim() ? parseDiff(model.patch) : []), [model.patch]);
  const oldSites = useMemo(() => byLine(model.base.effects.sites), [model.base.effects.sites]);
  const newSites = useMemo(() => byLine(model.head.effects.sites), [model.head.effects.sites]);
  const file = files[0];
  const tokens = useMemo(
    () =>
      file
        ? tokenize(file.hunks, {
            highlight: true,
            refractor: syntaxHighlighter,
            language: "csharp",
            oldSource: model.base.content,
            enhancers: [markEdits(file.hunks)],
          })
        : null,
    [file, model.base.content],
  );
  const widgets = expanded
    ? { [expanded.key]: <EffectWidget expanded={expanded} callbacks={callbacks} /> }
    : {};

  return (
    <div className="rig-diff-island">
      <div className="rig-diff-head">
        <div>
          <strong>{model.relativePath}</strong>
          <span>{shortSha(model.base.commit)} → {shortSha(model.head.commit)}</span>
        </div>
        <div className="rig-diff-summary">
          <span>{model.base.effects.sites.length} base marks</span>
          <span>{model.head.effects.sites.length} head marks</span>
          <button type="button" className={viewType === "unified" ? "on" : ""} onClick={() => setViewType("unified")}>
            unified
          </button>
          <button type="button" className={viewType === "split" ? "on" : ""} onClick={() => setViewType("split")}>
            split
          </button>
        </div>
      </div>
      {!file ? (
        <div className="rig-diff-empty">No textual changes in this file.</div>
      ) : (
        <Diff
          viewType={viewType}
          diffType={file.type}
          hunks={file.hunks}
          tokens={tokens}
          widgets={widgets}
          renderGutter={({ change, side, renderDefault, wrapInAnchor }) => {
            const line = changeLine(change, side);
            const sites = line == null ? [] : (side === "old" ? oldSites : newSites).get(line) || [];
            const key = getChangeKey(change);
            return wrapInAnchor(
              <span className="rig-diff-gutter">
                {sites.length > 0 ? (
                  <button
                    type="button"
                    className="rig-diff-mark-button"
                    title="Show effects and open their call trees"
                    onClick={(event) => {
                      event.preventDefault();
                      event.stopPropagation();
                      setExpanded((current) =>
                        current?.key === key && current.side === side
                          ? null
                          : { key, side, line: line!, sites },
                      );
                    }}
                  >
                    <EffectMarks sites={sites} />
                  </button>
                ) : null}
                {renderDefault()}
              </span>,
            );
          }}
        >
          {(hunks) => hunks.map((hunk) => <Hunk key={hunk.content} hunk={hunk} />)}
        </Diff>
      )}
    </div>
  );
}

export function mountFileDiff(element: Element, model: FileDiffModel, callbacks: FileDiffCallbacks = {}): void {
  let root = roots.get(element);
  if (!root) {
    root = createRoot(element);
    roots.set(element, root);
  }
  root.render(<FileDiffView model={model} callbacks={callbacks} />);
}

export function unmountFileDiff(element: Element): void {
  const root = roots.get(element);
  root?.unmount();
  roots.delete(element);
}
