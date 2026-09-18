param(
    [Parameter(Mandatory)] [string] $ArtifactDirectory,
    [switch] $RequireValidSignature
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$dir=(Resolve-Path $ArtifactDirectory).Path
$files=@(Get-ChildItem $dir -File -Recurse)
if (-not $files) { throw "No release files found in $dir" }

$bad=@()
foreach($file in $files) {
    if ($file.Extension -in '.exe','.msi','.msix','.dll') {
        $sig=Get-AuthenticodeSignature -FilePath $file.FullName
        if ($RequireValidSignature -and $sig.Status -ne 'Valid') { $bad += "$($file.Name): $($sig.Status)" }
    }
}
if ($bad.Count) { throw "Signature validation failed:`n$($bad -join "`n")" }

$hashes = @($files | Where-Object Extension -notin '.sha256' | ForEach-Object {
    $h=Get-FileHash $_.FullName -Algorithm SHA256
    [pscustomobject]@{ File=$_.FullName.Substring($dir.Length).TrimStart('\'); Sha256=$h.Hash; Size=$_.Length }
})
$hashes | Sort-Object File | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $dir 'checksums.json') -Encoding UTF8
Write-Host "Verified $($files.Count) release file(s)."
