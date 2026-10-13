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
        def fake(argv):
            return json.dumps({"data": {"repository": {"pullRequest": {"reviewThreads": {
                "nodes": [], "pageInfo": {"hasNextPage": True, "endCursor": "repeat"}
            }}}}})
        with self.assertRaises(RuntimeError):
            pr_tool.review_threads("Youchenjiang/Clickra", 2, fake)

    def test_publish_refuses_without_side_effect(self):
        with redirect_stdout(StringIO()) as output:
            result = pr_tool.main(["publish"], lambda argv: self.fail("no command expected"))
        self.assertEqual(2, result)
        self.assertIn("disabled", output.getvalue())

    def test_ready_executes_only_versioned_hook_with_body(self):
        with TemporaryDirectory() as folder:
            body = Path(folder) / "pr.md"
            body.write_text("## Summary\n", encoding="utf-8")
            with patch.object(pr_tool.shutil, "which", side_effect=lambda name: "/git/sh" if name == "sh" else None), \
                 patch.object(pr_tool.subprocess, "call", return_value=7) as call:
                self.assertEqual(7, pr_tool.main(["ready", "--body-file", str(body)]))
            argv = call.call_args.args[0]
            self.assertEqual("/git/sh", argv[0])
            self.assertEqual("pre-push", Path(argv[1]).name)
            self.assertEqual(str(body.resolve()), call.call_args.kwargs["env"]["CLICKRA_PR_BODY_FILE"])
            self.assertNotIn("push", argv[0])

    def test_ready_missing_body_refuses_without_hook(self):
        with patch.object(pr_tool.subprocess, "call") as call:
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
