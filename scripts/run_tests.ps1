<#
.SYNOPSIS
    Runs the Clickra.Core test suite with correct invocation and optional clean.

.DESCRIPTION
    Replaces erroneous `dotnet test` invocations on Exe test projects.
    Executes `dotnet run --project tests/Clickra.Core.Tests/Clickra.Core.Tests.csproj`.
    Supports `-Clean` to purge stale build artifacts (bin/obj), obsolete platform outputs,
    leftover test PDFs, and orphaned temp directories before running tests.

.PARAMETER Clean
    Purges build outputs (bin/obj), stale test PDFs, and orphaned temp directories before testing.

.PARAMETER RequireFixtures
    Enforces test fixture presence (treats missing fixtures as FAIL instead of SKIP).

.PARAMETER Configuration
    Build configuration (default: Debug; can specify Release).
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$RequireFixtures,
    [string]$Configuration = "Debug",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path

function Remove-StalePath {
    param([string]$Path)
    if (Test-Path $Path) {
        try {
            Remove-Item -Recurse -Force $Path -ErrorAction SilentlyContinue
            Write-Host "  [Cleaned] $Path" -ForegroundColor DarkGray
            return 1
        } catch {
            Write-Warning "Failed to remove: $Path ($($_.Exception.Message))"
            return 0
        }
    }
    return 0
}

if ($Clean) {
    Write-Host "[Clean] Purging stale build and test artifacts..." -ForegroundColor Yellow
    $cleanedCount = 0

    # 1. Project bin and obj folders across all projects
    $projectDirs = @(
        "src/Clickra.Core",
        "src/Clickra.CLI",
        "src/ClickraShell",
        "src/ClickraLauncher",
        "src/Clickra.Fluent",
        "tests/Clickra.Core.Tests"
    )

    foreach ($p in $projectDirs) {
        $binPath = Join-Path $repoRoot "$p/bin"
        $objPath = Join-Path $repoRoot "$p/obj"
        $cleanedCount += Remove-StalePath $binPath
        $cleanedCount += Remove-StalePath $objPath
    }

    # 2. Obsolete platform outputs and orphan directories
    $obsoleteDirs = @(
        "src/Clickra.Fluent/bin/x64",
        "tmp/Diag"
    )
    foreach ($o in $obsoleteDirs) {
        $fullPath = Join-Path $repoRoot $o
        $cleanedCount += Remove-StalePath $fullPath
    }

    # 3. Leftover split failure and debug files in repo root
    $leftoverPatterns = @("clickra-split-fail-*.pdf", "*_renderdbg.log", "*_translated.pdf")
    foreach ($pattern in $leftoverPatterns) {
        Get-ChildItem -Path $repoRoot -Filter $pattern -File -ErrorAction SilentlyContinue | ForEach-Object {
            $cleanedCount += Remove-StalePath $_.FullName
        }
    }

    # 4. Stale isolated test data in %TEMP%
    $tempDir = [System.IO.Path]::GetTempPath()
    Get-ChildItem -Path $tempDir -Filter "clickra-test-data-*" -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $cleanedCount += Remove-StalePath $_.FullName
    }

    Write-Host "[Clean] Completed. Cleaned $cleanedCount item(s)." -ForegroundColor Green
}

# Construct the correct dotnet run invocation with warnings as errors enforced
$projectFile = Join-Path $repoRoot "tests/Clickra.Core.Tests/Clickra.Core.Tests.csproj"
$runArgs = @("--project", $projectFile, "-c", $Configuration, "--property:TreatWarningsAsErrors=true")

$testArgs = @()
if ($RequireFixtures) {
    $testArgs += "--require-fixtures"
}
if ($Clean) {
    $testArgs += "--clean"
}
if ($RemainingArgs) {
    $testArgs += $RemainingArgs
}

if ($testArgs.Count -gt 0) {
    $runArgs += "--"
    $runArgs += $testArgs
}

Write-Host "[Test] Running test suite: dotnet run $($runArgs -join ' ')" -ForegroundColor Cyan
& dotnet run @runArgs
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Host "[Test] Test suite failed (exit code $exitCode)." -ForegroundColor Red
} else {
    Write-Host "[Test] Test suite passed." -ForegroundColor Green
}

exit $exitCode
