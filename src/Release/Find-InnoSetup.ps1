Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Inno Setup can be installed machine-wide or per-user. WinGet interactive
# installs may use the current-user location under LocalAppData.
$candidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate) {
        Write-Output $candidate
        exit 0
    }
}

# Also honor PATH if the user added ISCC manually.
$command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($command) {
    Write-Output $command.Source
    exit 0
}

# Finally query uninstall registration for both per-user and machine-wide installs.
$uninstallRoots = @(
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1',
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
)

foreach ($key in $uninstallRoots) {
    try {
        $item = Get-ItemProperty -LiteralPath $key -ErrorAction Stop
        if ($item.InstallLocation) {
            $candidate = Join-Path ([string]$item.InstallLocation) 'ISCC.exe'
            if (Test-Path -LiteralPath $candidate) {
                Write-Output $candidate
                exit 0
            }
        }
    } catch { }
}

throw 'Inno Setup 7 or 6 was not found.'
