param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("major", "minor", "patch", "revision")]
    [string]$Type = "patch",
    
    [Parameter(Mandatory=$false)]
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$propsPath = "src/Directory.Build.props"
$manifestPath = "packaging/msix/AppxManifest.xml"

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

# AppxManifest.xml (同時更新外層與嵌入層)
$manifestPaths = @("packaging/msix/AppxManifest.xml", "src/resources/AppxManifest.xml")
foreach ($mPath in $manifestPaths) {
    if (Test-Path $mPath) {
        $manifest = Get-Content $mPath -Raw
        $newManifest = $manifest -replace '(?<=<Identity\s+[^>]*?Version=")([\d\.]+)', $newVersion
        Set-Content $mPath $newManifest -NoNewline
        Write-Host "📦 已同步 Manifest: $mPath" -ForegroundColor Gray
    }
}

# 4. 更新 README 檔案
$readmeFiles = @("README.md", "README.zh-TW.md")
foreach ($f in $readmeFiles) {
    if (Test-Path $f) {
        $content = Get-Content $f -Raw
        # 更新標題 (# Clickra vX.X.X.X) - 僅替換第一行
        $content = $content -replace '(?m)^# Clickra v[\d\.]+', "# Clickra v$newVersion"
        Set-Content $f $content -NoNewline
        Write-Host "📝 已同步文檔: $f" -ForegroundColor Gray
    }
}

Write-Host "✅ 所有檔案已同步完成！" -ForegroundColor Green

if ($Build) {
    Write-Host "`n🏗️  正在開始自動編譯 MSIX 封裝..." -ForegroundColor Cyan
    powershell -File scripts/build_msix.ps1
}
