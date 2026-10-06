param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDir
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null
$work = Join-Path $env:RUNNER_TEMP ("playnite-open-target-" + [Guid]::NewGuid().ToString("N"))
$downloadDir = Join-Path $work "download"
$runtimeDir = Join-Path $work "runtime"
New-Item $downloadDir, $runtimeDir -ItemType Directory -Force | Out-Null

$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot "playnite-10.62.json") -Raw | ConvertFrom-Json
$archive = Join-Path $downloadDir "Playnite-$($manifest.playnite_version).7z"
Invoke-WebRequest -Uri $manifest.portable_url -OutFile $archive -UseBasicParsing
$archiveSha = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($archiveSha -ne $manifest.portable_sha256.ToLowerInvariant()) {
    throw "Pinned Playnite portable SHA-256 mismatch."
}

$sevenZip = (Get-Command 7z.exe -ErrorAction SilentlyContinue).Source
if (-not $sevenZip) {
    $sevenZip = "C:\Program Files\7-Zip\7z.exe"
}
if (-not (Test-Path -LiteralPath $sevenZip)) {
    throw "7-Zip unavailable."
}

& $sevenZip x $archive "-o$runtimeDir" -y | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Playnite extraction failed with exit code $LASTEXITCODE."
}

$playniteDlls = @(Get-ChildItem -Path $runtimeDir -Recurse -File -Filter "Playnite.dll")
if ($playniteDlls.Count -ne 1) {
    throw "Expected exactly one Playnite.dll; found $($playniteDlls.Count)."
}
$playniteDll = $playniteDlls[0].FullName
$playniteHome = Split-Path -Parent $playniteDll

$handlerSource = @'
using System;
using System.IO;

public static class Handler
{
    public static int Main(string[] args)
    {
        var receipt = Environment.GetEnvironmentVariable("RDTE_PLAYNITE_OPEN_TARGET_RECEIPT");
        if (String.IsNullOrWhiteSpace(receipt) || args.Length != 1)
        {
            return 30;
        }

        Uri uri;
        if (Uri.TryCreate(args[0], UriKind.Absolute, out uri) &&
            !String.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(receipt, args[0]);
        }
        else
        {
            File.WriteAllText(receipt, Path.GetFullPath(args[0]));
        }
        return 0;
    }
}
'@

$invokerSource = @'
using System;
using System.IO;
using System.Reflection;

public static class PlayniteInvoker
{
    public static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            return 2;
        }

        var runtimeDir = args[0];
        var playniteDll = args[1];
        var mode = args[2];
        var target = args[3];

        AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
        {
            var name = new AssemblyName(eventArgs.Name).Name + ".dll";
            var candidate = Path.Combine(runtimeDir, name);
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };

        try
        {
            var asm = Assembly.LoadFrom(playniteDll);
            var type = asm.GetType("Playnite.Common.ProcessStarter", true);

            if (String.Equals(mode, "file", StringComparison.Ordinal))
            {
                var method = type.GetMethod(
                    "StartProcess",
                    new[] { typeof(string), typeof(string), typeof(string), typeof(bool) });
                method.Invoke(null, new object[]
                {
                    target,
                    String.Empty,
                    Path.GetDirectoryName(target) ?? String.Empty,
                    false
                });
                return 0;
            }

            if (String.Equals(mode, "uri", StringComparison.Ordinal))
            {
                var method = type.GetMethod("StartUrl", new[] { typeof(string) });
                method.Invoke(null, new object[] { target });
                return 0;
            }

            return 3;
        }
        catch (TargetInvocationException ex)
        {
            File.WriteAllText(
                Path.Combine(runtimeDir, "open-target-invocation-error.txt"),
                (ex.InnerException ?? ex).ToString());
            return 20;
        }
        catch (Exception ex)
        {
            File.WriteAllText(
                Path.Combine(runtimeDir, "open-target-invocation-error.txt"),
                ex.ToString());
            return 21;
        }
    }
}
'@

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "Required .NET Framework compiler is unavailable."
}

$handlerCs = Join-Path $work "handler.cs"
$handlerExe = Join-Path $work "handler.exe"
$invokerCs = Join-Path $work "playnite-invoker.cs"
$invokerExe = Join-Path $work "playnite-invoker.exe"
Set-Content -LiteralPath $handlerCs -Value $handlerSource -Encoding UTF8
Set-Content -LiteralPath $invokerCs -Value $invokerSource -Encoding UTF8
& $csc /nologo /target:exe /out:$handlerExe $handlerCs
if ($LASTEXITCODE -ne 0) { throw "Handler compilation failed." }
& $csc /nologo /target:exe /out:$invokerExe $invokerCs
if ($LASTEXITCODE -ne 0) { throw "Invoker compilation failed." }

$classesRoot = "HKCU:\Software\Classes"
$extension = ".rdteplaynite"
$extensionKey = Join-Path $classesRoot $extension
$progId = "SemperSupra.RDTE.PlayniteFile"
$progIdKey = Join-Path $classesRoot $progId
$commandKey = Join-Path $progIdKey "shell\open\command"
$uriScheme = "rdteplaynite"
$uriKey = Join-Path $classesRoot $uriScheme
$uriCommandKey = Join-Path $uriKey "shell\open\command"
$handlerReceipt = Join-Path $work "handler-receipt.txt"
$target = Join-Path $work ("fixture" + $extension)
Set-Content -LiteralPath $target -Value "%PDF-1.4`n% SemperSupra Playnite 10.62 OPEN_TARGET fixture`n" -Encoding ASCII

$receipt = [ordered]@{
    schema = "sempersupra-playnite-open-target-rdte/v1"
    source_sha = if ($env:RDTE_SOURCE_SHA) { $env:RDTE_SOURCE_SHA } elseif ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { "" }
    playnite_version = $manifest.playnite_version
    playnite_portable_sha256 = $archiveSha
    result = "RUNNING"
}

try {
    New-Item -Path $extensionKey -Force | Out-Null
    Set-Item -Path $extensionKey -Value $progId
    New-Item -Path $commandKey -Force | Out-Null
    Set-Item -Path $commandKey -Value ('"' + $handlerExe + '" "%1"')

    New-Item -Path $uriCommandKey -Force | Out-Null
    Set-Item -Path $uriKey -Value "URL:SemperSupra Playnite RDTE protocol"
    New-ItemProperty -Path $uriKey -Name "URL Protocol" -Value "" -PropertyType String -Force | Out-Null
    Set-Item -Path $uriCommandKey -Value ('"' + $handlerExe + '" "%1"')

    $env:RDTE_PLAYNITE_OPEN_TARGET_RECEIPT = $handlerReceipt

    $fileRun = Start-Process -FilePath $invokerExe -ArgumentList @(
        $playniteHome,
        $playniteDll,
        "file",
        $target
    ) -PassThru -Wait
    if ($fileRun.ExitCode -ne 0) {
        throw "Playnite ProcessStarter file probe returned $($fileRun.ExitCode)."
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $handlerReceipt) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $handlerReceipt)) {
        throw "Playnite ProcessStarter did not reach the registered file handler."
    }
    $receivedFile = (Get-Content -LiteralPath $handlerReceipt -Raw).Trim()
    if ($receivedFile -ne [IO.Path]::GetFullPath($target)) {
        throw "Playnite ProcessStarter file target mismatch."
    }

    Remove-Item -LiteralPath $handlerReceipt -Force
    $uriTarget = "rdteplaynite://fixture/media?id=42"
    $uriRun = Start-Process -FilePath $invokerExe -ArgumentList @(
        $playniteHome,
        $playniteDll,
        "uri",
        $uriTarget
    ) -PassThru -Wait
    if ($uriRun.ExitCode -ne 0) {
        throw "Playnite ProcessStarter URI probe returned $($uriRun.ExitCode)."
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $handlerReceipt) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $handlerReceipt)) {
        throw "Playnite StartUrl did not reach the registered URI handler."
    }
    $receivedUri = (Get-Content -LiteralPath $handlerReceipt -Raw).Trim()
    if ($receivedUri -ne $uriTarget) {
        throw "Playnite StartUrl URI target mismatch."
    }

    $receipt.file_handler_received_exact_target = $true
    $receipt.uri_handler_received_exact_target = $true
    $receipt.result = "PASS"
}
finally {
    Remove-Item Env:\RDTE_PLAYNITE_OPEN_TARGET_RECEIPT -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $progIdKey -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $extensionKey -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $uriKey -Recurse -Force -ErrorAction SilentlyContinue
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $EvidenceDir "playnite-open-target-receipt.json") -Encoding UTF8
}

if ($receipt.result -ne "PASS") {
    exit 1
}
