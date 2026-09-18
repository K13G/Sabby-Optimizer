[CmdletBinding()]
param(
    [switch] $RequireInstaller,
    [switch] $RequireSigning
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') { throw 'Sabby Optimizer release builds require Windows.' }
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw '.NET SDK was not found. Install the .NET 10 SDK before building Sabby Optimizer.' }
$version = (& dotnet --version).Trim()
$major = 0
if (-not [int]::TryParse(($version -split '\.')[0], [ref]$major) -or $major -lt 10) {
    throw "Sabby Optimizer targets net10.0-windows. .NET SDK 10+ is required; detected '$version'."
}

$iscc = $null
try { $iscc = & (Join-Path $PSScriptRoot 'Find-InnoSetup.ps1') } catch { }
if ($RequireInstaller -and -not $iscc) {
    throw 'Inno Setup 7 or 6 is required for Setup.exe but ISCC.exe was not found.'
}

$signToolPath = $null
try { $signToolPath = & (Join-Path $PSScriptRoot 'Find-SignTool.ps1') } catch { }
if ($RequireSigning -and -not $signToolPath) {
    throw 'Signing was requested but Windows SignTool could not be located.'
}

[pscustomobject]@{
    Windows = $true
    DotnetSdk = $version
    InnoSetup = if ($iscc) { $iscc } else { 'Not installed (portable ZIP still supported)' }
    SignTool = if ($signToolPath) { $signToolPath } else { 'Not found' }
} | Format-List
