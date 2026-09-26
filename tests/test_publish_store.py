import importlib.util
import pathlib
import unittest
from unittest import mock


SCRIPT_PATH = pathlib.Path(__file__).resolve().parents[1] / "scripts" / "publish_store.py"
SPEC = importlib.util.spec_from_file_location("publish_store", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load Store publishing module from {SCRIPT_PATH}")
publish_store = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(publish_store)


class VerifyCommitStatusTests(unittest.TestCase):
    def test_confirms_accepted_status_immediately(self):
        with mock.patch.object(publish_store, "api_request", return_value={"status": "Certification"}), \
             mock.patch.object(publish_store.time, "sleep") as sleep:
            result = publish_store.verify_commit_status("token", "product", "submission")

        self.assertEqual("confirmed", result)
        sleep.assert_not_called()

    def test_commit_failed_fails_immediately(self):
        response = {"status": "CommitFailed", "statusDetails": {"errors": []}}
        with mock.patch.object(publish_store, "api_request", return_value=response), \
             mock.patch.object(publish_store.time, "sleep") as sleep:
            result = publish_store.verify_commit_status("token", "product", "submission")

        self.assertIsNone(result)
        sleep.assert_not_called()

    def test_commit_started_is_accepted_after_short_poll_window(self):
        with mock.patch.object(publish_store, "api_request", return_value={"status": "CommitStarted"}) as request, \
             mock.patch.object(publish_store.time, "sleep") as sleep:
            result = publish_store.verify_commit_status("token", "product", "submission")

        self.assertEqual("unverified", result)
        self.assertEqual(publish_store.POST_COMMIT_POLL_ATTEMPTS, request.call_count)
        self.assertEqual(publish_store.POST_COMMIT_POLL_ATTEMPTS - 1, sleep.call_count)
        sleep.assert_called_with(publish_store.POST_COMMIT_POLL_INTERVAL_SECONDS)

    def test_unknown_final_status_fails_closed(self):
        with mock.patch.object(publish_store, "api_request", return_value={"status": "MysteryState"}), \
             mock.patch.object(publish_store.time, "sleep"):
            result = publish_store.verify_commit_status("token", "product", "submission")

        self.assertIsNone(result)

    def test_missing_final_response_fails_closed(self):
        responses = [
            {"status": "CommitStarted"},
            {"status": "CommitStarted"},
            None,
        ]
        with mock.patch.object(publish_store, "api_request", side_effect=responses), \
             mock.patch.object(publish_store.time, "sleep"):
            result = publish_store.verify_commit_status("token", "product", "submission")

        self.assertIsNone(result)


if __name__ == "__main__":
    unittest.main()
