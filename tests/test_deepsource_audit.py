"""Regression tests for authenticated DeepSource read-only pagination."""
import io
import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from deepsource_audit import pr_issues


class Response(io.BytesIO):
    def __enter__(self):
        return self

    def __exit__(self, *_args):
        self.close()


class DeepSourceAuditTests(unittest.TestCase):
    def test_requires_token(self):
        with self.assertRaisesRegex(ValueError, "DEEPSOURCE_TOKEN"):
            pr_issues("org/repo", 180, "")

    def test_fetches_all_pages_without_exposing_token(self):
        cursors = []

        def opener(request, timeout):
            self.assertEqual(15, timeout)
            self.assertEqual("Bearer secret", request.get_header("Authorization"))
            value = json.loads(request.data)["variables"]["after"]
            cursors.append(value)
            page = {"edges": [{"node": {"path": "src/a.cs" if value is None else "src/b.cs"}}],
                    "pageInfo": {"hasNextPage": value is None, "endCursor": "next"}}
            return Response(json.dumps({"data": {"repository": {"pullRequest": {"issues": page}}}}).encode())

        self.assertEqual(2, len(pr_issues("org/repo", 180, "secret", opener)))
        self.assertEqual([None, "next"], cursors)

    def test_rejects_api_errors(self):
        def opener(_request, timeout):
            return Response(b'{"errors":[{"message":"denied"}]}')

        with self.assertRaisesRegex(RuntimeError, "rejected"):
            pr_issues("org/repo", 180, "secret", opener)


if __name__ == "__main__":
    unittest.main()
