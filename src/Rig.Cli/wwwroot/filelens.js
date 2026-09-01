// FILE EFFECT LENS — the source overlay. Renders the shared projection documented in
// Rendering/FileEffectLens.cs plus the findings tiers from Rig.Domain/Functions/HazardKinds.cs.
//
// ONE GRAMMAR, stated once, for every mark on the page:
//
//   FILL says WHERE            ● the effect is in this line's callee body   ○ it is N calls below
//   `?`  says ON WHAT BASIS    a trailing ? = the reach exists only through virtual/interface dispatch
//   ⟳    says IT REPEATS       the effect runs once per iteration (tier 2, looped_effect)
//   ⟳↓   says THE LOOP IS HERE the repetition is on this line, the I/O is somewhere beneath the call
//                              (tier 3, cross_method_amplification; the number is the witness depth,
//                               which IS the confidence: ≤1 high, ≤4 medium, else a `lead`)
//   ⚠    says JUDGMENT         a tier-1 hazard is anchored on this line
//
// Nothing is carried by colour alone: every distinction above also has a glyph, a fill, or a border style,
// because a reader who cannot separate red from green must still read the page. Colour is the redundant
// third cue, never the first.
//
// WHY THE DISTANT SET COLLAPSES. On the real MedDBase store, WriteDischargeDetail.cs marks 133 lines with
// 771 badges; 664 of those (86%) are dispatch-only guesses and only 46 (6%) are at depth 0. Rendering all
// of them means the six proven writes are indistinguishable from — and on a 128px gutter, literally clipped
// behind — a CHA fan-out through one `get_Control` property. So the gutter shows what HAPPENS HERE (depth 0)
// and every finding, and folds the whole distant fan-out into ONE chip whose popover has the detail.
// Fewer, better-placed marks: 771 badges become ~80 chips, and the 37 lines that really do I/O stand out.

import { h, mount } from "./lib.js";
import { highlightCSharp } from "./highlight.js";
import { baseName, shortLabel } from "./components.js";

// ---- vocabulary tables ---------------------------------------------------------------------------------

// Family -> providers, straight from `rig derive --list-providers`. This is the GROUPING for the grain
// toggle: `family` (8 buckets, the default density) collapses providers; `provider` expands them. The map is
// presentation data — a legend and a fallback label — never a re-derivation of what the store said.
export const FAMILY_PROVIDERS = {
  blob: ["aws_s3", "azure_blob", "object_store", "parquet"],
  bus: ["mediatr", "rabbitmq"],
  cache: ["cache", "entity_cache", "inproc_cache", "redis"],
  db: ["dapper", "db_command", "db_connection", "db_reader", "db_transaction", "efcore", "linq2db", "llblgen", "repository", "yessql"],
  echo: ["actor", "chamber_msg", "echo_publish", "eventbus", "gcp_pubsub", "queue", "webhook"],
  io: ["io"],
  rpc: ["clientpage_proxy", "fhir", "http", "http_response", "ldap", "openai", "sendgrid", "smtp", "soap", "socket", "twilio", "xero"],
  search: ["azure_search", "elasticsearch"],
};
const PROVIDER_FAMILY = Object.fromEntries(
  Object.entries(FAMILY_PROVIDERS).flatMap(([family, providers]) => providers.map((p) => [p, family])),
);
export const familyOf = (provider) => PROVIDER_FAMILY[provider] || provider;

// One-line gloss per family, for the legend. A reader should not need to guess what `echo` means.
export const FAMILY_HELP = {
  blob: "object / blob storage — S3, Azure Blob, parquet files",
  bus: "in-process message bus — MediatR, RabbitMQ",
  cache: "cache read/write — entity cache, in-proc cache, Redis",
  db: "relational database — LLBLGen, Dapper, EF Core, raw commands",
  echo: "async handoff — Echo actors, chamber messages, queues, webhooks",
  io: "filesystem and stream I/O",
  rpc: "outbound network call — HTTP, SOAP, SMTP, LDAP, third parties",
  search: "search index — Elasticsearch, Azure Search",
};

// Tier-1 hazard type -> the SHORT inline label. The full type string is always in the tooltip and the
// evidence panel; the gutter gets an abbreviation because a 128px strip cannot hold `static_init_capture`.
const HAZARD_SHORT = {
  n_plus_1: "n+1",
  race_window: "race",
  lazy_init_race: "lazy-race",
  thread_local_context: "ctx-leak",
  dual_write: "dual-write",
  sync_over_async: "sync/async",
  event_cycle: "cycle",
  cache_coherence: "stale-cache",
  static_init_capture: "static-init",
  unserializable_payload: "payload",
};
export const hazardShort = (type) => HAZARD_SHORT[type] || type.replace(/_/g, "-");

// Tier 3 confidence is DERIVED from witness depth — CrossMethodAmplificationDataset.AnchorFinding.Confidence
// (≤1 high, ≤4 medium, else low). Mirrored here rather than trusted from the wire so the badge can never
// disagree with the number printed next to it.
export const anchorConfidence = (witnessDepth) => (witnessDepth <= 1 ? "high" : witnessDepth <= 4 ? "medium" : "low");

// ---- the lens model -----------------------------------------------------------------------------------

// MOCKED FIELDS. Everything in this list is synthesized client-side because /api/file-effects does not
// return it yet; the UI labels every one of them so a reader can never mistake a mock for a fact. See the
// REQUIRED-INPUT list in the design report — when the server ships these, delete the mock plumbing and the
// component code above it stops caring.
export const MOCK_FIELDS = [
  "hazards[] (tier 1) — from filelens-findings.mock.json",
  "amplifications[] (tier 2, looped_effect) — from filelens-findings.mock.json",
  "anchors[] (tier 3, cross_method_amplification) — from filelens-findings.mock.json",
  "badge.provider / badge.operation — inferred, see providerKnown",
];

let findingsDoc = null;
// The store the loaded mock was derived from, and the store actually being viewed. A MISMATCH is not a
// cosmetic problem: every mocked finding is LINE-ANCHORED, so a dataset dumped from one commit lands its
// `⚠` and `⟳↓` marks on whatever happens to sit at those line numbers in another. That is undetectable by
// eye and worse than showing nothing, so a mismatch suppresses the findings entirely and says why.
let findingsStore = null;
let findingsStoreMismatch = null;

// Lazily fetch the mock findings dataset ONCE. Returns {} on any failure — a missing mock must degrade to
// "no findings shown", never to a broken lens. `viewingStore` is the store id the page is reading (from the
// runs list); pass null only where it is genuinely unknown.
export async function loadFindingsMock(viewingStore) {
  if (findingsDoc) {
    checkFindingsStore(viewingStore);
    return findingsDoc;
  }
  try {
    const response = await fetch("./filelens-findings.mock.json");
    findingsDoc = response.ok ? await response.json() : { files: {} };
  } catch {
    findingsDoc = { files: {} };
  }
  findingsStore = (findingsDoc && findingsDoc.store) || null;
  checkFindingsStore(viewingStore);
  return findingsDoc;
}

function checkFindingsStore(viewingStore) {
  findingsStoreMismatch =
    findingsStore && viewingStore && findingsStore !== viewingStore ? { mock: findingsStore, viewing: viewingStore } : null;
}

// What the UI must say about the mock, or null when there is nothing to say.
export function findingsProvenance() {
  return findingsStoreMismatch;
}
// Path lookup is case-insensitive on the drive letter and separator-normalised: the mock keys come from a
// tsv dump, the DTO path from the store, and the two agree on everything except casing nobody controls.
const normPath = (p) => (p || "").replace(/\//g, "\\").toLowerCase();
export function findingsFor(file) {
  // Refuse rather than mis-anchor: a mock from another store has line numbers that no longer describe this
  // source. Showing nothing is a smaller lie than showing a hazard on the wrong line.
  if (findingsStoreMismatch) return null;
  const files = (findingsDoc && findingsDoc.files) || {};
  const want = normPath(file);
  for (const key of Object.keys(files)) if (normPath(key) === want) return files[key];
  return null;
}

// A badge, normalised. `provider`/`operation` are "" when the API's family grain is all we have —
// providerKnown says which it is, so provider grain can render honest gaps instead of inventing traffic.
function badgeOf(effect, providerHint) {
  return {
    family: effect.family,
    depth: effect.nearestDepth,
    dispatch: !!effect.viaDispatchOnly,
    direct: effect.nearestDepth === 0,
    provider: providerHint ? providerHint.provider : "",
    operation: providerHint ? providerHint.operation : "",
    providerKnown: !!providerHint,
    // TIER 2. `effect.looped` is REAL — FileEffectAggregateDto.Looped, additive on the wire. When the store
    // says so we mark it as a fact (`loopedReal`); the mock rows below only fill in for a server that predates
    // the field, and they carry the iteration text a badge alone cannot.
    looped: effect.looped ? { synthetic: false, iteration: "an enclosing loop (store: looped)" } : null,
    loopedReal: !!effect.looped,
  };
}

// The label a reader sees in the terminal (`db!`, `cache:5?`) — kept identical here on purpose. A reader who
// learns the text form in `rig annotate` must read the same string in the tooltip.
export const badgeText = (b) =>
  `${b.direct ? `${b.family}!` : `${b.family}:${b.depth}`}${b.looped ? "*" : ""}${b.dispatch ? "?" : ""}`;

// Build the render model: the DTO joined to the findings tiers, indexed by line. Pure — no DOM, no fetch.
export function lensModel(dto, findings) {
  const f = findings || { hazards: [], amplifications: [], anchors: [] };
  const byLine = new Map();
  const row = (line) => {
    let r = byLine.get(line);
    if (!r) {
      r = { line, badges: [], targets: [], haz: [], amp: [], anchors: [], ampAttached: new Set() };
      byLine.set(line, r);
    }
    return r;
  };
  for (const hz of f.hazards || []) row(hz.line).haz.push(hz);
  for (const am of f.amplifications || []) row(am.line).amp.push(am);
  for (const an of f.anchors || []) row(an.line).anchors.push({ ...an, confidence: anchorConfidence(an.witnessDepth) });

  // Site badges. Several call sites on one line MERGE, keeping the shortest distance per family and the
  // strongest basis with it — the same rule as FileEffectLens.Merge, because a line badge that showed a
  // dispatch-only distance under a proven one would claim a route the facts do not have.
  const siteTargets = new Map();
  for (const site of dto.sites || []) {
    const r = row(site.line);
    if (site.targetMethodId) {
      const set = siteTargets.get(site.line) || new Set();
      set.add(site.targetMethodId);
      siteTargets.set(site.line, set);
    }
    for (const effect of site.effects) {
      const existing = r.badges.find((b) => b.family === effect.family);
      const incoming = badgeOf(effect);
      if (!existing) r.badges.push(incoming);
      else {
        const better = existing.dispatch && !incoming.dispatch;
        const closer = existing.dispatch === incoming.dispatch && incoming.depth < existing.depth;
        if (better || closer) Object.assign(existing, incoming);
      }
    }
  }
  for (const [line, set] of siteTargets) row(line).targets = [...set].sort();

  // Tier 2 attaches to the BADGE it amplifies, not to the line: `looped_effect` carries provider:operation,
  // which maps to a family, so "the cache read here repeats" lands on the cache badge and leaves a
  // neighbouring proven db write alone. An amplification whose family has no badge on the line (possible if
  // a filter dropped it, or the effect is enclosing-keyed rather than call-keyed) keeps a line-level mark.
  for (const r of byLine.values()) {
    for (const am of r.amp) {
      const family = familyOf(am.provider);
      const target = r.badges.find((b) => b.family === family);
      if (target) {
        // Identity is tracked on the ROW, not by comparing `badge.looped === am`: the attach wraps the row in
        // a new object, so reference equality reported every attached amplification as an orphan too and the
        // line grew a duplicate ⟳ mark next to the badge that already carried one.
        r.ampAttached.add(am);
        target.looped = { ...am, synthetic: !target.loopedReal };
        if (!target.providerKnown) {
          target.provider = am.provider;
          target.operation = am.operation;
          target.providerKnown = true;
        }
      }
    }
  }

  const methodFindings = (m) => {
    const inside = (line) => line >= m.line && line <= (m.endLine || m.line);
    const rows = [...byLine.values()].filter((r) => inside(r.line));
    return {
      haz: rows.flatMap((r) => r.haz),
      amp: rows.flatMap((r) => r.amp),
      anchors: rows.flatMap((r) => r.anchors),
    };
  };
  const methods = (dto.methods || []).map((m) => ({
    id: m.id,
    name: m.name || shortLabel(m.id),
    signature: m.signature || "",
    line: m.line,
    endLine: m.endLine,
    badges: (m.effects || []).map((e) => badgeOf(e)),
    findings: methodFindings(m),
  }));

  return {
    file: dto.file,
    families: dto.families || [],
    columnsAvailable: !!dto.columnsAvailable,
    // A SERVER-side filter (FileEffectsFilterDto) would remove badges before the client ever sees them, so it
    // must reach the same disclosure line. The client drives its own filtering and never sends one today —
    // this is carried defensively, because the one failure this overlay may not have is a narrowed view that
    // reads as a quiet file.
    serverFilter: dto.filter && dto.filter.active ? dto.filter : null,
    methods,
    lines: byLine,
    covered: !!findings,
    counts: {
      markedLines: byLine.size,
      badges: [...byLine.values()].reduce((n, r) => n + r.badges.length, 0),
      haz: (f.hazards || []).length,
      amp: (f.amplifications || []).length,
      anchors: (f.anchors || []).length,
    },
  };
}

// ---- the filter ---------------------------------------------------------------------------------------

// Every field here is URL-addressable (see store.js). CLIENT-SIDE fields re-render instantly, which matters
// a great deal when the underlying query costs ~50s on a cold store: a reader tunes depth and basis without
// ever refetching. Only `intrinsic` and `async` change what the SERVER computes, and the UI says so.
export const LENS_FILTER_DEFAULTS = {
  mode: "none", // none | only | exclude   (provider / provider:operation tokens)
  tokens: [],
  minDepth: "", // "" = no floor
  maxDepth: "", // "" = no ceiling
  directOnly: false, // depth 0 only
  loopedOnly: false, // tier-2 only: effects that run once per iteration (mirrors LensFilter.LoopedOnly)
  dispatch: "show", // show | hide | only   (basis gate)
  tiers: ["haz", "amp", "xm"], // which findings tiers render
  tier3Min: "low", // low | medium | high  (minimum anchor confidence)
  grain: "family", // family | provider
  distant: "fold", // fold | expand | hide  (what happens to the depth>0 fan-out)
  outlineSort: "line", // line | severity   (view preference, not a filter — never counts as "filtered")
  intrinsic: false, // SERVER — refetch
  async: false, // SERVER — refetch
};
export const lensFilterDefaults = () => ({ ...LENS_FILTER_DEFAULTS, tokens: [] });

const SERVER_KEYS = ["intrinsic", "async"];
// `outlineSort` is a VIEW preference, not a filter — sorting the index differently hides nothing, so it must
// never trip the FILTERED disclosure. Everything else in the defaults does hide something.
const FILTER_KEYS = Object.keys(LENS_FILTER_DEFAULTS).filter((k) => k !== "outlineSort");
export const isFilterActive = (f) =>
  FILTER_KEYS.some((k) =>
    Array.isArray(LENS_FILTER_DEFAULTS[k])
      ? [...(f[k] || [])].sort().join(",") !== [...LENS_FILTER_DEFAULTS[k]].sort().join(",")
      : f[k] !== LENS_FILTER_DEFAULTS[k],
  );
export const serverFilterChanged = (a, b) => SERVER_KEYS.some((k) => a[k] !== b[k]);

const tokenMatch = (b, t) => {
  const low = t.toLowerCase();
  return (
    low === b.family.toLowerCase() ||
    (b.provider && (low === b.provider.toLowerCase() || low === `${b.provider}:${b.operation}`.toLowerCase()))
  );
};

// Apply the filter and REPORT WHAT IT REMOVED. The report is not optional garnish: a lens that hides 94 of
// 133 marked lines and looks like a clean file is a lie, so `applyLensFilter` always returns the counts the
// disclosure line prints, and the view always prints them when anything is non-default.
export function applyLensFilter(model, filter) {
  const hidden = { lines: 0, badges: 0, haz: 0, amp: 0, anchors: 0, folded: 0 };
  const keepBadge = (b) => {
    if (filter.directOnly && !b.direct) return false;
    if (filter.loopedOnly && !b.looped) return false;
    if (filter.dispatch === "hide" && b.dispatch) return false;
    if (filter.dispatch === "only" && !b.dispatch) return false;
    if (filter.minDepth !== "" && b.depth < Number(filter.minDepth)) return false;
    if (filter.maxDepth !== "" && b.depth > Number(filter.maxDepth)) return false;
    if (filter.mode === "only" && !filter.tokens.some((t) => tokenMatch(b, t))) return false;
    if (filter.mode === "exclude" && filter.tokens.some((t) => tokenMatch(b, t))) return false;
    return true;
  };
  const CONF_RANK = { low: 0, medium: 1, high: 2 };
  const keepAnchor = (a) => filter.tiers.includes("xm") && CONF_RANK[a.confidence] >= CONF_RANK[filter.tier3Min];

  const lines = new Map();
  for (const r of model.lines.values()) {
    const badges = r.badges.filter(keepBadge);
    hidden.badges += r.badges.length - badges.length;
    const haz = filter.tiers.includes("haz") ? r.haz : [];
    const amp = filter.tiers.includes("amp") ? r.amp : [];
    const anchors = r.anchors.filter(keepAnchor);
    hidden.haz += r.haz.length - haz.length;
    hidden.amp += r.amp.length - amp.length;
    hidden.anchors += r.anchors.length - anchors.length;
    const direct = badges.filter((b) => b.direct);
    const distant = badges.filter((b) => !b.direct);
    if (filter.distant === "hide") hidden.badges += distant.length;
    if (filter.distant === "fold") hidden.folded += distant.length;
    const shown = filter.distant === "hide" ? direct : badges;
    if (!shown.length && !haz.length && !amp.length && !anchors.length) {
      if (r.badges.length || r.haz.length || r.amp.length || r.anchors.length) hidden.lines += 1;
      continue;
    }
    lines.set(r.line, { ...r, ampAttached: r.ampAttached, badges: shown, direct, distant: filter.distant === "hide" ? [] : distant, haz, amp, anchors });
  }
  const methods = model.methods
    .map((m) => {
      const badges = m.badges.filter(keepBadge);
      return {
        ...m,
        badges,
        direct: badges.filter((b) => b.direct),
        distant: badges.filter((b) => !b.direct),
        findings: {
          haz: filter.tiers.includes("haz") ? m.findings.haz : [],
          amp: filter.tiers.includes("amp") ? m.findings.amp : [],
          anchors: m.findings.anchors.filter(keepAnchor),
        },
      };
    })
    .filter((m) => m.badges.length || m.findings.haz.length || m.findings.amp.length || m.findings.anchors.length);
  hidden.methods = model.methods.length - methods.length;
  return { lines, methods, hidden };
}

// The single most important derived predicate on the page: does this line DO I/O, as a fact? Proven, depth 0,
// no dispatch guess. It is what the rail's solid state means and what `n`/`p` navigation jumps between.
const provenDirect = (r) => r.badges.some((b) => b.direct && !b.dispatch);
const dispatchDirect = (r) => r.badges.some((b) => b.direct && b.dispatch);
export const lineSeverity = (r) =>
  r.haz.length ? 4 : r.anchors.length || r.amp.length ? 3 : provenDirect(r) ? 2 : dispatchDirect(r) ? 1 : 0;

// ---- marks --------------------------------------------------------------------------------------------

const popovers = new Set();
function closePopovers() {
  for (const p of popovers) p.remove();
  popovers.clear();
}
document.addEventListener("click", closePopovers);
// A click-anchored popover instead of a `title`: the distant rollup and the tier-3 anchor both carry EVIDENCE
// (per-family depths, witness method, iteration source) that a native tooltip truncates and cannot be read
// from at leisure.
function openPopover(anchorEl, title, body) {
  closePopovers();
  const rect = anchorEl.getBoundingClientRect();
  const pop = h(
    "div",
    { class: "fx-pop", onClick: (e) => e.stopPropagation() },
    h("div", { class: "fx-pop-title" }, title),
    body,
  );
  document.body.append(pop);
  const width = pop.offsetWidth;
  pop.style.left = `${Math.max(8, Math.min(window.innerWidth - width - 8, rect.left))}px`;
  pop.style.top = `${Math.min(window.innerHeight - pop.offsetHeight - 8, rect.bottom + 4)}px`;
  popovers.add(pop);
}

// An effect badge. Class carries the three orthogonal axes so CSS owns the look:
//   fx-here / fx-below   (fill: where)      fx-guess (border: basis)      fx-loop (glyph: repetition)
function Badge(b, grain) {
  const cls = ["fx", b.direct ? "fx-here" : "fx-below", b.dispatch ? "fx-guess" : "", b.looped ? "fx-loop" : ""]
    .filter(Boolean)
    .join(" ");
  const label =
    grain === "provider" ? (b.providerKnown ? `${b.provider}:${b.operation}` : `${b.family}:?`) : b.family;
  const title = [
    `${badgeText(b)} — ${b.direct ? "the effect is in this call's body" : `nearest is ${b.depth} call${b.depth === 1 ? "" : "s"} below`}`,
    b.dispatch ? "BASIS: reachable only through virtual/interface dispatch — CHA says it can land here, nothing proves it does" : "BASIS: a real call edge",
    b.looped ? `AMPLIFIED: runs once per iteration of — ${b.looped.iteration}` : "",
    grain === "provider" && !b.providerKnown ? "provider unknown at this grain (the API returns family only)" : "",
  ]
    .filter(Boolean)
    .join("\n");
  return h(
    "span",
    { class: cls, title },
    b.looped ? h("span", { class: "fx-amp" }, "⟳") : null,
    h("span", { class: "fx-fill" }, b.direct ? "●" : "○"),
    h("span", { class: "fx-name" + (grain === "provider" && !b.providerKnown ? " fx-unknown" : "") }, label),
    b.direct ? null : h("span", { class: "fx-depth" }, b.depth),
    b.dispatch ? h("span", { class: "fx-q", title: "dispatch-only" }, "?") : null,
  );
}

// The distant fan-out, folded to ONE chip. `○ 5↓1?` = five families reachable below this line, nearest one
// hop away, at least one of them a dispatch guess. This is the chip that turns line 388 of
// WriteDischargeDetail.cs from eighteen clipped pills into a single readable token.
function Rollup(distant, grain) {
  if (!distant.length) return null;
  const nearest = Math.min(...distant.map((b) => b.depth));
  const guesses = distant.filter((b) => b.dispatch).length;
  const allGuess = guesses === distant.length;
  const el = h(
    "button",
    {
      class: "fx fx-below fx-rollup" + (allGuess ? " fx-guess" : ""),
      title: `${distant.length} famil${distant.length === 1 ? "y" : "ies"} reachable below this line, nearest ${nearest} away${guesses ? ` · ${guesses} dispatch-only` : ""} — click for the breakdown`,
      onClick: (e) => {
        e.stopPropagation();
        openPopover(
          el,
          `reachable below this line — ${distant.length} famil${distant.length === 1 ? "y" : "ies"}`,
          h(
            "div",
            { class: "fx-pop-rows" },
            ...[...distant]
              .sort((a, b) => a.depth - b.depth || a.family.localeCompare(b.family))
              .map((b) =>
                h(
                  "div",
                  { class: "fx-pop-row" },
                  Badge(b, grain),
                  h("span", { class: "fx-pop-note" }, b.dispatch ? "dispatch-only — may not be a real call" : "real call edge"),
                ),
              ),
          ),
        );
      },
    },
    h("span", { class: "fx-fill" }, "○"),
    h("span", { class: "fx-name" }, distant.length),
    h("span", { class: "fx-depth" }, "↓" + nearest),
    guesses ? h("span", { class: "fx-q" }, "?") : null,
  );
  return el;
}

// TIER 1 — a judgment anchored on this line. Own pill, own glyph, confidence in the border.
function HazMark(hazards) {
  if (!hazards.length) return null;
  const worst = hazards.some((z) => z.confidence === "high") ? "high" : hazards.some((z) => z.confidence === "medium") ? "medium" : "low";
  const el = h(
    "button",
    {
      class: `fx fx-haz conf-${worst}`,
      title: hazards.map((z) => `${z.type} (${z.confidence}) — ${z.subtype}${z.key ? ` · key ${z.key}` : ""}`).join("\n"),
      onClick: (e) => {
        e.stopPropagation();
        openPopover(
          el,
          `hazard — tier 1 (a judgment over the effects)`,
          h(
            "div",
            { class: "fx-pop-rows" },
            ...hazards.map((z) =>
              h(
                "div",
                { class: "fx-pop-row col" },
                h("strong", {}, `${z.type} · ${z.confidence} confidence`),
                h("span", { class: "fx-pop-note" }, z.subtype),
                z.key ? h("span", { class: "fx-pop-note" }, `key: ${z.key}`) : null,
                z.detail ? h("span", { class: "fx-pop-note" }, z.detail) : null,
                z.enclosing ? h("span", { class: "fx-pop-note" }, `in ${z.enclosing}`) : null,
              ),
            ),
          ),
        );
      },
    },
    "⚠",
    h("span", { class: "fx-name" }, hazards.length === 1 ? hazardShort(hazards[0].type) : `${hazards.length} hazards`),
  );
  return el;
}

// TIER 3 — the loop is HERE, the I/O is BENEATH the call. Deliberately NOT shaped like an effect badge: no
// ● / ○ fill, because nothing happens on this line. `⟳↓ cache 0` reads "repeats, downward, cache, 0 hops".
// The number is the witness depth and therefore the confidence; a `low` anchor says `lead` in words so it can
// never be mistaken for a finding.
function AnchorMark(anchors, grain) {
  if (!anchors.length) return null;
  const best = [...anchors].sort((a, b) => a.witnessDepth - b.witnessDepth)[0];
  const label = grain === "provider" && best.witnessProvider ? `${best.witnessProvider}:${best.witnessOperation}` : familyOf(best.witnessProvider);
  const el = h(
    "button",
    {
      class: `fx fx-anchor conf-${best.confidence}`,
      title:
        `cross-method amplification (tier 3) — this call is issued once per ${best.iterationKind || "iteration"}; ` +
        `the ${best.witnessProvider}:${best.witnessOperation} it reaches is ${best.witnessDepth} hop(s) below it.\n` +
        `${best.confidence} confidence (witness depth ${best.witnessDepth})${best.confidence === "low" ? " — a LEAD, not a finding" : ""}`,
      onClick: (e) => {
        e.stopPropagation();
        openPopover(
          el,
          "cross-method amplification — tier 3 (the loop is here, the I/O is below)",
          h(
            "div",
            { class: "fx-pop-rows" },
            ...anchors.map((a) =>
              h(
                "div",
                { class: "fx-pop-row col" },
                h("strong", {}, `${a.confidence} confidence · witness ${a.witnessDepth} hop${a.witnessDepth === 1 ? "" : "s"} below`),
                a.confidence === "low" ? h("span", { class: "fx-pop-lead" }, "a LEAD, not a finding — path-insensitive reach at this depth is weak evidence") : null,
                h("span", { class: "fx-pop-note" }, `loop: ${a.iterationKind} ${a.iterationDetail || ""}`),
                a.iteratedSource ? h("span", { class: "fx-pop-note" }, `over: ${a.iteratedSource}`) : null,
                h("span", { class: "fx-pop-note" }, `call: ${a.callee}`),
                h("span", { class: "fx-pop-note" }, `witness: ${a.witnessProvider}:${a.witnessOperation} in ${a.witnessMethod}${a.witnessLine ? `:${a.witnessLine}` : ""}`),
                a.witnessResource ? h("span", { class: "fx-pop-note" }, `resource: ${a.witnessResource}`) : null,
                a.key ? h("span", { class: "fx-pop-note" }, `key: ${a.key}`) : null,
              ),
            ),
          ),
        );
      },
    },
    h("span", { class: "fx-amp" }, "⟳↓"),
    h("span", { class: "fx-name" }, label),
    h("span", { class: "fx-depth" }, best.witnessDepth),
    best.confidence === "low" ? h("span", { class: "fx-lead" }, "lead") : null,
    anchors.length > 1 ? h("span", { class: "fx-depth" }, `×${anchors.length}`) : null,
  );
  return el;
}

// A tier-2 amplification whose family has no badge left on the line to ride on. Rare, but it must not vanish.
function OrphanAmpMark(row) {
  const orphans = row.amp.filter((a) => !row.ampAttached.has(a));
  if (!orphans.length) return null;
  return h(
    "span",
    {
      class: "fx fx-loop fx-orphan",
      title: orphans.map((a) => `${a.provider}:${a.operation} runs once per iteration — ${a.iteration}`).join("\n"),
    },
    h("span", { class: "fx-amp" }, "⟳"),
    h("span", { class: "fx-name" }, orphans.length === 1 ? familyOf(orphans[0].provider) : orphans.length),
  );
}

// The rail: the ONE mark a reader scans a 700-line file with. Precedence is deliberate — a hazard outranks a
// proven effect outranks a guess — because the rail answers "should I stop here?", not "what is here".
function railClass(row) {
  if (row.haz.length) return "rail rail-haz";
  if (row.anchors.length || row.amp.length) return "rail rail-amp";
  if (provenDirect(row)) return "rail rail-here";
  if (dispatchDirect(row)) return "rail rail-guess";
  return "rail rail-below";
}

// The marks strip for one source line. Reading order is fixed — findings, then what happens HERE, then one
// chip for everything below — but the strip is BUDGETED, and that is not cosmetic: the old gutter silently
// amputated chips (a bare "e" where a `cache` badge had been), so the one thing this strip may never do is
// overflow. Past the budget the LOWEST-PRIORITY marks fold into a `+N` chip that opens the full list, so a
// mark is either fully legible or explicitly counted — never half-drawn.
// Provider grain prints `entity_cache:read` where family grain prints `cache` — roughly double the width —
// so the budget is per grain. Both numbers were set by measuring the real store until the clipped-row count
// hit zero on Controller.cs and WriteDischargeDetail.cs at both grains.
const MARK_BUDGET = { family: 4, provider: 3 };
function LineMarks(row, filter) {
  const budget = MARK_BUDGET[filter.grain] || 4;
  const slots = [];
  const push = (node, rank) => {
    if (node) slots.push({ node, rank });
  };
  push(HazMark(row.haz), 0);
  push(AnchorMark(row.anchors, filter.grain), 1);
  push(OrphanAmpMark(row), 2);
  // A proven direct effect outranks a dispatch-only one: "this line writes to the db" is worth a slot that
  // "this line MIGHT, through an interface" is not.
  const directs = [...row.direct].sort((a, b) => Number(a.dispatch) - Number(b.dispatch) || a.family.localeCompare(b.family));
  directs.forEach((b, i) => push(Badge(b, filter.grain), 3 + i / 100));
  if (filter.distant === "fold") push(Rollup(row.distant, filter.grain), 3.5);
  if (filter.distant === "expand") row.distant.forEach((b, i) => push(Badge(b, filter.grain), 5 + i / 100));

  if (slots.length <= budget) return slots.map((x) => x.node);
  const ranked = [...slots].sort((a, b) => a.rank - b.rank);
  const keep = new Set(ranked.slice(0, budget - 1).map((x) => x.node));
  const dropped = ranked.slice(budget - 1);
  const shown = slots.filter((x) => keep.has(x.node)).map((x) => x.node);
  const more = h(
    "button",
    {
      class: "fx fx-more",
      title: `${dropped.length} more mark(s) on this line — click to see them all`,
      onClick: (e) => {
        e.stopPropagation();
        openPopover(more, `every mark on line ${row.line}`, h("div", { class: "fx-pop-rows" }, h("div", { class: "fx-pop-row wrap" }, ...slots.map((x) => x.node))));
      },
    },
    `+${dropped.length}`,
  );
  return [...shown, more];
}

// ---- method row (code vision) -------------------------------------------------------------------------

// Method grain is a DIFFERENT QUANTITY from line grain (from the method vs from the call target), and it is
// where the tier counts belong: HazardKinds puts tier 3 "on the CALLER method (where a human would fix the
// loop)", so a reviewer scanning declarations wants "this body has 5 looped call sites" without reading the
// body. Counts, not individual marks — the marks are already on the lines.
function TierCounts(findings, onJump) {
  const parts = [];
  if (findings.haz.length)
    parts.push(
      h(
        "button",
        {
          class: "tier tier-haz",
          title: findings.haz.map((z) => `${z.type} (${z.confidence}) at line ${z.line}`).join("\n") + "\nclick to jump to the first",
          onClick: (e) => {
            e.stopPropagation();
            onJump(Math.min(...findings.haz.map((z) => z.line)));
          },
        },
        `⚠ ${findings.haz.length}`,
      ),
    );
  if (findings.amp.length)
    parts.push(
      h(
        "button",
        {
          class: "tier tier-amp",
          title: `${findings.amp.length} effect(s) in this body run once per iteration\n` + findings.amp.map((a) => `line ${a.line}: ${a.provider}:${a.operation}`).join("\n"),
          onClick: (e) => {
            e.stopPropagation();
            onJump(Math.min(...findings.amp.map((a) => a.line)));
          },
        },
        `⟳ ${findings.amp.length}`,
      ),
    );
  if (findings.anchors.length) {
    const worst = findings.anchors.some((a) => a.confidence === "high") ? "high" : findings.anchors.some((a) => a.confidence === "medium") ? "medium" : "low";
    parts.push(
      h(
        "button",
        {
          class: `tier tier-xm conf-${worst}`,
          title: `${findings.anchors.length} looped call site(s) in this body reach I/O below — this is where a human fixes the loop\n` + findings.anchors.map((a) => `line ${a.line}: ${a.witnessProvider}:${a.witnessOperation} @${a.witnessDepth} (${a.confidence})`).join("\n"),
          onClick: (e) => {
            e.stopPropagation();
            onJump(Math.min(...findings.anchors.map((a) => a.line)));
          },
        },
        `⟳↓ ${findings.anchors.length}`,
      ),
    );
  }
  return parts;
}

function MethodRow(method, filter, actions, onJump) {
  return h(
    "div",
    { class: "file-vision" },
    h(
      "button",
      {
        class: "file-method-link",
        title: `${method.signature || method.id}\nopen as a call tree`,
        onClick: () => actions.openFileTree(method.id),
      },
      method.name,
      " ↗",
    ),
    ...TierCounts(method.findings, onJump),
    ...method.direct.map((b) => Badge(b, filter.grain)),
    filter.distant === "fold" ? Rollup(method.distant, filter.grain) : null,
    ...(filter.distant === "expand" ? method.distant.map((b) => Badge(b, filter.grain)) : []),
  );
}

// ---- filter bar ---------------------------------------------------------------------------------------

const seg = (label, value, title, onClick, active) =>
  h(
    "button",
    { class: "fseg" + (active ? " on" : ""), title, onClick },
    h("span", { class: "fseg-k" }, label),
    h("span", { class: "fseg-v" }, value),
  );

// The filter reads as a SENTENCE, not a wall of checkboxes: each segment shows its current value and cycles
// or prompts on click. That supports arbitrary combinations (the brief's requirement) in one line of chrome,
// and — because the value is always printed — a reader can see the whole filter state at a glance without
// opening anything.
function FilterBar(s, actions, model, result) {
  const f = s.lensFilter;
  const set = (patch) => actions.setLensFilter(patch);
  const cycle = (key, values) => () => set({ [key]: values[(values.indexOf(f[key]) + 1) % values.length] });
  const tierOn = (t) => f.tiers.includes(t);
  const toggleTier = (t) => () => set({ tiers: tierOn(t) ? f.tiers.filter((x) => x !== t) : [...f.tiers, t] });
  const num = (key, label, title) =>
    h(
      "label",
      { class: "fseg fnum", title },
      h("span", { class: "fseg-k" }, label),
      h("input", {
        type: "number",
        min: "0",
        value: f[key] === "" ? "" : String(f[key]),
        placeholder: "—",
        onChange: (e) => set({ [key]: e.target.value === "" ? "" : Math.max(0, Number.parseInt(e.target.value, 10) || 0) }),
      }),
    );
  return h(
    "div",
    { class: "lens-filter" },
    h(
      "div",
      { class: "lens-filter-row" },
      seg(
        "families",
        f.mode === "none" ? "all" : `${f.mode} ${f.tokens.join(",") || "—"}`,
        "cycle: no filter → only these → all but these. Tokens are provider or provider:operation — the same vocabulary the tree toolbar uses.",
        cycle("mode", ["none", "only", "exclude"]),
        f.mode !== "none",
      ),
      h("input", {
        class: "ftokens",
        placeholder: "db,cache,llblgen:write…",
        value: f.tokens.join(","),
        title: "comma-separated provider or provider:operation tokens",
        onChange: (e) => set({ tokens: e.target.value.split(",").map((t) => t.trim()).filter(Boolean) }),
      }),
      num("minDepth", "depth ≥", "hide effects nearer than this — useful to find the deep-only reaches"),
      num("maxDepth", "depth ≤", "hide effects further than this — the fastest way to cut the CHA fan-out"),
      seg(
        "here only",
        f.directOnly ? "on" : "off",
        "depth 0 only — show only what this line's callee actually does",
        () => set({ directOnly: !f.directOnly }),
        f.directOnly,
      ),
      seg(
        "loops only",
        f.loopedOnly ? "on" : "off",
        "only effects that run once per iteration (tier 2) — mirrors `rig annotate --looped`",
        () => set({ loopedOnly: !f.loopedOnly }),
        f.loopedOnly,
      ),
      seg(
        "dispatch ?",
        f.dispatch,
        "show / hide / only the reaches that exist ONLY through virtual or interface dispatch. On MedDBase these are the majority of badges and none of them is proven.",
        cycle("dispatch", ["show", "hide", "only"]),
        f.dispatch !== "show",
      ),
      seg(
        "below",
        f.distant,
        "the depth>0 fan-out: fold it to one chip (default), expand every badge, or hide it entirely",
        cycle("distant", ["fold", "expand", "hide"]),
        f.distant !== "fold",
      ),
      seg(
        "grain",
        f.grain,
        "collapse to the 8 families (default) or expand to provider:operation",
        cycle("grain", ["family", "provider"]),
        f.grain !== "family",
      ),
    ),
    h(
      "div",
      { class: "lens-filter-row" },
      h("span", { class: "fseg-lbl" }, "findings"),
      seg("⚠ tier 1", tierOn("haz") ? "on" : "HIDDEN", "hazards — a judgment over the effects (n+1, race window, dual write …)", toggleTier("haz"), !tierOn("haz")),
      seg("⟳ tier 2", tierOn("amp") ? "on" : "HIDDEN", "amplification — the effect on this line runs once per iteration (looped_effect)", toggleTier("amp"), !tierOn("amp")),
      seg("⟳↓ tier 3", tierOn("xm") ? "on" : "HIDDEN", "cross-method amplification — the loop is on this line, the I/O is beneath the call", toggleTier("xm"), !tierOn("xm")),
      seg("tier 3 ≥", f.tier3Min, "minimum anchor confidence. `low` anchors are leads, not findings — raise this to medium to see only the ones worth acting on.", cycle("tier3Min", ["low", "medium", "high"]), f.tier3Min !== "low"),
      h("span", { class: "fsep" }, "│"),
      h("span", { class: "fseg-lbl" }, "server"),
      seg("intrinsic", f.intrinsic ? "on" : "off", "include language-intrinsic alloc/throw effects — CHANGES THE QUERY, refetches", () => set({ intrinsic: !f.intrinsic }), f.intrinsic),
      seg("async", f.async ? "on" : "off", "walk async / scheduled handoffs — CHANGES THE QUERY, refetches", () => set({ async: !f.async }), f.async),
      isFilterActive(f)
        ? h("button", { class: "fclear", onClick: () => actions.resetLensFilter() }, "clear filters")
        : null,
    ),
    Disclosure(model, result, f, actions),
  );
}

// MANDATORY DISCLOSURE. A filtered view that looks unfiltered is a lie, so this line is not dismissible
// while anything is non-default, and it always names the quantity hidden — not just "filters on".
function Disclosure(model, result, f, actions) {
  const hid = result.hidden;
  const sf = model.serverFilter;
  const active = isFilterActive(f) || !!sf;
  const bits = [];
  if (sf) {
    bits.push(
      `server filter removed ${sf.hiddenBadges} badge(s), ${sf.hiddenLines} line(s), ${sf.hiddenMethods} method(s) before this page saw them`,
    );
    for (const note of sf.notes || []) bits.push(note);
  }
  if (hid.lines) bits.push(`${hid.lines} marked line${hid.lines === 1 ? "" : "s"} hidden`);
  if (hid.badges) bits.push(`${hid.badges} badge${hid.badges === 1 ? "" : "s"} hidden`);
  if (hid.haz) bits.push(`${hid.haz} hazard${hid.haz === 1 ? "" : "s"} hidden`);
  if (hid.amp) bits.push(`${hid.amp} amplification${hid.amp === 1 ? "" : "s"} hidden`);
  if (hid.anchors) bits.push(`${hid.anchors} tier-3 anchor${hid.anchors === 1 ? "" : "s"} hidden`);
  if (hid.methods) bits.push(`${hid.methods} method${hid.methods === 1 ? "" : "s"} hidden`);
  const folded = hid.folded ? `${hid.folded} distant badge${hid.folded === 1 ? "" : "s"} folded into rollup chips` : "";
  if (!active && !folded) {
    return h(
      "div",
      { class: "lens-disclosure clean" },
      `unfiltered · every mark this store has for this file is on the page — ${model.counts.markedLines} marked lines, ${model.counts.badges} badges, ${model.counts.haz + model.counts.amp + model.counts.anchors} findings`,
    );
  }
  return h(
    "div",
    { class: "lens-disclosure" + (active ? " filtered" : "") },
    active ? h("strong", {}, "FILTERED · ") : null,
    bits.length ? bits.join(" · ") : active ? "no marks removed by the current filter" : "",
    bits.length && folded ? " · " : "",
    folded,
    active ? h("button", { class: "fclear inline", onClick: () => actions.resetLensFilter() }, "show everything") : null,
  );
}

// ---- legend -------------------------------------------------------------------------------------------

// The legend lives at the TOP OF THE OUTLINE — the panel a reader's eye already goes to for "what is in this
// file" — and starts open on a file whose marks a reader has not seen before. One glance must be enough to
// state the whole rule, which is why it is written as the four axes rather than a list of twelve pills.
function Legend(open, onToggle) {
  const row = (mark, meaning) => h("div", { class: "lg-row" }, h("span", { class: "lg-mark" }, mark), h("span", {}, meaning));
  return h(
    "div",
    { class: "lens-legend" + (open ? " open" : "") },
    h("button", { class: "lg-head", onClick: onToggle }, open ? "▾ HOW TO READ THE MARKS" : "▸ how to read the marks"),
    open
      ? h(
          "div",
          { class: "lg-body" },
          h("p", {}, h("strong", {}, "Fill says where, "), h("strong", {}, "? says on what basis, "), h("strong", {}, "⟳ says it repeats.")),
          row(h("span", { class: "fx fx-here" }, h("span", { class: "fx-fill" }, "●"), h("span", { class: "fx-name" }, "db")), "the effect is in this call's body — it happens here"),
          row(h("span", { class: "fx fx-below" }, h("span", { class: "fx-fill" }, "○"), h("span", { class: "fx-name" }, "db"), h("span", { class: "fx-depth" }, "3")), "nearest db effect is 3 calls below"),
          row(h("span", { class: "fx fx-below fx-guess" }, h("span", { class: "fx-fill" }, "○"), h("span", { class: "fx-name" }, "db"), h("span", { class: "fx-depth" }, "3"), h("span", { class: "fx-q" }, "?")), "…and only through virtual/interface dispatch — a guess, not a fact"),
          row(h("span", { class: "fx fx-below fx-rollup" }, h("span", { class: "fx-fill" }, "○"), h("span", { class: "fx-name" }, "5"), h("span", { class: "fx-depth" }, "↓1"), h("span", { class: "fx-q" }, "?")), "5 families below, nearest 1 hop, some are guesses — click to expand"),
          row(h("span", { class: "fx fx-here fx-loop" }, h("span", { class: "fx-amp" }, "⟳"), h("span", { class: "fx-fill" }, "●"), h("span", { class: "fx-name" }, "cache")), "tier 2 — this effect runs once per iteration, not once"),
          row(h("span", { class: "fx fx-anchor conf-high" }, h("span", { class: "fx-amp" }, "⟳↓"), h("span", { class: "fx-name" }, "cache"), h("span", { class: "fx-depth" }, "0")), "tier 3 — the loop is HERE, the I/O is 0 hops beneath this call (high)"),
          row(h("span", { class: "fx fx-anchor conf-low" }, h("span", { class: "fx-amp" }, "⟳↓"), h("span", { class: "fx-name" }, "db"), h("span", { class: "fx-depth" }, "6"), h("span", { class: "fx-lead" }, "lead")), "…at depth 6 the same claim is only a lead — dotted, and it says so"),
          row(h("span", { class: "fx fx-haz conf-high" }, "⚠", h("span", { class: "fx-name" }, "n+1")), "tier 1 — a judgment anchored on this line"),
          h("p", { class: "lg-rail" }, "The rail beside the line number is the scan channel: ", h("span", { class: "rail rail-here lg-chip" }), " happens here · ", h("span", { class: "rail rail-guess lg-chip" }), " a guess happens here · ", h("span", { class: "rail rail-below lg-chip" }), " only below · ", h("span", { class: "rail rail-amp lg-chip" }), " repeats · ", h("span", { class: "rail rail-haz lg-chip" }), " hazard."),
          h("p", { class: "lg-keys" }, "Keys: ", h("kbd", {}, "n"), "/", h("kbd", {}, "p"), " next/previous line that does I/O · ", h("kbd", {}, "N"), "/", h("kbd", {}, "P"), " next/previous finding · ", h("kbd", {}, "l"), " toggle this legend."),
          h(
            "div",
            { class: "lg-fams" },
            ...Object.keys(FAMILY_PROVIDERS).map((fam) =>
              h("span", { class: "lg-fam", title: `${FAMILY_HELP[fam]}\nproviders: ${FAMILY_PROVIDERS[fam].join(", ")}` }, fam),
            ),
          ),
        )
      : null,
  );
}

// ---- minimap ------------------------------------------------------------------------------------------

// The answer to "131 marked lines — is there any way to navigate to the interesting ones". One tick per
// marked line, positioned over the WHOLE FILE (not the loaded page), coloured by the rail precedence, so the
// shape of a file's I/O is visible before any scrolling and a click lands on it — paging first if needed.
function Minimap(rows, extent, onGo) {
  const ticks = [...rows]
    .sort((a, b) => a.line - b.line)
    .map((r) =>
      h("button", {
        class: `mm-tick sev-${lineSeverity(r)}`,
        dataset: { line: String(r.line) },
        style: `top:${((r.line - 1) / Math.max(1, extent - 1)) * 100}%`,
        title: `line ${r.line} — ${[r.haz.length ? "hazard" : "", r.anchors.length ? "tier 3" : "", r.amp.length ? "tier 2" : "", provenDirect(r) ? "does I/O here" : "", !provenDirect(r) && dispatchDirect(r) ? "dispatch-only here" : "", r.distant.length ? `${r.distant.length} below` : ""].filter(Boolean).join(", ")}`,
        onClick: () => onGo(r.line),
      }),
    );
  const view = h("div", { class: "mm-view" });
  return h("div", { class: "mm", title: `${rows.length} marked lines across ${extent} lines` }, view, ...ticks);
}

// ---- the view -----------------------------------------------------------------------------------------

export function FileEffectsView(s, actions) {
  if (!s.filePath)
    return h(
      "div",
      { class: "file-empty" },
      h("h2", {}, "Open an indexed C# file"),
      h("p", {}, "Type a filename or path above, or use ↗ next to a location in Tree."),
    );
  if (s.fileError) return h("div", { class: "file-empty" }, h("h2", {}, "File lens unavailable"), h("p", {}, s.fileError));
  if (!s.fileEffects || !s.fileSource) return h("div", { class: "file-empty" }, "Loading file effects…");

  const source = s.fileSource;
  if (source.origin === "unavailable" || !source.lines.length)
    return h("div", { class: "file-empty" }, `Source unavailable: ${source.reason || "no text"}`);

  const filter = s.lensFilter;
  const model = lensModel(s.fileEffects, findingsFor(s.fileEffects.file));
  const result = applyLensFilter(model, filter);
  const rows = [...result.lines.values()];
  const extent = Math.max(source.endLine, ...(rows.length ? rows.map((r) => r.line) : [source.endLine]));
  const go = (line) => actions.gotoFileLine(line);

  const methodsAt = new Map();
  for (const m of result.methods) {
    if (!methodsAt.has(m.line)) methodsAt.set(m.line, []);
    methodsAt.get(m.line).push(m);
  }

  const hl = highlightCSharp(source.lines.map((line) => line.text));
  const width = String(source.lines[source.lines.length - 1].number).length;
  const provenance =
    source.origin === "git"
      ? source.storeDirty
        ? `git ${source.commit} · dirty index may differ`
        : `git ${source.commit}`
      : "working tree · indexed revision verified";

  const codeRows = [];
  source.lines.forEach((line, i) => {
    for (const m of methodsAt.get(line.number) || []) codeRows.push(MethodRow(m, filter, actions, go));
    const row = result.lines.get(line.number);
    codeRows.push(
      h(
        "div",
        {
          class: `file-code-row${row ? " effect sev-" + lineSeverity(row) : ""}${s.fileFocusLine === line.number ? " focus" : ""}`,
          dataset: { line: String(line.number) },
        },
        h("span", { class: "file-gutter" }, ...(row ? LineMarks(row, filter) : [])),
        h("span", { class: row ? railClass(row) : "rail" }),
        h("span", { class: "srcnum", style: `min-width:${width}ch` }, String(line.number)),
        h("code", {}, ...hl[i].map((token) => (token.cls ? h("span", { class: token.cls }, token.text) : token.text))),
      ),
    );
  });

  // Outline = the whole-file index, not just the loaded page: sortable, and every row jumps. On a 42-method
  // file paged 400 lines at a time the old page-scoped outline showed five, which is how a reader ended up
  // scrolling to find out whether anything worse lived further down.
  const sorted = [...result.methods].sort(
    filter.outlineSort === "severity"
      ? (a, b) => methodWeight(b) - methodWeight(a) || a.line - b.line
      : (a, b) => a.line - b.line,
  );

  return h(
    "div",
    { class: "file-lens grain-" + filter.grain },
    h(
      "div",
      { class: "file-lens-head" },
      h("div", {}, h("strong", {}, baseName(model.file)), h("span", { title: model.file }, model.file)),
      h(
        "div",
        { class: "file-summary" },
        `${result.methods.length}/${model.methods.length} methods · ${result.lines.size}/${model.counts.markedLines} marked lines · ${provenance}`,
      ),
    ),
    FilterBar(s, actions, model, result),
    findingsProvenance()
      ? h(
          "div",
          { class: "lens-mock stale" },
          h("strong", {}, "MOCK SUPPRESSED "),
          `the findings dataset was derived from store ${findingsProvenance().mock}, you are viewing ${findingsProvenance().viewing}. Its marks are line-anchored, so on another store they would land on the wrong lines — no tier 1–3 marks are shown. Effect badges and depths are REAL and come from this store.`,
        )
      : !model.covered
      ? h(
          "div",
          { class: "lens-mock" },
          h("strong", {}, "MOCK "),
          "tier 1–3 findings are not on /api/file-effects yet; this file is not in the local mock dataset, so no findings marks are shown. Effect badges and depths are REAL.",
        )
      : h(
          "div",
          { class: "lens-mock covered" },
          h("strong", {}, "MOCK "),
          `findings for this file come from filelens-findings.mock.json (real \`rig derive\` rows, reshaped) — ${model.counts.haz} hazard(s), ${model.counts.amp} amplification(s), ${model.counts.anchors} tier-3 anchor(s). Effect badges and depths are REAL.`,
        ),
    h(
      "div",
      { class: "file-lens-grid" },
      h(
        "div",
        { class: "file-editor-wrap" },
        h("pre", { class: "file-editor" }, ...codeRows),
        Minimap(rows, extent, go),
      ),
      h(
        "aside",
        { class: "file-outline" },
        Legend(s.lensLegend, actions.toggleLensLegend),
        h(
          "div",
          { class: "file-outline-title" },
          "METHODS",
          h(
            "button",
            {
              class: "outline-sort",
              title: "sort the index by declaration line or by worst finding",
              onClick: () => actions.setLensFilter({ outlineSort: filter.outlineSort === "severity" ? "line" : "severity" }),
            },
            filter.outlineSort === "severity" ? "by severity" : "by line",
          ),
        ),
        ...sorted.map((m) =>
          h(
            "div",
            { class: "file-outline-method" + (s.fileFocusLine >= m.line && s.fileFocusLine <= (m.endLine || m.line) ? " in-page" : "") },
            h(
              "div",
              { class: "om-head" },
              h(
                "button",
                { class: "om-jump", title: `jump to line ${m.line}`, onClick: () => go(m.line) },
                h("strong", {}, m.name),
                h("span", { class: "om-line" }, `:${m.line}`),
              ),
              h(
                "button",
                { class: "om-tree", title: `open ${m.id} as a call tree`, onClick: () => actions.openFileTree(m.id) },
                "↗",
              ),
            ),
            h("div", { class: "om-marks" }, ...TierCounts(m.findings, go), ...m.direct.map((b) => Badge(b, filter.grain)), Rollup(m.distant, filter.grain)),
          ),
        ),
        result.methods.length === 0 ? h("p", { class: "hint" }, "No methods match the current filter.") : null,
        h(
          "div",
          { class: "file-page" },
          h("span", {}, `whole file · ${source.lines.length} lines`),
          source.hasMore
            ? h("strong", { class: "file-trunc", title: "the client stitches 400-line pages; this file exceeded the safety cap" }, `TRUNCATED at ${source.endLine}`)
            : null,
        ),
        !model.columnsAvailable
          ? h("p", { class: "file-limit" }, "Line precision only · several calls on one line share one mark, keeping the shortest distance and the strongest basis per family.")
          : null,
      ),
    ),
  );
}

const methodWeight = (m) =>
  m.findings.haz.length * 1000 +
  m.findings.anchors.length * 100 +
  m.findings.amp.length * 10 +
  m.direct.filter((b) => !b.dispatch).length;

// Navigation targets, in the reader's two useful senses: "lines that do I/O" and "lines with a finding".
// Both respect the active filter, so `n` never jumps to something the page is not showing.
export function navTargets(dto, filter, kind) {
  const model = lensModel(dto, findingsFor(dto.file));
  const rows = [...applyLensFilter(model, filter).lines.values()];
  const want = kind === "finding" ? (r) => r.haz.length || r.anchors.length || r.amp.length : (r) => provenDirect(r) || r.haz.length || r.anchors.length;
  return rows.filter(want).map((r) => r.line).sort((a, b) => a - b);
}
