"""Conservative, read-only PR report fetching for trusted provider URLs."""

from html.parser import HTMLParser
import re
from urllib.parse import urlsplit
from urllib.request import Request, urlopen

ALLOWED_HOSTS = frozenset({"app.deepsource.com", "sonarcloud.io", "sourcery.ai"})
MAX_REPORT_BYTES = 1024 * 1024


class _PageText(HTMLParser):
    def __init__(self):
        super().__init__()
        self.parts = []
        self.skip = 0

    def handle_starttag(self, tag, attrs):
        if tag in ("script", "style"):
            self.skip += 1

    def handle_endtag(self, tag):
        if tag in ("script", "style") and self.skip:
            self.skip -= 1

    def handle_data(self, data):
        if not self.skip and data.strip():
            self.parts.append(data.strip())


def fetch_report(url, opener=urlopen):
    """Only fetch HTTPS from known providers; reject redirects to other hosts."""
    parts = urlsplit(url)
    if parts.scheme != "https" or parts.hostname not in ALLOWED_HOSTS or parts.username or parts.password:
        raise ValueError("unsupported report URL")
    request = Request(url, headers={"User-Agent": "Clickra-PR-Diagnostics/1.0", "Accept": "text/html"})
    with opener(request, timeout=15) as response:
        actual = urlsplit(response.geturl())
        if actual.scheme != "https" or actual.hostname not in ALLOWED_HOSTS:
            raise ValueError("report redirected outside trusted providers")
        content_type = response.headers.get("Content-Type", "")
        if "text/html" not in content_type.lower():
            raise ValueError("report is not HTML")
        raw = response.read(MAX_REPORT_BYTES + 1)
    if len(raw) > MAX_REPORT_BYTES:
        raise ValueError("report exceeds read limit")
    html = raw.decode("utf-8", errors="replace")
    parser = _PageText()
    parser.feed(html)
    text = " ".join(parser.parts)
    # DeepSource embeds its issue inventory in Nuxt SSR even when it is not
    # included in human-visible HTML or in GitHub review comments.
    issue_count = len(re.findall(r"allIssuesNodes:\[\{", html)) if parts.hostname == "app.deepsource.com" else 0
    return {"text": re.sub(r"\s+", " ", text)[:6000], "has_ssr_issues": issue_count > 0,
            "bytes": len(raw)}
