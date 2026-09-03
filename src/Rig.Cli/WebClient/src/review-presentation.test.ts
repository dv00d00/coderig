import assert from "node:assert/strict";
import test from "node:test";
import {
  amplificationLabel, anchorEvidenceNote, anchorGutterHint, anchorLabel, effectFamilyLabel, findingsStatus,
  inlineEffectLabel, disclosureLabel,
  readReviewEffectMode, reviewEffectModeKey, reviewEffectModes, saveReviewEffectMode,
  sameVisibleAnnotations,
  canSuppressBaseGutter,
} from "./review-presentation.ts";

test("effect rendering defaults to Inline and persists each supported preference", () => {
  const values = new Map<string, string>();
  const access = () => ({ getItem: (key: string) => values.get(key) ?? null, setItem: (key: string, value: string) => { values.set(key, value); } });
  assert.equal(readReviewEffectMode(access), "inline");
  for (const mode of reviewEffectModes) {
    saveReviewEffectMode(mode, access);
    assert.equal(readReviewEffectMode(access), mode);
  }
  values.set(reviewEffectModeKey, "old-or-invalid-setting");
  assert.equal(readReviewEffectMode(access), "inline");
});

test("blocked storage access and writes do not break the renderer", () => {
  const denied = () => { throw new Error("SecurityError"); };
  assert.equal(readReviewEffectMode(denied), "inline");
  assert.doesNotThrow(() => saveReviewEffectMode("gutter", denied));
  assert.doesNotThrow(() => saveReviewEffectMode("off", () => ({ getItem: () => null, setItem: denied })));
});

test("real Reads.cs amplification is described without claiming runtime query counts", () => {
  // GET /api/file-findings?store=904674e12dc1&file=…/Rig.Storage/Queries/Reads.cs, 2026-09-02.
  const finding = { type: "looped_effect", confidence: "high", subtype: "effect_inside_loop", key: "while", enclosing: "Reads.SearchSymbolsAsync", line: 141, iteration: "while", provider: "db_reader", operation: "row_read" };
  assert.equal(amplificationLabel(finding), "db_reader:row_read · inside iteration");
});

test("effects expose readable family, depth and uncertain dispatch explicitly", () => {
  assert.equal(inlineEffectLabel({ family: "db", nearestDepth: 0, viaDispatchOnly: false, looped: false }), "Database · direct");
  assert.equal(inlineEffectLabel({ family: "io", nearestDepth: 3, viaDispatchOnly: true, looped: true }), "File system / I/O · depth 3 · possible dispatch · inside iteration");
  assert.equal(effectFamilyLabel("future-family"), "future-family");
  assert.equal(anchorLabel({ witnessProvider: "efcore", witnessOperation: "read", witnessDepth: 2 }), "efcore:read · reached from iterating call · depth 2 · candidate");
});

// The tier is the SERVER's; these only pin that the label says which one, and says WHY when the reason is
// one a reader would act on differently (a fan-out to N impls is not the same doubt as a guessed hop).
test("an anchor label carries its evidence tier and, when inferred, the doubt that caused it", () => {
  const row = { witnessProvider: "efcore", witnessOperation: "read", witnessDepth: 0 };
  assert.equal(anchorLabel({ ...row, evidence: "direct" }), "efcore:read · reached from iterating call · depth 0 · per-iteration call");
  assert.equal(
    anchorLabel({ ...row, witnessDepth: 5, evidence: "inferred", dispatchBasis: "heuristic", dispatchDegree: 0 }),
    "efcore:read · reached from iterating call · depth 5 · inferred reach · (guessed dispatch hop)",
  );
  assert.equal(
    anchorLabel({ ...row, witnessDepth: 3, evidence: "inferred", dispatchBasis: "roslyn", dispatchDegree: 7 }),
    "efcore:read · reached from iterating call · depth 3 · inferred reach · (dispatch fan-out 7)",
  );
  // An absent tier must degrade to the WEAK word, never the strong one.
  assert.equal(anchorLabel(row), "efcore:read · reached from iterating call · depth 0 · candidate");
});

test("the note claims per-iteration issuance only where the evidence is direct, and never claims a query count", () => {
  const direct = { evidence: "direct" };
  const candidate = { evidence: "candidate" };

  assert.equal(anchorEvidenceNote([candidate, { evidence: "inferred" }]), "Static iteration candidate — not proof of runtime N+1 or a query count.");
  assert.equal(anchorEvidenceNote([]), "Static iteration candidate — not proof of runtime N+1 or a query count.");
  // An un-graded row is not a direct one.
  assert.equal(anchorEvidenceNote([{}]), "Static iteration candidate — not proof of runtime N+1 or a query count.");

  const strong = anchorEvidenceNote([direct]);
  assert.match(strong, /^One call is issued once per iteration/);
  assert.doesNotMatch(strong, /candidate/);
  assert.match(anchorEvidenceNote([direct, direct]), /^2 calls are issued once per iteration/);

  // Mixed: the strong claim stands AND the weaker rows keep their hedge. Local (tier-2) amplifications count
  // toward the hedged rows, because the note sits under both lists.
  assert.match(anchorEvidenceNote([direct, candidate]), /The remaining row is a static candidate\.$/);
  assert.match(anchorEvidenceNote([direct, candidate], 2), /The remaining 3 rows are static candidates\.$/);

  // The half that is true at every tier is never dropped.
  for (const note of [anchorEvidenceNote([direct]), anchorEvidenceNote([direct, candidate], 1)]) {
    assert.match(note, /not a query count|not proof of runtime N\+1/i);
  }

  assert.equal(anchorGutterHint([candidate]), "Iteration candidate — not proof of runtime N+1");
  assert.equal(anchorGutterHint([candidate, direct]), "Per-iteration call — N is data-dependent, not a query count");
});

test("readiness distinguishes loading, failure, absent/unindexed source and zero findings", () => {
  assert.equal(findingsStatus({ semanticState: "available" }).state, "loading");
  assert.equal(findingsStatus({ semanticState: "available", findings: null }).state, "unavailable");
  assert.equal(findingsStatus({ semanticState: "not-indexed" }).state, "not-indexed");
  assert.equal(findingsStatus({ semanticState: "not-present" }).state, "not-present");
  const empty = { hazards: [], amplifications: [], anchors: [] as Array<{ evidence?: string }>, crossMethodAvailable: true };
  assert.equal(findingsStatus({ semanticState: "available", findings: empty }).label, "no findings");
  assert.equal(findingsStatus({ semanticState: "available", findings: { ...empty, crossMethodAvailable: false } }).label, "no findings · cross-method off");
  assert.equal(findingsStatus({ semanticState: "available", findings: { ...empty, amplifications: Array(11).fill({}), crossMethodAvailable: false } }).label, "11 findings · cross-method off");
});

test("collapsed inline disclosure has a side-aware accessible name without noisy effect lists", () => {
  assert.equal(disclosureLabel("old", 141), "Show effects for base line 141");
  assert.equal(disclosureLabel("new", 12345), "Show effects for head line 12345");
});

test("context suppression compares call identities and finding details, but ignores shifted lines", () => {
  const base = { sites: [{ line: 5, targetMethodId: "M:Db.Read", enclosingMethodId: "M:A.Run", effects: [] }], effects: [], hazards: [{ line: 5, detail: "first detail" }], amplifications: [], anchors: [] };
  assert.equal(sameVisibleAnnotations(base, { ...base, sites: [{ ...base.sites[0], line: 8 }] }), true);
  const differentCall = { ...base, sites: [{ ...base.sites[0], targetMethodId: "M:Db.Write" }] };
  const differentFinding = { ...base, hazards: [{ ...base.hazards[0], detail: "different detail" }] };
  assert.equal(sameVisibleAnnotations(base, differentCall), false);
  assert.equal(sameVisibleAnnotations(base, differentFinding), false);
  assert.equal(sameVisibleAnnotations(undefined, base), false);
  assert.equal(sameVisibleAnnotations(undefined, undefined), true);
});

test("gutter retains base method declaration when its last effect was removed", () => {
  assert.equal(canSuppressBaseGutter(true, 1, 1), false);
  assert.equal(canSuppressBaseGutter(true, 1, 0), false);
  assert.equal(canSuppressBaseGutter(true, 0, 1), false);
  assert.equal(canSuppressBaseGutter(true, 0, 0), true);
  assert.equal(canSuppressBaseGutter(false, 0, 0), false);
});
