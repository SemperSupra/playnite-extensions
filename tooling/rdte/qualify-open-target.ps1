param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDir
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null
$work = Join-Path $env:RUNNER_TEMP ("open-target-rdte-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $work | Out-Null

$handlerSource = @'
using System;
using System.IO;

public static class Handler
{
    public static int Main(string[] args)
    {
        var receipt = Environment.GetEnvironmentVariable("RDTE_OPEN_TARGET_RECEIPT");
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
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

public static class OpenTargetProbe
{
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            return 2;
        }

        var target = args[0];
        Uri uri;
        var isUri = Uri.TryCreate(target, UriKind.Absolute, out uri) &&
            !String.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase);

        if (!isUri)
        {
            target = Path.GetFullPath(target);
            if (!File.Exists(target) && !Directory.Exists(target))
            {
                return 10;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
            return 0;
        }
        catch (Win32Exception)
        {
            return 20;
        }
    }
}
'@

$handlerCs = Join-Path $work "handler.cs"
$invokerCs = Join-Path $work "open-target-probe.cs"
$handlerExe = Join-Path $work "handler.exe"
$invokerExe = Join-Path $work "open-target-probe.exe"
$target = Join-Path $work "fixture.rdtepdf"
$handlerReceipt = Join-Path $work "handler-receipt.txt"

Set-Content -LiteralPath $handlerCs -Value $handlerSource -Encoding UTF8
Set-Content -LiteralPath $invokerCs -Value $invokerSource -Encoding UTF8
Set-Content -LiteralPath $target -Value "%PDF-1.4`n% SemperSupra OPEN_TARGET RDTE fixture`n" -Encoding ASCII

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "Required .NET Framework compiler is unavailable: $csc"
}

& $csc /nologo /target:exe /out:$handlerExe $handlerCs
if ($LASTEXITCODE -ne 0) { throw "Handler compilation failed." }
& $csc /nologo /target:exe /out:$invokerExe $invokerCs
if ($LASTEXITCODE -ne 0) { throw "Invoker compilation failed." }

$classesRoot = "HKCU:\Software\Classes"
$extensionKey = Join-Path $classesRoot ".rdtepdf"
$progId = "SemperSupra.RDTE.Pdf"
$progIdKey = Join-Path $classesRoot $progId
$commandKey = Join-Path $progIdKey "shell\open\command"
$uriScheme = "rdteopen"
$uriKey = Join-Path $classesRoot $uriScheme
$uriCommandKey = Join-Path $uriKey "shell\open\command"

$extensionExisted = Test-Path -LiteralPath $extensionKey
$previousDefault = $null
if ($extensionExisted) {
    $previousDefault = (Get-Item -LiteralPath $extensionKey).GetValue("")
}

$receipt = [ordered]@{
    schema = "sempersupra-open-target-rdte/v1"
    source_sha = if ($env:RDTE_SOURCE_SHA) { $env:RDTE_SOURCE_SHA } elseif ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { "" }
    target_kind = "local-file"
    target_extension = ".rdtepdf"
    payload_format = "pdf"
    shell_execute = $true
    result = "RUNNING"
}

try {
    New-Item -Path $extensionKey -Force | Out-Null
    Set-Item -Path $extensionKey -Value $progId

    New-Item -Path $commandKey -Force | Out-Null
    Set-Item -Path $commandKey -Value ('"' + $handlerExe + '" "%1"')

    $env:RDTE_OPEN_TARGET_RECEIPT = $handlerReceipt
    Remove-Item -LiteralPath $handlerReceipt -Force -ErrorAction SilentlyContinue

    $opened = Start-Process -FilePath $invokerExe -ArgumentList @($target) -PassThru -Wait
    if ($opened.ExitCode -ne 0) {
        throw "Shell-open probe returned exit code $($opened.ExitCode)."
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $handlerReceipt) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $handlerReceipt)) {
        throw "Registered isolated file handler did not receive the PDF-format target."
    }

    $received = (Get-Content -LiteralPath $handlerReceipt -Raw).Trim()
    $expected = [IO.Path]::GetFullPath($target)
    if ($received -ne $expected) {
        throw "Registered handler received the wrong target."
    }

    Remove-Item -LiteralPath $handlerReceipt -Force
    $missing = Join-Path $work "missing.rdtepdf"
    $missingRun = Start-Process -FilePath $invokerExe -ArgumentList @($missing) -PassThru -Wait
    if ($missingRun.ExitCode -ne 10) {
        throw "Missing-target guard returned $($missingRun.ExitCode), expected 10."
    }
    if (Test-Path -LiteralPath $handlerReceipt) {
        throw "Handler was invoked for a missing target."
    }

    $unassociated = Join-Path $work "fixture.rdtenone"
    Set-Content -LiteralPath $unassociated -Value "SemperSupra unassociated target" -Encoding ASCII
    $unassociatedRun = Start-Process -FilePath $invokerExe -ArgumentList @($unassociated) -PassThru -Wait
    if ($unassociatedRun.ExitCode -ne 0 -and $unassociatedRun.ExitCode -ne 20) {
        throw "Unexpected unassociated-target dispatch exit code $($unassociatedRun.ExitCode)."
    }
    if (Test-Path -LiteralPath $handlerReceipt) {
        throw "Known RDTE handler was invoked for an unassociated target."
    }

    New-Item -Path $uriCommandKey -Force | Out-Null
    Set-Item -Path $uriKey -Value "URL:SemperSupra RDTE protocol"
    New-ItemProperty -Path $uriKey -Name "URL Protocol" -Value "" -PropertyType String -Force | Out-Null
    Set-Item -Path $uriCommandKey -Value ('"' + $handlerExe + '" "%1"')

    $uriTarget = "rdteopen://fixture/media?id=42"
    Remove-Item -LiteralPath $handlerReceipt -Force -ErrorAction SilentlyContinue
    $uriRun = Start-Process -FilePath $invokerExe -ArgumentList @($uriTarget) -PassThru -Wait
    if ($uriRun.ExitCode -ne 0) {
        throw "URI shell-open probe returned exit code $($uriRun.ExitCode)."
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $handlerReceipt) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $handlerReceipt)) {
        throw "Registered URI handler did not receive the target."
    }
    $receivedUri = (Get-Content -LiteralPath $handlerReceipt -Raw).Trim()
    if ($receivedUri -ne $uriTarget) {
        throw "Registered URI handler received the wrong target."
    }

    $receipt.handler_received_exact_target = $true
    $receipt.missing_target_exit_code = $missingRun.ExitCode
    $receipt.unassociated_target_exit_code = $unassociatedRun.ExitCode
    $receipt.unassociated_policy = "delegate-to-os"
    $receipt.uri_handler_received_exact_target = $true
    $receipt.result = "PASS"
}
finally {
    Remove-Item Env:\RDTE_OPEN_TARGET_RECEIPT -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $progIdKey -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $uriKey -Recurse -Force -ErrorAction SilentlyContinue

    if ($extensionExisted) {
        New-Item -Path $extensionKey -Force | Out-Null
        Set-Item -Path $extensionKey -Value $previousDefault
    }
    else {
        Remove-Item -LiteralPath $extensionKey -Recurse -Force -ErrorAction SilentlyContinue
    }

    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $EvidenceDir "open-target-receipt.json") -Encoding UTF8
}

if ($receipt.result -ne "PASS") {
    exit 1
}
