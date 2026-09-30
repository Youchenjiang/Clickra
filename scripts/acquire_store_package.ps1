param(
    [Parameter(Mandatory = $true)]
    [string]$ProductId,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedIdentity,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [string]$ExpectedPublisher = "CN=CBF59877-21AD-4BC4-8F91-FE8DA520A138"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Test-MicrosoftDeliveryUri {
    param([Parameter(Mandatory = $true)][Uri]$Uri)

    return $Uri.Scheme -in @("http", "https") -and
        $Uri.Host -match '(^|\.)delivery\.mp\.microsoft\.com$'
}

if ($ProductId -notmatch '^(?:[A-Z0-9]{12}|XP[A-Z0-9]{12})$') {
    throw "Invalid Microsoft Store Product ID '$ProductId'."
}
if ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Expected Store version must be four numeric components; got '$ExpectedVersion'."
}

$root = Split-Path -Parent $PSScriptRoot
$vendorScript = Join-Path $root "third_party\microsoft-store-package-downloader\scripts\download-store-package.ps1"
if (-not (Test-Path -LiteralPath $vendorScript)) {
    throw "Vendored Microsoft Store acquisition helper is missing: $vendorScript"
}

$destination = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetExtension($destination) -cne ".msix") {
    throw "Store acquisition output must use the canonical .msix extension."
}
if (Test-Path -LiteralPath $destination) {
    throw "Store acquisition output already exists; refusing to reuse stale bytes: $destination"
}
[IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("clickra-store-acquire-" + [guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
try {
    $vendorOutput = & powershell -NoProfile -ExecutionPolicy Bypass `
        -File $vendorScript `
        -ProductId $ProductId `
        -Architecture "x64" `
        -OutputDirectory $temporaryRoot 2>&1
    if ($LASTEXITCODE -ne 0) {
        $details = ($vendorOutput | Out-String).Trim()
        throw "Microsoft Store endpoint acquisition failed with exit code $LASTEXITCODE.`n$details"
    }

    $manifestPath = Join-Path $temporaryRoot "package-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Microsoft Store acquisition did not produce package-manifest.json."
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ([string]$manifest.ProductId -cne $ProductId) {
        throw "Store manifest ProductId '$($manifest.ProductId)' does not match '$ProductId'."
    }
    if ([string]$manifest.PackageIdentityName -cne $ExpectedIdentity) {
        throw "Store manifest identity '$($manifest.PackageIdentityName)' does not match '$ExpectedIdentity'."
    }

    $escapedIdentity = [regex]::Escape($ExpectedIdentity)
    $escapedVersion = [regex]::Escape($ExpectedVersion)
    $expectedNamePattern = "^${escapedIdentity}_${escapedVersion}_neutral__(?<publisherId>[a-z0-9]+)\.msix$"
    $packageMatches = @(
        $manifest.Packages |
            Where-Object { [string]$_.FileName -match $expectedNamePattern }
    )
    if ($packageMatches.Count -ne 1) {
        throw "Expected exactly one '$ExpectedIdentity' Store MSIX for version '$ExpectedVersion', found $($packageMatches.Count)."
    }

    $selected = $packageMatches[0]
    $sourceUri = [Uri][string]$selected.SourceUrl
    if (-not (Test-MicrosoftDeliveryUri -Uri $sourceUri)) {
        throw "Store package source escaped the Microsoft delivery boundary: $sourceUri"
    }

    $sourcePath = Join-Path $temporaryRoot ([string]$selected.FileName)
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Store manifest selected a package that is missing on disk: $sourcePath"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $sourcePath
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Store acquisition signature status is '$($signature.Status)'."
    }
    if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Subject -cne $ExpectedPublisher) {
        throw "Store acquisition signer does not match expected Publisher '$ExpectedPublisher'."
    }
    if ($signature.SignerCertificate.Issuer -notmatch '^CN=Microsoft Marketplace CA ') {
        throw "Store acquisition signer issuer '$($signature.SignerCertificate.Issuer)' is not a Microsoft Marketplace CA."
    }

    $sha1 = (Get-FileHash -Algorithm SHA1 -LiteralPath $sourcePath).Hash # NOSONAR: SHA-1 is retained only as a secondary byte-consistency field; signature and SHA-256 remain authoritative.
    $sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourcePath).Hash
    Move-Item -LiteralPath $sourcePath -Destination $destination

    [pscustomobject]@{
        ProductId = $ProductId
        Identity = $ExpectedIdentity
        Version = $ExpectedVersion
        SourceFileName = [string]$selected.FileName
        SourceUrl = [string]$selected.SourceUrl
        OutputPath = $destination
        SignatureStatus = [string]$signature.Status
        SignerSubject = $signature.SignerCertificate.Subject
        SignerIssuer = $signature.SignerCertificate.Issuer
        SHA1 = $sha1
        SHA256 = $sha256
    } | ConvertTo-Json -Depth 3
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
