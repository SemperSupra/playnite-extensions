[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WorkRoot,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDir,

    [int]$StartupTimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$pluginId = "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377"
$pluginName = "Media Library Enrichment"
$pluginRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "plugins\media-library-enrichment"
$userData = Join-Path $WorkRoot "userdata"
$runtimeDir = Join-Path $WorkRoot "runtime"
New-Item -Path $EvidenceDir -ItemType Directory -Force | Out-Null

function Find-ExactlyOneFile {
    param([string]$Root, [string]$Name)
    $items = @(Get-ChildItem -Path $Root -Filter $Name -File -Recurse)
    if ($items.Count -ne 1) {
        throw "Expected exactly one '$Name' under '$Root', found $($items.Count)."
    }
    return $items[0].FullName
}

function Start-Playnite {
    param([string]$DesktopExe, [string]$UserData)
    Start-Process -FilePath $DesktopExe -WorkingDirectory (Split-Path -Parent $DesktopExe) -ArgumentList @(
        "--userdatadir", $UserData,
        "--nolibupdate",
        "--hidesplashscreen",
        "--forcedefaulttheme",
        "--forcesoftrender"
    ) -PassThru
}

function Stop-Playnite {
    param([string]$DesktopExe, [string]$UserData)

    $stopper = Start-Process -FilePath $DesktopExe -WorkingDirectory (Split-Path -Parent $DesktopExe) -ArgumentList @(
        "--userdatadir", $UserData,
        "--shutdown"
    ) -PassThru
    $stopper.WaitForExit(30000) | Out-Null

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
            $_.ProcessName -like "Playnite.DesktopApp*" -or
            $_.ProcessName -like "Playnite.FullscreenApp*"
        })
        if ($running.Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    }

    throw "Playnite did not stop cleanly before timeout."
}

function Wait-ForText {
    param([string]$Path, [string]$Text, $Process, [int]$TimeoutSeconds)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process -and $Process.HasExited) {
            throw "Playnite exited while waiting for '$Text'. Exit code: $($Process.ExitCode)"
        }

        if (Test-Path $Path) {
            $content = Get-Content $Path -Raw
            if ($content.Contains($Text)) { return }
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for '$Text' in '$Path'."
}

function Wait-ForFile {
    param([string]$Path, $Process, [int]$TimeoutSeconds)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process -and $Process.HasExited) {
            throw "Playnite exited while waiting for '$Path'. Exit code: $($Process.ExitCode)"
        }
        if (Test-Path $Path) { return }
        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for '$Path'."
}

function Wait-ForAbsent {
    param([string]$Path, [int]$TimeoutSeconds)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not (Test-Path $Path)) { return }
        Start-Sleep -Milliseconds 250
    }

    throw "Path still exists after timeout: $Path"
}

function Read-PextManifest {
    param([string]$Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.GetEntry("extension.yaml")
        if (-not $entry) { throw "Package lacks extension.yaml." }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { $yaml = $reader.ReadToEnd() }
        finally { $reader.Dispose() }

        $entries = @($zip.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    }
    finally {
        $zip.Dispose()
    }

    $id = [regex]::Match($yaml, "(?m)^Id:\s*(?<value>\S+)\s*$")
    $version = [regex]::Match($yaml, "(?m)^Version:\s*(?<value>\S+)\s*$")
    $name = [regex]::Match($yaml, "(?m)^Name:\s*(?<value>.+?)\s*$")
    if (-not $id.Success -or -not $version.Success -or -not $name.Success) {
        throw "Unable to resolve package identity from extension.yaml."
    }

    [pscustomobject]@{
        Id = $id.Groups["value"].Value.Trim()
        Version = $version.Groups["value"].Value.Trim()
        Name = $name.Groups["value"].Value.Trim()
        Entries = $entries
    }
}

function Assert-ObservationReceipt {
    param([string]$Path)

    $observation = Get-Content $Path -Raw | ConvertFrom-Json
    if ($observation.Schema -ne "sempersupra-media-library-enrichment-observation/v1") {
        throw "Unexpected Media Library Enrichment observation schema."
    }
    if ($observation.FixtureContract -ne "media-baseline-v1") {
        throw "Observation receipt is not bound to media-baseline-v1."
    }
    if ($observation.CandidateCount -ne 3 -or @($observation.Candidates).Count -ne 3) {
        throw "Expected exactly three Humble missing-cover candidates."
    }

    $expected = @{
        "RDTE Humble Ebook" = "book"
        "RDTE Humble Comic" = "comic"
        "RDTE Humble Soundtrack" = "audio"
    }

    foreach ($candidate in @($observation.Candidates)) {
        if (-not $expected.ContainsKey($candidate.Name)) {
            throw "Unexpected observation candidate '$($candidate.Name)'."
        }
        if ($candidate.Source -ne "Humble Bundle RDTE") {
            throw "Candidate '$($candidate.Name)' has unexpected source '$($candidate.Source)'."
        }
        if (-not $candidate.CoverMissing) {
            throw "Candidate '$($candidate.Name)' does not have the expected missing-cover state."
        }
        if ($candidate.Kind -ne $expected[$candidate.Name]) {
            throw "Candidate '$($candidate.Name)' kind '$($candidate.Kind)' does not match '$($expected[$candidate.Name])'."
        }
    }

    if (@($observation.Candidates | Where-Object Name -eq "RDTE Manual Game").Count -ne 0) {
        throw "Manual control fixture was incorrectly admitted as a Humble media candidate."
    }

    return $observation
}

$receipt = [ordered]@{
    schema = "sempersupra-media-library-enrichment-rdte/v1"
    source_sha = if ($env:RDTE_SOURCE_SHA) { $env:RDTE_SOURCE_SHA } else { $env:GITHUB_SHA }
    trigger_sha = if ($env:RDTE_TRIGGER_SHA) { $env:RDTE_TRIGGER_SHA } else { $env:GITHUB_SHA }
    plugin_id = $pluginId
    plugin_name = $pluginName
    phases = [ordered]@{
        unit_tests = "NOT_RUN"
        build = "NOT_RUN"
        stage = "NOT_RUN"
        toolbox_pack = "NOT_RUN"
        native_install = "NOT_RUN"
        observation_oracle = "NOT_RUN"
        restart_oracle = "NOT_RUN"
        native_uninstall = "NOT_RUN"
        data_preservation = "NOT_RUN"
    }
    result = "RUNNING"
}

$playniteProcess = $null
try {
    if ($env:OS -ne "Windows_NT") {
        throw "Media Library Enrichment native qualification requires Windows."
    }

    $desktopExe = Find-ExactlyOneFile -Root $runtimeDir -Name "Playnite.DesktopApp.exe"
    $toolboxExe = Find-ExactlyOneFile -Root $runtimeDir -Name "Toolbox.exe"
    $logPath = Join-Path $userData "playnite.log"

    Push-Location $pluginRoot
    try {
        & dotnet test ".\tests\MediaLibraryEnrichment.Core.Tests\MediaLibraryEnrichment.Core.Tests.csproj" -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "Media Library Enrichment unit tests failed." }
        $receipt.phases.unit_tests = "PASS"

        & dotnet build ".\src\MediaLibraryEnrichment\MediaLibraryEnrichment.csproj" -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "Media Library Enrichment plugin build failed." }
        $receipt.phases.build = "PASS"
    }
    finally {
        Pop-Location
    }

    $buildOutput = Join-Path $pluginRoot "src\MediaLibraryEnrichment\bin\Release\net462"
    $stageDir = Join-Path $WorkRoot "media-library-enrichment-stage"
    $packageDir = Join-Path $WorkRoot "media-library-enrichment-package"
    New-Item $stageDir, $packageDir -ItemType Directory -Force | Out-Null

    $payload = @(
        @{ Name = "extension.yaml"; Source = (Join-Path $pluginRoot "extension.yaml") },
        @{ Name = "MediaLibraryEnrichment.dll"; Source = (Join-Path $buildOutput "MediaLibraryEnrichment.dll") },
        @{ Name = "MediaLibraryEnrichment.Core.dll"; Source = (Join-Path $buildOutput "MediaLibraryEnrichment.Core.dll") }
    )

    foreach ($item in $payload) {
        if (-not (Test-Path $item.Source -PathType Leaf)) {
            throw "Required release payload file is missing: $($item.Source)"
        }
        Copy-Item $item.Source (Join-Path $stageDir $item.Name) -Force
    }

    $normalizedStageTimestampUtc = [DateTime]::new(2000, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)
    foreach ($file in @(Get-ChildItem $stageDir -File)) {
        $file.LastWriteTimeUtc = $normalizedStageTimestampUtc
    }
    $receipt.normalized_stage_timestamp_utc = $normalizedStageTimestampUtc.ToString("o")

    $expectedNames = @("MediaLibraryEnrichment.Core.dll", "MediaLibraryEnrichment.dll", "extension.yaml")
    $stagedNames = @(Get-ChildItem $stageDir -File | ForEach-Object Name | Sort-Object)
    if (@(Compare-Object $expectedNames $stagedNames).Count -ne 0) {
        throw "Media Library Enrichment staged payload differs from the approved three-file allowlist."
    }

    $receipt.staged_files = @(
        Get-ChildItem $stageDir -File | Sort-Object Name | ForEach-Object {
            [ordered]@{
                name = $_.Name
                sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                last_write_time_utc = $_.LastWriteTimeUtc.ToString("o")
            }
        }
    )
    $receipt.phases.stage = "PASS"

    & $toolboxExe pack $stageDir $packageDir
    if ($LASTEXITCODE -ne 0) { throw "Playnite Toolbox pack failed for Media Library Enrichment." }

    $packages = @(Get-ChildItem $packageDir -Filter "*.pext" -File)
    if ($packages.Count -ne 1) {
        throw "Expected one Media Library Enrichment .pext, found $($packages.Count)."
    }

    $packagePath = $packages[0].FullName
    $package = Read-PextManifest -Path $packagePath
    if ($package.Id -ne $pluginId -or $package.Name -ne $pluginName) {
        throw "Media Library Enrichment package identity mismatch."
    }
    if (@(Compare-Object $expectedNames $package.Entries).Count -ne 0) {
        throw "Media Library Enrichment .pext contains an unexpected payload."
    }

    $receipt.version = $package.Version
    $receipt.package_sha256 = (Get-FileHash $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $receipt.package_entries = $package.Entries
    Copy-Item $packagePath (Join-Path $EvidenceDir "MediaLibraryEnrichment-$($package.Version).pext") -Force
    $receipt.phases.toolbox_pack = "PASS"

    $queuePath = Join-Path $userData "extinstalls.json"
    $installQueue = @([ordered]@{ InstallType = 0; Path = $packagePath })
    ConvertTo-Json -InputObject $installQueue -Depth 4 |
        Set-Content -Path $queuePath -Encoding UTF8

    if (Test-Path $logPath) { Remove-Item $logPath -Force }
    $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData

    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ((Test-Path $queuePath) -and [DateTime]::UtcNow -lt $deadline) {
        if ($playniteProcess.HasExited) { throw "Playnite exited before installing Media Library Enrichment." }
        Start-Sleep -Milliseconds 250
    }
    if (Test-Path $queuePath) { throw "Playnite did not consume Media Library Enrichment install queue." }

    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $($package.Version)" -Process $playniteProcess -TimeoutSeconds $StartupTimeoutSeconds

    $installedDir = Join-Path $userData "Extensions\$pluginId"
    if (-not (Test-Path (Join-Path $installedDir "extension.yaml"))) {
        throw "Media Library Enrichment was not materialized under its Playnite extension ID."
    }
    $receipt.phases.native_install = "PASS"

    $pluginData = Join-Path $userData "ExtensionsData\$pluginId"
    $observationPath = Join-Path $pluginData "observation-receipt.json"
    Wait-ForFile -Path $observationPath -Process $playniteProcess -TimeoutSeconds 20
    $firstObservation = Assert-ObservationReceipt -Path $observationPath
    $firstObservationHash = (Get-FileHash $observationPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $receipt.observation = [ordered]@{
        candidate_count = $firstObservation.CandidateCount
        names = @($firstObservation.Candidates | ForEach-Object Name | Sort-Object)
        kinds = @($firstObservation.Candidates | Sort-Object Name | ForEach-Object { "$($_.Name):$($_.Kind)" })
        sha256 = $firstObservationHash
    }
    Copy-Item $observationPath (Join-Path $EvidenceDir "observation-first.json") -Force
    $receipt.phases.observation_oracle = "PASS"

    Stop-Playnite -DesktopExe $desktopExe -UserData $userData
    Remove-Item $observationPath -Force
    if (Test-Path $logPath) { Remove-Item $logPath -Force }

    $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $($package.Version)" -Process $playniteProcess -TimeoutSeconds $StartupTimeoutSeconds
    Wait-ForFile -Path $observationPath -Process $playniteProcess -TimeoutSeconds 20
    $secondObservation = Assert-ObservationReceipt -Path $observationPath
    $secondObservationHash = (Get-FileHash $observationPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($secondObservationHash -ne $firstObservationHash) {
        throw "Observation receipt changed across restart."
    }
    Copy-Item $observationPath (Join-Path $EvidenceDir "observation-second.json") -Force
    $receipt.restart_observation_sha256 = $secondObservationHash
    $receipt.phases.restart_oracle = "PASS"

    Stop-Playnite -DesktopExe $desktopExe -UserData $userData

    $uninstallQueue = @([ordered]@{ InstallType = 1; Path = $installedDir })
    ConvertTo-Json -InputObject $uninstallQueue -Depth 4 |
        Set-Content -Path $queuePath -Encoding UTF8

    if (Test-Path $logPath) { Remove-Item $logPath -Force }
    $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData

    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ((Test-Path $queuePath) -and [DateTime]::UtcNow -lt $deadline) {
        if ($playniteProcess.HasExited) { throw "Playnite exited before uninstalling Media Library Enrichment." }
        Start-Sleep -Milliseconds 250
    }
    if (Test-Path $queuePath) { throw "Playnite did not consume Media Library Enrichment uninstall queue." }

    Wait-ForAbsent -Path $installedDir -TimeoutSeconds 15
    $receipt.phases.native_uninstall = "PASS"

    if (-not (Test-Path $observationPath)) {
        throw "Media Library Enrichment observation data did not survive native plugin uninstall."
    }
    $receipt.phases.data_preservation = "PASS"

    Stop-Playnite -DesktopExe $desktopExe -UserData $userData
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

    $receipt.finished_utc = [DateTime]::UtcNow.ToString("o")
    $receipt | ConvertTo-Json -Depth 12 |
        Set-Content -Path (Join-Path $EvidenceDir "receipt.json") -Encoding UTF8

    if (Test-Path (Join-Path $userData "playnite.log")) {
        Copy-Item (Join-Path $userData "playnite.log") (Join-Path $EvidenceDir "playnite.log") -Force
    }
}
