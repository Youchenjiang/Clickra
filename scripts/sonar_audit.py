"""Read-only SonarQube Cloud issue inventory for one pull request."""
import json
import re
from http.client import HTTPSConnection, HTTPException
from urllib.parse import urlencode

PROJECT = re.compile(r"[A-Za-z0-9_.:-]+\Z")
DEFAULT_HOST = "https://sonarcloud.io"
HOSTS = {DEFAULT_HOST, "https://sonarqube.us"}
METRIC_KEYS = ("new_duplicated_lines_density", "new_coverage", "new_bugs",
               "new_vulnerabilities", "new_code_smells")


def _get_json(base_url, endpoint, params, token=None):
    if base_url not in HOSTS:
        raise ValueError("invalid SonarQube host")
    headers = {"Accept": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    connection = HTTPSConnection(base_url.removeprefix("https://"), timeout=20)
    try:
        connection.request("GET", f"{endpoint}?{urlencode(params)}", headers=headers)
        response = connection.getresponse()
        if response.status != 200:
            raise RuntimeError(f"SonarQube API HTTP {response.status} (check project access and token)")
        return json.load(response)
    except (OSError, HTTPException) as exc:
        raise RuntimeError("SonarQube API unavailable") from exc
    finally:
        connection.close()


def quality_metrics(project, pr, *, base_url=DEFAULT_HOST, token=None):
    """Read branch-specific measures and actual quality gate, not merely issue counts."""
    if not PROJECT.fullmatch(project) or not isinstance(pr, int) or pr < 1:
        raise ValueError("invalid SonarQube project or PR")
    measures = _get_json(base_url, "/api/measures/component",
                         {"component": project, "pullRequest": pr,
                          "metricKeys": ",".join(METRIC_KEYS)}, token)
    gate = _get_json(base_url, "/api/qualitygates/project_status",
                     {"projectKey": project, "pullRequest": pr}, token)
    values = measures.get("component", {}).get("measures")
    status = gate.get("projectStatus")
    if not isinstance(values, list) or not isinstance(status, dict) or status.get("status") not in ("OK", "ERROR", "WARN", "NONE") or not isinstance(status.get("conditions"), list):
        raise RuntimeError("invalid SonarQube quality response")
    metrics = {}
    for item in values:
        key = item.get("metric")
        if key in METRIC_KEYS:
            period = item.get("period") or {}
            metrics[key] = period.get("value", item.get("value"))
    for condition in status["conditions"]:
        key = condition.get("metricKey")
        if key in METRIC_KEYS and metrics.get(key) is None:
            metrics[key] = condition.get("actualValue")
    return {"metrics": {key: metrics.get(key) for key in METRIC_KEYS},
            "quality_gate": {"status": status["status"], "conditions": status["conditions"]},
            "url": f"{base_url}/component_measures?{urlencode({'id': project, 'pullRequest': pr})}"}


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
    connection = HTTPSConnection(base_url.removeprefix("https://"), timeout=20)
    try:
        # HTTPSConnection does not accept arbitrary URL schemes or follow redirects.
        connection.request("GET", f"/api/issues/search?{query}", headers=headers)
        response = connection.getresponse()
        if response.status != 200:
            raise RuntimeError(f"SonarQube API HTTP {response.status} (check project access and token)")
        return json.load(response)
    except (OSError, HTTPException) as exc:
        raise RuntimeError("SonarQube API unavailable") from exc
    finally:
        connection.close()


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
