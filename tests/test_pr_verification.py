"""Conservative final PR checks reject missing or unresolved findings."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import pr_verification


class VerificationTests(unittest.TestCase):
    def test_new_code_duplication_is_reported_without_new_zero_policy(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main", "state": "OPEN",
              "statusCheckRollup": [{"name": name, "conclusion": "SUCCESS"}
                                    for name in pr_verification.REQUIRED]}
        quality = {"metrics": {"new_duplicated_lines_density": "0.2"},
                   "quality_gate": {"status": "OK", "conditions": []}}
        report = {"counts": {"unresolved": 0}, "sonar": {"total": 0},
                  "sonar_quality": quality}
        result = pr_verification.summarize(pr, report, remote_sha="a" * 40)
        self.assertTrue(result["ready"])
        self.assertEqual(quality, result["sonar_quality"])
        quality["quality_gate"]["status"] = "ERROR"
        result = pr_verification.summarize(pr, report, remote_sha="a" * 40)
        self.assertFalse(result["ready"])
        self.assertIn("SonarQube quality gate not passed", result["problems"])

    def test_dependency_scan_is_required_only_for_matching_changed_paths(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main", "state": "OPEN",
              "statusCheckRollup": [{"name": name, "conclusion": "SUCCESS"}
                                    for name in pr_verification.REQUIRED]}
        report = {"counts": {"unresolved": 0}, "sonar": {"total": 0}}
        pr["files"] = [{"path": "scripts/pr_tool.py"}]
        self.assertTrue(pr_verification.summarize(pr, report, remote_sha="a" * 40)["ready"])
        pr["files"] = [{"path": "src/Clickra.Core/Example.cs"}]
        result = pr_verification.summarize(pr, report, remote_sha="a" * 40)
        self.assertFalse(result["ready"])
        self.assertTrue(any("NuGet Dependency Vulnerability Scan" in error
                            for error in result["problems"]))

    def test_pending_skipped_and_findings_never_report_green(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main",
              "statusCheckRollup": [{"name": "CI", "status": "IN_PROGRESS"},
                                    {"name": "Sourcery", "conclusion": "SKIPPED"}]}
        report = {"counts": {"unresolved": 1}, "sonar": {"total": 2},
                  "threads": [{"id": "PRRT_1", "path": "file.cs", "line": 10, "resolved": False, "outdated": True}]}
        result = pr_verification.summarize(pr, report, remote_sha="a" * 40)
        self.assertFalse(result["ready"])
        self.assertEqual(["Sourcery"], result["skipped"])
        self.assertGreaterEqual(len(result["problems"]), 3)
        self.assertEqual([{"id": "PRRT_1", "path": "file.cs", "line": 10, "outdated": True}],
                         result["unresolved_thread_details"])

    def test_clean_snapshot_passes_with_explicit_skips(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main", "state": "OPEN",
              "statusCheckRollup": [{"name": name, "conclusion": "SUCCESS"} for name in pr_verification.REQUIRED] + [
                                    {"name": "Sourcery", "conclusion": "SKIPPED"}]}
        report = {"counts": {"unresolved": 0}, "sonar": {"total": 0}}
        result = pr_verification.summarize(pr, report, remote_sha="a" * 40)
        self.assertTrue(result["ready"])
        self.assertEqual(["Sourcery"], result["skipped"])

    def test_verify_includes_live_check_status_urls_and_required_flags(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main", "state": "OPEN",
              "statusCheckRollup": [{"name": name, "conclusion": "SUCCESS",
                                     "detailsUrl": "https://github.com/example/check"}
                                    for name in pr_verification.REQUIRED] + [
                                        {"context": "Sourcery review", "state": "PENDING",
                                         "targetUrl": "https://sourcery.ai"}]}
        report = {"counts": {"unresolved": 0}, "sonar": {"total": 0}}
        result = pr_verification.summarize(pr, report, remote_sha="a" * 40)
        self.assertFalse(result["ready"])
        self.assertEqual(len(pr_verification.REQUIRED) + 1, len(result["checks"]))
        self.assertEqual("https://github.com/example/check", result["checks"][0]["url"])
        self.assertTrue(result["checks"][0]["required"])
        self.assertFalse(result["checks"][-1]["required"])

    def test_obsolete_aggregate_build_name_is_not_required(self):
        self.assertNotIn("Build Core / CLI / Shell / Setup", pr_verification.REQUIRED)
        self.assertIn("Build Clickra.Core", pr_verification.REQUIRED)
        self.assertIn("Build Clickra.Fluent", pr_verification.REQUIRED)

    def test_public_pdf_regression_is_required(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main", "state": "OPEN",
              "statusCheckRollup": [{"name": name, "conclusion": "SUCCESS"}
                                    for name in pr_verification.REQUIRED
                                    if name != "Public PDF translation regression"]}
        report = {"counts": {"unresolved": 0}, "sonar": {"total": 0}}
        result = pr_verification.summarize(pr, report, remote_sha="a" * 40)
        self.assertFalse(result["ready"])
        self.assertTrue(any("Public PDF translation regression" in issue
                            for issue in result["problems"]))
    def test_merged_deleted_branch_reports_historical_checks(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main", "state": "MERGED",
              "statusCheckRollup": [{"name": name, "conclusion": "SUCCESS"}
                                    for name in pr_verification.REQUIRED]}
        report = {"counts": {"unresolved": 0}, "sonar": {"total": 0}}
        result = pr_verification.summarize(pr, report, remote_sha=None)
        self.assertFalse(result["ready"])
        self.assertEqual("MERGED", result["state"])
        self.assertTrue(result["historical_checks_passed"])
        self.assertIn("PR is not open", result["problems"])

    def test_open_missing_remote_sha_remains_blocked(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main", "state": "OPEN",
              "statusCheckRollup": [{"name": name, "conclusion": "SUCCESS"}
                                    for name in pr_verification.REQUIRED]}
        report = {"counts": {"unresolved": 0}, "sonar": {"total": 0}}
        result = pr_verification.summarize(pr, report, remote_sha=None)
        self.assertFalse(result["ready"])
        self.assertIn("PR HEAD differs from remote SHA", result["problems"])

    def test_merged_historical_checks_do_not_hide_failed_checks(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main", "state": "MERGED",
              "statusCheckRollup": [{"name": name, "conclusion": "FAILURE" if name == "DeepSource: C#" else "SUCCESS"}
                                    for name in pr_verification.REQUIRED]}
        report = {"counts": {"unresolved": 0}, "sonar": {"total": 0}}
        result = pr_verification.summarize(pr, report, remote_sha=None)
        self.assertFalse(result["historical_checks_passed"])
        self.assertTrue(any("DeepSource: C#" in problem for problem in result["problems"]))

    def test_closed_unmerged_pr_not_ready(self):
        pr = {"headRefOid": "a" * 40, "baseRefName": "main", "state": "CLOSED",
              "statusCheckRollup": [{"name": name, "conclusion": "SUCCESS"}
                                    for name in pr_verification.REQUIRED]}
        report = {"counts": {"unresolved": 0}, "sonar": {"total": 0}}
        result = pr_verification.summarize(pr, report, remote_sha=None)
        self.assertFalse(result["ready"])
        self.assertFalse(result["historical_checks_passed"])
        self.assertIn("PR is not open", result["problems"])

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
