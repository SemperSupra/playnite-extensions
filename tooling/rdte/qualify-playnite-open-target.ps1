[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$WorkRoot,
    [Parameter(Mandatory = $true)][string]$EvidenceDir,
    [int]$StartupTimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$seederId = "SemperSupraRdteSeeder_6d06cf1b-d1e4-4caa-b6c3-cc6026953135"
$seederDataId = "6d06cf1b-d1e4-4caa-b6c3-cc6026953135"
$seederName = "RDTE Fixture Seeder"

$userData = Join-Path $WorkRoot "userdata"
$runtimeDir = Join-Path $WorkRoot "runtime"
New-Item -Path $EvidenceDir -ItemType Directory -Force | Out-Null

$desktopExe = @(Get-ChildItem $runtimeDir -Filter "Playnite.DesktopApp.exe" -File -Recurse)
if ($desktopExe.Count -ne 1) { throw "Expected exactly one Playnite.DesktopApp.exe." }
$desktopExe = $desktopExe[0].FullName

$seederPackage = @(Get-ChildItem (Join-Path $WorkRoot "fixture-seeder-package") -Filter "*.pext" -File)
if ($seederPackage.Count -ne 1) { throw "Expected exactly one fixture-seeder package." }
$seederPackage = $seederPackage[0].FullName

$logPath = Join-Path $userData "playnite.log"
$queuePath = Join-Path $userData "extinstalls.json"
$seederData = Join-Path $userData "ExtensionsData\$seederDataId"
$profilePath = Join-Path $seederData "fixture-profile.txt"
$targetConfigPath = Join-Path $seederData "open-target-path.txt"
$invocationReceiptPath = Join-Path $seederData "open-target-invocation-receipt.json"

$openRoot = Join-Path $WorkRoot "open-target-rdte"
$targetPath = Join-Path $openRoot "rdte-open-target.s2rdte"
$handlerScript = Join-Path $openRoot "handler.ps1"
$handlerReceipt = Join-Path $openRoot "handler-receipt.json"

$extension = ".s2rdte"
$progId = "SemperSupra.RDTE.OpenTarget"
$extensionKey = "HKCU:\Software\Classes\$extension"
$progIdKey = "HKCU:\Software\Classes\$progId"
$commandKey = Join-Path $progIdKey "shell\open\command"

function Start-Playnite {
    Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path -Parent $desktopExe) -ArgumentList @(
        "--userdatadir", $userData,
        "--nolibupdate",
        "--hidesplashscreen",
        "--forcedefaulttheme",
        "--forcesoftrender"
    ) -PassThru
}

function Wait-ForPlayniteQuiescence {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
            $_.ProcessName -like "Playnite.DesktopApp*" -or
            $_.ProcessName -like "Playnite.FullscreenApp*" -or
            $_.ProcessName -like "Playnite.BrowserProcess*"
        })
        if ($running.Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "Playnite runtime was not quiescent."
}

function Stop-Playnite {
    $stopper = Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path -Parent $desktopExe) -ArgumentList @(
        "--userdatadir", $userData,
        "--shutdown"
    ) -PassThru
    $stopper.WaitForExit(30000) | Out-Null
    Wait-ForPlayniteQuiescence
}

function Wait-ForFile {
    param([string]$Path, $Process)
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process -and $Process.HasExited) {
            throw "Playnite exited while waiting for '$Path'."
        }
        if (Test-Path $Path -PathType Leaf) { return }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out waiting for '$Path'."
}

function Wait-ForText {
    param([string]$Path, [string]$Text, $Process)
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process -and $Process.HasExited) {
            throw "Playnite exited while waiting for '$Text'."
        }
        if (Test-Path $Path -PathType Leaf) {
            $content = Get-Content $Path -Raw
            if ($null -ne $content -and $content.Contains($Text)) { return }
        }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out waiting for '$Text'."
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
        Start-Sleep -Milliseconds 200
    }
    if (Test-Path $queuePath) { throw "Playnite did not consume extension queue." }
}

function Find-InstalledExtension {
    $root = Join-Path $userData "Extensions"
    if (-not (Test-Path $root)) { return $null }
    foreach ($manifest in @(Get-ChildItem $root -Filter "extension.yaml" -File -Recurse)) {
        $yaml = Get-Content $manifest.FullName -Raw
        $match = [regex]::Match($yaml, "(?m)^Id:\s*(?<value>\S+)\s*$")
        if ($match.Success -and $match.Groups["value"].Value.Trim() -eq $seederId) {
            return $manifest.Directory.FullName
        }
    }
    return $null
}

function Remove-TestAssociation {
    if (Test-Path $extensionKey) { Remove-Item $extensionKey -Recurse -Force }
    if (Test-Path $progIdKey) { Remove-Item $progIdKey -Recurse -Force }
}

$receipt = [ordered]@{
    schema = "sempersupra-playnite-open-target-rdte/v1"
    source_sha = if ($env:RDTE_SOURCE_SHA) { $env:RDTE_SOURCE_SHA } else { $env:GITHUB_SHA }
    result = "RUNNING"
    target_extension = $extension
}

$process = $null
$installedDir = $null
try {
    Wait-ForPlayniteQuiescence
    Remove-TestAssociation

    New-Item -Path $openRoot -ItemType Directory -Force | Out-Null
    New-Item -Path $seederData -ItemType Directory -Force | Out-Null
    Set-Content -Path $targetPath -Value "SemperSupra OPEN_TARGET deterministic fixture" -Encoding UTF8
    if (Test-Path $handlerReceipt) { Remove-Item $handlerReceipt -Force }
    if (Test-Path $invocationReceiptPath) { Remove-Item $invocationReceiptPath -Force }

    @'
param(
    [Parameter(Mandatory = $true)][string]$Target,
    [Parameter(Mandatory = $true)][string]$Receipt
)
$ErrorActionPreference = "Stop"
[ordered]@{
    schema = "sempersupra-open-target-handler/v1"
    result = "PASS"
    target_path = [System.IO.Path]::GetFullPath($Target)
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Receipt -Encoding UTF8
'@ | Set-Content -Path $handlerScript -Encoding UTF8

    $windowsPowerShell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
    if (-not (Test-Path $windowsPowerShell -PathType Leaf)) {
        throw "Windows PowerShell executable is missing."
    }

    New-Item -Path $extensionKey -Force | Out-Null
    Set-Item -Path $extensionKey -Value $progId
    New-Item -Path $commandKey -Force | Out-Null
    $command = ('"{0}" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{1}" "%1" "{2}"' -f
        $windowsPowerShell, $handlerScript, $handlerReceipt)
    Set-Item -Path $commandKey -Value $command

    Set-Content -Path $profilePath -Value "open-target-file-activate-v1" -Encoding UTF8
    Set-Content -Path $targetConfigPath -Value $targetPath -Encoding UTF8

    Queue-Install -PackagePath $seederPackage
    if (Test-Path $logPath) { Remove-Item $logPath -Force }
    $process = Start-Playnite
    Wait-ForQueueConsumed -Process $process
    Wait-ForText -Path $logPath -Text "Loaded plugin: $seederName, version 1.0" -Process $process
    Wait-ForFile -Path $invocationReceiptPath -Process $process
    Wait-ForFile -Path $handlerReceipt -Process $process

    $invocation = Get-Content $invocationReceiptPath -Raw | ConvertFrom-Json
    if ($invocation.schema -ne "sempersupra-playnite-open-target-invocation/v1" -or
        $invocation.result -ne "PASS" -or
        $invocation.action_type -ne "File" -or
        $invocation.is_play_action -ne $false -or
        $invocation.action_name -ne "Read" -or
        [System.IO.Path]::GetFullPath($invocation.target_path) -ne [System.IO.Path]::GetFullPath($targetPath)) {
        throw "Fixture did not invoke the expected non-Play File action."
    }

    $handler = Get-Content $handlerReceipt -Raw | ConvertFrom-Json
    if ($handler.schema -ne "sempersupra-open-target-handler/v1" -or
        $handler.result -ne "PASS" -or
        [System.IO.Path]::GetFullPath($handler.target_path) -ne [System.IO.Path]::GetFullPath($targetPath)) {
        throw "System default-handler dispatch did not reach the deterministic handler with the exact target."
    }

    $installedDir = Find-InstalledExtension
    if (-not $installedDir) { throw "Fixture seeder did not install." }

    $receipt.action_type = $invocation.action_type
    $receipt.is_play_action = $invocation.is_play_action
    $receipt.action_name = $invocation.action_name
    $receipt.target_path = [System.IO.Path]::GetFullPath($targetPath)
    $receipt.handler_target_path = [System.IO.Path]::GetFullPath($handler.target_path)
    $receipt.result = "PASS"

    Copy-Item $invocationReceiptPath (Join-Path $EvidenceDir "open-target-invocation.json") -Force
    Copy-Item $handlerReceipt (Join-Path $EvidenceDir "open-target-handler.json") -Force

    Stop-Playnite
    $process = $null

    Queue-Uninstall -InstalledDir $installedDir
    $process = Start-Playnite
    Wait-ForQueueConsumed -Process $process
    Stop-Playnite
    $process = $null
}
catch {
    $receipt.result = "FAIL"
    $receipt.error = $_.Exception.Message
    throw
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-TestAssociation
    $receipt.finished_utc = [DateTime]::UtcNow.ToString("o")
    $receipt | ConvertTo-Json -Depth 8 |
        Set-Content -Path (Join-Path $EvidenceDir "open-target-qualification-receipt.json") -Encoding UTF8
    if (Test-Path $logPath -PathType Leaf) {
        Copy-Item $logPath (Join-Path $EvidenceDir "playnite.log") -Force
    }
}
