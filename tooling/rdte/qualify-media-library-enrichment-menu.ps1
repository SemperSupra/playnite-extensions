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
$seederExtensionId = "SemperSupraRdteSeeder_6d06cf1b-d1e4-4caa-b6c3-cc6026953135"
$seederDataId = "6d06cf1b-d1e4-4caa-b6c3-cc6026953135"
$userData = Join-Path $WorkRoot "userdata"
$runtimeDir = Join-Path $WorkRoot "runtime"
$desktop = @(Get-ChildItem $runtimeDir -Filter "Playnite.DesktopApp.exe" -File -Recurse)
if ($desktop.Count -ne 1) {
    throw "Expected exactly one Playnite.DesktopApp.exe."
}
$desktopExe = $desktop[0].FullName

$productDir = Join-Path $userData "Extensions\$productId"
$productAssembly = Join-Path $productDir "MediaLibraryEnrichment.dll"
if (-not (Test-Path $productAssembly -PathType Leaf)) {
    throw "Media Library Enrichment must remain installed for menu qualification."
}

$seederData = Join-Path $userData "ExtensionsData\$seederId"
$productData = Join-Path $userData "ExtensionsData\$productId"
New-Item $seederData, $productData, $EvidenceDir -ItemType Directory -Force | Out-Null

$profilePath = Join-Path $seederData "fixture-profile.txt"
$menuReceiptPath = Join-Path $seederData "mle-menu-control-receipt.json"
$settingsPath = Join-Path $productData "settings.json"
$queuePath = Join-Path $userData "extinstalls.json"

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

    throw "Playnite did not stop cleanly before menu qualification."
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

function Wait-ForQueue {
    param($Process)

    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ((Test-Path $queuePath) -and [DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "Playnite exited before consuming the extension queue."
        }
        Start-Sleep -Milliseconds 250
    }
    if (Test-Path $queuePath) {
        throw "Playnite did not consume the extension queue."
    }
}

$process = $null
try {
    Set-ProductMode -Mode "observe"
    Set-Content -Path $profilePath -Value "mle-menu-control-qualification-v1" -Encoding UTF8
    if (Test-Path $menuReceiptPath) {
        Remove-Item $menuReceiptPath -Force
    }

    $seederDir = Join-Path $userData "Extensions\$seederId"
    if (-not (Test-Path (Join-Path $seederDir "extension.yaml") -PathType Leaf)) {
        $seederPackages = @(
            Get-ChildItem (Join-Path $WorkRoot "fixture-seeder-package") -Filter "*.pext" -File
        )
        if ($seederPackages.Count -ne 1) {
            throw "Expected exactly one fixture-seeder package."
        }

        @([ordered]@{ InstallType = 0; Path = $seederPackages[0].FullName }) |
            ConvertTo-Json -Depth 4 |
            Set-Content -Path $queuePath -Encoding UTF8
    }

    $process = Start-Playnite
    if (Test-Path $queuePath) {
        Wait-ForQueue -Process $process
    }
    Wait-ForFile -Path $menuReceiptPath -Process $process

    $receipt = Get-Content $menuReceiptPath -Raw | ConvertFrom-Json
    if ($receipt.schema -ne "sempersupra-playnite-mle-menu-control/v1" -or
        $receipt.result -ne "PASS" -or
        $receipt.menu_surface -ne $true -or
        $receipt.preview_no_library_mutation -ne $true -or
        $receipt.apply_enabled -ne $true -or
        $receipt.observe_preserved_state -ne $true -or
        $receipt.rollback_returned_to_observe -ne $true) {
        throw "Native MLE menu-control qualification receipt failed."
    }

    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if ($settings.Mode -ne "observe") {
        throw "Menu-control qualification did not finish in observe mode."
    }

    Copy-Item $menuReceiptPath (Join-Path $EvidenceDir "menu-control-receipt.json") -Force
}
finally {
    Set-Content -Path $profilePath -Value "media-raw-v1" -Encoding UTF8
    try { Set-ProductMode -Mode "observe" } catch {}
    if ($process -and -not $process.HasExited) {
        try { Stop-Playnite } catch {
            try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch {}
        }
    }
}
