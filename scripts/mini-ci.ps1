param(
    [string]$Configuration = "Release",
    [string]$ToolVersion = "",
    [switch]$SkipTests,
    [switch]$FullTests,
    [switch]$SkipToolInstall
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "mini-ci-process-probe.ps1")

# FAIL FAST on either lock a running rig holds. Two DIFFERENT processes, two different failures, and the
# second one is the trap: checking only the first is what cost a full build on 2026-09-03.
#
#   1. The installed tool (`rig serve`, process name `rig`) holds ~/.dotnet/tools/.store/rig/<version>, so the
#      INSTALL step cannot swap the binary. That step is the LAST thing this script does, so without a check
#      the lock costs a full build plus every test lane — ~10 minutes with -FullTests — before throwing. The
#      uninstall is deliberately swallowed (Continue + *> $null) so it does not even fail at the first
#      opportunity. -SkipToolInstall never publishes, so it is exempt from this one by construction.
#
#   2. A LOCALLY-BUILT host (`dotnet src/Rig.Cli/bin/Release/net10.0/Rig.Cli.dll serve`, process name
#      `dotnet`) holds bin/Release/*.dll, so the BUILD fails with MSB3026 "being used by another process" —
#      much earlier, and reading like a compile error rather than a lock. -SkipToolInstall does NOT exempt
#      this: the build still runs. CLAUDE.md recommends exactly this host as the way to see wwwroot edits
#      without repacking, so it is the likelier of the two in practice — and the name check above cannot see
#      it, because the process is `dotnet`.
$binHolders = @(Select-RigCliBinHolder -ProcessRows @(Get-RigProcessRows))
if ($binHolders.Count -gt 0) {
    throw ("A locally-built rig is running (PID " + (($binHolders | ForEach-Object { $_.ProcessId }) -join ", ") +
        ") and holds src/Rig.Cli/bin/$Configuration/**/*.dll, so the build below would fail with MSB3026. " +
        "Stop it and re-run: Stop-Process -Id " + (($binHolders | ForEach-Object { $_.ProcessId }) -join ", ") + " -Force")
}

if (-not $SkipToolInstall) {
    $holding = @(Get-Process -Name rig -ErrorAction SilentlyContinue)
    if ($holding.Count -gt 0) {
        throw ("rig is already running (PID " + ($holding.Id -join ", ") + ") and holds the global tool store, " +
            "so the install step at the end of this script would fail. Stop it and re-run, or pass " +
            "-SkipToolInstall to build and test without publishing the tool.")
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$solution = Join-Path $repoRoot "RuntimeIntelligenceGraph.slnx"
$toolProject = Join-Path $repoRoot "src/Rig.Cli/Rig.Cli.csproj"
$mainTestProject = Join-Path $repoRoot "tests/Rig.Tests/Rig.Tests.csproj"
$integrationTestProject = Join-Path $repoRoot "tests/Rig.IntegrationTests/Rig.IntegrationTests.csproj"
$independentIntegrationTestScript = Join-Path $repoRoot "scripts/run-independent-integration-tests.ps1"
$liveIntegrationTestProject = Join-Path $repoRoot "tests/Rig.LiveIntegrationTests/Rig.LiveIntegrationTests.csproj"
$packageOutput = Join-Path $repoRoot ".rig-nupkg"

if ([string]::IsNullOrWhiteSpace($ToolVersion)) {
    [xml]$toolProjectXml = Get-Content $toolProject
    $baseVersion = $toolProjectXml.Project.PropertyGroup.Version |
        Select-Object -First 1
    $stamp = Get-Date -Format "yyyyMMddHHmmss"
    $ToolVersion = "$baseVersion-ci.$stamp"
}

New-Item -ItemType Directory -Force -Path $packageOutput | Out-Null

Push-Location $repoRoot
try {
    dotnet csharpier format .
    if ($LASTEXITCODE -ne 0) { throw "csharpier format failed (exit $LASTEXITCODE)." }
    
    dotnet build $solution -c $Configuration 
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE) - not testing/packing." }

    if (-not $SkipTests) {
        # The local release gate is the parallel, MSBuild-free suite. It is intentionally the default:
        # synthetic indexing and resident/live integration belong to the explicit full correctness gate.
        dotnet test $mainTestProject -c $Configuration --no-build --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Main tests failed (exit $LASTEXITCODE) - not packing/installing." }

        if ($FullTests) {
            # The shared integration lane builds its AnalyzedPlaygrounds fixture once. Independent
            # correctness classes retain fresh-process isolation; resident/live tests own the last host.
            dotnet test $integrationTestProject -c $Configuration --no-build --no-restore -- --maximum-parallel-tests 1 --minimum-expected-tests 1
            if ($LASTEXITCODE -ne 0) { throw "Shared integration tests failed (exit $LASTEXITCODE) - not packing/installing." }

            & $independentIntegrationTestScript -Configuration $Configuration

            dotnet test $liveIntegrationTestProject -c $Configuration --no-build --no-restore -- --maximum-parallel-tests 1 --minimum-expected-tests 1
            if ($LASTEXITCODE -ne 0) { throw "Live integration tests failed (exit $LASTEXITCODE) - not packing/installing." }
        }
    }
    
    dotnet pack $toolProject `
        -c $Configuration `
        -o $packageOutput `
        /p:PackageVersion=$ToolVersion `
        /p:Version=$ToolVersion
    if ($LASTEXITCODE -ne 0) { throw "Pack failed (exit $LASTEXITCODE) - not installing." }

    if (-not $SkipToolInstall) {
        
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        dotnet tool uninstall --global rig *> $null
        $ErrorActionPreference = $previousErrorActionPreference

        # Keep BOTH pins. `rig` is a TAKEN package id on nuget.org (an unrelated dev launcher whose
        # 1.5.0 outranks our 0.1.1-ci.*), so any unpinned install/update resolves the stranger's
        # package — an ad-hoc `dotnet tool update -g rig --prerelease` did exactly that (2026-07-15).
        dotnet tool install --global rig `
            --add-source $packageOutput `
            --version $ToolVersion
        if ($LASTEXITCODE -ne 0) {
            throw "Global tool install failed (exit $LASTEXITCODE). A running rig process (e.g. 'rig web') can lock the tool store - stop it and re-run."
        }
        # Native failure reports via exit code (same trap as above), and a locked/failed swap can leave a
        # STALE rig on PATH while everything above was green — verify the binary actually is this build.
        $installedVersion = rig --version
        if ($LASTEXITCODE -ne 0 -or -not "$installedVersion".StartsWith($ToolVersion)) {
            throw "Installed rig reports '$installedVersion', expected $ToolVersion - global tool did not update."
        }
        $installedVersion
    }
}
finally {
    Pop-Location
}
