# `n_plus_1` is INTRA-METHOD: cross-method amplification (loop in caller, read in callee) is invisible

**Status:** IN PROGRESS — step 1 of 3 (the dataset instrument) landed 2026-08-03; see "Step 1" at the bottom · **Found:** 2026-08-03, measuring FR-3 recall against preprod runtime data after shipping
[n-plus-1-iteration-contexts-beyond-loop-statements](../done/n-plus-1-iteration-contexts-beyond-loop-statements.md)
· **Family:** hazard-recall / FR-3 · **Tier:** graph (not effect-local)

## Evidence

The preprod dashboard "Fat Spans — Wall + SQL Roundtrip Triage" (`np-hyperdx`, tile "N+1 hotspots") gives
runtime-CONFIRMED per-endpoint read amplification — ground truth to measure static recall against. Top 8 by
total SQL seconds, checked against a full-solution store (`32f4dac9dc7b`, rig with the iteration-context fix):

| Endpoint | Hot query | calls/trace | total_sec | rig `n_plus_1`? |
|---|---|---|---|---|
| `Document/BrowserComponent/HtmlEdit2` | `OBJECT_HOLDER` | 1,099.8 | 86.2 | ✗ |
| `Account/Configuration/Main` | `DEPARTMENT_CODE` | 341.9 | 74.1 | ✓ (Main.cs:1039, in a lambda) |
| `Admin/Profile/Home2` | `PROFILE` | 2,347 | 35.7 | ✓ (Home2.cs:294/296) |
| `Workflows/ReferralOutbound/ListPane` | `OBJECT_HOLDER_INDEX` | 73.7 | 27.1 | ✗ |
| `Prescription2/Edit` | `sandbox.FDB` | 202.1 | 23.7 | ✗ |
| `Profile/StatusPanel` | `WORK_MEMBER`+ | 1,340.5 | 11.5 | ✗ |
| `Admin/CommonCatalogues2/Home` | `OBJECT_HOLDER` | 157.6 | 10.8 | ✓ (3 findings) |
| `Document/…/NewHtmlDocumentFromTemplate` | `OBJECT_HOLDER_INDEX` | 298 | 8.0 | ✗ |

**3 of 8.** The misses are NOT the iteration-context gap that was just fixed — that one was lexical and is
covered. `HtmlEdit2` was checked under full forward reachability, not just its own file:

- `rig tree HtmlEdit2 --view hazards` → **18 hazards** (lazy_init_race, thread_local_context, dual_write,
  race_window) and **zero n_plus_1**.
- The same reachable tree contains **20 loop-marked (`🔁`) nodes**.

So loops ARE present on the path and reads ARE present on the path, but no read is LEXICALLY inside a loop in
its OWN method. `FactObservationDeriver` derives `n_plus_1` from the effect's own `EnclosingLoopKind` +
argument surface, both intra-method facts, so `foreach (x in xs) Helper.Load(x)` where `Helper.Load` does the
read is structurally invisible: the loop is in the caller's frame, the read in the callee's.

## Why this is the next tier, not a tweak

Two sub-problems, neither effect-local:

1. **Loop propagation across edges.** "Is this effect reachable from a call site that is itself in an
   iteration context?" The substrate exists — `NearestLoopKind` over the BFS reach already drives the tree's
   `🔁` and is consumed by `ImpactEngine` — so the reachability half is largely solved. The finding would move
   from effect-attached to graph-tier, alongside `event_cycle`/`cache_coherence`.
2. **Key propagation, which is the hard half.** The varying-key discriminator is what makes `n_plus_1`
   precise rather than "any read under a loop". Across a call boundary the loop variable becomes a PARAMETER,
   so deciding the key still varies needs the loop variable → argument → parameter → read-key chain. Without
   it the finding degrades to `looped_effect`-under-reach, which on MedDBase would be enormous (109,825 query
   sites + 31,740 foreach sites, most of them calling something that eventually reads).

Cheap partial worth considering first: propagate one hop only, and only when the loop variable is passed
DIRECTLY as an argument at the call site. That covers the `foreach (x in xs) Helper.Load(x)` shape — likely
the common one — without a general dataflow pass.

## Also visible in the same measurement — INVESTIGATED, and it is NOT this gap

All 175 current `n_plus_1` findings are `entity_cache:read`. The first guess was that this shared the
cross-method root cause. **That was investigated and refuted** (2026-08-03): it is two INDEPENDENT defects,
tracked separately in [n-plus-1-key-capture-and-gate-defects](n-plus-1-key-capture-and-gate-defects.md).
Iteration context IS found for looped `llblgen:read` (14 sites) and `object_store:read` (33 sites) — they
carry `looped_effect` and fail only at the key/gate stage, so they are not instances of this cross-method
problem. Only redis/inproc_cache/efcore/repository/fhir/elasticsearch/azure_search have genuine zeros, and
those are vacuous (5 of the 12 gated providers have no effect rule at all in the MedDBase ruleset).

**The finding that matters for THIS item:** `entity_cache` fires only by syntactic accident —
`Cache.New(chamber.PfkCompany)` happens to be a bare member-access, so the key lands in `FirstArgumentName`.
The varying-key discriminator is therefore far more brittle than 175 findings suggests: it works only when the
key is a syntactically bare identifier/member path at the read's OWN call site. Any cross-method key
propagation designed here is strictly harder than an intra-method base case that is itself only working for
one syntactic shape — so the arg-surface capture fix is likely a PREREQUISITE milestone, not an unrelated bug.

## Step 1 of 3 landed (2026-08-03) — a DATA-GATHERING instrument, not a detector

Sequencing per the design's plan of record (docs/design-effect-cep-n-plus-1.md, "PLAN OF RECORD after review"):
**(1) promote loop effects and gather a dataset -> (2) analyze it -> (3) derive the amortization rules from the
shapes the data actually shows.** Only step 1 exists. Nothing about caching, tiering, hub suppression or
on-by-default is encoded, deliberately: those are step 3's decisions, to be made on step 2's evidence.

What shipped:
* `FactIterationFanoutDeriver` — pure `FactInvocation` -> `DerivedEffect` pseudo-events (`iteration:fanout`),
  `EnclosingSymbolId` = **the CALLEE** (so the existing reach step means "read reachable AT OR BENEATH the
  per-iteration call" with zero reach changes), file/line = the call site, `Caller` carried beside it.
  Emitted for keyed AND keyless sites — presence is the finding, so a null key token is data.
* `CorrelationPolarity.Presence` + `CorrelationKeyMatch` + nullable witness/dispatch fields on
  `CorrelationFinding` (Absence output byte-identical, FR-7 `cache_coherence` unmoved at 4).
* `IterationContext` — the iteration-context union and the whole-word key test, extracted so the intra-method
  detector and this one cannot drift.
* Opt-in rules section `crossMethodAmplification` (absent = off, mirroring `cacheCoherence`) and a
  `derive --format tsv` row type `cross_method_amplification` at **(anchor x witness) grain** — the full cross
  product, which is what a cross-tab needs and emphatically not a review surface. NOT a `HazardKinds` member:
  admitting it would swamp the Hazards view and move `rig impact`'s hazard deltas.

**NO `maxDepth` semantic gate and NO key gate** — only the resource bounds (`MaxDepth` 6 by rules default,
`maxNodes` 20000). Depth, guards, dispatch basis/via/degree, the iterated source expression and the key token
are all emitted as COLUMNS so step 2 can measure what each explains.

The dataset and its descriptive breakdown live in `meddbase-analysis` (the grounded-roadmap side), per the
existing split. Step 2 is the analysis; step 3 derives the rules.

## 2026-08-04 — renamed + widened to general amplification (no back-compat)

The rule section is now `crossMethodAmplification`, the finding type `cross_method_amplification`, and the
gate is `witnesses: [{providers, operations}]` (the tier-2 `observations.amplification` shape) plus
`excludeWitnessProviders`. An empty/omitted `witnesses` list = ALL effects except the exclusions (default:
alloc, throw, shared_state, config) — the all-IO mode. Rationale: N+1 is the read subset of the general
finding; the read-only gate demonstrably hid looped SENDS (echo:tell / queue:publish / smtp / http:POST)
during the 2026-08-04 MedDBase hotspot triage. Old key/fields are DROPPED, not aliased.
