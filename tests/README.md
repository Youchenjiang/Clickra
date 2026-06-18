# Translation test suite

Run the reliable offline gate:

```powershell
python tests/run_translation_tests.py
```

Useful optional layers:

```powershell
# Full 16-page pipeline without network variability
python tests/run_translation_tests.py --identity-e2e

# Live MyMemory smoke plus live 16-page provider routing
python tests/run_translation_tests.py --provider-smoke --real-e2e

# Geometry heuristics that are useful for triage but have known false positives
python tests/run_translation_tests.py --strict-render --legacy-diagnostics
```

The default gate contains:

- native C# layout/provider contracts;
- baseline health regression checks;
- baseline freshness, page count, text retention, duplicate-line and bounds checks;
- renderability and blank-page checks on curated high-risk pages.

`translation_baseline_expectations.json` records existing output debt so the
default gate fails on regressions without pretending the debt is fixed. When a
baseline is improved, lower or remove its allowance.
