<#
.SYNOPSIS
    Builds Windows WinGet-compatible packages (.zip portable archive and .msi installer)
    and generates validated WinGet 1.6.0 multi-file YAML manifests for ETLMadness.FlowUI.

.DESCRIPTION
    Publishes self-contained .NET 10 FlowUI.exe (with native Windows SCM service support),
    bundles wwwroot static assets, reference guides, man pages, and PowerShell Windows
    Service setup scripts, builds:
      1. flow-ui-<version>-windows-<arch>.zip (WinGet NestedInstallerType: portable)
      2. flow-ui-<version>-windows-<arch>.msi (WiX v4/v5/v6 'wix', WiX v3 'candle/light', or 'wixl')
    and generates WinGet manifests in packaging/dist/winget/e/ETLMadness/FlowUI/<version>/.

.EXAMPLE
    .\packaging\scripts\Build-Windows.ps1
    .\packaging\scripts\Build-Windows.ps1 -Format all -Arch both
    .\packaging\scripts\Build-Windows.ps1 -Format msi -Arch amd64 -RequireMsi
#>
[CmdletBinding()]
param(
    [ValidateSet("all", "zip", "msi")]
    [string]$Format = "all",

    [ValidateSet("amd64", "arm64", "both")]
    [string]$Arch = "amd64",

    [string]$Version = "",

    [string]$ReleaseBaseUrl = "",

    [string]$SignCertThumbprint = "",

    [string]$TimestampServer = "http://timestamp.digicert.com",

    [switch]$RequireMsi,

    [switch]$SkipWingetValidate
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$ScriptsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PackagingDir = Split-Path -Parent $ScriptsDir
$RepoRoot = Split-Path -Parent $PackagingDir
$CsprojPath = Join-Path $RepoRoot "FlowUI\FlowUI.csproj"
$BuildRoot = Join-Path $PackagingDir "build"
$DistDir = Join-Path $PackagingDir "dist"
$UpgradeCode = "{8F3A9C12-6D4E-4B8A-9E21-5C7B2A1F04D9}"

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

if ([string]::IsNullOrWhiteSpace($ReleaseBaseUrl)) {
    $ReleaseBaseUrl = "https://github.com/etl-madness/flowUI/releases/download/v$Version"
}
$ReleaseBaseUrl = $ReleaseBaseUrl.TrimEnd('/')

function Get-MsiNumericVersion {
    param([string]$Ver)
    $clean = ($Ver -split '[-+]')[0]
    $parts = $clean -split '\.'
    $nums = @(0, 0, 0)
    for ($i = 0; $i -lt [Math]::Min(3, $parts.Count); $i++) {
        $val = 0
        if ([int]::TryParse($parts[$i], [ref]$val)) {
            $nums[$i] = [Math]::Min(65535, [Math]::Max(0, $val))
        }
    }
    return "$($nums[0]).$($nums[1]).$($nums[2])"
}

function Get-DeterministicProductCode {
    param([string]$Ver, [string]$ArchName)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes("ETLMadness.FlowUI.MSI:$Ver`:$ArchName")
        $hash = $sha.ComputeHash($bytes)
        $guidBytes = New-Object byte[] 16
        [Array]::Copy($hash, 0, $guidBytes, 0, 16)
        $guidBytes[7] = ($guidBytes[7] -band 0x0F) -bor 0x50
        $guidBytes[8] = ($guidBytes[8] -band 0x3F) -bor 0x80
        $g = New-Object System.Guid (,$guidBytes)
        return "{$($g.ToString().ToUpperInvariant())}"
    } finally {
        $sha.Dispose()
    }
}

function Invoke-AuthenticodeSign {
    param([string]$FilePath)
    if ([string]::IsNullOrWhiteSpace($SignCertThumbprint)) {
        return
    }
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) {
        throw "SignCertThumbprint was provided, but signtool.exe was not found in PATH."
    }
    Write-Host "[INFO] Signing $FilePath..."
    & $signtool.Source sign /sha1 $SignCertThumbprint /fd SHA256 /tr $TimestampServer /td SHA256 $FilePath
    if ($LASTEXITCODE -ne 0) {
        throw "signtool.exe failed on $FilePath"
    }
}

function Stage-WindowsPayload {
    param([string]$TargetArch, [string]$StageDir)
    if (Test-Path $StageDir) {
        Remove-Item -Recurse -Force $StageDir
    }
    $rid = if ($TargetArch -eq "amd64") { "win-x64" } else { "win-arm64" }
    $appDir = Join-Path $StageDir "app"
    $docsDir = Join-Path $StageDir "docs"
    $winSvcDir = Join-Path $StageDir "share\services\windows"
    $man1Dir = Join-Path $StageDir "share\man\man1"
    $man5Dir = Join-Path $StageDir "share\man\man5"

    New-Item -ItemType Directory -Path $appDir, $docsDir, $winSvcDir, $man1Dir, $man5Dir -Force | Out-Null

    Write-Host "[INFO] Publishing self-contained FlowUI for $rid..."
    & dotnet publish $CsprojPath `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $appDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid"
    }

    Invoke-AuthenticodeSign (Join-Path $appDir "FlowUI.exe")

    Copy-Item -Force (Join-Path $RepoRoot "README.md") (Join-Path $docsDir "README.md")
    Copy-Item -Force (Join-Path $RepoRoot "DOCKER.md") (Join-Path $docsDir "DOCKER.md")

    Copy-Item -Force (Join-Path $PackagingDir "services\windows\Install-FlowUIService.ps1") (Join-Path $winSvcDir "Install-FlowUIService.ps1")
    Copy-Item -Force (Join-Path $PackagingDir "services\windows\Uninstall-FlowUIService.ps1") (Join-Path $winSvcDir "Uninstall-FlowUIService.ps1")

    $envTemplate = [System.IO.File]::ReadAllText((Join-Path $PackagingDir "etc\flow-ui\flow-ui.env"))
    $winEnvTemplate = $envTemplate -replace '/etc/flow-ui', 'C:/ProgramData/ETL Madness/Flow UI'
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $winSvcDir "flow-ui.env.example"), $winEnvTemplate, $utf8NoBom)

    Copy-Item -Force (Join-Path $PackagingDir "man\man1\flow-ui.1") (Join-Path $man1Dir "flow-ui.1")
    Copy-Item -Force (Join-Path $PackagingDir "man\man5\flow-ui.env.5") (Join-Path $man5Dir "flow-ui.env.5")
    Copy-Item -Force (Join-Path $RepoRoot "LICENSE.txt") (Join-Path $StageDir "LICENSE.txt")
}

function Write-Wix3AppFragment {
    param([string]$AppDir, [string]$OutWxs)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
    [void]$sb.AppendLine('<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">')
    [void]$sb.AppendLine('  <Fragment>')
    [void]$sb.AppendLine('    <DirectoryRef Id="AppFolder">')

    $dirIdMap = @{ "" = "AppFolder" }
    $dirCounter = 0
    $baseLen = $AppDir.TrimEnd('\', '/').Length + 1

    # Emit nested Directory structure recursively
    function Emit-DirXml {
        param([string]$CurrentPath, [string]$Indent)
        $subDirs = Get-ChildItem -Path $CurrentPath -Directory | Sort-Object Name
        foreach ($d in $subDirs) {
            $script:dirCounter++
            $rel = $d.FullName.Substring($baseLen)
            $dirId = "AppSubDir_$($script:dirCounter)"
            $dirIdMap[$rel] = $dirId
            $escapedName = [System.Security.SecurityElement]::Escape($d.Name)
            [void]$sb.AppendLine("$Indent<Directory Id=`"$dirId`" Name=`"$escapedName`">")
            Emit-DirXml -CurrentPath $d.FullName -Indent ($Indent + "  ")
            [void]$sb.AppendLine("$Indent</Directory>")
        }
    }
    Emit-DirXml -CurrentPath $AppDir -Indent "      "

    [void]$sb.AppendLine('    </DirectoryRef>')
    [void]$sb.AppendLine('    <ComponentGroup Id="FlowUIAppFiles">')

    $fileCounter = 0
    Get-ChildItem -Path $AppDir -Recurse -File | Sort-Object FullName | ForEach-Object {
        $fileCounter++
        $parentPath = $_.DirectoryName
        $relDir = if ($parentPath.Length -le $AppDir.TrimEnd('\', '/').Length) { "" } else { $parentPath.Substring($baseLen) }
        $targetDirId = $dirIdMap[$relDir]
        $escapedSrc = [System.Security.SecurityElement]::Escape($_.FullName)
        [void]$sb.AppendLine("      <Component Id=`"CmpAppFile_$fileCounter`" Directory=`"$targetDirId`" Guid=`"*`">")
        [void]$sb.AppendLine("        <File Id=`"FilAppFile_$fileCounter`" Source=`"$escapedSrc`" KeyPath=`"yes`" />")
        [void]$sb.AppendLine("      </Component>")
    }

    [void]$sb.AppendLine('    </ComponentGroup>')
    [void]$sb.AppendLine('  </Fragment>')
    [void]$sb.AppendLine('</Wix>')

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($OutWxs, $sb.ToString(), $utf8NoBom)
}

function Build-PortableZip {
    param([string]$StageDir, [string]$OutZip)
    if (Test-Path $OutZip) {
        Remove-Item -Force $OutZip
    }
    Write-Host "[INFO] Creating portable archive: $OutZip"
    $zipStream = [System.IO.File]::Open($OutZip, [System.IO.FileMode]::Create)
    try {
        $archive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            $baseLen = $StageDir.TrimEnd('\', '/').Length + 1
            Get-ChildItem -Path $StageDir -Recurse -File | Sort-Object FullName | ForEach-Object {
                $relPath = $_.FullName.Substring($baseLen) -replace '\\', '/'
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive,
                    $_.FullName,
                    $relPath,
                    [System.IO.Compression.CompressionLevel]::Optimal
                ) | Out-Null
            }
        } finally {
            $archive.Dispose()
        }
    } finally {
        $zipStream.Dispose()
    }
}

function Build-WindowsMsi {
    param([string]$TargetArch, [string]$StageDir, [string]$OutMsi, [string]$ProductCode)
    $msiVersion = Get-MsiNumericVersion -Ver $Version
    $wixArch = if ($TargetArch -eq "amd64") { "x64" } else { "arm64" }

    $wixCmd = Get-Command wix -ErrorAction SilentlyContinue
    if (-not $wixCmd) {
        $dotnetToolWix = Join-Path $HOME ".dotnet\tools\wix.exe"
        if (Test-Path $dotnetToolWix) {
            $wixCmd = Get-Item $dotnetToolWix
        }
    }

    $candleCmd = Get-Command candle.exe -ErrorAction SilentlyContinue
    $lightCmd = Get-Command light.exe -ErrorAction SilentlyContinue
    $wixlCmd = Get-Command wixl -ErrorAction SilentlyContinue

    if ($wixCmd) {
        $wixExe = if ($wixCmd.PSObject.Properties['Source']) { $wixCmd.Source } else { $wixCmd.FullName }
        Write-Host "[INFO] Building MSI with WiX Toolset ($wixExe) for $wixArch..."
        $wxsPath = Join-Path $PackagingDir "windows\flow-ui.wxs"
        & $wixExe build $wxsPath `
            -arch $wixArch `
            -d "StageDir=$StageDir" `
            -d "FlowMsiVersion=$msiVersion" `
            -d "FlowProductCode=$ProductCode" `
            -o $OutMsi
        if ($LASTEXITCODE -ne 0) {
            throw "wix build failed for $OutMsi"
        }
        Invoke-AuthenticodeSign $OutMsi
        return $true
    } elseif ($candleCmd -and $lightCmd) {
        Write-Host "[INFO] Building MSI with WiX v3 (candle/light) for $wixArch..."
        $workParent = Split-Path -Parent $StageDir
        $wxs3Path = Join-Path $PackagingDir "windows\flow-ui-wix3.wxs"
        $appFragWxs = Join-Path $workParent "app-files-wix3.wxs"
        Write-Wix3AppFragment -AppDir (Join-Path $StageDir "app") -OutWxs $appFragWxs

        $wixObjMain = Join-Path $workParent "flow-ui.wixobj"
        $wixObjApp = Join-Path $workParent "app-files-wix3.wixobj"

        & $candleCmd.Source -nologo -arch $wixArch `
            "-dStageDir=$StageDir" `
            "-dFlowMsiVersion=$msiVersion" `
            "-dFlowProductCode=$ProductCode" `
            -out $wixObjMain $wxs3Path
        if ($LASTEXITCODE -ne 0) { throw "candle.exe failed on flow-ui-wix3.wxs" }

        & $candleCmd.Source -nologo -arch $wixArch -out $wixObjApp $appFragWxs
        if ($LASTEXITCODE -ne 0) { throw "candle.exe failed on app-files-wix3.wxs" }

        & $lightCmd.Source -nologo -sval -out $OutMsi $wixObjMain $wixObjApp
        if ($LASTEXITCODE -ne 0) { throw "light.exe failed" }
        Invoke-AuthenticodeSign $OutMsi
        return $true
    } elseif ($wixlCmd) {
        Write-Host "[INFO] Building MSI with wixl (msitools) for $wixArch..."
        $workParent = Split-Path -Parent $StageDir
        $wxs3Path = Join-Path $PackagingDir "windows\flow-ui-wix3.wxs"
        $appFragWxs = Join-Path $workParent "app-files-wix3.wxs"
        Write-Wix3AppFragment -AppDir (Join-Path $StageDir "app") -OutWxs $appFragWxs
        & $wixlCmd.Source --arch $wixArch `
            -D "StageDir=$StageDir" `
            -D "FlowMsiVersion=$msiVersion" `
            -D "FlowProductCode=$ProductCode" `
            -o $OutMsi $wxs3Path $appFragWxs
        if ($LASTEXITCODE -ne 0) { throw "wixl failed" }
        return $true
    } else {
        $msg = "WiX Toolset not found. Install WiX v5/v6 via 'dotnet tool install --global wix' (or 'choco install wixtoolset') to build .msi installers."
        if ($Format -eq "msi" -or $RequireMsi) {
            throw $msg
        }
        Write-Warning "$msg Skipping .msi build (portable .zip and WinGet manifests will still be generated)."
        return $false
    }
}

New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

$archList = if ($Arch -eq "both") { @("amd64", "arm64") } else { @($Arch) }
$installerEntries = New-Object System.Collections.Generic.List[string]

foreach ($targetArch in $archList) {
    $wingetArch = if ($targetArch -eq "amd64") { "x64" } else { "arm64" }
    $workDir = Join-Path $BuildRoot "windows-$targetArch"
    $stageDir = Join-Path $workDir "payload"

    Stage-WindowsPayload -TargetArch $targetArch -StageDir $stageDir

    if ($Format -in @("all", "zip")) {
        $zipName = "flow-ui-$Version-windows-$targetArch.zip"
        $zipPath = Join-Path $DistDir $zipName
        Build-PortableZip -StageDir $stageDir -OutZip $zipPath
        $zipSha256 = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToUpperInvariant()

        $zipBlock = @"
  - Architecture: $wingetArch
    InstallerType: zip
    NestedInstallerType: portable
    NestedInstallerFiles:
      - RelativeFilePath: app\FlowUI.exe
        PortableCommandAlias: flow-ui
    InstallerUrl: $ReleaseBaseUrl/$zipName
    InstallerSha256: $zipSha256
"@
        $installerEntries.Add($zipBlock.TrimEnd())
    }

    if ($Format -in @("all", "msi")) {
        $msiName = "flow-ui-$Version-windows-$targetArch.msi"
        $msiPath = Join-Path $DistDir $msiName
        $productCode = Get-DeterministicProductCode -Ver $Version -ArchName $targetArch
        $msiBuilt = Build-WindowsMsi -TargetArch $targetArch -StageDir $stageDir -OutMsi $msiPath -ProductCode $productCode
        if ($msiBuilt -and (Test-Path $msiPath)) {
            $msiSha256 = (Get-FileHash -Algorithm SHA256 -Path $msiPath).Hash.ToUpperInvariant()
            $msiBlock = @"
  - Architecture: $wingetArch
    Scope: machine
    InstallerType: wix
    InstallerUrl: $ReleaseBaseUrl/$msiName
    InstallerSha256: $msiSha256
    ProductCode: '$productCode'
    AppsAndFeaturesEntries:
      - DisplayName: ETL Madness FlowUI
        Publisher: ETL Madness
        DisplayVersion: $(Get-MsiNumericVersion -Ver $Version)
        ProductCode: '$productCode'
        UpgradeCode: '$UpgradeCode'
"@
            $installerEntries.Add($msiBlock.TrimEnd())
        }
    }
}

if ($installerEntries.Count -eq 0) {
    throw "No Windows installer artifacts were produced."
}

$wingetManifestDir = Join-Path $DistDir "winget\e\ETLMadness\FlowUI\$Version"
New-Item -ItemType Directory -Path $wingetManifestDir -Force | Out-Null

$versionYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json

PackageIdentifier: ETLMadness.FlowUI
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@

$localeYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json

PackageIdentifier: ETLMadness.FlowUI
PackageVersion: $Version
PackageLocale: en-US
Publisher: ETL Madness
PublisherUrl: https://github.com/etl-madness
PublisherSupportUrl: https://github.com/etl-madness/flowUI/issues
PackageName: ETL Madness FlowUI
PackageUrl: https://github.com/etl-madness/flowUI
License: MIT
LicenseUrl: https://github.com/etl-madness/flowUI/blob/main/LICENSE.txt
ShortDescription: Interactive .NET 10 Blazor Server visual studio (Flow Builder Studio) for ETL Madness go-flow and go-flowAPI.
Description: >-
  ETL Madness FlowUI (Flow Builder Studio) is an interactive .NET 10 ASP.NET Core
  Blazor Server web application and native Windows Service for visually designing,
  configuring, signing, encrypting, validating, executing, and monitoring XML ETL
  pipelines powered by go-flow and go-flowAPI.
Moniker: flow-ui
Tags:
  - blazor
  - dotnet
  - etl
  - pipeline
  - studio
  - ui
  - workflow
Documentations:
  - DocumentLabel: Online Documentation
    DocumentUrl: https://etl-madness.github.io/flow-docs/
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@

$joinedInstallers = ($installerEntries -join "`n")
$installerYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json

PackageIdentifier: ETLMadness.FlowUI
PackageVersion: $Version
Commands:
  - flow-ui
Installers:
$joinedInstallers
ManifestType: installer
ManifestVersion: 1.6.0
"@

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $wingetManifestDir "ETLMadness.FlowUI.yaml"), ($versionYaml -replace "`r`n", "`n") + "`n", $utf8NoBom)
[System.IO.File]::WriteAllText((Join-Path $wingetManifestDir "ETLMadness.FlowUI.locale.en-US.yaml"), ($localeYaml -replace "`r`n", "`n") + "`n", $utf8NoBom)
[System.IO.File]::WriteAllText((Join-Path $wingetManifestDir "ETLMadness.FlowUI.installer.yaml"), ($installerYaml -replace "`r`n", "`n") + "`n", $utf8NoBom)

Write-Host "[INFO] Generated WinGet manifests in: $wingetManifestDir"

if (-not $SkipWingetValidate) {
    $wingetCmd = Get-Command winget.exe -ErrorAction SilentlyContinue
    if ($wingetCmd) {
        Write-Host "[INFO] Validating WinGet manifests with winget validate..."
        & $wingetCmd.Source validate --manifest $wingetManifestDir
        if ($LASTEXITCODE -ne 0) {
            throw "winget validate reported manifest errors in $wingetManifestDir"
        }
    }
}
