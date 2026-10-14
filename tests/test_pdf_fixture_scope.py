import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from pdf_fixture_scope import requires_fixtures


class PdfFixtureScopeTests(unittest.TestCase):
    def test_governance_changes_do_not_require_external_pdfs(self):
        self.assertFalse(requires_fixtures(["AGENTS.md", "scripts/pr_tool.py"]))

    def test_translation_pipeline_requires_fixtures(self):
        self.assertTrue(requires_fixtures([
            "src/Clickra.Core/Processors/PdfTranslation/Layout/PdfTranslationLayoutPlanner.cs"
        ]))

    def test_translation_adapter_requires_fixtures(self):
        self.assertTrue(requires_fixtures([
            "src/Clickra.Core/Processors/FileProcessing/PdfTranslateProcessor.cs"
        ]))

    def test_pdf_regression_and_translation_entrypoint_require_fixtures(self):
        self.assertTrue(requires_fixtures(["tests/PdfRegression/pdf_test_common.py"]))
        self.assertTrue(requires_fixtures(["src/Clickra.Core/FileProcessor.cs"]))
        self.assertFalse(requires_fixtures(["scripts/pdf_fixture_scope.py"]))


if __name__ == "__main__":
    unittest.main()
