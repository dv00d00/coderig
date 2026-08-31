param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Install,
    [string]$RiderProfile = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $repoRoot "experiments/RiderBackendEffectSpike"
$project = Join-Path $projectRoot "RiderBackendEffectSpike.csproj"
$manifestPath = Join-Path $projectRoot "plugin/META-INF/plugin.xml"
[xml]$manifest = Get-Content $manifestPath
$pluginVersion = $manifest.'idea-plugin'.version
if ([string]::IsNullOrWhiteSpace($pluginVersion)) {
    throw "Plugin manifest does not declare a version"
}
$artifactRoot = Join-Path $repoRoot "artifacts/rider"
$stagingRoot = Join-Path $artifactRoot "staging"
$pluginRoot = Join-Path $stagingRoot "CodeRig"
$artifact = Join-Path $artifactRoot "CodeRig-$pluginVersion.zip"

dotnet build $project -c $Configuration -m:1 /p:UseSharedCompilation=false --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Rider plugin build failed with exit code $LASTEXITCODE"
}

Remove-Item -Recurse -Force $stagingRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $pluginRoot "META-INF") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $pluginRoot "dotnet") | Out-Null
Copy-Item (Join-Path $projectRoot "plugin/META-INF/*") (Join-Path $pluginRoot "META-INF") -Recurse -Force
Copy-Item (Join-Path $projectRoot "bin/RiderBackendEffectSpike/$Configuration/CodeRig.Rider.dll") (Join-Path $pluginRoot "dotnet") -Force
Copy-Item (Join-Path $projectRoot "bin/RiderBackendEffectSpike/$Configuration/CodeRig.Rider.pdb") (Join-Path $pluginRoot "dotnet") -Force

Remove-Item -Force $artifact -ErrorAction SilentlyContinue
Compress-Archive -Path $pluginRoot -DestinationPath $artifact -CompressionLevel Optimal

if ($Install) {
    if ([string]::IsNullOrWhiteSpace($RiderProfile)) {
        if ($IsMacOS) {
            $RiderProfile = Join-Path $HOME "Library/Application Support/JetBrains/Rider2026.2"
        } elseif ($IsWindows) {
            $RiderProfile = Join-Path $env:APPDATA "JetBrains/Rider2026.2"
        } else {
            $RiderProfile = Join-Path $HOME ".config/JetBrains/Rider2026.2"
        }
    }

    $pluginsRoot = Join-Path $RiderProfile "plugins"
    $installRoot = Join-Path $pluginsRoot "CodeRig"
    New-Item -ItemType Directory -Force $pluginsRoot | Out-Null
    # A running Rider holds CodeRig.Rider.dll open, so the delete FAILS. Swallowing that failure used to be
    # silent and fatal: `Copy-Item <dir> <existing dir>` NESTS, producing plugins/CodeRig/CodeRig/META-INF,
    # which Rider does not recognise as a plugin at all — it just stops loading, with nothing in any log.
    # So: delete loudly, verify the directory is gone, and copy the CONTENTS into a freshly created root.
    Remove-Item -Recurse -Force $installRoot -ErrorAction SilentlyContinue
    if (Test-Path $installRoot) {
        throw "Could not remove $installRoot (Rider is probably running and holding the plugin DLL). Close Rider and re-run."
    }
    New-Item -ItemType Directory -Force $installRoot | Out-Null
    Copy-Item (Join-Path $pluginRoot "*") $installRoot -Recurse -Force
    Write-Host "Installed CodeRig Rider plugin to $installRoot"
    Write-Host "Restart Rider to load it."
}

Remove-Item -Recurse -Force $stagingRoot
Write-Host "Built $artifact"
