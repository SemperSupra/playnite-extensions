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
$seederId = "SemperSupraRdteSeeder_6d06cf1b-d1e4-4caa-b6c3-cc6026953135"
$seederDataId = "6d06cf1b-d1e4-4caa-b6c3-cc6026953135"
$seederName = "RDTE Fixture Seeder"
$bookCategoryName = "SemperSupra.Media:Book"

$userData = Join-Path $WorkRoot "userdata"
$runtimeDir = Join-Path $WorkRoot "runtime"
$desktopExe = @(Get-ChildItem $runtimeDir -Filter "Playnite.DesktopApp.exe" -File -Recurse)
if ($desktopExe.Count -ne 1) {
    throw "Expected exactly one Playnite.DesktopApp.exe."
}
$desktopExe = $desktopExe[0].FullName
$logPath = Join-Path $userData "playnite.log"
$queuePath = Join-Path $userData "extinstalls.json"

$pluginPackages = @(Get-ChildItem $EvidenceDir -Filter "MediaLibraryEnrichment-*.pext" -File)
if ($pluginPackages.Count -ne 1) {
    throw "Expected exactly one qualified Media Library Enrichment package."
}
$pluginPackage = $pluginPackages[0].FullName

$seederPackages = @(
    Get-ChildItem (Join-Path $WorkRoot "fixture-seeder-package") -Filter "*.pext" -File
)
if ($seederPackages.Count -ne 1) {
    throw "Expected exactly one already-qualified fixture-seeder package."
}
$seederPackage = $seederPackages[0].FullName

function Start-Playnite {
    return Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path -Parent $desktopExe) -ArgumentList @(
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
    param([string]$Path, [string]$Text, $Process)

    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "Playnite exited while waiting for '$Text'."
        }
        if (Test-Path $Path) {
            $content = Get-Content $Path -Raw
            if ($null -ne $content -and $content.Contains($Text)) {
                return
            }
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for '$Text'."
}

function Wait-ForFile {
    param([string]$Path, $Process)

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "Playnite exited while waiting for '$Path'."
        }
        if (Test-Path $Path) {
            return
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for '$Path'."
}

function Find-InstalledExtension {
    param([string]$ExpectedId)

    $root = Join-Path $userData "Extensions"
    if (-not (Test-Path $root)) {
        return $null
    }

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
        if ($Process.HasExited) {
            throw "Playnite exited before consuming extension queue."
        }
        Start-Sleep -Milliseconds 250
    }
    if (Test-Path $queuePath) {
        throw "Playnite did not consume extension queue."
    }
}

function Set-ProductMode {
    param([ValidateSet("observe", "apply", "rollback")][string]$Mode)

    $pluginData = Join-Path $userData "ExtensionsData\$pluginId"
    New-Item $pluginData -ItemType Directory -Force | Out-Null
    [ordered]@{ Mode = $Mode } |
        ConvertTo-Json -Depth 4 |
        Set-Content -Path (Join-Path $pluginData "settings.json") -Encoding UTF8
}

function Assert-Reapply {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "apply" -or
        $value.CandidateCount -ne 4 -or
        $value.AppliedCount -ne 4 -or
        $value.NoopCount -ne 0 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "APPLIED").Count -ne 4) {
        throw "R4I re-apply did not reproduce four clean owned mutations."
    }
    return $value
}

function Assert-ConflictReceipt {
    param([string]$Path, [ValidateSet("apply", "verify")][string]$ExpectedMode)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.schema -ne "sempersupra-playnite-r4i-conflict-fixture/v1" -or
        $value.mode -ne $ExpectedMode -or
        $value.result -ne "PASS" -or
        $value.membership_present -ne $true -or
        $value.category_name -ne $bookCategoryName) {
        throw "Fixture seeder did not prove '$ExpectedMode' external Book-category membership."
    }
    return $value
}

function Assert-ConflictRollback {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "rollback" -or
        $value.CandidateCount -ne 4 -or
        $value.RollbackAppliedCount -ne 4 -or
        $value.ConflictCount -ne 1) {
        throw "Conflict rollback did not prove four owned reversions plus one preserved external conflict."
    }

    if (@($value.Operations | Where-Object Outcome -eq "ROLLBACK_APPLIED").Count -ne 4) {
        throw "Conflict rollback did not remove exactly four owned memberships."
    }

    if (@($value.Operations | Where-Object Outcome -eq "CATEGORY_REMOVED").Count -ne 2) {
        throw "Conflict rollback should remove only Comic and Audio category objects."
    }

    $preserved = @(
        $value.Operations |
            Where-Object Outcome -eq "CONFLICT_CATEGORY_IN_USE"
    )
    if ($preserved.Count -ne 1 -or
        $preserved[0].CategoryName -ne $bookCategoryName) {
        throw "Conflict rollback did not preserve the externally referenced Book category."
    }

    return $value
}


function Assert-ActionReapply {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Schema -ne "sempersupra-media-library-enrichment-action-r4i/v1" -or
        $value.Mode -ne "apply" -or
        $value.CandidateCount -ne 4 -or
        $value.AppliedCount -ne 4 -or
        $value.NoopCount -ne 0 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "APPLIED").Count -ne 4) {
        throw "Action re-apply did not reproduce four clean custom actions."
    }

    if (@($value.Operations | Where-Object IsPlayAction -eq $true).Count -ne 0) {
        throw "Action re-apply produced a primary play action."
    }

    return $value
}

function Assert-ActionConflictReceipt {
    param([string]$Path, [ValidateSet("apply", "verify")][string]$ExpectedMode)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.schema -ne "sempersupra-playnite-r4i-action-conflict-fixture/v1" -or
        $value.mode -ne $ExpectedMode -or
        $value.result -ne "PASS" -or
        $value.action_name -ne "Read" -or
        $value.action_path_name -ne "external-book.pdf" -or
        $value.is_play_action -ne $false -or
        $value.playtime -ne 0 -or
        $value.play_count -ne 0 -or
        $value.last_activity_present -ne $false -or
        $value.last_activity -ne "") {
        throw "Action conflict fixture did not prove preserved custom-action semantics for '$ExpectedMode'."
    }

    return $value
}

function Assert-ActionConflictRollback {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "rollback" -or
        $value.CandidateCount -ne 4 -or
        $value.RollbackAppliedCount -ne 3 -or
        $value.ConflictCount -ne 1) {
        throw "Action conflict rollback did not prove three owned removals plus one preserved external mutation."
    }

    if (@($value.Operations | Where-Object Outcome -eq "ROLLBACK_APPLIED").Count -ne 3) {
        throw "Action conflict rollback did not remove exactly three still-owned actions."
    }

    $preserved = @($value.Operations | Where-Object Outcome -eq "CONFLICT_ACTION_CHANGED")
    if ($preserved.Count -ne 1 -or
        $preserved[0].PlayniteId -ne "73000000-0000-4000-8000-000000000001" -or
        $preserved[0].SemanticKey -ne "media-open-book" -or
        $preserved[0].ActionName -ne "Read") {
        throw "Action conflict rollback did not preserve the externally changed Ebook Read action."
    }

    return $value
}

function Assert-FilterPresetReapply {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Schema -ne "sempersupra-media-library-enrichment-filter-preset-r4i/v1" -or
        $value.Mode -ne "apply" -or
        $value.CandidateCount -ne 3 -or
        $value.AppliedCount -ne 3 -or
        $value.NoopCount -ne 0 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "APPLIED").Count -ne 3) {
        throw "Filter-preset re-apply did not reproduce three clean native shelves."
    }

    return $value
}

function Assert-FilterPresetConflictReceipt {
    param([string]$Path, [ValidateSet("apply", "verify")][string]$ExpectedMode)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.schema -ne "sempersupra-playnite-r4i-filter-preset-conflict-fixture/v1" -or
        $value.mode -ne $ExpectedMode -or
        $value.result -ne "PASS" -or
        $value.preset_id -ne "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1501" -or
        $value.preset_name -ne "External Books Shelf" -or
        $value.category_id -ne "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1401" -or
        $value.category_reference_present -ne $true -or
        $value.category_present -ne $true) {
        throw "Filter-preset conflict fixture did not prove preserved external Books shelf state for '$ExpectedMode'."
    }

    return $value
}

function Assert-FilterPresetConflictRollback {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "rollback" -or
        $value.CandidateCount -ne 3 -or
        $value.RollbackAppliedCount -ne 2 -or
        $value.ConflictCount -ne 1) {
        throw "Filter-preset conflict rollback did not prove two owned removals plus one preserved external mutation."
    }

    if (@($value.Operations | Where-Object Outcome -eq "ROLLBACK_APPLIED").Count -ne 2) {
        throw "Filter-preset conflict rollback did not remove exactly two still-owned shelves."
    }

    $preserved = @($value.Operations | Where-Object Outcome -eq "CONFLICT_PRESET_CHANGED")
    if ($preserved.Count -ne 1 -or
        $preserved[0].PresetId -ne "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1501" -or
        $preserved[0].PresetName -ne "SemperSupra Media: Books") {
        throw "Filter-preset conflict rollback did not preserve the externally changed Books shelf."
    }

    return $value
}

function Assert-CoverReapply {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Schema -ne "sempersupra-media-library-enrichment-cover-r4i/v1" -or
        $value.Mode -ne "apply" -or
        $value.CandidateCount -ne 2 -or
        $value.AppliedCount -ne 2 -or
        $value.NoopCount -ne 0 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "APPLIED").Count -ne 2) {
        throw "Cover re-apply did not reproduce two clean native CoverImage mutations."
    }

    return $value
}

function Assert-CoverConflictReceipt {
    param([string]$Path, [ValidateSet("apply", "verify")][string]$ExpectedMode)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.schema -ne "sempersupra-playnite-r4i-cover-conflict-fixture/v1" -or
        $value.mode -ne $ExpectedMode -or
        $value.result -ne "PASS" -or
        $value.book_game_id -ne "73000000-0000-4000-8000-000000000001" -or
        $value.cover_image_present -ne $true -or
        $value.external_source_name -ne "external-book-cover.png") {
        throw "Cover conflict fixture did not prove preserved external Book cover state for '$ExpectedMode'."
    }

    return $value
}

function Assert-CoverConflictRollback {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.Mode -ne "rollback" -or
        $value.CandidateCount -ne 2 -or
        $value.RollbackAppliedCount -ne 1 -or
        $value.ConflictCount -ne 1) {
        throw "Cover conflict rollback did not prove one owned removal plus one preserved external mutation."
    }

    if (@($value.Operations | Where-Object Outcome -eq "ROLLBACK_APPLIED").Count -ne 1) {
        throw "Cover conflict rollback did not remove exactly one still-owned cover."
    }

    $preserved = @($value.Operations | Where-Object Outcome -eq "CONFLICT_COVER_CHANGED")
    if ($preserved.Count -ne 1 -or
        $preserved[0].PlayniteId -ne "73000000-0000-4000-8000-000000000001" -or
        $preserved[0].EvidenceKey -ne "rdte-book-cover-v1") {
        throw "Cover conflict rollback did not preserve the externally changed Ebook CoverImage."
    }

    return $value
}

function Native-InstallAndRun {
    param(
        [string]$PackagePath,
        [string]$ExpectedId,
        [string]$ExpectedName,
        [string]$ExpectedVersion,
        [string]$ReadyFile
    )

    Queue-Install -PackagePath $PackagePath
    if (Test-Path $logPath) {
        Remove-Item $logPath -Force
    }

    $process = Start-Playnite
    try {
        Wait-ForQueueConsumed -Process $process
        Wait-ForText -Path $logPath -Text "Loaded plugin: $ExpectedName, version $ExpectedVersion" -Process $process

        $installedDir = Find-InstalledExtension -ExpectedId $ExpectedId
        if (-not $installedDir) {
            throw "'$ExpectedName' did not materialize through native install."
        }

        if ($ReadyFile) {
            Wait-ForFile -Path $ReadyFile -Process $process
        }

        return [pscustomobject]@{
            Process = $process
            InstalledDir = $installedDir
        }
    }
    catch {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
        throw
    }
}

function Native-Uninstall {
    param([string]$InstalledDir)

    Queue-Uninstall -InstalledDir $InstalledDir
    $process = Start-Playnite
    try {
        Wait-ForQueueConsumed -Process $process

        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while ((Test-Path $InstalledDir) -and [DateTime]::UtcNow -lt $deadline) {
            if ($process.HasExited) {
                throw "Playnite exited before extension uninstall completed."
            }
            Start-Sleep -Milliseconds 250
        }
        if (Test-Path $InstalledDir) {
            throw "Extension directory remains after native uninstall: $InstalledDir"
        }
    }
    finally {
        if ($process -and -not $process.HasExited) {
            try { Stop-Playnite } catch { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
        }
    }
}

function Assert-NativePersistenceReceipt {
    param([string]$Path)

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    if ($value.schema -ne "sempersupra-playnite-native-persistence-fixture/v1" -or
        $value.result -ne "PASS" -or
        $value.category_memberships -ne 4 -or
        $value.custom_actions -ne 4 -or
        $value.filter_presets -ne 3 -or
        $value.cover_images -ne 2 -or
        $value.local_files_present -ne $true) {
        throw "Native persistence fixture did not prove the complete surviving enrichment set."
    }

    return $value
}

function Assert-PersistenceReconcile {
    param(
        [string]$CategoryPath,
        [string]$ActionPath,
        [string]$FilterPresetPath,
        [string]$CoverPath
    )

    $category = Get-Content $CategoryPath -Raw | ConvertFrom-Json
    $action = Get-Content $ActionPath -Raw | ConvertFrom-Json
    $filterPreset = Get-Content $FilterPresetPath -Raw | ConvertFrom-Json
    $cover = Get-Content $CoverPath -Raw | ConvertFrom-Json

    if ($category.Mode -ne "apply" -or
        $category.CandidateCount -ne 4 -or
        $category.AppliedCount -ne 0 -or
        $category.NoopCount -ne 4 -or
        $category.ConflictCount -ne 0 -or
        @($category.Operations | Where-Object Outcome -eq "NOOP").Count -ne 4) {
        throw "Reinstall did not reconcile surviving category memberships as four NOOPs."
    }

    if ($action.Mode -ne "apply" -or
        $action.CandidateCount -ne 4 -or
        $action.AppliedCount -ne 0 -or
        $action.NoopCount -ne 4 -or
        $action.ConflictCount -ne 0 -or
        @($action.Operations | Where-Object Outcome -eq "NOOP").Count -ne 4) {
        throw "Reinstall did not reconcile surviving custom actions as four NOOPs."
    }

    if ($filterPreset.Mode -ne "apply" -or
        $filterPreset.CandidateCount -ne 3 -or
        $filterPreset.AppliedCount -ne 0 -or
        $filterPreset.NoopCount -ne 3 -or
        $filterPreset.ConflictCount -ne 0 -or
        @($filterPreset.Operations | Where-Object Outcome -eq "NOOP").Count -ne 3) {
        throw "Reinstall did not reconcile surviving media shelves as three NOOPs."
    }

    if ($cover.Mode -ne "apply" -or
        $cover.CandidateCount -ne 2 -or
        $cover.AppliedCount -ne 0 -or
        $cover.NoopCount -ne 2 -or
        $cover.ConflictCount -ne 0 -or
        @($cover.Operations | Where-Object Outcome -eq "NOOP").Count -ne 2) {
        throw "Reinstall did not reconcile surviving CoverImage state as two NOOPs."
    }

    return [pscustomobject]@{
        CategoryPlanSha256 = $category.PlanSha256
        ActionPlanSha256 = $action.PlanSha256
        FilterPresetPlanSha256 = $filterPreset.PlanSha256
        CoverPlanSha256 = $cover.PlanSha256
    }
}

function Assert-UserCategoryOverrideReceipt {
    param(
        [string]$Path,
        [ValidateSet("remove", "verify", "restore")][string]$ExpectedMode
    )

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    $expectedMembership = $ExpectedMode -eq "restore"
    if ($value.schema -ne "sempersupra-playnite-user-category-override-fixture/v1" -or
        $value.mode -ne $ExpectedMode -or
        $value.result -ne "PASS" -or
        $value.book_game_id -ne "73000000-0000-4000-8000-000000000001" -or
        $value.category_name -ne $bookCategoryName -or
        $value.book_membership_present -ne $expectedMembership -or
        $value.other_managed_memberships -ne 3) {
        throw "Fixture seeder did not prove '$ExpectedMode' user-category override state."
    }

    return $value
}

function Assert-UserActionOverrideReceipt {
    param(
        [string]$Path,
        [ValidateSet("remove", "verify", "restore")][string]$ExpectedMode
    )

    $value = Get-Content $Path -Raw | ConvertFrom-Json
    $expectedAction = $ExpectedMode -eq "restore"
    if ($value.schema -ne "sempersupra-playnite-user-action-override-fixture/v1" -or
        $value.mode -ne $ExpectedMode -or
        $value.result -ne "PASS" -or
        $value.book_game_id -ne "73000000-0000-4000-8000-000000000001" -or
        $value.action_name -ne "Read" -or
        $value.book_action_present -ne $expectedAction -or
        $value.other_managed_actions -ne 3) {
        throw "Fixture seeder did not prove '$ExpectedMode' user-action override state."
    }

    return $value
}

function Assert-UserActionOverrideReconcile {
    param(
        [string]$ReceiptPath,
        [string]$LedgerPath
    )

    $reconcile = Get-Content $ReceiptPath -Raw | ConvertFrom-Json
    $overrideOperations = @($reconcile.Operations | Where-Object Outcome -eq "USER_OVERRIDE")
    $bookOverride = @($overrideOperations | Where-Object {
        $_.PlayniteId -eq "73000000-0000-4000-8000-000000000001" -and
        $_.SemanticKey -eq "media-open-book" -and
        $_.ActionName -eq "Read"
    })

    if ($reconcile.Mode -ne "apply" -or
        $reconcile.CandidateCount -ne 4 -or
        $reconcile.AppliedCount -ne 0 -or
        $reconcile.NoopCount -ne 3 -or
        $reconcile.UserOverrideCount -ne 1 -or
        $reconcile.ConflictCount -ne 0 -or
        $overrideOperations.Count -ne 1 -or
        $bookOverride.Count -ne 1) {
        throw "Product reconcile did not surface exactly one Ebook Read USER_OVERRIDE while leaving the other three managed actions as NOOP."
    }

    $ledger = Get-Content $LedgerPath -Raw | ConvertFrom-Json
    $overrideLedger = @($ledger.Entries | Where-Object {
        $_.PlayniteId -eq "73000000-0000-4000-8000-000000000001" -and
        $_.SemanticKey -eq "media-open-book" -and
        $_.ActionName -eq "Read" -and
        $_.Status -eq "USER_OVERRIDDEN"
    })
    $stillManaged = @($ledger.Entries | Where-Object {
        $_.Status -eq "COMMITTED" -and
        $_.PlayniteId -ne "73000000-0000-4000-8000-000000000001"
    })
    if ($overrideLedger.Count -ne 1 -or $stillManaged.Count -ne 3) {
        throw "Action ownership ledger did not persist one USER_OVERRIDDEN Ebook Read entry while leaving the other three actions COMMITTED."
    }

    return $reconcile
}

function Assert-UserCategoryOverrideReconcile {
    param(
        [string]$ReceiptPath,
        [string]$LedgerPath
    )

    $reconcile = Get-Content $ReceiptPath -Raw | ConvertFrom-Json
    $overrideOperations = @($reconcile.Operations | Where-Object Outcome -eq "USER_OVERRIDE")
    $bookOverride = @($overrideOperations | Where-Object {
        $_.PlayniteId -eq "73000000-0000-4000-8000-000000000001" -and
        $_.CategoryName -eq $bookCategoryName
    })

    if ($reconcile.Mode -ne "apply" -or
        $reconcile.CandidateCount -ne 4 -or
        $reconcile.AppliedCount -ne 0 -or
        $reconcile.NoopCount -ne 3 -or
        $reconcile.UserOverrideCount -ne 1 -or
        $reconcile.ConflictCount -ne 0 -or
        $overrideOperations.Count -ne 1 -or
        $bookOverride.Count -ne 1) {
        throw "Product reconcile did not surface exactly one Book USER_OVERRIDE while leaving the other three memberships as NOOP."
    }

    $ledger = Get-Content $LedgerPath -Raw | ConvertFrom-Json
    $overrideLedger = @($ledger.Entries | Where-Object {
        $_.PlayniteId -eq "73000000-0000-4000-8000-000000000001" -and
        $_.CategoryName -eq $bookCategoryName -and
        $_.Status -eq "USER_OVERRIDDEN"
    })
    if ($overrideLedger.Count -ne 1) {
        throw "Category ownership ledger did not persist USER_OVERRIDDEN for the externally removed Book membership."
    }

    return $reconcile
}

$receipt = [ordered]@{
    schema = "sempersupra-media-library-enrichment-r4i-conflict-rdte/v1"
    source_sha = if ($env:RDTE_SOURCE_SHA) { $env:RDTE_SOURCE_SHA } else { $env:GITHUB_SHA }
    product_authority_sha = $env:RDTE_PRODUCT_AUTHORITY_SHA
    phases = [ordered]@{
        product_present = "NOT_RUN"
        reapply = "NOT_RUN"
        native_persistence_uninstall = "NOT_RUN"
        native_persistence_verify = "NOT_RUN"
        native_persistence_reinstall = "NOT_RUN"
        native_persistence_reconcile = "NOT_RUN"
        user_category_override_remove = "NOT_RUN"
        user_category_override_reconcile = "NOT_RUN"
        user_category_override_verify = "NOT_RUN"
        user_category_override_restore = "NOT_RUN"
        user_action_override_remove = "NOT_RUN"
        user_action_override_reconcile = "NOT_RUN"
        user_action_override_verify = "NOT_RUN"
        user_action_override_restore = "NOT_RUN"
        external_conflict_inject = "NOT_RUN"
        conflict_rollback = "NOT_RUN"
        external_conflict_verify = "NOT_RUN"
        action_reapply = "NOT_RUN"
        action_conflict_inject = "NOT_RUN"
        action_conflict_rollback = "NOT_RUN"
        action_conflict_verify = "NOT_RUN"
        filter_preset_reapply = "NOT_RUN"
        filter_preset_conflict_inject = "NOT_RUN"
        filter_preset_conflict_rollback = "NOT_RUN"
        filter_preset_conflict_verify = "NOT_RUN"
        cover_reapply = "NOT_RUN"
        cover_conflict_inject = "NOT_RUN"
        cover_conflict_rollback = "NOT_RUN"
        cover_conflict_verify = "NOT_RUN"
        product_uninstall = "NOT_RUN"
        data_preservation = "NOT_RUN"
    }
    result = "RUNNING"
}

$productProcess = $null
try {
    $pluginReceipt = Get-Content (Join-Path $EvidenceDir "receipt.json") -Raw |
        ConvertFrom-Json
    $productVersion = $pluginReceipt.version

    $seederManifestYaml = $null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $seederZip = [System.IO.Compression.ZipFile]::OpenRead($seederPackage)
    try {
        $entry = $seederZip.GetEntry("extension.yaml")
        if (-not $entry) {
            throw "Fixture seeder package lacks extension.yaml."
        }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { $seederManifestYaml = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally {
        $seederZip.Dispose()
    }
    $seederVersionMatch = [regex]::Match(
        $seederManifestYaml,
        "(?m)^Version:\s*(?<value>\S+)\s*$")
    if (-not $seederVersionMatch.Success) {
        throw "Unable to resolve fixture seeder version."
    }
    $seederVersion = $seederVersionMatch.Groups["value"].Value.Trim()

    $productInstalledDir = Find-InstalledExtension -ExpectedId $pluginId
    if (-not $productInstalledDir) {
        throw "Media Library Enrichment was not left installed for the conflict rep."
    }
    $receipt.phases.product_present = "PASS"

    $seederData = Join-Path $userData "ExtensionsData\$seederDataId"
    New-Item $seederData -ItemType Directory -Force | Out-Null
    $profilePath = Join-Path $seederData "fixture-profile.txt"

    Set-ProductMode -Mode "apply"
    $r4iPath = Join-Path $userData "ExtensionsData\$pluginId\r4i-receipt.json"
    $actionR4iPath = Join-Path $userData "ExtensionsData\$pluginId\action-r4i-receipt.json"
    $filterPresetR4iPath = Join-Path $userData "ExtensionsData\$pluginId\filter-preset-r4i-receipt.json"
    $coverR4iPath = Join-Path $userData "ExtensionsData\$pluginId\cover-r4i-receipt.json"
    if (Test-Path $r4iPath) {
        Remove-Item $r4iPath -Force
    }
    if (Test-Path $logPath) {
        Remove-Item $logPath -Force
    }
    $productProcess = Start-Playnite
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $productVersion" -Process $productProcess
    Wait-ForFile -Path $r4iPath -Process $productProcess

    $reapply = Assert-Reapply -Path $r4iPath
    $receipt.reapply_plan_sha256 = $reapply.PlanSha256
    Copy-Item $r4iPath (Join-Path $EvidenceDir "r4i-conflict-reapply.json") -Force
    $receipt.phases.reapply = "PASS"

    Stop-Playnite
    $productProcess = $null

    # Release gate: native enrichment must remain useful after product uninstall
    # when rollback was not requested.
    Set-ProductMode -Mode "observe"
    Native-Uninstall -InstalledDir $productInstalledDir
    $receipt.phases.native_persistence_uninstall = "PASS"

    $pluginData = Join-Path $userData "ExtensionsData\$pluginId"
    foreach ($name in @(
        "settings.json",
        "admission-evidence.json",
        "category-ledger.json",
        "action-ledger.json",
        "filter-preset-ledger.json",
        "cover-evidence.json",
        "cover-ledger.json"
    )) {
        if (-not (Test-Path (Join-Path $pluginData $name) -PathType Leaf)) {
            throw "Plugin-owned reconciliation data did not survive native uninstall: $name"
        }
    }

    $nativePersistencePath = Join-Path $seederData "native-persistence-receipt.json"
    Set-Content -Path $profilePath -Value "r4i-native-persistence-verify-v1" -Encoding UTF8
    if (Test-Path $nativePersistencePath) {
        Remove-Item $nativePersistencePath -Force
    }

    $persistenceSeeder = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $nativePersistencePath
    $nativePersistence = Assert-NativePersistenceReceipt -Path $nativePersistencePath
    $receipt.native_persistence = [ordered]@{
        category_memberships = $nativePersistence.category_memberships
        custom_actions = $nativePersistence.custom_actions
        filter_presets = $nativePersistence.filter_presets
        cover_images = $nativePersistence.cover_images
        local_files_present = $nativePersistence.local_files_present
    }
    Copy-Item $nativePersistencePath (Join-Path $EvidenceDir "native-persistence-after-uninstall.json") -Force
    $receipt.phases.native_persistence_verify = "PASS"

    Stop-Playnite
    Native-Uninstall -InstalledDir $persistenceSeeder.InstalledDir

    Set-ProductMode -Mode "apply"
    foreach ($path in @($r4iPath, $actionR4iPath, $filterPresetR4iPath, $coverR4iPath)) {
        if (Test-Path $path) {
            Remove-Item $path -Force
        }
    }
    if (Test-Path $logPath) {
        Remove-Item $logPath -Force
    }

    $reinstalledProduct = Native-InstallAndRun -PackagePath $pluginPackage -ExpectedId $pluginId -ExpectedName $pluginName -ExpectedVersion $productVersion -ReadyFile $r4iPath
    $productInstalledDir = $reinstalledProduct.InstalledDir
    $productProcess = $reinstalledProduct.Process
    Wait-ForFile -Path $actionR4iPath -Process $productProcess
    Wait-ForFile -Path $filterPresetR4iPath -Process $productProcess
    Wait-ForFile -Path $coverR4iPath -Process $productProcess
    $receipt.phases.native_persistence_reinstall = "PASS"

    $persistenceReconcile = Assert-PersistenceReconcile -CategoryPath $r4iPath -ActionPath $actionR4iPath -FilterPresetPath $filterPresetR4iPath -CoverPath $coverR4iPath
    $receipt.native_persistence_reconcile_plan_sha256 = [ordered]@{
        category = $persistenceReconcile.CategoryPlanSha256
        action = $persistenceReconcile.ActionPlanSha256
        filter_preset = $persistenceReconcile.FilterPresetPlanSha256
        cover = $persistenceReconcile.CoverPlanSha256
    }
    Copy-Item $r4iPath (Join-Path $EvidenceDir "native-persistence-category-noop.json") -Force
    Copy-Item $actionR4iPath (Join-Path $EvidenceDir "native-persistence-action-noop.json") -Force
    Copy-Item $filterPresetR4iPath (Join-Path $EvidenceDir "native-persistence-filter-preset-noop.json") -Force
    Copy-Item $coverR4iPath (Join-Path $EvidenceDir "native-persistence-cover-noop.json") -Force
    $receipt.phases.native_persistence_reconcile = "PASS"

    Stop-Playnite
    $productProcess = $null

    # Release gate #4 falsification rep: user/tool removal of previously managed
    # native state must not be silently reasserted by normal apply.
    $categoryLedgerPath = Join-Path $userData "ExtensionsData\$pluginId\category-ledger.json"
    $categoryLedgerBackup = Join-Path $EvidenceDir "category-ledger-before-user-override.json"
    Copy-Item $categoryLedgerPath $categoryLedgerBackup -Force

    Set-ProductMode -Mode "observe"
    $userOverridePath = Join-Path $seederData "user-category-override-receipt.json"
    Set-Content -Path $profilePath -Value "r4i-user-category-override-remove-v1" -Encoding UTF8
    if (Test-Path $userOverridePath) { Remove-Item $userOverridePath -Force }
    $userOverrideSeeder = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $userOverridePath
    Assert-UserCategoryOverrideReceipt -Path $userOverridePath -ExpectedMode "remove" | Out-Null
    Copy-Item $userOverridePath (Join-Path $EvidenceDir "user-category-override-removed.json") -Force
    $receipt.phases.user_category_override_remove = "PASS"
    Stop-Playnite
    Native-Uninstall -InstalledDir $userOverrideSeeder.InstalledDir

    Set-ProductMode -Mode "apply"
    if (Test-Path $r4iPath) { Remove-Item $r4iPath -Force }
    if (Test-Path $logPath) { Remove-Item $logPath -Force }
    $productProcess = Start-Playnite
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $productVersion" -Process $productProcess
    Wait-ForFile -Path $r4iPath -Process $productProcess
    $userOverrideReconcile = Assert-UserCategoryOverrideReconcile -ReceiptPath $r4iPath -LedgerPath $categoryLedgerPath
    Copy-Item $r4iPath (Join-Path $EvidenceDir "user-category-override-reconcile.json") -Force
    Copy-Item $categoryLedgerPath (Join-Path $EvidenceDir "category-ledger-after-user-override.json") -Force
    $receipt.user_category_override_reconcile_plan_sha256 =
        $userOverrideReconcile.PlanSha256
    $receipt.phases.user_category_override_reconcile = "PASS"
    Stop-Playnite
    $productProcess = $null

    Set-ProductMode -Mode "observe"
    Set-Content -Path $profilePath -Value "r4i-user-category-override-verify-v1" -Encoding UTF8
    if (Test-Path $userOverridePath) { Remove-Item $userOverridePath -Force }
    $userOverrideVerifySeeder = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $userOverridePath
    Assert-UserCategoryOverrideReceipt -Path $userOverridePath -ExpectedMode "verify" | Out-Null
    Copy-Item $userOverridePath (Join-Path $EvidenceDir "user-category-override-verified.json") -Force
    $receipt.phases.user_category_override_verify = "PASS"
    Stop-Playnite
    Native-Uninstall -InstalledDir $userOverrideVerifySeeder.InstalledDir

    Set-Content -Path $profilePath -Value "r4i-user-category-override-restore-v1" -Encoding UTF8
    if (Test-Path $userOverridePath) { Remove-Item $userOverridePath -Force }
    $userOverrideRestoreSeeder = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $userOverridePath
    Assert-UserCategoryOverrideReceipt -Path $userOverridePath -ExpectedMode "restore" | Out-Null
    $receipt.phases.user_category_override_restore = "PASS"
    Stop-Playnite
    Native-Uninstall -InstalledDir $userOverrideRestoreSeeder.InstalledDir
    Copy-Item $categoryLedgerBackup $categoryLedgerPath -Force

    # Continue release gate #4 falsification with the smallest next owned
    # native object: complete external deletion of the managed Ebook Read action.
    $actionLedgerPath = Join-Path $userData "ExtensionsData\$pluginId\action-ledger.json"
    $actionLedgerBackup = Join-Path $EvidenceDir "action-ledger-before-user-override.json"
    Copy-Item $actionLedgerPath $actionLedgerBackup -Force

    Set-ProductMode -Mode "observe"
    $userActionOverridePath = Join-Path $seederData "user-action-override-receipt.json"
    Set-Content -Path $profilePath -Value "r4i-user-action-override-remove-v1" -Encoding UTF8
    if (Test-Path $userActionOverridePath) { Remove-Item $userActionOverridePath -Force }
    $userActionOverrideSeeder = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $userActionOverridePath
    Assert-UserActionOverrideReceipt -Path $userActionOverridePath -ExpectedMode "remove" | Out-Null
    Copy-Item $userActionOverridePath (Join-Path $EvidenceDir "user-action-override-removed.json") -Force
    $receipt.phases.user_action_override_remove = "PASS"
    Stop-Playnite
    Native-Uninstall -InstalledDir $userActionOverrideSeeder.InstalledDir

    Set-ProductMode -Mode "apply"
    if (Test-Path $actionR4iPath) { Remove-Item $actionR4iPath -Force }
    if (Test-Path $logPath) { Remove-Item $logPath -Force }
    $productProcess = Start-Playnite
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $productVersion" -Process $productProcess
    Wait-ForFile -Path $actionR4iPath -Process $productProcess
    $userActionOverrideReconcile = Assert-UserActionOverrideReconcile -ReceiptPath $actionR4iPath -LedgerPath $actionLedgerPath
    Copy-Item $actionR4iPath (Join-Path $EvidenceDir "user-action-override-reconcile.json") -Force
    Copy-Item $actionLedgerPath (Join-Path $EvidenceDir "action-ledger-after-user-override-reconcile.json") -Force
    $receipt.user_action_override_reconcile_plan_sha256 =
        $userActionOverrideReconcile.PlanSha256
    $receipt.phases.user_action_override_reconcile = "PASS"
    Stop-Playnite
    $productProcess = $null

    Set-ProductMode -Mode "observe"
    Set-Content -Path $profilePath -Value "r4i-user-action-override-verify-v1" -Encoding UTF8
    if (Test-Path $userActionOverridePath) { Remove-Item $userActionOverridePath -Force }
    $userActionOverrideVerifySeeder = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $userActionOverridePath
    Assert-UserActionOverrideReceipt -Path $userActionOverridePath -ExpectedMode "verify" | Out-Null
    Copy-Item $userActionOverridePath (Join-Path $EvidenceDir "user-action-override-verified.json") -Force
    $receipt.phases.user_action_override_verify = "PASS"
    Stop-Playnite
    Native-Uninstall -InstalledDir $userActionOverrideVerifySeeder.InstalledDir

    Set-Content -Path $profilePath -Value "r4i-user-action-override-restore-v1" -Encoding UTF8
    if (Test-Path $userActionOverridePath) { Remove-Item $userActionOverridePath -Force }
    $userActionOverrideRestoreSeeder = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $userActionOverridePath
    Assert-UserActionOverrideReceipt -Path $userActionOverridePath -ExpectedMode "restore" | Out-Null
    $receipt.phases.user_action_override_restore = "PASS"
    Stop-Playnite
    Native-Uninstall -InstalledDir $userActionOverrideRestoreSeeder.InstalledDir
    Copy-Item $actionLedgerBackup $actionLedgerPath -Force

    Set-ProductMode -Mode "observe"

    $conflictPath = Join-Path $seederData "conflict-receipt.json"
    Set-Content -Path $profilePath -Value "r4i-conflict-apply-v1" -Encoding UTF8
    if (Test-Path $conflictPath) {
        Remove-Item $conflictPath -Force
    }

    $seederInstall = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $conflictPath
    Assert-ConflictReceipt -Path $conflictPath -ExpectedMode "apply" | Out-Null
    Copy-Item $conflictPath (Join-Path $EvidenceDir "external-conflict-applied.json") -Force
    $receipt.phases.external_conflict_inject = "PASS"

    Stop-Playnite
    Native-Uninstall -InstalledDir $seederInstall.InstalledDir

    Set-ProductMode -Mode "rollback"
    if (Test-Path $r4iPath) {
        Remove-Item $r4iPath -Force
    }

    if (Test-Path $logPath) {
        Remove-Item $logPath -Force
    }
    $productProcess = Start-Playnite
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $productVersion" -Process $productProcess
    Wait-ForFile -Path $r4iPath -Process $productProcess

    $conflictRollback = Assert-ConflictRollback -Path $r4iPath
    $receipt.conflict_rollback_plan_sha256 = $conflictRollback.PlanSha256
    Copy-Item $r4iPath (Join-Path $EvidenceDir "r4i-conflict-rollback.json") -Force
    $receipt.phases.conflict_rollback = "PASS"

    Stop-Playnite
    $productProcess = $null

    Set-ProductMode -Mode "observe"
    Set-Content -Path $profilePath -Value "r4i-conflict-verify-v1" -Encoding UTF8
    if (Test-Path $conflictPath) {
        Remove-Item $conflictPath -Force
    }

    $seederVerify = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $conflictPath
    Assert-ConflictReceipt -Path $conflictPath -ExpectedMode "verify" | Out-Null
    Copy-Item $conflictPath (Join-Path $EvidenceDir "external-conflict-verified.json") -Force
    $receipt.phases.external_conflict_verify = "PASS"

    Stop-Playnite
    Native-Uninstall -InstalledDir $seederVerify.InstalledDir

    Set-ProductMode -Mode "apply"
    $actionR4iPath = Join-Path $userData "ExtensionsData\$pluginId\action-r4i-receipt.json"
    if (Test-Path $r4iPath) {
        Remove-Item $r4iPath -Force
    }
    if (Test-Path $actionR4iPath) {
        Remove-Item $actionR4iPath -Force
    }
    if (Test-Path $logPath) {
        Remove-Item $logPath -Force
    }

    $productProcess = Start-Playnite
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $productVersion" -Process $productProcess
    Wait-ForFile -Path $actionR4iPath -Process $productProcess
    $actionReapply = Assert-ActionReapply -Path $actionR4iPath
    $receipt.action_reapply_plan_sha256 = $actionReapply.PlanSha256
    Copy-Item $actionR4iPath (Join-Path $EvidenceDir "action-conflict-reapply.json") -Force
    $receipt.phases.action_reapply = "PASS"

    Stop-Playnite
    $productProcess = $null
    Set-ProductMode -Mode "observe"

    Set-Content -Path $profilePath -Value "r4i-action-conflict-apply-v1" -Encoding UTF8
    $actionConflictPath = Join-Path $seederData "action-conflict-receipt.json"
    if (Test-Path $actionConflictPath) {
        Remove-Item $actionConflictPath -Force
    }

    $actionSeederInstall = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $actionConflictPath
    Assert-ActionConflictReceipt -Path $actionConflictPath -ExpectedMode "apply" | Out-Null
    Copy-Item $actionConflictPath (Join-Path $EvidenceDir "external-action-conflict-applied.json") -Force
    $receipt.phases.action_conflict_inject = "PASS"

    Stop-Playnite
    Native-Uninstall -InstalledDir $actionSeederInstall.InstalledDir

    Set-ProductMode -Mode "rollback"
    if (Test-Path $r4iPath) {
        Remove-Item $r4iPath -Force
    }
    if (Test-Path $actionR4iPath) {
        Remove-Item $actionR4iPath -Force
    }
    if (Test-Path $logPath) {
        Remove-Item $logPath -Force
    }

    $productProcess = Start-Playnite
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $productVersion" -Process $productProcess
    Wait-ForFile -Path $actionR4iPath -Process $productProcess
    $actionConflictRollback = Assert-ActionConflictRollback -Path $actionR4iPath
    $receipt.action_conflict_rollback_plan_sha256 = $actionConflictRollback.PlanSha256
    Copy-Item $actionR4iPath (Join-Path $EvidenceDir "action-conflict-rollback.json") -Force
    $receipt.phases.action_conflict_rollback = "PASS"

    Stop-Playnite
    $productProcess = $null
    Set-ProductMode -Mode "observe"

    Set-Content -Path $profilePath -Value "r4i-action-conflict-verify-v1" -Encoding UTF8
    if (Test-Path $actionConflictPath) {
        Remove-Item $actionConflictPath -Force
    }

    $actionSeederVerify = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $actionConflictPath
    Assert-ActionConflictReceipt -Path $actionConflictPath -ExpectedMode "verify" | Out-Null
    Copy-Item $actionConflictPath (Join-Path $EvidenceDir "external-action-conflict-verified.json") -Force
    $receipt.phases.action_conflict_verify = "PASS"

    Stop-Playnite
    Native-Uninstall -InstalledDir $actionSeederVerify.InstalledDir

    Set-ProductMode -Mode "apply"
    $filterPresetR4iPath = Join-Path $userData "ExtensionsData\$pluginId\filter-preset-r4i-receipt.json"
    if (Test-Path $r4iPath) {
        Remove-Item $r4iPath -Force
    }
    if (Test-Path $actionR4iPath) {
        Remove-Item $actionR4iPath -Force
    }
    if (Test-Path $filterPresetR4iPath) {
        Remove-Item $filterPresetR4iPath -Force
    }
    if (Test-Path $logPath) {
        Remove-Item $logPath -Force
    }

    $productProcess = Start-Playnite
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $productVersion" -Process $productProcess
    Wait-ForFile -Path $filterPresetR4iPath -Process $productProcess
    $filterPresetReapply = Assert-FilterPresetReapply -Path $filterPresetR4iPath
    $receipt.filter_preset_reapply_plan_sha256 = $filterPresetReapply.PlanSha256
    Copy-Item $filterPresetR4iPath (Join-Path $EvidenceDir "filter-preset-conflict-reapply.json") -Force
    $receipt.phases.filter_preset_reapply = "PASS"

    Stop-Playnite
    $productProcess = $null
    Set-ProductMode -Mode "observe"

    Set-Content -Path $profilePath -Value "r4i-filter-preset-conflict-apply-v1" -Encoding UTF8
    $filterPresetConflictPath = Join-Path $seederData "filter-preset-conflict-receipt.json"
    if (Test-Path $filterPresetConflictPath) {
        Remove-Item $filterPresetConflictPath -Force
    }

    $filterPresetSeederInstall = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $filterPresetConflictPath
    Assert-FilterPresetConflictReceipt -Path $filterPresetConflictPath -ExpectedMode "apply" | Out-Null
    Copy-Item $filterPresetConflictPath (Join-Path $EvidenceDir "external-filter-preset-conflict-applied.json") -Force
    $receipt.phases.filter_preset_conflict_inject = "PASS"

    Stop-Playnite
    Native-Uninstall -InstalledDir $filterPresetSeederInstall.InstalledDir

    Set-ProductMode -Mode "rollback"
    foreach ($path in @($r4iPath, $actionR4iPath, $filterPresetR4iPath)) {
        if (Test-Path $path) {
            Remove-Item $path -Force
        }
    }
    if (Test-Path $logPath) {
        Remove-Item $logPath -Force
    }

    $productProcess = Start-Playnite
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $productVersion" -Process $productProcess
    Wait-ForFile -Path $filterPresetR4iPath -Process $productProcess
    Wait-ForFile -Path $r4iPath -Process $productProcess

    $filterPresetConflictRollback = Assert-FilterPresetConflictRollback -Path $filterPresetR4iPath
    $receipt.filter_preset_conflict_rollback_plan_sha256 = $filterPresetConflictRollback.PlanSha256
    Copy-Item $filterPresetR4iPath (Join-Path $EvidenceDir "filter-preset-conflict-rollback.json") -Force

    $filterPresetCategoryRollback = Assert-ConflictRollback -Path $r4iPath
    $receipt.filter_preset_category_conflict_rollback_plan_sha256 = $filterPresetCategoryRollback.PlanSha256
    Copy-Item $r4iPath (Join-Path $EvidenceDir "filter-preset-category-conflict-rollback.json") -Force
    $receipt.phases.filter_preset_conflict_rollback = "PASS"

    Stop-Playnite
    $productProcess = $null
    Set-ProductMode -Mode "observe"

    Set-Content -Path $profilePath -Value "r4i-filter-preset-conflict-verify-v1" -Encoding UTF8
    if (Test-Path $filterPresetConflictPath) {
        Remove-Item $filterPresetConflictPath -Force
    }

    $filterPresetSeederVerify = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $filterPresetConflictPath
    Assert-FilterPresetConflictReceipt -Path $filterPresetConflictPath -ExpectedMode "verify" | Out-Null
    Copy-Item $filterPresetConflictPath (Join-Path $EvidenceDir "external-filter-preset-conflict-verified.json") -Force
    $receipt.phases.filter_preset_conflict_verify = "PASS"

    Stop-Playnite
    Native-Uninstall -InstalledDir $filterPresetSeederVerify.InstalledDir

    Set-ProductMode -Mode "apply"
    $coverR4iPath = Join-Path $userData "ExtensionsData\$pluginId\cover-r4i-receipt.json"
    if (Test-Path $r4iPath) {
        Remove-Item $r4iPath -Force
    }
    if (Test-Path $actionR4iPath) {
        Remove-Item $actionR4iPath -Force
    }
    if (Test-Path $filterPresetR4iPath) {
        Remove-Item $filterPresetR4iPath -Force
    }
    if (Test-Path $coverR4iPath) {
        Remove-Item $coverR4iPath -Force
    }
    if (Test-Path $logPath) {
        Remove-Item $logPath -Force
    }

    $productProcess = Start-Playnite
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $productVersion" -Process $productProcess
    Wait-ForFile -Path $coverR4iPath -Process $productProcess
    $coverReapply = Assert-CoverReapply -Path $coverR4iPath
    $receipt.cover_reapply_plan_sha256 = $coverReapply.PlanSha256
    Copy-Item $coverR4iPath (Join-Path $EvidenceDir "cover-conflict-reapply.json") -Force
    $receipt.phases.cover_reapply = "PASS"

    Stop-Playnite
    $productProcess = $null
    Set-ProductMode -Mode "observe"

    Set-Content -Path $profilePath -Value "r4i-cover-conflict-apply-v1" -Encoding UTF8
    $coverConflictPath = Join-Path $seederData "cover-conflict-receipt.json"
    if (Test-Path $coverConflictPath) {
        Remove-Item $coverConflictPath -Force
    }

    $coverSeederInstall = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $coverConflictPath
    Assert-CoverConflictReceipt -Path $coverConflictPath -ExpectedMode "apply" | Out-Null
    Copy-Item $coverConflictPath (Join-Path $EvidenceDir "external-cover-conflict-applied.json") -Force
    $receipt.phases.cover_conflict_inject = "PASS"

    Stop-Playnite
    Native-Uninstall -InstalledDir $coverSeederInstall.InstalledDir

    Set-ProductMode -Mode "rollback"
    foreach ($path in @($r4iPath, $actionR4iPath, $filterPresetR4iPath, $coverR4iPath)) {
        if (Test-Path $path) {
            Remove-Item $path -Force
        }
    }
    if (Test-Path $logPath) {
        Remove-Item $logPath -Force
    }

    $productProcess = Start-Playnite
    Wait-ForText -Path $logPath -Text "Loaded plugin: $pluginName, version $productVersion" -Process $productProcess
    Wait-ForFile -Path $coverR4iPath -Process $productProcess

    $coverConflictRollback = Assert-CoverConflictRollback -Path $coverR4iPath
    $receipt.cover_conflict_rollback_plan_sha256 = $coverConflictRollback.PlanSha256
    Copy-Item $coverR4iPath (Join-Path $EvidenceDir "cover-conflict-rollback.json") -Force
    $receipt.phases.cover_conflict_rollback = "PASS"

    Stop-Playnite
    $productProcess = $null
    Set-ProductMode -Mode "observe"

    Set-Content -Path $profilePath -Value "r4i-cover-conflict-verify-v1" -Encoding UTF8
    if (Test-Path $coverConflictPath) {
        Remove-Item $coverConflictPath -Force
    }

    $coverSeederVerify = Native-InstallAndRun -PackagePath $seederPackage -ExpectedId $seederId -ExpectedName $seederName -ExpectedVersion $seederVersion -ReadyFile $coverConflictPath
    Assert-CoverConflictReceipt -Path $coverConflictPath -ExpectedMode "verify" | Out-Null
    Copy-Item $coverConflictPath (Join-Path $EvidenceDir "external-cover-conflict-verified.json") -Force
    $receipt.phases.cover_conflict_verify = "PASS"

    Stop-Playnite
    Native-Uninstall -InstalledDir $coverSeederVerify.InstalledDir

    Native-Uninstall -InstalledDir $productInstalledDir
    $receipt.phases.product_uninstall = "PASS"

    $pluginData = Join-Path $userData "ExtensionsData\$pluginId"
    foreach ($name in @(
        "settings.json",
        "admission-evidence.json",
        "observation-receipt.json",
        "r4i-receipt.json",
        "category-ledger.json",
        "action-r4i-receipt.json",
        "action-ledger.json",
        "filter-preset-r4i-receipt.json",
        "filter-preset-ledger.json",
        "cover-evidence.json",
        "cover-r4i-receipt.json",
        "cover-ledger.json"
    )) {
        if (-not (Test-Path (Join-Path $pluginData $name) -PathType Leaf)) {
            throw "Persistent plugin data did not survive final native uninstall: $name"
        }
    }
    $receipt.phases.data_preservation = "PASS"

    $receipt.result = "PASS"
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
        ConvertTo-Json -Depth 12 |
        Set-Content -Path (Join-Path $EvidenceDir "r4i-conflict-qualification-receipt.json") -Encoding UTF8
}
