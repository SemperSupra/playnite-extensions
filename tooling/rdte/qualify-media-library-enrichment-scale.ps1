[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$WorkRoot,
    [Parameter(Mandatory = $true)][string]$EvidenceDir,
    [int]$StartupTimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$pluginId = "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377"
$pluginName = "Media Library Enrichment"
$seederId = "SemperSupraRdteSeeder_6d06cf1b-d1e4-4caa-b6c3-cc6026953135"
$seederDataId = "6d06cf1b-d1e4-4caa-b6c3-cc6026953135"
$seederName = "RDTE Fixture Seeder"

$userData = Join-Path $WorkRoot "userdata"
$runtimeDir = Join-Path $WorkRoot "runtime"
$desktopExe = @(Get-ChildItem $runtimeDir -Filter "Playnite.DesktopApp.exe" -File -Recurse)
if ($desktopExe.Count -ne 1) { throw "Expected exactly one Playnite.DesktopApp.exe." }
$desktopExe = $desktopExe[0].FullName
$logPath = Join-Path $userData "playnite.log"
$queuePath = Join-Path $userData "extinstalls.json"

$pluginPackage = @(Get-ChildItem $EvidenceDir -Filter "MediaLibraryEnrichment-*.pext" -File)
if ($pluginPackage.Count -ne 1) { throw "Expected exactly one MLE package." }
$pluginPackage = $pluginPackage[0].FullName

$seederPackage = @(Get-ChildItem (Join-Path $WorkRoot "fixture-seeder-package") -Filter "*.pext" -File)
if ($seederPackage.Count -ne 1) { throw "Expected exactly one fixture-seeder package." }
$seederPackage = $seederPackage[0].FullName

function Start-Playnite {
    Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path -Parent $desktopExe) -ArgumentList @(
        "--userdatadir", $userData,
        "--nolibupdate",
        "--hidesplashscreen",
        "--forcedefaulttheme",
        "--forcesoftrender"
    ) -PassThru
}

function Stop-Playnite {
    $stopper = Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path -Parent $desktopExe) -ArgumentList @(
        "--userdatadir", $userData,
        "--shutdown"
    ) -PassThru
    $stopper.WaitForExit(30000) | Out-Null

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
            $_.ProcessName -like "Playnite.DesktopApp*" -or
            $_.ProcessName -like "Playnite.FullscreenApp*"
        })
        if ($running.Count -eq 0) {
            return
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Playnite did not stop cleanly before timeout."
}

function Wait-ForText {
    param([string]$Path,[string]$Text,$Process)
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) { throw "Playnite exited while waiting for '$Text'." }
        if (Test-Path $Path) {
            $content = Get-Content $Path -Raw
            if ($null -ne $content -and $content.Contains($Text)) { return }
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for '$Text'."
}

function Wait-ForFile {
    param([string]$Path,$Process)
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) { throw "Playnite exited while waiting for '$Path'." }
        if (Test-Path $Path) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for '$Path'."
}

function Find-InstalledExtension {
    param([string]$ExpectedId)
    $root = Join-Path $userData "Extensions"
    if (-not (Test-Path $root)) { return $null }
    foreach ($manifest in @(Get-ChildItem $root -Filter "extension.yaml" -File -Recurse)) {
        $yaml = Get-Content $manifest.FullName -Raw
        $match = [regex]::Match($yaml, "(?m)^Id:\s*(?<value>\S+)\s*$")
        if ($match.Success -and $match.Groups["value"].Value.Trim() -eq $ExpectedId) {
            return $manifest.Directory.FullName
        }
    }
    return $null
}

function Queue-Install {
    param([string]$PackagePath)
    $queue = @([ordered]@{ InstallType = 0; Path = $PackagePath })
    ConvertTo-Json -InputObject $queue -Depth 4 |
        Set-Content -Path $queuePath -Encoding UTF8
}

function Queue-Uninstall {
    param([string]$InstalledDir)
    $queue = @([ordered]@{ InstallType = 1; Path = $InstalledDir })
    ConvertTo-Json -InputObject $queue -Depth 4 |
        Set-Content -Path $queuePath -Encoding UTF8
}

function Wait-ForQueueConsumed {
    param($Process)
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ((Test-Path $queuePath) -and [DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) { throw "Playnite exited before consuming extension queue." }
        Start-Sleep -Milliseconds 250
    }
    if (Test-Path $queuePath) { throw "Playnite did not consume extension queue." }
}

function Native-InstallAndRun {
    param([string]$PackagePath,[string]$ExpectedId,[string]$ExpectedName,[string]$ExpectedVersion,[string]$ReadyFile)
    Queue-Install $PackagePath
    if (Test-Path $logPath) { Remove-Item $logPath -Force }
    $process = Start-Playnite
    Wait-ForQueueConsumed $process
    Wait-ForText $logPath "Loaded plugin: $ExpectedName, version $ExpectedVersion" $process
    $installed = Find-InstalledExtension $ExpectedId
    if (-not $installed) { throw "'$ExpectedName' did not install." }
    if ($ReadyFile) { Wait-ForFile $ReadyFile $process }
    [pscustomobject]@{ Process = $process; InstalledDir = $installed }
}

function Native-Uninstall {
    param([string]$InstalledDir)
    Queue-Uninstall $InstalledDir
    $process = Start-Playnite
    Wait-ForQueueConsumed $process
    Stop-Playnite
}

$receipt = [ordered]@{
    schema = "sempersupra-media-library-enrichment-scale-rdte/v1"
    source_sha = if ($env:RDTE_SOURCE_SHA) { $env:RDTE_SOURCE_SHA } else { $env:GITHUB_SHA }
    result = "RUNNING"
}

function Wait-ForPlayniteQuiescence {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
            $_.ProcessName -like "Playnite.DesktopApp*" -or
            $_.ProcessName -like "Playnite.FullscreenApp*" -or
            $_.ProcessName -like "Playnite.BrowserProcess*"
        })
        if ($running.Count -eq 0) {
            return
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Playnite runtime was not quiescent before scale qualification."
}

$productProcess = $null
try {
    Wait-ForPlayniteQuiescence
    $productReceipt = Get-Content (Join-Path $EvidenceDir "receipt.json") -Raw | ConvertFrom-Json
    $productVersion = $productReceipt.version

    $source = Get-Content "./plugins/media-library-enrichment/src/MediaLibraryEnrichment/MediaLibraryEnrichmentPlugin.cs" -Raw
    foreach ($token in @("ItemUpdated +=", "ItemCollectionChanged +=", "OnLibraryUpdated(")) {
        if ($source.Contains($token)) {
            throw "Current product unexpectedly contains event-driven reconciliation token '$token'."
        }
    }
    $receipt.event_driven_reconciliation = "ABSENT"

    $seederData = Join-Path $userData "ExtensionsData\$seederDataId"
    New-Item $seederData -ItemType Directory -Force | Out-Null
    $profilePath = Join-Path $seederData "fixture-profile.txt"
    $scaleReceiptPath = Join-Path $seederData "scale-update-receipt.json"
    Set-Content -Path $profilePath -Value "mle-scale-update-v1" -Encoding UTF8
    if (Test-Path $scaleReceiptPath) { Remove-Item $scaleReceiptPath -Force }

    $seederInstall = Native-InstallAndRun $seederPackage $seederId $seederName "1.0" $scaleReceiptPath
    $scale = Get-Content $scaleReceiptPath -Raw | ConvertFrom-Json
    if ($scale.schema -ne "sempersupra-playnite-scale-update-fixture/v1" -or
        $scale.result -ne "PASS" -or
        $scale.scale_game_count -ne 10000 -or
        $scale.total_library_games -lt 10005) {
        throw "Scale library fixture did not reach the expected state."
    }
    Copy-Item $scaleReceiptPath (Join-Path $EvidenceDir "scale-library-seeded.json") -Force
    Stop-Playnite
    Native-Uninstall $seederInstall.InstalledDir

    $pluginData = Join-Path $userData "ExtensionsData\$pluginId"
    New-Item $pluginData -ItemType Directory -Force | Out-Null
    [ordered]@{ Mode = "observe" } |
        ConvertTo-Json |
        Set-Content -Path (Join-Path $pluginData "settings.json") -Encoding UTF8

    $observationPath = Join-Path $pluginData "observation-receipt.json"
    if (Test-Path $observationPath) { Remove-Item $observationPath -Force }
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $productInstall = Native-InstallAndRun $pluginPackage $pluginId $pluginName $productVersion $observationPath
    $productProcess = $productInstall.Process
    $watch.Stop()

    $observation = Get-Content $observationPath -Raw | ConvertFrom-Json
    if ($observation.CandidateCount -ne 4) {
        throw "Large irrelevant library changed the admitted media candidate count."
    }
    $scaleCandidates = @($observation.Candidates | Where-Object { $_.Name -like "RDTE Scale Game *" })
    if ($scaleCandidates.Count -ne 0) {
        throw "Scale-library controls leaked into media admission."
    }

    $receipt.scale_game_count = $scale.scale_game_count
    $receipt.total_library_games = $scale.total_library_games
    $receipt.candidate_count = $observation.CandidateCount
    $receipt.startup_observation_ms = $watch.ElapsedMilliseconds
    $receipt.result = "PASS"
    Copy-Item $observationPath (Join-Path $EvidenceDir "scale-library-observation.json") -Force

    Stop-Playnite
    $productProcess = $null
    Native-Uninstall $productInstall.InstalledDir
}
catch {
    $receipt.result = "FAIL"
    $receipt.error = $_.Exception.Message
    throw
}
finally {
    if ($productProcess -and -not $productProcess.HasExited) {
        Stop-Process -Id $productProcess.Id -Force -ErrorAction SilentlyContinue
    }
    $receipt.finished_utc = [DateTime]::UtcNow.ToString("o")
    $receipt |
        ConvertTo-Json -Depth 8 |
        Set-Content -Path (Join-Path $EvidenceDir "scale-qualification-receipt.json") -Encoding UTF8
}
