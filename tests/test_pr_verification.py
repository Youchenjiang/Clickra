"""Conservative final PR checks reject missing or unresolved findings."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import pr_verification


class VerificationTests(unittest.TestCase):
    def test_pending_skipped_and_findings_never_report_green(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main",
              "statusCheckRollup": [{"name": "CI", "status": "IN_PROGRESS"},
                                    {"name": "Sourcery", "conclusion": "SKIPPED"}]}
        report = {"counts": {"unresolved": 1}, "sonar": {"total": 2}}
        result = pr_verification.summarize(pr, report, remote_sha="a" * 40)
        self.assertFalse(result["ready"])
        self.assertEqual(["Sourcery"], result["skipped"])
        self.assertGreaterEqual(len(result["problems"]), 3)

    def test_clean_snapshot_passes_with_explicit_skips(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main", "state": "OPEN",
              "statusCheckRollup": [{"name": name, "conclusion": "SUCCESS"} for name in pr_verification.REQUIRED] + [
                                    {"name": "Sourcery", "conclusion": "SKIPPED"}]}
        report = {"counts": {"unresolved": 0}, "sonar": {"total": 0}}
        result = pr_verification.summarize(pr, report, remote_sha="a" * 40)
        self.assertTrue(result["ready"])
        self.assertEqual(["Sourcery"], result["skipped"])

    def test_missing_checks_fail_closed(self):
        with self.assertRaisesRegex(RuntimeError, "missing"):
            pr_verification.summarize({"headRefOid": "a" * 40},
                                      {"counts": {"unresolved": 0}, "sonar": {"total": 0}},
                                      remote_sha="a" * 40)

    def test_absent_provider_is_not_considered_passing(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main", "state": "OPEN",
              "statusCheckRollup": [{"name": "CI", "conclusion": "SUCCESS"}]}
        result = pr_verification.summarize(
            pr, {"counts": {"unresolved": 0}, "sonar": {"total": 0}}, remote_sha="a" * 40)
        self.assertFalse(result["ready"])
        self.assertIn("Missing required check: DeepSource: Python", result["problems"][-1])

    def test_skipped_required_provider_blocks_acceptance(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main", "state": "OPEN",
              "statusCheckRollup": [{"name": name, "conclusion": "SKIPPED" if name == "DeepSource: Python" else "SUCCESS"}
                                    for name in pr_verification.REQUIRED]}
        report = {"counts": {"unresolved": 0}, "sonar": {"total": 0}}
        result = pr_verification.summarize(pr, report, remote_sha="a" * 40)
        self.assertFalse(result["ready"])
        self.assertTrue(any("required checks skipped" in issue for issue in result["problems"]))


if __name__ == "__main__":
    unittest.main()
