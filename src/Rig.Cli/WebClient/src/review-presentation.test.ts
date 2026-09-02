import assert from "node:assert/strict";
import test from "node:test";
import {
  amplificationLabel, anchorLabel, effectFamilyLabel, findingsStatus, inlineEffectLabel, disclosureLabel,
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

test("readiness distinguishes loading, failure, absent/unindexed source and zero findings", () => {
  assert.equal(findingsStatus({ semanticState: "available" }).state, "loading");
  assert.equal(findingsStatus({ semanticState: "available", findings: null }).state, "unavailable");
  assert.equal(findingsStatus({ semanticState: "not-indexed" }).state, "not-indexed");
  assert.equal(findingsStatus({ semanticState: "not-present" }).state, "not-present");
  const empty = { hazards: [], amplifications: [], anchors: [], crossMethodAvailable: true };
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
