param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\obj\icons")
)

$ErrorActionPreference = "Stop"

# Forward to the canonical IconGen compiler tool
$iconGenProj = Join-Path $PSScriptRoot "tools/IconGen/IconGen.csproj"
if (Test-Path -LiteralPath $iconGenProj) {
    dotnet run --project $iconGenProj -- --force
    if ($LASTEXITCODE -ne 0) {
        throw "IconGen failed with exit code $LASTEXITCODE"
    }
}
