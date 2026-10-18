"""Conservative read-only final PR gate across GitHub and SonarQube."""

PASS = {"SUCCESS", "PASS", "PASSED"}
SKIP = {"SKIPPED", "SKIPPING", "NEUTRAL"}
PENDING = {"PENDING", "QUEUED", "IN_PROGRESS", "WAITING", "REQUESTED", "EXPECTED"}
REQUIRED = {"PR and commit policy", "PR governance script tests", "DeepSource: Python",
            "DeepSource: C#", "DeepSource: Secrets", "SonarCloud", "SonarCloud Code Analysis",
            "Deterministic tests (no fixtures)", "Public PDF translation regression",
            "Store publishing unit tests",
            "Build Clickra.Core", "Build Clickra.Fluent",
            "Publish CLI (NativeAOT)", "Publish Shell Extension (NativeAOT)",
            "Publish ClickraLauncher (NativeAOT)"}


def _classify_checks(checks, required):
    """Group provider statuses and require all mandatory checks."""
    groups = {"failed": [], "pending": [], "skipped": [], "passed": []}
    for item in checks:
        name = item.get("name") or item.get("context") or "unnamed"
        status = str(item.get("conclusion") or item.get("state") or item.get("status") or "PENDING").upper()
        if status in PASS:
            groups["passed"].append(name)
        elif status in SKIP:
            groups["skipped"].append(name)
        elif status in PENDING:
            groups["pending"].append(name)
        else:
            groups["failed"].append(name)
    present = set().union(*map(set, groups.values()))
    groups["pending"].extend(f"Missing required check: {name}" for name in sorted(required - present))
    return groups


def _pr_problems(pr, remote_sha):
    problems = []
    if pr.get("state") == "OPEN" and pr["headRefOid"] != remote_sha:
        problems.append("PR HEAD differs from remote SHA")
    for key, expected, message in (
            ("baseRefName", "main", "PR target is not main"),
            ("state", "OPEN", "PR is not open")):
        if pr.get(key) != expected:
            problems.append(message)
    for key, expected, message in (
            ("isDraft", True, "PR is still a draft"),
            ("mergeable", "CONFLICTING", "PR has merge conflicts"),
            ("reviewDecision", "CHANGES_REQUESTED", "PR has changes requested")):
        if pr.get(key) == expected:
            problems.append(message)
    return problems


def _required_checks(files):
    if files is not None and not isinstance(files, list):
        raise RuntimeError("invalid changed-file inventory")
    required = set(REQUIRED)
    if files and any(file.get("path", "").startswith("src/") or
                     file.get("path") == ".github/workflows/dependency-security-scan.yml"
                     for file in files):
        required.add("NuGet Dependency Vulnerability Scan")
    return required


def _finding_problems(report, groups, required):
    count = report["counts"]["unresolved"]
    sonar_count = report["sonar"]["total"]
    problems = []
    for value, label in ((count, "unresolved review threads"),
                         (sonar_count, "SonarQube issues")):
        if value:
            problems.append(f"{value} {label}")
    for names, label in ((groups["failed"], "failed checks"),
                         (sorted(required.intersection(groups["skipped"])), "required checks skipped"),
                         (groups["pending"], "pending checks")):
        if names:
            problems.append(f"{label}: {', '.join(names)}")
    return problems


def _check_details(checks, required):
    """Expose live check statuses and URLs without a separate native gh query."""
    details = []
    for item in checks:
        name = item.get("name") or item.get("context") or "unnamed"
        status = str(item.get("conclusion") or item.get("state") or item.get("status") or "PENDING").upper()
        details.append({"name": name, "status": status,
                        "required": name in required,
                        "url": item.get("detailsUrl") or item.get("targetUrl") or None})
    present = {item["name"] for item in details}
    details.extend({"name": name, "status": "MISSING", "required": True, "url": None}
                   for name in sorted(required - present))
    return details


def summarize(pr, report, *, remote_sha):
    """An absent or incomplete check is not a pass. Skip is reported separately."""
    head = pr.get("headRefOid")
    checks = pr.get("statusCheckRollup")
    if not isinstance(head, str) or len(head) != 40 or not isinstance(checks, list):
        raise RuntimeError("missing verified PR HEAD or CI rollup")
    required = _required_checks(pr.get("files"))
    groups = _classify_checks(checks, required)
    count = report["counts"]["unresolved"]
    sonar_count = report["sonar"]["total"]
    problems = _pr_problems(pr, remote_sha) + _finding_problems(report, groups, required)
    quality = report.get("sonar_quality")
    if quality is not None and quality["quality_gate"]["status"] != "OK":
        problems.append("SonarQube quality gate not passed")
    unresolved_details = [
        {"id": t["id"], "path": t["path"], "line": t["line"], "outdated": t["outdated"]}
        for t in report.get("threads", []) if not t.get("resolved")
    ]
    return {"ready": not problems, "state": pr.get("state"),
            "historical_checks_passed": (pr.get("state") == "MERGED" and
                                         not _finding_problems(report, groups, required)),
            "problems": problems, "passed": groups["passed"],
            "skipped": groups["skipped"], "head": head, "sonar_issues": sonar_count,
            "unresolved_threads": count, "unresolved_thread_details": unresolved_details,
            "checks": _check_details(checks, required),
            "sonar_quality": quality}
