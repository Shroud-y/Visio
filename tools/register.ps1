<#
.SYNOPSIS
  Adds Visio to Explorer's "Open with" menu for supported document types (current user only, no admin).

.DESCRIPTION
  Writes only under HKCU\Software\Classes. It does not take over existing defaults:
  Windows asks the user to pick the default app ("Open with" > "Always").
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

$progId = 'Visio.Document'
$classes = 'HKCU:\Software\Classes'
$extensions = @(
    'pdf', 'docx', 'docm', 'dotx', 'dotm', 'doc', 'dot', 'rtf', 'odt', 'ott', 'wps',
    'xlsx', 'xlsm', 'xltx', 'xlsb', 'xls', 'ods', 'csv', 'tsv',
    'pptx', 'pptm', 'ppsx', 'potx', 'ppt', 'pps', 'odp', 'vsd', 'vsdx',
    'md', 'markdown', 'txt', 'log', 'json', 'xml', 'yaml', 'yml', 'ini',
    'png', 'jpg', 'jpeg', 'gif', 'webp', 'bmp', 'svg'
)

function Notify-Shell {
    # SHCNE_ASSOCCHANGED so Explorer picks up the change without a restart.
    Add-Type -Namespace Win32 -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);'
    [Win32.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
}

if ($Uninstall) {
    foreach ($ext in $extensions) {
        $key = "$classes\.$ext\OpenWithProgids"
        if (Test-Path $key) { Remove-ItemProperty -Path $key -Name $progId -ErrorAction SilentlyContinue }
    }
    foreach ($key in "$classes\$progId", "$classes\Applications\Visio.exe") {
        if (Test-Path $key) { Remove-Item $key -Recurse -Force }
    }
    Notify-Shell
    Write-Host 'Visio removed from "Open with".'
    return
}

$ExePath = (Resolve-Path $ExePath).Path
$command = "`"$ExePath`" `"%1`""

# ProgID: what the file opens with.
New-Item -Path "$classes\$progId\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$classes\$progId" -Name '(default)' -Value 'Document (Visio)'
Set-ItemProperty -Path "$classes\$progId\shell\open\command" -Name '(default)' -Value $command
New-Item -Path "$classes\$progId\DefaultIcon" -Force | Out-Null
Set-ItemProperty -Path "$classes\$progId\DefaultIcon" -Name '(default)' -Value "`"$ExePath`",0"

# Application entry: friendly name in the "Open with" list.
New-Item -Path "$classes\Applications\Visio.exe\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$classes\Applications\Visio.exe" -Name 'FriendlyAppName' -Value 'Visio'
Set-ItemProperty -Path "$classes\Applications\Visio.exe\shell\open\command" -Name '(default)' -Value $command
New-Item -Path "$classes\Applications\Visio.exe\SupportedTypes" -Force | Out-Null

foreach ($ext in $extensions) {
    New-Item -Path "$classes\.$ext\OpenWithProgids" -Force | Out-Null
    New-ItemProperty -Path "$classes\.$ext\OpenWithProgids" -Name $progId -Value '' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path "$classes\Applications\Visio.exe\SupportedTypes" -Name ".$ext" -Value '' -PropertyType String -Force | Out-Null
}

Notify-Shell
Write-Host "Visio added to `"Open with`" for $($extensions.Count) file types."
Write-Host 'To make it the default: right-click a file > Open with > Choose another app > Visio > Always.'
