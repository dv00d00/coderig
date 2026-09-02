# Non-linear effect discovery — rank effects by amplification DEGREE

**Status:** PROGRESS — **SHIPPED 2026-08-28** as `rig amplify` (`b1e2952a`): `FactAmplificationDegreeDeriver`
(pure engine) + `AmplifyCommand` + 15 tests. Runs the whole MedDBase estate (10,109 EPs) in **1m06s**,
emitting 663 super-linear findings + 186 recursion. No store-schema change; no cache key touched. What
remains is listed under "Follow-ups" at the bottom.

Estate-wide sweep capability: classify every (entry point → effect site) path by how many
independent loop contexts stack along it. Degree 0 = constant, 1 = linear (the existing `looped_effect` /
`n_plus_1` tier), **≥2 = super-linear candidate**, plus a RECURSION flag for effects inside a call cycle
(unbounded degree).

## Why the existing tiers can't answer this

rig already has three amplification tiers, and none of them composes:

- **tier 2 `looped_effect`** (`observations.amplification`) — INTRA-METHOD: the effect is lexically inside an
  iteration context in its own enclosing method. Sound, but it is a boolean, not a degree; a effect in a loop
  called from a loop reads identically to one called once.
- **tier 3 `cross_method_amplification`** — anchor-grain: one looped call site plus a gated IO witness beneath
  it. It correlates exactly ONE loop edge with a downstream effect. Two stacked loops produce two independent
  anchors, never a single "this is quadratic" finding.
- **`n_plus_1`** — a judgment about whether the key varies at ONE looped read.

The missing primitive is **composition along a path**: caller loop × callee loop across call edges, through
virtual dispatch and lambdas. That is the difference between "N round trips" and "N² round trips", and it is
the only tier that distinguishes a slow page from an outage.

Commit `5488a2ab` (receiver-context dispatch expansion) is a prerequisite: before it, loops hidden behind a
`Save()`/`Delete()` override were invisible, so the flagship quadratic chain
(`WizardBase.Book` → `WizardServices.AddRealService` loop → `AppointmentServiceEntity.Save` override →
`Appointment.BuildScheduleServicesCache` loop) could not be seen at all.

## Degree model

`deg(M) = max( ownLoopDepth(M) , max over call edges M→C of ( loopBit(edge) + deg(C) ) )`

A forward DP over the call graph, memoized per node. Two contributing sources:

1. **Cross-method (hard, ✔).** A call edge carries `call_edges.LoopKind`/`LoopDetail` when the call site sits
   in an iteration context. Loops in DIFFERENT method frames are independent by construction — there is no
   correlation to argue about — so each loop edge crossed adds exactly 1. This is the load-bearing signal.
2. **Intra-method nesting (heuristic, ~).** The store records only the INNERMOST loop per call site
   (`EnclosingLoopKind` is a single value; there is no nesting depth or parent-loop id at the fact layer).
   Nesting is recovered by **line-span containment**: group a method's edges by `LoopDetail`, take
   `[min(Line), max(Line)]` as each loop's span, and treat loop B as nested in loop A when B's span is
   strictly contained in A's. Marked `~`, never `✔`.

### Why span containment is the right answer to the correlation requirement

Sibling loops over the same collection are ADDITIVE, not multiplicative, and must not read as degree 2.
Span containment gets this right for free: siblings have disjoint spans, so they never compose. Measured on
the MedDBase store `2f944e739e47-dirty` (2026-08-28):

| methods with ≥2 distinct loops | 1,290 |
| …with a genuinely NESTED pair (strict containment) | **240 (18.6%)** |
| …sibling-only (disjoint spans) | **1,050 (81.4%)** |

So a naive "count distinct loops per method" would overcount 81% of multi-loop methods. Real ground truth
confirms the discrimination: `InvoiceEntity.RecalculateTotal` has two loops (`credit in invoice.CreditNote`,
`item in invoice.BillingItem`) that are siblings → correctly degree 1, while
`Appointment.BuildScheduleServicesCache` has `serviceGroup in module.AppointmentServiceGroups` (lines
381–397) strictly inside `moduleGroup in AppointmentServiceModuleGroups` (375–414) → correctly degree 2.

**Known imprecision (disclosed, not fixed):** two SIBLING loops with the *same* `LoopDetail` text separated by
a third loop merge into one span that then appears to contain the third. Rare; it is why the intra-method
contribution is tagged `~`. The principled fix is a fact-layer loop-nesting depth (parent-loop id per
reference), which is a **schema bump** and orphans every existing store — deliberately out of scope here.

## Recursion

A method that is self-reachable (in a call-graph SCC of size > 1, or carrying a self-edge) and that reaches an
in-scope effect has **unbounded** degree — depth is a runtime property. Reported as a separate RECURSION
section rather than given a number. Ground truth: `TemplateEntity.HtmlService.ExpandSection` ↔ `Sections`
(structured-template expansion, width × depth).

## Requirements

1. **Multiplicity counts per CALL SITE, not per method.** rig's existing rollups dedupe per method; that trap
   would collapse two distinct looped call sites in one method into one finding.
2. Correlated sibling loops must not multiply (above).
3. **Ranking:** degree desc → effect kind (db round trips / `llblgen` first; `actor:tell` is fire-and-forget
   queueing and gets its OWN section; `lock:acquire` and reflection witnesses are contention/assembly-load
   amplifiers, historically noisy, and are excludable) → entry-point count.
4. **Output names every hop**: entry point, each loop in the chain with its range variable and method, the
   terminal effect, and `file:line` per hop — actionable without re-running rig.
5. **Sweep is one command** over all entry points of the main app, completing in minutes with caps.
   MedDBase has 10,109 entry points, so a per-EP shell-out is not viable — the sweep must be one in-process
   graph pass. (Baseline: `rig derive --format tsv` = 26 s.)

## Surface

New command rather than a flag on `derive`: the output grain (a ranked path chain) does not fit any existing
section, and the sweep needs its own caps. Reuses the existing shaped-graph load + dispatch expansion — it
must NOT re-implement traversal over raw `call_edges`, or it loses virtual-dispatch and lambda edges (and
with them the flagship chain).

## Web slice (follow-on, per CLAUDE.md decide-at-design-time)

The output is a ranked report, so it qualifies. Scoped as an explicit follow-on, NOT in v1: a degree-ranked
amplification view reusing the hazards mark stream. Needs its own cache-key thinking (query-side feature).

## Notes

- TSV escaping defect found while baselining: multi-line LINQ query text in `LoopDetail` leaks raw newlines
  into `derive --format tsv`, breaking row parsing (visible as stray `.Where(...)` lines in a row-type
  histogram). The new command must collapse whitespace in loop detail; the existing rows want the same fix.

## What shipped (2026-08-28, `b1e2952a`)

- `src/Rig.Domain/Functions/FactAmplificationDegreeDeriver.cs` — anchors (per CALL SITE) from the existing
  calibrated `FactIterationFanoutDeriver`; one `FactPathFinder.OpenSession` reach pass in batches, keeping only
  (reachable anchor callers, nearest in-scope effect) and discarding each reach set; iterative Tarjan SCC →
  condensation in emission order; `bearing` gates which successors compose; argmax chain reconstruction.
- `src/Rig.Cli/Commands/AmplifyCommand.cs` — options, three sections (super-linear / fire-and-forget
  `actor:tell` / recursion), EP attribution for reported findings only, human + TSV rendering.
- Scope gate stays pure rules data via `AmplificationScope`; no provider is named in the deriver.

### Measured on `2f944e739e47-dirty`

degree 1: 2,051 · **2: 509 · 3: 82 · 4: 33 · 5: 28 · 6: 7 · 7: 4** · recursion: 186.
Confidence on the degree≥2 set: 368 ✔ / 35 ~. Rediscovery: all four explicit degree≥2 ground-truth families
and both recursion items found; the 6 triage-list items not surfaced are single-loop or loop-free methods
correctly classified degree 1. Full results + scorecard: the sweep write-up (scratchpad, not in-repo).

### The intra-method LINQ fold (found in review, on the real store)

One query expression emits one detail per clause (the detail carries the CUMULATIVE bind set), whose spans
nest — so containment read `Register.GetRegisterByInvoiceDate` as **5 stacked loops for one query**. Fixed by
folding subset-related `query` details into one loop FAMILY (union-find over `IterationContext.LoopIdentifiers`,
family spans unioned, containment tested between families — transitive). After the fold no chain hop in the
estate exceeds intra-depth 2. This deliberately also folds a genuine multi-`from` cross product: the facts
cannot separate a cross-product `from` from a `join`/`let`, and precision on degree≥2 was the stated priority.

## Follow-ups

- **Web slice** (per the decide-at-design-time rule): a degree-ranked view reusing the hazards mark stream.
  Needs its own cache-key thinking — `amplify` is query-side and currently uncached.
- **No caching.** `QueryCacheKeys.cs` untouched; a warm-cache story needs a `*Schema`-style token.
- **Interior hops are their own findings.** A degree-3 chain also yields its degree-2 tail, so the top of the
  list carries near-duplicates differing only in hop 1. Dedupe by chain tail would compress it.
- **Recursion duplication** — 186 findings / 146 distinct heads; an anchor that merely REACHES a cycle is also
  reported unbounded. Dedupe by SCC.
- ~~Effect-kind ranking is a C# presentation table~~ — **FIXED, see "Core principle correction" below.**
- **Fact-layer loop nesting depth** (parent-loop id per reference) would retire the span-containment heuristic
  and its residual same-detail merge. Schema bump — **orphans every existing store**, so deliberately deferred.

## Core principle correction (2026-08-28, follow-up commit)

The first cut shipped the effect-kind ranking as a C# array (`AmplifyCommand.KindOrder` — `llblgen`,
`db_command`, …) plus a `FireAndForget = "actor:tell"` constant, flagged at review as "presentation-only, can
never admit or suppress a finding". **That reasoning was wrong and the deviation should have been blocking.**
Rig core carries NO project-specific data: provider/operation tokens are the vocabulary of a particular
codebase's ruleset — Echo actors (`actor:tell`) exist in exactly one repo — so a ranking table, a category
grouping, or a default exclusion list in core bakes one project's domain into the tool, regardless of whether
it gates anything.

Fixed by making grouping/ranking/exclusion rules DATA, mirroring how `observations.amplification` already
makes the scope data:

- `observations.amplificationCategories` — ordered list, FIRST MATCH WINS, `providers`/`operations` empty =
  "any". Per category: `name`, `weight` (lower sorts first), `separate` + `label` (own section), `excluded`
  (drop from display). Authored in `RuleDocument.AmplificationCategoryObservationRule`, projected to
  `FactAmplificationCategoryRule`.
- `Rig.Domain/Functions/AmplificationCategories.cs` — the generic matcher (`For` / `Rank`), companion to
  `AmplificationScope`. Names no effect.
- `AmplifyCommand` now ranks, sections and excludes purely by configured category, and the separate section's
  HEADING comes from the category's `label`.

**Core ships NO default categories.** Absent config = a NEUTRAL default: one implicit group, no weighting, no
separate sections, no exclusions, so findings order by degree then site. Deliberately unopinionated rather
than wrong. MedDBase's categories live on the MedDBase side; a ready-to-use overlay is in the sweep scratchpad
(`amplify-categories.rules.json`), layered with a second `--rules` flag.

Tests: the fire-and-forget test now asserts BOTH directions (no category ⇒ ranks in the main list; category
⇒ moves to its own section), plus a new `An_excluded_category_is_dropped_from_every_section`. 16 tests.

### Pre-existing violations of the same principle (NOT introduced here, not fixed here)

A repo-wide grep found the same class of hardcoding already in core, unrelated to this feature:

- `src/Rig.Cli/Effects/EffectDerivation.cs:257` — `EffectPredicate(Provider: "llblgen", Operation: "bulk_write")`.
- `src/Rig.Domain/Functions/FactHazardDeriver.cs:333-335` — a dictionary mapping `llblgen:write`/`:delete`/
  `:tx_commit` to a `"db"` family.

(`AmplificationScope.cs` also matches a grep, but only inside a comment showing the JSON shape — that is fine.)
Worth a separate pass; both are effect vocabulary living in core C#.

## Remainder extracted

Moved `progress/` -> `done/` on 2026-09-02 when `progress/` was unbundled into a shipped record plus its
tail. Everything above is unchanged. The open items now live on their own cards:

- [Web slice for `rig amplify`](../needs-review/amplify-web-slice.md)
- [`rig amplify` is uncached](../needs-review/amplify-is-uncached.md)
- [Interior hops are their own near-duplicate findings](../needs-review/amplify-interior-hops-are-near-duplicates.md)
- [Recursion findings duplicate per SCC](../needs-review/amplify-recursion-findings-duplicate-per-scc.md)
- [Fact-layer loop-nesting depth would retire the span-containment heuristic](../needs-review/fact-layer-loop-nesting-depth.md)
- [Multi-line `LoopDetail` breaks `derive --format tsv` row parsing](../todo/loop-detail-newlines-break-derive-tsv-rows.md)

The "pre-existing violations of the same principle" recorded above (`EffectDerivation.cs:257`,
`FactHazardDeriver.cs:333-335`) were fixed as F1 and F3 of
[core-purity-project-vocabulary](./core-purity-project-vocabulary.md), so they get no card.
