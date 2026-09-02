export const reviewEffectModes = ["inline", "gutter", "off"] as const;
export type ReviewEffectMode = typeof reviewEffectModes[number];
export const reviewEffectModeKey = "rig.review.effectMode";

type PreferenceStore = Pick<Storage, "getItem" | "setItem">;
type StoreAccess = () => PreferenceStore;
const browserStorage: StoreAccess = () => window.localStorage;

export function readReviewEffectMode(access: StoreAccess = browserStorage): ReviewEffectMode {
  try {
    const value = access().getItem(reviewEffectModeKey);
    return reviewEffectModes.includes(value as ReviewEffectMode) ? value as ReviewEffectMode : "inline";
  } catch {
    return "inline";
  }
}

export function saveReviewEffectMode(mode: ReviewEffectMode, access: StoreAccess = browserStorage): void {
  try {
    access().setItem(reviewEffectModeKey, mode);
  } catch {
    // A blocked browser store must not prevent changing this view's in-memory preference.
  }
}

const familyLabels: Record<string, string> = {
  db: "Database", cache: "Cache", blob: "Object store", bus: "Message bus",
  echo: "Event channel", io: "File system / I/O", rpc: "Remote call", search: "Search",
};

export function effectFamilyLabel(family: string): string {
  return familyLabels[family] || family;
}

export function inlineEffectLabel(effect: {
  family: string; nearestDepth: number; viaDispatchOnly: boolean; looped: boolean;
}): string {
  return [
    effectFamilyLabel(effect.family),
    effect.nearestDepth === 0 ? "direct" : `depth ${effect.nearestDepth}`,
    effect.viaDispatchOnly ? "possible dispatch" : "",
    effect.looped ? "inside iteration" : "",
  ].filter(Boolean).join(" · ");
}

export function amplificationLabel(finding: { provider: string; operation: string }): string {
  return `${finding.provider}:${finding.operation} · inside iteration`;
}

export function anchorLabel(finding: {
  witnessProvider: string; witnessOperation: string; witnessDepth: number;
}): string {
  return `${finding.witnessProvider}:${finding.witnessOperation} · reached from iterating call · depth ${finding.witnessDepth} · candidate`;
}

export function findingsStatus(revision: {
  semanticState: "available" | "not-indexed" | "not-present";
  findings?: { hazards: unknown[]; amplifications: unknown[]; anchors: unknown[]; crossMethodAvailable: boolean } | null;
}): { state: string; label: string; detail: string } {
  if (revision.semanticState === "not-present") {
    return { state: "not-present", label: "file absent", detail: "This revision has no file to analyze." };
  }
  if (revision.semanticState === "not-indexed") {
    return { state: "not-indexed", label: "not indexed", detail: "Source is available, but semantic findings are not." };
  }
  if (revision.findings === undefined) {
    return { state: "loading", label: "findings loading…", detail: "Effects are independent; findings are still being loaded." };
  }
  if (revision.findings === null) {
    return { state: "unavailable", label: "findings unavailable", detail: "Findings could not be loaded. This does not mean zero findings." };
  }
  const { hazards, amplifications, anchors, crossMethodAvailable } = revision.findings;
  const count = hazards.length + amplifications.length + anchors.length;
  return {
    state: crossMethodAvailable ? "ready" : "partial",
    label: `${count === 0 ? "no" : count} findings${crossMethodAvailable ? "" : " · cross-method off"}`,
    detail: crossMethodAvailable
      ? "Local and cross-method findings loaded. Iteration findings are candidates, not proof of runtime N+1."
      : "Local findings loaded. Cross-method analysis (tier 3) is disabled for this store; absence of anchors is not a negative result.",
  };
}

export function disclosureLabel(side: "old" | "new", line: number): string {
  return `Show effects for ${side === "old" ? "base" : "head"} line ${line}`;
}

type VisibleAnnotations = {
  sites: Array<{ line: number }>;
  effects: unknown[];
  hazards: Array<{ line: number }>;
  amplifications: Array<{ line: number }>;
  anchors: Array<{ line: number }>;
};

export function sameVisibleAnnotations(base?: VisibleAnnotations, head?: VisibleAnnotations): boolean {
  if (!base || !head) return base === head;
  // Positions naturally move across revisions; every other visible field (including targets and details)
  // must match. Equal aggregate families alone cannot hide two different call bindings on a context row.
  const withoutLine = ({ line: _line, ...value }: { line: number }) => value;
  const fingerprint = (value: VisibleAnnotations) => JSON.stringify({
    sites: value.sites.map(withoutLine), effects: value.effects,
    hazards: value.hazards.map(withoutLine), amplifications: value.amplifications.map(withoutLine),
    anchors: value.anchors.map(withoutLine),
  });
  return fingerprint(base) === fingerprint(head);
}

// Split view draws a lane per pane, so a context row whose two panes would draw the SAME lane pays twice
// for one fact. The counts are of CHANGED method aggregates, not of aggregates: an unchanged aggregate is
// equal on both sides by construction (compareEffects only reports "same" when family, depth, dispatch
// basis and repetition all match), so both panes would render identical slots and the base one is pure
// duplication. A CHANGED aggregate keeps both panes for the original reason — a removed last effect has
// no head mark, so retaining only that empty head lane would hide the removal even though the per-line
// annotations match.
export function canSuppressBaseGutter(identical: boolean, changedBaseHeaders: number, changedHeadHeaders: number): boolean {
  return identical && changedBaseHeaders === 0 && changedHeadHeaders === 0;
}
