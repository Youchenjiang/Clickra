---
description: "Build Clickra, translate test PDFs, and run all diagnostic scripts (health, diagram violations, untranslated text). Produces a summary report."
---

# Clickra Build-Test-Diagnose Pipeline

Run the full Clickra PDF translation test pipeline: build → translate → diagnose.

## Arguments

`$ARGUMENTS` — Optional PDF filter. If empty, tests all PDFs in `Clickra/test_pdfs/source/`. If a filename substring is given (e.g. `2407`), only tests matching PDFs.

## Procedure

All commands run from the Clickra project root: `C:\Users\g1014308\Documents\GitHub\Youchen\Clickra`

### Step 1: Build

```powershell
dotnet build src/Clickra.CLI/Clickra.csproj -c Release 2>&1
```

If build fails, stop and report errors. Do not proceed to translation.

### Step 2: Translate test PDFs

For each PDF in `test_pdfs/source/` (filtered by `$ARGUMENTS` if provided):

```powershell
dotnet run --project src/Clickra.CLI/Clickra.csproj -- translate-pdf --quiet "test_pdfs/source/<filename>.pdf"
```

The `--quiet` flag outputs `<name>_translated.pdf` in the same directory as the input. After translation, move the output to `test_pdfs/translated/`:

```powershell
Move-Item "test_pdfs/source/<name>_translated.pdf" "test_pdfs/translated/" -Force
```

### Step 3: Run diagnostic scripts

Diagnostics are in `tools/pdf-diagnostics/`. For each translated PDF in `test_pdfs/translated/`:

**Health check** — overall PDF translation quality:
```powershell
python tools/pdf-diagnostics/pdf_health.py "test_pdfs/translated/<translated>.pdf"
```

**Diagram violation check** — compare original vs translated for diagram label translation:
```powershell
python tools/pdf-diagnostics/check_diagram_translation.py --orig "test_pdfs/source/<original>.pdf" --trans "test_pdfs/translated/<translated>.pdf"
```

**Untranslated text check** — find remaining English text that should have been translated:
```powershell
python tools/pdf-diagnostics/find_untranslated.py "test_pdfs/translated/<translated>.pdf"
```

### Step 4: Summary report

Compile results into a table:

| PDF | Build | Health | Diagram Violations | Untranslated |
|-----|-------|--------|--------------------|--------------|
| ... | ✅/❌ | pass/fail | N spans | N spans |

Report any regressions compared to prior runs (check session checkpoints for historical counts).

## Notes

- Diagnostic scripts directory: `Clickra/tools/pdf-diagnostics/` (25 scripts including `_pdf_utils.py` shared library).
- `test_pdfs/` directory structure: `source/` (original PDFs), `translated/` (translated output), `diagnostic/` (health check JSONs). May need to be populated if missing.
- Bypass regions (diagrams, code, tables, references, math, gray prompts, author block) are NOT translation failures per `docs/translation_rules.md` §2.
- Diagram violation counts from prior runs: 2407=136, PentestAgent=52, TOGLL=37, final_project=156 (proximity-based).
- If `pdf_health.py` reports failures, cross-reference with `docs/translation_rules.md` to determine if they are real issues or expected bypass behavior.
