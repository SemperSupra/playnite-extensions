[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDir,

    [switch]$ExerciseTemplateLifecycle,

    [int]$StartupTimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Find-ExactlyOneFile {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $matches = @(Get-ChildItem -Path $Root -Filter $Name -File -Recurse)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one '$Name' under extracted runtime; found $($matches.Count)."
    }
    return $matches[0].FullName
}

function Get-PextManifest {
    param([Parameter(Mandatory = $true)][string]$Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.GetEntry("extension.yaml")
        if (-not $entry) {
            throw "Package '$Path' does not contain extension.yaml."
        }

        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { $yaml = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally {
        $zip.Dispose()
    }

    $id = [regex]::Match($yaml, "(?m)^Id:\s*(?<value>\S+)\s*$")
    $version = [regex]::Match($yaml, "(?m)^Version:\s*(?<value>\S+)\s*$")
    if (-not $id.Success -or -not $version.Success) {
        throw "Package '$Path' does not expose deterministic Id and Version fields."
    }

    [pscustomobject]@{
        Id = $id.Groups["value"].Value.Trim()
        Version = $version.Groups["value"].Value.Trim()
    }
}

function Find-InstalledExtension {
    param(
        [Parameter(Mandatory = $true)][string]$UserData,
        [Parameter(Mandatory = $true)][string]$ExpectedId
    )

    $root = Join-Path $UserData "Extensions"
    if (-not (Test-Path $root)) { return $null }

    foreach ($manifestPath in @(Get-ChildItem $root -Filter "extension.yaml" -File -Recurse)) {
        $yaml = Get-Content $manifestPath.FullName -Raw
        $id = [regex]::Match($yaml, "(?m)^Id:\s*(?<value>\S+)\s*$")
        if ($id.Success -and $id.Groups["value"].Value.Trim() -eq $ExpectedId) {
            return $manifestPath.Directory.FullName
        }
    }

    return $null
}

function Stop-Playnite {
    param(
        [Parameter(Mandatory = $true)][string]$DesktopExe,
        [Parameter(Mandatory = $true)][string]$UserData
    )

    $working = Split-Path -Parent $DesktopExe
    $stopper = Start-Process -FilePath $DesktopExe -WorkingDirectory $working -ArgumentList @(
        "--userdatadir", $UserData,
        "--shutdown"
    ) -PassThru
    $stopper.WaitForExit(30000) | Out-Null

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $running = @(Get-Process | Where-Object {
            $_.ProcessName -like "Playnite.DesktopApp*" -or
            $_.ProcessName -like "Playnite.FullscreenApp*"
        })
        if ($running.Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Playnite did not stop cleanly before timeout."
}

function Start-Playnite {
    param(
        [Parameter(Mandatory = $true)][string]$DesktopExe,
        [Parameter(Mandatory = $true)][string]$UserData,
        [switch]$ResetSettings
    )

    $args = @(
        "--userdatadir", $UserData,
        "--nolibupdate",
        "--hidesplashscreen",
        "--forcedefaulttheme",
        "--forcesoftrender"
    )
    if ($ResetSettings) { $args += "--resetsettings" }

    return Start-Process -FilePath $DesktopExe -WorkingDirectory (Split-Path -Parent $DesktopExe) -ArgumentList $args -PassThru
}

if ($env:OS -ne "Windows_NT") {
    throw "This probe requires Windows."
}

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schema_version -ne 1) {
    throw "Unsupported manifest schema '$($manifest.schema_version)'."
}

New-Item -Path $EvidenceDir -ItemType Directory -Force | Out-Null
$workRoot = Join-Path $env:RUNNER_TEMP ("playnite-runtime-" + [Guid]::NewGuid().ToString("N"))
$downloadDir = Join-Path $workRoot "download"
$runtimeDir = Join-Path $workRoot "runtime"
$userData = Join-Path $workRoot "userdata"
New-Item $downloadDir, $runtimeDir, $userData -ItemType Directory -Force | Out-Null

$receipt = [ordered]@{
    schema = "sempersupra-playnite-runtime-probe/v2"
    requested_runner = $env:RUNNER_NAME
    runner_os = $env:RUNNER_OS
    runner_arch = $env:RUNNER_ARCH
    image_os = $env:ImageOS
    image_version = $env:ImageVersion
    playnite = [ordered]@{
        requested_version = $manifest.playnite_version
        release_tag = $manifest.release_tag
        expected_sha256 = $manifest.portable_sha256
        observed_sha256 = $null
        desktop_file_version = $null
        toolbox_present = $false
        toolbox_file_version = $null
    }
    template_plugin = $null
    phases = [ordered]@{
        acquire = "NOT_RUN"
        extract = "NOT_RUN"
        toolbox = "NOT_RUN"
        initialize = "NOT_RUN"
        shutdown = "NOT_RUN"
        template_new = if ($ExerciseTemplateLifecycle) { "NOT_RUN" } else { "SKIPPED" }
        template_build = if ($ExerciseTemplateLifecycle) { "NOT_RUN" } else { "SKIPPED" }
        toolbox_pack = if ($ExerciseTemplateLifecycle) { "NOT_RUN" } else { "SKIPPED" }
        native_install = if ($ExerciseTemplateLifecycle) { "NOT_RUN" } else { "SKIPPED" }
        restart_load = if ($ExerciseTemplateLifecycle) { "NOT_RUN" } else { "SKIPPED" }
    }
    result = "RUNNING"
}

$playniteProcess = $null
try {
    $archivePath = Join-Path $downloadDir "Playnite-$($manifest.playnite_version).7z"
    Invoke-WebRequest -Uri $manifest.portable_url -OutFile $archivePath -UseBasicParsing
    $hash = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $receipt.playnite.observed_sha256 = $hash
    if ($hash -ne $manifest.portable_sha256.ToLowerInvariant()) {
        throw "Portable archive SHA-256 mismatch."
    }
    $receipt.phases.acquire = "PASS"

    $sevenZipCommand = Get-Command 7z.exe -ErrorAction SilentlyContinue
    $sevenZipPath = if ($sevenZipCommand) { $sevenZipCommand.Source } else { $null }
    if (-not $sevenZipPath) {
        $candidate = Join-Path $env:ProgramFiles "7-Zip\7z.exe"
        if (Test-Path $candidate) { $sevenZipPath = $candidate }
    }
    if (-not $sevenZipPath) {
        throw "7z.exe is unavailable on the runner."
    }

    & $sevenZipPath x $archivePath "-o$runtimeDir" -y | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip extraction failed with exit code $LASTEXITCODE."
    }
    $receipt.phases.extract = "PASS"

    $desktopExe = Find-ExactlyOneFile -Root $runtimeDir -Name $manifest.desktop_exe
    $toolboxExe = Find-ExactlyOneFile -Root $runtimeDir -Name $manifest.toolbox_exe
    $receipt.playnite.desktop_file_version = (Get-Item $desktopExe).VersionInfo.FileVersion
    $receipt.playnite.toolbox_present = $true
    $receipt.playnite.toolbox_file_version = (Get-Item $toolboxExe).VersionInfo.FileVersion
    $receipt.phases.toolbox = "PASS"

    $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData -ResetSettings
    $configPath = Join-Path $userData "config.json"
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while (-not (Test-Path $configPath) -and [DateTime]::UtcNow -lt $deadline) {
        if ($playniteProcess.HasExited) {
            throw "Playnite exited before creating config.json; exit code $($playniteProcess.ExitCode)."
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path $configPath)) {
        throw "Playnite did not create config.json before timeout."
    }
    $receipt.phases.initialize = "PASS"

    Start-Sleep -Seconds 5
    Stop-Playnite -DesktopExe $desktopExe -UserData $userData
    $receipt.phases.shutdown = "PASS"

    if ($ExerciseTemplateLifecycle) {
        $templateRoot = Join-Path $workRoot "template"
        $packageRoot = Join-Path $workRoot "package"
        New-Item $templateRoot, $packageRoot -ItemType Directory -Force | Out-Null

        & $toolboxExe new GenericPlugin SemperSupraRdteProbe $templateRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Toolbox template generation failed with exit code $LASTEXITCODE."
        }
        $pluginDir = Join-Path $templateRoot "SemperSupraRdteProbe"
        if (-not (Test-Path (Join-Path $pluginDir "extension.yaml"))) {
            throw "Toolbox did not materialize the expected GenericPlugin template."
        }
        $receipt.phases.template_new = "PASS"

        $projects = @(Get-ChildItem $pluginDir -Filter "*.csproj" -File -Recurse)
        if ($projects.Count -ne 1) {
            throw "Expected one generated plugin project, found $($projects.Count)."
        }
        & dotnet build $projects[0].FullName -c Release --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Generated Playnite plugin build failed with exit code $LASTEXITCODE."
        }
        $receipt.phases.template_build = "PASS"

        # Playnite's Toolbox documentation requires plugin packing to target
        # the folder containing built binaries. The generated project copies
        # extension.yaml/icon/localization into its build output.
        $buildOutput = Join-Path $pluginDir "bin\Release\net462"
        $modulePath = Join-Path $buildOutput "SemperSupraRdteProbe.dll"
        $builtManifest = Join-Path $buildOutput "extension.yaml"
        if (-not (Test-Path $modulePath) -or -not (Test-Path $builtManifest)) {
            throw "Generated plugin build output is missing its runtime module or extension.yaml."
        }

        $receipt.template_plugin = [ordered]@{
            build_output = $buildOutput
            staged_files = @(
                Get-ChildItem $buildOutput -File -Recurse |
                    ForEach-Object {
                        [ordered]@{
                            path = $_.FullName.Substring($buildOutput.Length).TrimStart("\")
                            sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                        }
                    }
            )
        }

        & $toolboxExe pack $buildOutput $packageRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Toolbox pack failed with exit code $LASTEXITCODE."
        }
        $packages = @(Get-ChildItem $packageRoot -Filter "*.pext" -File)
        if ($packages.Count -ne 1) {
            throw "Expected one .pext from Toolbox, found $($packages.Count)."
        }
        $pextPath = $packages[0].FullName
        $pextHash = (Get-FileHash $pextPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $pextManifest = Get-PextManifest -Path $pextPath
        $receipt.phases.toolbox_pack = "PASS"

        $receipt.template_plugin.id = $pextManifest.Id
        $receipt.template_plugin.version = $pextManifest.Version
        $receipt.template_plugin.package_sha256 = $pextHash

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $packageZip = [System.IO.Compression.ZipFile]::OpenRead($pextPath)
        try {
            $receipt.template_plugin.package_entries = @(
                $packageZip.Entries | ForEach-Object { $_.FullName } | Sort-Object
            )
        }
        finally {
            $packageZip.Dispose()
        }

        $queuePath = Join-Path $userData "extinstalls.json"
        $queue = @([ordered]@{
            InstallType = 0
            Path = $pextPath
        })
        ConvertTo-Json -InputObject $queue -Depth 4 |
            Set-Content -Path $queuePath -Encoding UTF8

        $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData
        $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
        while ((Test-Path $queuePath) -and [DateTime]::UtcNow -lt $deadline) {
            if ($playniteProcess.HasExited) {
                throw "Playnite exited before consuming the extension install queue; exit code $($playniteProcess.ExitCode)."
            }
            Start-Sleep -Milliseconds 250
        }
        if (Test-Path $queuePath) {
            throw "Playnite did not consume extinstalls.json before timeout."
        }

        $installedDir = $null
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while (-not $installedDir -and [DateTime]::UtcNow -lt $deadline) {
            $installedDir = Find-InstalledExtension -UserData $userData -ExpectedId $pextManifest.Id
            if (-not $installedDir) { Start-Sleep -Milliseconds 250 }
        }
        if (-not $installedDir) {
            throw "Playnite consumed the queue but did not materialize extension '$($pextManifest.Id)'."
        }
        $receipt.template_plugin.installed = $true
        $receipt.phases.native_install = "PASS"

        Start-Sleep -Seconds 5
        Stop-Playnite -DesktopExe $desktopExe -UserData $userData

        $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData
        Start-Sleep -Seconds 5
        if ($playniteProcess.HasExited) {
            throw "Playnite exited during restart/load smoke; exit code $($playniteProcess.ExitCode)."
        }

        $logPath = Join-Path $userData "playnite.log"
        if (-not (Test-Path $logPath)) {
            throw "Playnite log is unavailable for plugin-load verification."
        }

        $logText = Get-Content $logPath -Raw
        $loadSuccess = "Loaded plugin: Generic Plugin, version $($pextManifest.Version)"
        $loadFailure = "Failed to load plugin: Generic Plugin"
        if ($logText.Contains($loadFailure)) {
            throw "Playnite reported a plugin load failure for the native template package."
        }
        if (-not $logText.Contains($loadSuccess)) {
            throw "Playnite did not emit the expected positive plugin-load oracle: '$loadSuccess'."
        }

        $receipt.template_plugin.load_oracle = $loadSuccess
        Stop-Playnite -DesktopExe $desktopExe -UserData $userData
        $receipt.phases.restart_load = "PASS"
    }

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
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -like "Playnite.DesktopApp*" -or $_.ProcessName -like "Playnite.FullscreenApp*" } |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $logPath = Join-Path $userData "playnite.log"
    if (Test-Path $logPath) {
        Copy-Item $logPath (Join-Path $EvidenceDir "playnite.log") -Force
    }

    $receipt.finished_utc = [DateTime]::UtcNow.ToString("o")
    $receipt | ConvertTo-Json -Depth 10 |
        Set-Content -Path (Join-Path $EvidenceDir "receipt.json") -Encoding UTF8
}
