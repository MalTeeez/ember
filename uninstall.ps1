<#
.SYNOPSIS
  Removes Ember: stops it, deletes the scheduled task "Ember" and the install folder.
  Settings and notes in %APPDATA%\Ember are kept unless -RemoveData is given.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Ember'),
    # Also delete settings, notes and the log in %APPDATA%\Ember.
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$TaskName = 'Ember'

Write-Host "Stopping Ember..."
Get-Process -Name 'Ember' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Write-Host "Removing scheduled task '$TaskName'"
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

if (Test-Path $InstallDir) {
    Write-Host "Removing $InstallDir"
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
}

$data = Join-Path $env:APPDATA 'Ember'
if ($RemoveData -and (Test-Path $data)) {
    Write-Host "Removing settings and notes in $data"
    Remove-Item -LiteralPath $data -Recurse -Force
} elseif (Test-Path $data) {
    Write-Host "Settings and notes are kept in $data (use -RemoveData to delete them)."
}

Write-Host "Done."
