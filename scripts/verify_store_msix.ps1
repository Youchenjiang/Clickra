param(
    [Parameter(Mandatory = $true)]
    [string]$MsixPath,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,
    [Parameter(Mandatory = $true)]
    [string]$SourceFileName,
    [string]$ExpectedIdentity = "g1014308.Clickra",
    [string]$ExpectedPublisher = "CN=CBF59877-21AD-4BC4-8F91-FE8DA520A138",
    [string]$ExpectedPackageFamilyName = "g1014308.Clickra_mgcm3zc7fc0ty",
    [string]$ExpectedSha1
)

$ErrorActionPreference = "Stop"

$resolvedPath = (Resolve-Path $MsixPath).Path
if ([IO.Path]::GetExtension($resolvedPath) -cne ".msix") {
    throw "Store MSIX verification failed: expected a .msix file."
}

$signature = Get-AuthenticodeSignature -FilePath $resolvedPath
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Store MSIX verification failed: Authenticode status is '$($signature.Status)'."
}
if (-not $signature.SignerCertificate) {
    throw "Store MSIX verification failed: signer certificate is missing."
}
if ($signature.SignerCertificate.Subject -cne $ExpectedPublisher) {
    throw "Store MSIX verification failed: signer subject '$($signature.SignerCertificate.Subject)' does not match expected Store Publisher '$ExpectedPublisher'."
}
if ($signature.SignerCertificate.Issuer -notmatch '^CN=Microsoft Marketplace CA ') {
    throw "Store MSIX verification failed: signer issuer '$($signature.SignerCertificate.Issuer)' is not a Microsoft Marketplace CA."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPath)
try {
    $manifestEntry = $archive.GetEntry("AppxManifest.xml")
    if (-not $manifestEntry) {
        throw "Store MSIX verification failed: AppxManifest.xml is missing."
    }
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try {
        [xml]$manifest = $reader.ReadToEnd()
    } finally {
        $reader.Dispose()
    }
} finally {
    $archive.Dispose()
}

$identity = $manifest.Package.Identity
$actualName = [string]$identity.Name
$actualPublisher = [string]$identity.Publisher
$actualVersion = [string]$identity.Version
$actualArchitecture = [string]$identity.ProcessorArchitecture
if ($actualName -cne $ExpectedIdentity) {
    throw "Store MSIX verification failed: identity '$actualName' does not match '$ExpectedIdentity'."
}
if ($actualPublisher -cne $ExpectedPublisher) {
    throw "Store MSIX verification failed: manifest Publisher '$actualPublisher' does not match '$ExpectedPublisher'."
}
if ($actualVersion -cne $ExpectedVersion) {
    throw "Store MSIX verification failed: version '$actualVersion' does not match '$ExpectedVersion'."
}
if (-not [string]::IsNullOrWhiteSpace($actualArchitecture) -and $actualArchitecture -cne "neutral") {
    throw "Store MSIX verification failed: expected neutral architecture, found '$actualArchitecture'."
}

$storeNamePattern = '^g1014308\.Clickra_(?<version>\d+\.\d+\.\d+\.\d+)_neutral__(?<publisherId>[a-z0-9]+)\.msix$'
if ($SourceFileName -notmatch $storeNamePattern) {
    throw "Store MSIX verification failed: resolver source filename '$SourceFileName' is not an expected Clickra Store package filename."
}
if ($Matches.version -cne $ExpectedVersion) {
    throw "Store MSIX verification failed: source filename version '$($Matches.version)' does not match '$ExpectedVersion'."
}
$derivedFamily = "$ExpectedIdentity`_$($Matches.publisherId)"
if ($derivedFamily -cne $ExpectedPackageFamilyName) {
    throw "Store MSIX verification failed: derived package family '$derivedFamily' does not match '$ExpectedPackageFamilyName'."
}

$sha1 = (Get-FileHash -Algorithm SHA1 -LiteralPath $resolvedPath).Hash # NOSONAR: resolver publishes SHA-1 as comparison metadata; authenticity is gated by the valid package signature and SHA-256 is also recorded.
if ($ExpectedSha1 -and $sha1 -cne $ExpectedSha1.ToUpperInvariant()) {
    throw "Store MSIX verification failed: SHA-1 '$sha1' does not match resolver-reported '$ExpectedSha1'."
}
$sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedPath).Hash

[pscustomobject]@{
    Path = $resolvedPath
    Identity = $actualName
    Version = $actualVersion
    Publisher = $actualPublisher
    PackageFamilyName = $ExpectedPackageFamilyName
    SignatureStatus = [string]$signature.Status
    SignerIssuer = $signature.SignerCertificate.Issuer
    SignerThumbprint = $signature.SignerCertificate.Thumbprint
    SHA1 = $sha1
    SHA256 = $sha256
} | ConvertTo-Json -Depth 3
