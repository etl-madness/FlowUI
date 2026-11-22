<#
.SYNOPSIS
    Installs or updates the native Windows Service for ETL Madness FlowUI (Flow Builder Studio).

.DESCRIPTION
    FlowUI.exe natively implements the Windows Service Control Manager (SCM)
    dispatcher via Microsoft.Extensions.Hosting.WindowsServices (no NSSM required).
    This script:
      1. Creates C:\ProgramData\ETL Madness\Flow UI\{data,logs} and seeds flow-ui.env if missing.
      2. Grants read/modify ACLs on C:\ProgramData\ETL Madness\Flow UI\ to the chosen service account.
      3. Registers (or updates) the 'FlowUI' Windows Service with automatic delayed start
         and automatic failure recovery (restart after 5s / 10s / 30s).
      4. Supports NT AUTHORITY\NetworkService (default), LocalSystem, LocalService,
         gMSA (DOMAIN\gmsa$), or an Active Directory domain user (DOMAIN\svc_flowui).

.EXAMPLE
    # Run from an elevated (Administrator) PowerShell prompt:
    .\Install-FlowUIService.ps1 -ApiUrl "http://localhost:8080" -Start

.EXAMPLE
    # Run as an Active Directory domain user with Windows Auth mode:
    $cred = Get-Credential "EXAMPLE\svc_flowui"
    .\Install-FlowUIService.ps1 -Credential $cred -ApiUrl "http://flow-api.example.com:8080" -AuthMode Windows -Start
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "FlowUI",

    [string]$DisplayName = "ETL Madness FlowUI Service",

    [string]$BinaryPath = "",

    [string]$ProgramDataDir = "$env:ProgramData\ETL Madness\Flow UI",

    [string]$ListenUrls = "http://0.0.0.0:5185",

    [string]$ApiUrl = "http://localhost:8080",

    [ValidateSet("ApiBasic", "Windows")]
    [string]$AuthMode = "ApiBasic",

    [ValidateSet("NetworkService", "LocalSystem", "LocalService", "Custom")]
    [string]$BuiltInAccount = "NetworkService",

    [System.Management.Automation.PSCredential]$Credential = $null,

    [string]$GroupManagedServiceAccount = "",

    [switch]$Start
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Administrator privileges are required to register or modify a Windows Service. Re-run in an elevated PowerShell prompt."
}

if ([string]::IsNullOrWhiteSpace($BinaryPath)) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $installRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $scriptRoot))
    $relativeCandidate = Join-Path $installRoot "app\FlowUI.exe"
    $msiCandidate = "$env:ProgramFiles\ETL Madness\Flow UI\app\FlowUI.exe"
    $cmdCandidate = Get-Command FlowUI.exe -ErrorAction SilentlyContinue

    if (Test-Path $relativeCandidate) {
        $BinaryPath = (Resolve-Path $relativeCandidate).Path
    } elseif (Test-Path $msiCandidate) {
        $BinaryPath = $msiCandidate
    } elseif ($cmdCandidate) {
        $BinaryPath = $cmdCandidate.Source
    } else {
        throw "Could not locate FlowUI.exe. Pass -BinaryPath 'C:\path\to\app\FlowUI.exe'."
    }
}

if (-not (Test-Path $BinaryPath)) {
    throw "Binary not found at: $BinaryPath"
}

$contentRoot = Split-Path -Parent $BinaryPath
$dataDir = Join-Path $ProgramDataDir "data"
$logsDir = Join-Path $ProgramDataDir "logs"
$envFile = Join-Path $ProgramDataDir "flow-ui.env"

New-Item -ItemType Directory -Path $ProgramDataDir, $dataDir, $logsDir -Force | Out-Null

if (-not (Test-Path $envFile)) {
    $seedEnv = @"
# ETL Madness FlowUI — Windows Service Environment Configuration
# Restart service after editing: Restart-Service -Name $ServiceName
ASPNETCORE_ENVIRONMENT="Production"
ASPNETCORE_URLS="$ListenUrls"
FLOW_API_URL="$ApiUrl"
FLOW_UI_AUTH_MODE="$AuthMode"
# FLOW_API_BASIC_AUTH=""
# ASPNETCORE_Kestrel__Certificates__Default__Path="C:\ProgramData\ETL Madness\Flow UI\flow-ui.pfx"
# ASPNETCORE_Kestrel__Certificates__Default__Password="ChangeMe!"
"@
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($envFile, $seedEnv, $utf8NoBom)
    Write-Host "[INFO] Created initial configuration file: $envFile"
} else {
    Write-Host "[INFO] Existing configuration file preserved at: $envFile"
}

# Determine service account identity for ACLs and sc.exe
$scObj = "NT AUTHORITY\NetworkService"
$aclIdentity = "NT AUTHORITY\NETWORK SERVICE"
$scPasswordArgs = @()

if ($Credential) {
    $scObj = $Credential.UserName
    if ($scObj -notmatch '\\' -and $scObj -notmatch '@') {
        $scObj = ".\$scObj"
    }
    $aclIdentity = $scObj
    $scPasswordArgs = @("password=", $Credential.GetNetworkCredential().Password)
} elseif (-not [string]::IsNullOrWhiteSpace($GroupManagedServiceAccount)) {
    $scObj = $GroupManagedServiceAccount
    $aclIdentity = $GroupManagedServiceAccount
} else {
    switch ($BuiltInAccount) {
        "NetworkService" { $scObj = "NT AUTHORITY\NetworkService"; $aclIdentity = "NT AUTHORITY\NETWORK SERVICE" }
        "LocalService"   { $scObj = "NT AUTHORITY\LocalService";   $aclIdentity = "NT AUTHORITY\LOCAL SERVICE" }
        "LocalSystem"    { $scObj = "LocalSystem";                 $aclIdentity = "NT AUTHORITY\SYSTEM" }
    }
}

# Harden directory ACLs: grant modify to the service account
& icacls.exe $ProgramDataDir /grant "${aclIdentity}:(OI)(CI)M" | Out-Null

$binCmd = "`"$BinaryPath`" --service-name `"$ServiceName`" --env-file `"$envFile`" --contentRoot `"$contentRoot`""

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Write-Host "[INFO] Stopping existing service '$ServiceName'..."
        Stop-Service -Name $ServiceName -Force
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(25))
    }
    Write-Host "[INFO] Updating Windows Service '$ServiceName'..."
    & sc.exe config $ServiceName binPath= $binCmd start= delayed-auto DisplayName= $DisplayName obj= $scObj @scPasswordArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe config failed with exit code $LASTEXITCODE" }
} else {
    Write-Host "[INFO] Creating Windows Service '$ServiceName'..."
    & sc.exe create $ServiceName binPath= $binCmd start= delayed-auto DisplayName= $DisplayName obj= $scObj @scPasswordArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed with exit code $LASTEXITCODE" }
}

& sc.exe description $ServiceName "ETL Madness FlowUI — Flow Builder Studio (.NET 10 Blazor Server) web frontend for go-flowAPI" | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null

Write-Host "[INFO] Service '$ServiceName' configured."
Write-Host "       Binary:       $BinaryPath"
Write-Host "       ContentRoot:  $contentRoot"
Write-Host "       Account:      $scObj"
Write-Host "       Env File:     $envFile"
Write-Host "       Event Log:    Application (Source: $ServiceName)"

if ($Start) {
    Write-Host "[INFO] Starting service '$ServiceName'..."
    Start-Service -Name $ServiceName
    $svc = Get-Service -Name $ServiceName
    Write-Host "[INFO] Service '$ServiceName' status: $($svc.Status)"
} else {
    Write-Host "[INFO] Edit '$envFile' and start the service with: Start-Service -Name $ServiceName"
}
