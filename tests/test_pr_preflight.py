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
        self.assertIn("completed External provider metadata required", validate_pr_body(body))
        complete = body + ("- [x] **Assignee**: @owner\n"
                           "- [x] **Label**: agent\n"
                           "- [x] **Milestone**: Milestone 1\n"
                           "- [x] **External provider**: No new provider.\n")
        self.assertEqual([], validate_pr_body(complete))

    def test_rejects_unfinished_metadata_and_placeholders(self):
        body = ("## Summary\nText\n## Changes\n- Describe the change.\n"
                "## Validation\n- PASS\n## PR Metadata\n"
                "- [ ] **Assignee**: assign the PR owner when publishing.\n"
                "- [ ] **Label**: add an appropriate agent label.\n"
                "- [ ] **Milestone**: choose the current milestone.\n"
                "- [x] **External provider**: No new provider.\n")
        errors = validate_pr_body(body)
        self.assertIn("empty or placeholder ## Changes", errors)
        self.assertIn("completed Assignee metadata required", errors)
        self.assertIn("completed Label metadata required", errors)
        self.assertIn("completed Milestone metadata required", errors)

    def test_rejects_empty_sections(self):
        body = ("## Summary\n\n## Changes\n\n## Validation\n\n## PR Metadata\n"
                "- [x] **Assignee**: owner\n- [x] **Label**: agent\n"
                "- [x] **Milestone**: Roadmap\n"
                "- [x] **External provider**: no new provider\n")
        errors = validate_pr_body(body)
        self.assertIn("empty or placeholder ## Summary", errors)
        self.assertIn("empty or placeholder ## Changes", errors)


if __name__ == "__main__":
    unittest.main()
