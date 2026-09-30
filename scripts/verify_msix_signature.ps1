param(
    [Parameter(Mandatory = $true)]
    [string]$MsixPath,
    [switch]$AllowUntrustedDevelopmentCertificate
)

$ErrorActionPreference = "Stop"

$resolvedPath = Resolve-Path $MsixPath
$signature = Get-AuthenticodeSignature -FilePath $resolvedPath

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPath.Path)
try {
    $manifestEntry = $archive.GetEntry("AppxManifest.xml")
    if (-not $manifestEntry) {
        throw "MSIX signature verification failed: AppxManifest.xml is missing from the package."
    }

    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try {
        [xml]$manifest = $reader.ReadToEnd()
    } finally {
        $reader.Dispose()
    }
} finally {
    $archive.Dispose()
}

$manifestPublisher = [string]$manifest.Package.Identity.Publisher
if ([string]::IsNullOrWhiteSpace($manifestPublisher)) {
    throw "MSIX signature verification failed: package manifest Publisher is missing."
}

if (-not $signature.SignerCertificate -or $signature.Status -eq [System.Management.Automation.SignatureStatus]::NotSigned) {
    throw "MSIX signature verification failed: package is not signed."
}

if ($signature.SignerCertificate.Subject -cne $manifestPublisher) {
    throw "MSIX signature verification failed: signer subject '$($signature.SignerCertificate.Subject)' does not match package Publisher '$manifestPublisher'."
}

if ($signature.Status -eq [System.Management.Automation.SignatureStatus]::HashMismatch) {
    throw "MSIX signature verification failed: package signature hash does not match."
}

if ($signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) {
    Write-Host "[Sign] Signature valid: $($signature.SignerCertificate.Subject)" -ForegroundColor Gray
    exit 0
}

$isSelfSigned = $signature.SignerCertificate.Subject -eq $signature.SignerCertificate.Issuer
$chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
try {
    $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    [void]$chain.Build($signature.SignerCertificate)
    $chainStatusFlags = @($chain.ChainStatus | ForEach-Object Status)
    $hasOnlyUntrustedRoot = $chainStatusFlags.Count -eq 1 -and
        $chainStatusFlags[0] -eq [System.Security.Cryptography.X509Certificates.X509ChainStatusFlags]::UntrustedRoot
} finally {
    $chain.Dispose()
}

$isUntrustedDevelopmentStatus = $hasOnlyUntrustedRoot -and
    ($signature.Status -eq [System.Management.Automation.SignatureStatus]::NotTrusted -or
     $signature.Status -eq [System.Management.Automation.SignatureStatus]::UnknownError)

if ($AllowUntrustedDevelopmentCertificate -and $isSelfSigned -and $isUntrustedDevelopmentStatus) {
    Write-Host "[Sign] Development signature present but not publicly trusted: $($signature.SignerCertificate.Subject)" -ForegroundColor Yellow
    exit 0
}

throw "MSIX signature verification failed with status '$($signature.Status)': $($signature.StatusMessage)"
