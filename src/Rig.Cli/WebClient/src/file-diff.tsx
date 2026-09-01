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
  viaDispatchOnly: boolean;
  looped: boolean;
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

type FileHazard = {
  type: string;
  confidence: string;
  subtype: string;
  key: string;
  enclosing: string;
  line: number;
  detail: string;
};

type FileAmplification = {
  type: string;
  confidence: string;
  subtype: string;
  key: string;
  enclosing: string;
  line: number;
  iteration: string;
  provider: string;
  operation: string;
};

type FileAnchor = {
  line: number;
  caller: string;
  iterationKind: string;
  witnessProvider: string;
  witnessOperation: string;
  witnessResource: string;
  witnessDepth: number;
  confidence: string;
};

type FileFindings = {
  hazards: FileHazard[];
  amplifications: FileAmplification[];
  anchors: FileAnchor[];
  crossMethodAvailable: boolean;
};

type Revision = {
  store: string;
  commit: string;
  content: string;
  effects: FileEffects;
  // Loaded independently after the patch, matching the Windows file-lens API boundary. `undefined` means
  // loading; `null` means the slower findings derivation was unavailable and effect badges still remain valid.
  findings?: FileFindings | null;
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
  ignoreWhitespace?: boolean;
  onIgnoreWhitespaceChange?: (value: boolean) => void;
};

type Expanded = {
  key: string;
  side: "old" | "new";
  line: number;
};

type LineInsight = {
  sites: FileEffectSite[];
  effects: FileEffect[];
  hazards: FileHazard[];
  amplifications: FileAmplification[];
  anchors: FileAnchor[];
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

function badgeText(effect: FileEffect): string {
  const distance = effect.nearestDepth === 0 ? "!" : `:${effect.nearestDepth}`;
  return `${effect.family}${distance}${effect.looped ? "*" : ""}${effect.viaDispatchOnly ? "?" : ""}`;
}

function effectTitle(effect: FileEffect): string {
  return [
    `${badgeText(effect)} — ${effect.nearestDepth === 0 ? "the effect is in this call's body" : `nearest is ${effect.nearestDepth} calls below`}`,
    effect.viaDispatchOnly
      ? "BASIS: virtual/interface dispatch only — a lead, not a proven call"
      : "BASIS: a real call edge",
    effect.looped ? "AMPLIFIED: runs once per enclosing iteration" : "",
  ]
    .filter(Boolean)
    .join("\n");
}

function changeLine(change: ChangeData, side: "old" | "new"): number | null {
  if (side === "old") {
    if (change.type === "insert") return null;
    return change.type === "delete" ? change.lineNumber : change.oldLineNumber;
  }

  if (change.type === "delete") return null;
  return change.type === "insert" ? change.lineNumber : change.newLineNumber;
}

function byLine(revision: Revision): Map<number, LineInsight> {
  const result = new Map<number, LineInsight>();
  const row = (line: number): LineInsight => {
    let current = result.get(line);
    if (!current) {
      current = { sites: [], effects: [], hazards: [], amplifications: [], anchors: [] };
      result.set(line, current);
    }
    return current;
  };

  for (const site of revision.effects.sites) {
    const insight = row(site.line);
    insight.sites.push(site);
    for (const effect of site.effects) {
      const existing = insight.effects.find((candidate) => candidate.family === effect.family);
      if (!existing) {
        insight.effects.push({ ...effect });
        continue;
      }
      const strongerBasis = existing.viaDispatchOnly && !effect.viaDispatchOnly;
      const nearer = existing.viaDispatchOnly === effect.viaDispatchOnly && effect.nearestDepth < existing.nearestDepth;
      const looped = existing.looped || effect.looped;
      if (strongerBasis || nearer) Object.assign(existing, effect, { looped });
      else existing.looped = looped;
    }
  }
  for (const finding of revision.findings?.hazards || []) row(finding.line).hazards.push(finding);
  for (const finding of revision.findings?.amplifications || []) row(finding.line).amplifications.push(finding);
  for (const finding of revision.findings?.anchors || []) row(finding.line).anchors.push(finding);
  for (const insight of result.values()) {
    insight.effects.sort(
      (a, b) => Number(a.viaDispatchOnly) - Number(b.viaDispatchOnly) || a.nearestDepth - b.nearestDepth || a.family.localeCompare(b.family),
    );
  }
  return result;
}

function EffectBadge({ effect }: { effect: FileEffect }) {
  return (
    <span
      className={`rig-diff-effect-mark ${effect.nearestDepth === 0 ? "here" : "below"} ${effect.viaDispatchOnly ? "guess" : ""}`}
      title={effectTitle(effect)}
    >
      {effect.looped ? <span className="rig-diff-loop">⟳</span> : null}
      <span>{effect.nearestDepth === 0 ? "●" : "○"}</span>
      <span>{effect.family}</span>
      {effect.nearestDepth > 0 ? <small>{effect.nearestDepth}</small> : null}
      {effect.viaDispatchOnly ? <small>?</small> : null}
    </span>
  );
}

function EffectMarks({ insight }: { insight: LineInsight }) {
  const shownEffects = insight.effects.slice(0, 2);
  const hidden = insight.effects.length - shownEffects.length;
  const count = insight.effects.length + insight.hazards.length + insight.amplifications.length + insight.anchors.length;
  return (
    <span className="rig-diff-marks" aria-label={`${count} semantic annotations`}>
      {insight.hazards.length ? <span className="rig-diff-finding hazard" title={`${insight.hazards.length} tier-1 hazard(s)`}>⚠</span> : null}
      {insight.anchors.length ? <span className="rig-diff-finding anchor" title={`${insight.anchors.length} cross-method amplification anchor(s)`}>⟳↓</span> : null}
      {insight.amplifications.length ? <span className="rig-diff-finding amplification" title={`${insight.amplifications.length} looped effect(s)`}>⟳</span> : null}
      {shownEffects.map((effect) => <EffectBadge effect={effect} key={effect.family} />)}
      {hidden > 0 ? <span className="rig-diff-more" title={`${hidden} more effect families`}>+{hidden}</span> : null}
    </span>
  );
}

function EffectWidget({ expanded, insight, callbacks }: { expanded: Expanded; insight: LineInsight; callbacks: FileDiffCallbacks }) {
  return (
    <div className="rig-diff-widget">
      <strong>{expanded.side === "old" ? "base" : "head"}:{expanded.line}</strong>
      <div className="rig-diff-findings">
        {insight.hazards.map((finding, index) => (
          <span className={`rig-diff-finding-row hazard confidence-${finding.confidence}`} key={`hazard:${finding.type}:${index}`}>
            ⚠ tier 1 · {finding.type} · {finding.confidence} · {finding.subtype}
          </span>
        ))}
        {insight.amplifications.map((finding, index) => (
          <span className="rig-diff-finding-row amplification" key={`amplification:${finding.provider}:${index}`}>
            ⟳ tier 2 · {finding.provider}:{finding.operation} · {finding.iteration}
          </span>
        ))}
        {insight.anchors.map((finding, index) => (
          <span className={`rig-diff-finding-row anchor confidence-${finding.confidence}`} key={`anchor:${finding.witnessProvider}:${index}`}>
            ⟳↓ tier 3 · {finding.witnessProvider}:{finding.witnessOperation} · depth {finding.witnessDepth} · {finding.confidence}
          </span>
        ))}
      </div>
      {insight.sites.map((site, index) => {
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
            {site.effects.map((effect) => <EffectBadge effect={effect} key={`${effect.family}:${effect.nearestDepth}`} />)}
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
  const oldLines = useMemo(() => byLine(model.base), [model.base]);
  const newLines = useMemo(() => byLine(model.head), [model.head]);
  const file = files[0];
  const tokens = useMemo(
    () =>
      file
        ? tokenize(file.hunks, {
            highlight: true,
            refractor: syntaxHighlighter,
            language: "csharp",
            enhancers: [markEdits(file.hunks)],
          })
        : null,
    [file],
  );
  const widgets = expanded
    ? {
        [expanded.key]: (
          <EffectWidget
            expanded={expanded}
            insight={(expanded.side === "old" ? oldLines : newLines).get(expanded.line)!}
            callbacks={callbacks}
          />
        ),
      }
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
          <span className="rig-diff-tier-status">
            {model.base.findings === undefined || model.head.findings === undefined
              ? "tiers 1–3 loading…"
              : model.base.findings === null || model.head.findings === null
                ? "tiers 1–3 partially unavailable"
                : `${model.base.findings.hazards.length + model.base.findings.amplifications.length + model.base.findings.anchors.length}/${model.head.findings.hazards.length + model.head.findings.amplifications.length + model.head.findings.anchors.length} findings`}
          </span>
          <details className="rig-diff-settings">
            <summary aria-label="Diff settings" title="Diff settings">⚙</summary>
            <div className="rig-diff-settings-menu">
              <fieldset>
                <legend>Diff display</legend>
                <label>
                  <input
                    type="radio"
                    name="rig-diff-display"
                    value="unified"
                    checked={viewType === "unified"}
                    onChange={() => setViewType("unified")}
                  />
                  Unified
                </label>
                <label>
                  <input
                    type="radio"
                    name="rig-diff-display"
                    value="split"
                    checked={viewType === "split"}
                    onChange={() => setViewType("split")}
                  />
                  Split
                </label>
              </fieldset>
              <label className="rig-diff-settings-check">
                <input
                  type="checkbox"
                  checked={callbacks.ignoreWhitespace || false}
                  onChange={(event) => callbacks.onIgnoreWhitespaceChange?.(event.target.checked)}
                />
                Hide whitespace changes
              </label>
            </div>
          </details>
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
            const insight = line == null ? undefined : (side === "old" ? oldLines : newLines).get(line);
            const key = getChangeKey(change);
            return wrapInAnchor(
              <span className="rig-diff-gutter">
                {insight ? (
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
                          : { key, side, line: line! },
                      );
                    }}
                  >
                    <EffectMarks insight={insight} />
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
