# Design: cross-method `n_plus_1` as a 2nd derivation over effects (CEP phase-0, scoped)

**Status:** DESIGN ONLY (no code). **Date:** 2026-08-03. **Scope:** the phase-0 design doc called for by
[cep-over-effects](backlog/todo/cep-over-effects.md), narrowed to ONE detector —
[cross-method N+1 amplification](backlog/todo/n-plus-1-cross-method-amplification.md). Where a decision
generalizes past this detector it is marked **[generalizes]**; where it is a deliberate local shortcut it is
marked **[local]** with the condition under which it must be retired.

Every number below was measured on the full-solution store `32f4dac9dc7b` (run
`292715de5e5b46b09f31690fa4a1e7bc`, 440,241 symbols / 2,415,964 reference facts) unless stated otherwise.
Measurements marked **(lower bound)** were computed over the *stored* `call_edges` + `dispatch_edges` tables
with a hand-rolled BFS, i.e. WITHOUT rig's query-time CHA fallback, generic narrowing and delivery edges, so
the real closure is larger.

---

## PLAN OF RECORD after review (2026-08-03) — read this before §3/§4

The design below was reviewed with Dmytro and **substantially reframed**. Where this section conflicts with
§3, §4 or §5, this section wins; those sections are kept because their measurements are still valid, but their
*conclusions* about the key are superseded.

**1. The key was never a test of amplification — it is a cache-amortization predicate.** C# statements are
eager, so a read reachable inside an iteration context executes per element regardless of its key. The
canonical FR-3 "fix" (`foreach (id in ids) Get("/vars/all")`) still issues N round-trips and the shipped
detector is silent on it. So **presence is the finding**; the key only ever spoke to whether a *cache* would
absorb the repetition. Consequence: the 4-hop chain is not a precondition for emitting anything, and the
`high`/`medium`/`low` tier model of §4 collapses into an amortization question.

**2. A cache does not imply amortization — cardinality does.** `ProfileCache.New(p.PkProfile)` while iterating
profiles produced 11,461 SQL reads because key cardinality is **N by construction**, so first-pass hit rate is
0. The real predicate is *"is the key's domain bounded independently of N, or tied to it?"* The high-signal
statically-derivable form is a **self-keyed read over the iterated entity** (iterate X, read X by that
element's own identity ⇒ cardinality = N ⇒ zero amortization). This retro-predicts every confirmed hotspot in
§8 (PROFILE→ProfileCache, departmentCode→DepartmentCodeEntity, index→ObjectHolderIndexEntity). The
counter-case — iterate rows, read a cache by a *foreign/classification* key (`row.FkDepartmentCode`, domain
bounded by the referenced table) — is what genuinely amortizes, and is precisely what the varying-key rule
wrongly flags.

**3. Amortization rules are NOT to be designed speculatively.** Dmytro's sequencing, adopted:
*(1) implement loop-effect promotion to gather a dataset → (2) analyze it → (3) derive the amortization rules
from the shapes the data actually shows.* Only step 1 is in scope now. Do not encode a cache/amortization
heuristic in step 1 — capture the evidence needed to derive one.

**4. `maxDepth` is not a semantic gate.** Recall is monotone in depth (1,098 @2 → 1,927 @6 → 2,185 @19) and the
finding grain (one per anchor) already bounds volume, so the cap was being credited for the grain decision's
work. Keep only the existing resource bound (`MaxDepth = 20`, `maxNodes = 20000`) and **emit depth as data**.
Note §5's "<500 volume gate" was already 4× under water at 1,927, so depth was never the lever that could
meet it — suppression and archetype ranking are, and their effect is unquantified.

**5. Store cost is not a cost.** A store is a derived artifact of an immutable commit; dropping and re-mining
is `rm -rf` + ~8 min. §4's "every existing store must be re-mined … this is the entire cost" is withdrawn, as
is Slice 3's advice to wait for an unavoidable schema bump. Add facts when they earn their keep.

**6. Guards, not keys, are the real precision lever.** A read behind a rarely-true `if` inside the loop body
executes ~never whatever its key. `EnclosingGuards` exists intra-method (it does not compose across the
boundary). Emit it as evidence in step 1 so step 2 can measure how much it explains.

**7. Two caveats on the eager premise, both to be carried as data.** Our newest iteration contexts are the
LAZY ones — a `query`/enumerating-lambda body runs 0 times if never enumerated and 2N if enumerated twice — so
"eager by default" does not hold for them. And "cache" is broader than the blessed `*Cache` seam: a local
`Dictionary` memo or a `??=` lazy field amortizes identically.

**Step 1 is therefore a DATA-GATHERING INSTRUMENT, not a review surface.** Emit richly and filter nothing; the
tiering, suppression and amortization decisions are step 3's, made on step 2's evidence.

---

## 0. Corrections to the premises this design was handed

Stated plainly, per house convention, before anything is built on them.

1. **The "flood" argument in doc #2 is off by roughly an order of magnitude.** It reads "109,825 query sites
   + 31,740 foreach sites, most of them calling something that eventually reads". Those two counts are over
   **all** reference kinds; the call-edge-bearing subset (`invocation`+`ctor`) is **51,835 query / 16,363
   foreach**, and restricted to in-source callees inside an identifier-binding iteration context it is
   **51,181 call sites**. Of *those*, the fraction with any read effect in their forward closure is **14% at
   depth ≤ 19** (7,665 sites) — not "most" (lower bound). The unkeyed detector is therefore ~7.6k findings,
   not ~140k. That is still 44× today's 175 and still unusable as a default surface, so the *conclusion*
   survives; the *magnitude* does not, and the design must be sized to 7.6k not 140k.

2. **`ReachInfo.NearestLoopKind` is not a sound "is this reached under a loop" oracle.**
   `ReachesWithFanoutCore` (FactPathFinder.cs:439-507) writes `ReachInfo` **only on first reach** — the
   `info.ContainsKey(s.Node)` guard `continue`s, and even the binding-growth re-enqueue does not revisit the
   record. So loop tags are BFS-shortest-path-wins, not a union over paths: a helper reached first by a short
   non-looped path carries `NearestLoopKind == null` even though a longer looped path exists. That is fine for
   the tree's 🔁 glyph (a rendering hint about *the path shown*) and wrong as a detector predicate. The brief's
   "the loop-window over the forward tree already EXISTS" is true for rendering and **not** for a sound query.
   **Design consequence:** this detector must take its loop knowledge from the **anchor side** (the call site's
   own `LoopKind`/`LoopDetail`, an exact intra-method fact) and seed the reach at the **callee**, never from
   `NearestLoopKind` on a reached node. That happens to be both sound-for-findings and cheaper.

   **INDEPENDENTLY VERIFIED IN REVIEW (2026-08-03)** — this is the keystone of the design, so it was checked
   against source rather than accepted. Confirmed: the guard is
   `if (info.ContainsKey(s.Node)) { if (grew) queue.Enqueue(s.Node); continue; }`, and the `continue` skips the
   `info[s.Node] = new ReachInfo(...)` assignment entirely, so the record is never updated after first reach —
   the binding-growth re-enqueue re-expands *dispatch* but does not revisit the loop tag. Likewise confirmed
   for §0.3: `ReachesFromEachSeed` really does `new HashSet<string>(InfoFor(...).Keys, ...)`. Do not "simplify"
   this detector later by reading `NearestLoopKind` off the reach — it would compile, pass casual review, and
   be silently unsound in the under-report direction (a helper reached first by a short non-looped path carries
   a null loop tag even when a longer looped path exists).

3. **`ReachesFromEachSeed` throws away the information this detector needs.** It projects
   `InfoFor(...).Keys` into a `HashSet<string>` (FactPathFinder.cs:292-298). Depth — needed for
   confidence tiering and for picking the *nearest* witness — is computed and discarded. A sibling that
   returns `IReadOnlyDictionary<string, ReachInfo>` per seed is required.

4. **Parameter names are confirmed absent from the fact model.** `SymbolFact.Signature` is a display
   signature carrying parameter **types** only (verified against the store:
   `…APIHttpContext.getPath(System.Web.HttpRequestBase, string)`), and no other fact carries them.
   `ArgumentNames`/`ArgumentTemplates` exist per call site (index-aligned JSON `string?[]`), plus the
   unindexed `FirstArgumentName`/`FirstArgumentTemplate` fast path. So the missing link is exactly and only
   *the callee's ordered parameter-name list*. The brief is right.

5. **The intra-method base case is lossy, and its 175 findings are a syntactic accident** (per the
   coordinator's correction, independently confirmed here). `FactExtractor.ArgumentListOf`
   (src/Rig.Analysis/Extraction/FactExtractor.cs:1319) captures an argument's name only when the argument
   expression is *itself* `IdentifierNameSyntax` or `MemberAccessExpressionSyntax`; anything composite
   (`Fields.Name == s.Trim()`, `x.Pk + 1`, a cast, a ternary) yields `null` and `KeyVariesWith` has nothing
   to match. `entity_cache` dominates the 175 because `Cache.New(chamber.PfkCompany)` happens to be a bare
   member access. **Do not** treat "all 175 are `entity_cache:read`" as evidence about which providers
   amplify — 5 of the 12 gated providers have no effect rule at all in the MedDBase ruleset, so their zeros
   are vacuous, and `object_store` is simply missing from `observations.nPlusOne.providers`.
   Measured blast radius of the capture gap **on the anchor side of this design**: of the 51,181 in-source
   looped call sites, **38,333 have `ArgumentNames == null` entirely**, of which **3,972 call a callee that
   takes parameters** (i.e. arguments existed and none was nameable), and a further **4,258 sites** have at
   least one `null` hole in an otherwise-populated list. Anchor population roughly **doubles** when
   `ArgumentListOf` is fixed. This makes the arg-surface fix a **prerequisite milestone of this design**, not
   an unrelated bug (§7, Slice 0).

---

## 1. Event model

### The event
Two event species participate. Only the second is new.

| Role | Species | Source |
|---|---|---|
| **companion** (the finding's payload) | a `DerivedEffect` whose `provider:operation` is in the `nPlusOne` read gate | existing stage-2 effect derivation |
| **anchor** (the finding's site) | an **iteration-fanout pseudo-event**: an in-source call site that (a) sits in an iteration context and (b) passes a per-iteration identifier as argument *k* | NEW pure derivation over `ReferenceFact` (`EnclosingLoopKind`/`EnclosingLoopDetail` + `ArgumentNames`/`ArgumentTemplates`) |

The anchor is a *derived* event, not a mined one: it is the same union of iteration contexts the shipped
lexical detector already understands (`foreach` iteration variable, `query` range variables, rule-declared
enumerating-lambda parameters), applied to a **call site** instead of to an **effect site**. That symmetry is
the whole idea: the shipped detector asks "does the *read* sit in an iteration context with a varying key";
this one asks "does a *call* sit in an iteration context with a varying key, and is a read reachable beneath
it".

### The correlation key
Not `ResourceKey`. **[generalizes: the CEP doc's "correlation key = ResourceKey" is too narrow — it is
whatever identity the operator joins on.]** Here the join identity is a **key token** — a name that denotes
the per-element value as it travels:

```
  loop identifier          i          (anchor side, exact: EnclosingLoopDetail)
      ↓ appears in
  argument k               xs[i].Pk   (anchor side, exact when captured: ArgumentNames[k])
      ↓ binds to
  callee parameter k       pk         (MISSING FACT — §4)
      ↓ appears in
  read key argument        pk         (companion side, exact when captured: ArgumentNames/Templates)
```

Three of the four hops are already facts. Hop 3 is the whole cost of tier `high` (§4).

### What plays the role of "time"
Forward reachability from the **callee of the anchored call site**, bounded by depth — exactly the shipped
substrate (`FactPathFinder.ReachesFromEachSeed`, `narrowDispatch: true`, `TraversalMode.SyncCut`). Per §0.2,
loop-ness is decided at the anchor, so "time" here means only *"the read is downstream of the per-iteration
call"*. Structural, not executional; see §6.

### The window
**forward-tree ∩ loop**, seeded at the callee, `MaxDepth` default **6** (`rig`'s reach default is 20).
Rationale from measurement — keyed anchors with ≥1 read effect in their closure (lower bound):

| depth cap | keyed anchors with a read below | share of 5,410 |
|---|---|---|
| 2 | 1,098 | 20% |
| 6 | 1,927 | 36% |
| 10 | 2,089 | 39% |
| 19 | 2,185 | 40% |

Past ~6 the marginal recall is 4 points while the *witness* count per anchor explodes (§5). Depth is rules
data (`maxDepth`), default 6, calibration knob.

> **CORRECTED IN REVIEW (2026-08-03).** The first draft of this section set the default to **3**, which no
> measurement here supports: there is no depth-3 row, the knee the prose identifies is at ~6, and — decisively
> — the entire §8 oracle projection (strict recall 5/8, the 30-45× precision ratio) was measured at **d ≤ 6**.
> A default of 3 would have made the acceptance criterion in §8 unverifiable by the shipped configuration, and
> the unmeasured 2→6 band is a 43% swing in anchor population (1,098 → 1,927), so 3 was not interpolatable
> either. Default is now 6, matching both the knee and the depth every recall number in this document was
> measured at. If a later calibration wants 3, it must re-measure §8 at 3 and restate the oracle table.

The window explicitly does **not** include held-scope or EP-reach. A transaction-`using` around the loop is
relevant *evidence* (an N+1 inside a transaction is worse) but not part of the correlation; note it as a
future ranking input, not a gate.

---

## 2. The operator, and exactly how the shipped seam generalizes

### Which operator
`co-presence-join` in the CEP doc's operator set — i.e. the shipped
`Relation = CompanionForwardReachable` with **`Polarity = Presence`**. The CEP doc's migration map says
`N+1 = aggregate(read, loop-window)`; that is right for the *intra-method* detector (already shipped inside
`FactObservationDeriver`) and **wrong for the cross-method one**, which is a join between two distinct event
species over a reachability window. Recorded as a correction to the migration map: cross-method N+1 is
`presence-join(iteration_fanout, read, key-token, fwd-tree≤D)`, and the `aggregate` framing only ever
described the lexical case. **[generalizes]**

### Does the anchor concept have to widen?
`CorrelationSpec.Anchor` is an `EffectPredicate` over `DerivedEffect`. A looped call edge is not an effect.
Two options:

* **(A) Widen `Anchor` to a union** (`AnchorSpec = EffectAnchor | CallEdgeAnchor`), touching the spec type,
  the anchor-collection loop, and every existing construction site.
* **(B) Synthesize a pseudo-effect** and leave `CorrelationSpec.Anchor` alone.

**Decision: (B) now, (A) when the DSL lands. [local]** The iteration-fanout event is *shaped* like an
effect — it has a provider-ish kind, a resource-ish key, an enclosing symbol, a file and a line — and it is
derived from facts by a pure function, exactly like every `DerivedEffect`. Synthesizing it is not a hack that
lies about the model; it is an admission that `DerivedEffect` is already the repo's generic "event" record
and only its *name* says "effect". Concretely:

```
FactIterationFanoutDeriver.Derive(facts, rules) -> IReadOnlyList<DerivedEffect>
    Provider          = "iteration"
    Operation         = "fanout"
    ResourceType      = the key token carried across the boundary (ArgumentNames[k]), or "" when keyless
    EnclosingSymbolId = the CALLEE  ← load-bearing, see below
    FilePath/Line     = the CALL SITE in the caller (where a human fixes it)
    Observations      = [ looped_call(kind, detail, argIndex=k, identifier=i) ]
```

`EnclosingSymbolId = callee` is the trick that makes the existing reach step mean the right thing with **zero
changes**: `FactCorrelationDeriver` seeds `ReachesFromEachSeed` at each anchor's `EnclosingSymbolId`, and the
reach set *includes the seed*, so "companion forward-reachable from the anchor's enclosing method" becomes
"read reachable at or beneath the per-iteration call" — precisely the intended semantics. It also means a
read in the callee's own body (depth 0) is found, which is the common `foreach (x in xs) Helper.Load(x)`
shape. The cost of the trick is that `Enclosing` on the emitted finding must be overridden to the CALLER for
human consumption; the mapping function does that.

Retire (B) for (A) at CEP phase 3 (the JSON pattern DSL), where a declarative anchor genuinely needs to name
call edges as a first-class event source. Until then (A) buys nothing but churn.

### Concrete type/shape changes

```csharp
public enum CorrelationPolarity
{
    Absence,
    // Flag anchors that HAVE the companion (the amplification / co-presence shape). The finding carries
    // the WITNESS that made it fire — an absence finding has nothing to point at, a presence one does.
    Presence,
}

// How a companion is matched to an anchor beyond the predicate.
public enum CorrelationKeyMatch
{
    // Today's behavior: normalized ResourceKey equality (bulk_write of X ↔ invalidate of X).
    ResourceKeyEquality,
    // The anchor's key TOKEN must appear in the companion's key-argument surface. When the token cannot
    // be resolved through the boundary, the match SUCCEEDS at reduced certainty (see NoKeyCertainty) —
    // the key is OPTIONAL by construction, because §0.5 says it is frequently just unavailable.
    PropagatedKeyToken,
}

public sealed record CorrelationSpec(
    /* … unchanged … */
    CorrelationKeyMatch KeyMatch = CorrelationKeyMatch.ResourceKeyEquality,
    // Presence only: at most this many witnesses per anchor, nearest-depth first (0 = unlimited).
    int MaxWitnessesPerAnchor = 1,
    // Presence only: the certainty token for a companion matched without a resolved key chain.
    // Null = do not emit unresolved-key matches at all.
    string? NoKeyCertainty = "medium"
);

public sealed record CorrelationFinding(
    /* … unchanged, all existing positions … */
    // Presence only: the companion that made this fire. Null for Absence, so Absence output stays
    // byte-identical and the FR-7 golden oracle is untouched.
    string? WitnessMethod = null,
    string? WitnessFilePath = null,
    int? WitnessLine = null,
    string? WitnessResourceKey = null,
    string? WitnessProvider = null,
    string? WitnessOperation = null,
    int? WitnessDepth = null
);
```

Plus, in `FactPathFinder`:

```csharp
// Sibling of ReachesFromEachSeed that KEEPS the ReachInfo instead of projecting to a key set — depth is
// already computed and is needed for nearest-witness selection and depth-tiered confidence.
public static IReadOnlyList<IReadOnlyDictionary<string, ReachInfo>> ReachesInfoFromEachSeed(...)
```

Everything else in `FactCorrelationDeriver` survives verbatim: the companion index by key, the
predicate/namespace-suffix/in-scope-key anchor filtering with its certainty token (reused directly for the
tiering in §4 — `InScopeKeys` is already "restrict + tier the anchors, carrying an opaque token"), the
one-reach-per-distinct-enclosing-id batching, the dedup, the determinism sort. The polarity branch is a
single inversion at step 5 plus witness selection.

### Detector wiring is DATA
A new rules section, mirroring `cacheCoherence`'s shape (a single object projected by a
`Fact…RuleProvider`), so the *policy* is data and only the operator is C#:

```jsonc
"crossMethodAmplification": {
  "_doc": "presence-join(iteration_fanout, read, key-token, fwd-tree). Opt-in: absent section = detector off.",
  "witnesses (formerly readProviders)":  ["llblgen", "object_store", "db_command", "db_reader", "entity_cache", "repository", "http"],
  "readOperations": ["read", "fetch", "query", "row_read", "execute", "GET"],
  "maxDepth": 6,
  "minTier": "medium",                       // "low" = the opt-in over-approximating surface
  "excludeEnclosingNamespaceSuffix": ["CollectionClasses", "DaoClasses"],
  "suppressHubCallees": [ /* measured shared caches — see §5 */ ],
  "excludeCalleeNamespacePrefix": ["Echo.Process", "System", "LanguageExt"]
}
```

Note the read gate here is a *separate* list from `observations.nPlusOne` on purpose: the two detectors have
different precision budgets, and coupling them means a fix to one silently re-tunes the other.

---

## 3. Central tension: what happens to the varying-key discriminator across a boundary

The tension as handed over is real and the answer is **(c) both — carry the key one hop AND tier by
confidence — but in the opposite order from the obvious one.** Argued, not offered as a menu:

The obvious plan is "add the parameter-name fact, get the exact chain, ship tier `high` only". Reject it, for
three reasons:

1. **The exact chain is not exact.** Even with parameter names, hop 4 (the read's key argument surface) is
   subject to the *same* capture gap that already cripples the intra-method detector (§0.5). A read written
   `Fetch(pk.Value)` or `Fetch(new Key(pk))` has no captured name and the "exact" chain silently breaks. A
   design whose only tier requires all four hops resolved inherits every syntactic accident in the codebase
   and will under-report for reasons no reviewer can see.
2. **The anchor-side half of the key is already load-bearing on its own, and measurably so.** Requiring only
   "a per-iteration identifier crosses this call boundary as an argument" cuts the population from **51,181
   looped in-source call sites to 5,410** (10.6%), and — restricted to those with a read below at depth ≤ 6 —
   from 6,687 to 1,927 (lower bound). At the *page* grain the same gate is a 30-45× reduction (§8 table).
   That is most of the precision the full chain would buy, from facts that already exist.
3. **The keyless tier must exist anyway**, because `for`/`while`/`do` amplify and bind nothing, and because
   4,258+3,972 anchor sites have a broken argument surface. Its population (~7,665 sites) is exactly the
   thing that must be quarantined behind opt-in rather than deleted.

So: **the key is OPTIONAL in the operator (`NoKeyCertainty`), the anchor-side half of the chain is the
default gate, and the parameter-name fact is what promotes a finding to `high` — not what makes it exist.**

---

## 4. Key propagation: the fact, its cost, and the tiers

### The one new fact
```csharp
public sealed record SymbolFact(
    /* … */,
    // Ordered parameter NAMES of a method/ctor (JSON string[]), index-aligned with the parameter TYPES
    // already visible in Signature. The single missing link in cross-boundary key propagation: an argument
    // at position k at a looped call site binds to parameter k in the callee, and the callee's read names
    // that PARAMETER, not the caller's loop variable. Null for non-method symbols and zero-arg methods.
    string? ParameterNames = null
);
```

### Cost, stated plainly
* **Extraction:** ~5 lines (`symbol.Parameters.Select(p => p.Name)` at the existing method-symbol emit
  site). No new symbol resolution — the `IMethodSymbol` is already in hand.
* **Store:** one nullable TEXT column on `symbol_facts`, i.e. a **schema-version bump**. Per repo rule
  (no inline schema mutation, changes go via version bump + reindex) this means **every existing store must
  be re-mined** — a republished rig cannot read an old store. On MedDBase that is ~8 min per solution per
  commit, and every `--store` an analyst has kept becomes unreadable. This is the entire cost, and it is a
  workflow cost, not an engineering one.
* **Query:** negligible. Join by `SymbolId` (already indexed); parse one small JSON array per distinct
  anchored callee (3,150 distinct callees measured, not 51k).
* **Sequencing:** it should ride the next unavoidable schema bump rather than force one of its own — but it
  must not be *blocked* on one, because tier `high` is the tier that earns default-on. Slice 3 in §7.

### Confidence tiers, and what each means operationally

| Tier | Predicate | Population (lower bound) | Operationally |
|---|---|---|---|
| `high` | full chain: loop identifier ∈ `ArgumentNames[k]` at the anchor **and** callee `ParameterNames[k]` appears in the witness read's key-argument surface **and** `WitnessDepth ≤ maxDepth` | not measurable before the fact exists; §8 defines how it will be measured | default surface. Read as an actionable defect: "this read is issued once per element of `<detail>`, keyed on `<token>`". Same standing as today's intra-method `n_plus_1(high)`. |
| `medium` | a per-iteration identifier crosses the boundary as argument *k*, but the chain to the read's key is unresolved (no `ParameterNames`, or the read's key argument was not captured) | **1,927** anchors (d ≤ 6); **1,098** at d ≤ 2 | default surface, ranked below `high`, disclosed verbatim as *"a per-element value is passed into this call and a read is reachable beneath it; the read's key was not proven to be that value"*. This is the tier that ships first and the tier the calibration in §5 must clear. |
| `low` | a looped call site with **no** key evidence at all (`for`/`while`/`do`; or every argument surface null) with a read below | **7,665** sites (d ≤ 19), 6,687 at d ≤ 6 | **opt-in only** (`minTier: "low"`, never the default). This is `looped_effect`-under-reach: honest, sound-as-structure, and 44× the current hazard surface. Its job is to be *available* when someone is hunting a known-slow endpoint, not to be on. |

The tier token rides the existing `CorrelationSpec.InScopeKeys`-style mechanism (opaque certainty token
carried onto the finding) — no new plumbing.

---

## 5. False-positive control, and the calibration protocol

### The grain decision, which is the single biggest FP lever
Measured: with the key gate on and depth ≤ 6, the **(anchor site × distinct read-enclosing method)** cross
product is **74,489** pairs across 1,927 anchors — median 6 reads per anchor, p90 **157**, max **404**.
Emitting per pair is a non-starter regardless of tiering. Therefore:

> **One finding per anchor call site**, carrying the **nearest-depth witness** as evidence
> (`MaxWitnessesPerAnchor = 1`, configurable). The remaining witnesses are a `--verbose`/tsv concern, never
> the finding count.

That single choice takes the default surface from 74,489 to 1,927 before any other filter.

### The rest of the plan
* **Depth cap 6** by default (§1, corrected in review) — a read many frames beneath a looped call is a true
  structural fact and a poor review item, but 6 is the measured knee and the depth every §8 recall number was
  taken at. Tightening it is a calibration decision that must re-measure §8, not a free default.
* **Hub suppression, as rules data.** Measured across the 8 oracle pages, a handful of callees anchor on
  most of them: `MMS.Cache<,>.GetResult`, `SecureAccessors.GetAccountName` (`query: p in db.Profile`),
  `ClinicalCoding.WalkFormDataToFindUncodedFields`, `CommonEntityBase.LocalizePossibleDate`. These are
  either already-cached seams or recursion, and they will dominate every page's list. They must be
  suppressed/down-ranked as **data** (`suppressHubCallees`), never in C#, and each entry needs a one-line
  justification in the rules `_doc` so the suppression is auditable.
* **Recursion is not amplification.** `ObjectStore.GetIndexIdentifiers` (ObjectStore.cs:1001) is
  self-recursive: the "loop" iterates derived indexes and the call re-enters the same method. That is a tree
  walk, and its per-node read may be genuinely necessary. Detect the anchor-callee-equals-enclosing case and
  tier it down (`reason: recursive_descent`) rather than suppressing it — the runtime evidence for
  `OBJECT_HOLDER_INDEX` says these walks *are* sometimes the hotspot.
* **`entity_cache` gets no suppression.** It is tempting to down-rank "the read is a cache read, probably a
  hit". The runtime tile disproves it: `Admin/Profile/Home2`'s 11,461 single-row `SELECT PROFILE` came
  through `ProfileCache.New`. Disclose the possibility; do not act on it.
* **Annotate, never suppress** (house rule): a suppressed archetype is emitted at `low` with its reason, not
  dropped.

### Calibration BEFORE on-by-default (repo convention)
Sequenced, and each step is a gate:

1. **Volume gate.** Store-wide default-surface count (tiers `high`+`medium`, depth 3, hubs suppressed) must
   land **under ~500** — i.e. the same order as today's 175 intra-method findings. If it lands higher, cut
   depth or tighten the read gate; do not ship and hope.
2. **Stratified hand audit.** N = 50, stratified by tier × read provider × depth, each verified against
   source in `meddbase-main-application`. Thresholds: **≥ 80% TP for `high`**, **≥ 50% TP for `medium`**
   (a `medium` "TP" = a per-element read really is issued beneath that call site; whether it *matters* is a
   ranking question, not a correctness one). Failing `medium` at 50% means the tier ships opt-in, not off.
3. **Archetype sweep.** Group the findings by callee and inspect any callee with > 20 anchors. Every such
   group is either a real shared hotspot (keep, rank up on fan-in) or an archetype needing a data
   suppression. This is what turned FR-7 from a flood into a surface, and it is the step most likely to be
   skipped.
4. **Oracle recall** (§8) — measured, reported, and *published with the detector*, not asserted.
5. Record the whole run in `meddbase-analysis/docs/` (the grounded-roadmap side), not in this repo, per the
   existing split.

---

## 6. Disclosure ceiling — what this detector provably cannot know

Written to be copied into `docs/hazards.md` alongside the detector row, in the house style.

* **Path insensitivity (the hard ceiling).** "Time" is structural reachability. A read behind a cache-hit
  early return, behind a guard, or on a branch never taken with these inputs is still *present* on the tree.
  Findings are sound as **structural presence**; **clears are unsound**. `dominance` (which would let us say
  "this read always runs per element") needs a CFG rig does not have. The per-call-site
  `EnclosingGuards` fact gives an intra-method must-run/maybe-run hint for the *anchor*, and can be surfaced
  as evidence — it does not compose across the boundary and must not be presented as if it did.
* **No cardinality.** rig cannot know the loop's iteration count. N may be 1. Every finding is *potential*
  amplification; the runtime tile is the only thing that knows 1,099 calls/trace. Never render a multiplier.
* **Dispatch fan-out over-approximation.** Anchored callees include interfaces
  (`IClaimDiagnosisService.GetClaimDiagnosisCodeData`, `IDiagnosisCodesRepository.FindById`,
  `INHSNumberService.FindIdForPatientWithDuplicateNHSNumber` all appeared under `HtmlEdit2`). The witness read
  may live in an implementation that endpoint never reaches. `ReachInfo.DispatchBasis == "heuristic"` and
  `DispatchVia`/`DispatchDegree` must be carried onto the finding as evidence and must down-rank it.
* **The unsound-clears direction is wide, and enumerable.** Absence of a finding does not mean absence of an
  N+1: the argument-surface capture gap (§0.5, ~4k+4k anchor sites), keyless `for`/`while`/`do` at
  default tier, the depth cap, provider-gate omissions (`object_store` today), reflection/delegate-field
  dispatch rig cannot resolve, reads issued by generated LLBLGen internals below the mined surface, and
  anything crossing a handoff edge (SyncCut). Say this next to the recall number, every time.
* **Not a performance claim.** Per `docs/hazards.md`'s non-goals: this is an operational-hazard *suspicion*
  ("this shape resembles read amplification"), not a latency prediction. The endpoint may be fine.

---

## 7. Phased plan

### Slice 0 — prerequisite: repair the base case (independently verifiable, no new detector)
Fix `ArgumentListOf` to capture a name for composite argument expressions (a reduced member/identifier
*surface* of the expression, e.g. `Fields.Name == s.Trim()` → the identifier set `{Fields.Name, s}`), and
close the provider-gate omissions (`object_store`; `db_command`'s `execute` token). **Verification:** the
intra-method `n_plus_1` count moves from 175 by the +23 already-identified true positives (11 `llblgen` incl.
InvoicesByNominalCode.cs:96, 12 `object_store`), and no other detector's output changes (golden oracle).
This is listed here because §0.5 makes it a *dependency* of tiers `high`/`medium`, not because this design
owns it. **Do not fold it into the same MR as anything below** (minimal-diff convention).

### Slice 1 — the anchor event (first independently verifiable slice of THIS design)
`FactIterationFanoutDeriver`: pure, facts → `DerivedEffect` pseudo-events, no reach, no correlation, no
hazard. Surfaced as a *structural observation* (`looped_call_with_element_key`, not in `HazardKinds`) behind
the rules gate. **Verification, all without touching the correlation deriver:**
* synthetic fixtures for each iteration kind × argument position × keyless case;
* on store `32f4dac9dc7b`, the **keyed** pseudo-event count is **5,410** — `ArgumentNames`-based keys over
  `foreach`+`query` in-source sites (this number is the acceptance test, and it should roughly double after
  Slice 0 — a useful cross-check that Slice 0 landed);
* every existing detector's output byte-identical.

> **CORRECTED IN REVIEW (2026-08-03) — the acceptance number is KEYED-ONLY, and the keyless population is
> deliberately out of Slice 1.** As first drafted this slice read as "the pseudo-event count is 5,410", which
> contradicts §2 (whose pseudo-effect spec emits `ResourceType = ""` when keyless) and §4 (whose `low` tier
> needs keyless anchors). Taken literally, a correct implementation emitting both populations would FAIL the
> test, and one passing it would silently omit the population `low` depends on. Resolution: **Slice 1 emits
> keyed anchors over `foreach`/`query` only** — 5,410, exact — and keyless emission (`for`/`while`/`do`, and
> sites whose whole argument surface is null) moves to **Slice 2**, alongside the `low` tier that is the only
> consumer of it. Note also that the 7,665 figure in §4 is a POST-reach count (keyless sites *with a read
> below* at d ≤ 19), so it is not the keyless anchor count; that raw count is unmeasured and Slice 2 must
> establish it before wiring `low`, or `low`'s volume is unknown at the moment it is offered.

### Slice 2 — the Presence operator + wiring at `medium`/`low`
`CorrelationPolarity.Presence`, `CorrelationKeyMatch`, witness fields, `ReachesInfoFromEachSeed`. Wire
`cross_method_amplification` (a new `HazardKinds` member — deliberately a **distinct type string** from
`n_plus_1`, so the intra-method detector's calibrated precision is not diluted and `rig impact` deltas stay
attributable). Ships **opt-in** (`crossMethodAmplification` section absent = off, mirroring `cacheCoherence`).
**Verification:** FR-7 `cache_coherence` output byte-identical (the Absence path must not move); the §8
oracle measurement; the §5 volume + audit gates.

### Slice 3 — `ParameterNames` + tier `high` + the default-on decision
The schema bump, the chain resolution, the recalibration, and only then the on-by-default call. That call is
Dmytro's, on the calibration numbers.

### Slice 4 — generalize back to CEP **[generalizes]**
By this point the seam has two polarities, two key-match modes, an explicit window and a pseudo-event
synthesizer. That is enough shape to (a) migrate `read_before_commit` onto a `sequence` operator as the CEP
doc's stated first slice, and (b) replace the pseudo-event trick with a real `AnchorSpec` union when the JSON
pattern DSL lands. Also fold back two corrections into the CEP doc: the `N+1 = aggregate(read, loop-window)`
mapping applies only to the lexical case, and "correlation key = ResourceKey" is really
"correlation key = whatever identity the operator joins on".

---

## 8. Oracle: how this is measured against the 8 runtime-confirmed hotspots

**Ground truth:** the preprod dashboard (`np-hyperdx`, "Fat Spans — Wall + SQL Roundtrip Triage", tile "N+1
hotspots") — 8 endpoints ranked by total SQL seconds, of which the shipped lexical detector flags 3.

**Grain correction, which the measurement protocol must state:** the tile names a **URL/page**, not a method.
A page maps to many entry points (`HtmlEdit2` → 4 EPs; `Account/Configuration/Main` → 417). Recall must
therefore be measured at **page grain**: union the forward reach of every EP whose id lies under the page
type, then ask whether any anchor in that union fires.

**Two recall metrics, both reported:**
* **loose** — the page gets ≥ 1 default-tier finding.
* **strict** — the page gets ≥ 1 finding whose **witness read resource matches the tile's hot query**. This
  is the one that matters; loose recall is trivially gameable by lowering the tier.

**Measured projection today** (tier-`medium` predicate, key-carrying anchor with a read below at d ≤ 6, over
stored edges only — lower bound):

| Endpoint (page) | hot query | today | looped sites in reach | keyed anchors w/ read | strict witness found? |
|---|---|---|---|---|---|
| `Document/BrowserComponent/HtmlEdit2` | `OBJECT_HOLDER` | ✗ | 317 | 17 | **✓** `ObjectStore.cs:1117` `query: ohti in db.ObjectHolderIndexItemType` → `GetObjectInstance` → `llblgen:fetch ObjectHolderEntity` |
| `Account/Configuration/Main` | `DEPARTMENT_CODE` | ✓ | 1,419 | 45 | **✓** `Main.cs:697` `foreach: departmentCode in departmentCodes` → `DepartmentCodeEntity.Provide` |
| `Admin/Profile/Home2` | `PROFILE` | ✓ | 55 | 2 | ✓ (also intra-method: `Home2.cs:294/296`) |
| `Workflows/ReferralOutbound/ListPane` | `OBJECT_HOLDER_INDEX` | ✗ | 1,260 | 31 | to verify |
| `Prescription2/Edit` | `sandbox.FDB` | ✗ | 1,328 | 29 | to verify (external FDB — may be outside the mined surface) |
| `Profile/StatusPanel` | `WORK_MEMBER`+ | ✗ | 1,346 | 36 | to verify |
| `Admin/CommonCatalogues2/Home` | `OBJECT_HOLDER` | ✓ | 1,731 | 38 | **✓** `ObjectStore.cs:277` `foreach: index in indexes` → `ObjectHolderIndexEntity.Provide`; `ObjectStore.cs:1117` |
| `Document/…/NewHtmlDocumentFromTemplate` | `OBJECT_HOLDER_INDEX` | ✗ | 1,633 | 55 | **✓** `ObjectStore.cs:277` |

**Loose recall projects to 8/8. Strict recall is verified at 5/8 so far**, including the worst miss: the
`HtmlEdit2` → `ObjectStore.cs:1117` → `ObjectHolderEntity` fetch chain is exactly the runtime `OBJECT_HOLDER`
hotspot (1,099.8 calls/trace, 86.2s), and it is invisible to the lexical detector because the `query`
iteration context and the read live in different frames — which is the proof the gap is cross-method, now
confirmed constructively rather than only by the absence of findings.

**The precision half of the same measurement:** the key gate reduces per-page candidates by **30-45×**
(1,731 → 38 on CommonCatalogues2; 1,633 → 55 on NewHtmlDocumentFromTemplate; 317 → 17 on HtmlEdit2). That
ratio, not the store-wide count, is the number to defend when someone asks whether the key discriminator is
worth its cost.

**Acceptance for on-by-default:** strict recall ≥ 6/8 **and** the §5 volume + audit gates. Recall below that
means the detector ships opt-in and the residual misses are documented as named gaps — the same disclosure
discipline the lexical fix used ("do not read this as *rig now finds N+1s*").

---

## 9. Where this generalizes

| Piece | Reusable by |
|---|---|
| `CorrelationPolarity.Presence` + witness | every co-presence detector in the CEP migration map (`dual_write` divergence is Presence + a second predicate; `static_init_capture` is already a degenerate co-presence) |
| pseudo-event synthesis from non-effect facts | any detector whose anchor is a graph or syntax fact rather than a side effect (lock acquisition, transaction scope, parallel region) |
| `ReachesInfoFromEachSeed` | anything wanting depth/dispatch-basis evidence on a correlation, i.e. all of them |
| depth-tiered certainty + `NoKeyCertainty` | every join whose identity is sometimes unresolvable — per `docs/hazards.md`, that is *most* of them (the `~heuristic` column in the delivery-edge table) |
| the "one finding per anchor, nearest witness" grain | every presence operator, since the cross product always explodes |

The one thing this design deliberately does **not** generalize is the loop window: it is anchor-side by
necessity (§0.2), and until `ReachInfo` unions loop-ness across paths rather than taking BFS-first, no
detector should ask the reach "was this reached under a loop".
