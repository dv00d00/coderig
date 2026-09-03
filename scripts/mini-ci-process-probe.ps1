# Safe to dot-source: this file only defines the process-probe functions used by mini-ci.

function ConvertTo-RigProcessRow {
    param(
        [Parameter(Mandatory)]
        [object]$InputObject
    )

    if ($InputObject -is [string]) {
        if ($InputObject -notmatch '^\s*(?<pid>\d+)\s+(?<commandLine>.*)$') {
            return
        }

        return [pscustomobject]@{
            ProcessId  = [long]$Matches.pid
            CommandLine = $Matches.commandLine
        }
    }

    if ($null -eq $InputObject.PSObject.Properties['ProcessId'] -or
        $null -eq $InputObject.PSObject.Properties['CommandLine']) {
        return
    }

    $processId = 0L
    if (-not [long]::TryParse("$($InputObject.ProcessId)", [ref]$processId)) {
        return
    }

    return [pscustomobject]@{
        ProcessId  = $processId
        CommandLine = [string]$InputObject.CommandLine
    }
}

function Select-RigCliBinHolder {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$ProcessRows
    )

    foreach ($candidate in $ProcessRows) {
        $row = ConvertTo-RigProcessRow -InputObject $candidate
        if ($null -eq $row -or [string]::IsNullOrWhiteSpace($row.CommandLine)) {
            continue
        }

        # `ps` returns every process while the Windows CIM query is already name-filtered. Keep selection
        # identical by requiring dotnet as the executable before looking for the locally-built CLI argument.
        $isDotnet = $row.CommandLine -match '^\s*(?:dotnet(?:\.exe)?|[^\s"]*[\\/]dotnet(?:\.exe)?|"[^"]*[\\/]dotnet(?:\.exe)?")(?=\s|$)'
        if ($isDotnet -and $row.CommandLine -like '*Rig.Cli.dll*') {
            $row
        }
    }
}

function Get-RigProcessRows {
    $isWindowsPlatform = $PSVersionTable.Platform -eq 'Win32NT' -or $env:OS -eq 'Windows_NT'
    if ($isWindowsPlatform) {
        return @(
            Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
                ForEach-Object {
                    [pscustomobject]@{
                        ProcessId  = $_.ProcessId
                        CommandLine = $_.CommandLine
                    }
                }
        )
    }

    $rows = @(& ps -axo 'pid=,command=')
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect local processes with ps (exit $LASTEXITCODE)."
    }

    return @(
        $rows |
            ForEach-Object { ConvertTo-RigProcessRow -InputObject $_ }
    )
}
