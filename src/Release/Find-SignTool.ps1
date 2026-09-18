Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
if (-not (Test-Path $kits)) { throw 'Windows SDK SignTool was not found. Install a Windows SDK signing-tools component.' }
$tool = Get-ChildItem $kits -Directory | Sort-Object Name -Descending | ForEach-Object {
    Join-Path $_.FullName 'x64\signtool.exe'
} | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $tool) { throw 'signtool.exe was not found under the installed Windows SDK.' }
$tool
