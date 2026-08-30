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

# 1. 敺?Directory.Build.props ???桀??
$content = [System.IO.File]::ReadAllText("$root/$propsPath", [System.Text.Encoding]::UTF8)
if ($content -match '<Version>(?<v>.*)</Version>') {
    $currentVersion = [version]$Matches['v']
} else {
    Write-Error "Could not find Version in $propsPath"
}

# 2. 閮??啁???$major = $currentVersion.Major
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

# 3. ?湔??獢?# Directory.Build.props
$newProps = $content -replace '<Version>.*</Version>', "<Version>$newVersion</Version>"
[System.IO.File]::WriteAllText("$root/$propsPath", $newProps, $utf8NoBOM)

# AppxManifest.xml (???湔憭惜???亙惜)
    $manifestPaths = @("packaging/msix/AppxManifest.xml", "packaging/msix/AppxManifest.Fluent.xml", "src/resources/AppxManifest.xml")
foreach ($mPath in $manifestPaths) {
    if (Test-Path $mPath) {
        $manifest = [System.IO.File]::ReadAllText("$root/$mPath", [System.Text.Encoding]::UTF8)
        $newManifest = $manifest -replace '(?<=<Identity\s+[^>]*?Version=")([\d\.]+)', $newVersion
        [System.IO.File]::WriteAllText("$root/$mPath", $newManifest, $utf8NoBOM)
        Write-Host "[Manifest] Synced Manifest: $mPath" -ForegroundColor Gray
    }
}

# 4. ?湔 CHANGELOG.md嚗?暹??批捆???啁??穿?
$changelogPath = "CHANGELOG.md"
if (Test-Path $changelogPath) {
    $changelog = [System.IO.File]::ReadAllText("$root/$changelogPath", [System.Text.Encoding]::UTF8)
    $date = Get-Date -Format "yyyy-MM-dd"
    $newEntry = "`n## [v$newVersion] - $date`n`n- **TODO**: Add changelog entry here`n"
    $changelog = $changelog -replace '(?m)^# Changelog\r?\n', "# Changelog`n$newEntry"
    [System.IO.File]::WriteAllText("$root/$changelogPath", $changelog, $utf8NoBOM)
    Write-Host "[Doc] Updated CHANGELOG.md with new version entry" -ForegroundColor Gray
}

# 5. ?湔 README 瑼?嚗?頧??穿?蝚?蝑宏??CHANGELOG嚗?????蝑?
$readmeFiles = @("README.md", "README.zh-TW.md")
foreach ($f in $readmeFiles) {
    if (Test-Path $f) {
        $content = [System.IO.File]::ReadAllText("$root/$f", [System.Text.Encoding]::UTF8)
        # ?湔璅? (# Clickra vX.X.X.X)
        $content = $content -replace '(?m)^# Clickra v[\d\.]+', "# Clickra v$newVersion"

        # ?曉?銵冽銝剔?鞈?銵??璅?????嚗?        $lines = $content -split "`n"
        $tableStart = -1
        $rowCount = 0
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^\| \*\*v[\d\.]+\*\*') {
                if ($tableStart -eq -1) { $tableStart = $i }
                $rowCount++
            }
        }

        # 憒???蝑誑銝??洵3蝑宏??CHANGELOG
        if ($rowCount -ge 3 -and $tableStart -ge 0) {
            $thirdRow = $lines[$tableStart + 2]
            if ($thirdRow -match '\*\*(v[\d\.]+)\*\*\s*\|\s*([^|]+?)\s*\|\s*(.+?)\s*\|') {
                $oldVersion = $Matches[1]
                $oldDate = (Get-Date $Matches[2].Trim()).ToString("yyyy-MM-dd")
                $oldDesc = $Matches[3].Trim()

                # 蝘駁 README 銝剔?蝚砌?蝑?                $lines = $lines[0..($tableStart + 1)] + $lines[($tableStart + 3)..($lines.Count - 1)]
                $content = $lines -join "`n"

                # 撠??? CHANGELOG嚗銝??剁????亙蝚砌????祆?憿???
                if (Test-Path $changelogPath) {
                    $changelog = [System.IO.File]::ReadAllText("$root/$changelogPath", [System.Text.Encoding]::UTF8)
                    $escapedVersion = [regex]::Escape($oldVersion)
                    if ($changelog -notmatch "##\s*\[?v?${escapedVersion}\]?") {
                        $oldEntry = "`n## [$oldVersion] - $oldDate`n`n- $oldDesc`n"
                        # ?曉蝚砌???## [vX.X.X] 璅???蝵殷???刻府璅?銋?
                        $firstIdx = $changelog.IndexOf("## [v")
                        $secondIdx = -1
                        if ($firstIdx -ge 0) {
                            $secondIdx = $changelog.IndexOf("## [v", $firstIdx + 5)
                        }
                        if ($secondIdx -ge 0) {
                            $changelog = $changelog.Insert($secondIdx, $oldEntry)
                        } else {
                            $changelog = $changelog + $oldEntry
                        }
                        [System.IO.File]::WriteAllText("$root/$changelogPath", $changelog, $utf8NoBOM)
                        Write-Host "[Doc] Moved $oldVersion from README to CHANGELOG" -ForegroundColor Gray
                    } else {
                        Write-Host "[Doc] Version $oldVersion already exists in CHANGELOG, skipping rotation" -ForegroundColor Gray
                    }
                }
            }
        }

        # ??啁??祈??啗”?潮??剁?璅?????銋?嚗?        $lines = $content -split "`n"
        $tableStart = -1
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^\| \*\*v[\d\.]+\*\*') {
                if ($tableStart -eq -1) { $tableStart = $i }
                break
            }
        }
        if ($tableStart -ge 0) {
            $date = Get-Date -Format "yyyy/MM/dd"
            $newRow = "| **v$newVersion** | $date | **TODO**: Add milestone description here. |"
            $lines = $lines[0..($tableStart - 1)] + @($newRow) + $lines[$tableStart..($lines.Count - 1)]
            $content = $lines -join "`n"
        }

        [System.IO.File]::WriteAllText("$root/$f", $content, $utf8NoBOM)
        Write-Host "[Doc] Synced README: $f" -ForegroundColor Gray
    }
}

Write-Host "[Success] All files synced successfully!" -ForegroundColor Green

if ($Build) {
    Write-Host "`n[Build] Starting MSIX package build..." -ForegroundColor Cyan
    powershell -File scripts/build_msix.ps1
}
