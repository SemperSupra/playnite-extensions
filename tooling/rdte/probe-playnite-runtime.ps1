[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDir,

    [int]$StartupTimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Find-ExactlyOneFile {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $matches = @(Get-ChildItem -Path $Root -Filter $Name -File -Recurse)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one '$Name' under extracted runtime; found $($matches.Count)."
    }
    return $matches[0].FullName
}

function Stop-Playnite {
    param(
        [Parameter(Mandatory = $true)][string]$DesktopExe,
        [Parameter(Mandatory = $true)][string]$UserData
    )

    $working = Split-Path -Parent $DesktopExe
    $stopper = Start-Process -FilePath $DesktopExe -WorkingDirectory $working -ArgumentList @(
        "--userdatadir", $UserData,
        "--shutdown"
    ) -PassThru
    $stopper.WaitForExit(15000) | Out-Null

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $running = @(Get-Process | Where-Object {
            $_.ProcessName -like "Playnite.DesktopApp*" -or
            $_.ProcessName -like "Playnite.FullscreenApp*"
        })
        if ($running.Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Playnite did not stop cleanly before timeout."
}

if ($env:OS -ne "Windows_NT") {
    throw "This probe requires Windows."
}

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schema_version -ne 1) {
    throw "Unsupported manifest schema '$($manifest.schema_version)'."
}

New-Item -Path $EvidenceDir -ItemType Directory -Force | Out-Null
$workRoot = Join-Path $env:RUNNER_TEMP ("playnite-runtime-" + [Guid]::NewGuid().ToString("N"))
$downloadDir = Join-Path $workRoot "download"
$runtimeDir = Join-Path $workRoot "runtime"
$userData = Join-Path $workRoot "userdata"
New-Item $downloadDir, $runtimeDir, $userData -ItemType Directory -Force | Out-Null

$receipt = [ordered]@{
    schema = "sempersupra-playnite-runtime-probe/v1"
    requested_runner = $env:RUNNER_NAME
    runner_os = $env:RUNNER_OS
    runner_arch = $env:RUNNER_ARCH
    image_os = $env:ImageOS
    image_version = $env:ImageVersion
    playnite = [ordered]@{
        requested_version = $manifest.playnite_version
        release_tag = $manifest.release_tag
        expected_sha256 = $manifest.portable_sha256
        observed_sha256 = $null
        desktop_file_version = $null
        toolbox_present = $false
        toolbox_file_version = $null
    }
    phases = [ordered]@{
        acquire = "NOT_RUN"
        extract = "NOT_RUN"
        toolbox = "NOT_RUN"
        initialize = "NOT_RUN"
        shutdown = "NOT_RUN"
    }
    result = "RUNNING"
}

$playniteProcess = $null
try {
    $archivePath = Join-Path $downloadDir "Playnite-$($manifest.playnite_version).7z"
    Invoke-WebRequest -Uri $manifest.portable_url -OutFile $archivePath -UseBasicParsing
    $hash = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $receipt.playnite.observed_sha256 = $hash
    if ($hash -ne $manifest.portable_sha256.ToLowerInvariant()) {
        throw "Portable archive SHA-256 mismatch."
    }
    $receipt.phases.acquire = "PASS"

    $sevenZipCommand = Get-Command 7z.exe -ErrorAction SilentlyContinue
    $sevenZipPath = if ($sevenZipCommand) { $sevenZipCommand.Source } else { $null }
    if (-not $sevenZipPath) {
        $candidate = Join-Path $env:ProgramFiles "7-Zip\7z.exe"
        if (Test-Path $candidate) { $sevenZipPath = $candidate }
    }
    if (-not $sevenZipPath) {
        throw "7z.exe is unavailable on the runner."
    }

    & $sevenZipPath x $archivePath "-o$runtimeDir" -y | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip extraction failed with exit code $LASTEXITCODE."
    }
    $receipt.phases.extract = "PASS"

    $desktopExe = Find-ExactlyOneFile -Root $runtimeDir -Name $manifest.desktop_exe
    $toolboxExe = Find-ExactlyOneFile -Root $runtimeDir -Name $manifest.toolbox_exe
    $receipt.playnite.desktop_file_version = (Get-Item $desktopExe).VersionInfo.FileVersion
    $receipt.playnite.toolbox_present = $true
    $receipt.playnite.toolbox_file_version = (Get-Item $toolboxExe).VersionInfo.FileVersion
    $receipt.phases.toolbox = "PASS"

    $working = Split-Path -Parent $desktopExe
    $playniteProcess = Start-Process -FilePath $desktopExe -WorkingDirectory $working -ArgumentList @(
        "--userdatadir", $userData,
        "--resetsettings",
        "--nolibupdate",
        "--hidesplashscreen",
        "--forcedefaulttheme",
        "--forcesoftrender"
    ) -PassThru

    $configPath = Join-Path $userData "config.json"
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while (-not (Test-Path $configPath) -and [DateTime]::UtcNow -lt $deadline) {
        if ($playniteProcess.HasExited) {
            throw "Playnite exited before creating config.json; exit code $($playniteProcess.ExitCode)."
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path $configPath)) {
        throw "Playnite did not create config.json before timeout."
    }
    $receipt.phases.initialize = "PASS"

    Stop-Playnite -DesktopExe $desktopExe -UserData $userData
    $receipt.phases.shutdown = "PASS"
    $receipt.result = "PASS"
}
catch {
    $receipt.result = "FAIL"
    $receipt.error = $_.Exception.Message
    throw
}
finally {
    if ($playniteProcess -and -not $playniteProcess.HasExited) {
        try { Stop-Process -Id $playniteProcess.Id -Force -ErrorAction SilentlyContinue } catch {}
    }
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -like "Playnite.DesktopApp*" -or $_.ProcessName -like "Playnite.FullscreenApp*" } |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $receipt.finished_utc = [DateTime]::UtcNow.ToString("o")
    $receipt | ConvertTo-Json -Depth 10 |
        Set-Content -Path (Join-Path $EvidenceDir "receipt.json") -Encoding UTF8
}
