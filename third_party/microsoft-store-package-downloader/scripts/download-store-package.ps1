[CmdletBinding()]
param(
    [string]$ProductId = "9PLM9XGG6VKS",
    [ValidateSet("x64", "x86", "arm64", "arm", "all")]
    [string]$Architecture = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }),
    [string]$Market = "US",
    [string]$Language = "en",
    [string]$OutputDirectory = (Join-Path (Get-Location).Path "microsoft-store-downloads"),
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# FE3 templates and the device token are derived from StoreDev/StoreLib
# under MPL-2.0. See references/storelib/LICENSE.
$skillRoot = Split-Path -Parent $PSScriptRoot
$storeLibReference = Join-Path $skillRoot "references\storelib\FE3Handler.cs"
$templateRoot = Join-Path $PSScriptRoot "storelib-xml"
$deliveryEndpoint = "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx"
$securedDeliveryEndpoint = "$deliveryEndpoint/secured"

function Normalize-StoreProductId {
    param([Parameter(Mandatory)][string]$Value)

    $normalized = $Value.Trim().ToUpperInvariant()
    if ($normalized -notmatch "^(?:[A-Z0-9]{12}|XP[A-Z0-9]{12})$") {
        throw "Invalid Microsoft Store Product ID: '$Value'."
    }
    return $normalized
}

function Get-StoreLibDeviceToken {
    $source = Get-Content -Raw -LiteralPath $storeLibReference
    $match = [regex]::Match($source, 'private static readonly String _msaToken = "(.*?)";')
    if (-not $match.Success) {
        throw "The bundled StoreLib device token was not found."
    }
    return $match.Groups[1].Value
}

function Invoke-Fe3Soap {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Body
    )

    $headers = @{
        "User-Agent" = "StoreLib"
        "MS-CV" = ([guid]::NewGuid().ToString("N") + ".1")
    }
    Invoke-WebRequest -UseBasicParsing -Method Post -Uri $Uri -Headers $headers `
        -ContentType "application/soap+xml; charset=utf-8" -Body $Body
}

function Get-ContentDispositionFileName {
    param(
        [string]$ContentDisposition,
        [string]$Fallback
    )

    $name = $null
    if ($ContentDisposition -match "filename\*=UTF-8''([^;]+)") {
        $name = [Uri]::UnescapeDataString($Matches[1])
    } elseif ($ContentDisposition -match 'filename="?([^";]+)"?') {
        $name = $Matches[1]
    }
    if ([string]::IsNullOrWhiteSpace($name)) {
        $name = $Fallback
    }
    foreach ($invalidCharacter in [IO.Path]::GetInvalidFileNameChars()) {
        $name = $name.Replace([string]$invalidCharacter, "_")
    }
    return $name
}

function Test-MicrosoftDeliveryUri {
    param([Parameter(Mandatory)][Uri]$Uri)

    return $Uri.Scheme -in @("http", "https") -and
        $Uri.Host -match '(^|\.)delivery\.mp\.microsoft\.com$'
}

function Test-PackageArchitecture {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string]$WantedArchitecture
    )

    if ($WantedArchitecture -eq "all") {
        return $true
    }
    $match = [regex]::Match($FileName, '_(x64|x86|arm64|arm|neutral)_', "IgnoreCase")
    return -not $match.Success -or
        $match.Groups[1].Value -eq "neutral" -or
        $match.Groups[1].Value -eq $WantedArchitecture
}

function Save-PackageFile {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    $temporaryPath = "$Destination.download"
    try {
        if (Get-Command Start-BitsTransfer -ErrorAction SilentlyContinue) {
            try {
                Start-BitsTransfer -Source $Source -Destination $temporaryPath `
                    -DisplayName "Microsoft Store package download" -ErrorAction Stop
            } catch {
                Invoke-WebRequest -UseBasicParsing -Uri $Source -OutFile $temporaryPath `
                    -Headers @{ "User-Agent" = "Microsoft-Delivery-Optimization/10.0" }
            }
        } else {
            Invoke-WebRequest -UseBasicParsing -Uri $Source -OutFile $temporaryPath `
                -Headers @{ "User-Agent" = "Microsoft-Delivery-Optimization/10.0" }
        }
        Move-Item -LiteralPath $temporaryPath -Destination $Destination -Force
    } finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

$id = Normalize-StoreProductId -Value $ProductId
$catalogUri = "https://displaycatalog.mp.microsoft.com/v7.0/products/$id" +
    "?market=$Market&languages=$Language-$Market,$Language,neutral"
$catalog = Invoke-RestMethod -UseBasicParsing -Uri $catalogUri -Headers @{ "User-Agent" = "StoreLib" }
$product = if ($null -ne $catalog.Product) { $catalog.Product } else { $catalog.Products[0] }
if ($null -eq $product) {
    throw "Microsoft Store product '$id' was not found."
}

$sku = $product.DisplaySkuAvailabilities |
    Where-Object { $null -ne $_.Sku.Properties.FulfillmentData } |
    Select-Object -First 1
if ($null -eq $sku) {
    throw "Product '$id' does not expose downloadable package fulfillment data."
}

$wuCategoryId = $sku.Sku.Properties.FulfillmentData.WuCategoryId
$packageIdentityName = [string]$product.Properties.PackageIdentityName
$title = [string]$product.LocalizedProperties[0].ProductTitle
$publisher = [string]$product.LocalizedProperties[0].PublisherName
$deviceToken = Get-StoreLibDeviceToken

$cookieTemplate = Get-Content -Raw -LiteralPath (Join-Path $templateRoot "GetCookie.xml")
$cookieResponse = Invoke-Fe3Soap -Uri $deliveryEndpoint -Body $cookieTemplate
[xml]$cookieXml = $cookieResponse.Content
$encryptedData = $cookieXml.GetElementsByTagName("EncryptedData")[0].InnerText
if ([string]::IsNullOrWhiteSpace($encryptedData)) {
    throw "Microsoft FE3 did not return an update cookie."
}

$syncTemplate = Get-Content -Raw -LiteralPath (Join-Path $templateRoot "WUIDRequest.xml")
$syncBody = $syncTemplate -f $encryptedData, $wuCategoryId, $deviceToken
$syncResponse = Invoke-Fe3Soap -Uri $deliveryEndpoint -Body $syncBody
[xml]$syncXml = [System.Net.WebUtility]::HtmlDecode($syncResponse.Content)
$metadataNodes = @($syncXml.GetElementsByTagName("AppxMetadata"))
$securedNodes = @($syncXml.GetElementsByTagName("SecuredFragment"))
if ($securedNodes.Count -eq 0) {
    throw "Microsoft FE3 returned no downloadable packages for '$id'."
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$urlTemplate = Get-Content -Raw -LiteralPath (Join-Path $templateRoot "FE3FileUrl.xml")
$candidates = [Collections.Generic.List[object]]::new()

for ($index = 0; $index -lt $securedNodes.Count; $index++) {
    $identity = $securedNodes[$index].ParentNode.ParentNode.FirstChild
    $updateId = $identity.Attributes[0].Value
    $revision = $identity.Attributes[1].Value
    $moniker = if ($index -lt $metadataNodes.Count) {
        $metadataNodes[$index].Attributes["PackageMoniker"].Value
    } else {
        $updateId
    }

    $fileUrlBody = $urlTemplate -f $updateId, $revision, $deviceToken
    $fileUrlResponse = Invoke-Fe3Soap -Uri $securedDeliveryEndpoint -Body $fileUrlBody
    [xml]$fileUrlXml = $fileUrlResponse.Content
    $urls = @($fileUrlXml.GetElementsByTagName("FileLocation") | ForEach-Object {
        $_.ChildNodes | Where-Object Name -eq "Url" | ForEach-Object InnerText
    })

    foreach ($url in $urls) {
        $sourceUri = [Uri]$url
        if (-not (Test-MicrosoftDeliveryUri -Uri $sourceUri)) {
            throw "Refusing unexpected package host: $url"
        }

        $head = Invoke-WebRequest -UseBasicParsing -Method Head -Uri $url `
            -Headers @{ "User-Agent" = "Microsoft-Delivery-Optimization/10.0" }
        $fileName = Get-ContentDispositionFileName `
            -ContentDisposition ([string]$head.Headers["Content-Disposition"]) `
            -Fallback "$moniker.msix"
        $extension = [IO.Path]::GetExtension($fileName).ToLowerInvariant()
        if ($extension -notin @(".msix", ".msixbundle", ".appx", ".appxbundle", ".eappx", ".emsix")) {
            continue
        }
        if (-not (Test-PackageArchitecture -FileName $fileName -WantedArchitecture $Architecture)) {
            continue
        }

        $candidates.Add([pscustomobject]@{
            FileName = $fileName
            Bytes = [long]$head.Headers["Content-Length"]
            SourceUrl = $url
        })
    }
}

$packages = @($candidates | Sort-Object FileName -Unique)
if ($packages.Count -eq 0) {
    throw "No installable packages matched architecture '$Architecture'."
}

$results = [Collections.Generic.List[object]]::new()
foreach ($package in $packages) {
    $destination = Join-Path $outputRoot $package.FileName
    if ((Test-Path -LiteralPath $destination) -and -not $Force) {
        throw "Destination already exists: $destination. Use -Force to replace it."
    }

    Write-Host "Downloading $($package.FileName) ($($package.Bytes) bytes)..."
    Save-PackageFile -Source $package.SourceUrl -Destination $destination

    $signature = Get-AuthenticodeSignature -LiteralPath $destination
    if ($signature.Status -ne "Valid") {
        throw "Package signature validation failed for '$destination': $($signature.Status)"
    }
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $destination
    $file = Get-Item -LiteralPath $destination
    $results.Add([pscustomobject]@{
        FileName = $file.Name
        Path = $file.FullName
        Bytes = $file.Length
        Sha256 = $hash.Hash
        SignatureStatus = [string]$signature.Status
        Signer = $signature.SignerCertificate.Subject
        SourceUrl = $package.SourceUrl
    })
}

$manifest = [ordered]@{
    ProductId = $id
    Title = $title
    Publisher = $publisher
    PackageIdentityName = $packageIdentityName
    Architecture = $Architecture
    DownloadedAtUtc = [DateTime]::UtcNow.ToString("o")
    Packages = $results
}
$manifest | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $outputRoot "package-manifest.json") -Encoding UTF8

$installScript = @'
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifest = Get-Content -Raw -LiteralPath (Join-Path $root "package-manifest.json") | ConvertFrom-Json
$packages = @($manifest.Packages | ForEach-Object {
    Get-Item -LiteralPath (Join-Path $root $_.FileName)
})
$main = $packages | Where-Object { $_.Name -like "$($manifest.PackageIdentityName)_*" } | Select-Object -First 1
if ($null -eq $main) {
    throw "The main application package was not found."
}
$dependencies = @($packages | Where-Object FullName -ne $main.FullName)
if ($dependencies.Count -gt 0) {
    Add-AppxPackage -Path $main.FullName -DependencyPath $dependencies.FullName
} else {
    Add-AppxPackage -Path $main.FullName
}
Write-Host "$($manifest.Title) installed successfully."
'@
$installScript | Set-Content -LiteralPath (Join-Path $outputRoot "install.ps1") -Encoding UTF8

$results
