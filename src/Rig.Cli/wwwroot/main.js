// Controller / wiring: builds the Shell, mounts it, defines the actions (which call api + set state), and
// subscribes region re-renders to state slices. Preferences (theme, rail) and the transient search dropdown /
// status / busy live here as direct DOM (not app state). This is the only file that glues view↔state↔io.

import { h, mount, watch } from "./lib.js";
import { api, setCacheVersion, purgeCache } from "./api.js";
import { setReviewFolderSearch, toggleReviewFolder } from "./review-tree.js";
import {
  store,
  get,
  set,
  activeStoreId,
  querySlice,
  serializeUrl,
  readUrl,
  pushCrumb,
} from "./store.js";
import {
  Shell,
  RunsList,
  EpList,
  TreeView,
  CallersPanel,
  ImpactView,
  ImpactProgress,
  RefsView,
  HotspotsView,
  Chips,
  treeStatus,
  baseName,
  BreadcrumbTrail,
  shortLabel,
  ReviewFileList,
  visibleReviewFiles,
} from "./components.js";
import {
  FileEffectsView,
  setFileFindings,
  lensFilterDefaults,
  serverFilterChanged,
  navTargets,
} from "./filelens.js";

const explicit = () => get().storeId; // the id to put on URLs (null => LATEST)
const resolved = () => activeStoreId(); // the resolved id (for cache keys)
const namesIntrinsicToken = (token) => ["alloc", "throw"].includes(token.split(":", 1)[0].toLowerCase());

// ---- status + busy (transient DOM) ----------------------------------------------------------------------
let refs;
function status(msg, err = false) {
  refs.status.textContent = msg;
  refs.status.className = err ? "err" : "";
}
function setBusy(on) {
  refs.statusbar.classList.toggle("busy", on);
  refs.tree.classList.toggle("busy", on);
  refs.impact.classList.toggle("busy", on);
  refs.hotspots.classList.toggle("busy", on);
  refs.file.classList.toggle("busy", on);
  refs.reviewWrap.classList.toggle("busy", on);
  refs.go.disabled = on;
  refs.impactGo.disabled = on;
  refs.hotspotGo.disabled = on;
  refs.fileGo.disabled = on;
  refs.reviewGo.disabled = on;
}

// ---- data actions ---------------------------------------------------------------------------------------
// `recordHistory=false` is the escape hatch used by restoreCrumb (popstate / a breadcrumb click) and by
// openDiffTree (which records its OWN crumb before delegating here) — see the "pivot history" section below.
async function openTree(pattern, { recordHistory = true } = {}) {
  if (!pattern) {
    status("enter a pattern", true);
    return;
  }
  hideResults();
  // Keep the (uncontrolled) search box in sync with programmatic navigations — re-root, a drawer EP click,
  // an impact cross-link — not just typed queries.
  if (refs.from) refs.from.value = pattern;
  // drop a stale diff overlay when navigating to a DIFFERENT EP (openDiffTree sets it for the same pattern
  // right before calling here, so that case is preserved).
  if (get().diffOverlay && get().diffOverlay.from !== pattern)
    set({ diffOverlay: null });
  // A crumb marks a genuine RE-root: going from an already-shown tree to a DIFFERENT pattern. The very first
  // tree of a session (boot deep-link or the first search) has nothing to go "back" to, so it's not a pivot;
  // re-fetches of the SAME pattern (toggling async/raw) aren't a pivot either.
  const prevTreeFrom = get().treeFrom;
  const isNewRoot = recordHistory && prevTreeFrom !== "" && pattern !== prevTreeFrom;
  set({ from: pattern });
  setBusy(true);
  status("querying…");
  try {
    const data = await api.tree(
      resolved(),
      explicit(),
      pattern,
      get().asyncWalk,
      get().rawTree,
      get().intrinsic,
    );
    if (!data.matched) {
      set({ tree: null, treeFrom: "" });
      status(`no symbol matches '${pattern}'`, true);
      return;
    }
    set({ tree: data, treeFrom: pattern, hazardMarks: null });
    if (get().hazards) loadHazards();
    if (isNewRoot)
      recordCrumb("tree", shortLabel(pattern), {
        from: pattern,
        diffOverlay: get().diffOverlay,
        callers: null,
      });
  } catch (e) {
    status(e.message, true);
  } finally {
    setBusy(false);
  }
}
async function loadEntrypoints() {
  try {
    set({ eps: await api.entrypoints(resolved(), explicit()) });
  } catch (e) {
    refs.eps.textContent = "error: " + e.message;
  }
}

// THE LENS SHOWS THE WHOLE FILE. `/api/file-source` caps one response at 400 lines
// (SourceRenderer.DefaultMaxLines), so the client stitches the pages back together instead of making the
// reader page: a 2,400-line controller arrives as six requests and renders as one continuous document.
// Paging was never a reader's idea of a unit — it split methods in half, scoped the outline to whichever
// 400 lines happened to be loaded, and made "is there anything worse further down?" a navigation problem.
const SOURCE_CHUNK = 400;
const SOURCE_BATCH = 6; // chunks requested in parallel per round-trip wave
const SOURCE_MAX_LINES = 40000; // a hard stop, disclosed in the UI if it ever bites

async function loadWholeSource(file) {
  const first = await api.fileSource(explicit(), file, 1, SOURCE_CHUNK);
  if (!first || first.origin === "unavailable" || !first.lines.length) return first;
  const lines = [...first.lines];
  let more = first.hasMore;
  while (more && lines.length < SOURCE_MAX_LINES) {
    const before = lines.length;
    const next = lines[lines.length - 1].number + 1;
    // Speculative parallel wave: the total length is unknown up front, so ask for several chunks at once and
    // stop at the first short one. Six waves cover a 14k-line file in two round trips.
    const wave = await Promise.all(
      Array.from({ length: SOURCE_BATCH }, (_, i) => api.fileSource(explicit(), file, next + i * SOURCE_CHUNK, SOURCE_CHUNK)),
    );
    more = false;
    for (const page of wave) {
      if (!page || !page.lines.length) break;
      const fresh = page.lines.filter((l) => l.number > lines[lines.length - 1].number);
      if (!fresh.length) break;
      lines.push(...fresh);
      more = page.hasMore;
      if (!more) break;
    }
    // Past EOF the endpoint answers origin="unavailable" with no lines, so a wave that adds nothing is the
    // real end of the file whatever `hasMore` last claimed. Without this the loop could spin to the cap.
    if (lines.length === before) {
      more = false;
      break;
    }
  }
  return {
    ...first,
    startLine: lines[0].number,
    endLine: lines[lines.length - 1].number,
    lines,
    hasPrevious: false,
    hasMore: more,
    truncatedAt: more ? SOURCE_MAX_LINES : 0,
  };
}
// `line` is now a SCROLL TARGET, not a page start — the whole file is loaded either way.
async function openFile(file, line = 1) {
  if (!file) {
    status("choose an indexed file", true);
    return;
  }
  const focus = Math.max(1, Number.parseInt(line, 10) || 1);
  set({ appMode: "file", filePath: file, fileStart: focus, fileSource: null, fileError: "", fileFocusLine: focus > 1 ? focus : 0 });
  if (refs.fileQuery) refs.fileQuery.value = file;
  setBusy(true);
  status("building file effect lens…");
  try {
    // Tiers 1-3 come from their own endpoint and their own derivation, fetched in PARALLEL: the badges and the
    // source must not wait on the hazard/amplification pass, and a findings failure must not cost the reader
    // the lens. Hence `.catch(null)` — no findings marks is a degraded view; no view is a broken one.
    const [fileEffects, fileSource, findings] = await Promise.all([
      api.fileEffects(resolved(), explicit(), file),
      loadWholeSource(file),
      api.fileFindings(resolved(), explicit(), file).catch(() => null),
    ]);
    setFileFindings(findings);
    set({ fileEffects, fileSource, fileStart: focus, fileError: "" });
    status(
      `${baseName(file)} · ${fileSource.lines.length} lines · ${fileEffects.methods.length} effectful methods · ${fileEffects.sites.length} marked calls`,
    );
    if (focus > 1) scrollToLine(focus);
  } catch (e) {
    set({ fileEffects: null, fileSource: null, fileError: e.message });
    status("file: " + e.message, true);
  } finally {
    setBusy(false);
  }
}

async function openFileQuery(value) {
  const query = value.trim();
  if (!query) {
    status("enter a file name or path", true);
    return;
  }
  setBusy(true);
  status("finding indexed file…");
  try {
    const result = await api.files(explicit(), query, 50);
    const exact = result.files.find(
      (file) => file.path === query || file.name.toLowerCase() === query.toLowerCase(),
    );
    const selected = exact || result.files[0];
    if (!selected) {
      status(`no indexed file matches '${query}'`, true);
      return;
    }
    await openFile(selected.path, 1);
  } catch (e) {
    status("files: " + e.message, true);
  } finally {
    setBusy(false);
  }
}

// Centre a source line in the editor and flash it. The row carries data-line, so this needs no index.
function scrollToLine(line) {
  requestAnimationFrame(() => {
    const row = refs.file.querySelector(`.file-code-row[data-line="${line}"]`);
    if (row) row.scrollIntoView({ block: "center", behavior: "smooth" });
  });
}

// Keyboard navigation over the marks. `n`/`p` step between lines that DO I/O (or carry a finding) and
// `N`/`P` between findings only — the two questions a reader actually has on a 133-marked-line file, where
// scrolling past 96 dispatch-only guesses to find the six real writes is the failure mode. Both respect the
// active filter, so a key never lands on something the page is not showing.
function lensJump(kind, direction) {
  const s = get();
  if (s.appMode !== "file" || !s.fileEffects) return;
  const targets = navTargets(s.fileEffects, s.lensFilter, kind);
  if (!targets.length) {
    status(kind === "finding" ? "no findings in this file match the filter" : "no I/O lines match the filter", true);
    return;
  }
  const from = s.fileFocusLine || (s.fileSource ? s.fileSource.startLine : 1);
  const next =
    direction > 0
      ? targets.find((l) => l > from) ?? targets[0]
      : [...targets].reverse().find((l) => l < from) ?? targets[targets.length - 1];
  actions.gotoFileLine(next);
  status(`${kind === "finding" ? "finding" : "I/O"} line ${next} · ${targets.indexOf(next) + 1}/${targets.length}`);
}

// The minimap's viewport rectangle, driven from ONE delegated scroll handler registered at boot — the view
// re-renders on every filter change, so a per-render listener would either leak or be lost.
function syncMinimap() {
  const scroller = refs.file;
  const map = scroller.querySelector(".mm");
  const view = scroller.querySelector(".mm-view");
  const editor = scroller.querySelector(".file-editor");
  if (!map || !view || !editor) return;
  const total = editor.scrollHeight || 1;
  view.style.top = `${(scroller.scrollTop / total) * 100}%`;
  view.style.height = `${Math.max(1.5, (scroller.clientHeight / total) * 100)}%`;
}

function setupLensKeys() {
  refs.file.addEventListener("scroll", () => requestAnimationFrame(syncMinimap), { passive: true });
  window.addEventListener("resize", () => requestAnimationFrame(syncMinimap));
  document.addEventListener("keydown", (event) => {
    if (get().appMode !== "file") return;
    const tag = (event.target.tagName || "").toLowerCase();
    if (tag === "input" || tag === "textarea" || tag === "select" || event.metaKey || event.ctrlKey || event.altKey) return;
    if (event.key === "n") lensJump("io", 1);
    else if (event.key === "p") lensJump("io", -1);
    else if (event.key === "N") lensJump("finding", 1);
    else if (event.key === "P") lensJump("finding", -1);
    else if (event.key === "l") actions.toggleLensLegend();
    else return;
    event.preventDefault();
  });
}

function reviewViewedStorageKey(base, head) {
  return `rig-review-viewed:${encodeURIComponent(base)}:${encodeURIComponent(head)}`;
}

function loadReviewViewed(base, head) {
  if (!base || !head) return [];
  try {
    const value = JSON.parse(localStorage.getItem(reviewViewedStorageKey(base, head)) || "[]");
    return Array.isArray(value) ? value.filter((item) => typeof item === "string") : [];
  } catch {
    return [];
  }
}

function persistReviewViewed(base, head, viewed) {
  if (!base || !head) return;
  localStorage.setItem(reviewViewedStorageKey(base, head), JSON.stringify(viewed));
}

function setupReviewKeys() {
  document.addEventListener("keydown", (event) => {
    if (get().appMode !== "review") return;
    const tag = (event.target.tagName || "").toLowerCase();
    const typing = tag === "input" || tag === "textarea" || tag === "select";
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "p") {
      event.preventDefault();
      refs.reviewFiles.querySelector(".review-file-search")?.focus();
      return;
    }
    if (!typing && event.key === "/") {
      event.preventDefault();
      refs.reviewFiles.querySelector(".review-file-search")?.focus();
      return;
    }
    if (typing || event.metaKey || event.ctrlKey || event.altKey) return;
    if (event.key === "j" || event.key === "k") {
      event.preventDefault();
      actions.moveReviewFile(event.key === "j" ? 1 : -1);
    } else if (event.key.toLowerCase() === "v") {
      event.preventDefault();
      actions.setCurrentReviewViewed(!get().reviewViewed.includes(get().reviewFile));
    }
  });
}

let fileDiffModule;
let reviewRequestId = 0;
let reviewFilesRequestId = 0;
async function mountFileDiff(data) {
  fileDiffModule ||= await import("./assets/file-diff.js");
  fileDiffModule.mountFileDiff(refs.review, data, {
    onOpenTree: (id) => actions.openFileTree(id),
    focusLine: get().reviewLine > 0
      ? { side: get().reviewSide === "base" ? "old" : "new", line: get().reviewLine }
      : null,
    ignoreWhitespace: get().reviewIgnoreWhitespace,
    viewed: get().reviewViewed.includes(get().reviewFile),
    onViewedChange: (value) => actions.setCurrentReviewViewed(value),
    onIgnoreWhitespaceChange: (value) => {
      if (value === get().reviewIgnoreWhitespace) return;
      set({ reviewIgnoreWhitespace: value, reviewError: "" });
      loadFileDiff();
    },
  });
}

function reviewDefaults(file = "") {
  const s = get();
  const head = s.reviewHead || activeStoreId(s) || "";
  const base =
    s.reviewBase ||
    (s.runs.find((run) => run.storeId !== head) || {}).storeId ||
    "";
  return { reviewBase: base, reviewHead: head, reviewFile: file || s.reviewFile };
}

async function loadReviewFiles({ openFirst = false } = {}) {
  const { reviewBase: base, reviewHead: head } = get();
  if (!base || !head || base === head) {
    set({ reviewFiles: null, reviewFilesError: "" });
    return null;
  }

  const requestId = ++reviewFilesRequestId;
  set({ reviewFiles: null, reviewFilesError: "" });
  status("loading changed files…");
  try {
    const data = await api.reviewFiles(base, head);
    if (requestId !== reviewFilesRequestId) return null;
    set({ reviewFiles: data, reviewFilesError: "" });
    const first = data.files[0];
    if (openFirst && !get().reviewFile && first) {
      set({ reviewLine: 0, reviewSide: "head" });
      await loadFileDiff(first.path);
    }
    else status(`${data.files.length} changed files · ${data.files.filter((file) => file.semanticReady).length} Semantic-ready`);
    return data;
  } catch (error) {
    if (requestId !== reviewFilesRequestId) return null;
    set({ reviewFiles: null, reviewFilesError: error.message });
    status("review files: " + error.message, true);
    return null;
  }
}

async function loadFileDiff(file = get().reviewFile) {
  const { reviewBase: base, reviewHead: head } = get();
  if (!base || !head) {
    status("pick a base and a head store", true);
    return;
  }
  if (base === head) {
    status("base and head are the same store", true);
    return;
  }
  if (!file) {
    status("choose an indexed file", true);
    return;
  }

  const requestId = ++reviewRequestId;
  // Keep the mounted island while the next patch loads. Besides avoiding a blank flash, this preserves the
  // reader's one/two-column choice across file navigation and whitespace re-diffs.
  set({ appMode: "review", reviewFile: file, reviewError: "" });
  refs.reviewFile.value = file;
  setBusy(true);
  status("building file diff…");
  try {
    const data = await api.fileDiff(base, head, file, get().reviewIgnoreWhitespace);
    if (requestId !== reviewRequestId) return;
    const identity = data.relativePath || data.file;
    set({ reviewFile: identity, reviewData: data, reviewError: "" });
    refs.reviewFile.value = identity;
    setBusy(false);
    const markCount = (side) => side.effects?.sites?.length || 0;
    const available = (side) => side.semanticState === "available" && side.file;
    const semanticStatus = `${available(data.base) ? `${markCount(data.base)} base marks` : data.base.semanticState} · ${available(data.head) ? `${markCount(data.head)} head marks` : data.head.semanticState}`;
    status(`${baseName(identity)} · ${data.status} · ${semanticStatus}`);
    // Findings are meaningful only when that revision has an indexed physical file. Missing/text-only sides
    // resolve immediately to null, so the renderer never advertises a tier request that cannot complete.
    const findingsTask = Promise.all([
      available(data.base) ? api.fileFindings(base, base, data.base.file).catch(() => null) : Promise.resolve(null),
      available(data.head) ? api.fileFindings(head, head, data.head.file).catch(() => null) : Promise.resolve(null),
    ]);
    const [baseFindings, headFindings] = await findingsTask;
    if (requestId !== reviewRequestId) return;
    set({
      reviewData: {
        ...data,
        base: { ...data.base, findings: baseFindings },
        head: { ...data.head, findings: headFindings },
      },
    });
    const findingCount = (side) =>
      side ? side.hazards.length + side.amplifications.length + side.anchors.length : 0;
    const findingsStatus = available(data.base) && available(data.head)
      ? `${findingCount(baseFindings)}/${findingCount(headFindings)} findings`
      : available(data.base)
        ? `${findingCount(baseFindings)} base findings`
        : available(data.head)
          ? `${findingCount(headFindings)} head findings`
          : "";
    status(`${baseName(identity)} · ${data.status} · ${semanticStatus}${findingsStatus ? ` · ${findingsStatus}` : ""}`);
  } catch (error) {
    if (requestId !== reviewRequestId) return;
    set({ reviewData: null, reviewError: error.message });
    status("review: " + error.message, true);
  } finally {
    if (requestId === reviewRequestId) setBusy(false);
  }
}

async function openReviewQuery(value) {
  const query = value.trim();
  if (!query) {
    status("enter a file name or path", true);
    return;
  }
  const { reviewBase: base, reviewHead: head } = get();
  if (!base || !head) {
    status("pick a base and head store first", true);
    return;
  }
  setBusy(true);
  status("finding changed file…");
  try {
    const inventory = get().reviewFiles || await loadReviewFiles();
    if (!inventory) return;
    const normalized = query.replaceAll("\\", "/").toLowerCase();
    const basename = (value) => (value || "").replaceAll("\\", "/").split("/").pop().toLowerCase();
    const exact = inventory.files.find(
      (file) => [file.path, file.oldPath, file.newPath, file.oldFile, file.newFile]
        .filter(Boolean)
        .some((candidate) => candidate.replaceAll("\\", "/").toLowerCase() === normalized),
    );
    const selected = exact || inventory.files.find(
      (file) => [file.path, file.oldPath, file.newPath].some((candidate) => basename(candidate) === query.toLowerCase()),
    );
    if (!selected) {
      status(`no changed file matches '${query}'`, true);
      return;
    }
    set({ reviewLine: 0, reviewSide: "head" });
    await loadFileDiff(selected.path);
  } catch (error) {
    status("review files: " + error.message, true);
  } finally {
    setBusy(false);
  }
}

function openReviewFile(file) {
  const patch = reviewDefaults(file);
  set({
    appMode: "review",
    ...patch,
    reviewLine: 0,
    reviewSide: "head",
    reviewViewed: loadReviewViewed(patch.reviewBase, patch.reviewHead),
  });
  refs.reviewBase.value = patch.reviewBase;
  refs.reviewHead.value = patch.reviewHead;
  refs.reviewFile.value = patch.reviewFile;
  if (patch.reviewBase && patch.reviewHead) {
    loadReviewFiles();
    loadFileDiff(file);
  }
}
async function loadHazards() {
  const s = get();
  if (!s.tree || !s.hazards) return;
  try {
    set({ hazardMarks: await api.hazards(resolved(), explicit(), s.treeFrom) });
  } catch (e) {
    status("hazards: " + e.message, true);
  }
}
function selectStore(id) {
  const latest = get().runs.find((r) => r.isLatest) || get().runs[0];
  set({
    storeId: latest && id === latest.storeId ? null : id,
    diffOverlay: null,
  }); // a manual store switch invalidates any diff overlay
  if (get().tab === "eps") loadEntrypoints();
  if (get().appMode === "tree" && get().treeFrom) openTree(get().treeFrom);
  // a store switch invalidates the refs report (it's per-store) — reload if that view is showing.
  if (get().appMode === "refs") {
    set({ refsUnused: null, refsUsage: null });
    loadRefs();
  }
  // Both hotspot artifacts and explicit comparisons are store-specific. Invalidate them even when another
  // app mode is visible, so returning to Hotspots cannot briefly show the previous store's report.
  set({ hotspotData: null, effectsDiffData: null });
  if (get().appMode === "hotspots") loadHotspots();
  if (get().appMode === "file" && get().filePath) openFile(get().filePath, get().fileStart);
}
function loadImpact() {
  const { impactBase, impactHead, impactAsync } = get();
  if (!impactBase || !impactHead) {
    status("pick a base and a head store", true);
    return;
  }
  if (impactBase === impactHead) {
    status("base and head are the same store", true);
    return;
  }
  setBusy(true);
  set({ impactReviewFiles: null });
  status("diffing…");
  // Stream live phase progress over SSE (the stream ALSO warms the disk cache); on `done`, GET the now-warm
  // /api/impact for the data. Warm cache → `cache hit` → `done` almost immediately. (Hacky but "not sad".)
  const log = [];
  mount(refs.impact, ImpactProgress(log));
  const es = new EventSource(
    `/api/impact/stream?base=${encodeURIComponent(impactBase)}&head=${encodeURIComponent(impactHead)}` +
      (impactAsync ? "&async=true" : ""),
  );
  let settled = false;
  const finish = (fn) => {
    if (settled) return;
    settled = true;
    es.close();
    setBusy(false);
    fn();
  };
  es.addEventListener("phase", (e) => {
    log.push(e.data);
    mount(refs.impact, ImpactProgress(log));
  });
  es.addEventListener("done", () =>
    finish(async () => {
      try {
        const impactData = await api.impact(impactBase, impactHead, impactAsync);
        set({ impactData });
        api
          .reviewFiles(impactBase, impactHead)
          .then((impactReviewFiles) => {
            const current = get();
            if (current.impactBase === impactBase && current.impactHead === impactHead)
              set({ impactReviewFiles });
          })
          .catch(() => {}); // Impact remains useful when Git attribution cannot produce review links.
        const d = get().impactData;
        status(
          `impact: ${d.perEp.length.toLocaleString()} behavioral change(s), +${d.addedEps.length}/−${d.removedEps.length} EPs`,
        );
      } catch (e) {
        status(e.message, true);
      }
    }),
  );
  es.addEventListener("failed", (e) =>
    finish(() => status("diff failed: " + e.data, true)),
  );
  es.onerror = () => finish(() => status("diff stream connection lost", true));
}

// Refs (assembly-reference analysis) — a GLOBAL report fetched like the EP inventory (store + optional
// filter, no from-pattern). Loads ONLY the active sub-tab's endpoint (unused rebuilds the .csproj dependency
// graph, so it isn't free); a tab switch / filter change reloads the tab that's now shown.
let refsFilterTimer = null; // debounce for the filter box (server-side filter → avoid a fetch per keystroke)
async function loadRefs() {
  const s = get();
  const filter = s.refsFilter.trim() || undefined;
  setBusy(true);
  status("loading references…");
  try {
    if (s.refsTab === "usage") {
      set({ refsUsage: await api.refsUsage(explicit(), filter) });
    } else {
      set({ refsUnused: await api.refsUnused(explicit(), filter) });
    }
    status("references loaded");
  } catch (e) {
    status("refs: " + e.message, true);
  } finally {
    setBusy(false);
  }
}

async function loadHotspots() {
  const s = get();
  setBusy(true);
  status("loading hotspots…");
  try {
    const data = await api.hotspots(
      resolved(),
      explicit(),
      s.hotspotSort,
      s.hotspotTop,
      s.hotspotNoLambdas,
      s.hotspotIntrinsic,
    );
    set({ hotspotData: data });
    status(`hotspots: ${data.rows.length.toLocaleString()} method(s), sort=${data.sort}`);
  } catch (e) {
    status("hotspots: " + e.message, true);
  } finally {
    setBusy(false);
  }
}

async function compareEffects(a, b) {
  set({ compareA: a, compareB: b, effectsDiffData: null });
  if (!a || !b) {
    status("comparison requires explicit A and B patterns", true);
    return;
  }
  setBusy(true);
  status("comparing behavior…");
  try {
    const data = await api.effectsDiff(resolved(), explicit(), a, b);
    set({ effectsDiffData: data });
    status(data.matched ? `behavior diff: ${data.aOnly.length} A-only, ${data.bOnly.length} B-only` : data.error || "comparison unresolved", !data.matched);
  } catch (e) {
    status("behavior diff: " + e.message, true);
  } finally {
    setBusy(false);
  }
}

// ---- pivot history (breadcrumbs) -------------------------------------------------------------------------
// Every pivot (re-root, drawer open, diff cross-link) pushes a crumb onto `history` AND mirrors it onto a
// real `history.pushState` entry, so the breadcrumb trail (BreadcrumbTrail, mounted into refs.crumbs) and the
// browser's own back/forward button are driven by the SAME mechanism — clicking a crumb just replays that
// many real back/forward steps (see actions.jumpToCrumb), and the popstate handler below does the one true
// restore. `diffOverlay`/`callers` default to what's already on the crumb; callers pass what they need.
function recordCrumb(kind, label, extra = {}) {
  const s = get();
  const crumb = {
    kind,
    label,
    from: s.treeFrom,
    appMode: s.appMode,
    storeId: s.storeId,
    diffOverlay: null,
    callers: null,
    ...extra,
  };
  const patch = pushCrumb(s, crumb);
  set(patch);
  history.pushState(
    { crumb, cursor: patch.historyCursor },
    "",
    location.pathname + location.search,
  );
}

// Restore app state from a crumb (breadcrumb click or a real browser back/forward). Every pivot action below
// takes a `{ recordHistory: false }` escape hatch specifically so THIS path never re-pushes a crumb.
async function restoreCrumb(crumb) {
  set({ appMode: crumb.appMode, storeId: crumb.storeId ?? null });
  if (crumb.kind === "tree") {
    set({ diffOverlay: crumb.diffOverlay || null, callers: null });
    if (crumb.from) await openTree(crumb.from, { recordHistory: false });
    return;
  }
  // drawer pivots (callers/reaches/path) don't change the tree — restore whatever tree was behind the drawer
  // when the crumb was recorded, then reopen the drawer itself.
  if (crumb.from && crumb.from !== get().treeFrom)
    await openTree(crumb.from, { recordHistory: false });
  if (crumb.kind === "callers")
    await actions.openCallers(
      { id: crumb.callers.target },
      crumb.callers.mode,
      crumb.callers.asyncWalk,
      { recordHistory: false },
    );
  else if (crumb.kind === "reaches")
    await actions.openReaches({ id: crumb.callers.target }, { recordHistory: false });
  else if (crumb.kind === "path")
    await actions.openPath(crumb.callers.from, crumb.callers.target, {
      recordHistory: false,
    });
}

// Real browser back/forward: pushState's attached state carries the crumb + its cursor position directly, so
// there's no need to re-derive anything from the URL.
window.addEventListener("popstate", (e) => {
  if (!e.state || !e.state.crumb) return;
  set({ historyCursor: e.state.cursor });
  restoreCrumb(e.state.crumb);
});

// ---- actions passed to components -----------------------------------------------------------------------
// Positioned context menu for a tree node — the reverse-nav entry point. Built as a transient body-level div
// (dismissed on click-away / Escape), so it escapes the tree's overflow clipping.
function showNodeMenu(node, e) {
  document.querySelectorAll(".node-menu").forEach((m) => m.remove());
  const item = (label, fn) =>
    h("button", { class: "node-menu-item", onClick: () => { menu.remove(); teardown(); fn(); } }, label);
  const menu = h(
    "div",
    { class: "node-menu" },
    item("Re-root here", () => openTree(node.id)),
    item("Entry points reaching this →", () => actions.openCallers(node, "entrypoints")),
    item("Who reaches this (roots)", () => actions.openCallers(node, "roots")),
    item("Effects reachable from here →", () => actions.openReaches(node)),
  );
  menu.style.left = Math.min(e.clientX, window.innerWidth - 240) + "px";
  menu.style.top = e.clientY + "px";
  document.body.appendChild(menu);
  const dismiss = (ev) => { if (!menu.contains(ev.target)) { menu.remove(); teardown(); } };
  const esc = (ev) => { if (ev.key === "Escape") { menu.remove(); teardown(); } };
  const teardown = () => { document.removeEventListener("mousedown", dismiss); document.removeEventListener("keydown", esc); };
  setTimeout(() => { document.addEventListener("mousedown", dismiss); document.addEventListener("keydown", esc); }, 0);
}

const actions = {
  setTheme: applyTheme,
  nodeMenu: showNodeMenu,
  async openCallers(node, mode, asyncWalk = false, { recordHistory = true } = {}) {
    const from = node.id;
    set({ callers: { target: from, mode, async: asyncWalk, matched: false, loading: true } });
    try {
      const data = await api.callers(resolved(), explicit(), from, mode, asyncWalk);
      set({ callers: { target: from, mode, async: asyncWalk, matched: data.matched, entryPoints: data.entryPoints, roots: data.roots } });
      if (recordHistory)
        recordCrumb(
          "callers",
          (mode === "entrypoints" ? "EPs: " : "callers: ") + shortLabel(from),
          { callers: { target: from, mode, asyncWalk } },
        );
    } catch (err) {
      status("callers: " + err.message, true);
      set({ callers: null });
    }
  },
  closeCallers() {
    set({ callers: null });
  },
  // Declaration source for one symbol id — backs the location-chip expander (components.js `Loc`). Returns
  // the raw DTO and stays OUT of app state: the panel is transient DOM owned by the node that opened it (same
  // treatment as the search dropdown), so expanding source never re-renders — or blanks — the tree.
  loadSource: (id) => api.source(explicit(), id),
  openFile,
  openFileQuery,
  openReviewFile,
  openReviewQuery,
  openReviewFileEntry(file) {
    set({ reviewLine: 0, reviewSide: "head" });
    loadFileDiff(file.path);
  },
  setReviewFileSearch(value) {
    set(setReviewFolderSearch(get(), value));
  },
  toggleReviewFolder(path) {
    set(toggleReviewFolder(get(), path));
  },
  setReviewFileFilter(value) {
    set({ reviewFileFilter: value });
  },
  setReviewFileMode(value) {
    if (value !== "list" && value !== "tree") return;
    localStorage.setItem("rig-review-file-mode", value);
    set({ reviewFileMode: value });
  },
  setCurrentReviewViewed(value) {
    const s = get();
    if (!s.reviewFile || !s.reviewBase || !s.reviewHead) return;
    const viewed = new Set(s.reviewViewed);
    if (value) viewed.add(s.reviewFile);
    else viewed.delete(s.reviewFile);
    const next = [...viewed];
    persistReviewViewed(s.reviewBase, s.reviewHead, next);
    set({ reviewViewed: next });
  },
  moveReviewFile(direction) {
    const s = get();
    const files = visibleReviewFiles(s);
    if (!files.length) {
      status("no changed files match this filter", true);
      return;
    }
    const current = files.findIndex((file) => file.path === s.reviewFile);
    const index = current < 0
      ? direction > 0 ? 0 : files.length - 1
      : (current + direction + files.length) % files.length;
    set({ reviewLine: 0, reviewSide: "head" });
    loadFileDiff(files[index].path);
  },
  // ---- file lens overlay controls ----
  // Filter changes are CLIENT-SIDE and instant; only `intrinsic`/`async` change what the server computed, so
  // only those refetch. That asymmetry is the whole reason the overlay is usable on a store whose cold
  // file-effects query costs ~50s: a reader tunes depth, basis and grain without ever waiting.
  setLensFilter(patch) {
    const before = get().lensFilter;
    const lensFilter = { ...before, ...patch };
    set({ lensFilter });
    if (serverFilterChanged(before, lensFilter) && get().filePath) {
      status("lens: server-side flag changed — refetching…");
      openFile(get().filePath, get().fileStart);
    }
  },
  resetLensFilter() {
    set({ lensFilter: { ...lensFilterDefaults(), outlineSort: get().lensFilter.outlineSort } });
  },
  toggleLensLegend() {
    const open = !get().lensLegend;
    set({ lensLegend: open });
    localStorage.setItem("rig-lens-legend", open ? "1" : "0");
  },
  // Every line is in the DOM, so a jump is always a scroll — the minimap, the outline and `n`/`p` all land
  // without a refetch.
  gotoFileLine(line) {
    set({ fileFocusLine: line });
    scrollToLine(line);
  },
  openFileTree(id) {
    set({ appMode: "tree", from: id });
    refs.from.value = id;
    openTree(id);
  },
  async openReaches(node, { recordHistory = true } = {}) {
    const from = node.id;
    set({ callers: { target: from, mode: "reaches", loading: true } });
    try {
      const data = await api.reaches(resolved(), explicit(), from, get().intrinsic);
      set({ callers: { target: from, mode: "reaches", matched: data.matched, reachableCount: data.reachableCount, effects: data.effects, intrinsicHidden: data.intrinsicHidden } });
      if (recordHistory)
        recordCrumb("reaches", "reaches: " + shortLabel(from), {
          callers: { target: from },
        });
    } catch (err) {
      status("reaches: " + err.message, true);
      set({ callers: null });
    }
  },
  async openPath(fromFqn, targetId, { recordHistory = true } = {}) {
    set({ callers: { target: targetId, from: fromFqn, mode: "path", loading: true } });
    try {
      const data = await api.path(resolved(), explicit(), fromFqn, targetId, get().intrinsic);
      set({ callers: { target: targetId, from: fromFqn, mode: "path", matched: data.matched, nodes: data.nodes, intrinsicHidden: data.intrinsicHidden } });
      if (recordHistory)
        recordCrumb("path", "path → " + shortLabel(targetId), {
          callers: { from: fromFqn, target: targetId },
        });
    } catch (err) {
      status("path: " + err.message, true);
      set({ callers: null });
    }
  },
  setTab(id) {
    set({ tab: id });
    if (id === "eps" && !get().eps.length) loadEntrypoints();
  },
  setEpFilter(v) {
    set({ epFilter: v });
  },
  selectStore,
  openTree,
  setView(v) {
    set({ view: v });
  },
  setMode(v) {
    set({ mode: v });
    if (v === "only" && get().tokens.some(namesIntrinsicToken) && !get().intrinsic) {
      set({ intrinsic: true });
      if (get().treeFrom) openTree(get().treeFrom);
    }
  },
  setCollapse(v) {
    set({ collapse: v });
  },
  toggleToken(t) {
    set((s) => ({
      tokens: s.tokens.includes(t)
        ? s.tokens.filter((x) => x !== t)
        : [...s.tokens, t],
    }));
    if (get().mode === "only" && namesIntrinsicToken(t) && get().tokens.includes(t) && !get().intrinsic) {
      set({ intrinsic: true });
      if (get().treeFrom) openTree(get().treeFrom);
    }
  },
  renderMsList,
  setFlag(key, val) {
    set({ [key]: val });
    if (key === "asyncWalk" && get().treeFrom) openTree(get().treeFrom); // async changes the fetched tree
    if (key === "rawTree" && get().treeFrom) openTree(get().treeFrom); // raw/folded changes the fetched tree
    if (key === "intrinsic") {
      if (get().treeFrom) openTree(get().treeFrom); // intrinsic changes server effect selection
      const c = get().callers;
      if (c?.mode === "reaches") actions.openReaches({ id: c.target }, { recordHistory: false });
      if (c?.mode === "path") actions.openPath(c.from, c.target, { recordHistory: false });
    }
    if (key === "impactAsync" && get().impactBase && get().impactHead)
      loadImpact(); // async changes the diff
    if (key === "hazards" && val) loadHazards();
  },
  async purge() {
    await purgeCache();
    status("cache purged — refetching…");
    if (get().tab === "eps") {
      set({ eps: [] });
      loadEntrypoints();
    }
    if (get().appMode === "hotspots") {
      set({ hotspotData: null, effectsDiffData: null });
      loadHotspots();
    } else if (get().appMode === "file" && get().filePath) {
      set({ fileEffects: null, fileSource: null, fileError: "" });
      openFile(get().filePath, get().fileStart);
    } else if (get().appMode === "review") {
      const hadFile = get().reviewFile;
      set({ reviewFiles: null, reviewFilesError: "", reviewData: null, reviewError: "" });
      loadReviewFiles({ openFirst: !hadFile });
      if (hadFile) loadFileDiff();
    } else if (get().treeFrom) openTree(get().treeFrom);
    else status("cache purged");
  },
  // impact mode
  setAppMode(m) {
    set({ appMode: m });
    // refs is a global report — load it on first entry (like the EP inventory loads on its tab).
    if (m === "refs" && !get().refsUnused && !get().refsUsage) loadRefs();
    if (m === "hotspots" && !get().hotspotData) loadHotspots();
    if (m === "file" && get().filePath && (!get().fileEffects || !get().fileSource))
      openFile(get().filePath, get().fileStart);
    if (m === "review") {
      const patch = reviewDefaults();
      set({ ...patch, reviewViewed: loadReviewViewed(patch.reviewBase, patch.reviewHead) });
      refs.reviewBase.value = patch.reviewBase;
      refs.reviewHead.value = patch.reviewHead;
      refs.reviewFile.value = patch.reviewFile;
      if (!get().reviewFiles) loadReviewFiles({ openFirst: !patch.reviewFile });
      if (patch.reviewFile && !get().reviewData) loadFileDiff(patch.reviewFile);
    }
  },
  loadHotspots,
  setHotspotSort(sort, reload) {
    set({ hotspotSort: sort });
    if (refs.hotspotSort) refs.hotspotSort.value = sort;
    if (reload) loadHotspots();
  },
  setHotspotTop(value) {
    const parsed = Number.parseInt(value, 10);
    set({ hotspotTop: Number.isFinite(parsed) ? parsed : 50 });
  },
  setComparePattern(side, value) {
    set(side === "a" ? { compareA: value } : { compareB: value });
  },
  compareEffects,
  openHotspotTree(row) {
    set({ appMode: "tree", from: row.id });
    refs.from.value = row.id;
    openTree(row.id);
  },
  setRefsTab(id) {
    set({ refsTab: id });
    // lazy: fetch the sub-tab's endpoint only when first shown (unused rebuilds the .csproj graph).
    if (id === "usage" ? !get().refsUsage : !get().refsUnused) loadRefs();
  },
  setRefsFilter(v) {
    // Wire the box to the server-side `filter` param (matches the CLI's optional pattern). Debounced, and
    // both sub-tabs' data is invalidated so a tab switch re-applies the current filter.
    set({ refsFilter: v, refsUnused: null, refsUsage: null });
    clearTimeout(refsFilterTimer);
    refsFilterTimer = setTimeout(loadRefs, 300);
  },
  setImpactStore(which, id) {
    set({
      ...(which === "base" ? { impactBase: id } : { impactHead: id }),
      impactData: null,
      impactReviewFiles: null,
    });
  },
  setReviewStore(which, id) {
    const current = get();
    const reviewBase = which === "base" ? id : current.reviewBase;
    const reviewHead = which === "head" ? id : current.reviewHead;
    set({
      reviewBase,
      reviewHead,
      reviewFile: "",
      reviewLine: 0,
      reviewSide: "head",
      reviewFiles: null,
      reviewFilesError: "",
      reviewData: null,
      reviewError: "",
      reviewViewed: loadReviewViewed(reviewBase, reviewHead),
    });
    refs.reviewFile.value = "";
    loadReviewFiles({ openFirst: true });
  },
  setImpactFilter(v) {
    set({ impactFilter: v });
  },
  loadImpact,
  // cross-link: an impact EP card → open that EP's HEAD tree with the diff overlaid (added/removed effects'
  // enclosing methods highlighted). Uses the impact head store + the EP delta already loaded client-side.
  openDiffTree(p) {
    const enc = (arr) => [...new Set(arr.map((e) => e.enclosing))];
    const base = get().impactData?.base?.label || "base";
    const head = get().impactData?.head?.label || "head";
    const overlay = {
      from: p.fqn,
      base,
      head,
      effAdded: enc(p.added),
      effRemoved: enc(p.removed),
      addedReach: [],
      removedReach: [],
      changedOnly: true,
    };
    // Set the overlay immediately (effect deltas — already loaded), open the head tree, then ENRICH with the
    // structural reach delta (added/removed reachable methods) fetched from /api/impact/reach (warm lookup).
    set({
      appMode: "tree",
      storeId: get().impactHead, // view the HEAD store's tree
      from: p.fqn,
      asyncWalk: get().impactAsync, // match the diff's traversal mode so the tree reaches what the diff diffed
      diffOverlay: overlay,
    });
    refs.from.value = p.fqn;
    refs.view.value = get().view;
    // This IS the pivot's crumb — the nested openTree below is told not to double-record it.
    recordCrumb("tree", "diff: " + shortLabel(p.fqn), {
      from: p.fqn,
      diffOverlay: overlay,
      callers: null,
    });
    openTree(p.fqn, { recordHistory: false });
    api
      .impactReach(
        get().impactBase,
        get().impactHead,
        p.kind,
        p.route,
        get().impactAsync,
      )
      .then((r) => {
        const ov = get().diffOverlay;
        if (ov && ov.from === p.fqn)
          set({
            diffOverlay: {
              ...ov,
              addedReach: r.added.map((n) => n.id),
              removedReach: r.removed,
            },
          });
      })
      .catch(() => {}); // structural enrichment is best-effort; the effect overlay still stands
  },
  toggleChangedOnly() {
    set((s) =>
      s.diffOverlay
        ? {
            diffOverlay: {
              ...s.diffOverlay,
              changedOnly: !s.diffOverlay.changedOnly,
            },
          }
        : {},
    );
  },
  clearDiff() {
    set({ diffOverlay: null });
  },
  // Breadcrumb click: replay the browser's OWN back/forward to that entry — popstate does the actual
  // restore (restoreCrumb), so the trail and the real history stack never diverge.
  jumpToCrumb(index) {
    const delta = index - get().historyCursor;
    if (delta !== 0) history.go(delta);
  },
};

// ---- the provider checklist (built imperatively into refs.msList; state = selectedTokens) ---------------
function renderMsList(filter = "") {
  const s = get();
  const f = filter.trim().toLowerCase();
  const toks = new Set(s.tokens);
  const items = [
    ...s.providers.providers.map((t) => [t, false]),
    ...s.providers.providerOps.map((t) => [t, true]),
  ].filter(([t]) => !f || t.includes(f));
  mount(
    refs.msList,
    items.map(([t, op]) =>
      h(
        "label",
        { class: "ms-opt" },
        h("input", {
          type: "checkbox",
          value: t,
          checked: toks.has(t),
          onChange: () => actions.toggleToken(t),
        }),
        " " + t,
        op ? h("span", { class: "ms-op" }, "op") : null,
      ),
    ),
  );
}

// ---- preferences (localStorage) -------------------------------------------------------------------------
function applyTheme(mode) {
  if (mode === "system") document.documentElement.removeAttribute("data-theme");
  else document.documentElement.setAttribute("data-theme", mode);
  localStorage.setItem("rig-theme", mode);
  for (const b of refs.theme.children)
    b.classList.toggle("on", b.dataset.theme === mode);
}
function initSplitter() {
  const saved = localStorage.getItem("rig-rail");
  if (saved) document.documentElement.style.setProperty("--rail", saved + "px");
  let dragging = false;
  refs.splitter.addEventListener("mousedown", () => {
    dragging = true;
    refs.splitter.classList.add("drag");
    document.body.style.userSelect = "none";
  });
  document.addEventListener("mousemove", (e) => {
    if (dragging)
      document.documentElement.style.setProperty(
        "--rail",
        Math.min(640, Math.max(180, e.clientX)) + "px",
      );
  });
  document.addEventListener("mouseup", () => {
    if (!dragging) return;
    dragging = false;
    refs.splitter.classList.remove("drag");
    document.body.style.userSelect = "";
    localStorage.setItem(
      "rig-rail",
      parseInt(
        getComputedStyle(document.documentElement).getPropertyValue("--rail"),
      ) || 300,
    );
  });
}

// ---- search dropdown (transient DOM under #from) --------------------------------------------------------
let searchTimer = null,
  activeHit = -1;
function hideResults() {
  refs.results.classList.remove("show");
  refs.results.replaceChildren();
  activeHit = -1;
}
async function doSearch(q) {
  try {
    const hits = await api.search(explicit(), q);
    if (!hits.length) {
      hideResults();
      return;
    }
    activeHit = -1;
    mount(
      refs.results,
      hits.map((hh, i) =>
        h(
          "div",
          {
            class: "hit",
            dataset: { id: hh.id, i },
            onMousedown: () => {
              refs.from.value = hh.id;
              hideResults();
              openTree(hh.id);
            },
          },
          h("span", { class: "hkind" }, hh.kind),
          " " + hh.name,
          h("span", { class: "hfile" }, `${baseName(hh.file)}:${hh.line}`),
        ),
      ),
    );
    refs.results.classList.add("show");
  } catch {
    hideResults();
  }
}
function setupSearch() {
  refs.from.addEventListener("input", () => {
    clearTimeout(searchTimer);
    const q = refs.from.value.trim();
    if (q.length < 2) {
      hideResults();
      return;
    }
    searchTimer = setTimeout(() => doSearch(q), 220);
  });
  refs.from.addEventListener("keydown", (e) => {
    const hits = [...refs.results.querySelectorAll(".hit")];
    if (refs.results.classList.contains("show") && hits.length) {
      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        activeHit =
          (activeHit + (e.key === "ArrowDown" ? 1 : hits.length - 1)) %
          hits.length;
        hits.forEach((hh, i) => hh.classList.toggle("active", i === activeHit));
        hits[activeHit].scrollIntoView({ block: "nearest" });
        return;
      }
      if (e.key === "Enter" && activeHit >= 0) {
        e.preventDefault();
        const id = hits[activeHit].dataset.id;
        refs.from.value = id;
        hideResults();
        openTree(id);
        return;
      }
      if (e.key === "Escape") {
        hideResults();
        return;
      }
    }
    if (e.key === "Enter") openTree(refs.from.value.trim());
  });
  document.addEventListener("click", (e) => {
    if (!e.target.closest(".fromwrap")) hideResults();
  });
  // multiselect popover open/close
  refs.chips.addEventListener("click", () => {
    if (!refs.ms.classList.contains("disabled"))
      refs.ms.classList.toggle("open");
  });
  document.addEventListener("click", (e) => {
    if (!e.target.closest(".ms")) refs.ms.classList.remove("open");
  });
}

// ---- indexed-file autocomplete ------------------------------------------------------------------------
let fileSearchTimer = null,
  activeFileHit = -1,
  fileSearchGeneration = 0;
function hideFileResults() {
  refs.fileResults.classList.remove("show");
  refs.fileResults.replaceChildren();
  activeFileHit = -1;
}
function chooseFileHit(hit) {
  refs.fileQuery.value = hit.path;
  hideFileResults();
  openFile(hit.path, 1);
}
async function doFileSearch(query, generation) {
  try {
    const result = await api.files(explicit(), query, 20);
    if (generation !== fileSearchGeneration) return;
    if (!result.files.length) {
      hideFileResults();
      return;
    }
    activeFileHit = -1;
    mount(
      refs.fileResults,
      result.files.map((file, index) =>
        h(
          "div",
          {
            class: "hit file-hit",
            dataset: { path: file.path, i: index },
            title: file.path,
            onMousedown: () => chooseFileHit(file),
          },
          h("span", { class: "file-hit-name" }, file.name),
          file.projects.length
            ? h("span", { class: "file-hit-projects" }, file.projects.join(", "))
            : null,
          h("span", { class: "hfile" }, file.path),
        ),
      ),
    );
    refs.fileResults.classList.add("show");
  } catch {
    if (generation === fileSearchGeneration) hideFileResults();
  }
}
function setupFileSearch() {
  refs.fileQuery.addEventListener("input", () => {
    clearTimeout(fileSearchTimer);
    const query = refs.fileQuery.value.trim();
    const generation = ++fileSearchGeneration;
    if (!query) {
      hideFileResults();
      return;
    }
    fileSearchTimer = setTimeout(() => doFileSearch(query, generation), 180);
  });
  refs.fileQuery.addEventListener("keydown", (event) => {
    const hits = [...refs.fileResults.querySelectorAll(".file-hit")];
    if (refs.fileResults.classList.contains("show") && hits.length) {
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault();
        activeFileHit =
          (activeFileHit + (event.key === "ArrowDown" ? 1 : hits.length - 1)) %
          hits.length;
        hits.forEach((hit, index) => hit.classList.toggle("active", index === activeFileHit));
        hits[activeFileHit].scrollIntoView({ block: "nearest" });
        return;
      }
      if (event.key === "Enter" && activeFileHit >= 0) {
        event.preventDefault();
        chooseFileHit({ path: hits[activeFileHit].dataset.path });
        return;
      }
      if (event.key === "Escape") {
        hideFileResults();
        return;
      }
    }
    if (event.key === "Enter") {
      event.preventDefault();
      openFileQuery(refs.fileQuery.value);
    }
  });
  document.addEventListener("click", (event) => {
    if (!event.target.closest(".file-search-wrap")) hideFileResults();
  });
}

// ---- impact mode: toggle Tree/Impact UI + populate the base/head store pickers --------------------------
function applyAppMode(m) {
  refs.root.classList.toggle("review-mode", m === "review");
  refs.treeToolbar.classList.toggle("hidden", m !== "tree");
  refs.tree.classList.toggle("hidden", m !== "tree");
  refs.fileToolbar.classList.toggle("hidden", m !== "file");
  refs.file.classList.toggle("hidden", m !== "file");
  refs.reviewToolbar.classList.toggle("hidden", m !== "review");
  refs.reviewWrap.classList.toggle("hidden", m !== "review");
  refs.impactToolbar.classList.toggle("hidden", m !== "impact");
  refs.impact.classList.toggle("hidden", m !== "impact");
  refs.refsToolbar.classList.toggle("hidden", m !== "refs");
  refs.refs.classList.toggle("hidden", m !== "refs");
  refs.hotspotToolbar.classList.toggle("hidden", m !== "hotspots");
  refs.hotspots.classList.toggle("hidden", m !== "hotspots");
  for (const b of refs.appmode.children)
    b.classList.toggle("on", b.dataset.app === m);
}
function populateImpactStores(s) {
  const opts = (ph) => [
    h("option", { value: "" }, ph),
    ...s.runs.map((r) =>
      h(
        "option",
        { value: r.storeId },
        `${r.storeId}${r.branch ? " · " + r.branch : ""}`,
      ),
    ),
  ];
  mount(refs.impactBase, opts("base…"));
  mount(refs.impactHead, opts("head…"));
  refs.impactBase.value = s.impactBase;
  refs.impactHead.value = s.impactHead;
  mount(refs.reviewBase, opts("base…"));
  mount(refs.reviewHead, opts("head…"));
  refs.reviewBase.value = s.reviewBase;
  refs.reviewHead.value = s.reviewHead;
}

// ---- sync uncontrolled inputs from state (once, after URL restore) --------------------------------------
function syncControls(s) {
  refs.from.value = s.from;
  refs.view.value = s.view;
  refs.filterMode.value = s.mode;
  refs.collapse.value = s.collapse;
  refs.async.querySelector("input").checked = s.asyncWalk;
  refs.raw.querySelector("input").checked = s.rawTree;
  refs.intrinsic.querySelector("input").checked = s.intrinsic;
  refs.sig.querySelector("input").checked = s.signatures;
  refs.pred.querySelector("input").checked = s.predicates;
  refs.haz.querySelector("input").checked = s.hazards;
  refs.ms.classList.toggle("disabled", s.mode === "none");
  refs.impactBase.value = s.impactBase;
  refs.impactHead.value = s.impactHead;
  refs.impactAsync.querySelector("input").checked = s.impactAsync;
  refs.impactFilter.value = s.impactFilter;
  refs.refsFilter.value = s.refsFilter;
  refs.hotspotSort.value = s.hotspotSort;
  refs.hotspotTop.value = String(s.hotspotTop);
  refs.hotspotNoLambdas.querySelector("input").checked = s.hotspotNoLambdas;
  refs.hotspotIntrinsic.querySelector("input").checked = s.hotspotIntrinsic;
  refs.fileQuery.value = s.filePath;
  refs.reviewBase.value = s.reviewBase;
  refs.reviewHead.value = s.reviewHead;
  refs.reviewFile.value = s.reviewFile;
  applyAppMode(s.appMode);
}

// ---- region subscriptions (re-render only the affected region when its slice changes) -------------------
function setupWatches() {
  watch(
    store,
    (s) => [s.runs, s.storeId],
    (s) => {
      mount(refs.runs, RunsList(s, actions));
      populateImpactStores(s);
      const latest = s.runs.find((r) => r.isLatest) || s.runs[0];
      const solPath = latest ? latest.solutionPath || "" : "";
      refs.storeDir.textContent = solPath;
      refs.storeDir.title = solPath; // full path on hover — the span ellipsis-truncates when narrow
    },
  );
  watch(
    store,
    (s) => [s.hotspotData, s.effectsDiffData, s.appMode],
    (s) => {
      if (s.appMode === "hotspots") mount(refs.hotspots, HotspotsView(s, actions));
    },
  );
  watch(
    store,
    (s) => [s.fileEffects, s.fileSource, s.filePath, s.fileError, s.appMode, s.lensFilter, s.lensLegend, s.fileFocusLine],
    (s) => {
      if (s.appMode === "file") {
        mount(refs.file, FileEffectsView(s, actions));
        requestAnimationFrame(syncMinimap);
      }
    },
  );
  watch(
    store,
    (s) => [s.reviewFiles, s.reviewFilesError, s.reviewBase, s.reviewHead, s.reviewFile, s.reviewFileSearch, s.reviewFileFilter, s.reviewFileMode, s.reviewFolderCollapse, s.reviewViewed, s.appMode],
    (s) => {
      if (s.appMode !== "review") return;
      const active = document.activeElement;
      const currentSearch = refs.reviewFiles.querySelector(".review-file-search");
      const searchFocused = active === currentSearch;
      const focusedFolder = refs.reviewFiles.contains(active) ? active?.getAttribute("data-review-folder") : null;
      const start = searchFocused ? currentSearch.selectionStart : null;
      const end = searchFocused ? currentSearch.selectionEnd : null;
      mount(refs.reviewFiles, ReviewFileList(s, actions));
      const nextSearch = refs.reviewFiles.querySelector(".review-file-search");
      // The query changes on every keystroke. Keep the actual input node alive while replacing the result
      // list, otherwise a native typing sequence loses its event target after the first character.
      if (currentSearch && nextSearch) {
        currentSearch.value = s.reviewFileSearch;
        nextSearch.replaceWith(currentSearch);
      }
      if (searchFocused && currentSearch) {
        currentSearch.focus();
        if (start != null && end != null) currentSearch.setSelectionRange(start, end);
      } else if (focusedFolder) {
        // Buttons are rebuilt with the result list too. Restore disclosure focus for repeated Enter/Space
        // toggles without scrolling the sidebar; do not steal focus from outside the file queue.
        [...refs.reviewFiles.querySelectorAll("[data-review-folder]")]
          .find((button) => button.getAttribute("data-review-folder") === focusedFolder)
          ?.focus({ preventScroll: true });
      }
    },
  );
  watch(
    store,
    (s) => [s.reviewData, s.reviewError, s.reviewViewed, s.reviewLine, s.reviewSide, s.appMode],
    (s) => {
      if (s.appMode !== "review") return;
      if (s.reviewError) {
        fileDiffModule?.unmountFileDiff(refs.review);
        refs.review.textContent = "Review unavailable: " + s.reviewError;
      } else if (s.reviewData) {
        mountFileDiff(s.reviewData).catch((error) => {
          fileDiffModule?.unmountFileDiff(refs.review);
          refs.review.textContent = "Diff renderer failed: " + error.message;
        });
      } else {
        fileDiffModule?.unmountFileDiff(refs.review);
        refs.review.textContent = "Choose two indexed revisions and a file.";
      }
    },
  );
  watch(
    store,
    (s) => [s.eps, s.epFilter],
    (s) => mount(refs.eps, EpList(s, actions)),
  );
  watch(
    store,
    (s) => [s.tab],
    (s) => {
      refs.tabRuns.classList.toggle("on", s.tab === "runs");
      refs.tabEps.classList.toggle("on", s.tab === "eps");
      refs.paneRuns.classList.toggle("on", s.tab === "runs");
      refs.paneEps.classList.toggle("on", s.tab === "eps");
    },
  );
  watch(
    store,
    (s) => [s.mode],
    (s) => refs.ms.classList.toggle("disabled", s.mode === "none"),
  );
  watch(
    store,
    (s) => [s.tokens.join(",")],
    (s) => {
      mount(refs.chips, Chips(s, actions));
      renderMsList(refs.msSearch.value);
    },
  );
  watch(
    store,
    (s) => [
      s.tree,
      s.view,
      s.mode,
      s.tokens.join(","),
      s.collapse,
      s.signatures,
      s.predicates,
      s.hazards,
      s.hazardMarks,
      s.diffOverlay,
    ],
    (s) => {
      mount(refs.tree, TreeView(s, actions));
      if (s.tree) status(treeStatus(s));
    },
  );
  watch(
    store,
    (s) => [s.callers],
    (s) => mount(refs.callers, CallersPanel(s, actions)),
  );
  watch(
    store,
    (s) => [s.history, s.historyCursor],
    (s) => mount(refs.crumbs, BreadcrumbTrail(s, actions)),
  );
  watch(
    store,
    (s) => [s.appMode],
    (s) => applyAppMode(s.appMode),
  );
  watch(
    store,
    (s) => [s.impactData, s.impactReviewFiles, s.impactFilter, s.appMode, s.impactAsync],
    (s) => {
      if (s.appMode === "impact") mount(refs.impact, ImpactView(s, actions));
    },
  );
  watch(
    store,
    (s) => [s.refsUnused, s.refsUsage, s.refsTab, s.appMode],
    (s) => {
      if (s.appMode === "refs") mount(refs.refs, RefsView(s));
    },
  );
  watch(
    store,
    (s) => [s.refsTab],
    (s) => {
      for (const b of refs.refsTabs.children)
        b.classList.toggle("on", b.dataset.rtab === s.refsTab);
    },
  );
  watch(store, querySlice, (s) => serializeUrl(s)); // URL stays in sync with the query
}

// ---- boot -----------------------------------------------------------------------------------------------
(async function boot() {
  // Capture the incoming query BEFORE any watch runs — the serialize-watch fires on subscribe and would
  // otherwise rewrite the URL from empty defaults, destroying a shared deep-link's params.
  const initialSearch = location.search;
  const shell = Shell(actions);
  refs = shell.refs;
  mount(document.getElementById("app"), shell.root);
  applyTheme(localStorage.getItem("rig-theme") || "system");
  if (localStorage.getItem("rig-lens-legend") === "0") set({ lensLegend: false });
  set({ reviewFileMode: localStorage.getItem("rig-review-file-mode") === "tree" ? "tree" : "list" });
  initSplitter();
  setupSearch();
  setupFileSearch();
  setupLensKeys();
  setupReviewKeys();
  setupWatches();
  // Derivation version first — it keys the cache and purges a stale persisted store before any cached fetch.
  try {
    const meta = await api.meta();
    await setCacheVersion(meta.derivationVersion);
  } catch {
    /* cache degrades to per-session */
  }
  api.providers().then((p) => {
    set({ providers: p });
    renderMsList("");
  });
  try {
    const runs = await api.runs();
    set({ runs });
    const patch = readUrl(runs, initialSearch); // validate ?store= against known runs
    set({
      ...patch,
      reviewViewed: loadReviewViewed(patch.reviewBase, patch.reviewHead),
    });
    syncControls(get());
    if (patch.appMode === "file") {
      if (patch.filePath) openFile(patch.filePath, patch.fileStart);
    } else if (patch.appMode === "review") {
      if (patch.reviewBase && patch.reviewHead) {
        loadReviewFiles({ openFirst: !patch.reviewFile });
        if (patch.reviewFile) loadFileDiff(patch.reviewFile);
      }
    } else if (patch.appMode === "impact") {
      if (patch.impactBase && patch.impactHead) loadImpact();
    } else if (patch.appMode === "refs") {
      loadRefs();
    } else if (patch.appMode === "hotspots") {
      loadHotspots();
      if (patch.compareA && patch.compareB) compareEffects(patch.compareA, patch.compareB);
    } else if (patch.from) {
      openTree(patch.from);
    }
  } catch (e) {
    status("failed to load runs: " + e.message, true);
  }
})();
