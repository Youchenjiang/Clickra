import json
import sys
import unittest
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import pr_tool


class PrToolTests(unittest.TestCase):
    def test_diagnose_reads_failed_action_log_and_external_review(self):
        calls = []
        def fake(argv):
            calls.append(argv)
            if argv[:3] == ["gh", "run", "view"]:
                return "failure\x1b[2J details"
            self.fail(f"unexpected call: {argv}")
        data = {"statusCheckRollup": [
            {"name": "unit tests", "conclusion": "FAILURE", "detailsUrl": "https://github.com/org/repo/actions/runs/123/job/5"},
            {"context": "Sourcery review", "state": "FAILURE", "targetUrl": "https://sourcery.ai"},
            {"name": "build", "conclusion": "SUCCESS"}]}
        threads = [{"isResolved": False, "isOutdated": False, "path": "scripts/a.py",
                    "comments": {"nodes": [{"body": "fix this", "url": "https://github.com/review"}]}}]
        with patch.object(pr_tool, "inspect", return_value=data), \
             patch.object(pr_tool, "review_threads", return_value=threads), \
             redirect_stdout(StringIO()) as output:
            failed, open_threads = pr_tool.diagnose("org/repo", 123, fake)
        self.assertEqual(2, len(failed))
        self.assertEqual(1, len(open_threads))
        self.assertEqual(["gh", "run", "view", "123", "--repo", "org/repo", "--log-failed"], calls[0])
        self.assertIn("fix this", output.getvalue())
        self.assertIn("External provider", output.getvalue())
        self.assertNotIn("\x1b", output.getvalue())

    def test_diagnose_separates_passing_analyzer_legacy_comments(self):
        data = {"headRefOid": "a" * 40, "statusCheckRollup": [
            {"context": "DeepSource: Python", "state": "SUCCESS"}]}
        threads = [
            {"isResolved": False, "isOutdated": False, "comments": {"nodes": [{"body": "<!-- DeepSource: old --> historical"}]}},
            {"isResolved": False, "isOutdated": False, "comments": {"nodes": [{"body": "Sourcery actionable"}]}},
            {"isResolved": False, "isOutdated": True, "comments": {"nodes": [{"body": "old"}]}}]
        with patch.object(pr_tool, "inspect", return_value=data), \
             patch.object(pr_tool, "review_threads", return_value=threads), \
             redirect_stdout(StringIO()) as output:
            failed, active = pr_tool.diagnose("org/repo", 123, lambda argv: json.dumps({"check_runs": []}))
        self.assertFalse(failed)
        self.assertEqual(1, len(active))
        self.assertIn("potentially historical unresolved thread(s): 2", output.getvalue())
        self.assertIn("a" * 40, output.getvalue())

    def test_diagnose_reads_external_check_run_summary(self):
        sha = "a" * 40
        details = {"check_runs": [{"name": "Sourcery review", "output": {
            "summary": "Blocking security issues: 6", "text": "Review details"}}]}
        calls = []
        def fake(argv):
            calls.append(argv)
            return json.dumps(details)
        data = {"headRefOid": sha, "statusCheckRollup": [
            {"name": "Sourcery review", "conclusion": "FAILURE", "detailsUrl": "https://sourcery.ai"}]}
        with patch.object(pr_tool, "inspect", return_value=data), \
             patch.object(pr_tool, "review_threads", return_value=[]), \
             redirect_stdout(StringIO()) as output:
            failed, _ = pr_tool.diagnose("org/repo", 178, fake)
        self.assertEqual(1, len(failed))
        self.assertEqual(["gh", "api", f"repos/org/repo/commits/{sha}/check-runs?per_page=100"], calls[0])
        self.assertIn("Blocking security issues: 6", output.getvalue())
        self.assertIn("Review details", output.getvalue())

    def test_remote_terminal_controls_are_neutralized(self):
        payload = {"number": 177, "title": "safe\x1b[2Jspoof", "url": "https://example.test/\x1b[H",
                   "baseRefName": "main", "headRefName": "feature/a", "state": "OPEN",
                   "statusCheckRollup": [{"name": "build\x1b[2J", "conclusion": "SUCCESS"}]}
        with patch.object(pr_tool, "inspect", return_value=payload), \
             patch.object(pr_tool, "print_review", return_value=[]), \
             redirect_stdout(StringIO()) as output:
            pr_tool.print_status("Youchenjiang/Clickra", 177)
        self.assertNotIn("\x1b", output.getvalue())
        self.assertIn("build [2J", output.getvalue())

    def test_origin_and_branch_context(self):
        def fake(argv):
            return "feature/pr-governance-preflight\n" if "branch" in argv else "git@github.com:Youchenjiang/Clickra.git\n"
        self.assertEqual(("Youchenjiang/Clickra", "feature/pr-governance-preflight"), pr_tool.context(fake))

    def test_review_paginates_and_does_not_write(self):
        calls = []
        def fake(argv):
            calls.append(argv)
            self.assertEqual(["gh", "api", "graphql"], argv[:3])
            cursor = "-f" in argv and "after=next" in argv
            return json.dumps({"data": {"repository": {"pullRequest": {"reviewThreads": {
                "nodes": [{"id": "PRRT_second" if cursor else "PRRT_first", "isResolved": cursor,
                           "isOutdated": False, "path": "src/file.cs", "line": 14,
                           "comments": {"nodes": [{"body": "Check fallback", "url": "https://example.test"}]}}],
                "pageInfo": {"hasNextPage": not cursor, "endCursor": "next" if not cursor else None}
            }}}}})
        threads = pr_tool.review_threads("Youchenjiang/Clickra", 177, fake)
        self.assertEqual(2, len(threads))
        self.assertEqual(2, len(calls))
        self.assertTrue(all("mutation" not in " ".join(call) for call in calls))
        with redirect_stdout(StringIO()) as output:
            self.assertEqual(1, len(pr_tool.print_review("Youchenjiang/Clickra", 177, fake)))
        self.assertIn("PRRT_first", output.getvalue())

    def test_review_rejects_stuck_pagination(self):
        def fake(_argv):
            return json.dumps({"data": {"repository": {"pullRequest": {"reviewThreads": {
                "nodes": [], "pageInfo": {"hasNextPage": True, "endCursor": "repeat"}
            }}}}})
        with self.assertRaises(RuntimeError):
            pr_tool.review_threads("Youchenjiang/Clickra", 2, fake)

    def test_review_fails_closed_on_graphql_errors(self):
        def fake(_argv):
            return json.dumps({"errors": [{"message": "rate limit exceeded"}]})
        with self.assertRaisesRegex(RuntimeError, "rate limit exceeded"):
            pr_tool.review_threads("Youchenjiang/Clickra", 2, fake)

    def test_publish_refuses_without_side_effect(self):
        with redirect_stdout(StringIO()) as output:
            result = pr_tool.main(["publish"], lambda argv: self.fail("no command expected"))
        self.assertEqual(2, result)
        self.assertIn("disabled", output.getvalue())

    def test_resolve_defaults_to_preview_and_requires_explicit_execute(self):
        def fake(argv):
            if argv[:3] == ["git", "branch", "--show-current"]:
                return "feature/pr-governance-preflight"
            if argv[:3] == ["git", "remote", "get-url"]:
                return "https://github.com/Youchenjiang/Clickra.git"
            self.assertEqual(["gh", "api", "graphql"], argv[:3])
            self.assertNotIn("mutation", " ".join(argv))
            return json.dumps({"data": {"repository": {"pullRequest": {"reviewThreads": {
                "nodes": [{"id": "PRRT_test123", "isResolved": False, "isOutdated": False, "comments": {"totalCount": 0, "nodes": []}}],
                "pageInfo": {"hasNextPage": False}
            }}}}})
        with redirect_stdout(StringIO()) as output:
            result = pr_tool.main(["resolve", "177", "--thread", "PRRT_test123"], fake)
        self.assertEqual(0, result)
        self.assertIn("preview only", output.getvalue())
        self.assertEqual(1, pr_tool.main(["resolve", "177", "--thread", "PRRT_test123", "--execute"], fake))

    def test_ready_executes_only_versioned_hook_with_body(self):
        with TemporaryDirectory() as folder:
            body = Path(folder) / "pr.md"
            body.write_text("## Summary\n", encoding="utf-8")
            with patch.object(pr_tool.shutil, "which", side_effect=lambda name: "/git/sh" if name == "sh" else None), \
                 patch.object(pr_tool.subprocess, "run", return_value=type("Result", (), {"returncode": 7})()) as call:
                self.assertEqual(7, pr_tool.main(["ready", "--body-file", str(body)]))
            argv = call.call_args.args[0]
            self.assertEqual("/git/sh", argv[0])
            self.assertEqual("pre-push", Path(argv[1]).name)
            self.assertEqual(str(body.resolve()), call.call_args.kwargs["env"]["CLICKRA_PR_BODY_FILE"])
            self.assertNotIn("push", argv[0])

    def test_ready_missing_body_refuses_without_hook(self):
        with patch.object(pr_tool.subprocess, "run") as call:
            self.assertEqual(1, pr_tool.main(["ready", "--body-file", "does-not-exist.md"]))
            call.assert_not_called()

    def test_status_prints_check_and_review_state(self):
        def fake(argv):
            if argv[:3] == ["git", "branch", "--show-current"]:
                return "feature/pr-governance-preflight"
            if argv[:3] == ["git", "remote", "get-url"]:
                return "https://github.com/Youchenjiang/Clickra.git"
            if argv[:3] == ["gh", "pr", "view"]:
                return json.dumps({"number": 177, "title": "Test", "url": "https://example.test/pr",
                                   "baseRefName": "main", "headRefName": "feature/a", "state": "OPEN",
                                   "statusCheckRollup": [{"name": "build", "conclusion": "SUCCESS"}]})
            return json.dumps({"data": {"repository": {"pullRequest": {"reviewThreads": {
                "nodes": [], "pageInfo": {"hasNextPage": False, "endCursor": None}
            }}}}})
        with redirect_stdout(StringIO()) as output:
            self.assertEqual(0, pr_tool.main(["status", "177"], fake))
        self.assertIn("CI build: SUCCESS", output.getvalue())
        self.assertIn("0 unresolved", output.getvalue())


if __name__ == "__main__":
    unittest.main()
