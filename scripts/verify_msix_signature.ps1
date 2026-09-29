param(
    [Parameter(Mandatory = $true)]
    [string]$MsixPath,
    [switch]$AllowUntrustedDevelopmentCertificate
)

$ErrorActionPreference = "Stop"

$resolvedPath = Resolve-Path $MsixPath
$signature = Get-AuthenticodeSignature -FilePath $resolvedPath

if (-not $signature.SignerCertificate -or $signature.Status -eq [System.Management.Automation.SignatureStatus]::NotSigned) {
    throw "MSIX signature verification failed: package is not signed."
}

if ($signature.Status -eq [System.Management.Automation.SignatureStatus]::HashMismatch) {
    throw "MSIX signature verification failed: package signature hash does not match."
}

if ($signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) {
    Write-Host "[Sign] Signature valid: $($signature.SignerCertificate.Subject)" -ForegroundColor Gray
    exit 0
}

$isSelfSigned = $signature.SignerCertificate.Subject -eq $signature.SignerCertificate.Issuer
$isUntrustedDevelopmentStatus = $signature.Status -eq [System.Management.Automation.SignatureStatus]::NotTrusted -or
    ($signature.Status -eq [System.Management.Automation.SignatureStatus]::UnknownError -and
     $signature.StatusMessage -like "*root certificate which is not trusted*")

if ($AllowUntrustedDevelopmentCertificate -and $isSelfSigned -and $isUntrustedDevelopmentStatus) {
    Write-Host "[Sign] Development signature present but not publicly trusted: $($signature.SignerCertificate.Subject)" -ForegroundColor Yellow
    exit 0
}

throw "MSIX signature verification failed with status '$($signature.Status)': $($signature.StatusMessage)"
