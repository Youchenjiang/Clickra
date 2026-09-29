param(
    [Parameter(Mandatory = $true)]
    [string]$Publisher
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Publisher)) {
    throw "GitHub MSIX publisher must not be empty."
}

$repoRoot = (Resolve-Path "$PSScriptRoot/..").Path
$identityFiles = @(
    "packaging/msix/AppxManifest.xml",
    "src/resources/AppxManifest.xml",
    "src/resources/Clickra.exe.manifest",
    "src/resources/ClickraShell.dll.manifest"
)

$backups = @{}

function Set-PublisherAttribute {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $encoding = [System.Text.UTF8Encoding]::new($hasBom)
    $text = [System.IO.File]::ReadAllText($Path, $encoding)

    $pattern = '(?i)(\bpublisher=")[^"]+("\s*)'
    $matches = [regex]::Matches($text, $pattern)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one Publisher attribute in '$Path', found $($matches.Count)."
    }

    $match = $matches[0]
    $xmlValue = [System.Security.SecurityElement]::Escape($Value)
    if ($null -eq $xmlValue) {
        throw "Failed to XML-escape Publisher value for '$Path'."
    }
    $replacement = $match.Groups[1].Value + $xmlValue + $match.Groups[2].Value
    $updated = $text.Substring(0, $match.Index) +
        $replacement +
        $text.Substring($match.Index + $match.Length)
    [System.IO.File]::WriteAllText($Path, $updated, $encoding)
}

Push-Location $repoRoot
try {
    foreach ($relativePath in $identityFiles) {
        $path = Join-Path $repoRoot $relativePath
        $backups[$path] = [System.IO.File]::ReadAllBytes($path)
        Set-PublisherAttribute -Path $path -Value $Publisher
    }

    Write-Host "[Package] Building GitHub direct-download MSIX with Publisher '$Publisher'." -ForegroundColor Gray
    & "$PSScriptRoot/build_msix.ps1" -SkipSigning
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub direct-download MSIX build failed with exit code $LASTEXITCODE."
    }
} finally {
    foreach ($entry in $backups.GetEnumerator()) {
        [System.IO.File]::WriteAllBytes($entry.Key, $entry.Value)
    }
    Pop-Location
}
