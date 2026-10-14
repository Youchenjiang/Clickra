"""SonarQube Cloud inventory must be complete and read only."""
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import sonar_audit


class SonarAuditTests(unittest.TestCase):
    def test_multiple_pages_and_fields(self):
        calls = []

        def fake(project, pr, page, **kwargs):
            calls.append((project, pr, page))
            return {"total": 3, "issues": [
                {"key": f"ISSUE-{n}", "message": "fix", "line": n}
                for n in ([1, 2] if page == 1 else [3])]}

        result = sonar_audit.inventory("Youchenjiang_Clickra", 178, fetch=fake)
        self.assertEqual(3, result["total"])
        self.assertEqual(3, len(result["issues"]))
        self.assertEqual([1, 2], [call[2] for call in calls])

    def test_incomplete_page_fails_closed(self):
        with self.assertRaisesRegex(RuntimeError, "incomplete"):
            sonar_audit.inventory("A_B", 1, fetch=lambda *a, **k: {"total": 2, "issues": []})

    def test_bad_response_fails_closed(self):
        with self.assertRaisesRegex(RuntimeError, "invalid"):
            sonar_audit.inventory("A_B", 1, fetch=lambda *a, **k: {"issues": []})

    def test_reject_unsafe_project_host_and_page(self):
        for kwargs in ({"project": "../bad"}, {"base_url": "https://evil.invalid"}, {"page": 0}):
            with self.subTest(kwargs=kwargs), self.assertRaises(ValueError):
                sonar_audit.fetch_page(kwargs.get("project", "A_B"), 1,
                                            kwargs.get("page", 1),
                                            base_url=kwargs.get("base_url", "https://sonarcloud.io"))

    def test_http_request_does_not_embed_token_in_url(self):
        class Response:
            def __enter__(self):
                return self
            def __exit__(self, *args):
                pass
            def read(self, *args):
                return b'{"total":0,"issues":[]}'

        with patch.object(sonar_audit, "urlopen", return_value=Response()) as opener:
            sonar_audit.fetch_page("A_B", 178, 1, token="secret")
        request = opener.call_args.args[0]
        self.assertNotIn("secret", request.full_url)
        self.assertEqual("Bearer secret", request.get_header("Authorization"))


if __name__ == "__main__":
    unittest.main()
