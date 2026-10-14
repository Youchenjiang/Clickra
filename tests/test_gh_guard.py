import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import json
from gh_guard import validate_gh_args, resolve_thread


class GhGuardTests(unittest.TestCase):
    def test_safe_read_only_calls(self):
        for args in (["pr", "checks", "177"], ["pr", "view", "177", "--json", "state"],
                     ["run", "view", "123", "--log-failed"], ["pr", "list", "--state", "open"]):
            with self.subTest(args=args):
                self.assertIsNone(validate_gh_args(args))

    def test_mutations_are_denied(self):
        for args in (["pr", "create"], ["pr", "merge", "177"],
                     ["pr", "edit", "177"], ["api", "graphql", "-f", "query=mutation"],
                     ["api", "repos/org/repo/pulls/177/comments", "-f", "body=reply"],
                     ["pr", "comment", "177"], ["run", "rerun", "123"],
                     ["pr", "review", "177", "--comment"]):
            with self.subTest(args=args):
                self.assertIsNotNone(validate_gh_args(args))

    def test_disallow_shell_shaped_and_unknown_options(self):
        self.assertIsNotNone(validate_gh_args(["pr", "view", "177;echo"] ))
        self.assertIsNotNone(validate_gh_args(["pr", "view", "177", "--web"]))
        self.assertIsNotNone(validate_gh_args(["pr", "view", "177", "--json"]))

    def test_resolve_thread_only_uses_graphql_resolve_mutation(self):
        calls = []
        def fake_execute(argv):
            calls.append(argv)
            if len(calls) == 1:
                return json.dumps({"data": {"repository": {"pullRequest": {
                    "reviewThreads": {"nodes": [{"id": "PRRT_test123", "isResolved": False,
                                                 "isOutdated": False}], "pageInfo": {"hasNextPage": False}}}}}})
            return json.dumps({"data": {"resolveReviewThread": {
                "thread": {"id": "PRRT_test123", "isResolved": True}}}})
        result = resolve_thread("Youchenjiang/Clickra", "177", "PRRT_test123",
                                "177:PRRT_test123", execute=fake_execute)
        self.assertTrue(result["isResolved"])
        self.assertEqual(2, len(calls))
        self.assertIn("resolveReviewThread(input:", calls[1][4])
        self.assertNotIn("addPullRequestReviewComment", calls[1][4])

    def test_resolve_rejects_invalid_or_unverified_threads(self):
        for authorization in ("", "177:PRRT_other"):
            with self.assertRaises(ValueError):
                resolve_thread("Youchenjiang/Clickra", "177", "PRRT_test123",
                               authorization, execute=lambda argv: self.fail("no network allowed"))
        for resolved, outdated in ((True, False), (False, True)):
            def fake_execute(argv):
                return json.dumps({"data": {"repository": {"pullRequest": {
                    "reviewThreads": {"nodes": [{"id": "PRRT_test123", "isResolved": resolved,
                                                 "isOutdated": outdated}], "pageInfo": {"hasNextPage": False}}}}}})
            with self.assertRaises(ValueError):
                resolve_thread("Youchenjiang/Clickra", "177", "PRRT_test123",
                               "177:PRRT_test123", execute=fake_execute)

    def test_resolve_looks_beyond_first_page(self):
        calls = []
        def fake(argv):
            calls.append(argv)
            if "mutation(" in " ".join(argv):
                return json.dumps({"data": {"resolveReviewThread": {"thread": {"id": "PRRT_target", "isResolved": True}}}})
            later = "after=page2" in argv
            return json.dumps({"data": {"repository": {"pullRequest": {"reviewThreads": {
                "nodes": [{"id": "PRRT_target" if later else "PRRT_other", "isResolved": False, "isOutdated": False}],
                "pageInfo": {"hasNextPage": not later, "endCursor": "page2" if not later else None}
            }}}}})
        self.assertTrue(resolve_thread("Youchenjiang/Clickra", "177", "PRRT_target", "177:PRRT_target", execute=fake)["isResolved"])
        self.assertEqual(3, len(calls))
        self.assertIn("after=page2", calls[1])

    def test_resolve_refuses_graphql_errors_and_stuck_pagination(self):
        for payload in (
            {"errors": [{"message": "forbidden"}]},
            {"data": {"repository": {"pullRequest": {"reviewThreads": {
                "nodes": [], "pageInfo": {"hasNextPage": True, "endCursor": None}
            }}}}},
        ):
            with self.subTest(payload=payload):
                with self.assertRaises(RuntimeError):
                    resolve_thread("Youchenjiang/Clickra", "177", "PRRT_target",
                                   "177:PRRT_target", execute=lambda argv: json.dumps(payload))

    def test_command_specific_flags_and_empty_values(self):
        for argv in (["pr", "view", "1", "--workflow", "build"],
                     ["pr", "view", "1", "--repo="],
                     ["run", "view", "1", "--head", "main"],
                     ["pr", "list", "--limit", ""]):
            with self.subTest(argv=argv):
                self.assertIsNotNone(validate_gh_args(argv))


if __name__ == "__main__":
    unittest.main()
