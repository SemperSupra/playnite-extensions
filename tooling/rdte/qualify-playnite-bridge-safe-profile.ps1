[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WorkRoot,

    [Parameter(Mandatory = $true)]
    [string]$BridgePackage,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDir,

    [int]$StartupTimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$expectedPackageSha256 = "27733103da7885eb8400dfdbe075108ba9e9b7a6beef83f197ccf5bb27658cd4"
$extensionId = "PlayniteBridge_f47ac10b"
$pluginGuid = "f47ac10b-58cc-4372-a567-0e02b2c3d479"
$expectedVersion = "1.5.3"
$port = 19821

$userData = Join-Path $WorkRoot "userdata"
$runtimeDir = Join-Path $WorkRoot "runtime"
$logPath = Join-Path $userData "playnite.log"
$queuePath = Join-Path $userData "extinstalls.json"
$pluginData = Join-Path $userData "ExtensionsData\$pluginGuid"
$authPath = Join-Path $pluginData "auth.json"
$configPath = Join-Path $pluginData "config.json"

New-Item $EvidenceDir, $pluginData -ItemType Directory -Force | Out-Null

$packageSha = (Get-FileHash $BridgePackage -Algorithm SHA256).Hash.ToLowerInvariant()
if ($packageSha -ne $expectedPackageSha256) {
    throw "Pinned Playnite Bridge package SHA-256 mismatch."
}

$desktop = @(Get-ChildItem $runtimeDir -Filter "Playnite.DesktopApp.exe" -File -Recurse)
if ($desktop.Count -ne 1) {
    throw "Expected exactly one Playnite.DesktopApp.exe."
}
$desktopExe = $desktop[0].FullName

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
    throw "Playnite runtime did not become quiescent."
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
    Wait-ForPlayniteQuiescence
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

function Wait-ForText {
    param([string]$Path, [string]$Text, $Process)
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "Playnite exited while waiting for '$Text'."
        }
        if (Test-Path $Path -PathType Leaf) {
            $value = Get-Content $Path -Raw
            if ($null -ne $value -and $value.Contains($Text)) { return }
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for '$Text'."
}

function Wait-ForFile {
    param([string]$Path, $Process)
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "Playnite exited while waiting for '$Path'."
        }
        if (Test-Path $Path -PathType Leaf) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for '$Path'."
}

function Find-InstalledExtension {
    $root = Join-Path $userData "Extensions"
    if (-not (Test-Path $root)) { return $null }

    foreach ($manifest in @(Get-ChildItem $root -Filter "extension.yaml" -File -Recurse)) {
        $yaml = Get-Content $manifest.FullName -Raw
        $match = [regex]::Match($yaml, "(?m)^Id:\s*(?<value>\S+)\s*$")
        if ($match.Success -and $match.Groups["value"].Value.Trim() -eq $extensionId) {
            return $manifest.Directory.FullName
        }
    }

    return $null
}

function Queue-Install {
    @([ordered]@{ InstallType = 0; Path = $BridgePackage }) |
        ConvertTo-Json -Depth 4 |
        Set-Content -Path $queuePath -Encoding UTF8
}

function Queue-Uninstall {
    param([string]$InstalledDir)
    @([ordered]@{ InstallType = 1; Path = $InstalledDir }) |
        ConvertTo-Json -Depth 4 |
        Set-Content -Path $queuePath -Encoding UTF8
}

function Invoke-Bridge {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Method = "GET",
        [hashtable]$Headers = @{},
        [string]$Body = $null
    )

    $args = @{
        Uri = "http://localhost:$port$Path"
        Method = $Method
        Headers = $Headers
        SkipHttpErrorCheck = $true
        UseBasicParsing = $true
    }
    if ($null -ne $Body) {
        $args.Body = $Body
        $args.ContentType = "application/json"
    }

    Invoke-WebRequest @args
}

$receipt = [ordered]@{
    schema = "sempersupra-playnite-bridge-safe-profile-rdte/v1"
    source_sha = if ($env:RDTE_SOURCE_SHA) { $env:RDTE_SOURCE_SHA } else { $env:GITHUB_SHA }
    upstream = [ordered]@{
        repository = "rollacode/playnite-bridge"
        release = "v1.5.3"
        extension_id = $extensionId
        version = $expectedVersion
        package_sha256 = $packageSha
    }
    playnite = [ordered]@{
        version = "10.62"
    }
    observations = [ordered]@{}
    direct_agent_admission = "UNASSESSED"
    result = "RUNNING"
}

$process = $null
$installedDir = $null
try {
    Wait-ForPlayniteQuiescence

    # Suppress only the first-run network prompt so the native runner can
    # characterize the plugin's actual bind choice without UI automation.
    [ordered]@{
        SyncEnabled = $false
        SyncBackendUrl = ""
        SyncIntervalMinutes = 5
        SyncOnChange = $true
        SyncImages = $true
        NetworkPromptDismissed = $true
    } | ConvertTo-Json -Depth 4 | Set-Content $configPath -Encoding UTF8

    if (Test-Path $logPath) { Remove-Item $logPath -Force }

    Queue-Install
    $process = Start-Playnite
    Wait-ForQueueConsumed -Process $process
    Wait-ForText -Path $logPath -Text "Loaded plugin: Playnite Bridge, version $expectedVersion" -Process $process
    Wait-ForText -Path $logPath -Text "Playnite Bridge API started on port $port" -Process $process
    Wait-ForFile -Path $authPath -Process $process

    $installedDir = Find-InstalledExtension
    if (-not $installedDir) {
        throw "Playnite Bridge did not materialize through native install."
    }

    $log = Get-Content $logPath -Raw
    $listenerMatch = [regex]::Matches(
        $log,
        "HTTP server listening on (?<prefix>http://[^\s]+/)")
    if ($listenerMatch.Count -lt 1) {
        throw "No Playnite Bridge HTTP listener oracle was found."
    }
    $listenerPrefix = $listenerMatch[$listenerMatch.Count - 1].Groups["prefix"].Value
    $receipt.observations.listener_prefix = $listenerPrefix
    $receipt.observations.network_wide_listener = (
        $listenerPrefix.Contains("+:") -or $listenerPrefix.Contains("*:")
    )

    $auth = Get-Content $authPath -Raw | ConvertFrom-Json
    $token = [string]$auth.token
    if ([string]::IsNullOrWhiteSpace($token) -or -not $token.StartsWith("pb_")) {
        throw "Playnite Bridge auth token was not materialized."
    }

    $unauth = Invoke-Bridge -Path "/api/app/info"
    if ($unauth.StatusCode -ne 401) {
        throw "Unauthenticated API request was not rejected."
    }
    $receipt.observations.unauthenticated_request_rejected = $true

    $headers = @{ Authorization = "Bearer $token" }
    $app = Invoke-Bridge -Path "/api/app/info" -Headers $headers
    if ($app.StatusCode -ne 200) {
        throw "Authenticated app-info request failed."
    }
    $appJson = $app.Content | ConvertFrom-Json
    $receipt.observations.authenticated_read_works = $true
    $receipt.observations.reported_playnite_version = [string]$appJson.version

    $games = Invoke-Bridge -Path "/api/games?limit=5" -Headers $headers
    if ($games.StatusCode -ne 200) {
        throw "Authenticated game-list request failed."
    }
    $gamesJson = $games.Content | ConvertFrom-Json
    if ([int]$gamesJson.total -lt 1) {
        throw "Seeded disposable library was not visible through Playnite Bridge."
    }
    $receipt.observations.read_library_total = [int]$gamesJson.total

    $cors = [string]$app.Headers["Access-Control-Allow-Origin"]
    $receipt.observations.cors_allow_origin = $cors
    $receipt.observations.cors_wildcard = ($cors -eq "*")

    $index = Invoke-Bridge -Path "/api" -Headers $headers
    if ($index.StatusCode -ne 200) {
        throw "API index request failed."
    }
    $indexJson = $index.Content | ConvertFrom-Json
    $endpointMaterial = ($indexJson.endpoints -join "\n")
    $receipt.observations.api_advertises_eval = $endpointMaterial.Contains("/api/eval")
    $receipt.observations.api_advertises_delete = $endpointMaterial.Contains("DELETE /api/games/{id}")

    $skill = Invoke-Bridge -Path "/api/skill.md" -Headers $headers
    if ($skill.StatusCode -ne 200) {
        throw "AI skill request failed."
    }
    $skillJson = $skill.Content | ConvertFrom-Json
    $skillContainsToken = ([string]$skillJson.content).Contains($token)
    $receipt.observations.skill_embeds_broad_token = $skillContainsToken

    # Benign expression only: prove that the same bearer credential authorizes
    # in-process code execution without exercising destructive library state.
    $evalBody = @{
        code = "1+1"
        timeoutMs = 1000
        onUiThread = $false
    } | ConvertTo-Json -Compress
    $eval = Invoke-Bridge -Path "/api/eval" -Method "POST" -Headers $headers -Body $evalBody
    if ($eval.StatusCode -ne 200) {
        throw "Benign eval request failed at transport layer."
    }
    $evalJson = $eval.Content | ConvertFrom-Json
    if ($evalJson.success -ne $true) {
        throw "Same-token benign eval was not authorized/executed as expected."
    }
    $receipt.observations.same_token_eval_authorized = $true

    $riskReasons = @()
    if ($receipt.observations.network_wide_listener) {
        $riskReasons += "network-wide-listener-default"
    }
    if ($receipt.observations.cors_wildcard) {
        $riskReasons += "wildcard-cors"
    }
    if ($receipt.observations.skill_embeds_broad_token) {
        $riskReasons += "skill-embeds-broad-token"
    }
    if ($receipt.observations.same_token_eval_authorized) {
        $riskReasons += "same-token-in-process-eval"
    }
    if ($receipt.observations.api_advertises_delete) {
        $riskReasons += "same-router-destructive-endpoints"
    }

    $receipt.observations.risk_reasons = $riskReasons
    $receipt.direct_agent_admission = if ($riskReasons.Count -eq 0) { "ADMIT" } else { "REJECT" }

    Stop-Playnite
    $process = $null

    Queue-Uninstall -InstalledDir $installedDir
    $process = Start-Playnite
    Wait-ForQueueConsumed -Process $process

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ((Test-Path $installedDir) -and [DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            throw "Playnite exited before Playnite Bridge uninstall completed."
        }
        Start-Sleep -Milliseconds 250
    }
    if (Test-Path $installedDir) {
        throw "Playnite Bridge remained installed after native uninstall."
    }

    Stop-Playnite
    $process = $null
    $receipt.observations.native_uninstall = "PASS"
    $receipt.result = "PASS"
}
catch {
    $receipt.result = "FAIL"
    $receipt.error = $_.Exception.Message
    throw
}
finally {
    if ($process -and -not $process.HasExited) {
        try { Stop-Playnite } catch {
            try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch {}
        }
    }

    $receipt.finished_utc = [DateTime]::UtcNow.ToString("o")
    $receipt | ConvertTo-Json -Depth 10 |
        Set-Content -Path (Join-Path $EvidenceDir "playnite-bridge-safe-profile-receipt.json") -Encoding UTF8
}
