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

$productId = "4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377"
$uriSource = "sempersupra-mle"
$userData = Join-Path $WorkRoot "userdata"
$runtimeDir = Join-Path $WorkRoot "runtime"
$productData = Join-Path $userData "ExtensionsData\$productId"
$settingsPath = Join-Path $productData "settings.json"
$uriReceiptPath = Join-Path $productData "uri-command-receipt.json"
$categoryReceiptPath = Join-Path $productData "r4i-receipt.json"
$actionReceiptPath = Join-Path $productData "action-r4i-receipt.json"
$filterReceiptPath = Join-Path $productData "filter-preset-r4i-receipt.json"
$coverReceiptPath = Join-Path $productData "cover-r4i-receipt.json"
$managedPaths = @(
    $settingsPath,
    (Join-Path $productData "category-ledger.json"),
    (Join-Path $productData "action-ledger.json"),
    (Join-Path $productData "filter-preset-ledger.json"),
    (Join-Path $productData "cover-ledger.json")
)

New-Item $productData, $EvidenceDir -ItemType Directory -Force | Out-Null

$desktop = @(Get-ChildItem $runtimeDir -Filter "Playnite.DesktopApp.exe" -File -Recurse)
if ($desktop.Count -ne 1) {
    throw "Expected exactly one Playnite.DesktopApp.exe."
}
$desktopExe = $desktop[0].FullName
$logPath = Join-Path $userData "playnite.log"

function Set-ProductMode {
    param([ValidateSet("observe", "apply", "rollback")][string]$Mode)

    [ordered]@{ Mode = $Mode } |
        ConvertTo-Json -Depth 4 |
        Set-Content -Path $settingsPath -Encoding UTF8
}

function Start-Playnite {
    Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path -Parent $desktopExe) -ArgumentList @(
        "--userdatadir", $userData,
        "--nolibupdate",
        "--hidesplashscreen",
        "--forcedefaulttheme",
        "--forcesoftrender"
    ) -PassThru
}

function Start-PlayniteWithUri {
    param([Parameter(Mandatory = $true)][string]$Uri)

    Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path -Parent $desktopExe) -ArgumentList @(
        "--userdatadir", $userData,
        "--uridata", $Uri,
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
            $_.ProcessName -like "Playnite.FullscreenApp*" -or
            $_.ProcessName -like "Playnite.BrowserProcess*"
        })
        if ($running.Count -eq 0) {
            return
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Playnite did not stop cleanly before URI qualification timeout."
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

    throw "Playnite runtime was not quiescent before URI qualification."
}

function Wait-ForText {
    param([string]$Path, [string]$Text, $Process)

    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "Playnite exited while waiting for '$Text'."
        }
        if (Test-Path $Path -PathType Leaf) {
            $value = Get-Content $Path -Raw
            if ($null -ne $value -and $value.Contains($Text)) {
                return
            }
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for '$Text' in '$Path'."
}

function Wait-ForFile {
    param([string]$Path, $Process)

    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "Playnite exited while waiting for '$Path'."
        }
        if (Test-Path $Path -PathType Leaf) {
            return
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for '$Path'."
}

function Get-ManagedFingerprint {
    $values = [ordered]@{}
    foreach ($path in $managedPaths) {
        $name = Split-Path $path -Leaf
        $values[$name] = if (Test-Path $path -PathType Leaf) {
            (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        else {
            "MISSING"
        }
    }

    return ($values | ConvertTo-Json -Compress)
}

function Read-MleUriReceipt {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedCommand,
        [Parameter(Mandatory = $true)][ValidateSet("PASS", "FAIL")][string]$ExpectedResult,
        [Parameter(Mandatory = $true)][string]$EvidenceName,
        $Process
    )

    Wait-ForFile -Path $uriReceiptPath -Process $Process
    $receipt = Get-Content $uriReceiptPath -Raw | ConvertFrom-Json

    if ($receipt.schema -ne "sempersupra-media-library-enrichment-uri-command/v1" -or
        $receipt.source -ne $uriSource -or
        $receipt.command -ne $ExpectedCommand -or
        $receipt.result -ne $ExpectedResult) {
        throw "Unexpected URI command receipt."
    }

    if (-not ($receipt.playnite_version -like "10.62*")) {
        throw "URI receipt is not bound to Playnite 10.62."
    }
    if (-not ($receipt.sdk_version -like "6.16*")) {
        throw "URI receipt is not bound to SDK 6.16."
    }

    Copy-Item $uriReceiptPath (Join-Path $EvidenceDir $EvidenceName) -Force
    return $receipt
}

function Invoke-MleUri {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$ExpectedCommand,
        [Parameter(Mandatory = $true)][ValidateSet("PASS", "FAIL")][string]$ExpectedResult,
        [Parameter(Mandatory = $true)][string]$EvidenceName,
        $Process
    )

    if (Test-Path $uriReceiptPath) {
        Remove-Item $uriReceiptPath -Force
    }

    $sender = Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path -Parent $desktopExe) -ArgumentList @(
        "--userdatadir", $userData,
        "--uridata", $Uri
    ) -PassThru

    if (-not $sender.WaitForExit(30000)) {
        try { Stop-Process -Id $sender.Id -Force -ErrorAction SilentlyContinue } catch {}
        throw "URI sender did not exit after forwarding '$Uri'."
    }

    return Read-MleUriReceipt -ExpectedCommand $ExpectedCommand -ExpectedResult $ExpectedResult -EvidenceName $EvidenceName -Process $Process
}

function Assert-ApplyCounts {
    $category = Get-Content $categoryReceiptPath -Raw | ConvertFrom-Json
    $action = Get-Content $actionReceiptPath -Raw | ConvertFrom-Json
    $filter = Get-Content $filterReceiptPath -Raw | ConvertFrom-Json
    $cover = Get-Content $coverReceiptPath -Raw | ConvertFrom-Json

    if ($category.Mode -ne "apply" -or $category.AppliedCount -ne 4 -or
        $action.Mode -ne "apply" -or $action.AppliedCount -ne 4 -or
        $filter.Mode -ne "apply" -or $filter.AppliedCount -ne 3 -or
        $cover.Mode -ne "apply" -or $cover.AppliedCount -ne 2) {
        throw "URI apply did not materialize the expected 13 owned projections."
    }
}

function Assert-ObserveReceipts {
    foreach ($path in @(
        $categoryReceiptPath,
        $actionReceiptPath,
        $filterReceiptPath,
        $coverReceiptPath)) {
        $value = Get-Content $path -Raw | ConvertFrom-Json
        if ($value.Mode -ne "observe") {
            throw "URI observe did not leave an observe receipt."
        }
    }
}

function Assert-RollbackCounts {
    $category = Get-Content $categoryReceiptPath -Raw | ConvertFrom-Json
    $action = Get-Content $actionReceiptPath -Raw | ConvertFrom-Json
    $filter = Get-Content $filterReceiptPath -Raw | ConvertFrom-Json
    $cover = Get-Content $coverReceiptPath -Raw | ConvertFrom-Json

    if ($category.Mode -ne "rollback" -or $category.RollbackAppliedCount -ne 4 -or
        $action.Mode -ne "rollback" -or $action.RollbackAppliedCount -ne 4 -or
        $filter.Mode -ne "rollback" -or $filter.RollbackAppliedCount -ne 3 -or
        $cover.Mode -ne "rollback" -or $cover.RollbackAppliedCount -ne 2) {
        throw "URI rollback did not remove the expected 13 still-owned projections."
    }
}

$process = $null
try {
    Wait-ForPlayniteQuiescence
    Set-ProductMode -Mode "observe"
    if (Test-Path $logPath) {
        Remove-Item $logPath -Force
    }

    $before = Get-ManagedFingerprint
    if (Test-Path $uriReceiptPath) {
        Remove-Item $uriReceiptPath -Force
    }
    $process = Start-PlayniteWithUri -Uri "playnite://sempersupra-mle/preview"
    Wait-ForText -Path $logPath -Text "Loaded plugin: Media Library Enrichment, version 0.1.0" -Process $process
    $preview = Read-MleUriReceipt -ExpectedCommand "preview" -ExpectedResult "PASS" -EvidenceName "uri-preview-cold-start-receipt.json" -Process $process
    if (-not $preview.summary.Contains("No changes were applied.")) {
        throw "Cold-start URI preview did not return the qualified preview summary."
    }
    if ((Get-ManagedFingerprint) -ne $before) {
        throw "Cold-start URI preview mutated managed state."
    }

    $unknown = Invoke-MleUri -Uri "playnite://sempersupra-mle/not-a-command" -ExpectedCommand "not-a-command" -ExpectedResult "FAIL" -EvidenceName "uri-unknown-receipt.json" -Process $process
    if ((Get-ManagedFingerprint) -ne $before) {
        throw "Unknown URI command mutated managed state."
    }

    $surplus = Invoke-MleUri -Uri "playnite://sempersupra-mle/apply/surplus" -ExpectedCommand "apply" -ExpectedResult "FAIL" -EvidenceName "uri-surplus-receipt.json" -Process $process
    if ((Get-ManagedFingerprint) -ne $before) {
        throw "Surplus URI arguments mutated managed state."
    }

    $preview = Invoke-MleUri -Uri "playnite://sempersupra-mle/preview" -ExpectedCommand "preview" -ExpectedResult "PASS" -EvidenceName "uri-preview-running-receipt.json" -Process $process
    if (-not $preview.summary.Contains("No changes were applied.")) {
        throw "Running-instance URI preview did not return the qualified preview summary."
    }
    if ((Get-ManagedFingerprint) -ne $before) {
        throw "Running-instance URI preview mutated managed state."
    }

    $apply = Invoke-MleUri -Uri "playnite://sempersupra-mle/apply" -ExpectedCommand "apply" -ExpectedResult "PASS" -EvidenceName "uri-apply-receipt.json" -Process $process
    Assert-ApplyCounts
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if ($settings.Mode -ne "apply") {
        throw "URI apply did not persist apply mode."
    }

    $observe = Invoke-MleUri -Uri "playnite://sempersupra-mle/observe" -ExpectedCommand "observe" -ExpectedResult "PASS" -EvidenceName "uri-observe-receipt.json" -Process $process
    Assert-ObserveReceipts
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if ($settings.Mode -ne "observe") {
        throw "URI observe did not persist observe mode."
    }

    $rollback = Invoke-MleUri -Uri "playnite://sempersupra-mle/rollback" -ExpectedCommand "rollback" -ExpectedResult "PASS" -EvidenceName "uri-rollback-receipt.json" -Process $process
    Assert-RollbackCounts
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if ($settings.Mode -ne "observe") {
        throw "URI rollback did not return to observe mode."
    }

    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $csprojPath = Join-Path $repoRoot "plugins\media-library-enrichment\src\MediaLibraryEnrichment\MediaLibraryEnrichment.csproj"
    $installerManifestPath = Join-Path $repoRoot "plugins\media-library-enrichment\InstallerManifest.yaml"
    $csproj = Get-Content $csprojPath -Raw
    $installerManifest = Get-Content $installerManifestPath -Raw

    if ($csproj -notmatch 'PackageReference\s+Include="PlayniteSDK"\s+Version="6\.16\.0"') {
        throw "Product SDK reference is not pinned to PlayniteSDK 6.16.0."
    }
    if (-not $installerManifest.Contains("AddonId: '4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377'") -or
        -not $installerManifest.Contains("Version: 0.1.0") -or
        -not $installerManifest.Contains("RequiredApiVersion: 6.16.0")) {
        throw "Prepared installer manifest does not match MLE identity/version/API contract."
    }

    [ordered]@{
        schema = "sempersupra-media-library-enrichment-uri-rdte/v1"
        source_sha = $env:RDTE_SOURCE_SHA
        product_authority_sha = $env:RDTE_PRODUCT_AUTHORITY_SHA
        result = "PASS"
        uri_source = $uriSource
        playnite_version = $preview.playnite_version
        sdk_version = $preview.sdk_version
        unknown_command_fail_closed = $true
        surplus_arguments_fail_closed = $true
        cold_start_uri_dispatch = $true
        preview_non_mutating = $true
        apply_enabled = $true
        observe_preserved_state = $true
        rollback_returned_to_observe = $true
        required_api_version = "6.16.0"
    } |
        ConvertTo-Json -Depth 6 |
        Set-Content -Path (Join-Path $EvidenceDir "uri-qualification-receipt.json") -Encoding UTF8

    Stop-Playnite
    $process = $null
    Wait-ForPlayniteQuiescence
}
finally {
    try { Set-ProductMode -Mode "observe" } catch {}
    if ($process -and -not $process.HasExited) {
        try { Stop-Playnite } catch {
            try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch {}
        }
    }
}
