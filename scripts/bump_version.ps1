param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("major", "minor", "patch", "revision")]
    [string]$Type = "patch",
    
    [Parameter(Mandatory=$false)]
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$root = (Get-Location).Path
$propsPath = "src/Directory.Build.props"
$manifestPath = "packaging/msix/AppxManifest.xml"

# 1. 從 Directory.Build.props 抓取目前版本
$content = [System.IO.File]::ReadAllText("$root/$propsPath", [System.Text.Encoding]::UTF8)
if ($content -match '<Version>(?<v>.*)</Version>') {
    $currentVersion = [version]$Matches['v']
} else {
    Write-Error "Could not find Version in $propsPath"
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
Write-Host "[*] Upgrading version from $currentVersion to $newVersion ..." -ForegroundColor Cyan

$utf8NoBOM = New-Object System.Text.UTF8Encoding($false)

# 3. 更新各個檔案
# Directory.Build.props
$newProps = $content -replace '<Version>.*</Version>', "<Version>$newVersion</Version>"
[System.IO.File]::WriteAllText("$root/$propsPath", $newProps, $utf8NoBOM)

# AppxManifest.xml (同時更新外層與嵌入層)
$manifestPaths = @("packaging/msix/AppxManifest.xml", "src/resources/AppxManifest.xml")
foreach ($mPath in $manifestPaths) {
    if (Test-Path $mPath) {
        $manifest = [System.IO.File]::ReadAllText("$root/$mPath", [System.Text.Encoding]::UTF8)
        $newManifest = $manifest -replace '(?<=<Identity\s+[^>]*?Version=")([\d\.]+)', $newVersion
        [System.IO.File]::WriteAllText("$root/$mPath", $newManifest, $utf8NoBOM)
        Write-Host "[Manifest] Synced Manifest: $mPath" -ForegroundColor Gray
    }
}

# 4. 更新 README 檔案
$readmeFiles = @("README.md", "README.zh-TW.md")
foreach ($f in $readmeFiles) {
    if (Test-Path $f) {
        $content = [System.IO.File]::ReadAllText("$root/$f", [System.Text.Encoding]::UTF8)
        # 更新標題 (# Clickra vX.X.X.X) - 僅替換第一行
        $content = $content -replace '(?m)^# Clickra v[\d\.]+', "# Clickra v$newVersion"
        [System.IO.File]::WriteAllText("$root/$f", $content, $utf8NoBOM)
        Write-Host "[Doc] Synced README: $f" -ForegroundColor Gray
    }
}

# 5. 更新 CHANGELOG.md (在 [vX.X.X.0] 之前插入新版本)
$changelogPath = "CHANGELOG.md"
if (Test-Path $changelogPath) {
    $changelog = [System.IO.File]::ReadAllText("$root/$changelogPath", [System.Text.Encoding]::UTF8)
    $date = Get-Date -Format "yyyy-MM-dd"
    $newEntry = "`n## [v$newVersion] - $date`n`n- **TODO**: Add changelog entry here`n"
    $changelog = $changelog -replace '(?m)^# Changelog\r?\n', "# Changelog`n$newEntry"
    [System.IO.File]::WriteAllText("$root/$changelogPath", $changelog, $utf8NoBOM)
    Write-Host "[Doc] Updated CHANGELOG.md with new version entry" -ForegroundColor Gray
}

Write-Host "[Success] All files synced successfully!" -ForegroundColor Green

if ($Build) {
    Write-Host "`n[Build] Starting MSIX package build..." -ForegroundColor Cyan
    powershell -File scripts/build_msix.ps1
}
