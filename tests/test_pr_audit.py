import json
import sys
import unittest
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import pr_audit
import pr_tool


class AuditTests(unittest.TestCase):
    def test_inventory_reads_all_categories_and_pages_without_writes(self):
        calls = []
        head = "a" * 40
        def fake(argv):
            calls.append(argv)
            self.assertEqual(["gh", "api"], argv[:2])
            self.assertNotIn("mutation", " ".join(argv))
            path = argv[2]
            if "/issues/178/comments" in path:
                return json.dumps([{"id": i, "body": "general"} for i in range(100)] if path.endswith("&page=1") else [{"id": 101, "body": "other"}])
            if "/pulls/178/comments" in path:
                return json.dumps([{"id": 2, "body": "inline", "commit_id": head,
                                    "html_url": "https://github.com/pull/178#discussion_r2"},
                                   {"id": 4, "body": "reply", "in_reply_to_id": 2,
                                    "commit_id": "b" * 40}])
            if "/pulls/178/reviews" in path:
                return json.dumps([{"id": 3, "body": "summary", "state": "CHANGES_REQUESTED"}])
            self.fail(path)
        thread = {"id": "PRRT_A", "isResolved": False, "isOutdated": True, "path": "a.py", "line": 3,
                  "comments": {"nodes": [{"url": "https://github.com/pull/178#discussion_r2"}]}}
        data = pr_audit.inventory("org/repo", 178, head, [thread], fake)
        self.assertEqual(101, data["counts"]["general"])
        self.assertEqual(2, data["counts"]["inline"])
        self.assertEqual(1, data["counts"]["reviews"])
        self.assertEqual(1, data["counts"]["outdated_unresolved"])
        self.assertEqual("current", data["inline_comments"][0]["head_relation"])
        self.assertEqual("PRRT_A", data["inline_comments"][0]["thread_id"])
        self.assertEqual("PRRT_A", data["inline_comments"][1]["thread_id"])
        self.assertEqual("historical", data["inline_comments"][1]["head_relation"])
        self.assertEqual("unscoped", data["general_comments"][0]["head_relation"])
        self.assertEqual(4, len(calls))

    def test_inventory_refuses_bad_endpoints_or_api_payload(self):
        for endpoint in ("issues/reviews", "pulls/merge", "../../secrets"):
            with self.assertRaises(ValueError):
                pr_audit.paged("org/repo", 178, endpoint, lambda _: self.fail("no call"))
        with self.assertRaises(RuntimeError):
            pr_audit.paged("org/repo", 178, "issues/comments", lambda _: "{}")

    def test_cli_audit_outputs_full_json_and_keeps_thread_state(self):
        fake_result = {"pr": 178, "head": "b" * 40, "threads": [], "inline_comments": [],
                       "general_comments": [], "reviews": [], "counts": {"threads": 0, "unresolved": 0,
                       "outdated_unresolved": 0, "inline": 0, "general": 0, "reviews": 0}}
        with patch.object(pr_tool, "inspect", return_value={"headRefOid": "b" * 40}), \
             patch.object(pr_tool, "review_threads", return_value=[]), \
             patch.object(pr_tool, "inventory", return_value=fake_result), \
             redirect_stdout(StringIO()) as output:
            pr_tool.audit("org/repo", 178, lambda _: self.fail("unexpected call"), as_json=True)
        self.assertEqual(fake_result, json.loads(output.getvalue()))


if __name__ == "__main__":
    unittest.main()
