// App state (a single Zustand-shaped store) + the ONLY code that reads/writes the URL. No DOM, no fetch.
// The URL is the source of truth for the QUERY (shareable, refresh- and back/forward-safe); preferences
// (theme, rail width) live in localStorage (see main.js).

import { createStore } from "./lib.js";
import { lensFilterDefaults, LENS_FILTER_DEFAULTS } from "./filelens.js";

export const store = createStore({
  // query — mirrored to the URL
  from: "",
  storeId: null, // explicit store selection; null => LATEST (the read default)
  view: "paths", // paths | full | effects
  mode: "none", // none | only | exclude   (effect filter)
  tokens: [], // provider / provider:op filter tokens
  asyncWalk: false, // --async (changes the fetched tree → refetch)
  rawTree: false, // show the raw unfolded tree (bypass opaque/collapse seam folds → ?raw=true, refetch)
  intrinsic: false, // include language-intrinsic alloc/throw effects (server-side view → refetch)
  collapse: "", // client-side collapse depth ("" = none)
  signatures: false, // render mode: show param signatures
  predicates: false, // render mode: show control-dependence guards
  hazards: false, // render mode: overlay hazard marks
  callers: null, // reverse-nav drawer: null = closed; else { target, mode, matched, entryPoints?, roots? } from /api/callers
  // data
  runs: [],
  providers: { providers: [], providerOps: [] },
  tree: null, // last /api/tree response (the canonical tree)
  treeFrom: "", // the pattern `tree` was loaded for
  eps: [], // entry points for the active store
  hazardMarks: null, // /api/hazards response (array of {methodId,type,confidence,sites}) for the current tree
  // impact mode (store-vs-store diff)
  appMode: "tree", // tree | file | review | impact | refs | hotspots  (top-level view)
  filePath: "", // exact path from /api/files
  // The lens loads the WHOLE file, so this is a SCROLL TARGET (mirrored to ?line=), not a page start.
  fileStart: 1,
  fileEffects: null, // immutable semantic read model for filePath
  fileSource: null, // provenance-aware source page
  fileError: "",
  // one-file semantic review (exact Git patch + annotations from both immutable stores)
  reviewBase: "",
  reviewHead: "",
  reviewFile: "",
  reviewData: null,
  reviewError: "",
  // The lens overlay's own filter — URL-addressable so a tuned view is shareable (see lensToUrl/lensFromUrl).
  // Everything but `intrinsic`/`async` is applied CLIENT-SIDE, which is the point: the underlying query costs
  // ~50s cold, so depth/basis/grain tuning must never refetch.
  lensFilter: lensFilterDefaults(),
  // Legend starts OPEN and remembers being dismissed (main.js owns the localStorage side, as with theme):
  // the grammar is four axes, and a reader meeting it for the first time should not have to hunt for the key.
  lensLegend: true,
  fileFocusLine: 0, // the line `n`/`p`/minimap/outline navigation last landed on (highlight only)
  impactBase: "", // base store id
  impactHead: "", // head store id
  impactAsync: false, // --async for the diff: walk async/scheduled handoffs (changes the diff → refetch)
  impactData: null, // /api/impact response
  impactFilter: "", // filter over per-EP deltas (route / effect substring)
  // refs mode (assembly-reference analysis — a GLOBAL report, no from-pattern; fetched like the EP inventory)
  refsTab: "unused", // unused | usage  (which report)
  refsFilter: "", // substring filter → the `filter` query param (unused: declaring; usage: target assemblies)
  refsUnused: null, // /api/refs/unused response ({ solutionAvailable, groups, candidateCount, projectCount })
  refsUsage: null, // /api/refs/usage response ({ rows: [{ assembly, refs, fromMethods }] })
  // hotspot/refactoring report + its explicit A/B behavior comparison
  hotspotSort: "density",
  hotspotTop: 50,
  hotspotNoLambdas: false,
  hotspotIntrinsic: false,
  hotspotData: null,
  compareA: "",
  compareB: "",
  effectsDiffData: null,
  // diff overlay on a tree: when you open a tree FROM an impact EP card, this carries that EP's changed
  // methods so the head tree can highlight what the diff touched. Session-only (not URL-synced). null = off.
  diffOverlay: null, // { from, base, head, added:[enclosingFqn], removed:[enclosingFqn], changedOnly:bool }
  // pivot history: a breadcrumb trail of tree/drawer pivots (re-root, drawer open, diff cross-link) so an
  // investigation is a navigable session, not a single query. Session-only (not URL-synced, same reasoning as
  // `diffOverlay` above) — the CURRENT position is still fully expressed by the existing query params; this is
  // a TRAIL on top, driven by the History API's own state object (see main.js's pushState/popstate wiring).
  history: [], // [{ kind: "tree"|"callers"|"reaches"|"path", label, from, appMode, storeId, diffOverlay, callers }]
  historyCursor: -1, // index into `history` of the crumb currently being viewed; -1 = no crumbs yet
  // ui
  tab: "runs", // runs | eps
  epFilter: "",
  // (status text + busy spinner are transient DOM, managed directly via refs in main.js — not app state)
});

export const get = () => store.getState();
export const set = (patch) => store.setState(patch);

// The resolved store id: explicit selection, else the LATEST run's id. Used for cache keys + display.
export function activeStoreId(s = get()) {
  return (
    s.storeId || (s.runs.find((r) => r.isLatest) || s.runs[0] || {}).storeId
  );
}

// Append a pivot crumb to the trail, discarding any forward entries past the current cursor (standard
// back-then-navigate semantics — like a browser's own history). Returns a state patch; the caller `set()`s it
// (mirrors the module's no-DOM/no-fetch invariant — this is pure state math, main.js owns the pushState side).
export function pushCrumb(s, crumb) {
  const trail = [...s.history.slice(0, s.historyCursor + 1), crumb];
  return { history: trail, historyCursor: trail.length - 1 };
}

// ---- lens filter <-> URL -------------------------------------------------------------------------------
// Terse, prefixed keys, and only what differs from the default gets written — a shared link stays readable
// and a default view produces no lens params at all. Round-trips through `lensFromUrl` exactly.
const LENS_URL_KEYS = {
  mode: "lmode",
  minDepth: "lmin",
  maxDepth: "lmax",
  directOnly: "lhere",
  loopedOnly: "lloop",
  dispatch: "ldisp",
  tier3Min: "lt3",
  grain: "lgrain",
  distant: "lbelow",
  outlineSort: "lsort",
  intrinsic: "lintrinsic",
  async: "lasync",
};
function lensToUrl(p, f) {
  for (const [key, param] of Object.entries(LENS_URL_KEYS)) {
    const value = f[key];
    if (value === LENS_FILTER_DEFAULTS[key] || value === "" || value === false) continue;
    p.set(param, value === true ? "1" : String(value));
  }
  if (f.tokens.length) p.set("ltok", f.tokens.join(","));
  const tiers = [...f.tiers].sort().join(",");
  if (tiers !== [...LENS_FILTER_DEFAULTS.tiers].sort().join(",")) p.set("ltiers", f.tiers.join(",") || "none");
}
export function lensFromUrl(p) {
  const f = lensFilterDefaults();
  const oneOf = (param, key, values) => {
    const v = p.get(param);
    if (v !== null && values.includes(v)) f[key] = v;
  };
  const int = (param, key) => {
    const v = p.get(param);
    if (v !== null && v !== "" && Number.isFinite(Number.parseInt(v, 10))) f[key] = Math.max(0, Number.parseInt(v, 10));
  };
  oneOf("lmode", "mode", ["none", "only", "exclude"]);
  oneOf("ldisp", "dispatch", ["show", "hide", "only"]);
  oneOf("lt3", "tier3Min", ["low", "medium", "high"]);
  oneOf("lgrain", "grain", ["family", "provider"]);
  oneOf("lbelow", "distant", ["fold", "expand", "hide"]);
  oneOf("lsort", "outlineSort", ["line", "severity"]);
  int("lmin", "minDepth");
  int("lmax", "maxDepth");
  f.directOnly = p.get("lhere") === "1";
  f.loopedOnly = p.get("lloop") === "1";
  f.intrinsic = p.get("lintrinsic") === "1";
  f.async = p.get("lasync") === "1";
  f.tokens = (p.get("ltok") || "").split(",").map((t) => t.trim()).filter(Boolean);
  const tiers = p.get("ltiers");
  if (tiers !== null)
    f.tiers = tiers === "none" ? [] : tiers.split(",").map((t) => t.trim()).filter((t) => ["haz", "amp", "xm"].includes(t));
  return f;
}

// The query slice, for a watch() that re-serializes the URL only when the query changes.
export const querySlice = (s) => [
  s.from,
  s.storeId,
  s.view,
  s.mode,
  s.tokens.join(","),
  s.asyncWalk,
  s.intrinsic,
  s.collapse,
  s.signatures,
  s.predicates,
  s.hazards,
  s.appMode,
  s.filePath,
  s.fileStart,
  s.reviewBase,
  s.reviewHead,
  s.reviewFile,
  s.lensFilter,
  s.impactBase,
  s.impactHead,
  s.impactAsync,
  s.hotspotSort,
  s.hotspotTop,
  s.hotspotNoLambdas,
  s.hotspotIntrinsic,
  s.compareA,
  s.compareB,
];

// state -> URL (query params only; defaults omitted to keep links terse).
export function serializeUrl(s = get()) {
  const p = new URLSearchParams();
  if (s.from) p.set("from", s.from);
  if (s.storeId) p.set("store", s.storeId);
  if (s.view !== "paths") p.set("view", s.view);
  if (s.mode !== "none") p.set("mode", s.mode);
  if (s.tokens.length) p.set("tokens", s.tokens.join(","));
  if (s.asyncWalk) p.set("async", "1");
  // Hotspots has its own intrinsic report option below. Do not let a remembered Tree preference silently
  // opt a shared Hotspots URL into alloc/throw metrics.
  if (s.intrinsic && s.appMode !== "hotspots") p.set("intrinsic", "1");
  if (s.collapse) p.set("collapse", s.collapse);
  if (s.signatures) p.set("sig", "1");
  if (s.predicates) p.set("pred", "1");
  if (s.hazards) p.set("haz", "1");
  if (s.appMode === "file") {
    p.set("app", "file");
    if (s.filePath) p.set("file", s.filePath);
    if (s.fileStart > 1) p.set("line", String(s.fileStart));
    lensToUrl(p, s.lensFilter);
  } else if (s.appMode === "review") {
    p.set("app", "review");
    if (s.reviewBase) p.set("base", s.reviewBase);
    if (s.reviewHead) p.set("head", s.reviewHead);
    if (s.reviewFile) p.set("file", s.reviewFile);
  } else if (s.appMode === "impact") {
    p.set("app", "impact");
    if (s.impactBase) p.set("ibase", s.impactBase);
    if (s.impactHead) p.set("ihead", s.impactHead);
    if (s.impactAsync) p.set("iasync", "1");
  } else if (s.appMode === "refs") {
    p.set("app", "refs");
  } else if (s.appMode === "hotspots") {
    p.set("app", "hotspots");
    if (s.hotspotSort !== "density") p.set("sort", s.hotspotSort);
    if (s.hotspotTop !== 50) p.set("top", String(s.hotspotTop));
    if (s.hotspotNoLambdas) p.set("noLambdas", "1");
    if (s.hotspotIntrinsic) p.set("hintrinsic", "1");
    if (s.compareA) p.set("a", s.compareA);
    if (s.compareB) p.set("b", s.compareB);
  }
  // Preserve whatever state is already attached to the CURRENT history entry (a pivot crumb — see main.js's
  // pushState/popstate wiring) — this call's job is keeping the URL text in sync, not managing history state.
  // Hardcoding `null` here would silently wipe a crumb off the active entry the moment any OTHER query field
  // changes (e.g. a composite pivot's own `set()` firing before its `recordCrumb` gets to `pushState`),
  // permanently breaking back/forward into that entry.
  history.replaceState(
    history.state,
    "",
    location.pathname + (p.toString() ? "?" + p : ""),
  );
}

// URL -> a query-state patch. A persisted ?store= that no longer exists falls back to LATEST (null) silently.
// `search` is captured at boot BEFORE the serialize-watch runs (which would otherwise wipe the query first).
export function readUrl(runs, search = location.search) {
  const p = new URLSearchParams(search);
  const s = p.get("store");
  const mode = p.get("mode") || "none";
  const tokens = (p.get("tokens") || "").split(",").filter(Boolean);
  const onlyNamesIntrinsic =
    mode === "only" && tokens.some((t) => ["alloc", "throw"].includes(t.split(":", 1)[0].toLowerCase()));
  const requestedTop = Number.parseInt(p.get("top") || "50", 10);
  const requestedHotspotSort = p.get("sort") || "density";
  const hotspotSorts = ["callers", "callees", "effects", "density", "hazards", "amplification", "dispatch"];
  return {
    from: p.get("from") || "",
    storeId: s && runs.some((r) => r.storeId === s) ? s : null,
    view: p.get("view") || "paths",
    mode,
    tokens,
    asyncWalk: p.get("async") === "1",
    intrinsic: p.get("intrinsic") === "1" || onlyNamesIntrinsic,
    collapse: p.get("collapse") || "",
    signatures: p.get("sig") === "1",
    predicates: p.get("pred") === "1",
    hazards: p.get("haz") === "1",
    appMode:
      p.get("app") === "file"
        ? "file"
        : p.get("app") === "review"
          ? "review"
          : p.get("app") === "impact"
            ? "impact"
            : p.get("app") === "refs"
              ? "refs"
              : p.get("app") === "hotspots"
                ? "hotspots"
                : "tree",
    filePath: p.get("file") || "",
    fileStart: Math.max(1, Number.parseInt(p.get("line") || "1", 10) || 1),
    reviewBase: runs.some((r) => r.storeId === p.get("base")) ? p.get("base") : "",
    reviewHead: runs.some((r) => r.storeId === p.get("head")) ? p.get("head") : "",
    reviewFile: p.get("file") || "",
    lensFilter: lensFromUrl(p),
    impactBase: runs.some((r) => r.storeId === p.get("ibase"))
      ? p.get("ibase")
      : "",
    impactHead: runs.some((r) => r.storeId === p.get("ihead"))
      ? p.get("ihead")
      : "",
    impactAsync: p.get("iasync") === "1",
    hotspotSort: hotspotSorts.includes(requestedHotspotSort) ? requestedHotspotSort : "density",
    hotspotTop: Number.isFinite(requestedTop) ? Math.min(500, Math.max(1, requestedTop)) : 50,
    hotspotNoLambdas: p.get("noLambdas") === "1",
    // Keep the whole-store report preference independent of Tree's `intrinsic` flag. Sharing one URL key
    // made a refreshed Hotspots link silently enable alloc/throw when the user later switched to Tree.
    hotspotIntrinsic: p.get("hintrinsic") === "1",
    compareA: p.get("a") || "",
    compareB: p.get("b") || "",
  };
}
