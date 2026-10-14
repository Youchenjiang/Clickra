"""Read-only SonarQube Cloud issue inventory for one pull request."""
import json
import re
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

PROJECT = re.compile(r"[A-Za-z0-9_.:-]+\Z")
DEFAULT_HOST = "https://sonarcloud.io"
HOSTS = {DEFAULT_HOST, "https://sonarqube.us"}


def fetch_page(project, pr, page, *, base_url=DEFAULT_HOST, token=None):
    if not PROJECT.fullmatch(project) or not isinstance(pr, int) or pr < 1:
        raise ValueError("invalid SonarQube project or PR")
    if base_url not in HOSTS or not isinstance(page, int) or not 1 <= page <= 100:
        raise ValueError("invalid SonarQube host or page")
    query = urlencode({"componentKeys": project, "pullRequest": pr,
                       "issueStatuses": "OPEN,CONFIRMED", "p": page, "ps": 100})
    headers = {"Accept": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = Request(f"{base_url}/api/issues/search?{query}", headers=headers)
    try:
        # The URL is built from a strict HTTPS host allowlist and escaped parameters.
        with urlopen(request, timeout=20) as response:
            return json.load(response)
    except HTTPError as exc:
        raise RuntimeError(f"SonarQube API HTTP {exc.code} (check project access and token)") from exc
    except (URLError, TimeoutError) as exc:
        raise RuntimeError("SonarQube API unavailable") from exc


def inventory(project, pr, *, fetch=fetch_page, base_url=DEFAULT_HOST, token=None):
    """Fetch every page, fail rather than silently return a partial inventory."""
    results = []
    page = 1
    while True:
        data = fetch(project, pr, page, base_url=base_url, token=token)
        items, total = data.get("issues"), data.get("total")
        if not isinstance(items, list) or not isinstance(total, int) or total < 0:
            raise RuntimeError("invalid SonarQube API response")
        results.extend(items)
        if len(results) >= total:
            break
        if not items or page >= 100:
            raise RuntimeError("incomplete SonarQube issue pagination")
        page += 1
    return {"project": project, "pr": pr, "total": total,
            "issues": [{"key": item.get("key"), "message": item.get("message"),
                        "component": item.get("component"), "line": item.get("line"),
                        "rule": item.get("rule"), "severity": item.get("severity"),
                        "impacts": item.get("impacts"), "status": item.get("status"),
                        "type": item.get("type")} for item in results],
            "url": f"{base_url}/project/issues?{urlencode({'id': project, 'pullRequest': pr, 'issueStatuses': 'OPEN,CONFIRMED'})}"}
