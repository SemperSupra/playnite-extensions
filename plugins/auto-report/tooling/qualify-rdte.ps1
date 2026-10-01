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

$pluginId = "b27b4f0c-642b-4fbd-9559-6e832026f010"
$pluginName = "Playnite Auto Report"
$pluginRoot = Split-Path -Parent $PSScriptRoot
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
    param(
        [string]$Path,
        [string]$Text,
        $Process,
        [int]$TimeoutSeconds
    )

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

$receipt = [ordered]@{
    schema = "sempersupra-auto-report-rdte/v1"
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
        export_oracle = "NOT_RUN"
        restart_oracle = "NOT_RUN"
        native_uninstall = "NOT_RUN"
        data_preservation = "NOT_RUN"
    }
    result = "RUNNING"
}

$playniteProcess = $null
try {
    if ($env:OS -ne "Windows_NT") {
        throw "Auto Report native qualification requires Windows."
    }

    $desktopExe = Find-ExactlyOneFile -Root $runtimeDir -Name "Playnite.DesktopApp.exe"
    $toolboxExe = Find-ExactlyOneFile -Root $runtimeDir -Name "Toolbox.exe"
    $logPath = Join-Path $userData "playnite.log"

    Push-Location $pluginRoot
    try {
        & dotnet test ".\tests\PlayniteAutoReport.Tests\PlayniteAutoReport.Tests.csproj" -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "Auto Report unit tests failed." }
        $receipt.phases.unit_tests = "PASS"

        & dotnet build ".\src\PlayniteAutoReport\PlayniteAutoReport.csproj" -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "Auto Report plugin build failed." }
        $receipt.phases.build = "PASS"
    }
    finally {
        Pop-Location
    }

    $buildOutput = Join-Path $pluginRoot "src\PlayniteAutoReport\bin\Release\net462"
    $stageDir = Join-Path $WorkRoot "auto-report-stage"
    $packageDir = Join-Path $WorkRoot "auto-report-package"
    New-Item $stageDir, $packageDir -ItemType Directory -Force | Out-Null

    $payload = @(
        @{ Name = "extension.yaml"; Source = (Join-Path $pluginRoot "extension.yaml") },
        @{ Name = "PlayniteAutoReport.dll"; Source = (Join-Path $buildOutput "PlayniteAutoReport.dll") },
        @{ Name = "PlayniteAutoReport.Core.dll"; Source = (Join-Path $buildOutput "PlayniteAutoReport.Core.dll") }
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

    $stagedNames = @(Get-ChildItem $stageDir -File | ForEach-Object Name | Sort-Object)
    $expectedNames = @("PlayniteAutoReport.Core.dll", "PlayniteAutoReport.dll", "extension.yaml")
    if (@(Compare-Object $expectedNames $stagedNames).Count -ne 0) {
        throw "Auto Report staged payload differs from the approved three-file allowlist."
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
    if ($LASTEXITCODE -ne 0) { throw "Playnite Toolbox pack failed for Auto Report." }

    $packages = @(Get-ChildItem $packageDir -Filter "*.pext" -File)
    if ($packages.Count -ne 1) {
        throw "Expected one Auto Report .pext, found $($packages.Count)."
    }

    $packagePath = $packages[0].FullName
    $package = Read-PextManifest -Path $packagePath
    if ($package.Id -ne $pluginId -or $package.Name -ne $pluginName) {
        throw "Auto Report package identity mismatch."
    }

    $expectedEntries = @("PlayniteAutoReport.Core.dll", "PlayniteAutoReport.dll", "extension.yaml")
    if (@(Compare-Object $expectedEntries $package.Entries).Count -ne 0) {
        throw "Auto Report .pext contains an unexpected payload."
    }

    $receipt.version = $package.Version
    $receipt.package_sha256 = (Get-FileHash $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $receipt.package_entries = $package.Entries
    Copy-Item $packagePath (Join-Path $EvidenceDir "PlayniteAutoReport-$($package.Version).pext") -Force
    $receipt.phases.toolbox_pack = "PASS"

    $pluginData = Join-Path $userData "ExtensionsData\$pluginId"
    $reports = Join-Path $pluginData "reports"
    New-Item $pluginData -ItemType Directory -Force | Out-Null

    $settings = [ordered]@{
        OutputDirectory = "reports"
        ExportCsv = $true
        ExportJson = $true
        WriteSummary = $true
        ExportOnApplicationStart = $true
        ExportOnLibraryUpdate = $false
        ExportOnGameStopped = $false
        IncludeHidden = $true
        IncludeUninstalled = $true
        KeepHistory = $false
        HistoryRetentionDays = 90
        MaximumHistorySnapshots = 500
        MaximumHistorySizeMegabytes = 512
        MinimumHistoryIntervalMinutes = 60
        ListSeparator = " | "
    }
    $settings | ConvertTo-Json -Depth 8 |
        Set-Content -Path (Join-Path $pluginData "report-settings.json") -Encoding UTF8

    $queuePath = Join-Path $userData "extinstalls.json"
    $installQueue = @([ordered]@{ InstallType = 0; Path = $packagePath })
    ConvertTo-Json -InputObject $installQueue -Depth 4 |
        Set-Content -Path $queuePath -Encoding UTF8

    if (Test-Path $logPath) { Remove-Item $logPath -Force }
    $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData

    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ((Test-Path $queuePath) -and [DateTime]::UtcNow -lt $deadline) {
        if ($playniteProcess.HasExited) { throw "Playnite exited before installing Auto Report." }
        Start-Sleep -Milliseconds 250
    }
    if (Test-Path $queuePath) { throw "Playnite did not consume Auto Report install queue." }

    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $($package.Version)" -Process $playniteProcess -TimeoutSeconds $StartupTimeoutSeconds
    $installedDir = Join-Path $userData "Extensions\$pluginId"
    if (-not (Test-Path (Join-Path $installedDir "extension.yaml"))) {
        throw "Auto Report was not materialized under its Playnite extension ID."
    }
    $receipt.phases.native_install = "PASS"

    $latestJson = Join-Path $reports "playnite-library-latest.json"
    $latestCsv = Join-Path $reports "playnite-library-latest.csv"
    $latestSummary = Join-Path $reports "playnite-summary-latest.json"

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ((-not (Test-Path $latestJson) -or -not (Test-Path $latestCsv) -or -not (Test-Path $latestSummary)) -and [DateTime]::UtcNow -lt $deadline) {
        if ($playniteProcess.HasExited) { throw "Playnite exited before Auto Report export appeared." }
        Start-Sleep -Milliseconds 250
    }

    foreach ($path in @($latestJson, $latestCsv, $latestSummary)) {
        if (-not (Test-Path $path)) { throw "Missing Auto Report output: $path" }
    }

    $snapshot = Get-Content $latestJson -Raw | ConvertFrom-Json
    $summary = Get-Content $latestSummary -Raw | ConvertFrom-Json
    $csv = @(Import-Csv $latestCsv)

    $expectedFixtureNames = @(
        "RDTE Humble Ebook",
        "RDTE Humble Comic",
        "RDTE Humble Soundtrack",
        "RDTE Manual Game"
    )

    $actualNames = @($snapshot.Games | ForEach-Object Name | Sort-Object)
    if ($snapshot.Games.Count -ne 4 -or
        $summary.TotalGames -ne 4 -or
        $summary.InstalledGames -ne 3 -or
        $csv.Count -ne 4 -or
        @(Compare-Object ($expectedFixtureNames | Sort-Object) $actualNames).Count -ne 0) {
        throw "Auto Report output does not describe the deterministic four-game fixture correctly."
    }

    if ($snapshot.Trigger -ne "application-start" -or $summary.Trigger -ne "application-start") {
        throw "Auto Report export was not produced by the expected application-start hook."
    }

    $receipt.export = [ordered]@{
        trigger = $snapshot.Trigger
        game_count = $snapshot.Games.Count
        installed_games = $summary.InstalledGames
        csv_rows = $csv.Count
        names = $actualNames
    }
    Copy-Item $latestJson (Join-Path $EvidenceDir "playnite-library-latest.json") -Force
    Copy-Item $latestCsv (Join-Path $EvidenceDir "playnite-library-latest.csv") -Force
    Copy-Item $latestSummary (Join-Path $EvidenceDir "playnite-summary-latest.json") -Force
    $receipt.phases.export_oracle = "PASS"

    Stop-Playnite -DesktopExe $desktopExe -UserData $userData
    if (Test-Path $logPath) { Remove-Item $logPath -Force }

    $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $($package.Version)" -Process $playniteProcess -TimeoutSeconds $StartupTimeoutSeconds

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $restartSnapshot = if (Test-Path $latestJson) { Get-Content $latestJson -Raw | ConvertFrom-Json } else { $null }
        if ($restartSnapshot -and $restartSnapshot.Games.Count -eq 4 -and $restartSnapshot.Trigger -eq "application-start") { break }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    if (-not $restartSnapshot -or $restartSnapshot.Games.Count -ne 4) {
        throw "Auto Report restart export did not preserve the four-game oracle."
    }
    $receipt.phases.restart_oracle = "PASS"

    Stop-Playnite -DesktopExe $desktopExe -UserData $userData

    $uninstallQueue = @([ordered]@{ InstallType = 1; Path = $installedDir })
    ConvertTo-Json -InputObject $uninstallQueue -Depth 4 |
        Set-Content -Path $queuePath -Encoding UTF8

    if (Test-Path $logPath) { Remove-Item $logPath -Force }
    $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData

    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ((Test-Path $queuePath) -and [DateTime]::UtcNow -lt $deadline) {
        if ($playniteProcess.HasExited) { throw "Playnite exited before uninstalling Auto Report." }
        Start-Sleep -Milliseconds 250
    }
    if (Test-Path $queuePath) { throw "Playnite did not consume Auto Report uninstall queue." }

    Wait-ForAbsent -Path $installedDir -TimeoutSeconds 15
    $receipt.phases.native_uninstall = "PASS"

    if (-not (Test-Path (Join-Path $pluginData "report-settings.json")) -or
        -not (Test-Path $latestJson) -or
        -not (Test-Path $latestCsv) -or
        -not (Test-Path $latestSummary)) {
        throw "Auto Report persistent data did not survive native plugin uninstall."
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
