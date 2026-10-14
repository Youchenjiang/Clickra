"""Read-only DeepSource pull-request issue inventory through the official API."""

import json
import urllib.error
import urllib.request

API_URL = "https://api.deepsource.com/graphql/"
QUERY = """query($name:String!, $login:String!, $number:Int!, $after:String) {
  repository(name:$name, login:$login, vcsProvider:GITHUB) {
    pullRequest(number:$number) {
      issues(first:100, after:$after) {
        pageInfo { hasNextPage endCursor }
        edges { node { path beginLine shortcode severity title isSuppressed } }
      }
    }
  }
}"""


def pr_issues(repo, number, token, opener=urllib.request.urlopen):
    """Fetch every issue page, failing closed on missing or malformed responses."""
    if not token:
        raise ValueError("DEEPSOURCE_TOKEN is required for issue details")
    login, name = repo.split("/", 1)
    issues, cursor, seen = [], None, set()
    while True:
        payload = json.dumps({"query": QUERY, "variables": {
            "name": name, "login": login, "number": number, "after": cursor
        }}).encode("utf-8")
        request = urllib.request.Request(API_URL, payload, {
            "Authorization": f"Bearer {token}", "Content-Type": "application/json",
            "Accept": "application/json"
        }, method="POST")
        with opener(request, timeout=15) as response:
            result = json.load(response)
        if result.get("errors"):
            raise RuntimeError("DeepSource API rejected the query")
        try:
            page = result["data"]["repository"]["pullRequest"]["issues"]
            issues.extend(edge["node"] for edge in page["edges"])
            info = page["pageInfo"]
            if not info["hasNextPage"]:
                return issues
            next_cursor = info["endCursor"]
            if not isinstance(next_cursor, str) or not next_cursor or next_cursor in seen:
                raise ValueError("DeepSource pagination did not advance")
            seen.add(next_cursor)
            cursor = next_cursor
        except (KeyError, TypeError) as exc:
            raise ValueError("Malformed DeepSource issue response") from exc
