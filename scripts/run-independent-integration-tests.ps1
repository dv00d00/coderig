param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$manifestPath = Join-Path $repoRoot "tests/Rig.IntegrationTests/MsBuildIntegrationSources.props"
$projectPath = Join-Path $repoRoot "tests/Rig.IndependentIntegrationTests/Rig.IndependentIntegrationTests.csproj"

[xml]$manifest = Get-Content $manifestPath
$manifestDirectory = Split-Path $manifestPath
$lanes = @("SharedIntegrationSource", "IndependentIntegrationSource", "LiveIntegrationSource", "ManualIntegrationSource")
$classified = foreach ($lane in $lanes) {
    foreach ($source in @($manifest.SelectNodes("//$lane"))) {
        $namespace = "$($source.Namespace)"
        $testClass = "$($source.TestClass)"
        if ([string]::IsNullOrWhiteSpace($namespace) -or [string]::IsNullOrWhiteSpace($testClass)) {
            throw "Every $lane must declare Namespace and TestClass metadata: $($source.Include)"
        }

        $sourcePath = "$($source.Include)".Replace('$(MSBuildThisFileDirectory)', $manifestDirectory + [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path $sourcePath -PathType Leaf)) {
            throw "$lane source does not exist: $sourcePath"
        }

        $sourceText = Get-Content $sourcePath -Raw
        $escapedNamespace = [Regex]::Escape($namespace)
        $escapedClass = [Regex]::Escape($testClass)
        if ($sourceText -notmatch "(?m)^namespace\s+$escapedNamespace;" -or
            $sourceText -notmatch "(?m)^(?:public|internal)\s+(?:(?:sealed|static|partial|abstract)\s+)*(?:class|record\s+class)\s+$escapedClass\b") {
            throw "$lane metadata does not name a declared class: $namespace.$testClass in $sourcePath"
        }

        $usesSharedFixture = $sourceText.Contains("ClassDataSource<AnalyzedPlaygrounds", [StringComparison]::Ordinal)
        if ($usesSharedFixture -ne ($lane -eq "SharedIntegrationSource")) {
            throw "$namespace.$testClass must be classified in the shared lane exactly when it consumes AnalyzedPlaygrounds."
        }

        [pscustomobject]@{
            Lane = $lane
            Include = "$($source.Include)"
            Link = "$($source.Link)"
            Identity = "$namespace.$testClass"
            Source = $source
        }
    }
}

foreach ($property in @("Include", "Link", "Identity")) {
    $duplicate = $classified | Group-Object -Property $property | Where-Object Count -gt 1 | Select-Object -First 1
    if ($null -ne $duplicate) {
        throw "Integration manifest classifies duplicate $property '$($duplicate.Name)'."
    }
}

$sources = @($classified | Where-Object Lane -eq "IndependentIntegrationSource" | ForEach-Object Source)
if ($sources.Count -eq 0) {
    throw "The integration manifest contains no IndependentIntegrationSource entries."
}

Push-Location $repoRoot
try {
    for ($index = 0; $index -lt $sources.Count; $index++) {
        $source = $sources[$index]
        $namespace = "$($source.Namespace)"
        $testClass = "$($source.TestClass)"
        $filter = "/*/$namespace/$testClass/*"
        Write-Host "Independent integration class $($index + 1)/$($sources.Count): $namespace.$testClass"

        dotnet test $projectPath `
            -c $Configuration `
            --no-build `
            --no-restore `
            -- `
            --maximum-parallel-tests 1 `
            --minimum-expected-tests 1 `
            --treenode-filter $filter
        if ($LASTEXITCODE -ne 0) {
            throw "Independent integration class $namespace.$testClass failed (exit $LASTEXITCODE)."
        }
    }
}
finally {
    Pop-Location
}
