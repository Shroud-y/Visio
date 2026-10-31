<#
.SYNOPSIS
  Builds the release zips into dist/. Used by the GitHub release workflow and runnable locally.

.DESCRIPTION
  Produces two archives:
    Visio-<version>-win-x64.zip       self-contained, runs on any Windows 10/11 x64
    Visio-<version>-win-x64-lite.zip  needs the .NET 8 Desktop Runtime installed

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/package.ps1 -Version 0.1.0
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutDir) { $OutDir = Join-Path $root 'dist' }
$project = Join-Path $root 'src\Visio\Visio.csproj'
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $OutDir | Out-Null

$variants = @(
    @{ Name = "Visio-$Version-win-x64"; SelfContained = 'true' },
    @{ Name = "Visio-$Version-win-x64-lite"; SelfContained = 'false' }
)

foreach ($v in $variants) {
    $stage = Join-Path $OutDir $v.Name
    $zip = "$stage.zip"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    if (Test-Path $zip) { Remove-Item $zip -Force }

    Write-Host "== $($v.Name)"
    $publishArgs = @($project, '-c', 'Release', '-o', $stage, "-p:Version=$Version", "-p:SelfContained=$($v.SelfContained)")
    # Compression is only allowed for self-contained bundles; it roughly halves the exe.
    if ($v.SelfContained -eq 'true') { $publishArgs += '-p:EnableCompressionInSingleFile=true' }
    dotnet publish @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($v.Name)" }

    Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $stage
    if (Test-Path (Join-Path $root 'LICENSE')) { Copy-Item (Join-Path $root 'LICENSE') $stage }

    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Write-Host ("   {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
}
