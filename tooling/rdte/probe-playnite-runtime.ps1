[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDir,

    [switch]$ExerciseTemplateLifecycle,

    [switch]$ExerciseFixtureSeeder,

    [ValidateSet("media-baseline-v1", "media-raw-v1")]
    [string]$FixtureProfile = "media-baseline-v1",

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

function Prepare-PlayniteSettings {
    param(
        [Parameter(Mandatory = $true)][string]$ConfigPath,
        [Parameter(Mandatory = $true)][string]$UserData
    )

    $config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
    $libraryPath = Join-Path $UserData "library"
    New-Item -Path $libraryPath -ItemType Directory -Force | Out-Null

    # DesktopApplication.ProcessStartupWizard treats a non-empty DatabasePath
    # as the authoritative signal that an existing/explicit library should be
    # opened without entering the provider-selection wizard.
    $config.DatabasePath = $libraryPath
    $config.FirstTimeWizardComplete = $true
    $config.ShowElevatedRightsWarning = $false
    $config.ShowNahimicServiceWarning = $false
    $config | ConvertTo-Json -Depth 100 |
        Set-Content -Path $ConfigPath -Encoding UTF8

    # A forced bootstrap stop intentionally bypasses clean Playnite shutdown.
    # Remove the crash/safe-start sentinel before the clean qualification start.
    $safeStart = Join-Path $UserData "safestart.flag"
    if (Test-Path $safeStart) {
        Remove-Item $safeStart -Force
    }
}

function Wait-ForPlayniteStarted {
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)]$Process,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $marker = "Application $Version.0.0 started"
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "Playnite exited before the clean-start marker appeared; exit code $($Process.ExitCode)."
        }
        if (Test-Path $LogPath) {
            $text = Get-Content $LogPath -Raw
            if ($null -ne $text -and $text.Contains($marker)) {
                return $marker
            }
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Playnite did not emit clean-start marker '$marker' before timeout."
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
    fixture_seeder = $null
    phases = [ordered]@{
        acquire = "NOT_RUN"
        extract = "NOT_RUN"
        toolbox = "NOT_RUN"
        bootstrap_config = "NOT_RUN"
        clean_initialize = "NOT_RUN"
        shutdown = "NOT_RUN"
        template_new = if ($ExerciseTemplateLifecycle) { "NOT_RUN" } else { "SKIPPED" }
        template_build = if ($ExerciseTemplateLifecycle) { "NOT_RUN" } else { "SKIPPED" }
        toolbox_pack = if ($ExerciseTemplateLifecycle) { "NOT_RUN" } else { "SKIPPED" }
        native_install = if ($ExerciseTemplateLifecycle) { "NOT_RUN" } else { "SKIPPED" }
        restart_load = if ($ExerciseTemplateLifecycle) { "NOT_RUN" } else { "SKIPPED" }
        fixture_seed = if ($ExerciseFixtureSeeder) { "NOT_RUN" } else { "SKIPPED" }
        fixture_idempotence = if ($ExerciseFixtureSeeder) { "NOT_RUN" } else { "SKIPPED" }
        fixture_uninstall = if ($ExerciseFixtureSeeder) { "NOT_RUN" } else { "SKIPPED" }
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

    # Bootstrap only far enough for Playnite to create its own settings schema.
    # GitHub-hosted Windows runs elevated, so an untouched first startup blocks
    # on Playnite's elevated-rights warning. This bootstrap is setup, not a
    # lifecycle qualification oracle.
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

    Stop-Process -Id $playniteProcess.Id -Force -ErrorAction Stop
    $playniteProcess.WaitForExit(10000) | Out-Null
    Prepare-PlayniteSettings -ConfigPath $configPath -UserData $userData
    $receipt.phases.bootstrap_config = "PASS"

    $logPath = Join-Path $userData "playnite.log"
    if (Test-Path $logPath) {
        Remove-Item $logPath -Force
    }

    $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData
    $startMarker = Wait-ForPlayniteStarted -LogPath $logPath -Version $manifest.playnite_version -Process $playniteProcess -TimeoutSeconds $StartupTimeoutSeconds
    $receipt.playnite.clean_start_oracle = $startMarker
    $receipt.phases.clean_initialize = "PASS"

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

    if ($ExerciseFixtureSeeder) {
        $seederTemplateRoot = Join-Path $workRoot "fixture-seeder-template"
        $seederPackageRoot = Join-Path $workRoot "fixture-seeder-package"
        New-Item $seederTemplateRoot, $seederPackageRoot -ItemType Directory -Force | Out-Null

        & $toolboxExe new GenericPlugin SemperSupraRdteSeeder $seederTemplateRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Toolbox fixture-seeder template generation failed with exit code $LASTEXITCODE."
        }

        $seederDir = Join-Path $seederTemplateRoot "SemperSupraRdteSeeder"
        $seederSource = Join-Path $PSScriptRoot "FixtureSeeder.cs"
        if (-not (Test-Path $seederSource)) {
            throw "Fixture seeder source '$seederSource' is missing."
        }
        Copy-Item $seederSource (Join-Path $seederDir "SemperSupraRdteSeeder.cs") -Force

        $seederManifestPath = Join-Path $seederDir "extension.yaml"
        $seederManifestText = Get-Content $seederManifestPath -Raw
        $seederManifestText = [regex]::Replace(
            $seederManifestText,
            "(?m)^Id:\s*.*$",
            "Id: SemperSupraRdteSeeder_6d06cf1b-d1e4-4caa-b6c3-cc6026953135")
        $seederManifestText = [regex]::Replace(
            $seederManifestText,
            "(?m)^Name:\s*.*$",
            "Name: RDTE Fixture Seeder")
        $seederManifestText = [regex]::Replace(
            $seederManifestText,
            "(?m)^Author:\s*.*$",
            "Author: SemperSupra RDTE")
        Set-Content $seederManifestPath -Value $seederManifestText -Encoding UTF8

        $seederProject = @(Get-ChildItem $seederDir -Filter "*.csproj" -File -Recurse)
        if ($seederProject.Count -ne 1) {
            throw "Expected one fixture-seeder project, found $($seederProject.Count)."
        }

        & dotnet build $seederProject[0].FullName -c Release --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Fixture seeder build failed with exit code $LASTEXITCODE."
        }

        $seederBuildOutput = Join-Path $seederDir "bin\Release\net462"
        if (-not (Test-Path (Join-Path $seederBuildOutput "SemperSupraRdteSeeder.dll"))) {
            throw "Fixture seeder module is missing from build output."
        }

        & $toolboxExe pack $seederBuildOutput $seederPackageRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Fixture seeder Toolbox pack failed with exit code $LASTEXITCODE."
        }

        $seederPackages = @(Get-ChildItem $seederPackageRoot -Filter "*.pext" -File)
        if ($seederPackages.Count -ne 1) {
            throw "Expected one fixture-seeder .pext, found $($seederPackages.Count)."
        }

        $seederPext = $seederPackages[0].FullName
        $seederPextManifest = Get-PextManifest -Path $seederPext
        $expectedSeederId = "SemperSupraRdteSeeder_6d06cf1b-d1e4-4caa-b6c3-cc6026953135"
        if ($seederPextManifest.Id -ne $expectedSeederId) {
            throw "Fixture seeder package ID mismatch: '$($seederPextManifest.Id)'."
        }

        $receipt.fixture_seeder = [ordered]@{
            id = $seederPextManifest.Id
            version = $seederPextManifest.Version
            package_sha256 = (Get-FileHash $seederPext -Algorithm SHA256).Hash.ToLowerInvariant()
            first_seed = $null
            second_seed = $null
            uninstall = $false
        }

        $queuePath = Join-Path $userData "extinstalls.json"
        $seederInstallQueue = @([ordered]@{ InstallType = 0; Path = $seederPext })
        ConvertTo-Json -InputObject $seederInstallQueue -Depth 4 |
            Set-Content -Path $queuePath -Encoding UTF8

        if (Test-Path $logPath) { Remove-Item $logPath -Force }
        $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData
        $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
        while ((Test-Path $queuePath) -and [DateTime]::UtcNow -lt $deadline) {
            if ($playniteProcess.HasExited) {
                throw "Playnite exited before consuming fixture-seeder install queue."
            }
            Start-Sleep -Milliseconds 250
        }
        if (Test-Path $queuePath) {
            throw "Playnite did not consume fixture-seeder install queue."
        }

        Wait-ForPlayniteStarted -LogPath $logPath -Version $manifest.playnite_version -Process $playniteProcess -TimeoutSeconds $StartupTimeoutSeconds | Out-Null
        $seederInstalledDir = Find-InstalledExtension -UserData $userData -ExpectedId $expectedSeederId
        if (-not $seederInstalledDir) {
            throw "Fixture seeder did not materialize through Playnite native install."
        }

        $seederLog = Get-Content $logPath -Raw
        if ($seederLog.Contains("Failed to load plugin: RDTE Fixture Seeder")) {
            throw "Playnite reported fixture-seeder load failure."
        }
        if (-not $seederLog.Contains("Loaded plugin: RDTE Fixture Seeder, version $($seederPextManifest.Version)")) {
            throw "Missing positive fixture-seeder plugin-load oracle."
        }

        $seederDataPath = Join-Path $userData "ExtensionsData\6d06cf1b-d1e4-4caa-b6c3-cc6026953135"
        New-Item $seederDataPath -ItemType Directory -Force | Out-Null
        Set-Content -Path (Join-Path $seederDataPath "fixture-profile.txt") -Value $FixtureProfile -Encoding ASCII

        $seedReceiptPath = Join-Path $seederDataPath "seed-receipt.json"
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while (-not (Test-Path $seedReceiptPath) -and [DateTime]::UtcNow -lt $deadline) {
            if ($playniteProcess.HasExited) {
                throw "Playnite exited before fixture-seeder receipt appeared."
            }
            Start-Sleep -Milliseconds 250
        }
        if (-not (Test-Path $seedReceiptPath)) {
            throw "Fixture seeder did not emit seed-receipt.json."
        }

        $firstSeed = Get-Content $seedReceiptPath -Raw | ConvertFrom-Json
        if ($firstSeed.expected_fixture_games -ne 5 -or $firstSeed.observed_fixture_games -ne 5) {
            throw "Fixture seeder did not create exactly five expected fixture games."
        }
        $receipt.fixture_seeder.first_seed = $firstSeed
        Copy-Item $seedReceiptPath (Join-Path $EvidenceDir "seed-first.json") -Force
        Stop-Playnite -DesktopExe $desktopExe -UserData $userData
        $receipt.phases.fixture_seed = "PASS"

        Remove-Item $seedReceiptPath -Force
        if (Test-Path $logPath) { Remove-Item $logPath -Force }
        $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData
        Wait-ForPlayniteStarted -LogPath $logPath -Version $manifest.playnite_version -Process $playniteProcess -TimeoutSeconds $StartupTimeoutSeconds | Out-Null

        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while (-not (Test-Path $seedReceiptPath) -and [DateTime]::UtcNow -lt $deadline) {
            if ($playniteProcess.HasExited) {
                throw "Playnite exited before second fixture-seeder receipt appeared."
            }
            Start-Sleep -Milliseconds 250
        }
        if (-not (Test-Path $seedReceiptPath)) {
            throw "Fixture seeder did not re-emit its receipt on second run."
        }

        $secondSeed = Get-Content $seedReceiptPath -Raw | ConvertFrom-Json
        if ($secondSeed.expected_fixture_games -ne 5 -or
            $secondSeed.observed_fixture_games -ne 5 -or
            $secondSeed.total_library_games -ne $firstSeed.total_library_games) {
            throw "Fixture seeder is not idempotent across restart."
        }
        $receipt.fixture_seeder.second_seed = $secondSeed
        Copy-Item $seedReceiptPath (Join-Path $EvidenceDir "seed-second.json") -Force
        Stop-Playnite -DesktopExe $desktopExe -UserData $userData
        $receipt.phases.fixture_idempotence = "PASS"

        $seederUninstallQueue = @([ordered]@{ InstallType = 1; Path = $seederInstalledDir })
        ConvertTo-Json -InputObject $seederUninstallQueue -Depth 4 |
            Set-Content -Path $queuePath -Encoding UTF8

        if (Test-Path $logPath) { Remove-Item $logPath -Force }
        $playniteProcess = Start-Playnite -DesktopExe $desktopExe -UserData $userData
        $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
        while ((Test-Path $queuePath) -and [DateTime]::UtcNow -lt $deadline) {
            if ($playniteProcess.HasExited) {
                throw "Playnite exited before consuming fixture-seeder uninstall queue."
            }
            Start-Sleep -Milliseconds 250
        }
        if (Test-Path $queuePath) {
            throw "Playnite did not consume fixture-seeder uninstall queue."
        }

        Wait-ForPlayniteStarted -LogPath $logPath -Version $manifest.playnite_version -Process $playniteProcess -TimeoutSeconds $StartupTimeoutSeconds | Out-Null
        if (Test-Path $seederInstalledDir) {
            throw "Fixture seeder extension directory remains after native uninstall."
        }

        Stop-Playnite -DesktopExe $desktopExe -UserData $userData
        $receipt.fixture_seeder.uninstall = $true
        $receipt.phases.fixture_uninstall = "PASS"
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
