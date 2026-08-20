# Roslyn incrementality — what `AdhocWorkspace` actually gives us

**Spike, read-only.** Answers the five questions the resident-index plan
([live-background-index](../backlog/progress/live-background-index.md)) rests on, against the *pinned*
Roslyn, not against folklore.

## Provenance of every citation below

| | |
|---|---|
| Pinned package | **Microsoft.CodeAnalysis.CSharp / .Workspaces `5.6.0`** — `Directory.Packages.props:12-13`, confirmed resolved in `src/Rig.Analysis/obj/project.assets.json` |
| Correction | The brief said "pinned at Microsoft.CodeAnalysis 5.3.0". **`5.3.0` is `Microsoft.CodeAnalysis.Analyzers`** (a different package, pulled transitively). The compiler/workspace layer is `5.6.0`. |
| Exact source commit | `Microsoft.CodeAnalysis.Workspaces.dll` `ProductVersion = 5.6.0-2.26263.10+`**`c0573ed0a7dc3e3b4d2e70da47f97cc51a35524f`** |
| How the Roslyn quotes were obtained | `raw.githubusercontent.com/dotnet/roslyn/c0573ed…/<path>` — i.e. the source of the exact binary we ship against, not `main`. Type presence cross-checked by byte-scanning the shipped DLL (`SkeletonReferenceCache` ×5, `SkeletonReferenceSet` ×8, `SolutionCompilationState` ×55). |

Paths below of the form `src/Workspaces/Core/Portable/Workspace/Solution/…` and
`src/Compilers/CSharp/Portable/…` are Roslyn's, at that commit. Everything else is rig's.

---

## 1. Does `AdhocWorkspace` support the incremental path?

**Yes, fully — and it is not even the fastest path available to us.**

`AdhocWorkspace.CanApplyChange` is unconditional:

```csharp
public override bool CanApplyChange(ApplyChangesKind feature)
{
    // all kinds supported.
    return true;
}
```
— `Workspace/AdhocWorkspace.cs`

`Solution.WithDocumentText(DocumentId, SourceText, PreservationMode)` is public
(`Solution.cs:1288`), as are `WithProjectCompilationOptions` (488), `WithProjectParseOptions` (504),
`WithProjectReferences` (719), `WithProjectMetadataReferences` (804), `WithProjectAnalyzerReferences`
(898), `AddDocuments` (1081), `RemoveDocuments` (1186).

### What one changed document actually invalidates

`SolutionCompilationState.ForkProject` builds a new tracker map, and the reuse predicate is
purely the dependency graph:

```csharp
static bool CanReuse(ProjectId id, (ProjectId changedProjectId, ProjectDependencyGraph dependencyGraph) arg)
{
    if (id == arg.changedProjectId)
        return true;
    return !arg.dependencyGraph.DoesProjectTransitivelyDependOnProject(id, arg.changedProjectId);
}
```
— `SolutionCompilationState.cs:219-229`. Non-reusable trackers get `tracker.Fork(tracker.ProjectState, translate: null)` (line 292).

So the blast radius is: **the changed project, plus every project that transitively depends on it.
Siblings and dependencies are untouched (tracker object reused, `FinalCompilationTrackerState` intact,
compilation still realized).**

`Fork` demotes `Final` → `InProgress` but **keeps the old `Compilation` object** as the base
(`RegularCompilationTracker.Fork` / `ForkTrackerState`, lines 128-190) — nothing is thrown away eagerly.
Invalidation is *lazy*: the cost is only paid if someone asks for that project's compilation again.

### The three re-do costs, ranked

| layer | changed project | transitive dependent |
|---|---|---|
| **parse** | incremental reparse of the one file only | **nothing** — trees reused verbatim |
| **reference manager / imported PE symbols** | **reused** | **discarded** |
| **source symbol table + binding** | rebuilt lazily | rebuilt lazily |

For the changed project, `TouchDocumentsAction.TransformCompilationAsync` is literally
`finalCompilation.ReplaceSyntaxTree(oldTree, newTree)`
(`SolutionCompilationState.TranslationAction_Actions.cs:31-45`), and

```csharp
var reuseReferenceManager = !oldTree.HasReferenceOrLoadDirectives() && !newTree.HasReferenceOrLoadDirectives();
```
— `src/Compilers/CSharp/Portable/Compilation/CSharpCompilation.cs:1113`. No `#r` in rig's inputs ⇒ the
reference manager (all imported metadata symbols) survives. This is the single biggest saving.

For a **dependent**, `FinalizeCompilationWorkerAsync` re-resolves its reference list and then:

```csharp
if (!Enumerable.SequenceEqual(compilationWithoutGeneratedDocuments.ExternalReferences, newReferences))
{
    compilationWithoutGeneratedDocuments = compilationWithoutGeneratedDocuments.WithReferences(newReferences);
    …
}
```
— `SolutionCompilationState.RegularCompilationTracker.cs:585-588`. And `WithReferences` is explicitly
the *pessimistic* path:

```csharp
public new CSharpCompilation WithReferences(IEnumerable<MetadataReference>? references)
{
    // References might have changed, don't reuse reference manager.
    // Don't even reuse observed metadata - let the manager query for the metadata again.
```
— `CSharpCompilation.cs:708-712`.

**Mitigator (verified):** re-import is not re-read. PE assembly symbols are cached weakly *on the
metadata object* — `internal readonly WeakList<IAssemblySymbolInternal> CachedSymbols`
(`src/Compilers/Core/Portable/MetadataReference/AssemblyMetadata.cs`), consumed via
`assemblyMetadata.CachedSymbols` in `CommonReferenceManager.Binding.cs:583`. rig already caches
`AssemblyMetadata` per path (`SolutionSourceLoader.cs` `metadataCache` /
`AssemblyMetadata.CreateFromFile(p).GetReference(p)`), so as long as that cache is retained the
dependent re-binds *against cached PE symbols* rather than re-reading DLLs.

### Two implementation notes

1. **Do not use `TryApplyChanges` for the edit loop.** `Workspace.TryApplyChanges` computes
   `newSolution.GetChanges(oldSolution)` over the whole solution and *replays* the diff through
   `ApplyDocumentTextChanged` → `OnDocumentTextChanged(id, text, PreservationMode.PreserveValue)`
   (`Workspace.cs:1553`, `2004-2017`, `2224-2228`). The forked `SolutionCompilationState` you built with
   `WithDocumentText` is discarded; you pay a whole-solution diff to get an equivalent fork. Calling
   `OnDocumentTextChanged` directly is one fork and no diff — but it is `protected internal`, so it needs
   a `RigWorkspace : Workspace` (see §4; `AdhocWorkspace` is `sealed`).
2. **Feed a `SourceText`, never a `TextLoader`.** `UpdateText(SourceText/TextAndVersion, mode)` passes
   `incremental: true`; `UpdateText(TextLoader, mode)` passes `incremental: false`
   (`TextDocumentState.cs`), and `DocumentState.UpdateText` branches to
   `CreateLazyIncrementallyParsedTree` vs `CreateLazyFullyParsedTree` on exactly that flag
   (`DocumentState.cs:452-471`). rig's initial documents use `new FileTextLoader(filePath, null)`
   (`SolutionSourceLoader.cs:1100`) — correct for the cold load, wrong for the edit path.

> **VERDICT — HELPS.** The mechanism exists, is public, and is lazy; the only design constraints are
> "use `OnDocumentTextChanged`, not `TryApplyChanges`" and "pass `SourceText`".

---

## 2. Skeleton references — the plan's central premise is **false for rig**

`SkeletonReferenceCache` and `SkeletonReferenceSet` are present in the pinned DLL and behave as the plan
describes — **but only across a language boundary.** The class doc is unambiguous:

> Skeletons are used in the compilation tracker to allow **cross-language** project references with live
> semantic updating between VB/C# and vice versa. Specifically, **in a cross language case** we will build
> a skeleton ref for the referenced project and have the referrer use that to understand its semantics.

— `SolutionCompilationState.SkeletonReferenceCache.cs:22-27`

The dispatch is a single `if`:

```csharp
// If same language then we can wrap the other project's compilation into a compilation reference
if (tracker.ProjectState.LanguageServices == fromProject.LanguageServices)
{
    // otherwise, base it off the compilation by building it first.
    var compilation = await tracker.GetCompilationAsync(this, cancellationToken).ConfigureAwait(false);
    return compilation.ToMetadataReference(projectReference.Aliases, projectReference.EmbedInteropTypes);
}

if (!includeCrossLanguage)
    return null;

// otherwise get a metadata only image reference that is built by emitting the metadata from the
// referenced project's compilation and re-importing it.
… return await tracker.GetOrBuildSkeletonReferenceAsync(…);
```
— `SolutionCompilationState.cs:1312-1330`

**Every project rig puts in the workspace is C#.** `BuildProjectInfo` hard-codes
`language: LanguageNames.CSharp` (`SolutionSourceLoader.cs:871`); non-C# project references are
downgraded to file metadata refs via `NonCSharpProjectReferenceDlls`. So rig has **zero cross-language
project references and the skeleton path never executes.** Same-language dependents get a
`CompilationReference` wrapping the dependency's *live, fully-built* compilation.

Reuse rules (for completeness, since they'd matter if a VB project ever entered the set): keyed on
`Project.GetDependentSemanticVersionAsync`; identical version ⇒ reuse, differing ⇒ re-emit
metadata-only (`EmitOptions(metadataOnly: true, includePrivateMembers: false)`), with a
`ConditionalWeakTable<Compilation, AsyncLazy<SkeletonReferenceSet?>>` so two consumers of one
compilation share one emit, and a fallback to the last good skeleton on emit failure
(`SkeletonReferenceCache.cs:78-200`). It *is* a compilation-tracker feature, not a project-system one, so
it would work in `AdhocWorkspace` — it is simply never reached.

### Consequence, with numbers from the real target

Roslyn's cascade is **pessimistic**: any transitive dependent is invalidated, whether or not the
dependency's *public surface* changed. There is no surface hash anywhere in the pipeline. So the
"public API surface hash + invalidation cascade" the plan hoped to avoid building is **exactly the thing
Roslyn does not do for same-language references** — it is still on the table as a genuine, additive win.

Measured on the MedDBase store (`.rig/ae2cdb64e1cb/rig.db`, 187 in-source assemblies, 1,572 intra-set
edges, derived from `reference_facts ⋈ symbol_facts` where `TargetInSource = 1` — a **lower bound**, since
the ProjectReference graph is a superset of the actually-used graph):

| transitive-dependent count (= cascade size) | assemblies |
|---|---|
| 0 (leaf — cascade of 1) | 36 (19%) |
| 1-5 | 53 (28%) |
| 6-20 | 39 (21%) |
| 21-50 | 16 (9%) |
| **51+** | **43 (23%)** |

median **6**, mean 24, p90 **68**, max **164** (`Echo.Process`; then `MedDBase.NewTypes` 156,
`MMS.CommonInterfaces` 134, `MMS.Standard` 133).

So the median edit invalidates ~6 projects; an edit in a hub library invalidates ~70-90% of the solution
and degrades to a near-full re-extract.

> **VERDICT — BLOCKS the stated rationale, does not block the plan.** Skeleton references are
> cross-language-only and will never fire in rig's all-C# workspace. Fork (c) is still the right shape —
> live compilations, no DLLs — but "Roslyn's skeleton machinery *is* the surface-hash feature" must be
> struck from the plan, and a rig-level surface-hash gate re-enters scope as an optional second stage.

---

## 3. Source generators

### The workspace's own generator machinery *is* incremental and *does* run here

`FinalizeCompilationWorkerAsync` → `AddExistingOrComputeNewGeneratorInfoAsync` →
(no `RemoteHostClient` in-proc, so) `ComputeNewGeneratorInfoInCurrentProcessAsync`, which **retains the
driver across forks**:

```csharp
if (generatorDriver == null)
    generatorDriver = await generatorDriverCache.CreateAndRunGeneratorDriverAsync(this.ProjectState, compilationToRunGeneratorsOn, ShouldGeneratorRun, cancellationToken);
else
    generatorDriver = generatorDriver.RunGenerators(compilationToRunGeneratorsOn, ShouldGeneratorRun, cancellationToken);
```
— `SolutionCompilationState.RegularCompilationTracker_Generators.cs:307-313`. The driver lives in
`CompilationTrackerState.GeneratorInfo` and is carried through `Fork` unchanged
(`TouchDocumentsAction.TransformGeneratorDriver` is the identity, `TranslationAction_Actions.cs:53-54`).

There is also a re-use fast path: if the re-run produces byte-identical generated sources, the whole
stale generated compilation is reused (`…_Generators.cs:340-415`,
`CanUpdateCompilationWithStaleGeneratedTreesIfGeneratorsGiveSameOutput`).

Crucially, **the policy is not demoted in our host.** The demotion to `CreateOnlyRequired` /
`CreateIfAbsent` is gated on `SourceGeneratorExecution != Automatic`
(`RegularCompilationTracker.cs:620-628`), and `AdhocWorkspace` uses `MefHostServices.DefaultHost` ⇒
`DefaultWorkspaceConfigurationService` ⇒ `WorkspaceConfigurationOptions.Default` ⇒
`SourceGeneratorExecution = SourceGeneratorExecutionPreference.Automatic`
(`Workspace/IWorkspaceConfigurationService.cs`). So generators re-run on **every** fork, automatically,
and skeletons stay `Create`. Good news: no stale-generated-doc trap and no force-regeneration API needed.

### rig does not use any of that

`RunSourceGeneratorsAsync` builds a **fresh, stateless driver per project per call** and throws it away:

```csharp
GeneratorDriver driver = CSharpGeneratorDriver.Create(generators, parseOptions: parseOptions);
driver.RunGeneratorsAndUpdateCompilation(compilation, out var generatedCompilation, out _, cancellationToken);
```
— `SolutionSourceLoader.cs:1428-1434`, guarded by `catch (Exception) { return []; }`.

Two consequences:

- **Zero generator incrementality today, and none gained for free by going resident** — the driver rig
  creates has no prior state to reuse. Worse, the generator in question is a **v1 `ISourceGenerator`**
  (`[Generator] public class RequestResponseProxyGenerator : ISourceGenerator`,
  `playgrounds/LegacyNet48Web/ProxyGenerator/RequestResponseProxyGenerator.cs:11-12`), so even a retained
  driver gives no incrementality — v1 `Execute()` is re-run in full every time. Incremental generator
  caching requires `IIncrementalGenerator`, which is not ours to change (the real generator is
  MedDBase's `MMS.Tools.RequestResponseProxyProjectBuilder`).
- **Generators currently run twice per project** — once inside `project.GetCompilationAsync()` (analyzer
  refs *are* applied: `WireGeneratorAnalyzersAsync` ends with `workspace.TryApplyChanges(solution)`,
  `SolutionSourceLoader.cs:1414-1418`), then again by rig's own driver over that already-generated
  compilation. Independent corroboration that the workspace really does run them: rig's own heap finding
  that compilations stay rooted via "the `AdhocWorkspace`'s `SolutionCompilationState` (incremental
  source-generator DriverStateTable)" (`SolutionAnalyzer.cs`, pre-save `gcroot` comment). The in-code
  comment at `SolutionSourceLoader.cs:1295-1297` ("`GetSourceGeneratedDocumentsAsync` does not execute
  generators … it returns nothing") is at best about the *document* API, not about whether generators run.

### Would a resident workspace make the ClientPage flake better or worse?

**Better in frequency, worse in blast radius — and the resident design has to fix two swallowed
failures first.** (This section is INFERRED from code reading; it has not been reproduced.)

The wiring has two silent-null paths that a long-lived process turns from *transient* into *sticky*:
`EmitCompilationToTempAsync` returns `null` on any emit failure or exception
(`SolutionSourceLoader.cs:1397-1401`) ⇒ the analyzer reference is never added ⇒ zero generated types,
no diagnostic; and `RunSourceGeneratorsAsync`'s blanket `catch` returns `[]`. Today a bad draw costs one
index. In a resident process a bad draw at startup costs **every subsequent query for the process
lifetime**.

The most likely mechanism for the flake itself is in the generator, not in rig (INFERRED):

```csharp
var clientPageSymbol = context.Compilation.GetTypeByMetadataName("MMS.Web.UI.ClientPage");
if (clientPageSymbol == null)
    return;                      // ← silent, total no-op
```
— `RequestResponseProxyGenerator.cs:21-23`. `GetTypeByMetadataName` returns **null when the name is
ambiguous across two referenced assemblies**, which is precisely the dual-assembly-identity condition
rig's own loader comments warn about and which can flip depending on whether a playground `bin/` happens
to be populated by a concurrent build — matching the reported signature exactly (fails under the full
suite, passes in isolation). Cheap check when this is picked up: in a failing run, log
`compilation.GetTypesByMetadataName("MMS.Web.UI.ClientPage").Length`; `>1` confirms it.

> **VERDICT — COMPLICATES.** The workspace's generator path is incremental, retained, and (uniquely for
> our host) never policy-demoted — but rig bypasses it, runs generators twice, and the generator is a v1
> `ISourceGenerator` that cannot be incremental. Going resident is net-positive only if the two silent
> `null`/`[]` paths become loud, because a resident process makes a startup flake permanent.

---

## 4. Project-file changes

`.csproj` / `.props` / `.targets` changes force a Buildalyzer design-time rebuild for that project and a
new `ProjectInfo` — that part is unavoidable. The question was whether one project's `ProjectInfo` can be
swapped without rebuilding the solution. **Yes, two supported ways.**

**(a) `Workspace.OnProjectReloaded(ProjectInfo)`** — `protected internal virtual`, `Workspace.cs:820-832`:

```csharp
return this.AdjustReloadedProject(
    oldSolution.GetRequiredProject(projectId),
    oldSolution.RemoveProject(projectId).AddProject(reloadedProjectInfo).GetRequiredProject(projectId)).Solution;
```

Same `ProjectId` preserved, so dependents' `ProjectReference`s stay valid. Because it goes through
`SetCurrentSolution`, dependents cascade normally. **Requires a `RigWorkspace : Workspace`** —
`AdhocWorkspace` is `sealed`, and this is the same subclass §1 wanted for `OnDocumentTextChanged`. Since
`AdhocWorkspace` adds nothing rig uses beyond `AddSolution`/`AddProject`/`CanApplyChange`, the swap is
small and unlocks the whole `On*` surface (`OnProjectReferenceAdded/Removed`,
`OnMetadataReferenceAdded/Removed`, `OnAnalyzerReferenceAdded/Removed`, `OnCompilationOptionsChanged`,
`OnParseOptionsChanged`, `OnDocumentAdded/Removed`, `Workspace.cs:867-1100`).

**(b) Compose it from public `Solution` APIs.** Roslyn's own internal `Solution.WithProjectInfo`
(`Solution.cs:628`) is *only* a composition of methods that are all public:

```csharp
WithProjectAttributes(info.Attributes)
  .WithProjectCompilationOptions(projectId, info.CompilationOptions)
  .WithProjectParseOptions(projectId, info.ParseOptions)
  .WithProjectReferences(projectId, info.ProjectReferences)
  .WithProjectMetadataReferences(projectId, info.MetadataReferences)
  .WithProjectAnalyzerReferences(projectId, info.AnalyzerReferences)
// then add/remove/update documents
```
— `SolutionCompilationState.cs:608-630`. Only `WithProjectAttributes` is internal, and its parts
(`WithProjectAssemblyName`, `WithProjectName`, `WithProjectFilePath`, …) are public
(`SolutionCompilationState.cs:590-606` shows the decomposition). This route needs no subclass but pays
the `TryApplyChanges` diff.

Prefer (a). Watch the granularity: `WithProjectParseOptions` translates to
`ReplaceAllSyntaxTreesAction` (`SolutionCompilationState.cs:530-533`) — a full reparse of that project —
whereas `WithProjectMetadataReferences` does not. A design-time rebuild that yields byte-identical
`ParseOptions` should be diffed and skipped, not applied blindly.

### One hard hazard

**Never `RemoveProject` a project other projects still reference.** `FinalizeCompilationWorkerAsync`
silently drops a dangling reference:

```csharp
var referencedProject = compilationState.SolutionState.GetProjectState(projectReference.ProjectId);
// Even though we're creating a final compilation (vs. an in progress compilation),
// it's possible that the target project has been removed.
if (referencedProject is null)
    continue;
```
— `RegularCompilationTracker.cs:498-503`. Dependents then bind with the reference *missing* and no error
surfaced to us — the same recall-loss class as the `--no-closure` finding
(`live-background-index.md`, "What the spike killed"). `OnProjectReloaded` is safe because it
remove-and-re-adds under the same `ProjectId` inside one `SetCurrentSolution`.

> **VERDICT — HELPS.** Per-project `ProjectInfo` swap is supported and cheap on the workspace side; the
> only real cost is the design-time rebuild rig already has to pay. Cost of admission is replacing
> `AdhocWorkspace` with a small `Workspace` subclass.

---

## 5. Memory

### What a retained realized Solution costs

`FinalCompilationTrackerState` holds compilations by **strong** field:

```csharp
public readonly Compilation FinalCompilationWithGeneratedDocuments;
public override Compilation CompilationWithoutGeneratedDocuments { get; }
```
— `SolutionCompilationState.CompilationTracker.CompilationTrackerState.cs:162,171`. Plus a lazily-built
`RootedSymbolSet` over the final compilation (line 237). Trackers are held in
`_projectIdToTrackerMap`, which the `Workspace` roots via `CurrentSolution`.

**There is no public API to hold compilations weakly.** No knob, no policy, nothing analogous to the
recoverable-text machinery. Confirmed by exhaustion: the only `forkTracker: false` (drop-the-tracker)
call site in the whole file is `WithProjectParseOptions`, and it is gated on `PartialSemanticsEnabled`
(`SolutionCompilationState.cs:514-527`), which `AdhocWorkspace` does not enable.

### Levers that do exist, ranked

1. **Don't retain `SemanticModel`s / `SyntaxNode` roots — rig's own A1.** This is the dominant lever and
   it is rig's bug, not Roslyn's: `SolutionSourceSet.IndexedSources` is a
   `IReadOnlyList<SourceModel(…, SyntaxTree Tree, SyntaxNode Root, SemanticModel SemanticModel)>`
   (`RoslynAnalysisModels.cs:12,23`) accumulated for **every file of every project** before extraction
   starts. rig's own heap analysis already names this the biggest live contributor
   (`docs/memory-optimization-strategies.md` §A1) and its own pre-save `gcroot` found the graph rooted by
   *two* paths — the workspace state **and** the extract `Parallel.For` closure. A resident process makes
   this mandatory, not optional: today the whole thing dies with the process.
2. **Recoverable text is already on, free.** `PreservationMode.PreserveValue` (the default for
   `WithDocumentText` and for `Workspace.ApplyDocumentTextChanged`) routes through
   `CreateRecoverableText` → `RecoverableTextAndVersion`, which holds `SourceText` **weakly** after first
   use and spills to temporary storage (`TextDocumentState.cs:136`,
   `RecoverableTextAndVersion.RecoverableText.cs:19-49`). `PreserveIdentity` would defeat it —
   don't pass it.
3. **Per-project drop-and-re-realize, at project granularity only.** `OnProjectReloaded` /
   remove-and-re-add replaces the tracker, making the old compilation collectible. Because `ProjectInfo`
   is immutable and cheap to retain, rig can **cache the `ProjectInfo` per project** and re-add from cache
   with **no design-time rebuild** — that is the closest thing to "drop and re-realize on demand" the API
   allows. Re-realizing costs a full rebind of that project *and* cascades to its dependents (§1), so it
   only pays off for projects with few dependents — which, per §2's distribution, is 47% of them
   (≤5 dependents).
4. **Runtime GC knobs.** `DOTNET_GCConserveMemory` (0-9) and Server-vs-Workstation GC, already scoped in
   `docs/memory-optimization-strategies.md` §263-277. A resident process changes the objective from "peak
   during a 253s run" to "steady-state RSS between edits", which makes conserve-memory a much better
   trade than it is today.
5. **`Workspace.ClearSolution()`** — the all-or-nothing reset. Worth wiring as an explicit
   `rig serve --reset` escape hatch rather than leaking.

### Sizing note

The measured 12.1 GB peak (`docs/spikes/ide-architecture-steals.md:31-32`) is *not* the resident
steady-state figure. It is co-resident semantic state + the growing fact arrays + the 3.47 GB fact graph.
A resident design that fixes lever 1 converts the semantic component from "sum over all 187 projects" to
"max single cascade" — but adds a permanent floor of every project's `Compilation` + reference manager +
declaration table, which today is transient. **That floor is unmeasured and is the main open risk in the
plan.** It is cheap to measure before committing: index MedDBase, keep the workspace alive after
extraction with `IndexedSources` cleared, and take a `dotnet-gcdump`.

> **VERDICT — COMPLICATES (the largest open risk).** No weak-compilation lever exists; the only unit of
> release is the project, and it cascades. The resident design is memory-feasible only if rig stops
> retaining `SemanticModel`s, and the steady-state floor must be measured before the architecture is
> locked.

---

## What this means for rig

- **Strike the skeleton-reference rationale from the plan.** Skeletons are cross-language-only
  (`SkeletonReferenceCache.cs:22-27`; `SolutionCompilationState.cs:1312-1318`). rig's workspace is 100% C#
  (`SolutionSourceLoader.cs:871`), so the feature never fires. Fork (c) survives on its other merit — no
  output DLLs needed — not on this one.
- **The "public API surface hash + invalidation cascade" is back in scope**, as the optional second stage
  it always was. Roslyn's cascade is dependency-shaped and surface-blind; a rig-level gate ("A's public
  surface unchanged ⇒ reuse A's dependents' facts from the last run") is a real win Roslyn will not
  provide, and `symbol_facts.BodyHash` shows the machinery is already half-built.
- **Ship stage 1 without it anyway.** Median cascade is 6 of 187 assemblies and 47% of projects have ≤5
  transitive dependents, so plain Roslyn incrementality already delivers the SLO for the common edit.
- **Budget honestly for hub edits.** 23% of assemblies have 51+ transitive dependents, up to 164
  (`Echo.Process`, `MedDBase.NewTypes`, `MMS.CommonInterfaces`, `MMS.Standard`). An edit there is a
  near-full re-extract. The resident index needs a "this will take a while" path, not a promise of
  seconds.
- **Replace `AdhocWorkspace` with a small `RigWorkspace : Workspace`.** It is sealed, and the two APIs
  the design needs — `OnDocumentTextChanged` (one fork, no whole-solution diff) and `OnProjectReloaded`
  (per-project `ProjectInfo` swap) — are `protected internal`. Small diff, unlocks the whole `On*` surface.
- **Feed `SourceText`, not `TextLoader`, on the edit path** — that flag alone selects incremental vs full
  reparse (`DocumentState.cs:452-471`). And never `PreservationMode.PreserveIdentity`: it disables
  recoverable text.
- **Cache `AssemblyMetadata` for the process lifetime.** `WithReferences` deliberately re-imports
  (`CSharpCompilation.cs:710-711`), and the only thing that keeps that from being a re-read of every
  paket DLL is `AssemblyMetadata.CachedSymbols`. rig's `metadataCache` must outlive individual solutions.
- **rig runs source generators twice per project, and the generator is a v1 `ISourceGenerator`.** The
  workspace path is retained + incremental + never policy-demoted in our host (default
  `SourceGeneratorExecution = Automatic`); rig's hand-rolled `CSharpGeneratorDriver.Create` per call is
  neither. Consolidating onto the workspace path is a prerequisite for generator incrementality — and
  v1 generators cap the ceiling regardless.
- **Make the two silent generator failures loud before going resident.** `EmitCompilationToTempAsync`
  returning `null` and `RunSourceGeneratorsAsync`'s blanket `catch → []` turn a one-index flake into a
  process-lifetime one. Same class of hazard: never `RemoveProject` a referenced project —
  `FinalizeCompilationWorkerAsync` drops the dangling reference silently
  (`RegularCompilationTracker.cs:498-503`).
- **Memory is the main unquantified risk, and rig's own A1 is now a prerequisite.** No API holds
  compilations weakly (`CompilationTrackerState.cs:162`); the only release unit is the project, and
  releasing cascades. Measure the steady-state floor (index MedDBase, keep the workspace alive with
  `IndexedSources` cleared, `dotnet-gcdump`) before locking the architecture.
