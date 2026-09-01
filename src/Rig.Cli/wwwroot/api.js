// The IO layer: all HTTP access + a two-tier client cache (in-memory + IndexedDB), and NOTHING else (no DOM,
// no URL). In a React port each function becomes a TanStack `useQuery`.
//
// Caching correctness: a commit-scoped store's FACTS are immutable, but derived output (tree/effects/hazards)
// also depends on the rule set + the derivation logic/payload schema. So the cache is keyed by a DERIVATION
// VERSION (from /api/meta = hash(derivation-schema token ⊕ rules fingerprint)), not the store id alone. When
// that version changes (rules edit / a deliberate schema bump) the keys change AND the persisted store is
// purged — stale derived data is never served.
// IndexedDB (not localStorage) because trees are >1 MB; it degrades to memory-only if IDB is unavailable.

const mem = new Map();
let version = "v0"; // derivation version; set at boot via setCacheVersion()

// ---- IndexedDB (best-effort; any failure degrades to in-memory only) ------------------------------------
const DB = "rig-cache",
  STORE = "kv";
function idb() {
  return new Promise((resolve, reject) => {
    const r = indexedDB.open(DB, 1);
    r.onupgradeneeded = () => r.result.createObjectStore(STORE);
    r.onsuccess = () => resolve(r.result);
    r.onerror = () => reject(r.error);
  });
}
async function idbGet(k) {
  try {
    const db = await idb();
    return await new Promise((res) => {
      const q = db.transaction(STORE).objectStore(STORE).get(k);
      q.onsuccess = () => res(q.result);
      q.onerror = () => res(undefined);
    });
  } catch {
    return undefined;
  }
}
async function idbPut(k, v) {
  try {
    const db = await idb();
    db.transaction(STORE, "readwrite").objectStore(STORE).put(v, k);
  } catch {
    /* quota / unavailable — skip */
  }
}
async function idbClear() {
  try {
    const db = await idb();
    db.transaction(STORE, "readwrite").objectStore(STORE).clear();
  } catch {
    /* ignore */
  }
}

// Set the derivation version and purge the persisted store if it moved (keys are version-prefixed, so old
// entries would be unreachable anyway — this reclaims their space). Call once at boot after /api/meta.
export async function setCacheVersion(v) {
  version = v;
  if (localStorage.getItem("rig-cache-ver") !== v) {
    await idbClear();
    mem.clear();
    localStorage.setItem("rig-cache-ver", v);
  }
}
// Force-purge everything (the UI's "purge cache" button).
export async function purgeCache() {
  mem.clear();
  await idbClear();
}

async function getJson(url) {
  const res = await fetch(url);
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || body.detail || body.title || res.statusText);
  }
  return res.json();
}
async function cached(key, url) {
  const k = version + "|" + key;
  if (mem.has(k)) return mem.get(k);
  const hit = await idbGet(k);
  if (hit !== undefined) {
    mem.set(k, hit);
    return hit;
  }
  const data = await getJson(url);
  mem.set(k, data);
  idbPut(k, data); // fire-and-forget persist
  return data;
}

// Some contracts (notably explicit effects-diff ambiguity/no-match) deliberately return a useful DTO with
// HTTP 400. Preserve and cache that body so the UI can show candidates; transport/problem responses still throw.
async function cachedContract(key, url) {
  const k = version + "|" + key;
  if (mem.has(k)) return mem.get(k);
  const hit = await idbGet(k);
  if (hit !== undefined) {
    mem.set(k, hit);
    return hit;
  }
  const res = await fetch(url);
  const body = await res.json().catch(() => null);
  if (!res.ok && (!body || typeof body.matched !== "boolean"))
    throw new Error(body?.error || body?.detail || body?.title || res.statusText);
  mem.set(k, body);
  idbPut(k, body);
  return body;
}

// Query string; omits null/blank. `store` is included only when explicit (an id) — implicit LATEST stays off
// the URL (its response can't be frozen). The RESOLVED id goes in the cache key, so LATEST and its explicit
// URL share one entry.
function qs(params) {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v == null || v === "" || v === false) continue;
    p.set(k, v === true ? "true" : String(v));
  }
  const s = p.toString();
  return s ? "?" + s : "";
}

export const api = {
  meta: () => getJson("/api/meta"),
  runs: () => getJson("/api/runs"), // LATEST pointer moves → never cached
  providers: () => cached("providers", "/api/providers"),
  hotspots: (storeId, explicitStore, sort, top, noLambdas, intrinsic) =>
    cached(
      `hotspots|${storeId}|${sort}|${top}|${!!noLambdas}|${!!intrinsic}`,
      "/api/hotspots" + qs({ store: explicitStore, sort, top, noLambdas: noLambdas ? true : undefined, intrinsic: intrinsic ? true : undefined }),
    ),
  // Explicit A/B only: both patterns are user-supplied and part of the stable cache key. A 400 ambiguity/
  // no-match contract is retained as data so its candidates can be rendered instead of collapsed to an error.
  effectsDiff: (storeId, explicitStore, a, b) =>
    cachedContract(
      `effects-diff|${storeId}|${a}|${b}`,
      "/api/effects-diff" + qs({ store: explicitStore, a, b }),
    ),
  // raw=true bypasses the opaque/collapse seam folds (server returns the full unfolded tree). It changes the
  // payload, so it MUST be in the cache key alongside the async-walk mode.
  tree: (storeId, explicitStore, from, asyncWalk, raw, intrinsic) =>
    cached(
      `tree|${storeId}|${from}|${!!asyncWalk}|${!!raw}|${!!intrinsic}`,
      "/api/tree" + qs({ from, store: explicitStore, async: !!asyncWalk, raw: raw ? true : undefined, intrinsic: intrinsic ? true : undefined }),
    ),
  entrypoints: (storeId, explicitStore) =>
    cached(`eps|${storeId}`, "/api/entrypoints" + qs({ store: explicitStore })),
  // reverse reachability — who reaches `from`. mode: "entrypoints" (rule-detected EPs, service-annotated) | "roots".
  // async=true also walks async-handoff edges (background workers / actor inboxes / events) — changes the set.
  callers: (storeId, explicitStore, from, mode, asyncWalk) =>
    cached(
      `callers|${storeId}|${from}|${mode}|${!!asyncWalk}`,
      "/api/callers" + qs({ from, store: explicitStore, mode, async: asyncWalk ? true : undefined }),
    ),
  // flat effect inventory reachable from `from` (provider:op tallies + reachable-method count).
  reaches: (storeId, explicitStore, from, intrinsic) =>
    cached(
      `reaches|${storeId}|${from}|${!!intrinsic}`,
      "/api/reaches" + qs({ from, store: explicitStore, intrinsic: intrinsic ? true : undefined }),
    ),
  // one concrete From->To path.
  path: (storeId, explicitStore, from, to, intrinsic) =>
    cached(
      `path|${storeId}|${from}|${to}|${!!intrinsic}`,
      "/api/path" + qs({ from, to, store: explicitStore, intrinsic: intrinsic ? true : undefined }),
    ),
  hazards: (storeId, explicitStore, from) =>
    cached(
      `haz|${storeId}|${from}`,
      "/api/hazards" + qs({ from, store: explicitStore }),
    ),
  // impact is keyed by (base, head, mode) — both stores are immutable, so safe to cache under the derivation
  // version; the sync/async traversal mode changes the diff, so it MUST be in the key (else an async request
  // would be served a cached sync result, or vice versa).
  impact: (base, head, asyncWalk) =>
    cached(
      `impact|${base}|${head}|${!!asyncWalk}`,
      "/api/impact" + qs({ base, head, async: !!asyncWalk }),
    ),
  // per-EP structural reach delta (added/removed reachable methods) for the tree diff overlay — same mode key.
  impactReach: (base, head, kind, route, asyncWalk) =>
    cached(
      `reach|${base}|${head}|${kind}|${route}|${!!asyncWalk}`,
      "/api/impact/reach" + qs({ base, head, kind, route, async: !!asyncWalk }),
    ),
  search: (explicitStore, q) =>
    getJson("/api/search" + qs({ q, store: explicitStore, limit: 15 })), // high-churn, uncached
  // Assembly-reference analysis (`rig refs --unused` / `--usage`). UNCACHED (getJson, not cached): this data
  // depends on facts + the solution's .csproj files, whose mtime the derivation-version cache key does NOT
  // capture — caching under that key could serve a stale result after a .csproj edit. `filter` is an optional
  // substring (unused → declaring assemblies; usage → target assemblies), matching the CLI's optional pattern.
  refsUnused: (explicitStore, filter) =>
    getJson("/api/refs/unused" + qs({ store: explicitStore, filter })),
  refsUsage: (explicitStore, filter) =>
    getJson("/api/refs/usage" + qs({ store: explicitStore, filter })),
  // Declaration source for ONE symbol id (`rig show`, web slice). UNCACHED (getJson, not cached): the text
  // is read from the working tree / git, whose state the `derivationVersion` cache key does NOT capture —
  // caching under that key could serve stale code after an edit. Keyed by symbol id only; the server
  // resolves the file path from the store (never a client-supplied path).
  source: (explicitStore, id, context) =>
    getJson("/api/source" + qs({ id, store: explicitStore, context })),
  // Indexed-file inventory. LATEST moves and the query is cheap, so keep it uncached like /api/runs.
  files: (explicitStore, q, limit = 100) =>
    getJson("/api/files" + qs({ store: explicitStore, q, limit })),
  // The semantic projection is immutable for (store,rules,file), so it belongs in the derivation cache.
  fileEffects: (storeId, explicitStore, file) =>
    cached(
      `file-effects|${storeId}|${file}`,
      "/api/file-effects" + qs({ store: explicitStore, file }),
    ),
  // Tiers 1-3 for one file. Its own request, and its own cache entry, because it is its own derivation: the
  // badges must not wait on it, and it must not be re-fetched when only the badge filters change.
  fileFindings: (storeId, explicitStore, file) =>
    cached(
      `file-findings|${storeId}|${file}`,
      "/api/file-findings" + qs({ store: explicitStore, file }),
    ),
  // Source may come from the working tree and therefore stays deliberately outside the client cache.
  fileSource: (explicitStore, file, start, count) =>
    getJson("/api/file-source" + qs({ store: explicitStore, file, start, count })),
  // Two immutable stores + a validated indexed file produce an exact Git patch and store-native old/new
  // annotations. The derivation version covers the semantic payload; commit-scoped stores cover source.
  fileDiff: (base, head, file, ignoreWhitespace = false) =>
    cached(
      `file-diff|${base}|${head}|${file}|${!!ignoreWhitespace}`,
      "/api/file-diff" + qs({ base, head, file, ignoreWhitespace: ignoreWhitespace ? true : undefined }),
    ),
  // The Git change inventory is cheap but immutable for a pair of commit-scoped stores. It is the review
  // navigation model; source-file inventories decide which rows the current one-path renderer can open.
  reviewFiles: (base, head) =>
    cached(
      `review-files|${base}|${head}`,
      "/api/review-files" + qs({ base, head }),
    ),
};
