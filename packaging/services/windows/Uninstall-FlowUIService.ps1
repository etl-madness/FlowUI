<#
.SYNOPSIS
    Stops and removes the ETL Madness FlowUI Windows Service.

.EXAMPLE
    .\Uninstall-FlowUIService.ps1
    .\Uninstall-FlowUIService.ps1 -PurgeData
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "FlowUI",

    [string]$ProgramDataDir = "$env:ProgramData\ETL Madness\Flow UI",

    [switch]$PurgeData
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Administrator privileges are required to remove a Windows Service. Re-run in an elevated PowerShell prompt."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Write-Host "[INFO] Stopping Windows Service '$ServiceName'..."
        Stop-Service -Name $ServiceName -Force
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(25))
    }
    Write-Host "[INFO] Deleting Windows Service '$ServiceName'..."
    & sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe delete failed with exit code $LASTEXITCODE"
    }
    Write-Host "[INFO] Removed Windows Service '$ServiceName'."
} else {
    Write-Host "[INFO] Windows Service '$ServiceName' is not installed."
}

if ($PurgeData -and (Test-Path $ProgramDataDir)) {
    Remove-Item -Recurse -Force $ProgramDataDir
    Write-Host "[INFO] Purged configuration and logs directory: $ProgramDataDir"
} elseif (Test-Path $ProgramDataDir) {
    Write-Host "[INFO] Preserved configuration and logs in '$ProgramDataDir' (pass -PurgeData to delete)."
}
