"""Check whether a specific Clickra release is the current published Store submission.

This helper is read-only. It authenticates with the Microsoft Store submission API,
reads the application's last published submission, and reports whether that
submission contains the exact four-part version requested by the release tag.
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import time
import urllib.error
import urllib.parse
import urllib.request


STORE_RESOURCE = "https://manage.devcenter.microsoft.com"
DEFAULT_IDENTITY = "g1014308.Clickra"


def required_env(env: dict[str, str], name: str) -> str:
    value = env.get(name)
    if not value:
        raise RuntimeError(f"Missing required environment variable: {name}")
    return value


def parse_release_version(tag: str) -> str:
    if not tag.startswith("v") or len(tag) == 1:
        raise ValueError(f"Expected release tag in v<version> form, got '{tag}'.")
    version = tag[1:]
    parts = version.split(".")
    if len(parts) != 4 or any(not part.isdigit() for part in parts):
        raise ValueError(f"Expected four-part numeric release version, got '{version}'.")
    return version


def open_https(request: urllib.request.Request, timeout: int):
    if not request.full_url.lower().startswith("https://"):
        raise ValueError(f"Only HTTPS URLs are allowed: {request.full_url}")
    return urllib.request.urlopen(request, timeout=timeout)  # skipcq: BAN-B310


def request_json(request: urllib.request.Request, *, attempts: int = 3) -> dict[str, object]:
    delay = 5
    for attempt in range(1, attempts + 1):
        try:
            with open_https(request, timeout=30) as response:
                payload = response.read()
                return json.loads(payload) if payload else {}
        except urllib.error.HTTPError as error:
            retryable = error.code == 429 or error.code >= 500
            if not retryable or attempt == attempts:
                raise
        except Exception:
            if attempt == attempts:
                raise
        time.sleep(delay)
        delay *= 2
    raise RuntimeError("Microsoft Store API request exhausted retries.")


def get_token(tenant_id: str, client_id: str, client_secret: str) -> str:
    token_url = f"https://login.microsoftonline.com/{tenant_id}/oauth2/token"
    body = urllib.parse.urlencode(
        {
            "grant_type": "client_credentials",
            "client_id": client_id,
            "client_secret": client_secret,
            "resource": STORE_RESOURCE,
        }
    ).encode()
    request = urllib.request.Request(token_url, data=body, method="POST")
    payload = request_json(request)
    token = payload.get("access_token")
    if not isinstance(token, str) or not token:
        raise RuntimeError("Microsoft identity endpoint returned no access token.")
    return token


def api_get(url: str, token: str) -> dict[str, object]:
    request = urllib.request.Request(
        url,
        headers={"Authorization": f"Bearer {token}"},
        method="GET",
    )
    return request_json(request)


def published_submission_matches(
    application: dict[str, object],
    submission: dict[str, object],
    *,
    expected_identity: str,
    expected_version: str,
) -> tuple[bool, str]:
    identity = application.get("packageIdentityName")
    if identity != expected_identity:
        raise RuntimeError(
            f"Store application identity '{identity}' does not match '{expected_identity}'."
        )

    status = submission.get("status") or submission.get("Status")
    if status != "Published":
        return False, f"last published submission status is '{status}'"

    packages = submission.get("applicationPackages") or submission.get("ApplicationPackages") or []
    if not isinstance(packages, list):
        raise RuntimeError("Store submission applicationPackages is not a list.")

    versions = {
        str(package.get("version") or package.get("Version"))
        for package in packages
        if isinstance(package, dict)
        and (package.get("fileStatus") or package.get("FileStatus")) != "PendingDelete"
        and (package.get("version") or package.get("Version"))
    }
    if versions != {expected_version}:
        displayed = ", ".join(sorted(versions)) if versions else "none"
        return False, f"live published package version(s): {displayed}"
    return True, f"published package version {expected_version}"


def check_release_published(
    *,
    tag: str,
    tenant_id: str,
    client_id: str,
    client_secret: str,
    product_id: str,
    expected_identity: str = DEFAULT_IDENTITY,
) -> tuple[bool, str, str | None]:
    version = parse_release_version(tag)
    token = get_token(tenant_id, client_id, client_secret)
    app_url = f"{STORE_RESOURCE}/v1.0/my/applications/{product_id}"
    application = api_get(app_url, token)
    published = application.get("lastPublishedApplicationSubmission")
    if not isinstance(published, dict) or not published.get("id"):
        return False, "application has no last published submission", None

    submission_id = str(published["id"])
    submission = api_get(f"{app_url}/submissions/{submission_id}", token)
    matches, reason = published_submission_matches(
        application,
        submission,
        expected_identity=expected_identity,
        expected_version=version,
    )
    return matches, reason, submission_id


def write_github_output(path: str | None, published: bool) -> None:
    if not path:
        return
    output_path = pathlib.Path(path)
    with output_path.open("a", encoding="utf-8", newline="\n") as stream:
        stream.write(f"published={'true' if published else 'false'}\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--github-output", default=os.environ.get("GITHUB_OUTPUT"))
    args = parser.parse_args()

    env = os.environ
    published, reason, submission_id = check_release_published(
        tag=args.tag,
        tenant_id=required_env(env, "STORE_TENANT_ID"),
        client_id=required_env(env, "STORE_CLIENT_ID"),
        client_secret=required_env(env, "STORE_CLIENT_SECRET"),
        product_id=required_env(env, "STORE_PRODUCT_ID"),
    )
    write_github_output(args.github_output, published)
    print(
        json.dumps(
            {
                "tag": args.tag,
                "published": published,
                "submissionId": submission_id,
                "reason": reason,
            },
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
