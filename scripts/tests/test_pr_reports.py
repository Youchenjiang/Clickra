import io
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from pr_reports import fetch_report


class Response(io.BytesIO):
    headers = {"Content-Type": "text/html; charset=utf-8"}

    def __init__(self, data, url):
        super().__init__(data)
        self.url = url

    def geturl(self):
        return self.url


class ReportTests(unittest.TestCase):
    def test_rejects_untrusted_url_without_network(self):
        for url in ("http://app.deepsource.com/", "https://evil.example.com/", "file:///etc/passwd"):
            with self.assertRaises(ValueError):
                fetch_report(url, lambda *_args, **_kwargs: self.fail("should not fetch"))

    def test_reads_page_text_and_embedded_issue_marker(self):
        url = "https://app.deepsource.com/gh/owner/repo/run/id/csharp/"
        html = b"<html><body><h1>Analysis failed</h1><script>allIssuesNodes:[{foo:bar}]</script></body></html>"
        report = fetch_report(url, lambda *_args, **_kwargs: Response(html, url))
        self.assertTrue(report["has_ssr_issues"])
        self.assertIn("Analysis failed", report["text"])
        self.assertNotIn("allIssuesNodes", report["text"])

    def test_rejects_untrusted_redirect(self):
        with self.assertRaisesRegex(ValueError, "redirected"):
            fetch_report("https://sonarcloud.io/project", lambda *_args, **_kwargs:
                         Response(b"<html></html>", "https://untrusted.example/"))


if __name__ == "__main__":
    unittest.main()
