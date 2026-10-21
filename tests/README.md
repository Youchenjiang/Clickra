# Tests

This directory is dedicated exclusively to **Clickra product code tests**:
1. **`Clickra.Core.Tests/`**: C# contract suite (architecture guards, localization, settings, and functional unit tests).
2. **`PdfRegression/`**: Python PDF translation, visual layout occupancy, and rendering regression suite.

> Developer tooling, PR governance, and Store publication unit tests live under [`scripts/tests/`](../scripts/tests/README.md).

## C# Contract Suite

The primary test project is the C# contract suite:

```powershell
dotnet run --project tests\Clickra.Core.Tests\Clickra.Core.Tests.csproj
```

The C# test runner distinguishes **Guards** (architecture, localization, settings registry, and UI resource contracts) from **Functional** unit tests:
- `dotnet run --project tests\Clickra.Core.Tests\Clickra.Core.Tests.csproj -- --guards` runs only the architecture and localization guards.
- `dotnet run --project tests\Clickra.Core.Tests\Clickra.Core.Tests.csproj -- --functional` runs only the functional unit tests.
- Without flags, the suite runs all tests and reports both overall counts and per-category breakdowns.

The C# runner follows the same fixture contract as the Python regression
runner below: tests that need `test_pdfs/` fixtures print `SKIP` (and count
as skipped, not failed) when the fixtures are absent, so a clean checkout or
CI without the ignored fixtures still reports a green suite. The summary line
counts passed / failed / skipped explicitly.

Pass `--require-fixtures` to flip that contract (mirroring the Python
runner's `--require-fixtures`): fixture-dependent tests that cannot run are
reported as failures and the runner exits non-zero, so a gate that expects
fixtures fails loudly instead of quietly passing. The `fixture-regression-tests`
CI job runs with this flag once fixtures are available. The ordinary PR gate
does not require every maintainer to download the ignored `test_pdfs/` tree.
The versioned pre-push hook and CI run the three pinned public PDF identity
translation tests when changes touch translation code or regression tests
(see `scripts/pdf_fixture_scope.py`); otherwise they are supplementary.
The PDF downloads are automatic and SHA-256 verified. A download failure or
translation failure blocks translation-related changes; no private PDF tree is
needed. This identity test does **not** certify translation language quality or
the historical manually reviewed translated-PDF visual baselines.


PDF regression checks live under `tests/PdfRegression`. They require local
`test_pdfs/` fixtures, which are intentionally ignored by git because the PDFs
are large.

For the three selected public-source fixtures, run
`python scripts/fetch_public_pdf_fixtures.py 2407 pentest togll` followed by
`python tests/PdfRegression/test_public_pdf_sources.py` (requires PyMuPDF).
To exercise the actual Clickra translation pipeline without contacting any
translation provider, use
`python tests/PdfRegression/test_public_translation_identity.py` on a machine
with the .NET SDK and the full Clickra source checkout. This opt-in test runs
all three PDFs in identity mode and checks basic output integrity. It does not
test translation language quality or replace curated visual regression checks.
The downloader verifies pinned SHA-256 digests, stores PDFs in the ignored
`test_pdfs/downloaded_candidates/` directory and does not commit them to Git.
This validates retrieval, PDF parsing and extractable text, **not translation
correctness**. The resulting PDFs are **not automatically equivalent** to
the historic regression fixtures: the arXiv version and the PentestAgent
camera-ready version may differ in layout, and `2407.11279v1_clean.pdf` may
be modified. Do not rename candidates or count tests as passing without
checking the document identity, page counts, assertions and expected baseline.
`114423046_final_project.pdf` has no verified public download source.
Do not claim the legacy translated-PDF baseline tests pass solely because
these public inputs can be downloaded or parsed. Translation E2E and visual
regression assertions still need independent expected outputs.

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
