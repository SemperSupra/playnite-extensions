[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WorkRoot,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDir,

    [switch]$KeepInstalledForConflict,

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

function Set-ReconcileMode {
    param([string]$Path, [ValidateSet("observe", "apply", "rollback")][string]$Mode)

    [ordered]@{ Mode = $Mode } |
        ConvertTo-Json -Depth 4 |
        Set-Content -Path $Path -Encoding UTF8
}


function Assert-ObservationReceipt {
    param([string]$Path)

    $observation = Get-Content $Path -Raw | ConvertFrom-Json
    if ($observation.Schema -ne "sempersupra-media-library-enrichment-observation/v1") {
        throw "Unexpected Media Library Enrichment observation schema."
    }
    if ($observation.FixtureContract -ne "media-admission-v1") {
        throw "Observation receipt is not bound to media-admission-v1."
    }
    if ($observation.CandidateCount -ne 4 -or @($observation.Candidates).Count -ne 4) {
        throw "Expected three Humble candidates plus one explicit non-Humble media candidate."
    }

    $expected = @{
        "RDTE Humble Ebook" = [ordered]@{ Kind = "book"; Source = "Humble Bundle RDTE"; Producer = "humble-source-v1"; Key = "humble-source:rdte-humble-ebook" }
        "RDTE Humble Comic" = [ordered]@{ Kind = "comic"; Source = "Humble Bundle RDTE"; Producer = "humble-source-v1"; Key = "humble-source:rdte-humble-comic" }
        "RDTE Humble Soundtrack" = [ordered]@{ Kind = "audio"; Source = "Humble Bundle RDTE"; Producer = "humble-source-v1"; Key = "humble-source:rdte-humble-soundtrack" }
        "RDTE Manual Media Book" = [ordered]@{ Kind = "book"; Source = "Manual RDTE"; Producer = "rdte-manual-evidence-v1"; Key = "rdte-manual-media-book-v1" }
    }

    foreach ($candidate in @($observation.Candidates)) {
        if (-not $expected.ContainsKey($candidate.Name)) {
            throw "Unexpected observation candidate '$($candidate.Name)'."
        }
        $want = $expected[$candidate.Name]
        if ($candidate.Source -ne $want.Source -or
            $candidate.Kind -ne $want.Kind -or
            $candidate.AdmissionProducerKind -ne $want.Producer -or
            $candidate.AdmissionEvidenceKey -ne $want.Key) {
            throw "Candidate '$($candidate.Name)' does not match its normalized admission contract."
        }
        if (-not $candidate.CoverMissing) {
            throw "Candidate '$($candidate.Name)' does not have the expected missing-cover state."
        }
    }

    if (@($observation.Candidates | Where-Object Name -eq "RDTE Manual Game").Count -ne 0) {
        throw "Ordinary manual-game control was incorrectly admitted without explicit evidence."
    }

    return $observation
}

function Assert-FirstApply {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Schema -ne "sempersupra-media-library-enrichment-category-r4i/v1" -or
        $value.Mode -ne "apply" -or
        $value.CandidateCount -ne 4 -or
        $value.AppliedCount -ne 4 -or
        $value.NoopCount -ne 0 -or
        $value.ConflictCount -ne 0) {
        throw "First R4I apply receipt does not prove exactly four clean membership mutations."
    }

    $applied = @($value.Operations | Where-Object Outcome -eq "APPLIED")
    if ($applied.Count -ne 4) {
        throw "Expected exactly four APPLIED category operations."
    }

    $expectedNames = @(
        "SemperSupra.Media:Audio",
        "SemperSupra.Media:Book",
        "SemperSupra.Media:Book",
        "SemperSupra.Media:Comic"
    )
    $actualNames = @($applied | ForEach-Object CategoryName | Sort-Object)
    if (@(Compare-Object $expectedNames $actualNames).Count -ne 0) {
        throw "First apply did not materialize the expected owned categories."
    }

    return $value
}

function Assert-IdempotentApply {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "apply" -or
        $value.CandidateCount -ne 4 -or
        $value.AppliedCount -ne 0 -or
        $value.NoopCount -ne 4 -or
        $value.ConflictCount -ne 0) {
        throw "Second apply is not a clean four-item NOOP."
    }

    if (@($value.Operations | Where-Object Outcome -eq "NOOP").Count -ne 4) {
        throw "Second apply did not report exactly four NOOP operations."
    }

    return $value
}

function Assert-Rollback {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "rollback" -or
        $value.CandidateCount -ne 4 -or
        $value.RollbackAppliedCount -ne 4 -or
        $value.ConflictCount -ne 0) {
        throw "Rollback receipt does not prove four clean owned-membership reversions."
    }

    if (@($value.Operations | Where-Object Outcome -eq "ROLLBACK_APPLIED").Count -ne 4) {
        throw "Rollback did not remove exactly four owned memberships."
    }
    if (@($value.Operations | Where-Object Outcome -eq "CATEGORY_REMOVED").Count -ne 3) {
        throw "Rollback did not remove and verify exactly three plugin-created category objects."
    }

    return $value
}

function Assert-IdempotentRollback {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "rollback" -or
        $value.CandidateCount -ne 0 -or
        $value.RollbackAppliedCount -ne 0 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations).Count -ne 0) {
        throw "Second rollback is not an empty NOOP state."
    }

    return $value
}

function Assert-RolledBackLedger {
    param([string]$Path)

    $ledger = Get-Content $Path -Raw | ConvertFrom-Json
    $entries = @($ledger.Entries)
    if ($entries.Count -ne 4) {
        throw "Expected four ownership-ledger entries."
    }
    if (@($entries | Where-Object Status -ne "ROLLED_BACK").Count -ne 0) {
        throw "Ownership ledger contains an entry that is not ROLLED_BACK."
    }
    if (@($entries | Where-Object CategoryCreated -eq $true).Count -ne 3 -or
        @($entries | Where-Object CategoryCreated -ne $true).Count -ne 1) {
        throw "Expected three category-object creations and one shared Book-category membership."
    }

    return $ledger
}


function Assert-FirstActionApply {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Schema -ne "sempersupra-media-library-enrichment-action-r4i/v1" -or
        $value.Mode -ne "apply" -or
        $value.CandidateCount -ne 4 -or
        $value.AppliedCount -ne 4 -or
        $value.NoopCount -ne 0 -or
        $value.ConflictCount -ne 0) {
        throw "First action apply does not prove exactly four clean custom-action mutations."
    }

    $applied = @($value.Operations | Where-Object Outcome -eq "APPLIED")
    if ($applied.Count -ne 4 -or @($applied | Where-Object IsPlayAction -eq $true).Count -ne 0) {
        throw "Action apply must materialize exactly four non-play custom actions."
    }

    $expected = @(
        "RDTE Humble Comic|Read|rdte-comic.cbz",
        "RDTE Humble Ebook|Read|rdte-book.pdf",
        "RDTE Humble Soundtrack|Listen|rdte-soundtrack.flac",
        "RDTE Manual Media Book|Read|rdte-book.pdf"
    )
    $actual = @(
        $applied |
            Sort-Object Name |
            ForEach-Object { "$($_.Name)|$($_.ActionName)|$($_.LocalEvidenceName)" }
    )
    if (@(Compare-Object $expected $actual).Count -ne 0) {
        throw "First action apply does not match the expected Read/Listen custom actions."
    }

    return $value
}

function Assert-IdempotentActionApply {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "apply" -or
        $value.CandidateCount -ne 4 -or
        $value.AppliedCount -ne 0 -or
        $value.NoopCount -ne 4 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "NOOP").Count -ne 4) {
        throw "Second action apply is not a clean four-action NOOP."
    }

    return $value
}

function Assert-ActionRollback {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "rollback" -or
        $value.CandidateCount -ne 4 -or
        $value.RollbackAppliedCount -ne 4 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "ROLLBACK_APPLIED").Count -ne 4) {
        throw "Action rollback did not remove exactly four owned custom actions."
    }

    return $value
}

function Assert-IdempotentActionRollback {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "rollback" -or
        $value.CandidateCount -ne 0 -or
        $value.RollbackAppliedCount -ne 0 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations).Count -ne 0) {
        throw "Second action rollback is not an empty NOOP state."
    }

    return $value
}

function Assert-RolledBackActionLedger {
    param([string]$Path)

    $ledger = Get-Content $Path -Raw | ConvertFrom-Json
    $entries = @($ledger.Entries)
    if ($entries.Count -ne 4 -or
        @($entries | Where-Object Status -ne "ROLLED_BACK").Count -ne 0) {
        throw "Expected four ROLLED_BACK action-ledger entries."
    }

    return $ledger
}

function Assert-FirstFilterPresetApply {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "apply" -or
        $value.CandidateCount -ne 3 -or
        $value.AppliedCount -ne 3 -or
        $value.NoopCount -ne 0 -or
        $value.ConflictCount -ne 0) {
        throw "First filter-preset apply does not prove exactly three clean native shelf mutations."
    }

    $applied = @($value.Operations | Where-Object Outcome -eq "APPLIED")
    if ($applied.Count -ne 3) {
        throw "Expected exactly three APPLIED filter-preset operations."
    }

    $expected = @(
        "SemperSupra Media: Audio",
        "SemperSupra Media: Books",
        "SemperSupra Media: Comics"
    )
    $actual = @($applied | ForEach-Object PresetName | Sort-Object)
    if (@(Compare-Object $expected $actual).Count -ne 0) {
        throw "First filter-preset apply did not materialize the expected media shelves."
    }

    return $value
}

function Assert-IdempotentFilterPresetApply {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "apply" -or
        $value.CandidateCount -ne 3 -or
        $value.AppliedCount -ne 0 -or
        $value.NoopCount -ne 3 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "NOOP").Count -ne 3) {
        throw "Second filter-preset apply is not a clean three-preset NOOP."
    }

    return $value
}

function Assert-FilterPresetRollback {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "rollback" -or
        $value.CandidateCount -ne 3 -or
        $value.RollbackAppliedCount -ne 3 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "ROLLBACK_APPLIED").Count -ne 3) {
        throw "Filter-preset rollback did not remove exactly three owned native shelves."
    }

    return $value
}

function Assert-IdempotentFilterPresetRollback {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "rollback" -or
        $value.CandidateCount -ne 0 -or
        $value.RollbackAppliedCount -ne 0 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations).Count -ne 0) {
        throw "Second filter-preset rollback is not an empty NOOP state."
    }

    return $value
}

function Assert-RolledBackFilterPresetLedger {
    param([string]$Path)

    $ledger = Get-Content $Path -Raw | ConvertFrom-Json
    $entries = @($ledger.Entries)
    if ($entries.Count -ne 3 -or
        @($entries | Where-Object Status -ne "ROLLED_BACK").Count -ne 0) {
        throw "Expected three ROLLED_BACK filter-preset ledger entries."
    }

    return $ledger
}

function Assert-FirstCoverApply {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "apply" -or
        $value.CandidateCount -ne 2 -or
        $value.AppliedCount -ne 2 -or
        $value.NoopCount -ne 0 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "APPLIED").Count -ne 2) {
        throw "First cover apply does not prove exactly two clean native CoverImage mutations."
    }

    $expected = @(
        "73000000-0000-4000-8000-000000000001|rdte-book-cover-v1|rdte-book-cover.png",
        "73000000-0000-4000-8000-000000000002|rdte-comic-cover-v1|rdte-comic-cover.png"
    )
    $actual = @(
        $value.Operations |
            Where-Object Outcome -eq "APPLIED" |
            Sort-Object PlayniteId |
            ForEach-Object { "$($_.PlayniteId)|$($_.EvidenceKey)|$($_.LocalEvidenceName)" }
    )
    if (@(Compare-Object $expected $actual).Count -ne 0) {
        throw "First cover apply does not match the normalized ebook/comic evidence set."
    }

    return $value
}

function Assert-IdempotentCoverApply {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "apply" -or
        $value.CandidateCount -ne 2 -or
        $value.AppliedCount -ne 0 -or
        $value.NoopCount -ne 2 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "NOOP").Count -ne 2) {
        throw "Second cover apply is not a clean two-cover NOOP."
    }

    return $value
}

function Assert-CoverRollback {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "rollback" -or
        $value.CandidateCount -ne 2 -or
        $value.RollbackAppliedCount -ne 2 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "ROLLBACK_APPLIED").Count -ne 2) {
        throw "Cover rollback did not remove exactly two owned CoverImage projections."
    }

    return $value
}

function Assert-IdempotentCoverRollback {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "rollback" -or
        $value.CandidateCount -ne 0 -or
        $value.RollbackAppliedCount -ne 0 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations).Count -ne 0) {
        throw "Second cover rollback is not an empty NOOP state."
    }

    return $value
}

function Assert-RolledBackCoverLedger {
    param([string]$Path)

    $ledger = Get-Content $Path -Raw | ConvertFrom-Json
    $entries = @($ledger.Entries)
    if ($entries.Count -ne 2 -or
        @($entries | Where-Object Status -ne "ROLLED_BACK").Count -ne 0) {
        throw "Expected two ROLLED_BACK cover-ledger entries."
    }

    return $ledger
}

$receipt = [ordered]@{
    schema = "sempersupra-media-library-enrichment-rdte/v2"
    source_sha = if ($env:RDTE_SOURCE_SHA) { $env:RDTE_SOURCE_SHA } else { $env:GITHUB_SHA }
    product_authority_sha = $env:RDTE_PRODUCT_AUTHORITY_SHA
    trigger_sha = if ($env:RDTE_TRIGGER_SHA) { $env:RDTE_TRIGGER_SHA } else { $env:GITHUB_SHA }
    plugin_id = $pluginId
    plugin_name = $pluginName
    fixture_profile = "media-raw-v1"
    phases = [ordered]@{
        unit_tests = "NOT_RUN"
        build = "NOT_RUN"
        stage = "NOT_RUN"
        toolbox_pack = "NOT_RUN"
        native_install = "NOT_RUN"
        observation_oracle = "NOT_RUN"
        first_apply = "NOT_RUN"
        first_action_apply = "NOT_RUN"
        first_filter_preset_apply = "NOT_RUN"
        first_cover_apply = "NOT_RUN"
        idempotent_apply = "NOT_RUN"
        idempotent_action_apply = "NOT_RUN"
        idempotent_filter_preset_apply = "NOT_RUN"
        idempotent_cover_apply = "NOT_RUN"
        rollback = "NOT_RUN"
        action_rollback = "NOT_RUN"
        filter_preset_rollback = "NOT_RUN"
        cover_rollback = "NOT_RUN"
        idempotent_rollback = "NOT_RUN"
        idempotent_action_rollback = "NOT_RUN"
        idempotent_filter_preset_rollback = "NOT_RUN"
        idempotent_cover_rollback = "NOT_RUN"
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

    $pluginData = Join-Path $userData "ExtensionsData\$pluginId"
    New-Item $pluginData -ItemType Directory -Force | Out-Null
    $settingsPath = Join-Path $pluginData "settings.json"
    $observationPath = Join-Path $pluginData "observation-receipt.json"
    $r4iPath = Join-Path $pluginData "r4i-receipt.json"
    $ledgerPath = Join-Path $pluginData "category-ledger.json"
    $actionR4iPath = Join-Path $pluginData "action-r4i-receipt.json"
    $actionLedgerPath = Join-Path $pluginData "action-ledger.json"
    $filterPresetR4iPath = Join-Path $pluginData "filter-preset-r4i-receipt.json"
    $filterPresetLedgerPath = Join-Path $pluginData "filter-preset-ledger.json"
    $admissionEvidencePath = Join-Path $pluginData "admission-evidence.json"
    $coverEvidencePath = Join-Path $pluginData "cover-evidence.json"
    $coverR4iPath = Join-Path $pluginData "cover-r4i-receipt.json"
    $coverLedgerPath = Join-Path $pluginData "cover-ledger.json"

    $seederMediaPath = Join-Path $userData "ExtensionsData\6d06cf1b-d1e4-4caa-b6c3-cc6026953135\media"
    $bookCoverPath = Join-Path $seederMediaPath "rdte-book-cover.png"
    $comicCoverPath = Join-Path $seederMediaPath "rdte-comic-cover.png"

    $bookCoverSha = (Get-FileHash $bookCoverPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $comicCoverSha = (Get-FileHash $comicCoverPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($bookCoverSha -ne "ca3845dd963d15fe3131cbaf9a2de3f525726457f9eae923eaa749d842078660" -or
        $comicCoverSha -ne "03e7a770b4891ee4e1b35e880f6700238830dcc99eb8e0f6c7a0d5a1fb2d362d") {
        throw "Deterministic local cover evidence does not match the pinned content hashes."
    }

    $coverEvidence = [ordered]@{
        Schema = "sempersupra-media-library-enrichment-cover-evidence/v1"
        Items = @(
            [ordered]@{
                PlayniteId = "73000000-0000-4000-8000-000000000001"
                EvidenceKey = "rdte-book-cover-v1"
                LocalPath = $bookCoverPath
                ContentSha256 = $bookCoverSha
                SourceKind = "rdte-local"
            },
            [ordered]@{
                PlayniteId = "73000000-0000-4000-8000-000000000002"
                EvidenceKey = "rdte-comic-cover-v1"
                LocalPath = $comicCoverPath
                ContentSha256 = $comicCoverSha
                SourceKind = "rdte-local"
            }
        )
    }
    ConvertTo-Json -InputObject $coverEvidence -Depth 6 |
        Set-Content -Path $coverEvidencePath -Encoding UTF8

    $admissionEvidence = [ordered]@{
        Schema = "sempersupra-media-library-enrichment-admission-evidence/v1"
        Items = @(
            [ordered]@{
                PlayniteId = "73000000-0000-4000-8000-000000000005"
                ProviderGameId = "rdte-manual-media-book"
                EvidenceKey = "rdte-manual-media-book-v1"
                ProducerKind = "rdte-manual-evidence-v1"
            }
        )
    }
    ConvertTo-Json -InputObject $admissionEvidence -Depth 6 |
        Set-Content -Path $admissionEvidencePath -Encoding UTF8

    Set-ReconcileMode -Path $settingsPath -Mode "apply"

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

    Wait-ForFile -Path $observationPath -Process $playniteProcess -TimeoutSeconds 20
    $firstObservation = Assert-ObservationReceipt -Path $observationPath
    $receipt.observation = [ordered]@{
        candidate_count = $firstObservation.CandidateCount
        names = @($firstObservation.Candidates | ForEach-Object Name | Sort-Object)
        kinds = @($firstObservation.Candidates | Sort-Object Name | ForEach-Object { "$($_.Name):$($_.Kind)" })
    }
    Copy-Item $observationPath (Join-Path $EvidenceDir "observation-first.json") -Force
    $receipt.phases.observation_oracle = "PASS"

    Wait-ForFile -Path $r4iPath -Process $playniteProcess -TimeoutSeconds 20
    $firstApply = Assert-FirstApply -Path $r4iPath
    $receipt.first_apply_plan_sha256 = $firstApply.PlanSha256
    Copy-Item $r4iPath (Join-Path $EvidenceDir "r4i-first-apply.json") -Force
    Copy-Item $ledgerPath (Join-Path $EvidenceDir "ledger-after-first-apply.json") -Force
    $receipt.phases.first_apply = "PASS"

    Wait-ForFile -Path $actionR4iPath -Process $playniteProcess -TimeoutSeconds 20
    $firstActionApply = Assert-FirstActionApply -Path $actionR4iPath
    $receipt.first_action_apply_plan_sha256 = $firstActionApply.PlanSha256
    Copy-Item $actionR4iPath (Join-Path $EvidenceDir "action-r4i-first-apply.json") -Force
    Copy-Item $actionLedgerPath (Join-Path $EvidenceDir "action-ledger-after-first-apply.json") -Force
    $receipt.phases.first_action_apply = "PASS"

    Wait-ForFile -Path $filterPresetR4iPath -Process $playniteProcess -TimeoutSeconds 20
    $firstFilterPresetApply = Assert-FirstFilterPresetApply -Path $filterPresetR4iPath
    $receipt.first_filter_preset_apply_plan_sha256 = $firstFilterPresetApply.PlanSha256
    Copy-Item $filterPresetR4iPath (Join-Path $EvidenceDir "filter-preset-r4i-first-apply.json") -Force
    Copy-Item $filterPresetLedgerPath (Join-Path $EvidenceDir "filter-preset-ledger-after-first-apply.json") -Force
    $receipt.phases.first_filter_preset_apply = "PASS"

    Wait-ForFile -Path $coverR4iPath -Process $playniteProcess -TimeoutSeconds 20
    $firstCoverApply = Assert-FirstCoverApply -Path $coverR4iPath
    $receipt.first_cover_apply_plan_sha256 = $firstCoverApply.PlanSha256
    Copy-Item $coverR4iPath (Join-Path $EvidenceDir "cover-r4i-first-apply.json") -Force
    Copy-Item $coverLedgerPath (Join-Path $EvidenceDir "cover-ledger-after-first-apply.json") -Force
    $receipt.phases.first_cover_apply = "PASS"

    Stop-Playnite -DesktopExe $desktopExe -UserData $userData
    Remove-Item $r4iPath -Force
    if (Test-Path $actionR4iPath) { Remove-Item $actionR4iPath -Force }
    if (Test-Path $filterPresetR4iPath) { Remove-Item $filterPresetR4iPath -Force }
    if (Test-Path $coverR4iPath) { Remove-Item $coverR4iPath -Force }
    if (Test-Path $logPath) { Remove-Item $logPath -Force }

    $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $($package.Version)" -Process $playniteProcess -TimeoutSeconds $StartupTimeoutSeconds
    Wait-ForFile -Path $r4iPath -Process $playniteProcess -TimeoutSeconds 20
    $secondApply = Assert-IdempotentApply -Path $r4iPath
    $receipt.idempotent_apply_plan_sha256 = $secondApply.PlanSha256
    Copy-Item $r4iPath (Join-Path $EvidenceDir "r4i-second-apply.json") -Force
    $receipt.phases.idempotent_apply = "PASS"

    Wait-ForFile -Path $actionR4iPath -Process $playniteProcess -TimeoutSeconds 20
    $secondActionApply = Assert-IdempotentActionApply -Path $actionR4iPath
    $receipt.idempotent_action_apply_plan_sha256 = $secondActionApply.PlanSha256
    Copy-Item $actionR4iPath (Join-Path $EvidenceDir "action-r4i-second-apply.json") -Force
    $receipt.phases.idempotent_action_apply = "PASS"

    Wait-ForFile -Path $filterPresetR4iPath -Process $playniteProcess -TimeoutSeconds 20
    $secondFilterPresetApply = Assert-IdempotentFilterPresetApply -Path $filterPresetR4iPath
    $receipt.idempotent_filter_preset_apply_plan_sha256 = $secondFilterPresetApply.PlanSha256
    Copy-Item $filterPresetR4iPath (Join-Path $EvidenceDir "filter-preset-r4i-second-apply.json") -Force
    $receipt.phases.idempotent_filter_preset_apply = "PASS"

    Wait-ForFile -Path $coverR4iPath -Process $playniteProcess -TimeoutSeconds 20
    $secondCoverApply = Assert-IdempotentCoverApply -Path $coverR4iPath
    $receipt.idempotent_cover_apply_plan_sha256 = $secondCoverApply.PlanSha256
    Copy-Item $coverR4iPath (Join-Path $EvidenceDir "cover-r4i-second-apply.json") -Force
    $receipt.phases.idempotent_cover_apply = "PASS"

    Stop-Playnite -DesktopExe $desktopExe -UserData $userData
    Set-ReconcileMode -Path $settingsPath -Mode "rollback"
    Remove-Item $r4iPath -Force
    if (Test-Path $actionR4iPath) { Remove-Item $actionR4iPath -Force }
    if (Test-Path $filterPresetR4iPath) { Remove-Item $filterPresetR4iPath -Force }
    if (Test-Path $coverR4iPath) { Remove-Item $coverR4iPath -Force }
    if (Test-Path $logPath) { Remove-Item $logPath -Force }

    $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $($package.Version)" -Process $playniteProcess -TimeoutSeconds $StartupTimeoutSeconds
    Wait-ForFile -Path $r4iPath -Process $playniteProcess -TimeoutSeconds 20
    $rollback = Assert-Rollback -Path $r4iPath
    $receipt.rollback_plan_sha256 = $rollback.PlanSha256
    Assert-RolledBackLedger -Path $ledgerPath | Out-Null
    Copy-Item $r4iPath (Join-Path $EvidenceDir "r4i-rollback.json") -Force
    Copy-Item $ledgerPath (Join-Path $EvidenceDir "ledger-after-rollback.json") -Force
    $receipt.phases.rollback = "PASS"

    Wait-ForFile -Path $actionR4iPath -Process $playniteProcess -TimeoutSeconds 20
    $actionRollback = Assert-ActionRollback -Path $actionR4iPath
    $receipt.action_rollback_plan_sha256 = $actionRollback.PlanSha256
    Assert-RolledBackActionLedger -Path $actionLedgerPath | Out-Null
    Copy-Item $actionR4iPath (Join-Path $EvidenceDir "action-r4i-rollback.json") -Force
    Copy-Item $actionLedgerPath (Join-Path $EvidenceDir "action-ledger-after-rollback.json") -Force
    $receipt.phases.action_rollback = "PASS"

    Wait-ForFile -Path $filterPresetR4iPath -Process $playniteProcess -TimeoutSeconds 20
    $filterPresetRollback = Assert-FilterPresetRollback -Path $filterPresetR4iPath
    $receipt.filter_preset_rollback_plan_sha256 = $filterPresetRollback.PlanSha256
    Assert-RolledBackFilterPresetLedger -Path $filterPresetLedgerPath | Out-Null
    Copy-Item $filterPresetR4iPath (Join-Path $EvidenceDir "filter-preset-r4i-rollback.json") -Force
    Copy-Item $filterPresetLedgerPath (Join-Path $EvidenceDir "filter-preset-ledger-after-rollback.json") -Force
    $receipt.phases.filter_preset_rollback = "PASS"

    Wait-ForFile -Path $coverR4iPath -Process $playniteProcess -TimeoutSeconds 20
    $coverRollback = Assert-CoverRollback -Path $coverR4iPath
    $receipt.cover_rollback_plan_sha256 = $coverRollback.PlanSha256
    Assert-RolledBackCoverLedger -Path $coverLedgerPath | Out-Null
    Copy-Item $coverR4iPath (Join-Path $EvidenceDir "cover-r4i-rollback.json") -Force
    Copy-Item $coverLedgerPath (Join-Path $EvidenceDir "cover-ledger-after-rollback.json") -Force
    $receipt.phases.cover_rollback = "PASS"

    Stop-Playnite -DesktopExe $desktopExe -UserData $userData
    Remove-Item $r4iPath -Force
    if (Test-Path $actionR4iPath) { Remove-Item $actionR4iPath -Force }
    if (Test-Path $filterPresetR4iPath) { Remove-Item $filterPresetR4iPath -Force }
    if (Test-Path $coverR4iPath) { Remove-Item $coverR4iPath -Force }
    if (Test-Path $logPath) { Remove-Item $logPath -Force }

    $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $($package.Version)" -Process $playniteProcess -TimeoutSeconds $StartupTimeoutSeconds
    Wait-ForFile -Path $r4iPath -Process $playniteProcess -TimeoutSeconds 20
    Assert-IdempotentRollback -Path $r4iPath | Out-Null
    Copy-Item $r4iPath (Join-Path $EvidenceDir "r4i-second-rollback.json") -Force
    $receipt.phases.idempotent_rollback = "PASS"

    Wait-ForFile -Path $actionR4iPath -Process $playniteProcess -TimeoutSeconds 20
    Assert-IdempotentActionRollback -Path $actionR4iPath | Out-Null
    Copy-Item $actionR4iPath (Join-Path $EvidenceDir "action-r4i-second-rollback.json") -Force
    $receipt.phases.idempotent_action_rollback = "PASS"

    Wait-ForFile -Path $filterPresetR4iPath -Process $playniteProcess -TimeoutSeconds 20
    $secondFilterPresetRollback = Assert-IdempotentFilterPresetRollback -Path $filterPresetR4iPath
    $receipt.idempotent_filter_preset_rollback_plan_sha256 = $secondFilterPresetRollback.PlanSha256
    Copy-Item $filterPresetR4iPath (Join-Path $EvidenceDir "filter-preset-r4i-second-rollback.json") -Force
    $receipt.phases.idempotent_filter_preset_rollback = "PASS"

    Wait-ForFile -Path $coverR4iPath -Process $playniteProcess -TimeoutSeconds 20
    $secondCoverRollback = Assert-IdempotentCoverRollback -Path $coverR4iPath
    $receipt.idempotent_cover_rollback_plan_sha256 = $secondCoverRollback.PlanSha256
    Copy-Item $coverR4iPath (Join-Path $EvidenceDir "cover-r4i-second-rollback.json") -Force
    $receipt.phases.idempotent_cover_rollback = "PASS"

    Stop-Playnite -DesktopExe $desktopExe -UserData $userData

    if ($KeepInstalledForConflict) {
        $receipt.phases.native_uninstall = "DEFERRED_TO_CONFLICT"
        $receipt.phases.data_preservation = "DEFERRED_TO_CONFLICT"
        $receipt.result = "PASS"
        return
    }

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

    foreach ($persistentPath in @(
        $settingsPath,
        $observationPath,
        $r4iPath,
        $ledgerPath,
        $actionR4iPath,
        $actionLedgerPath,
        $filterPresetR4iPath,
        $filterPresetLedgerPath,
        $coverEvidencePath,
        $coverR4iPath,
        $coverLedgerPath
    )) {
        if (-not (Test-Path $persistentPath -PathType Leaf)) {
            throw "Persistent plugin data did not survive native uninstall: $persistentPath"
        }
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
