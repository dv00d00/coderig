# Rider file-effects plugin contract

> Status: research spike, 2026-08-30. This note uses only JetBrains documentation and
> JetBrains-owned source repositories. It distinguishes **documented fact**, **source fact**, and
> **inference/recommendation** because the public ReSharper SDK guide still targets Platform
> 2022.2.1, while current JetBrains plugins show newer APIs in practice.

## Conclusion

The smallest credible implementation is a **ReSharper backend plugin with a per-file daemon
stage**, backed by an asynchronous cache populated from the out-of-process rig host. It does not
need a Kotlin/IntelliJ frontend for the first spike.

The external host should not return editor coordinates. It should return rig's stable method DocIDs
and effect summaries for one file/context. The backend plugin joins those DocIDs to the current C#
PSI declarations, obtains exact `DocumentRange`s from the current editor snapshot, and publishes
normal ReSharper `IHighlighting`s. This keeps stale SQLite/source coordinates out of the editor and
lets Rider own range tracking and invalidation.

The resulting data flow is:

```text
ReSharper daemon starts for one C# PSI file
        |
        +-- cache hit for current file/context/source snapshot
        |      -> DocID -> current IMethodDeclaration -> DocumentRange -> IHighlighting
        |
        `-- cache miss
               -> enqueue one cancellable host request, commit no rig highlights yet
               -> host returns method DocIDs + effects
               -> update cache
               -> DaemonBase.Invalidate()
               -> daemon reruns and publishes current ranges
```

This differs from the initial mental model in one useful way: a selected-file event exists, but it
does not have to be the load-bearing trigger. The daemon already owns the supported lifecycle for
as-you-type C# highlighting.

## Evidence labels

- **Documented fact**: stated by current official JetBrains SDK documentation.
- **Source fact**: demonstrated in a JetBrains-owned repository at a pinned commit.
- **Inference**: proposed rig design derived from those facts; not claimed as a JetBrains guarantee.

## 1. File focus, open, and document-change notifications

### If the spike explicitly wants a selected-file event

**Documented fact.** IntelliJ plugins use the project-level `FileEditorManagerListener` for file
open, close, and selection changes. The official editor FAQ names this listener for exactly those
events. `FileEditorManager` is also the generic API for obtaining the active editor:
[Editors FAQ](https://plugins.jetbrains.com/docs/intellij/editors.html#editors-faq).

**Source fact.** The listener has `fileOpened`, `fileClosed`, and `selectionChanged`; its Javadoc
states that every callback runs on the EDT. See
[`FileEditorManagerListener`](https://github.com/JetBrains/intellij-community/blob/ab48c424a25bea91b8873d41fb9c587f6afe1bae/platform/analysis-api/src/com/intellij/openapi/fileEditor/FileEditorManagerListener.java#L14-L65).
Therefore a listener may capture the new file identity and schedule work, but must not call rig or
walk PSI synchronously.

For text changes, the supported choices are:

- `Document.addDocumentListener(...)` for one document;
- `EditorFactory.getEventMulticaster().addDocumentListener(...)` for all open documents;
- `FileDocumentManagerListener` for save/reload notifications.

These are listed in the official [Documents](https://plugins.jetbrains.com/docs/intellij/documents.html#how-do-i-get-notified-when-documents-change)
documentation. The source contract also says not to modify the emitting document from a listener and
recommends `BulkAwareDocumentListener.Simple` for performance:
[`DocumentListener`](https://github.com/JetBrains/intellij-community/blob/ab48c424a25bea91b8873d41fb9c587f6afe1bae/platform/core-api/src/com/intellij/openapi/editor/event/DocumentListener.java#L12-L49).

### What should actually trigger highlights

**Documented fact.** ReSharper daemon stages are created per document; `CSharpDaemonStageBase`
provides an `ICSharpFile`, and committing a `DaemonStageResult` publishes the file's highlights.
See [Daemons](https://www.jetbrains.com/help/resharper/sdk/Daemons.html). The daemon subsystem is
specifically described as reacting to solution/environment changes and providing background
highlighting: [Architectural overview](https://www.jetbrains.com/help/resharper/sdk/Architecture_Overview.html#daemon).

**Inference.** Use a custom per-file daemon stage, not an `ElementProblemAnalyzer<IMethodDeclaration>`.
An element analyzer is called once for every matching tree node; that is the wrong lifecycle for a
single file-shaped host request. A custom stage receives the whole `ICSharpFile`, can do one cache
lookup, and can publish all matching methods together.

Restrict the first spike to the visible/current-document daemon process kind. JetBrains' own daemon
guide describes `DaemonProcessKind` as the discriminator between visible-document checking and
solution-wide analysis: [JetBrains .NET Tools blog daemon walkthrough](https://blog.jetbrains.com/dotnet/2010/07/20/writing-plug-ins-for-resharper-part-2-of-n/).
The exact enum spelling should be compiled against the targeted Rider SDK rather than copied from
the old article.

## 2. Where C# methods and exact ranges live

### Frontend versus backend

**Documented fact.** Rider's IntelliJ frontend does not build a full C# PSI. C# syntax and semantic
models, inspections, rewrites, quick fixes, and refactorings run in the out-of-process ReSharper
backend. JetBrains explicitly notes that many plugins can be backend-only and Rider will display
their inspection results: [IntelliJ Platform — Rider](https://plugins.jetbrains.com/docs/intellij/intellij-platform.html#rider).
The Rider plugin guide says the same: the frontend supplies UI, while ReSharper supplies .NET
language features: [Rider plugin development](https://plugins.jetbrains.com/docs/intellij/rider.html).

**Recommendation.** Resolve C# methods and create highlightings in the ReSharper backend. Do not try
to reconstruct C# methods from IntelliJ frontend PSI, and do not add an RD frontend/backend model
until a genuinely custom frontend UI requires one. If that UI appears later, Rider's supported
frontend/backend transport is an extensible generated RD model:
[Rider protocol extension](https://www.jetbrains.com/help/resharper/sdk/Rider.html#protocol-extension).

### Method enumeration and identity

**Documented fact.** C# method declarations are `IMethodDeclaration` tree nodes and expose a
`NameIdentifier`. ReSharper supports typed child collections and visitor/recursive-processor tree
walking; see [Strongly typed navigation](https://www.jetbrains.com/help/resharper/sdk/StronglyTypedNavigation.html)
and [Navigating syntax trees](https://www.jetbrains.com/help/resharper/sdk/NavigatingSyntaxTrees.html).

**Documented fact.** CLR type members implement `IXmlDocIdOwner`, whose `XMLDocId` is used by
ReSharper for XML documentation identity:
[Existing QuickDoc providers](https://www.jetbrains.com/help/resharper/sdk/ExistingProviders.html#elements-without-documentation).
This is the natural join key because rig's `SymbolFact.SymbolId` is also the documentation-comment
ID. Constructors, accessors, operators, and explicit-interface methods must be covered by a tracer
test before assuming every executable declaration maps one-to-one; plain methods are the first
slice.

### Exact ranges

**Documented fact.** ReSharper highlightings return a `DocumentRange`. `DocumentRange` combines a
document with a `TextRange`; its offsets are the editor's concrete coordinate system. `TextRange`
uses an inclusive start and exclusive end:
[Text control and DocumentRange](https://www.jetbrains.com/help/resharper/sdk/TextControl.html#documentrange),
[TextRange semantics](https://www.jetbrains.com/help/resharper/sdk/TextBuffers.html#text-range).

For a method marker there are two useful PSI-owned ranges:

- `declaration.NameIdentifier.GetHighlightingRange()` for the method-name token, demonstrated by the
  official [code-inspection sample](https://www.jetbrains.com/help/resharper/sdk/Features__Code_Inspections.html#sample-implementation);
- `declaration.GetDocumentRange()` for the entire declaration, demonstrated by JetBrains' Rider
  Unity line-marker implementation:
  [`RiderPerformanceLineMarkerAnalyzer`](https://github.com/JetBrains/resharper-unity/blob/a891ba108ce401b5479d4949afc43479b73df70e/resharper/resharper-unity/src/Unity.Rider/Common/CSharp/Daemon/Stages/PerformanceCriticalCodeAnalysis/Analyzers/RiderPerformanceLineMarkerAnalyzer.cs#L11-L19).

**Recommendation.** The semantic file read model may expose `startOffset`/`endOffset` for tests or a
future frontend, but these offsets must be produced by the plugin after the DocID-to-PSI join. Rider
itself should receive `DocumentRange`s directly through `IHighlighting`. Line/column pairs are a
derived display format, not the primary contract.

## 3. Publishing highlights and gutter annotations

**Documented fact.** The supported as-you-type surface is `IHighlighting`, produced by an element
analyzer or custom daemon stage. `CalculateRange()` supplies the range, `IsValid()` guards a PSI
element, and static/configurable severity attributes control presentation. The standard APIs cover
underlines, backgrounds, marker-bar entries, and gutter icons:
[Analysis](https://www.jetbrains.com/help/resharper/sdk/Analysis.html),
[Code inspections](https://www.jetbrains.com/help/resharper/sdk/Features__Code_Inspections.html).
Gutter marks are normal highlights and use the same daemon testing infrastructure:
[Testing daemon stages and gutter marks](https://www.jetbrains.com/help/resharper/sdk/Analysis_Testing.html#testing-gutter-marks-and-dead-code).

**Source fact.** JetBrains' Unity plugin uses a `CSharpDaemonStageBase`, receives one `ICSharpFile`,
walks its declarations, reads an externally updated profiler snapshot cache, collects highlightings,
and commits one `DaemonStageResult`:
[`UnityProfilerDaemon`](https://github.com/JetBrains/resharper-unity/blob/a891ba108ce401b5479d4949afc43479b73df70e/resharper/resharper-unity/src/Unity.Rider/Common/CSharp/Daemon/Profiler/UnityProfilerDaemon.cs#L31-L109).
It implements custom gutter styling through `ICustomAttributeIdHighlighting`:
[`UnityGutterMarkInfo`](https://github.com/JetBrains/resharper-unity/blob/a891ba108ce401b5479d4949afc43479b73df70e/resharper/resharper-unity/src/Unity/CSharp/Daemon/Errors/UnityGutterMarkInfo.cs#L9-L55).

**Source fact.** When external profiler data changes, the same plugin updates its cache and schedules
`DaemonBase.GetInstance(solution).Invalidate()` under `StartMainRead`, causing re-analysis:
[`UnityProfilerSnapshotProvider`](https://github.com/JetBrains/resharper-unity/blob/a891ba108ce401b5479d4949afc43479b73df70e/resharper/resharper-unity/src/Unity.Rider/Common/CSharp/Daemon/Profiler/UnityProfilerSnapshotProvider.cs#L423-L456).

**Recommendation.** Mirror this exact architecture for rig:

1. an async solution component owns the host connection and immutable per-file result cache;
2. the daemon stage never waits for host I/O; it renders a matching cached result or enqueues one
   request and commits an empty rig result;
3. completion replaces the cache entry and invalidates the daemon;
4. document edits naturally rerun the daemon, where stale cache keys no longer match.

“Static” highlighting severity does not mean storing a permanent editor range. The daemon remains
the lifecycle owner and republishes current results after changes.

## 4. Threading, read actions, and cancellation

### Frontend constraints

**Documented fact.** `FileEditorManagerListener` callbacks are EDT callbacks. IntelliJ requires EDT
work to be short, requires read actions for PSI/VFS/project-model reads off EDT, and recommends
cancellable coroutine read actions or non-blocking read actions for long work. It explicitly says
not to traverse PSI, resolve references, query indexes, or perform long operations on EDT:
[Threading model](https://plugins.jetbrains.com/docs/intellij/threading-model.html).
I/O and external-process work belongs on `Dispatchers.IO`, while a project service's injected
coroutine scope is cancelled when the project closes or the plugin unloads:
[Coroutine dispatchers](https://plugins.jetbrains.com/docs/intellij/coroutine-dispatchers.html),
[Coroutine scopes](https://plugins.jetbrains.com/docs/intellij/coroutine-scopes.html#service-scopes).

### Backend constraints

**Documented fact.** PSI/project-model reads require a ReSharper read lock. The SDK navigation
example uses `IShellLocks.ExecuteOrQueueReadLock` before reading the node under the caret:
[Use manual navigation](https://www.jetbrains.com/help/resharper/sdk/UseManualNavigation.html).
Complex element analyzers are expected to call `ElementProblemAnalyzerData.ThrowIfInterrupted()`:
[Code inspections — analyzer data](https://www.jetbrains.com/help/resharper/sdk/Features__Code_Inspections.html#elementproblemanalyzer).

**Source fact.** JetBrains' Unity provider starts its external snapshot fetch in a background
activity, uses a sequential lifetime so a newer request terminates the older one, and handles
cancellation separately from failure:
[`UnityProfilerSnapshotProvider`](https://github.com/JetBrains/resharper-unity/blob/a891ba108ce401b5479d4949afc43479b73df70e/resharper/resharper-unity/src/Unity.Rider/Common/CSharp/Daemon/Profiler/UnityProfilerSnapshotProvider.cs#L286-L328).

**Recommendation.** Never wait on the rig host from EDT, `IDaemonStageProcess.Execute`, or while
holding a PSI read lock. Copy only small immutable request data under the daemon's read context,
queue I/O on a per-solution background lifetime, cancel the prior request for the same
file/context/selector, and discard any response whose snapshot token no longer matches. The daemon
render pass should only join cached DocIDs to current PSI and create highlightings.

## 5. Freshness and document versions

**Source fact.** IntelliJ `Document` is a `ModificationTracker`; `getModificationStamp()` changes on
every content modification and is explicitly unrelated to filesystem modification time:
[`Document.getModificationStamp`](https://github.com/JetBrains/intellij-community/blob/ab48c424a25bea91b8873d41fb9c587f6afe1bae/platform/core-api/src/com/intellij/openapi/editor/Document.java#L183-L192).
This is useful if a frontend listener participates.

**Documented fact.** PSI/tree objects can become invalid after edits; ReSharper highlightings expose
`IsValid()`, and JetBrains warns not to retain declared elements across edits without an envoy/pointer
mechanism: [Implementing a QuickDoc provider](https://www.jetbrains.com/help/resharper/sdk/ImplementingProvider.html).

**Recommendation.** Keep three different versions rather than calling all of them “stamp”:

- `clientSnapshotToken`: opaque value generated by the plugin for the current document content;
  an IntelliJ modification stamp is acceptable in a frontend implementation, while a content digest
  works across frontend/backend boundaries. The host only echoes it. It rejects late responses.
- `indexedSourceDigest`: digest of the source snapshot represented by rig's graph. The host compares
  it with an optional digest in the request and reports `exact`, `stale`, or `unindexed`.
- `graphGeneration`: immutable rig resident-generation identity. It explains which call graph and
  derived effects produced the answer.

Do not persist `DocumentRange` or PSI declarations in the host-response cache. Persist DocIDs and
effect data; recreate current ranges on every daemon render pass.

An empty `methods` array means “no selected effects” only when `sourceStatus == exact`. With a stale,
unindexed, unavailable, or ambiguous context it is not a negative answer.

## 6. File path is not a sufficient identity

**Documented fact.** `IPsiSourceFile` represents a file inside an `IPsiModule` compilation unit. One
project can produce multiple modules, and one `IProjectFile` can map to multiple `IPsiSourceFile`s.
The SDK explicitly says to use `ToSourceFiles()` unless there is certainly only one source file:
[Navigate code — project model and PSI](https://www.jetbrains.com/help/resharper/sdk/NavigateCode.html#project-model-basics).

**Source fact.** JetBrains' Unity daemon checks both the source file and its owning project before
deciding support, via `sourceFile.ToProjectFile()?.GetProject()`:
[`UnityProfilerDaemon.IsSupported`](https://github.com/JetBrains/resharper-unity/blob/a891ba108ce401b5479d4949afc43479b73df70e/resharper/resharper-unity/src/Unity.Rider/Common/CSharp/Daemon/Profiler/UnityProfilerDaemon.cs#L55-L69).

**Inference.** `filePath` alone cannot disambiguate linked files, multi-target projects, conditional
compilation, or different references. The request must carry a compilation context that both sides
can understand. A JetBrains-internal `IPsiModule` identity is unsuitable as the external protocol
key because rig does not know it. The portable first choice is:

```text
projectPath + targetFramework/compilationMoniker + normalized filePath
```

For the current rig index, `targetFramework` must identify the framework selected by `rig index`.
If the host has only one indexed context, a mismatching or absent Rider context should return
`ambiguous-context`/`context-not-indexed`, not silently union results from unrelated compilations.

## 7. Smallest realistic rig-host seam

This is an **inference/recommendation**, intentionally transport-neutral. Local named pipe, Unix
domain socket, or loopback HTTP are implementation choices; none changes the semantic contract.

### Host request

```json
{
  "protocolVersion": 1,
  "requestId": "opaque",
  "solutionKey": "normalized solution or analysis-root identity",
  "file": {
    "path": "/repo/src/PatientService.cs",
    "projectPath": "/repo/src/App/App.csproj",
    "compilationMoniker": "net10.0"
  },
  "clientSnapshotToken": "opaque-to-host",
  "sourceDigest": "optional digest of the current editor text",
  "effectSelector": {
    "families": ["sql"]
  }
}
```

### Host response

```json
{
  "protocolVersion": 1,
  "requestId": "opaque",
  "file": {
    "path": "/repo/src/PatientService.cs",
    "projectPath": "/repo/src/App/App.csproj",
    "compilationMoniker": "net10.0"
  },
  "clientSnapshotToken": "opaque-to-host",
  "graphGeneration": "generation-id",
  "indexedSourceDigest": "digest-or-null",
  "sourceStatus": "exact",
  "methods": [
    {
      "symbolId": "M:App.PatientService.Save(App.Patient)",
      "effects": [
        {
          "family": "sql",
          "operation": "write",
          "resource": "patient",
          "nearestDepth": 3
        }
      ]
    }
  ]
}
```

`resource` may be null/unknown. `effects` should be semantic aggregates, not every terminal call
site. A separate lazy request can ask for a witness path after the user clicks a marker. The file
response must not contain line numbers, columns, or offsets from the rig store.

### Plugin-local read model

After joining response DocIDs to the current `ICSharpFile`, the backend owns the editor-shaped model:

```text
FileEffectPresentation {
  clientSnapshotToken
  graphGeneration
  sourceStatus
  methods[] {
    symbolId
    declarationRange: DocumentRange
    nameRange: DocumentRange
    effects[]
  }
}
```

This is the “read model for a whole file”: one cache/request unit containing many method rows. It is
not one graph traversal or one host request per method.

## 8. Spike acceptance test

The next throwaway spike should prove only the seam, not plugin productization:

1. A fake host returns two method DocIDs and different SQL effect aggregates for one file request.
2. A backend `CSharpDaemonStageBase` issues/enqueues at most one request for that file snapshot.
3. The daemon resolves both DocIDs against the current `ICSharpFile`, creates two highlightings with
   PSI-derived `DocumentRange`s, and ignores a clean method.
4. Editing text changes the client snapshot; a late old response is discarded.
5. Completing the current response updates the cache and calls daemon invalidation; no synchronous
   network/process wait occurs in the daemon stage.
6. The same physical file in two project/module contexts either returns the indexed context only or
   an explicit context error; it is never silently unioned.

JetBrains provides `HighlightingTestBase`/`CSharpHighlightingTestBase` for daemon and gutter tests:
[Testing daemon stages](https://www.jetbrains.com/help/resharper/sdk/Analysis_Testing.html). A single
manual Rider run is still required to prove host process discovery/connection and actual gutter/UI
rendering, because those are outside the pure daemon test harness.

## Decision for the rig spike

- **Keep:** one host request/read-model per file + compilation context.
- **Change:** host rows become `symbolId + effect summaries`; exact spans are a backend projection,
  not host data.
- **Use:** ReSharper backend daemon highlighting and invalidation.
- **Defer:** Kotlin frontend, RD protocol, custom tool window, witness-path UI, settings UX.
- **Reject:** SQL/SQLite query from EDT or daemon execution, per-method host calls, path-only file
  identity, and treating a stale empty response as “no SQL effects.”
