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

# 1. Read the current version from Directory.Build.props
$content = [System.IO.File]::ReadAllText("$root/$propsPath", [System.Text.Encoding]::UTF8)
if ($content -match '<Version>(?<v>.*)</Version>') {
    $currentVersion = [version]$Matches['v']
} else {
    Write-Error "Could not find Version in $propsPath"
}

# 2. Compute the new version from the requested bump type
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

# ReadAllText strips a detected UTF-8 BOM from the returned string. Preserve the
# file's existing BOM state when writing version updates back to disk.
function Write-TextPreservingBom {
    param(
        [string]$Path,
        [string]$Text
    )

    $withBom = $false
    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite)
    try {
        $head = New-Object byte[] 3
        $withBom = (
            $stream.Read($head, 0, 3) -eq 3 -and
            $head[0] -eq 0xEF -and
            $head[1] -eq 0xBB -and
            $head[2] -eq 0xBF)
    } finally {
        $stream.Dispose()
    }

    $encoding = New-Object System.Text.UTF8Encoding($withBom)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

# 3. Update Directory.Build.props
$newProps = $content -replace '<Version>.*</Version>', "<Version>$newVersion</Version>"
Write-TextPreservingBom -Path "$root/$propsPath" -Text $newProps

# Update all AppxManifest.xml files (Identity Version attribute)
$manifestPaths = @("packaging/msix/AppxManifest.xml", "packaging/msix/AppxManifest.Fluent.xml", "src/resources/AppxManifest.xml")
foreach ($mPath in $manifestPaths) {
    if (Test-Path $mPath) {
        $manifest = [System.IO.File]::ReadAllText("$root/$mPath", [System.Text.Encoding]::UTF8)
        $newManifest = $manifest -replace '(?<=<Identity\s+[^>]*?Version=")([\d\.]+)', $newVersion
        Write-TextPreservingBom -Path "$root/$mPath" -Text $newManifest
        Write-Host "[Manifest] Synced Manifest: $mPath" -ForegroundColor Gray
    }
}

# 4. Insert a TODO placeholder entry at the top of CHANGELOG.md
$changelogPath = "CHANGELOG.md"
if (Test-Path $changelogPath) {
    $changelog = [System.IO.File]::ReadAllText("$root/$changelogPath", [System.Text.Encoding]::UTF8)
    $date = Get-Date -Format "yyyy-MM-dd"
    $newEntry = "`n## [v$newVersion] - $date`n`n- **TODO**: Add changelog entry here`n"
    $changelog = $changelog -replace '(?m)^# Changelog\r?\n', "# Changelog`n$newEntry"
    Write-TextPreservingBom -Path "$root/$changelogPath" -Text $changelog
    Write-Host "[Doc] Updated CHANGELOG.md with new version entry" -ForegroundColor Gray
}

# 5. Update StoreListing version stamp at the top of each file
$storeListingFiles = @(
    "docs/StoreListing_EN.md",
    "docs/StoreListing_ZH.md",
    "docs/StoreListing_JA.md",
    "docs/StoreListing_KO.md",
    "docs/StoreListing_ZH-CN.md"
)
foreach ($f in $storeListingFiles) {
    # A missing listing must fail the release, never be silently skipped.
    if (-not (Test-Path "$root/$f")) {
        throw "Required StoreListing file not found: $f"
    }
    $content = [System.IO.File]::ReadAllText("$root/$f", [System.Text.Encoding]::UTF8)
    # Only bump the version stamp in the top-of-file intro sentence; never
    # rewrite version-formatted strings that may appear later in feature
    # descriptions or historical notes.
    $eol = if ($content -match "`r`n") { "`r`n" } else { "`n" }
    $lines = $content -split "`r?`n"
    $stampFound = $false
    $headerLimit = [Math]::Min(10, $lines.Count)
    for ($i = 0; $i -lt $headerLimit; $i++) {
        if ($lines[$i] -match 'v\d+\.\d+\.\d+\.\d+') {
            $lines[$i] = $lines[$i] -replace 'v\d+\.\d+\.\d+\.\d+', "v$newVersion"
            $stampFound = $true
            break
        }
    }
    if (-not $stampFound) {
        throw "No version stamp found in the top of $f; refusing to bump it blindly"
    }
    $content = $lines -join $eol
    Write-TextPreservingBom -Path "$root/$f" -Text $content
    Write-Host "[Doc] Synced StoreListing: $f" -ForegroundColor Gray
}

Write-Host "[Success] All files synced successfully!" -ForegroundColor Green

if ($Build) {
    Write-Host "`n[Build] Starting MSIX package build..." -ForegroundColor Cyan
    powershell -File scripts/build_msix.ps1
}