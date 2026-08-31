param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Install,
    [string]$RiderProfile = "",
    [string]$RiderHome = ""
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

function Resolve-RiderHome([string]$explicitHome) {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($explicitHome)) {
        $candidates += $explicitHome
    }
    if (-not [string]::IsNullOrWhiteSpace($env:RIDER_HOME)) {
        $candidates += $env:RIDER_HOME
    }
    if ($IsMacOS) {
        $candidates += Get-ChildItem (Join-Path $HOME "Applications") -Directory -Filter "Rider*.app" -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName "Contents" }
        $candidates += Get-ChildItem "/Applications" -Directory -Filter "Rider*.app" -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName "Contents" }
    } elseif ($IsWindows) {
        $programs = Join-Path $env:LOCALAPPDATA "Programs"
        $candidates += Get-ChildItem $programs -Directory -Filter "Rider*" -ErrorAction SilentlyContinue |
            ForEach-Object FullName
        $toolbox = Join-Path $env:LOCALAPPDATA "JetBrains/Toolbox/apps/Rider"
        if (Test-Path $toolbox) {
            $candidates += Get-ChildItem $toolbox -Recurse -File -Filter "product-info.json" -ErrorAction SilentlyContinue |
                ForEach-Object { Split-Path -Parent $_.FullName }
        }
    } else {
        $candidates += "/opt/rider"
        $candidates += Get-ChildItem (Join-Path $HOME ".local/share/JetBrains/Toolbox/apps/Rider") -Recurse -File -Filter "product-info.json" -ErrorAction SilentlyContinue |
            ForEach-Object { Split-Path -Parent $_.FullName }
    }

    $resolved = $candidates |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path (Join-Path $_ "lib")) } |
        Sort-Object { (Get-Item $_).LastWriteTimeUtc } -Descending |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($resolved)) {
        throw "Could not locate Rider. Pass -RiderHome <installation root> or set RIDER_HOME."
    }
    return $resolved
}

function Resolve-JbrBin([string]$riderRoot) {
    $mac = Join-Path $riderRoot "jbr/Contents/Home/bin"
    if (Test-Path $mac) { return $mac }
    $plain = Join-Path $riderRoot "jbr/bin"
    if (Test-Path $plain) { return $plain }
    throw "Rider JBR was not found under $riderRoot"
}

dotnet build $project -c $Configuration -m:1 /p:UseSharedCompilation=false --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Rider plugin build failed with exit code $LASTEXITCODE"
}

Remove-Item -Recurse -Force $stagingRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $pluginRoot "META-INF") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $pluginRoot "dotnet") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $pluginRoot "lib") | Out-Null
Copy-Item (Join-Path $projectRoot "plugin/META-INF/*") (Join-Path $pluginRoot "META-INF") -Recurse -Force
Copy-Item (Join-Path $projectRoot "bin/RiderBackendEffectSpike/$Configuration/CodeRig.Rider.dll") (Join-Path $pluginRoot "dotnet") -Force
Copy-Item (Join-Path $projectRoot "bin/RiderBackendEffectSpike/$Configuration/CodeRig.Rider.pdb") (Join-Path $pluginRoot "dotnet") -Force

$resolvedRiderHome = Resolve-RiderHome $RiderHome
$jbrBin = Resolve-JbrBin $resolvedRiderHome
$javac = Join-Path $jbrBin $(if ($IsWindows) { "javac.exe" } else { "javac" })
$jarTool = Join-Path $jbrBin $(if ($IsWindows) { "jar.exe" } else { "jar" })
if (-not (Test-Path $jarTool)) {
    $jarTool = (Get-Command jar -ErrorAction Stop).Source
}
$frontendRoot = Join-Path $projectRoot "frontend/src"
$frontendClasses = Join-Path $projectRoot "obj/frontend/classes"
Remove-Item -Recurse -Force (Split-Path -Parent $frontendClasses) -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $frontendClasses | Out-Null
$javaFiles = @(Get-ChildItem $frontendRoot -Recurse -File -Filter "*.java" | ForEach-Object FullName)
if ($javaFiles.Count -eq 0) {
    throw "No Rider frontend Java sources were found under $frontendRoot"
}
& $javac --release 17 -encoding UTF-8 -classpath (Join-Path $resolvedRiderHome "lib/*") -d $frontendClasses @javaFiles
if ($LASTEXITCODE -ne 0) {
    throw "Rider frontend build failed with exit code $LASTEXITCODE"
}
& $jarTool --create --file (Join-Path $pluginRoot "lib/coderig-rider-frontend.jar") -C $frontendClasses .
if ($LASTEXITCODE -ne 0) {
    throw "Rider frontend packaging failed with exit code $LASTEXITCODE"
}

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
