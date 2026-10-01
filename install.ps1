<#
.SYNOPSIS
  Installs Ember: copies Ember.exe to %LOCALAPPDATA%\Ember and creates the scheduled task
  "Ember" (at logon, highest privileges). Run once from an elevated PowerShell.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    # Path to the published Ember.exe. Default: next to this script, or in .\publish\.
    [string]$Source,
    # Install folder. Default: %LOCALAPPDATA%\Ember of the user who runs the script.
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Ember'),
    # The user the widget starts for at logon. Default: the current user.
    [string]$User = "$env:USERDOMAIN\$env:USERNAME",
    # Do not start Ember after the install.
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$TaskName = 'Ember'

if (-not $Source) {
    $candidates = @(
        (Join-Path $PSScriptRoot 'Ember.exe'),
        (Join-Path $PSScriptRoot 'publish\Ember.exe'),
        (Join-Path $PSScriptRoot 'src\Ember\bin\Release\net10.0\win-x64\publish\Ember.exe')
    )
    $Source = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Source -or -not (Test-Path $Source)) {
    throw "Ember.exe not found. Build it with 'dotnet publish src/Ember -c Release -r win-x64 -o publish' or give -Source."
}

Write-Host "Stopping a running Ember (if any)..."
Get-Process -Name 'Ember' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host "Copying $Source to $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$exe = Join-Path $InstallDir 'Ember.exe'
Copy-Item -LiteralPath $Source -Destination $exe -Force

Write-Host "Creating scheduled task '$TaskName' for $User (at logon, highest privileges)"
$action    = New-ScheduledTaskAction -Execute $exe
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $User
$principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Highest
# No time limit (the default of 72 h would stop the widget), run on battery, one instance.
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Force | Out-Null

if (-not $NoStart) {
    Write-Host "Starting Ember"
    Start-ScheduledTask -TaskName $TaskName
}

Write-Host "Done. Ember starts at each logon without a UAC prompt."
Write-Host "Settings and notes: $env:APPDATA\Ember"
