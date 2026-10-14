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
            project = kwargs.get("project", "A_B")
            page = kwargs.get("page", 1)
            host = kwargs.get("base_url", sonar_audit.DEFAULT_HOST)
            with self.subTest(kwargs=kwargs), self.assertRaises(ValueError):
                sonar_audit.fetch_page(project, 1, page, base_url=host)

    def test_http_request_does_not_embed_token_in_url(self):
        class Response:
            status = 200
            def read(self, *args):
                return b'{"total":0,"issues":[]}'

        with patch.object(sonar_audit, "HTTPSConnection") as connection:
            connection.return_value.getresponse.return_value = Response()
            sonar_audit.fetch_page("A_B", 178, 1, token="secret")
        self.assertEqual("sonarcloud.io", connection.call_args.args[0])
        method, path = connection.return_value.request.call_args.args
        self.assertEqual("GET", method)
        self.assertNotIn("secret", path)
        self.assertEqual("Bearer secret", connection.return_value.request.call_args.kwargs["headers"]["Authorization"])
        connection.return_value.close.assert_called_once()


if __name__ == "__main__":
    unittest.main()
