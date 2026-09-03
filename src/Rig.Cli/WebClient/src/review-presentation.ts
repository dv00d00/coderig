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

// The server's tier (CrossMethodAmplificationDataset.AnchorFinding.Evidence). Never re-derived here: the
// note this drives is a claim about evidence, and a client that recomputed the tier could drift from the
// definition the server calibrated it against.
export type AnchorEvidence = "direct" | "candidate" | "inferred";

type AnchorRow = {
  witnessProvider: string; witnessOperation: string; witnessDepth: number;
  evidence?: string; dispatchBasis?: string | null; dispatchDegree?: number;
};

const evidenceWords: Record<string, string> = {
  direct: "per-iteration call",
  inferred: "inferred reach",
  candidate: "candidate",
};

// Why a row is only "inferred", from the two fields that can cause it — a fan-out to N implementations is a
// different doubt from a name-guessed hop, and a reader deciding whether to chase the row needs to know which.
function inferredReason(finding: AnchorRow): string {
  if ((finding.dispatchDegree ?? 0) > 1) return `dispatch fan-out ${finding.dispatchDegree}`;
  return finding.dispatchBasis === "heuristic" ? "guessed dispatch hop" : "";
}

export function anchorLabel(finding: AnchorRow): string {
  const evidence = finding.evidence ?? "candidate";
  const reason = evidence === "inferred" ? inferredReason(finding) : "";
  return [
    `${finding.witnessProvider}:${finding.witnessOperation}`,
    "reached from iterating call",
    `depth ${finding.witnessDepth}`,
    evidenceWords[evidence] || evidence,
    reason ? `(${reason})` : "",
  ].filter(Boolean).join(" · ");
}

// The note under the finding list. It used to be one flat sentence over every row, which undersold the
// strongest tier: a depth-0 unconditional call reached with no dispatch inference read exactly like a depth-5
// witness found through a guessed virtual hop. Now the strong claim is made where it holds and the hedge is
// kept where it belongs — and the half of the old sentence that is true at EVERY tier (no query count, because
// N is data-dependent and a callee may cache) is never dropped.
export function anchorEvidenceNote(anchors: Array<{ evidence?: string }>, localAmplifications = 0): string {
  const direct = anchors.filter((anchor) => anchor.evidence === "direct").length;
  const hedged = anchors.length - direct + localAmplifications;
  if (direct === 0) {
    return "Static iteration candidate — not proof of runtime N+1 or a query count.";
  }
  const strong = `${direct === 1 ? "One call is" : `${direct} calls are`} issued once per iteration: unconditional inside the loop, `
    + "with the effect reached without dispatch inference. Not a query count — N is data-dependent and a callee may cache.";
  return hedged === 0 ? strong : `${strong} The remaining ${hedged === 1 ? "row is a static candidate" : `${hedged} rows are static candidates`}.`;
}

// The one-line form for a gutter tooltip, where the full note does not fit.
export function anchorGutterHint(anchors: Array<{ evidence?: string }>): string {
  return anchors.some((anchor) => anchor.evidence === "direct")
    ? "Per-iteration call — N is data-dependent, not a query count"
    : "Iteration candidate — not proof of runtime N+1";
}

export function findingsStatus(revision: {
  semanticState: "available" | "not-indexed" | "not-present";
  findings?: { hazards: unknown[]; amplifications: unknown[]; anchors: Array<{ evidence?: string }>; crossMethodAvailable: boolean } | null;
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
    // The tier-3 half of this sentence is graded the same way the note under the list is: claiming
    // "candidates" over a set that contains a direct per-iteration call undersells it.
    detail: crossMethodAvailable
      ? `Local and cross-method findings loaded. ${anchorEvidenceNote(anchors)}`
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
