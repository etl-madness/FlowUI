<#
.SYNOPSIS
    Cross-publishes Unix .NET 10 FlowUI payloads, stages compressed man/info documentation,
    builds Windows .zip/.msi + WinGet manifests, and builds .deb/.rpm/Arch packages via nfpm.

.DESCRIPTION
    Useful on Windows workstations or CI runners to verify cross-publishing for
    Linux, macOS, and FreeBSD (framework-dependent), compress man/info documentation,
    build Windows packages, and optionally produce .deb, .rpm, and Arch .pkg.tar.zst
    packages via nfpm without WSL.

.EXAMPLE
    .\packaging\scripts\Build-Packages.ps1 -Target stage-all
    .\packaging\scripts\Build-Packages.ps1 -Target windows -Arch amd64
#>
[CmdletBinding()]
param(
    [ValidateSet("stage-all", "linux", "darwin", "freebsd", "openbsd", "windows", "winget", "msi", "zip", "deb", "rpm", "arch", "archlinux")]
    [string]$Target = "stage-all",

    [ValidateSet("amd64", "arm64")]
    [string]$Arch = "amd64",

    [string]$Version = "",

    [string]$Release = "1"
)

$ErrorActionPreference = "Stop"

$ScriptsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PackagingDir = Split-Path -Parent $ScriptsDir
$RepoRoot = Split-Path -Parent $PackagingDir
$CsprojPath = Join-Path $RepoRoot "FlowUI\FlowUI.csproj"
$BuildRoot = Join-Path $PackagingDir "build"
$DistDir = Join-Path $PackagingDir "dist"

if ([string]::IsNullOrWhiteSpace($Version)) {
    try {
        $rawTag = git -C $RepoRoot describe --tags --abbrev=0 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($rawTag)) {
            $Version = $rawTag -replace '^[vV]', ''
        }
    } catch { }
}
if ([string]::IsNullOrWhiteSpace($Version) -and (Test-Path $CsprojPath)) {
    [xml]$csprojXml = Get-Content -Path $CsprojPath
    $verNode = $csprojXml.Project.PropertyGroup.Version | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
    if ($verNode) { $Version = $verNode.Trim() }
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "1.3.1"
}

function Compress-GzipFile {
    param([string]$SourcePath, [string]$DestPath)
    $destParent = Split-Path -Parent $DestPath
    if (-not (Test-Path $destParent)) {
        New-Item -ItemType Directory -Path $destParent -Force | Out-Null
    }
    $inBytes = [System.IO.File]::ReadAllBytes($SourcePath)
    $outStream = [System.IO.File]::Create($DestPath)
    try {
        $gzipStream = New-Object System.IO.Compression.GZipStream($outStream, [System.IO.Compression.CompressionLevel]::Optimal)
        try {
            $gzipStream.Write($inBytes, 0, $inBytes.Length)
        } finally {
            $gzipStream.Dispose()
        }
    } finally {
        $outStream.Dispose()
    }
}

function Stage-Docs {
    param([string]$DocDir)
    Compress-GzipFile (Join-Path $PackagingDir "man\man1\flow-ui.1") (Join-Path $DocDir "man1\flow-ui.1.gz")
    Compress-GzipFile (Join-Path $PackagingDir "man\man5\flow-ui.env.5") (Join-Path $DocDir "man5\flow-ui.env.5.gz")
    Compress-GzipFile (Join-Path $PackagingDir "info\flow-ui.info") (Join-Path $DocDir "info\flow-ui.info.gz")
    Copy-Item -Force (Join-Path $PackagingDir "info\dir") (Join-Path $DocDir "info\dir")
}

function Write-UnixLauncher {
    param([string]$BinDir, [string]$Mode = "self-contained")
    New-Item -ItemType Directory -Path $BinDir -Force | Out-Null
    $content = if ($Mode -eq "framework-dependent") {
        "#!/bin/sh`nset -eu`nDOTNET_BIN=`"`${DOTNET_BIN:-`$(command -v dotnet 2>/dev/null || true)}`"`nif [ -z `"`$DOTNET_BIN`" ] && [ -x /usr/local/bin/dotnet ]; then`n    DOTNET_BIN=`"/usr/local/bin/dotnet`"`nfi`nif [ -z `"`$DOTNET_BIN`" ]; then`n    printf '[ERROR] dotnet (.NET 10 ASP.NET Core runtime) was not found in PATH or /usr/local/bin/dotnet.\n' >&2`n    exit 1`nfi`nexec `"`$DOTNET_BIN`" /opt/flow-ui/app/FlowUI.dll --contentRoot /opt/flow-ui/app `"`$@`"`n"
    } else {
        "#!/bin/sh`nset -eu`nexec /opt/flow-ui/app/FlowUI --contentRoot /opt/flow-ui/app `"`$@`"`n"
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $BinDir "flow-ui"), $content, $utf8NoBom)
}

function Publish-FlowUI {
    param([string]$TargetOS, [string]$TargetArch, [string]$StageBase)
    $appDir = Join-Path $StageBase "app"
    $binDir = Join-Path $StageBase "bin"
    if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
    New-Item -ItemType Directory -Path $appDir -Force | Out-Null

    if ($TargetOS -in @("freebsd", "openbsd")) {
        Write-Host "[INFO] Publishing framework-dependent FlowUI for $TargetOS (net10.0)..."
        & dotnet publish $CsprojPath `
            -c Release `
            --no-self-contained `
            -p:UseAppHost=false `
            -p:DebugType=none `
            -p:DebugSymbols=false `
            -o $appDir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $TargetOS" }
        Write-UnixLauncher -BinDir $binDir -Mode "framework-dependent"
    } else {
        $rid = switch ("$TargetOS/$TargetArch") {
            "linux/amd64"  { "linux-x64" }
            "linux/arm64"  { "linux-arm64" }
            "darwin/amd64" { "osx-x64" }
            "darwin/arm64" { "osx-arm64" }
            default { throw "Unsupported target: $TargetOS/$TargetArch" }
        }
        Write-Host "[INFO] Publishing self-contained FlowUI for $rid..."
        & dotnet publish $CsprojPath `
            -c Release `
            -r $rid `
            --self-contained true `
            -p:PublishSingleFile=false `
            -p:DebugType=none `
            -p:DebugSymbols=false `
            -o $appDir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }
        Write-UnixLauncher -BinDir $binDir -Mode "self-contained"
    }
}

$DocStage = Join-Path $BuildRoot "docs"
Stage-Docs -DocDir $DocStage
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

switch ($Target) {
    "stage-all" {
        Publish-FlowUI -TargetOS "linux"   -TargetArch $Arch -StageBase (Join-Path $BuildRoot "stage\linux-$Arch")
        Publish-FlowUI -TargetOS "darwin"  -TargetArch $Arch -StageBase (Join-Path $BuildRoot "stage\darwin-$Arch")
        Publish-FlowUI -TargetOS "freebsd" -TargetArch $Arch -StageBase (Join-Path $BuildRoot "stage\freebsd-$Arch")
        Publish-FlowUI -TargetOS "openbsd" -TargetArch $Arch -StageBase (Join-Path $BuildRoot "stage\openbsd-$Arch")
        Write-Host "[INFO] Staged FlowUI payloads and compressed man/info docs in $BuildRoot"
    }
    "linux" {
        Publish-FlowUI -TargetOS "linux" -TargetArch $Arch -StageBase (Join-Path $BuildRoot "stage\linux-$Arch")
    }
    "darwin" {
        Publish-FlowUI -TargetOS "darwin" -TargetArch $Arch -StageBase (Join-Path $BuildRoot "stage\darwin-$Arch")
    }
    "freebsd" {
        Publish-FlowUI -TargetOS "freebsd" -TargetArch $Arch -StageBase (Join-Path $BuildRoot "stage\freebsd-$Arch")
    }
    "openbsd" {
        Publish-FlowUI -TargetOS "openbsd" -TargetArch $Arch -StageBase (Join-Path $BuildRoot "stage\openbsd-$Arch")
    }
    { $_ -in @("windows", "winget", "msi", "zip") } {
        $winFmt = if ($_ -in @("msi", "zip")) { $_ } else { "all" }
        & (Join-Path $ScriptsDir "Build-Windows.ps1") -Format $winFmt -Arch $Arch -Version $Version
    }
    { $_ -in @("deb", "rpm", "arch", "archlinux") } {
        $packager = if ($_ -in @("arch", "archlinux")) { "archlinux" } else { $_ }
        if (-not (Get-Command nfpm -ErrorAction SilentlyContinue)) {
            $scriptHint = if ($packager -eq "archlinux") { "build-arch.sh" } else { "build-$packager.sh" }
            throw "nfpm is required on Windows to build $Target directly (or run packaging/scripts/$scriptHint on Linux/WSL)."
        }
        $stageBase = Join-Path $BuildRoot "stage\linux-$Arch"
        Publish-FlowUI -TargetOS "linux" -TargetArch $Arch -StageBase $stageBase

        $env:REPO_ROOT = $RepoRoot
        $env:PKG_NAME = "flow-ui"
        $env:STAGE_APP_DIR = Join-Path $stageBase "app"
        $env:STAGE_BIN_DIR = Join-Path $stageBase "bin"
        $env:STAGE_DOC_DIR = $DocStage
        $env:NFPM_ARCH = $Arch
        $env:FLOW_RELEASE = $Release

        if ($packager -eq "deb") {
            $env:FLOW_VERSION = $Version
            $outFile = Join-Path $DistDir "flow-ui_${Version}-${Release}_${Arch}.deb"
        } elseif ($packager -eq "rpm") {
            $env:FLOW_VERSION = ($Version -replace '-', '~')
            $rpmArch = if ($Arch -eq "amd64") { "x86_64" } else { "aarch64" }
            $outFile = Join-Path $DistDir "flow-ui-$($env:FLOW_VERSION)-${Release}.${rpmArch}.rpm"
        } else {
            $env:FLOW_VERSION = ($Version -replace '-', '_')
            $archCpu = if ($Arch -eq "amd64") { "x86_64" } else { "aarch64" }
            $outFile = Join-Path $DistDir "flow-ui-$($env:FLOW_VERSION)-${Release}-${archCpu}.pkg.tar.zst"
        }

        nfpm package --config (Join-Path $PackagingDir "nfpm.yaml") --packager $packager --target $outFile
        if ($LASTEXITCODE -ne 0) { throw "nfpm failed for $packager" }
        Write-Host "[INFO] Created package: $outFile"
    }
}
