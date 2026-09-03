# An .slnx project path that escapes the solution directory is silently dropped

**Status:** done — shipped 2026-09-03
**Triage:** ready-for-agent

## Symptom

Indexing `C:\Git\AngleSharp.ReadOnlyDom\AngleSharp.ReadOnlyDom.slnx` (14 projects) produced a store
with **12**. The missing project was the one whose `<Project Path>` leaves the solution directory:

```xml
<Folder Name="/External/">
  <Project Path="../AngleSharp/src/AngleSharp/AngleSharp.Core.csproj" />
</Folder>
```

There is **no error, no warning, and no skip message**. `Excluding 1 test project(s)` accounts for
the test project; the 13th project just never appears again. The only visible trace is an arithmetic
mismatch between two progress lines that nobody reads together:

```
MSBuild: design-time build 13/13: ...        <- 13 attempted
build cache: 0 hit(s), 12 miss(es) of 12 project(s)   <- 12 recorded
Assembling workspace from 12 project(s)
```

`rig index --time` is the only thing that names it, and only because the duration `finally` block
records every attempt:

```
slowest: AngleSharp.Core.csproj 1.8s, LoadRunner.csproj 0.0s, ...
```

1.8s of real build work, and then the result is thrown away.

## Why it matters here

This is not a browse-only solution-folder entry. `Directory.Build.targets` in that repo *removes*
the `AngleSharp` PackageReference and substitutes a ProjectReference to the sibling source checkout,
so `AngleSharp.Core` is a first-class source dependency of the `ReadOnlyDom` and `Compact` projects.
Dropping it costs the cross-project call edges: `tree`/`path`/`reaches` cannot descend from
ReadOnlyDom into AngleSharp at all. Measured on that solution:

| index                              | projects | symbols | call edges |
| ---------------------------------- | -------- | ------- | ---------- |
| as-is (escaping relative path)     | 12       | 3,647   | 4,140      |
| with the path made absolute        | 13       | 15,041  | 21,344     |

This is the same class of failure as the documented `--from` gotcha (a store that *looks* complete
because rule-tagged effects still surface at first-party call sites via `receiverTypes`, while the
callee internals are simply absent), except it happens on a plain full-solution index, where the
user has done nothing wrong and has no reason to suspect narrowing.

## Root cause

`BuildCompileOnly` → `PreferredResult(results)` returns null when Buildalyzer yields **zero**
results, and every caller treats null as "skip quietly":

- `SolutionSourceLoader.cs:1474` `BuildCompileOnly` — `PreferredResult` is null on an empty
  `IAnalyzerResults`.
- `SolutionSourceLoader.cs:309` `BuildChecked` — `if (built is null) return null;`
- `SolutionSourceLoader.cs:295` `BuildOrLoad` — returns null **without** incrementing `cacheHits`
  or `cacheMisses`, which is why the cache line under-counts and the drop is invisible.
- `SolutionSourceLoader.cs:546` the build loop — `if (info is not null)` adds to the bag; the `else`
  is missing. The adjacent `catch` at :551 *does* report (`MSBuild: skipping ... build failed`), so
  a thrown failure is visible while a null result is not.

Note the asymmetry: the single-project path (`BuildSingleProjectResults`, :451) turns the same null
into `throw new InvalidOperationException("Buildalyzer produced no build results for ...")`. Only
the solution path swallows it.

Confirmed not to be the cause: restore state (`dotnet build` of the project succeeds, and indexing
that csproj *directly* yields 11,394 symbols / 739 files), parallelism (`--parallelism 1` drops it
too), framework selection (`net10.0` is declared), and rules exclusion (no `projects.exclude` hit).
A solution containing **only** that project reports `Loaded 0 C# project(s)`, and the same solution
with the path rewritten absolute loads it fine — so the trigger is specifically the escaping
relative path, not the project.

## Fix

Two independent changes; the first is the important one.

1. **Never drop a project silently.** In the solution build loop, treat a null `info` the way a
   thrown exception is already treated — report it. Better: make it fatal by default, since a
   missing project corrupts dependents' binding exactly like `IsDegradedBuild` does (that case
   already throws `DegradedBuildException` rather than shipping a partial store). At minimum, count
   it so `Assembling workspace from N project(s)` can be compared against the solution's project
   count, and warn when they differ.

2. **Normalise slnx project paths before handing them to Buildalyzer.** `Path.GetFullPath` each
   `<Project Path>` relative to the solution directory. `DependencyGraph.ParseSlnx` (used by
   `--from`) resolves paths itself and is unaffected — this is only the
   `new AnalyzerManager(solutionPath, options)` path at `SolutionSourceLoader.cs:478`. Worth
   checking whether `.sln` with `..` paths has the same hole.

## Current workaround

`C:\Git\AngleSharp.ReadOnlyDom\scripts\rig-index.ps1` generates a normalised copy of the solution
with out-of-tree paths rewritten absolute, and indexes that.

## Resolution

`.slnx` project paths are now resolved against the original solution directory before Buildalyzer sees the
manifest. The disposable normalized manifest lives under the OS temp directory, while the original
`SolutionDir` and solution identity are restored at project-global precedence; read-only checkouts remain
untouched. A null Buildalyzer result now aborts indexing with the affected project path instead of silently
dropping it. A fresh-process MSBuild regression fixture proves the sibling project's symbols, a first-party
cross-project invocation, and a source inclusion that depends on the original `$(SolutionDir)`. The full
ship gate passed: 1,421 main tests, 83 shared integration tests (+1 known skip), all 17 independent classes,
and 33 live tests.
