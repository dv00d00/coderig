# Slice 4 — the surface-hash cascade gate

**Design, read-only.** Slice 4 of [live-background-index](../backlog/done/live-background-index.md).
The gate that decides whether an edit cascades to a project's dependents. Roslyn's cascade is
dependency-shaped and surface-BLIND ([roslyn-incrementality-findings](roslyn-incrementality-findings.md) §2:
skeleton references are cross-language only, `SolutionCompilationState.cs:1312-1330`, and rig hard-codes
`LanguageNames.CSharp` at `SolutionSourceLoader.cs:871`), so this is the one part of the design Roslyn will
not do for us.

Every claim below is either a `file:line` citation or a query run against
`C:\Git\meddbase-analysis\.rig\ae2cdb64e1cb\rig.db` (445,163 symbol rows, 220 assemblies) or against
`C:\Git\meddbase-main-application`. Inference is labelled INFERRED.

**The headline, up front, because it corrects the program doc.** Measured over 564 real `.cs` file-edits,
the ungated (coarse) cascade re-extracts a **median of 3,366 source files — 27% of the 12,369-file
codebase — per edit**. The program doc's "median cascade is 6 of 187 assemblies, so coarse already
delivers the SLO for the common edit" counts *projects*, and projects here are wildly unequal: 79% of
coarse cascades pull in `MedDBase.Pages` (2,595 files) or `MedDBase.DataAccessTier` (2,475). Coarse is
not "seconds". With the gate the median is **1 file**. Conversely, the *hub-edit* rationale in the program
doc is the weaker half of the case: only 2.3% of edits are "body-only edit to a 51+-dependent assembly".
The gate's value is the median, not the tail.

---

## 1. "Public surface", operationally — and why `BodyHash` is not half of it

### 1.1 What `BodyHash` actually covers (read, not guessed)

`FactExtractor.BodyHashOf` (`FactExtractor.cs:1140-1153`):

```csharp
var span = node.Span;
if (span.IsEmpty) return "";
Span<byte> hash = stackalloc byte[8];
XxHash3.Hash(source: MemoryMarshal.AsBytes(fileText.AsSpan(start: span.Start, length: span.Length)), destination: hash);
return Convert.ToHexStringLower(hash);
```

- 64-bit XxHash3 over the **native-endian UTF-16 bytes** of `node.Span`, sliced from the file text
  materialized once per tree (`FactExtractor.cs:29`). 16 hex chars. `""` for an empty span.
- `node` is whatever `AddSymbol` was handed. Call sites: the whole `MemberDeclarationSyntax` for types /
  methods / properties / events (`:99`), the `VariableDeclaratorSyntax` for each field (`:81`), the accessor
  node for bodied accessors (`:121`), the lambda node for lambdas (`:2710`).
- `Span` (not `FullSpan`) ⇒ leading/trailing trivia excluded, so a doc comment above a member does not move
  it; interior comments and whitespace **are** included; attribute lists **are** included (they are child
  nodes, inside `Span`).
- Stored as `symbol_facts.BodyHash TEXT NOT NULL` (`SymbolFactEntity.cs:21-23`, INSERT at
  `Writes.cs:321,359`), read via a `ColumnExists`-guarded raw-ADO read because the column was added late
  (`Reads.cs:851-866`). Sole consumer today: `rig impact`'s in-place-body-change signal
  (`ImpactEngine.cs:164,320`).

**So the name is a misnomer and the shape is wrong for a surface hash in both directions.** For a TYPE,
`BodyHash` is the entire type declaration text — every member, every body. It is not the complement of a
surface hash; it is a superset of both. It cannot be "half the machinery": it is one whole-declaration
digest at the wrong granularity.

### 1.2 What `symbol_facts` structurally does NOT carry

This is the load-bearing finding, because it kills the tempting zero-extractor-change design ("aggregate
the columns you already have"). Verified against the real store:

| surface element | present? | evidence |
|---|---|---|
| method **return type** | **NO** | `ToDisplayString()` default omits it and DocID omits it (except conversion operators, which encode `~T`). `M:MMS.AssemblyCache.LoadFile(System.String)` → `Signature = 'MMS.AssemblyCache.LoadFile(string)'` |
| field / property / event **type** | **NO** | `F:...APIHttpContext.DomainKey` → `Signature = 'MedDBase.APIGateway.Common.APIHttpContext.DomainKey'`; `P:MMS.CacheBase\`2.Keys` accessor → `'MMS.CacheBase<T, R>.Keys.get'` |
| parameter **default values**, `params` | NO | not in DocID, not in `ToDisplayString()` default |
| generic **constraints**, type-param **variance** | NO | — |
| **attributes** | NO | no attribute facts exist on symbols at all; only `attributeUse` reference rows, emitted at the *using* site (`FactExtractor.cs:2387`) |
| `partial`, `unsafe`, `ref struct`, extension-ness | NO | `BuildModifiers` (`FactExtractor.cs:2826-2878`) emits only accessibility + static/abstract/sealed/virtual/override/async/readonly/volatile. **Doc drift worth fixing separately: `Facts.cs:15` claims `partial` is in `Modifiers`; it never is.** |
| const / enum-member **values** | NO | — |
| accessibility | yes | `Modifiers`, via `AccessibilityOf` (`FactExtractor.cs:2879-2891`) |
| name, arity, parameter types, `ref`/`out` | yes | DocID (`ref`/`out` as trailing `@`: `M:...RelWhere.Deconstruct(System.String@,System.String@)`) |
| static/abstract/sealed/virtual/override, `TypeKind` | yes | `Modifiers`, `TypeKind` |
| base / interface list | yes, elsewhere | `type_relation_facts` (17,993 rows) |
| Roslyn-mined override/impl edges | yes, elsewhere | `dispatch_facts` |

A method's return type and a field's type are the two most common signature changes there are, and neither
is representable from existing columns. **Any surface hash assembled from today's columns is unsound on the
common case.** That single fact drives the whole design below and the shipping recommendation in §6.

### 1.3 The surface hash — definition

Add **`symbol_facts.SurfaceHash`**, computed in `FactExtractor.AddSymbol` beside `BodyHash`, from the same
cached `fileText`: XxHash3 over the declaration's **tokens with every executable body excised**, tokens
joined by a single space (trivia dropped) so reformatting a signature does not cascade.

Per kind, `SurfaceText(node)` retains:

| kind | retained | excised |
|---|---|---|
| type (`class`/`struct`/`interface`/`enum`/`record`/`delegate`) | attribute lists, modifiers, keyword, identifier, type-parameter list (incl. variance), **record primary-constructor parameter list**, base list, constraint clauses — everything from `Span.Start` to the `{`/`;` | the member list entirely (members are their own rows) |
| method / ctor / operator / conversion / accessor | attributes, modifiers, **return type**, name, type params, parameter list **with defaults and `params`**, constraints | `Body` (BlockSyntax) and the expression of `ExpressionBody` |
| property / indexer / event | attributes, modifiers, **type**, name, accessor *declarations* (`get;`/`set;`/`init;` — presence and accessibility are surface) | accessor bodies; the property initializer `= expr` unless `const` |
| field / enum member | modifiers + the parent `BaseFieldDeclarationSyntax.Declaration.Type` + the declarator identifier | the initializer, **unless** `const` or an enum member (then retained — see A11) |
| lambda | — | **excluded entirely** (`SurfaceHash = ""`): 66,485 rows on the real store, never bindable cross-assembly |
| namespace | — | **excluded** (12,654 rows), no surface content |

**Plus one body-derived bit that must be folded in: `IsIterator`.** See A1 in §4 — it is not optional, and
it is the only body-derived value that crosses a project boundary in stage 1.

### 1.4 Accessibility: include EVERY accessibility. Exclude nothing.

The instinct is "surface = public (+ protected)". That is wrong here, for three reasons in increasing order
of force.

1. **`internal` is cross-assembly surface in this codebase.** MedDBase uses `InternalsVisibleTo` in 40+
   places, in both attribute and MSBuild-item form — `src/mms/MMS/InternalsVisibleTo.cs:3`,
   `src/main/MedDBase.DataAccessTier/AssemblyInfo.cs:4-5`,
   `src/drug/FirstDataBank.DrugServer.API/FirstDataBank.DrugServer.API.csproj:10`,
   `src/mms/MMS.Drawing/MMS.Drawing.csproj:9` (`<AssemblyAttribute>` form),
   `src/main/MedDBase.Processes/ProjectUtilities.cs:4` (`DynamicProxyGenAssembly2`). In the store *as
   currently indexed*, none of those targets are present — I queried `assemblies` for `MMS.UnitTests`,
   `MedDBase.UnitTests`, `Tools.SqlRunner.Tests`, `FirstDataBank.DrugServer.VirtualProxy`,
   `DynamicProxyGenAssembly2`: all 0 rows, and `AssemblyName LIKE '%Tests%'` returns nothing across all
   220 assemblies. So today's IVT grants point *outside* the indexed set and are inert. That is an accident
   of which solution is indexed; `rig index --from`, a test-inclusive `.slnx`, or a new IVT grant changes
   it silently. Cost of including `internal`/`protected internal`/`private protected` unconditionally:
   **12,033 of 445,163 rows (2.7%)**.
2. **`private` members are surface too, and rig has a measured consumer.**
   `AllocationSizeEstimator.Object` (`AllocationSizeEstimator.cs:26-40`) walks every non-static field of
   the allocated type **and all its base types**, private included, to compute
   `allocation_facts.ShallowSizeBytes`. **92,667** store rows carry a non-null size. So adding a private
   instance field to a dependency type changes a *dependent's* facts. Excluding private makes the gate
   unsound for exactly that, with no warning.
3. **The saving is not worth the argument.** `private` + `private static` are 37,802 of 445,163 rows
   (8.5%). **The load-bearing exclusion is the BODY, not the accessibility** — that is where the 59%
   body-only edit rate lives (§6).

Conclusion: hash every declared symbol regardless of accessibility. Simpler code, no IVT parsing on the
hot path, and it removes an entire class of unsoundness.

---

## 2. Where it is computed and stored

**Per symbol, at extraction.** `SurfaceHash` goes in `FactExtractor.AddSymbol` next to `BodyHash`
(`FactExtractor.cs:1104-1126`), reusing the already-materialized `fileText`. Zero extra tree walks; the
cost is one `DescendantTokens()` pass over the retained span, which is strictly smaller than the span
`BodyHash` already hashes.

**Per project, as the comparison unit.** Add `assemblies.SurfaceHash` — the `assemblies` table already
exists with exactly this shape (`AssemblyName` PK, `ContentHash`, `SymbolCount`, `ReferenceCount`), so this
is a sibling column, not a new table. Compute it with the existing primitive
`ProjectContentHash.Compute` (`ProjectContentHash.cs:20-28`), which is already documented as
order-independent, path-independent and add/remove-sensitive (`ProjectContentHash.cs:14-17`) — precisely the
three properties the gate needs, so no new hashing primitive is introduced.

Item multiset for assembly `A`:

```
per surface-bearing symbol of A:   SymbolId | Kind | Modifiers | IsOverride | IsIterator | SurfaceHash
per type_relation_facts row of A:  rel | TypeSymbolId | RelationKind | RelatedSymbolId
per dispatch_facts row of A:       disp | SourceMember | Kind | TargetMember
per generated tree of A:           gen | <normalized path> | <token hash of the tree>
once for A:                        asmattrs | <sorted assembly-level attribute list, incl. InternalsVisibleTo>
once for A:                        opts | LangVersion | Nullable | AllowUnsafeBlocks | sorted(PreprocessorSymbols)
```

The last three lines close A7 and A9 in §4; `opts` comes from `ProjectBuildInfo.Properties` /
`PreprocessorSymbols` (`ProjectBuildInfo.cs:11-18`).

**Why per-project and not per-file.** 1,728 of 22,195 type symbols are **partial across more than one
file** (measured; `T:MedDBase.Configuration.Settings` spans **80** files, `T:MedDBase.Pages.TestBed` 30). A
type's surface is the union of its parts, so a per-file surface hash is either wrong (a member added in
part 2 leaves part 1's hash untouched) or requires stitching that the project aggregate gives for free. The
cascade gate is project-level anyway — that is the granularity of the dependency graph.

**Restart survival + resident mode.** On disk it is a column, so it survives a process restart like
`ContentHash` does. In the resident overlay it is a `Dictionary<string,string>` (assembly → hash) loaded
once from `assemblies`, and recomputed for a dirty assembly from `(base rows for its unedited files) ∪
(overlay rows for its edited files)` — a set swap over ~2k items, microseconds, not a project re-read.

**Old stores.** Read `SurfaceHash` through the same `ColumnExists` guard as `BodyHash`
(`Reads.cs:853-866`). A pre-column store yields `""` for every symbol ⇒ every project's aggregate is
"unknown" ⇒ the gate declines and falls back to coarse. Degradation is toward *more* work, never toward a
stale answer.

**No `QueryCacheKeys` bump.** This is a stage-1 fact-schema change, so it lands via a re-index, which moves
`rig.db`'s size+mtime — the store-identity axis already covers it (CLAUDE.md's cache section). None of the
`*Schema` constants change meaning. (It does mean **a full MedDBase re-index is a prerequisite** to
evaluating the gate on the real store.)

---

## 3. The cascade rule

Inputs: `Δfiles` (changed / added / deleted paths — `git diff --name-only` plus the fs watcher); the base
store; `D`, the project dependency graph; `S[p]`, project `p`'s stored surface hash.

```
Dirty   = ∅          # files to re-extract
Reeval  = ∅          # projects needing a design-time rebuild
Cascade = ∅

# --- Arm 1: project-file / import changes. No gate applies. ---
for f in Δfiles matching *.csproj | *.props | *.targets | paket.references | packages.config
                       | Directory.Packages.props | global.json | nuget.config:
    Reeval ∪= { p : BuildInputFingerprint.Gather(p) folds f }      # existing ancestor-walk allowlist
for p in Reeval:
    Dirty ∪= Files(p);  Cascade ∪= Dependents*(p)                  # unconditional

# --- Arm 2: source edits, round 1 = the edited FILES only. ---
P1 = ∅
for f in Δfiles ∩ *.cs:
    p = Project(f)                                                 # from ProjectBuildInfo.SourceFiles
    if p is null:  Reeval ∪= { OwningProjectByPath(f) }; goto Arm1  # a NEW file MSBuild must glob
    Dirty ∪= { f };  P1 ∪= { p }                                    # deleted file: drop rows, nothing to extract
facts' = Extract(Dirty)                                            # over the retained Solution
for p in P1: rerun generators for p; add changed generated trees to p's inputs

# --- Arm 3: THE GATE. ---
for p in P1:
    S'[p] = Aggregate( (base rows of p minus Dirty rows) ∪ facts'(p) )
    if S'[p] == S[p]:  continue                                    # BODY-ONLY -> stop. No cascade at all.
    Dirty ∪= Files(p)                                              # surface moved: rest of p re-binds too
    Cascade ∪= Dependents*(p)                                      # transitive, over D
for q in Cascade: Dirty ∪= Files(q)

# --- Arm 4: round 2, then rebake the derived graph WHOLE. ---
facts'' = Extract(Dirty \ already-extracted)
rebake dispatch_edges / CHA / shaped graph over the WHOLE fact set   # slice 3's rule, unchanged
```

Five properties that make this reviewable:

1. **Round 1 is always exactly the edited files**, so the gate can never make the fast path slower. Its
   only added cost on a cascading edit is one aggregate re-hash of the edited project.
2. **The gate is a monotone widener** — it only ever adds to `Dirty`. It cannot un-dirty what arm 1 set.
3. **`Dependents*` must be built from `ProjectBuildInfo.ProjectReferences`** (`ProjectBuildInfo.cs:14`),
   the MSBuild-resolved reference graph — **not** from `reference_facts`. See A12.
4. **The gate is per-project, so there is no partial credit.** A file containing both a body edit and a
   signature edit cascades. Correct and conservative.
5. **A `rig.rules.json` edit is not a cascade at all** — it is stage 2, already covered by the
   rules-fingerprint axis of `QueryCacheKeys`.

Case table:

| edit | outcome |
|---|---|
| body-only, any project (incl. a hub) | round 1 only: 1 file re-extracted, `S'==S`, **no cascade**. The win. |
| signature change — return type, param, default, constraint, attribute, accessibility | `S'≠S` → all of `p` + all files of every transitive dependent |
| new public type / new member | new multiset item ⇒ `S'≠S` (`ProjectContentHash.cs:17`) → cascade |
| deleted type / member | item removed ⇒ `S'≠S` → cascade; the symbol's rows are dropped from the overlay |
| new FILE | if the design-time `SourceFiles` set doesn't contain it, MSBuild globs decide membership → arm 1 (re-evaluate `p`). If it does, arm 2 then arm 3 (a new file with only a private helper *still* cascades — honest, not clever) |
| deleted FILE | rows dropped, `S'` recomputed without them; cascade iff surface moved |
| mixed body + signature in one file | cascades (property 4) |
| `.csproj` / `.props` / `.targets` / manifests | arm 1, unconditional, gate bypassed |
| generated-tree change (source generator output moved) | folded into `S'[p]` as a `gen` item ⇒ cascades like a signature change |

---

## 4. Soundness — and where it is UNSOUND

Two things the gate is explicitly *not* responsible for, established first so the risk list is honest about
its own scope:

- **Whole-program CHA / dispatch fan-out is stage 2, not facts.** Adding an implementer in a project that
  nothing depends on changes what `reaches` returns, but that is recomputed at query time from the whole
  graph (`FactPathFinder.GraphIndex.cs:340-355`), and slice 3 already mandates rebaking `dispatch_edges`
  WHOLE. So this genuinely non-dependency-shaped coupling is a rebake-whole concern, not a cascade concern.
- **Rule changes** (`builtin-rules.json`) are stage 2, covered by the rules-fingerprint cache axis. What
  matters *here* is whether an attribute on a dependency's member is inside the surface — and it is, because
  attribute lists live inside the retained declaration span.

Now the real holes, **ranked by how likely they are to bite this codebase.**

### A1 — cross-project callee-BODY reads inside stage 1. REAL, MEASURED, a direct counterexample.

`FactExtractor.cs:481-495`, `AddIteratorAllocation`, decides whether a **call site** emits an
`iterator_state_machine` allocation fact by reading **the callee's body**:

```csharp
isIterator = !target.IsAsync && target.DeclaringSyntaxReferences.Any(reference => ContainsYield(reference.GetSyntax()));
```

Add or remove a `yield return` in a dependency method — a pure body edit, surface hash unchanged — and
every caller's allocation facts change. **Measured: 260 `iterator_state_machine` rows in the store, of
which 159 are cross-assembly** (caller's assembly ≠ the target symbol's `DefiningAssembly`).

Is this the only one? `grep DeclaringSyntaxReferences src/Rig.Analysis/**` → 5 hits: `:486` (this one),
`:532` (local functions, same file by construction), `:2549` and `:2564` (accessor nodes of the symbol being
emitted). So `:486` is the only cross-project body read in `Extraction/`.

**Mitigation: widen the hash.** Fold a per-symbol `IsIterator` bool into the surface item. It is
body-derived, which offends the tidiness of "surface excludes bodies", but it is one bit, it is already
computed at extraction, and it closes the hole completely. This is also the case that argues loudest for
`--verify-cascade-gate` (§5): nobody would have derived it from first principles about public surfaces — it
was found by grepping.

### A2 — return type / member type invisible to `symbol_facts`, and reference facts carry resolved TYPE STRINGS.

`reference_facts` records `ReceiverType`, `FirstArgumentType`, `TypeArguments`, `DeclaringTypeArgBinding`,
`MethodTypeArgBinding`, `EnclosingLoopElementType`, `EnclosingLoopBindType` — all semantic-model type
strings (`Facts.cs:36-95`, and the store DDL). A dependency changing `Task<Foo>` → `Task<Bar>` changes those
strings in every dependent that touches the value. Per §1.2 neither the DocID, nor `Signature`, nor
`Modifiers` carries a return type or a field/property type.

**This is the highest-likelihood failure mode of the tempting zero-extractor-change design**, and the
reason §1.3 computes `SurfaceHash` from declaration text instead of reusing existing columns. With
`SurfaceHash`, closed. Without it, the gate is unsound on the single most common kind of signature change.

### A3 — overload resolution and the `CandidateSymbols` fallback.

`FactExtractor.cs:147-148`: `var target = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();`
— on overload-resolution failure rig takes the *first candidate*. Overload sets are pervasive here:
**19,135** `(containingType, name)` groups have more than one method (of 221,634 methods).

Adding, removing or retyping an overload is a surface change (new/changed multiset item) ⇒ cascade, so the
common case is covered. Residual: something invisible to the hash reordering candidates. With text-based
`SurfaceHash` the invisible set collapses to (i) a change in an assembly that is not a reference — which
overload resolution cannot see — and (ii) `CandidateSymbols` *ordering*, which is Roslyn-internal and
unspecified. (ii) is unfixable by any hash and is already nondeterministic across cold indexes, so the gate
does not introduce it. **Accept + disclose.**

### A4 — extension-method applicability. High volume, but covered.

**Measured: 21,630 in-source invocations are extension-method-shaped** (receiver type ≠ the target's
declaring static class; 107,724 if you include static calls written with a type qualifier, which the
extractor also records a receiver for — `FactExtractor.cs:160-165`). Adding, removing, or retyping the
`this` parameter of an extension method all change the DocID or the surface text ⇒ cascade. Extension
methods are only in scope through a `using` of a *referenced* assembly, so the coupling stays
dependency-shaped and `Dependents*` covers it. **No mitigation needed; listed because the volume makes it
look scarier than it is.**

### A5 — generic inference. Covered, and this is where the constraints matter.

Inference reads the dependency's parameter types, its **constraints**, and the argument types — all in the
retained text. Constraints are exactly what `Modifiers`/`Signature` do *not* carry, so this is another case
that only works because §1.3 hashes text. Covered.

### A6 — `dynamic`. Pre-existing recall gap, not a staleness gap.

**Measured: 2,067** reference rows carry a `dynamic`-flavoured `ReceiverType` (0.5% of 408k receiver-bearing
in-source invocations). Under `dynamic` Roslyn binds nothing at compile time, so rig already records nothing
resolvable — a dependency's surface change cannot make those facts stale, because they were never
resolution-dependent. **Accept; unchanged by the gate.**

### A7 — assembly-level attributes are attached to no symbol.

Attribute lists on *members* are in the surface (they are inside the declaration span). But
`[assembly: InternalsVisibleTo(...)]` and friends live in a file with no member declaration
(`src/mms/MMS/InternalsVisibleTo.cs`, `src/main/MedDBase.DataAccessTier/AssemblyInfo.cs`) — nothing in
`symbol_facts` represents them, so editing one of those files alone could leave `S'[p] == S[p]`.
**Mitigation: the `asmattrs` item in §2's recipe folds the assembly-level attribute list explicitly.** The
`<AssemblyAttribute>` MSBuild form (`src/mms/MMS.Drawing/MMS.Drawing.csproj:9`) is arm 1 and needs nothing.
Closed by widening.

### A8 — `partial` types split across files. A design constraint, not a hole — but it must be pinned.

**Measured: 1,728 of 22,195 types (7.8%) span more than one file**; worst case 80. The per-project
aggregate handles this correctly. It breaks the moment someone "optimizes" the aggregate to per-file: a
member added to part 2 would change the type's surface with part 1's hash untouched. **Pin it with test 7
in §5.**

### A9 — source generators.

rig builds a fresh stateless driver per project per call (`SolutionSourceLoader.cs:1428-1434`), so there is
zero generator incrementality, and generated trees are **not files on disk** — they never appear in
`Δfiles`. A generator's output for `p` depends on `p`'s own source (a new page class ⇒ a new proxy type,
itself surface) and on a dependency's surface (`RequestResponseProxyGenerator.cs:21-23` looks up
`MMS.Web.UI.ClientPage`). **Mitigation: re-run generators for every `p ∈ P1` and fold the generated trees'
token hash into `S'[p]` (the `gen` item).** Do not attempt to gate generator re-runs. Residual: a generator
whose output depends on a dependency's method *body* — none known here (the real one is symbol-driven), but
it is not provably absent. **Widen + disclose.**

### A10 — the `!:` unresolved-name recovery bucket. Currently INERT; a rebake concern if it returns.

`FactPathFinder.GraphIndex.cs:201-206,351-355` builds `ImplsByErrorInterfaceName` keyed on interface
**simple name**, consumed at `FactPathFinder.Dispatch.cs:551-561` as an always-on dispatch fallback. If it
were populated, adding a type with a colliding simple name in *any* project would change dispatch fan-out
globally — a non-dependency-shaped coupling the cascade cannot see.

**Measured: `select count(*) from type_relation_facts where RelatedSymbolId like '!:%'` → 0.** The bucket is
empty on the current store, so the recovery path is dormant. The *reference* side of partial binding is
still alive (**13,642** `reference_facts` rows have a `!:` target, 1 in 179), so the code path is not dead
code — it is one loader regression away from firing. And when it fires it is a *stage-2 rebake* concern, not
a cascade concern, so slice 3's rebake-whole rule already covers it. **Accept + disclose.**

### A11 — `const` / enum-member values.

The compiler inlines `const` into dependents. rig records a `read` reference to the field, not its value,
and a const reference reaches the extractor as an identifier, not a literal — so it lands in
`FirstArgumentName`, not `FirstArgumentTemplate` (the rules file makes the same distinction explicitly at
`builtin-rules.json:1377`: "string_argument would miss these: static-readonly is not const"). So today no
dependent fact depends on a const's value. **Include const initializers and enum-member values in the
surface anyway** — it costs nothing, and the reasoning above is one rule change away from being wrong.

### A12 — `Dependents*` from the wrong graph. The easiest thing in this design to get wrong.

Every measurement in this program — including §6's and the spike's median-6 — derives the assembly graph
from `reference_facts ⋈ symbol_facts where TargetInSource=1` (I reproduced it: 218 assemblies, 1,846 edges,
median 9 dependents, mean 25.3, p90 70, max 171 — `netstandard` 171, `MMS.NewTypes` 165, `Echo.Process` 164).
That graph is a **lower bound**: it contains an edge only where rig observed a reference. If `A` references
`B` in its `.csproj` but currently uses nothing from it, there is no edge — yet a *new* public member in `B`
(an extension method, an overload) can change how an `A` call binds.

**Mitigation: build `D` from `ProjectBuildInfo.ProjectReferences` (`ProjectBuildInfo.cs:14`), the
MSBuild-resolved graph.** Using the reference-derived graph would be a silent recall loss of exactly the
`--no-closure` class the program already learned about. Non-negotiable, and cheap — the data is already
gathered and cached.

### A13 — the purity assumption under the gate's fast path.

Arm 2 re-extracts only the edited files, which relies on `FactExtractor.Extract` being a pure function of
(one file, the Solution). The extraction-granularity audit established that (`Extraction/` has no static
mutable state; `EnclosingSymbolId` walks only syntactic ancestors), but `SymbolStringCache` is passed in and
shared across files. If it ever became order-dependent, the fast path is unsound. **Pin with the existing
`IncrementalExtractionSpikeTests` comparator, which already asserts fact-set identity against a cold
index.**

### Ranked summary

| # | case | likelihood on THIS codebase | disposition |
|---|---|---|---|
| A2 | return / member type invisible to existing columns | **certain** if you build the hash from existing columns | **widen** — compute `SurfaceHash` from declaration text. Reason the cheap design is rejected. |
| A12 | `Dependents*` from the reference-derived graph | **certain** if you reuse this program's own measurement code | **widen** — use `ProjectBuildInfo.ProjectReferences` |
| A1 | callee-body read (`ContainsYield`) | **real, 159 cross-assembly facts** | **widen** — fold `IsIterator` |
| A7 | assembly-level attributes attached to no symbol | real, 40+ such files | **widen** — fold `asmattrs` |
| A9 | source-generated trees absent from `Δfiles` | real (ClientPage generator ships here) | **widen** — fold `gen`; disclose the body-dependent-generator residual |
| A8 | partial types | 7.8% of types, up to 80 files | design constraint; pin with a test |
| A3 | `CandidateSymbols` ordering | low; already nondeterministic cold | **accept + disclose** |
| A11 | const values | none observed | widen anyway (free) |
| A10 | `!:` simple-name recovery | **0 rows today**, path is live | **accept + disclose** |
| A6 | `dynamic` | 2,067 rows, but never resolution-dependent | **accept** (pre-existing recall gap) |
| A4 / A5 | extension methods (21,630), generic inference | high volume, fully covered | no action |
| A13 | shared `SymbolStringCache` | low | pin with the existing comparator |

---

## 5. How to test it

### Does `playgrounds/DeepChain` suffice?

Partly. It has the right *shape*: 7 projects, a 5-deep chain
`Web → ApiGateway → Business → {Domain, DataAccess} → Contracts → Foundation` (from the `.csproj`
`ProjectReference`s and `DeepChain.slnx`), with `Foundation` as the hub (6 transitive dependents) and a
purpose-built cross-project binding hazard in `ApiGateway/BookingController.cs`. It already has the
retained-workspace fixture (`DeepChainPlayground`, `SolutionAnalyzer.AnalyzeRetainingWorkspaceAsync`) and a
canonical fact comparator that already includes `BodyHash` and `EndLine`
(`IncrementalExtractionSpikeTests.CanonicalFacts`, `:145-175`).

**Four additions are required** — all small, and they belong in DeepChain rather than a new playground,
because the gate's whole point is a cross-project chain:

- a **sizeable class in `Foundation`** with several instance fields, plus a `new` site in a dependent — for
  the `AllocationSizeEstimator` / private-field case;
- an **iterator method** in `Foundation` (`IEnumerable<T>` with `yield return`) plus a call site in
  `DataAccess` — for A1;
- a **partial type split across two files** in `Contracts` (`PatientDto.cs` + `PatientDto.Extra.cs`) — for A8;
- a **`Foundation/AssemblyInfo.cs`** carrying an assembly-level attribute — for A7.

### Test cases (new file `SurfaceHashGateTests.cs` — never the shared `CliApplicationTests.cs`)

Each case asserts three things: (a) the computed `Dirty` set, (b) `S'[p] == S[p]` or `≠`, and (c) that the
resulting facts are **set-equal to a cold full index of the mutated tree** (reuse `CanonicalFacts`).

| # | edit | expected |
|---|---|---|
| 1 | body-only: change a string literal inside `Foundation.Db.Query` | `Dirty` = that one file; `S'==S`; **no cascade**; facts == cold. *The program's acceptance arm 1.* |
| 2 | **return type** change: `Foundation.Db.Query(string)` `string` → `int` | `S'≠S`; `Dirty` ⊇ all files of all 6 dependents; facts == cold. **The single most valuable test: it FAILS with an existing-columns hash and PASSES with `SurfaceHash`.** |
| 3 | add a public member to `Contracts.IPatientRepository` | cascade to its 5 dependents |
| 4 | delete a public member | cascade |
| 5 | add a **private instance field** to the new `Foundation` class | cascade; the dependent's `allocation_facts.ShallowSizeBytes` at its `new` site changes and matches cold. *Acceptance for the include-private decision.* |
| 6 | add `yield return` to the new `Foundation` iterator method | cascade; the `DataAccess` call site gains an `iterator_state_machine` alloc. **FAILS without the `IsIterator` widening — this is A1's acceptance test.** |
| 7 | partial type: (a) add a member to part 2 → cascade; (b) edit a body in part 1 → no cascade | pins A8 and blocks a per-file "optimization" |
| 8 | mixed file: body edit + signature edit in one file | cascade |
| 9 | new file with only a `private` helper; deleted file | both cascade (honest, not clever) |
| 10 | `.csproj`: add a `<DefineConstants>` | arm 1 — re-evaluate `p` + all dependents, gate bypassed |
| 11 | `AssemblyInfo.cs`-only edit (add an `InternalsVisibleTo`) | cascade, via `asmattrs`. Pins A7. |
| 12 | **formatting-only** signature edit: reflow the parameter list across lines, add an interior comment | **no cascade**. Pins the token-normalized hash (a raw span slice would cascade here). |

### The verification mode — the most important deliverable in this slice

There is an exact precedent in this repo for "did my fingerprint miss an input that actually changed the
output?": `--verify-build-cache`, whose purpose statement is worth quoting because it is the same argument
(`BuildInfoEquivalence.cs:3-6`) — *"This is what no fingerprint unit test can prove — that the fingerprint
captured every input affecting the build OUTPUT. A mismatch means the fingerprint is under-specified."* The
mechanism is `SolutionSourceLoader.cs:279-305` plus the pure comparator in `BuildInfoEquivalence`.

**Ship `--verify-cascade-gate` in the same slice as the gate.** Run arm 2, apply arm 3, then *also*
re-extract everything the gate said to skip and diff the facts per project. A non-empty diff names an
under-specified surface hash and the project it fired on. That is the only construct that can turn "I
believe the hash is complete" into evidence, and it is what would have caught A1 without anyone thinking
about iterators.

### Real-store acceptance (orchestrator's job — a subagent cannot touch the MedDBase store)

Two arms on `MMS.Standard` (133 dependents): a body-only edit and a signature edit. For each, run the
resident path and a cold `rig index <MedDBase.slnx> --rules rig.rules.json` from
`c:/git/meddbase-analysis`, and diff `rig derive --format tsv`. Body arm: identical facts, one file
re-extracted. Signature arm: identical facts, cascade observed. Capture the baseline **before** dispatching
any build.

---

## 6. Shipping order — with the numbers that decide it

### Measurement

Trace: the last 300 first-parent commits of `meddbase-main-application` (103 of which touch `.cs`),
yielding **564 mapped `.cs` file-edits** (120 unmapped — files not in the indexed set). Each edit is
classified body-only vs surface-touching by regex over added/removed diff lines (member/type declarations,
attributes, interface members, constraints), tuned **conservative** — a private-member declaration counts as
surface, so this *understates* the gate's benefit. **Classification is INFERRED/heuristic; the cascade sizes
and file counts are measured from the store.** Cascade sizes use the reference-derived graph (see A12: a
lower bound, so again conservative).

"Coarse" = re-extract the edited project entirely + every transitive dependent project entirely (the
`incremental-indexing.md` fallback: "treat any dependency change as invalidating dependents").
"Gate" = coarse when the surface moved, one file when it did not.

| unit | coarse | with the gate |
|---|---|---|
| **projects** re-extracted per edit | median 6, mean 14.6, p90 43, max 157 | median **1**, mean 8.5, p90 18, max 157 |
| **source FILES** re-extracted per edit (extraction-cost proxy) | median **3,366** (27% of 12,369), mean 3,664, p90 **7,737** (63%) | median **1**, mean 1,670, p90 4,794 (39%) |
| total over the 564-edit trace | 2,066,358 file-extractions | 942,077 — **54.4% removed** |
| edits whose cascade pulls in `MedDBase.Pages` (2,595 files) or `MedDBase.DataAccessTier` (2,475) | **79.1%** | 32.6% |
| body-only share of file-edits | — | **59.4%** (335 of 564) |
| body-only edits to a 51+-dependent assembly | — | 13 of 564 = **2.3%** |

Hottest edited assemblies (edits / surface-touching / transitive dependents): `MedDBase.ServiceLayer`
122/56/13, `MedDBase.PatientPortal` 93/46/5, `MedDBase.Pages` 76/12/1, `MedDBase.EnterpriseApi` 67/27/0,
`MedDBase.BusinessLogic` 32/19/17, `MedDBase.DataAccessTier` 22/5/42.

INFERRED, in the gate's favour: this trace measures *committed* diffs. An agent's edit loop produces many
intermediate saves that are more often body-only than a finished, reviewed commit, so the real body-only
rate under the resident index is probably **above** 59%.

### Two corrections to the program doc

1. **Coarse is NOT good enough.** The doc says "median cascade is 6 of 187 assemblies … so plain Roslyn
   incrementality already delivers the SLO for the common edit". Projects here are not interchangeable: the
   median edit's 6-project cascade is **3,366 source files, 27% of the codebase**, because 79% of coarse
   cascades pull in one of the two ~2,500-file giants. Against the doc's own ~150s rig-pipeline figure that
   is tens of seconds, not "seconds".
2. **The hub-edit rationale is the weaker half of the case.** Only 8.7% of file-edits land in a
   51+-dependent assembly, and only 26.5% of *those* are body-only: 13 of 564 edits (2.3%) get a 50+-project
   cascade erased. The gate's real value is that it makes the **median** edit cost one file instead of a
   quarter of the codebase — and that it cuts giant-pulling cascades from 79% to 33%.

### Recommendation

**Coarse-first as a shipping ORDER — but do not stop there, and do NOT ship an existing-columns gate.**

1. **Slice 3 as specified *is* the coarse arm.** Correct by construction, and it is what the gate degrades
   to on an old store. Land it, measure it on MedDBase, ship it behind slice 5's staleness disclosure. It is
   genuinely useful for the ~21% of edits whose cascade stays clear of the giants.
2. **Then `SurfaceHash` + the per-project aggregate + the cascade rule, as ONE slice.** The sequencing I
   would refuse is "gate on existing columns now, add `SurfaceHash` later": per A2 that version is unsound
   on return types and member types — the most common signature change there is — and its failure mode is
   *silently serving a stale binding*, the exact thing this program exists to remove. A gate that is unsound
   on the common case is worse than no gate, because it earns trust it cannot keep. The widenings A1
   (`IsIterator`), A7 (`asmattrs`), A9 (`gen`) and A12 (MSBuild reference graph) are part of that same slice,
   not follow-ons — each is a few lines and each closes a measured hole.
3. **`--verify-cascade-gate` ships with it, not after.** A1 was found by grepping
   `DeclaringSyntaxReferences`, not by reasoning about surfaces. There is no reason to believe that grep
   found the last one.
4. **Defer the used-surface refinement.** "Cascade to dependent `q` only if `q` references a symbol whose
   containing type + name matches a changed surface symbol" would attack the residual p90 of 4,794 files,
   but it stacks a second over-approximation on the first, its soundness argument is much harder (name
   lookup and overload resolution are not reference-shaped, so it needs a name-level filter plus
   "additions/deletions always cascade"), and 33% of edits still pull in a giant regardless. Do not scope it
   until the gate has run on the real store for a while.

Prerequisite to note in the plan: adding `symbol_facts.SurfaceHash` is an extractor change, so **a full
MedDBase re-index is required** before the gate can be evaluated on the real store (~3 min warm dtb cache).
No `QueryCacheKeys` `*Schema` bump is needed — the store-identity axis covers a re-index.
