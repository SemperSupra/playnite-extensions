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
            if ($content.Contains($Text)) {
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
        $value.CandidateCount -ne 3 -or
        $value.AppliedCount -ne 3 -or
        $value.NoopCount -ne 0 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "APPLIED").Count -ne 3) {
        throw "R4I re-apply did not reproduce three clean owned mutations."
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
        $value.CandidateCount -ne 3 -or
        $value.RollbackAppliedCount -ne 3 -or
        $value.ConflictCount -ne 1) {
        throw "Conflict rollback did not prove three owned reversions plus one preserved external conflict."
    }

    if (@($value.Operations | Where-Object Outcome -eq "ROLLBACK_APPLIED").Count -ne 3) {
        throw "Conflict rollback did not remove exactly three owned memberships."
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
        $value.CandidateCount -ne 3 -or
        $value.AppliedCount -ne 3 -or
        $value.NoopCount -ne 0 -or
        $value.ConflictCount -ne 0 -or
        @($value.Operations | Where-Object Outcome -eq "APPLIED").Count -ne 3) {
        throw "Action re-apply did not reproduce three clean custom actions."
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
        $value.CandidateCount -ne 3 -or
        $value.RollbackAppliedCount -ne 2 -or
        $value.ConflictCount -ne 1) {
        throw "Action conflict rollback did not prove two owned removals plus one preserved external mutation."
    }

    if (@($value.Operations | Where-Object Outcome -eq "ROLLBACK_APPLIED").Count -ne 2) {
        throw "Action conflict rollback did not remove exactly two still-owned actions."
    }

    $preserved = @($value.Operations | Where-Object Outcome -eq "CONFLICT_ACTION_CHANGED")
    if ($preserved.Count -ne 1 -or
        $preserved[0].ActionName -ne "Read" -or
        $preserved[0].Name -ne "RDTE Humble Ebook") {
        throw "Action conflict rollback did not preserve the externally changed Ebook Read action."
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

$receipt = [ordered]@{
    schema = "sempersupra-media-library-enrichment-r4i-conflict-rdte/v1"
    source_sha = if ($env:RDTE_SOURCE_SHA) { $env:RDTE_SOURCE_SHA } else { $env:GITHUB_SHA }
    product_authority_sha = $env:RDTE_PRODUCT_AUTHORITY_SHA
    phases = [ordered]@{
        product_present = "NOT_RUN"
        reapply = "NOT_RUN"
        external_conflict_inject = "NOT_RUN"
        conflict_rollback = "NOT_RUN"
        external_conflict_verify = "NOT_RUN"
        action_reapply = "NOT_RUN"
        action_conflict_inject = "NOT_RUN"
        action_conflict_rollback = "NOT_RUN"
        action_conflict_verify = "NOT_RUN"
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

    Set-ProductMode -Mode "apply"
    $r4iPath = Join-Path $userData "ExtensionsData\$pluginId\r4i-receipt.json"
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
    Set-ProductMode -Mode "observe"

    $seederData = Join-Path $userData "ExtensionsData\$seederDataId"
    New-Item $seederData -ItemType Directory -Force | Out-Null
    $profilePath = Join-Path $seederData "fixture-profile.txt"
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

    Native-Uninstall -InstalledDir $productInstalledDir
    $receipt.phases.product_uninstall = "PASS"

    $pluginData = Join-Path $userData "ExtensionsData\$pluginId"
    foreach ($name in @(
        "settings.json",
        "observation-receipt.json",
        "r4i-receipt.json",
        "category-ledger.json",
        "action-r4i-receipt.json",
        "action-ledger.json"
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
