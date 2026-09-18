param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $ArtifactPath,
    [Parameter(Mandatory)] [string] $DownloadUrl,
    [string] $ReleaseNotes = '',
    [bool] $Mandatory = $true,
    [string] $OutputPath = (Join-Path $PSScriptRoot 'release-manifest.json')
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$artifact=(Resolve-Path $ArtifactPath).Path
$hash=(Get-FileHash $artifact -Algorithm SHA256).Hash
$sig=Get-AuthenticodeSignature -FilePath $artifact
$signerThumbprint = if ($sig.Status -eq 'Valid' -and $sig.SignerCertificate) { $sig.SignerCertificate.Thumbprint } else { $null }
$manifest=[ordered]@{
    version=$Version
    downloadUrl=$DownloadUrl
    sha256=$hash
    signerThumbprint=$signerThumbprint
    releaseNotes=$ReleaseNotes
    mandatory=$Mandatory
    publishedAt=(Get-Date).ToUniversalTime().ToString('o')
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content $OutputPath -Encoding UTF8
Write-Host "Update manifest: $OutputPath"
Write-Host "SHA-256: $hash"
