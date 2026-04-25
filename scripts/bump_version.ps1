    [Parameter(Mandatory=$false)]
    [ValidateSet("major", "minor", "patch", "revision")]
    [string]$Type = "revision",
    
    [Parameter(Mandatory=$false)]
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$propsPath = "src/Directory.Build.props"
$manifestPath = "src/resources/AppxManifest.xml"
$installerPath = "setup_context_menu.ps1"

# 1. 從 Directory.Build.props 抓取目前版本
$content = Get-Content $propsPath -Raw
if ($content -match '<Version>(?<v>.*)</Version>') {
    $currentVersion = [version]$Matches['v']
} else {
    Write-Error "無法在 $propsPath 中找到版本號"
}

# 2. 計算新版本
$major = $currentVersion.Major
$minor = $currentVersion.Minor
$patch = $currentVersion.Build
if ($patch -lt 0) { $patch = 0 }
$revision = $currentVersion.Revision
if ($revision -lt 0) { $revision = 0 }

switch ($Type) {
    "major" { $major++; $minor = 0; $patch = 0; $revision = 0 }
    "minor" { $minor++; $patch = 0; $revision = 0 }
    "patch" { $patch++; $revision = 0 }
    "revision" { $revision++ }
}

$newVersion = "$major.$minor.$patch.$revision"
Write-Host "🚀 正在將版本從 $currentVersion 升級至 $newVersion ..." -ForegroundColor Cyan

# 3. 更新各個檔案
# Directory.Build.props
$newProps = $content -replace '<Version>.*</Version>', "<Version>$newVersion</Version>"
Set-Content $propsPath $newProps -NoNewline

# AppxManifest.xml
$manifest = Get-Content $manifestPath -Raw
$newManifest = $manifest -replace '(?<=<Identity\s+[^>]*?Version=")([\d\.]+)', $newVersion
Set-Content $manifestPath $newManifest -NoNewline

# setup_context_menu.ps1
$installer = Get-Content $installerPath -Raw
$newInstaller = $installer -replace '\$Version = "[\d\.]+"', "`$Version = ""$newVersion"""
Set-Content $installerPath $newInstaller -NoNewline

Write-Host "✅ 所有檔案已同步完成！" -ForegroundColor Green

if ($Build) {
    Write-Host "`n🏗️ 正在開始自動編譯 (NativeAOT)..." -ForegroundColor Cyan
    dotnet publish src/Clickra.CLI/Clickra.csproj -c Release -r win-x64 --output .
    dotnet publish src/ClickraShell/ClickraShell.csproj -c Release -r win-x64 --output .
    Write-Host "🚀 編譯產物已更新！" -ForegroundColor Green
}
