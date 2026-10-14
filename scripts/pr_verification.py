"""Conservative read-only final PR gate across GitHub and SonarQube."""

PASS = {"SUCCESS", "PASS", "PASSED"}
SKIP = {"SKIPPED", "SKIPPING", "NEUTRAL"}
PENDING = {"PENDING", "QUEUED", "IN_PROGRESS", "WAITING", "REQUESTED", "EXPECTED"}
REQUIRED = {"PR and commit policy", "PR governance script tests", "DeepSource: Python",
            "DeepSource: C#", "DeepSource: Secrets", "SonarCloud", "SonarCloud Code Analysis",
            "Deterministic tests (no fixtures)", "Fixture regression tests",
            "NuGet Dependency Vulnerability Scan", "Store publishing unit tests",
            "Build Clickra.Core", "Build Clickra.Fluent", "Build Core / CLI / Shell / Setup",
            "Publish CLI (NativeAOT)", "Publish Shell Extension (NativeAOT)",
            "Publish ClickraLauncher (NativeAOT)"}


def _classify_checks(checks):
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
    groups["pending"].extend(f"Missing required check: {name}" for name in sorted(REQUIRED - present))
    return groups


def _pr_problems(pr, remote_sha):
    problems = []
    if pr["headRefOid"] != remote_sha:
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


def summarize(pr, report, *, remote_sha):
    """An absent or incomplete check is not a pass. Skip is reported separately."""
    head = pr.get("headRefOid")
    checks = pr.get("statusCheckRollup")
    if not isinstance(head, str) or len(head) != 40 or not isinstance(checks, list):
        raise RuntimeError("missing verified PR HEAD or CI rollup")
    groups = _classify_checks(checks)
    failed, pending, skipped, passed = (groups[key] for key in ("failed", "pending", "skipped", "passed"))
    count = report["counts"]["unresolved"]
    sonar_count = report["sonar"]["total"]
    problems = _pr_problems(pr, remote_sha)
    if count:
        problems.append(f"{count} unresolved review threads")
    if sonar_count:
        problems.append(f"{sonar_count} SonarQube issues")
    if failed:
        problems.append(f"failed checks: {', '.join(failed)}")
    required_skipped = REQUIRED.intersection(skipped)
    if required_skipped:
        problems.append(f"required checks skipped: {', '.join(sorted(required_skipped))}")
    if pending:
        problems.append(f"pending checks: {', '.join(pending)}")
    return {"ready": not problems, "problems": problems, "passed": passed,
            "skipped": skipped, "head": head, "sonar_issues": sonar_count,
            "unresolved_threads": count}
