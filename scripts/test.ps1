<#
.SYNOPSIS
    Convenience shortcut to run the Clickra test suite with proper invocation.

.EXAMPLE
    .\scripts\test.ps1
    .\scripts\test.ps1 -Clean
    .\scripts\test.ps1 -RequireFixtures
    .\scripts\test.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$RequireFixtures,
    [string]$Configuration = "Debug",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$runTestsScript = Join-Path $PSScriptRoot "run_tests.ps1"
& $runTestsScript -Clean:$Clean -RequireFixtures:$RequireFixtures -Configuration $Configuration @RemainingArgs
exit $LASTEXITCODE
