import importlib.util
import pathlib
import tempfile
import unittest
from unittest import mock


SCRIPT_PATH = pathlib.Path(__file__).resolve().parents[1] / "scripts" / "check_store_release_published.py"
SPEC = importlib.util.spec_from_file_location("check_store_release_published", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load Store publication probe from {SCRIPT_PATH}")
probe = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(probe)


class StorePublicationProbeTests(unittest.TestCase):
    def test_parse_release_version_requires_four_numeric_parts(self):
        self.assertEqual("3.11.1.0", probe.parse_release_version("v3.11.1.0"))
        for value in ("3.11.1.0", "v3.11", "v3.11.1.x", "v"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                probe.parse_release_version(value)

    def test_exact_published_version_matches(self):
        application = {"packageIdentityName": "g1014308.Clickra"}
        submission = {
            "status": "Published",
            "applicationPackages": [{"fileName": "Clickra.msix", "version": "3.11.1.0"}],
        }
        matched, reason = probe.published_submission_matches(
            application,
            submission,
            expected_identity="g1014308.Clickra",
            expected_version="3.11.1.0",
        )
        self.assertTrue(matched)
        self.assertIn("3.11.1.0", reason)

    def test_newer_or_older_published_version_does_not_match(self):
        application = {"packageIdentityName": "g1014308.Clickra"}
        submission = {
            "status": "Published",
            "applicationPackages": [{"version": "3.11.0.0"}],
        }
        matched, reason = probe.published_submission_matches(
            application,
            submission,
            expected_identity="g1014308.Clickra",
            expected_version="3.11.1.0",
        )
        self.assertFalse(matched)
        self.assertIn("3.11.0.0", reason)

    def test_mixed_live_versions_fail_closed(self):
        application = {"packageIdentityName": "g1014308.Clickra"}
        submission = {
            "status": "Published",
            "applicationPackages": [
                {"fileStatus": "Uploaded", "version": "3.11.0.0"},
                {"fileStatus": "Uploaded", "version": "3.11.1.0"},
            ],
        }
        matched, reason = probe.published_submission_matches(
            application,
            submission,
            expected_identity="g1014308.Clickra",
            expected_version="3.11.1.0",
        )
        self.assertFalse(matched)
        self.assertIn("3.11.0.0", reason)

    def test_pending_delete_versions_are_ignored(self):
        application = {"packageIdentityName": "g1014308.Clickra"}
        submission = {
            "status": "Published",
            "applicationPackages": [
                {"fileStatus": "PendingDelete", "version": "3.11.0.0"},
                {"fileStatus": "Uploaded", "version": "3.11.1.0"},
            ],
        }
        matched, _ = probe.published_submission_matches(
            application,
            submission,
            expected_identity="g1014308.Clickra",
            expected_version="3.11.1.0",
        )
        self.assertTrue(matched)

    def test_non_published_submission_does_not_match(self):
        matched, reason = probe.published_submission_matches(
            {"packageIdentityName": "g1014308.Clickra"},
            {"status": "Certification", "applicationPackages": [{"version": "3.11.1.0"}]},
            expected_identity="g1014308.Clickra",
            expected_version="3.11.1.0",
        )
        self.assertFalse(matched)
        self.assertIn("Certification", reason)

    def test_identity_mismatch_fails_closed(self):
        with self.assertRaises(RuntimeError):
            probe.published_submission_matches(
                {"packageIdentityName": "Other.App"},
                {"status": "Published", "applicationPackages": [{"version": "3.11.1.0"}]},
                expected_identity="g1014308.Clickra",
                expected_version="3.11.1.0",
            )

    def test_check_release_reads_last_published_submission_only(self):
        responses = [
            {
                "packageIdentityName": "g1014308.Clickra",
                "lastPublishedApplicationSubmission": {"id": "published-id"},
                "pendingApplicationSubmission": {"id": "pending-id"},
            },
            {
                "status": "Published",
                "applicationPackages": [{"version": "3.11.1.0"}],
            },
        ]
        with mock.patch.object(probe, "get_token", return_value="token"), \
             mock.patch.object(probe, "api_get", side_effect=responses) as api_get:
            published, reason, submission_id = probe.check_release_published(
                tag="v3.11.1.0",
                tenant_id="tenant",
                client_id="client",
                client_secret="secret",
                product_id="product",
            )
        self.assertTrue(published)
        self.assertEqual("published-id", submission_id)
        self.assertIn("3.11.1.0", reason)
        self.assertIn("published-id", api_get.call_args_list[1].args[0])

    def test_github_output_records_boolean(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            output = pathlib.Path(temp_dir) / "github-output.txt"
            probe.write_github_output(str(output), True)
            probe.write_github_output(str(output), False)
            self.assertEqual("published=true\npublished=false\n", output.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
