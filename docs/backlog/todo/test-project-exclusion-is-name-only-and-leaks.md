# `--exclude-tests` matches on project NAME only, so test projects under `tests/` get indexed

**Status:** todo · **Priority: LOW-MEDIUM** (wasted index time and inflated counts; it became visible because a
leaked test project dominated a compile-health calibration, contributing 24,309 of 24,545 error diagnostics) ·
**Found:** 2026-08-21, calibrating the compile-health disclosure · **Family:** indexing / scoping

## The gap

`SolutionSourceLoader.IsTestProjectPath` (`:871`) decides purely from the project's file NAME:

```csharp
var name = Path.GetFileNameWithoutExtension(projectPath);
return name.EndsWith("Tests", ...) || name.EndsWith("UnitTests", ...)
    || name.EndsWith("IntegrationTests", ...) || name.Contains(".Tests.", ...);
```

The parameter is named `projectPath` and the full path IS available, but only the leaf name is consulted. So a
project whose name does not end in `Tests` is indexed even when it sits squarely under a `tests/` directory.

Observed on MedDBase: **`MedDBase.QA.Automation.Setup`**, at `tests/ui/MedDBase.QA.Automation/…`, is indexed
under `excludeTests: true`. It is a test-harness project by location and by purpose; its name simply does not
match the convention.

## Why it is worth fixing rather than tolerating

- **It skews any per-file or per-project metric.** In the compile-health calibration it produced 24,309 of the
  healthy tree's 24,545 error diagnostics — a single leaked project nearly monopolising the signal, which is
  exactly the sort of thing that trains a reader to ignore a disclosure.
- It costs design-time build and extraction time on code nobody queries.
- `--from`'s closure logic already drops test projects by name too (`IndexCommands.BuildEntryClosureAsync`), so
  the same blind spot exists in two places with one convention behind it.

## Fix

Consult the PATH as well as the name: a project under a path segment of `tests`/`test` (case-insensitive) is a
test project, in addition to the existing name suffixes. Keep the name rules — they catch test projects that do
not live under a `tests/` root.

Worth checking whether MSBuild already answers this better: an `IsTestProject` property, a
`Microsoft.NET.Test.Sdk` package reference, or a `TestProject` capability would be evidence rather than
convention. rig already has the evaluated project in hand (Buildalyzer), so this may be a property read rather
than a heuristic — which would be the honest fix, with the name/path heuristic as the fallback.

## Acceptance

1. `MedDBase.QA.Automation.Setup` is excluded under `excludeTests: true`.
2. A test pinning both arms: a `tests/`-rooted project with a non-matching name is excluded; a production
   project that merely has "test" somewhere in its path (e.g. `src/TestDataBuilder/`) is NOT.
3. Indexed project count on MedDBase drops by exactly the leaked projects, named in the run output.

## Related

Surfaced by [live-index-serves-confident-answers-from-a-broken-compilation](../done/live-index-serves-confident-answers-from-a-broken-compilation-shipped.md)'s
calibration: the disclosure's healthy-tree noise floor is partly this leak rather than real compile breakage.
