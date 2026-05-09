# Create a self-signed certificate for local Clickra development
# Password: 1234

$certName = "ClickraDev"
$password = ConvertTo-SecureString "1234" -AsPlainText -Force
$outPath = "packaging/msix/ClickraDev.pfx"

Write-Host "🚀 Creating self-signed certificate: $certName" -ForegroundColor Cyan

$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=$certName" `
    -KeyUsage DigitalSignature `
    -FriendlyName "Clickra Development Certificate" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

Export-PfxCertificate -Cert $cert -FilePath $outPath -Password $password

Write-Host "✅ Certificate created at: $outPath" -ForegroundColor Green
Write-Host "⚠️  Please install this PFX to 'Trusted People' on your local machine to test MSIX installation." -ForegroundColor Yellow
