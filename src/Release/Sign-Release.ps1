[CmdletBinding(DefaultParameterSetName='Pfx')]
param(
    [Parameter(Mandatory, Position=0)] [string[]] $Path,
    [Parameter(ParameterSetName='Pfx')] [string] $PfxPath,
    [Parameter(ParameterSetName='Pfx')] [string] $PfxPassword = $env:SABBY_SIGN_PFX_PASSWORD,
    [Parameter(ParameterSetName='Store')] [string] $Thumbprint,
    [string] $TimestampUrl = 'http://timestamp.digicert.com'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$signTool = & (Join-Path $PSScriptRoot 'Find-SignTool.ps1')

foreach ($item in $Path) {
    $resolved = (Resolve-Path $item).Path
    $args = @('sign','/fd','SHA256','/tr',$TimestampUrl,'/td','SHA256')
    if ($PSCmdlet.ParameterSetName -eq 'Store') {
        if ([string]::IsNullOrWhiteSpace($Thumbprint)) { throw 'Provide -Thumbprint for certificate-store signing.' }
        $args += @('/sha1', ($Thumbprint -replace '\s',''), '/s','My')
    } else {
        if ([string]::IsNullOrWhiteSpace($PfxPath)) { throw 'Provide -PfxPath or use -Thumbprint.' }
        $args += @('/f', (Resolve-Path $PfxPath).Path)
        if (-not [string]::IsNullOrWhiteSpace($PfxPassword)) { $args += @('/p', $PfxPassword) }
    }
    $args += $resolved
    & $signTool @args
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed for $resolved (exit $LASTEXITCODE)." }
    & $signTool verify /pa /v $resolved
    if ($LASTEXITCODE -ne 0) { throw "Signature verification failed for $resolved." }
}
