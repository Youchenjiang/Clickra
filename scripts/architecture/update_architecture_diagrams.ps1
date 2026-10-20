# Regenerate docs/architecture (run from the repo root):
#   powershell -ExecutionPolicy Bypass -File scripts\architecture\update_architecture_diagrams.ps1
# 1. Roslyn (scripts/architecture/SemanticGraph) compiles every project and writes file-to-file references
# 2. Count lines of every .cs under src/
# 3. Redraw docs/architecture/*.html and svg/
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$arch = Join-Path $repo 'scripts\architecture'
$dotnet = Join-Path $repo '..\.clickra-tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$python = if (Get-Command python -ErrorAction SilentlyContinue) { 'python' } else { 'py' }

& $dotnet run --project (Join-Path $arch 'SemanticGraph\SemanticGraph.csproj') -c Release -- $repo (Join-Path $arch 'semantic-msbuild.json')
if ($LASTEXITCODE -ne 0) { throw "SemanticGraph failed ($LASTEXITCODE)" }
& $python (Join-Path $arch 'file_lines.py')
if ($LASTEXITCODE -ne 0) { throw 'file_lines.py failed' }
& $python (Join-Path $arch 'generate_layers.py')
if ($LASTEXITCODE -ne 0) { throw 'generate_layers.py failed' }
Write-Host 'Done. Check the output for "names ok" and any warning lines (stale names, lines no longer in code).'
