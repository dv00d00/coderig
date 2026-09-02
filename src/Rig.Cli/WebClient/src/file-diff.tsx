import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
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
import {
  buildMethodDeltaIndex,
  changedEffects,
  effectChangeAtSite,
  type EffectChangeKind,
  type MethodComparison,
  type MethodDeltaIndex,
} from "./effect-delta.ts";
import { semanticLaneSide } from "./review-gutter.ts";
import {
  amplificationLabel, anchorLabel, effectFamilyLabel, findingsStatus, inlineEffectLabel, disclosureLabel,
  readReviewEffectMode, reviewEffectModes, saveReviewEffectMode, type ReviewEffectMode,
  sameVisibleAnnotations,
  canSuppressBaseGutter,
} from "./review-presentation.ts";

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
  semanticState: "available" | "not-indexed" | "not-present";
  path: string | null;
  file: string | null;
  content: string;
  effects: FileEffects | null;
  // Loaded independently after the patch, matching the Windows file-lens API boundary. `undefined` means
  // loading; `null` means the slower findings derivation was unavailable and effect badges still remain valid.
  findings?: FileFindings | null;
};

export type FileDiffModel = {
  file: string;
  relativePath: string;
  status: string;
  oldPath: string | null;
  newPath: string | null;
  language: "csharp" | "text";
  patch: string;
  contextLines: number;
  base: Revision;
  head: Revision;
};

export type FileDiffCallbacks = {
  onOpenTree?: (symbolId: string, side: "old" | "new") => void;
  focusLine?: { side: "old" | "new"; line: number } | null;
  ignoreWhitespace?: boolean;
  onIgnoreWhitespaceChange?: (value: boolean) => void;
  viewed?: boolean;
  onViewedChange?: (value: boolean) => void;
  focusMode?: boolean;
  onFocusModeChange?: (value: boolean) => void;
  filesHidden?: boolean;
  onFilesHiddenChange?: (value: boolean) => void;
};

type Expanded = {
  key: string;
  side: "old" | "new";
  line: number;
  context?: object;
};

type LineInsight = {
  sites: FileEffectSite[];
  effects: FileEffect[];
  hazards: FileHazard[];
  amplifications: FileAmplification[];
  anchors: FileAnchor[];
};

type SemanticRow = { side: "old" | "new"; line: number; insight?: LineInsight; headers: MethodComparison[] };
type ProjectedChange = { change: ChangeData; old?: SemanticRow; new?: SemanticRow; identical: boolean };

const effectFamilies = [
  { key: "db", mark: "D", label: "database" },
  { key: "cache", mark: "C", label: "cache" },
  { key: "blob", mark: "B", label: "blob/object store" },
  { key: "bus", mark: "Q", label: "message bus" },
  { key: "echo", mark: "E", label: "echo/event channel" },
  { key: "io", mark: "I", label: "file system / I/O" },
  { key: "rpc", mark: "R", label: "remote call" },
  { key: "search", mark: "S", label: "search" },
] as const;

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

function pathParts(value: string): { name: string; parent: string } {
  const normalized = value.replaceAll("\\", "/");
  const slash = normalized.lastIndexOf("/");
  return {
    name: slash < 0 ? normalized : normalized.slice(slash + 1),
    parent: slash < 0 ? "" : normalized.slice(0, slash),
  };
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
    effect.looped ? "ITERATION: an effectful edge occurs inside an iteration; runtime count is not established" : "",
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

function mergeEffect(target: FileEffect[], effect: FileEffect): void {
  const existing = target.find((candidate) => candidate.family === effect.family);
  if (!existing) {
    target.push({ ...effect });
    return;
  }
  const strongerBasis = existing.viaDispatchOnly && !effect.viaDispatchOnly;
  const nearer = existing.viaDispatchOnly === effect.viaDispatchOnly && effect.nearestDepth < existing.nearestDepth;
  const looped = existing.looped || effect.looped;
  if (strongerBasis || nearer) Object.assign(existing, effect, { looped });
  else existing.looped = looped;
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

  for (const site of revision.effects?.sites || []) {
    const insight = row(site.line);
    insight.sites.push(site);
    for (const effect of site.effects) {
      mergeEffect(insight.effects, effect);
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

function sameVisibleLineInsight(base: LineInsight | undefined, head: LineInsight | undefined): boolean {
  return sameVisibleAnnotations(base, head);
}

function changeForSide(kind: EffectChangeKind | undefined, side: "old" | "new"): EffectChangeKind {
  if (kind === "changed") return "changed";
  if (side === "old" && kind === "removed") return "removed";
  if (side === "new" && kind === "added") return "added";
  return "same";
}

function lineEffectChange(
  effect: FileEffect,
  insight: LineInsight | undefined,
  headers: MethodComparison[],
  side: "old" | "new",
  deltas: MethodDeltaIndex,
): EffectChangeKind {
  const headerKinds = headers.map((comparison) => changeForSide(comparison.effects.get(effect.family)?.kind, side));
  const siteKinds = (insight?.sites || [])
    .map((site) => (side === "old" ? deltas.baseById : deltas.headById).get(site.enclosingMethodId))
    .filter((comparison): comparison is MethodComparison => !!comparison)
    .map((comparison) => effectChangeAtSite(comparison.effects.get(effect.family), side, effect));
  const kinds = [...headerKinds, ...siteKinds];
  if (kinds.includes("changed")) return "changed";
  if (kinds.includes("added")) return "added";
  if (kinds.includes("removed")) return "removed";
  return "same";
}

function methodChangeTitle(headers: MethodComparison[], side: "old" | "new"): string {
  const rows = headers.flatMap((comparison) => changedEffects(comparison).map((change) => {
    const state = side === "old" ? change.base : change.head;
    const family = effectFamilyLabel(state?.family || change.base?.family || change.head?.family || "effect");
    if (change.kind === "added") return `+${family}`;
    if (change.kind === "removed") return `−${family}`;
    const details = [
      change.base?.nearestDepth !== change.head?.nearestDepth ? "distance" : "",
      change.base?.looped !== change.head?.looped ? "repetition" : "",
      change.base?.viaDispatchOnly !== change.head?.viaDispatchOnly ? "dispatch basis" : "",
    ].filter(Boolean).join(", ");
    return `△${family}${details ? ` (${details})` : ""}`;
  }));
  return rows.length ? `Method reach changed: ${rows.join(" · ")}` : "";
}

function EffectLane({
  insight,
  headers,
  side,
  deltas,
}: {
  insight?: LineInsight;
  headers: MethodComparison[];
  side: "old" | "new";
  deltas: MethodDeltaIndex;
}) {
  const effects: FileEffect[] = [];
  for (const effect of insight?.effects || []) mergeEffect(effects, effect);
  for (const comparison of headers) {
    const method = side === "old" ? comparison.base : comparison.head;
    for (const effect of method?.effects || []) mergeEffect(effects, effect);
  }
  const byFamily = new Map(effects.map((effect) => [effect.family, effect]));
  const count = effects.length
    + (insight?.hazards.length || 0)
    + (insight?.amplifications.length || 0)
    + (insight?.anchors.length || 0);
  return (
    <span className="rig-diff-marks" aria-label={`${count} semantic annotations`}>
      <span className="rig-diff-finding-stack">
        {insight?.hazards.length ? <span className="rig-diff-finding hazard" title={`${insight.hazards.length} tier-1 hazard(s)`}>⚠</span> : null}
        {insight?.anchors.length ? <span className="rig-diff-finding anchor" title={`${insight.anchors.length} cross-method amplification anchor(s)`}>↓</span> : null}
        {insight?.amplifications.length ? <span className="rig-diff-finding amplification" title={`${insight.amplifications.length} looped effect(s)`}>⟳</span> : null}
      </span>
      <span className="rig-diff-lane" aria-label="effect reach lane">
        {effectFamilies.map((family) => {
          const effect = byFamily.get(family.key);
          const change = effect ? lineEffectChange(effect, insight, headers, side, deltas) : "same";
          const title = effect
            ? `${family.label}: ${effectTitle(effect)}${change === "same" ? "" : `\nDELTA: ${change} at method grain`}`
            : family.label;
          return (
            <span
              className={[
                "rig-diff-slot",
                effect ? "on" : "off",
                effect?.nearestDepth === 0 ? "here" : "below",
                effect?.viaDispatchOnly ? "uncertain" : "",
                effect?.looped ? "amp" : "",
                change !== "same" ? `moved ${change}` : "",
              ].filter(Boolean).join(" ")}
              data-family={family.key}
              key={family.key}
              title={title}
            />
          );
        })}
      </span>
    </span>
  );
}

function EffectWidget({ expanded, insight, headers = [], callbacks, deltas }: { expanded: Expanded; insight?: LineInsight; headers?: MethodComparison[]; callbacks: FileDiffCallbacks; deltas: MethodDeltaIndex }) {
  return (
    <div className="rig-diff-widget" data-rig-side={expanded.side} data-rig-line={expanded.line}>
      <strong>{expanded.side === "old" ? "base" : "head"}:{expanded.line}</strong>
      {headers.map((comparison, index) => <span className="rig-diff-method-summary" key={index}>
        {(expanded.side === "old" ? comparison.base : comparison.head)?.name}: {methodChangeTitle([comparison], expanded.side)}
      </span>)}
      <div className="rig-diff-findings">
        {insight?.hazards.map((finding, index) => (
          <span className={`rig-diff-finding-row hazard confidence-${finding.confidence}`} key={`hazard:${finding.type}:${index}`}>
            <strong>{finding.type.replaceAll("_", " ")}</strong> · {finding.confidence} · {finding.subtype}
            {finding.detail ? <span className="rig-diff-finding-detail">{finding.detail}</span> : null}
          </span>
        ))}
        {insight?.amplifications.map((finding, index) => (
          <span className="rig-diff-finding-row amplification" key={`amplification:${finding.provider}:${index}`}>
            {amplificationLabel(finding)}
            <span className="rig-diff-finding-detail">Iteration: {finding.iteration} · {finding.confidence} confidence</span>
          </span>
        ))}
        {insight?.anchors.map((finding, index) => (
          <span className={`rig-diff-finding-row anchor confidence-${finding.confidence}`} key={`anchor:${finding.witnessProvider}:${index}`}>
            {anchorLabel(finding)}
            <span className="rig-diff-finding-detail">{finding.caller} · {finding.iterationKind} · {finding.confidence} confidence{finding.witnessResource ? ` · ${finding.witnessResource}` : ""}</span>
          </span>
        ))}
      </div>
      {insight && (insight.anchors.length > 0 || insight.amplifications.length > 0)
        ? <span className="rig-diff-candidate-note">Static iteration candidate — not proof of runtime N+1 or a query count.</span>
        : null}
      {insight?.sites.map((site, index) => {
        const target = site.targetMethodId || site.enclosingMethodId;
        return (
          <button
            key={`${target}:${index}`}
            type="button"
            className="rig-diff-path"
            onClick={() => callbacks.onOpenTree?.(target, expanded.side)}
            disabled={!target || !callbacks.onOpenTree}
            title={target || "No symbol identity for this external effect"}
          >
            <span>{shortTarget(site.targetMethodId)}</span>
            {site.effects.map((effect, effectIndex) => {
              const comparison = (expanded.side === "old" ? deltas.baseById : deltas.headById).get(site.enclosingMethodId);
              const delta = effectChangeAtSite(comparison?.effects.get(effect.family), expanded.side, effect);
              return <span key={`${effect.family}:${effectIndex}`} className={delta === "same" ? "rig-diff-effect-detail" : `rig-diff-effect-detail rig-diff-inline-delta ${delta}`} title={`${effectTitle(effect)}${delta === "same" ? "" : `\n${delta} at method grain`}`}>
                {delta === "same" ? "" : delta === "added" ? "+ " : delta === "removed" ? "− " : "△ "}{inlineEffectLabel(effect)}
              </span>;
            })}
            <span className="rig-diff-open">open tree ↗</span>
          </button>
        );
      })}
    </div>
  );
}

function FileDiffView({ model, callbacks }: { model: FileDiffModel; callbacks: FileDiffCallbacks }) {
  const rootRef = useRef<HTMLDivElement>(null);
  const [viewType, setViewType] = useState<ViewType>("unified");
  const [wrapLines, setWrapLines] = useState(true);
  const [effectMode, setEffectMode] = useState<ReviewEffectMode>(() => readReviewEffectMode());
  const [expanded, setExpanded] = useState<Expanded | null>(null);
  const expansionContext = useMemo(() => ({}), [model.file, model.base.store, model.head.store, model.base.commit, model.head.commit, model.patch]);
  const activeExpanded = expanded?.context === expansionContext ? expanded : null;
  const [focusFound, setFocusFound] = useState<boolean | null>(null);
  const files = useMemo(() => (model.patch.trim() ? parseDiff(model.patch) : []), [model.patch]);
  const oldLines = useMemo(() => byLine(model.base), [model.base]);
  const newLines = useMemo(() => byLine(model.head), [model.head]);
  const file = files[0];
  const path = useMemo(() => pathParts(model.relativePath || model.file), [model.relativePath, model.file]);
  const patchCounts = useMemo(() => {
    if (!file) return { additions: 0, deletions: 0 };
    return file.hunks.reduce(
      (counts, hunk) => {
        for (const change of hunk.changes) {
          if (change.type === "insert") counts.additions += 1;
          else if (change.type === "delete") counts.deletions += 1;
        }
        return counts;
      },
      { additions: 0, deletions: 0 },
    );
  }, [file]);
  const baseAvailable = model.base.semanticState === "available" && model.base.effects !== null;
  const headAvailable = model.head.semanticState === "available" && model.head.effects !== null;
  const methodDeltas = useMemo(
    () => buildMethodDeltaIndex(
      model.base.effects?.methods || [],
      model.head.effects?.methods || [],
      baseAvailable && headAvailable,
    ),
    [model.base.effects, model.head.effects, baseAvailable, headAvailable],
  );
  const baseEffectSites = model.base.effects?.sites.length || 0;
  const headEffectSites = model.head.effects?.sites.length || 0;
  const baseFindingsStatus = findingsStatus(model.base);
  const headFindingsStatus = findingsStatus(model.head);
  const effectDelta = headEffectSites - baseEffectSites;
  const semanticSummary = baseAvailable && headAvailable
    ? `effect sites ${effectDelta > 0 ? "+" : ""}${effectDelta}`
    : baseAvailable
      ? "base-only semantics"
      : headAvailable
        ? "head-only semantics"
        : model.language === "text"
          ? "text-only · semantics unavailable"
          : "semantics unavailable";
  const tokens = useMemo(
    () =>
      file && model.language === "csharp"
        ? tokenize(file.hunks, {
            highlight: true,
            refractor: syntaxHighlighter,
            language: "csharp",
            enhancers: [markEdits(file.hunks)],
          })
        : null,
    [file, model.language],
  );
  const projection = useMemo(() => {
    const rows = new Map<string, ProjectedChange>();
    const headersAt = (source: ReadonlyMap<number, MethodComparison[]>) => new Map(
      [...source].map(([line, comparisons]) => [line, comparisons.filter((comparison) => changedEffects(comparison).length > 0)]),
    );
    const oldHeaders = headersAt(methodDeltas.baseByLine);
    const newHeaders = headersAt(methodDeltas.headByLine);
    for (const hunk of file?.hunks || []) for (const change of hunk.changes) {
      const row = (side: "old" | "new"): SemanticRow | undefined => {
        const line = changeLine(change, side);
        if (line == null) return undefined;
        const insight = (side === "old" ? oldLines : newLines).get(line);
        const headers = (side === "old" ? oldHeaders : newHeaders).get(line) || [];
        return insight || headers.length ? { side, line, insight, headers } : undefined;
      };
      const old = row("old");
      const next = row("new");
      const identical = sameVisibleLineInsight(old?.insight, next?.insight)
        && (old?.headers.length || 0) === (next?.headers.length || 0)
        && (old?.headers || []).every((header, index) => header === next?.headers[index]);
      rows.set(getChangeKey(change), { change, old, new: next, identical });
    }
    return rows;
  }, [file, oldLines, newLines, methodDeltas]);
  const toggleExpanded = (value: Expanded) => setExpanded((current) =>
    current?.context === expansionContext && current.key === value.key && current.side === value.side
      ? null : { ...value, context: expansionContext },
  );
  const widgets = useMemo(() => {
    const result: Record<string, ReactNode> = {};
    if (effectMode === "off" || !activeExpanded) return result;
    const item = projection.get(activeExpanded.key);
    const row = item?.[activeExpanded.side];
    if (!item || !row) return result;
    const widget = <EffectWidget expanded={activeExpanded} insight={row.insight} headers={row.headers} callbacks={callbacks} deltas={methodDeltas} />;
    // Normal changes share a single library widget key (colspan=4). Place the sole expansion in its
    // native pane; never allocate duplicate details or any extra table row before the reader asks for it.
    result[activeExpanded.key] = viewType === "split" && item.change.type === "normal"
      ? <div className="rig-diff-inline-pair"><div>{activeExpanded.side === "old" ? widget : null}</div><div>{activeExpanded.side === "new" ? widget : null}</div></div>
      : <div className="rig-diff-inline-single">{widget}</div>;
    return result;
  }, [projection, effectMode, activeExpanded, viewType, callbacks, expansionContext, methodDeltas]);

  useEffect(() => setExpanded(null), [expansionContext]);

  useEffect(() => {
    const focus = callbacks.focusLine;
    if (!focus) {
      setFocusFound(null);
      return;
    }

    const frame = requestAnimationFrame(() => {
      const gutter = rootRef.current?.querySelector(
        `.rig-diff-gutter[data-rig-side="${focus.side}"][data-rig-line="${focus.line}"]`,
      );
      setFocusFound(!!gutter);
      gutter?.closest("tr")?.scrollIntoView({ block: "center" });
    });
    return () => cancelAnimationFrame(frame);
  }, [callbacks.focusLine?.line, callbacks.focusLine?.side, model.patch, viewType]);

  return (
    <div className={`rig-diff-island view-${viewType} effects-${effectMode} ${wrapLines ? "wrap-lines" : "no-wrap"}`} ref={rootRef}>
      <div className="rig-diff-head">
        <div className="rig-diff-identity" title={model.relativePath}>
          <div className="rig-diff-file-line">
            <span className={`rig-diff-status status-${model.status.toLowerCase()}`} title={`Git status ${model.status}`}>
              {model.status}
            </span>
            <strong>{path.name}</strong>
            <span className="rig-diff-patch-counts" aria-label={`${patchCounts.additions} additions, ${patchCounts.deletions} deletions`}>
              <b>+{patchCounts.additions}</b>
              <i>−{patchCounts.deletions}</i>
            </span>
          </div>
          {path.parent ? <span className="rig-diff-parent">{path.parent}</span> : null}
          {model.oldPath && model.newPath && model.oldPath !== model.newPath
            ? <span className="rig-diff-path-change">{model.oldPath} → {model.newPath}</span>
            : model.oldPath && !model.newPath
              ? <span className="rig-diff-path-change">deleted from {model.oldPath}</span>
              : !model.oldPath && model.newPath
                ? <span className="rig-diff-path-change">added as {model.newPath}</span>
                : null}
          <span className="rig-diff-revisions">{shortSha(model.base.commit)} → {shortSha(model.head.commit)}</span>
        </div>
        <div className="rig-diff-summary">
          {callbacks.onFilesHiddenChange ? <button type="button" className="rig-diff-toolbar-button" aria-expanded={!callbacks.filesHidden} onClick={() => callbacks.onFilesHiddenChange?.(!callbacks.filesHidden)}>
            {callbacks.filesHidden ? "Show files" : "Hide files"}
          </button> : null}
          {callbacks.onFocusModeChange ? <button type="button" className="rig-diff-toolbar-button" aria-pressed={!!callbacks.focusMode} title="Use the full app viewport for review. Escape exits focus mode." onClick={() => callbacks.onFocusModeChange?.(!callbacks.focusMode)}>
            {callbacks.focusMode ? "Exit focus" : "Focus mode"}
          </button> : null}
          <span
            className={`rig-diff-effect-delta ${effectDelta > 0 ? "added" : effectDelta < 0 ? "removed" : "stable"}`}
            title={baseAvailable && headAvailable
              ? `${baseEffectSites} base effect sites → ${headEffectSites} head effect sites`
              : `base: ${model.base.semanticState}; head: ${model.head.semanticState}`}
          >
            {semanticSummary}
          </span>
          <label className="rig-diff-viewed" title="Mark this file as reviewed (V)">
            <input
              type="checkbox"
              checked={callbacks.viewed || false}
              onChange={(event) => callbacks.onViewedChange?.(event.target.checked)}
            />
            Viewed
          </label>
          <details className="rig-diff-settings">
            <summary aria-label="Diff settings" title="Diff settings">⚙</summary>
            <div className="rig-diff-settings-menu">
              <fieldset>
                <legend>Effect annotations</legend>
                {reviewEffectModes.map((mode) => <label key={mode}>
                  <input type="radio" name="rig-effect-display" value={mode} checked={effectMode === mode} onChange={() => {
                    setEffectMode(mode);
                    saveReviewEffectMode(mode);
                    setExpanded(null);
                  }} />
                  {mode === "inline" ? "Inline" : mode === "gutter" ? "Gutter" : "Off"}
                </label>)}
              </fieldset>
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
              <label className="rig-diff-settings-check">
                <input
                  type="checkbox"
                  checked={wrapLines}
                  onChange={(event) => setWrapLines(event.target.checked)}
                />
                Wrap long lines
              </label>
            </div>
          </details>
        </div>
      </div>
      <div className="rig-diff-readiness" aria-live="polite">
        <span>Effects: {effectMode === "off" ? "hidden" : effectMode}</span>
        <span data-findings-side="base" data-state={baseFindingsStatus.state} title={baseFindingsStatus.detail}>Base: {baseFindingsStatus.label}</span>
        <span data-findings-side="head" data-state={headFindingsStatus.state} title={headFindingsStatus.detail}>Head: {headFindingsStatus.label}</span>
      </div>
      {effectMode === "gutter" && (baseAvailable || headAvailable)
        ? <div className="rig-diff-lane-key" title="Effect reach by family">
            <span className="rig-diff-lanehead" aria-label="Effect lane columns">
              {effectFamilies.map((family) => <b key={family.key} title={family.label}>{family.mark}</b>)}
            </span>
            <details className="rig-diff-lane-help">
              <summary aria-label="Explain effect reach lane" title="Explain effect reach lane">?</summary>
              <div>
                <strong>Effect reach</strong>
                <span>● in this call · ○ through callees</span>
                <span>teal changed · violet edge repeated</span>
                <span>exact depth and dispatch basis are in each mark's tooltip</span>
              </div>
            </details>
          </div>
        : null}
      {callbacks.focusLine && focusFound === false
        ? <div className="rig-diff-focus-note">
            {callbacks.focusLine.side === "old" ? "Base" : "Head"} line {callbacks.focusLine.line} is outside the changed hunks and their {model.contextLines}-line context.
          </div>
        : null}
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
            const nativeLine = changeLine(change, side);
            const key = getChangeKey(change);
            const item = projection.get(key);
            const suppressBase = canSuppressBaseGutter(item?.identical || false, item?.old?.headers.length || 0, item?.new?.headers.length || 0);
            const laneSide = effectMode !== "off" ? semanticLaneSide(viewType, change.type, side, suppressBase) : null;
            const line = laneSide == null ? null : changeLine(change, laneSide);
            const row = laneSide == null ? undefined : item?.[laneSide];
            const insight = row?.insight;
            const headers = row?.headers || [];
            const focused = callbacks.focusLine?.side === side && callbacks.focusLine.line === nativeLine;
            const changed = headers.length > 0 || (insight?.effects || []).some((effect) => lineEffectChange(effect, insight, headers, laneSide!, methodDeltas) !== "same");
            const hazard = !!insight?.hazards.length;
            const amplified = !!(insight?.amplifications.length || insight?.anchors.length);
            const marks = effectMode === "gutter" && (insight || headers.length)
              ? <EffectLane insight={insight} headers={headers} side={laneSide!} deltas={methodDeltas} />
              : null;
            const title = [
              laneSide == null ? "" : methodChangeTitle(headers, laneSide),
              ...(insight?.effects || []).map(inlineEffectLabel),
              hazard ? "Hazard findings — click for details" : "",
              amplified ? "Iteration candidate — not proof of runtime N+1" : "",
            ].filter(Boolean).join("\n");
            return wrapInAnchor(
              <span
                className={`rig-diff-gutter${focused ? " focus" : ""}${headers.length ? " method-change" : ""}`}
                data-rig-side={side}
                data-rig-line={nativeLine ?? undefined}
                title={laneSide == null ? undefined : methodChangeTitle(headers, laneSide) || undefined}
              >
                {insight || headers.length ? (
                  <button
                    type="button"
                    className={effectMode === "inline"
                      ? `rig-diff-disclosure-trigger${changed ? " changed" : ""}${hazard ? " hazard" : ""}${amplified ? " amplification" : ""}`
                      : "rig-diff-mark-button"}
                    title={title || "Show effects and open their call trees"}
                    aria-label={disclosureLabel(laneSide!, line!)}
                    aria-expanded={activeExpanded?.key === key && activeExpanded.side === laneSide}
                    data-rig-side={laneSide ?? undefined}
                    data-rig-line={line ?? undefined}
                    onClick={(event) => {
                      event.preventDefault();
                      event.stopPropagation();
                      toggleExpanded({ key, side: laneSide!, line: line! });
                    }}
                  >
                    {effectMode === "inline"
                      ? <svg viewBox="0 0 16 20" width="13" height="16" aria-hidden="true"><path fill="currentColor" d="M9 1 2 11h5l-1 8L14 8H9z" /></svg>
                      : marks}
                  </button>
                ) : marks}
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
