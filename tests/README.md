# Tests

The primary test project is the C# contract suite:

```powershell
dotnet build tests\Clickra.Core.Tests\Clickra.Core.Tests.csproj --no-restore
dotnet tests\Clickra.Core.Tests\bin\Debug\net10.0-windows\Clickra.Core.Tests.dll
```

PDF regression checks live under `tests/PdfRegression`. They require local
`test_pdfs/` fixtures, which are intentionally ignored by git because the PDFs
are large.

```powershell
python tests\PdfRegression\run_translation_tests.py
```

If fixtures are missing, the PDF regression runner prints `SKIP` and exits 0.
Maintainers or CI jobs that expect fixtures can enforce them:

```powershell
python tests\PdfRegression\run_translation_tests.py --require-fixtures
```

Useful optional PDF regression layers:

```powershell
# Full 16-page pipeline without network variability
python tests\PdfRegression\run_translation_tests.py --identity-e2e

# Live MyMemory smoke plus live 16-page provider routing
python tests\PdfRegression\run_translation_tests.py --provider-smoke --real-e2e

# Geometry heuristics that are useful for triage but have known false positives
python tests\PdfRegression\run_translation_tests.py --strict-render --legacy-diagnostics
```

`tests/PdfRegression/translation_baseline_expectations.json` records existing
output debt so the regression gate fails on regressions without pretending the
debt is fixed. When a baseline is improved, lower or remove its allowance.
