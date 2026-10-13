import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from pr_preflight import validate_message, validate_pr_body


class PrPreflightTests(unittest.TestCase):
    def test_commit_requires_numbered_body(self):
        self.assertTrue(validate_message("fix(core): repair fallback"))
        self.assertEqual([], validate_message("fix(core): repair fallback\n\n1. Repair fallback."))

    def test_invalid_commit_title(self):
        self.assertTrue(validate_message("a nonconventional subject\n\n1. Change."))

    def test_pr_body_requires_provider_acknowledgement(self):
        body = "## Summary\nText\n## Changes\n- A\n## Validation\n- B\n## PR Metadata\n"
        self.assertIn("checked External provider metadata", validate_pr_body(body))
        self.assertEqual([], validate_pr_body(body + "- [x] **External provider**: No new provider.\n"))


if __name__ == "__main__":
    unittest.main()
