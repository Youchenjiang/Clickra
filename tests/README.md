# Tests

The primary test project is the C# contract suite:

```powershell
dotnet build tests\Clickra.Core.Tests\Clickra.Core.Tests.csproj --no-restore
dotnet tests\Clickra.Core.Tests\bin\Debug\net10.0-windows\Clickra.Core.Tests.dll
```

The C# runner follows the same fixture contract as the Python regression
runner below: tests that need `test_pdfs/` fixtures print `SKIP` (and count
as skipped, not failed) when the fixtures are absent, so a clean checkout or
CI without the ignored fixtures still reports a green suite. The summary line
counts passed / failed / skipped explicitly.

Pass `--require-fixtures` to flip that contract (mirroring the Python
runner's `--require-fixtures`): fixture-dependent tests that cannot run are
reported as failures and the runner exits non-zero, so a gate that expects
fixtures fails loudly instead of quietly passing. The `fixture-regression-tests`
CI job runs with this flag once fixtures are available.


PDF regression checks live under `tests/PdfRegression`. They require local
`test_pdfs/` fixtures, which are intentionally ignored by git because the PDFs
are large.

The offline suite compares every translated fixture with its source rendering.
It checks each page column for newly introduced blank bands, excessive growth
in total large-gap space, lost visual occupancy, and columns that end much
earlier than the source. The comparison uses rendered ink occupancy rather
than literal pixel equality, so translated glyph shapes do not count as layout
regressions.

```powershell
python tests\PdfRegression\run_translation_tests.py
```

If fixtures are missing, the PDF regression runner prints `SKIP` and exits 0.
Maintainers or CI jobs that expect fixtures can enforce them:

```powershell
python tests\PdfRegression\run_translation_tests.py --require-fixtures
```

To regenerate translated baselines from every PDF in `test_pdfs/source`:

```powershell
dotnet run --no-restore --project src\Clickra.CLI\Clickra.csproj -- translate-pdf --no-ui --out-dir test_pdfs\translated test_pdfs\source
```

`--out-dir`, `-o`, and `--out` are equivalent. The output directory is created
when it does not exist.

Useful optional PDF regression layers:

The ASTER fixtures have different purposes: `ASTER .pdf` intentionally has no
annotations, while `ASTER- .pdf` contains the publisher hyperlinks.  To verify
that a translated linked fixture retains every URI destination (rectangles may
move when text reflows), run:

```powershell
python tests\PdfRegression\test_pdf_links.py `
  --translated "tmp\pdfs\aster-google-full-release-candidate\ASTER- _translated.pdf"
```

Running without `--translated` still checks that the two source fixtures are
not accidentally treated as interchangeable.

ASTER may live either at the repository root or under `test_pdfs/source`. Its
E2E gate also runs the source/translated layout-occupancy comparison and writes
`ASTER _layout_occupancy.json` beside the generated PDF.

```powershell
# Full 16-page pipeline without network variability
python tests\PdfRegression\run_translation_tests.py --identity-e2e

# Live MyMemory smoke plus live 16-page provider routing
python tests\PdfRegression\run_translation_tests.py --provider-smoke --real-e2e

# Geometry heuristics that are useful for triage but have known false positives
python tests\PdfRegression\run_translation_tests.py --strict-render --legacy-diagnostics

# Local ASTER fixture with deterministic CJK layout stress translation
python tests\PdfRegression\run_translation_tests.py --aster-e2e
```

`tests/PdfRegression/translation_baseline_expectations.json` records existing
output debt so the regression gate fails on regressions without pretending the
debt is fixed. When a baseline is improved, lower or remove its allowance.
