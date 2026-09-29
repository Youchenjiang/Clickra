# Create a self-signed certificate for local Clickra development
# Password: 1234
# Subject is read from AppxManifest.xml Publisher attribute.

$repoRoot = Resolve-Path "$PSScriptRoot/../.."
$packagingDir = Join-Path $repoRoot "packaging/msix"
$manifestPath = Join-Path $packagingDir "AppxManifest.xml"
$outPath = Join-Path $packagingDir "ClickraDev.pfx"

[xml]$manifest = Get-Content $manifestPath
$certName = $manifest.Package.Identity.Publisher -replace '^CN=', ''
$password = ConvertTo-SecureString "1234" -AsPlainText -Force

Write-Host "🚀 Creating self-signed certificate: $certName" -ForegroundColor Cyan

$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=$certName" `
    -KeyUsage DigitalSignature `
    -FriendlyName "Clickra Development Certificate" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

Export-PfxCertificate -Cert $cert -FilePath $outPath -Password $password

Write-Host "✅ Certificate created at: $outPath" -ForegroundColor Green
Write-Host "⚠️  Please install this PFX to 'Trusted People' on your local machine to test MSIX installation." -ForegroundColor Yellow
