# `mini-ci.ps1` calls Windows-only `Get-CimInstance` before every non-Windows build

**Status:** done 2026-09-03 · **Found:** 2026-09-03 while running the full gate for the first orchestrated bug batch
**Triage:** ready-for-agent
**Family:** developer workflow / release gate

## Defect

`pwsh scripts/mini-ci.ps1 -FullTests` exits before formatting or building on macOS because the new
locally-built-host lock check unconditionally calls:

```text
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"
```

`Get-CimInstance`/`Win32_Process` is a Windows-only mechanism. The failure is therefore in the release gate,
not in the product build:

```text
Get-CimInstance: scripts/mini-ci.ps1:27
The term 'Get-CimInstance' is not recognized ...
```

## Fix shape

- Keep the existing `Rig.Cli.dll serve` lock-holder guard on Windows.
- Add a non-Windows process/command-line probe (or a shared cross-platform helper) that detects the same
  locally-built host without requiring CIM.
- Preserve `-SkipToolInstall` semantics: it exempts the installed `rig` process check, but not a local
  `dotnet ... Rig.Cli.dll serve` process because the latter still locks build output.
- Do not silently disable the guard outside Windows.

## Acceptance

1. On non-Windows, `pwsh scripts/mini-ci.ps1 -SkipTests -SkipToolInstall` reaches formatting/build instead of
   failing on an unavailable cmdlet.
2. A locally-built `dotnet ... Rig.Cli.dll serve` process is still reported with its PID before the build.
3. Windows retains the current `Win32_Process` behavior.
4. The process-probe logic is unit-testable without starting or killing an unrelated user process.

## Verification

- Synthetic Windows and Unix process rows select only `dotnet … Rig.Cli.dll` hosts.
- On macOS, `pwsh scripts/mini-ci.ps1 -SkipTests -SkipToolInstall` completed format, Release build, and pack.
- Release build completed with 0 warnings and 0 errors; no process was killed.
