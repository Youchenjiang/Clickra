"""Read-only, complete PR comment inventory across GitHub's comment surfaces."""
import json
import re


REPO = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z")


def paged(repo, number, endpoint, run):
    """Fetch all REST pages; fail closed on API or pagination anomalies."""
    if not REPO.fullmatch(repo) or not isinstance(number, int) or number < 1:
        raise ValueError("invalid repository or pull request number")
    if endpoint not in {"issues/comments", "pulls/comments", "pulls/reviews"}:
        raise ValueError("unsupported comment resource")
    resource, kind = endpoint.split("/")
    results = []
    page = 1
    while True:
        path = f"repos/{repo}/{resource}/{number}/{kind}?per_page=100&page={page}"
        items = json.loads(run(["gh", "api", path]))
        if not isinstance(items, list):
            raise RuntimeError("unexpected GitHub comment response")
        results.extend(items)
        if len(items) < 100:
            return results
        page += 1
        if page > 1000:
            raise RuntimeError("GitHub comments exceed pagination safety bound")


def inventory(repo, number, head, threads, run):
    """Use thread state only for code-review discussions, not unrelated comments."""
    general = paged(repo, number, "issues/comments", run)
    inline = paged(repo, number, "pulls/comments", run)
    reviews = paged(repo, number, "pulls/reviews", run)
    result = {"pr": number, "head": head, "threads": [], "general_comments": [],
              "inline_comments": [], "reviews": []}
    thread_url_map = {}
    for thread in threads:
        copied = {"id": thread.get("id"), "path": thread.get("path"),
                  "line": thread.get("line"), "resolved": bool(thread.get("isResolved")),
                  "outdated": bool(thread.get("isOutdated"))}
        result["threads"].append(copied)
        for comment in thread.get("comments", {}).get("nodes", []):
            url = comment.get("url")
            if url:
                thread_url_map[url] = copied["id"]
    # Replies reference the root review-comment ID; propagate only verified
    # associations. Unmatched comments stay unscoped rather than guessed.
    root_ids = {c.get("id"): thread_url_map[c.get("html_url")]
                for c in inline if c.get("html_url") in thread_url_map}
    for comment in inline:
        item = _comment(comment, head)
        item["thread_id"] = (thread_url_map.get(comment.get("html_url")) or
                             root_ids.get(comment.get("in_reply_to_id")))
        result["inline_comments"].append(item)
    result["general_comments"] = [_comment(c, head, sha_key=None) for c in general]
    result["reviews"] = [_comment(c, head) | {"state": c.get("state")} for c in reviews]
    result["counts"] = {"threads": len(threads), "unresolved": sum(not t["resolved"] for t in result["threads"]),
                        "outdated_unresolved": sum(not t["resolved"] and t["outdated"] for t in result["threads"]),
                        "inline": len(inline), "general": len(general), "reviews": len(reviews)}
    return result


def _comment(item, head, sha_key="commit_id"):
    sha = item.get(sha_key) if sha_key else None
    if not sha:
        relation = "unscoped"
    elif sha == head:
        relation = "current"
    else:
        relation = "historical"
    return {"id": item.get("id"), "author": (item.get("user") or {}).get("login"),
            "body": item.get("body") or "", "url": item.get("html_url"),
            "created_at": item.get("created_at") or item.get("submitted_at"),
            "path": item.get("path"), "commit": sha,
            "head_relation": relation}
