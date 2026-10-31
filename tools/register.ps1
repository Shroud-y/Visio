<#
.SYNOPSIS
  Registers Visio for supported document types (current user only, no admin).

.DESCRIPTION
  Adds Visio to Explorer's "Open with" menu and to Settings > Default apps.
  It does not take over existing defaults: Windows asks the user to confirm.
  Visio offers the same on first launch; this script is for scripted setups.
  Run from the folder that contains Visio.exe.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File register.ps1
  powershell -ExecutionPolicy Bypass -File register.ps1 -Uninstall
#>
param(
    [string]$ExePath = (Join-Path $PSScriptRoot 'Visio.exe'),
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$ExePath = (Resolve-Path $ExePath).Path

if ($Uninstall) {
    Start-Process $ExePath -ArgumentList '--unregister' -Wait
    Write-Host 'Visio registration removed.'
} else {
    Start-Process $ExePath -ArgumentList '--register' -Wait
    Write-Host 'Visio registered. To make it the default: Settings > Apps > Default apps > Visio > Set default.'
}
