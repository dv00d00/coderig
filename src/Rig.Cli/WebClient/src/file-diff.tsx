import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from "react";
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
import { changeForSide, laneHeaderCells, semanticLaneSide } from "./review-gutter.ts";
import { canHighlightSource, matchesReviewSource, reviewSourceIdentity, sourceHunk, type ReviewSource } from "./review-source.ts";
import {
  amplificationLabel, anchorEvidenceNote, anchorGutterHint, anchorLabel, effectFamilyLabel, findingsStatus,
  inlineEffectLabel, disclosureLabel,
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
  // The server's evidence tier plus the two fields that explain it. Sent rather than re-derived here, so the
  // note under the list cannot drift from the definition the server graded the row against.
  evidence: string;
  guards: string | null;
  dispatchBasis: string | null;
  dispatchDegree: number;
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
  onLoadSource?: (side: "base" | "head") => Promise<ReviewSource>;
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

type SemanticRow = { side: "old" | "new"; line: number; insight?: LineInsight; headers: MethodComparison[]; changedHeaders: MethodComparison[] };
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

// Porcelain letters are git internals; the glyph carries the meaning and the word carries it for anyone who
// cannot use the colour. An unrecognised letter renders as itself rather than disappearing. DUPLICATED as
// `reviewFileStatusMarks` in wwwroot/components.js (the file list) — the two shells cannot share a module,
// one being a bundled TS island and the other plain JS served directly, so change both together.
const statusMarks: Record<string, { glyph: string; label: string }> = {
  A: { glyph: "+", label: "added" },
  M: { glyph: "±", label: "modified" },
  D: { glyph: "−", label: "deleted" },
  R: { glyph: "→", label: "renamed" },
  C: { glyph: "⧉", label: "copied" },
};

function statusMark(status: string): { glyph: string; label: string } {
  return statusMarks[String(status).toUpperCase()] || { glyph: status, label: status };
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

// Which revision's semantics belong in this gutter cell. The sticky lane header and the gutter renderer must
// answer this the same way, or the header labels a column that renders no lane.
function laneSideAt(
  change: ChangeData,
  gutterSide: "old" | "new",
  item: ProjectedChange | undefined,
  viewType: ViewType,
  showSource: boolean,
  sourceNativeSide: "old" | "new",
): "old" | "new" | null {
  if (showSource) return gutterSide === "new" ? sourceNativeSide : null;
  const suppressBase = canSuppressBaseGutter(item?.identical || false, item?.old?.changedHeaders.length || 0, item?.new?.changedHeaders.length || 0);
  return semanticLaneSide(viewType, change.type, gutterSide, suppressBase);
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

// What a method lane says when there is no delta to report: the aggregate reach itself, in the same
// vocabulary a call site uses (inlineEffectLabel).
function methodReachTitle(headers: MethodComparison[], side: "old" | "new"): string {
  const rows = headers.flatMap((comparison) => ((side === "old" ? comparison.base : comparison.head)?.effects || []).map(inlineEffectLabel));
  return rows.length ? `Method reach: ${rows.join(" · ")}` : "";
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
      {headers.map((comparison, index) => {
        const delta = methodChangeTitle([comparison], expanded.side);
        return <span className={`rig-diff-method-summary${delta ? "" : " unchanged"}`} key={index}>
          {(expanded.side === "old" ? comparison.base : comparison.head)?.name}: {delta || methodReachTitle([comparison], expanded.side) || "no reach recorded"}
        </span>;
      })}
      {insight && (insight.hazards.length > 0 || insight.amplifications.length > 0 || insight.anchors.length > 0)
        ? <div className="rig-diff-findings">
          {insight.hazards.map((finding, index) => (
            <span className={`rig-diff-finding-row hazard confidence-${finding.confidence}`} key={`hazard:${finding.type}:${index}`}>
              <strong>{finding.type.replaceAll("_", " ")}</strong> · {finding.confidence} · {finding.subtype}
              {finding.detail ? <span className="rig-diff-finding-detail">{finding.detail}</span> : null}
            </span>
          ))}
          {insight.amplifications.map((finding, index) => (
            <span className="rig-diff-finding-row amplification" key={`amplification:${finding.provider}:${index}`}>
              {amplificationLabel(finding)}
              <span className="rig-diff-finding-detail">Iteration: {finding.iteration} · {finding.confidence} confidence</span>
            </span>
          ))}
          {insight.anchors.map((finding, index) => (
            <span className={`rig-diff-finding-row anchor confidence-${finding.confidence}`} key={`anchor:${finding.witnessProvider}:${index}`}>
              {anchorLabel(finding)}
              <span className="rig-diff-finding-detail">{finding.caller} · {finding.iterationKind} · {finding.confidence} confidence{finding.guards ? ` · guarded by ${finding.guards}` : ""}{finding.witnessResource ? ` · ${finding.witnessResource}` : ""}</span>
            </span>
          ))}
        </div>
        : null}
      {insight && (insight.anchors.length > 0 || insight.amplifications.length > 0)
        ? <span className="rig-diff-candidate-note">{anchorEvidenceNote(insight.anchors, insight.amplifications.length)}</span>
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
  const headRef = useRef<HTMLDivElement>(null);
  const [viewType, setViewType] = useState<ViewType>("unified");
  const [wrapLines, setWrapLines] = useState(true);
  const [effectMode, setEffectMode] = useState<ReviewEffectMode>(() => readReviewEffectMode());
  const [expanded, setExpanded] = useState<Expanded | null>(null);
  const identity = reviewSourceIdentity(model);
  const [sourceView, setSourceView] = useState<{ identity: string; side: "base" | "head" } | null>(null);
  const showSource = sourceView?.identity === identity;
  const sourceSide = sourceView?.side || "head";
  const sourceNativeSide = sourceSide === "base" ? "old" : "new";
  const [sourceResult, setSourceResult] = useState<{ key: string; value?: ReviewSource; error?: string } | null>(null);
  const [sourceRetry, setSourceRetry] = useState(0);
  const sourceKey = JSON.stringify([identity, sourceSide, sourceRetry]);
  const source = showSource && sourceResult?.key === sourceKey ? sourceResult.value : undefined;
  const sourceError = showSource && sourceResult?.key === sourceKey ? sourceResult.error : undefined;
  const sourceLoader = useRef(callbacks.onLoadSource);
  sourceLoader.current = callbacks.onLoadSource;
  useEffect(() => {
    setSourceView(null);
    setSourceResult(null);
  }, [identity]);
  useEffect(() => {
    if (!showSource) return;
    let cancelled = false;
    setSourceResult(null);
    const load = sourceLoader.current;
    if (!load) return;
    load(sourceSide).then((value) => {
      if (cancelled) return;
      if (!matchesReviewSource(value, model, sourceSide)) throw new Error("Source revision does not match the selected review.");
      setSourceResult({ key: sourceKey, value });
    }).catch((error: unknown) => {
      if (!cancelled) setSourceResult({ key: sourceKey, error: error instanceof Error ? error.message : "Source request failed." });
    });
    return () => { cancelled = true; };
  }, [identity, showSource, sourceSide, sourceRetry]);
  const fullHunk = useMemo(() => source?.state === "available" && source.content !== null ? sourceHunk(source.content) : null, [source]);
  const effectiveView = showSource ? "unified" : viewType;
  const expansionContext = useMemo(() => ({}), [model.file, model.base.store, model.head.store, model.base.commit, model.head.commit, model.patch, showSource, sourceSide]);
  const activeExpanded = expanded?.context === expansionContext ? expanded : null;
  const [focusFound, setFocusFound] = useState<boolean | null>(null);
  const files = useMemo(() => (model.patch.trim() ? parseDiff(model.patch) : []), [model.patch]);
  const oldLines = useMemo(() => byLine(model.base), [model.base]);
  const newLines = useMemo(() => byLine(model.head), [model.head]);
  const file = files[0];
  const displayedHunks = useMemo(() => showSource ? (fullHunk ? [fullHunk] : []) : file?.hunks || [], [showSource, fullHunk, file]);
  const sourceHighlight = !showSource || canHighlightSource(source?.content || "", fullHunk?.newLines || 0);
  const displayedLanguage = showSource ? source?.language : model.language;
  const displayPath = showSource ? model[sourceSide].path || model.relativePath : model.relativePath;
  const path = useMemo(() => pathParts(displayPath || model.file), [displayPath, model.file]);
  const mark = statusMark(model.status);
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
    ? `effect sites ${baseEffectSites} → ${headEffectSites}`
    : baseAvailable
      ? "base-only semantics"
      : headAvailable
        ? "head-only semantics"
        : model.language === "text"
          ? "text-only · semantics unavailable"
          : "semantics unavailable";
  const tokens = useMemo(
    () =>
      displayedHunks.length && displayedLanguage === "csharp" && sourceHighlight
        ? tokenize(displayedHunks, {
            highlight: true,
            refractor: syntaxHighlighter,
            language: "csharp",
            enhancers: showSource ? [] : [markEdits(displayedHunks)],
          })
        : null,
    [displayedHunks, displayedLanguage, showSource, sourceHighlight],
  );
  const projection = useMemo(() => {
    const rows = new Map<string, ProjectedChange>();
    // Every method aggregate reaches the renderer, changed or not: a method row's lane means "this
    // method's reach", not "this method's reach changed". The delta is carried by the slot styling
    // (changeForSide) and by `changedHeaders` below, never by withholding the reach.
    const oldHeaders = methodDeltas.baseByLine;
    const newHeaders = methodDeltas.headByLine;
    for (const hunk of displayedHunks) for (const change of hunk.changes) {
      const row = (side: "old" | "new"): SemanticRow | undefined => {
        if (showSource && side !== sourceNativeSide) return undefined;
        const line = changeLine(change, side);
        if (line == null) return undefined;
        const insight = (side === "old" ? oldLines : newLines).get(line);
        const headers = (side === "old" ? oldHeaders : newHeaders).get(line) || [];
        const changedHeaders = headers.filter((comparison) => changedEffects(comparison).length > 0);
        return insight || headers.length ? { side, line, insight, headers, changedHeaders } : undefined;
      };
      const old = row("old");
      const next = row("new");
      const identical = sameVisibleLineInsight(old?.insight, next?.insight)
        && (old?.headers.length || 0) === (next?.headers.length || 0)
        && (old?.headers || []).every((header, index) => header === next?.headers[index]);
      rows.set(getChangeKey(change), { change, old, new: next, identical });
    }
    return rows;
  }, [displayedHunks, oldLines, newLines, methodDeltas, showSource, sourceNativeSide]);
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
    result[activeExpanded.key] = effectiveView === "split" && item.change.type === "normal"
      ? <div className="rig-diff-inline-pair"><div>{activeExpanded.side === "old" ? widget : null}</div><div>{activeExpanded.side === "new" ? widget : null}</div></div>
      : <div className="rig-diff-inline-single">{widget}</div>;
    return result;
  }, [projection, effectMode, activeExpanded, effectiveView, callbacks, expansionContext, methodDeltas]);
  // Width of the gutter's line-number track (see `--rig-lane-number`). The widest line number the rendered
  // hunks can show decides it: any narrower and a row carrying that number would push its lane off the
  // common origin the sticky key aligns to.
  const laneNumberTrack = useMemo(() => {
    let widest = 1;
    for (const hunk of displayedHunks) {
      widest = Math.max(widest, hunk.oldStart + hunk.oldLines - 1, hunk.newStart + hunk.newLines - 1);
    }
    return `${Math.max(2, String(widest).length)}ch`;
  }, [displayedHunks]);
  // The gutter columns that actually render a lane, answered by the renderer's own rule over the rendered
  // rows — a base lane suppressed on every row must not get a header group.
  const laneSides = useMemo(() => {
    const sides = new Set<"old" | "new">();
    if (effectMode !== "gutter") return sides;
    for (const hunk of displayedHunks) for (const change of hunk.changes) {
      if (sides.size === 2) return sides;
      const item = projection.get(getChangeKey(change));
      for (const gutterSide of ["old", "new"] as const) {
        if (sides.has(gutterSide)) continue;
        const laneSide = laneSideAt(change, gutterSide, item, viewType, showSource, sourceNativeSide);
        const row = laneSide == null ? undefined : item?.[laneSide];
        if (row && (row.insight || row.headers.length)) sides.add(gutterSide);
      }
    }
    return sides;
  }, [displayedHunks, projection, effectMode, viewType, showSource, sourceNativeSide]);

  useEffect(() => setExpanded(null), [expansionContext]);

  // Focus mode makes `.rig-diff-head` sticky at the top of the scrolling review pane, which is exactly where
  // the lane header sticks. Publish the head's measured height so the header stacks below it instead of
  // hiding behind it; CSS cannot read that height and a constant would drift with any toolbar wrap.
  useEffect(() => {
    const head = headRef.current;
    const island = rootRef.current;
    if (!head || !island) return;
    const sync = () => island.style.setProperty("--rig-diff-head-height", `${head.offsetHeight}px`);
    sync();
    const observer = new ResizeObserver(sync);
    observer.observe(head);
    return () => observer.disconnect();
  }, []);

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
  }, [callbacks.focusLine?.line, callbacks.focusLine?.side, model.patch, effectiveView, fullHunk]);

  // POSITION carries family identity in the lane, so the key is a row of the diff table itself: one glyph
  // group per rendered lane, laid out by the same gutter cell and the same width tokens as the slots below
  // it, and sticky so it stays with the lane while a long file scrolls.
  const laneKeyCells = effectMode === "gutter" && laneSides.size > 0
    ? laneHeaderCells(effectiveView, showSource ? "modify" : file.type, laneSides)
    : null;
  const laneHelpAt = laneKeyCells ? laneKeyCells.findIndex((cell) => cell.kind === "code") : -1;
  const laneKey = laneKeyCells
    ? <thead className="rig-diff-lane-key" key="rig-diff-lane-key">
        <tr>
          {/* `td`, not `th`: this row labels the lane, not the columns of data. A column header would be
              announced again on every code line below it. */}
          {laneKeyCells.map((cell, index) => cell.kind === "gutter"
            ? <td className="diff-gutter" key={index}>
                {cell.lane
                  ? <span className="rig-diff-gutter">
                      <span className="rig-diff-marks">
                        <span className="rig-diff-finding-stack" />
                        <span className="rig-diff-lanehead" aria-label="Effect lane columns" title="Effect reach by family">
                          {effectFamilies.map((family) => <b key={family.key} title={family.label}>{family.mark}</b>)}
                        </span>
                      </span>
                      <span className="rig-diff-line-number" />
                    </span>
                  : null}
              </td>
            : <td className="diff-code" key={index}>
                {index === laneHelpAt
                  ? <details className="rig-diff-lane-help">
                      <summary aria-label="Explain effect reach lane" title="Explain effect reach lane">?</summary>
                      <div>
                        <strong>Effect reach</strong>
                        <span>● in this call · ○ through callees</span>
                        <span>teal changed · violet edge repeated</span>
                        <span>exact depth and dispatch basis are in each mark's tooltip</span>
                      </div>
                    </details>
                  : null}
              </td>)}
        </tr>
      </thead>
    : null;

  return (
    <div
      className={`rig-diff-island view-${effectiveView} effects-${effectMode} ${showSource ? "full-source" : "patch-source"} ${wrapLines ? "wrap-lines" : "no-wrap"}`}
      style={{ "--rig-lane-number": laneNumberTrack } as CSSProperties}
      ref={rootRef}
    >
      <div className="rig-diff-head" ref={headRef}>
        <div className="rig-diff-identity" title={displayPath}>
          <div className="rig-diff-file-line">
            <span className={`rig-diff-status status-${model.status.toLowerCase()}`} title={mark.label} aria-label={mark.label}>
              {mark.glyph}
            </span>
            <strong>{path.name}</strong>
            {!showSource ? <span className="rig-diff-patch-counts" aria-label={`${patchCounts.additions} additions, ${patchCounts.deletions} deletions`}>
              <b>+{patchCounts.additions}</b>
              <i>−{patchCounts.deletions}</i>
            </span> : <span className="rig-source-revision">{sourceSide === "base" ? "Base" : "Head"} · {shortSha(model[sourceSide].commit)}</span>}
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
          {callbacks.onLoadSource ? <button type="button" className="rig-diff-toolbar-button" aria-pressed={showSource} onClick={() => {
            setSourceView(showSource ? null : { identity, side: model.newPath ? "head" : "base" });
          }}>{showSource ? "Back to diff" : "Full file"}</button> : null}
          {showSource ? <select className="rig-diff-toolbar-button" aria-label="File revision" value={sourceSide} onChange={(event) => setSourceView({ identity, side: event.target.value as "base" | "head" })}>
            <option value="base">Base</option><option value="head">Head</option>
          </select> : null}
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
              <fieldset disabled={showSource}>
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
                  disabled={showSource}
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
      {callbacks.focusLine && focusFound === false && !showSource
        ? <div className="rig-diff-focus-note">
            {callbacks.focusLine.side === "old" ? "Base" : "Head"} line {callbacks.focusLine.line} is outside the changed hunks and their {model.contextLines}-line context.
          </div>
        : null}
      {showSource && (!source || source.state !== "available" || !fullHunk) ? (
        <div className="rig-diff-empty rig-source-message" role="status">
          {sourceError || (source ? source.reason || "Empty file — 0 bytes in this revision." : "Loading exact file from Git…")}
          {sourceError || source?.state === "unavailable" ? <button type="button" className="rig-diff-toolbar-button" onClick={() => setSourceRetry(value => value + 1)}>Retry source</button> : null}
        </div>
      ) : !showSource && !file ? (
        <div className="rig-diff-empty">No textual changes in this file.</div>
      ) : (
        <>
        {showSource && !sourceHighlight ? <div className="rig-diff-focus-note">Full file · {fullHunk?.newLines} lines. Syntax highlighting is off for this large file; all source lines remain available.</div> : null}
        <Diff
          viewType={effectiveView}
          diffType={showSource ? "modify" : file.type}
          hunks={displayedHunks}
          tokens={tokens}
          widgets={widgets}
          renderGutter={({ change, side, renderDefault, wrapInAnchor }) => {
            const nativeLine = changeLine(change, side);
            const key = getChangeKey(change);
            const item = projection.get(key);
            const laneSide = effectMode !== "off" ? laneSideAt(change, side, item, viewType, showSource, sourceNativeSide) : null;
            const line = laneSide == null ? null : changeLine(change, laneSide);
            const row = laneSide == null ? undefined : item?.[laneSide];
            const insight = row?.insight;
            const headers = row?.headers || [];
            const changedHeaders = row?.changedHeaders || [];
            const displayedSide = showSource ? sourceNativeSide : side;
            const focused = callbacks.focusLine?.side === displayedSide && callbacks.focusLine.line === nativeLine;
            const changed = changedHeaders.length > 0 || (insight?.effects || []).some((effect) => lineEffectChange(effect, insight, headers, laneSide!, methodDeltas) !== "same");
            const hazard = !!insight?.hazards.length;
            const amplified = !!(insight?.amplifications.length || insight?.anchors.length);
            const marks = effectMode === "gutter" && (insight || headers.length)
              ? <EffectLane insight={insight} headers={headers} side={laneSide!} deltas={methodDeltas} />
              : null;
            // A method row with no delta still has something to say: its reach.
            const methodTitle = laneSide == null ? "" : methodChangeTitle(headers, laneSide) || methodReachTitle(headers, laneSide);
            const title = [
              methodTitle,
              ...(insight?.effects || []).map(inlineEffectLabel),
              hazard ? "Hazard findings — click for details" : "",
              amplified ? anchorGutterHint(insight?.anchors || []) : "",
            ].filter(Boolean).join("\n");
            return wrapInAnchor(
              <span
                className={`rig-diff-gutter${focused ? " focus" : ""}${changedHeaders.length ? " method-change" : ""}`}
                data-rig-side={showSource && side === "old" ? undefined : displayedSide}
                data-rig-line={showSource && side === "old" ? undefined : nativeLine ?? undefined}
                title={methodTitle || undefined}
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
                <span className="rig-diff-line-number">{renderDefault()}</span>
              </span>,
            );
          }}
        >
          {(hunks) => {
            const bodies = hunks.map((hunk) => <Hunk key={hunk.content} hunk={hunk} />);
            return laneKey ? [laneKey, ...bodies] : bodies;
          }}
        </Diff>
        </>
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
