[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [ValidateSet('Stable','Preview','Nightly')] [string] $Channel = 'Stable',
    [switch] $Sign,
    [switch] $RequireInstaller,
    [string] $PfxPath,
    [string] $PfxPassword = $env:SABBY_SIGN_PFX_PASSWORD,
    [string] $CertificateThumbprint,
    [string] $DownloadBaseUrl = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($DownloadBaseUrl)) {
    $downloadUrlFile = Join-Path $PSScriptRoot 'DOWNLOAD_BASE_URL.txt'
    if (Test-Path $downloadUrlFile) {
        $candidate = (Get-Content $downloadUrlFile -Raw).Trim()
        if ($candidate -and -not $candidate.StartsWith('#') -and -not $candidate.Contains('YOUR-DOWNLOAD-BASE-URL')) {
            $DownloadBaseUrl = $candidate
        }
    }
}
& (Join-Path $PSScriptRoot 'Test-ReleasePrerequisites.ps1') -RequireInstaller:$RequireInstaller -RequireSigning:$Sign
$project = Join-Path $repo 'SabbyOptimizer\SabbyOptimizer.csproj'
$artifacts = Join-Path $repo 'artifacts'
$publish = Join-Path $artifacts "publish\$Runtime"
$packages = Join-Path $artifacts 'packages'
$installerOut = Join-Path $artifacts 'installer'
Remove-Item $publish,$packages,$installerOut -Recurse -Force -ErrorAction SilentlyContinue
New-Item $publish,$packages,$installerOut -ItemType Directory -Force | Out-Null

[xml]$projectXml=Get-Content $project
$version=[string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Project Version could not be read.' }

Write-Host "Restoring and validating Sabby Optimizer $version..."
dotnet restore $project -r $Runtime `
  -p:PublishReadyToRun=true -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit $LASTEXITCODE" }
dotnet build $project -c Release -r $Runtime --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit $LASTEXITCODE" }

Write-Host "Publishing Sabby Optimizer $version ($Runtime)..."
dotnet publish $project -c Release -r $Runtime --self-contained true --no-restore `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishReadyToRun=true -p:PublishTrimmed=false -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit $LASTEXITCODE" }

$exe = Join-Path $publish 'SabbyOptimizer.exe'
if (-not (Test-Path $exe)) { throw 'Published SabbyOptimizer.exe was not produced.' }

if ($Sign) {
    $signArgs=@{Path=@($exe)}
    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) { $signArgs.Thumbprint=$CertificateThumbprint }
    else { $signArgs.PfxPath=$PfxPath; $signArgs.PfxPassword=$PfxPassword }
    & (Join-Path $PSScriptRoot 'Sign-Release.ps1') @signArgs
}

$portable = Join-Path $packages "SabbyOptimizer-$version-$Runtime-Portable.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $portable -CompressionLevel Optimal

# Build an installer when Inno Setup 7 or 6 is present. Portable packaging remains available when it is not.
$iscc = $null
try { $iscc = & (Join-Path $PSScriptRoot 'Find-InnoSetup.ps1') } catch { }
$installer=$null
if ($iscc) {
    $iss=Join-Path $repo 'Installer\SabbyOptimizer.iss'
    & $iscc "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$installerOut" $iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit $LASTEXITCODE" }
    $installer=Get-ChildItem $installerOut -Filter '*.exe' | Select-Object -First 1
    if ($Sign -and $installer) {
        $signArgs=@{Path=@($installer.FullName)}
        if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) { $signArgs.Thumbprint=$CertificateThumbprint }
        else { $signArgs.PfxPath=$PfxPath; $signArgs.PfxPassword=$PfxPassword }
        & (Join-Path $PSScriptRoot 'Sign-Release.ps1') @signArgs
    }
} else {
    Write-Warning 'Inno Setup 7 or 6 was not found. Portable ZIP was created; install Inno Setup to generate Setup.exe.'
}

$primary = if ($installer) { $installer.FullName } else { $portable }
$base = $DownloadBaseUrl.TrimEnd('/')
$url = if ($base) { "$base/$(Split-Path $primary -Leaf)" } else { $primary }
$manifest = Join-Path $packages "SabbyOptimizer-$Channel-manifest.json"
& (Join-Path $PSScriptRoot 'New-UpdateManifest.ps1') -Version $version -ArtifactPath $primary -DownloadUrl $url -ReleaseNotes "Sabby Optimizer $version ($Channel)" -Mandatory $true -OutputPath $manifest

& (Join-Path $PSScriptRoot 'Verify-Release.ps1') -ArtifactDirectory $packages
if ($installer) { & (Join-Path $PSScriptRoot 'Verify-Release.ps1') -ArtifactDirectory $installerOut -RequireValidSignature:$Sign }
Write-Host ''
Write-Host 'Release build complete.' -ForegroundColor Green
Write-Host "Portable: $portable"
if ($installer) { Write-Host "Installer: $($installer.FullName)" }
Write-Host "Manifest: $manifest"
