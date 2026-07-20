# Clickra Workspace Rules & Store Publishing Guidelines

This document contains critical project-specific rules and guidelines for building, packaging, and publishing Clickra to the Microsoft Store. These rules must be strictly followed by all AI agents.

## 0. Mandatory Authorization Gate for External Actions

This is a hard safety boundary for every agent and every conversation.

### Default permissions

* Read-only inspection is allowed by default.
* Local edits are allowed only when the user requested the change. Local edits do not authorize commits, branch operations, or remote operations.
* Any operation that changes GitHub, Git history, a remote branch/tag, a workflow run, a release, or Microsoft Partner Center requires explicit authorization for that exact operation in the current conversation.

### Actions that require separate explicit authorization

Treat each item below as a separate operation. Do not infer permission for one item from permission for another:

* create, switch to, rename, or delete a branch;
* commit, push, force-push, create, move, or delete a tag;
* create, edit, comment on, resolve threads in, merge, close, or delete a pull request;
* create, edit, delete, rerun, dispatch, or cancel a GitHub Actions workflow or release;
* submit, commit, delete, or otherwise mutate Microsoft Store / Partner Center state.

For example, “force-push tag `v3.6.3.0`” authorizes only that exact tag operation. It does not authorize opening a branch, opening or merging a PR, changing the workflow, deleting a release, or submitting to the Store.

### Preflight and escalation rule

Before any external action, the agent must:

1. Quote or summarize the user authorization and list the exact operation(s) it covers.
2. Check the current target (repository, branch, tag, PR, workflow, or release).
3. Stop and ask if the next required operation is not explicitly covered.
4. Execute one authorized mutation at a time and report its result before considering another mutation.

If an authorized operation fails and fixing it requires a new branch, code change, PR, merge, workflow change, release change, or Store submission, stop and report the evidence. Do not expand the authorization to unblock the task.

If the user objects to or revokes an action, stop immediately. Do not “clean up” by reverting, deleting, cancelling, or force-pushing anything unless the user separately authorizes that exact cleanup.

## 1. Microsoft Store Ingestion API Lifecycle & State Definitions

Microsoft Ingestion API operations are heavily asynchronous. Do NOT trust transient HTTP response success codes or simple status string outputs without validating the actual states inside Microsoft Partner Center.

### A. The "CommitStarted" Pitfall (Crucial)
* **What it means**: When the API receives a `POST .../commit` request, it immediately responds with `202 Accepted` and sets the submission status to `CommitStarted`. **This does NOT mean the app is in certification.**
* **What happens behind the scenes**: Microsoft's backend queues the submission to copy package files, distribute localized CDN screenshots, and run static binary scans.
* **The Danger**: If this background processing fails (e.g., due to missing assets in some locales or file copy deadlock), the submission will silently stay in `CommitStarted` indefinitely or revert back to `PendingCommit` (Draft) in the partner portal.
* **True Certification State**: The submission is only officially submitted for human review when its status changes to **`Certification`** (which corresponds to "認證中" / "Undergoing certification" in the Partner Center UI).

### B. Prevention Checklist before Committing
Before sending a commit request, the submission payload must meet these hard constraints:
1. **Case-Insensitive Keywords Limit**: Every listing object inside `listings` must have **at most 7 keywords**. Enforce this recursively across the entire JSON payload (cleaning both `Keywords` and `keywords` casing variants, especially when copying/cloning listings via deepcopy).
2. **Screenshots Completeness**: In Microsoft Store, locales like `en-us` and `en` are handled as separate listing entries. Every active listing container **must contain at least 1 screenshot** (preferably 2+). A listing with 0 screenshots will cause the submission to remain incomplete (顯示 "未完成" / "Incomplete" in Web UI) and fail the validation check.
3. **Traditional Chinese Mapping**: Localized Traditional Chinese listings (`zh-tw`) must have screenshots mapped properly. Ensure `tw1.png`/`tw2.png` are linked to the `zh-tw` listing container.

### C. Handling API Gateway Timeouts (HTTP 504 / 502)
* The Microsoft Dev Center Ingestion API is prone to gateway timeouts (HTTP 502/504) during large metadata updates (`PUT`) or status checks (`GET`).
* **Handling Strategy**: Always use exponential backoff retry algorithms (e.g., 15s -> 30s -> 60s -> 120s) and set connection timeouts to at least **180 seconds** for metadata operations to give the Azure ingestion pipeline sufficient processing time.
* **Verification**: If a `PUT` or `POST` request times out, always query the app status again before retrying the action. The write operation may have already succeeded on the backend database.

## 2. Code Quality & Commits Guidelines

### A. Atomic Commits Rule
* Never mix code changes (e.g., script logic updates, error handling improvements) with static asset changes (e.g., migrating screenshots, updating localization markdown docs) in a single commit.
* Group changes logically and perform sequential commits for atomic history tracking.

### B. Commit Message Formatting
All commit messages must follow the structured format (scope is optional):
```
<type>(<scope>): <short description>
or
<type>: <short description>

1. <Numbered detail 1 in English>
2. <Numbered detail 2 in English>
```

The authoritative scope/type allowlist for pull-request validation is
maintained in `.github/workflows/policy.yml`; keep local examples aligned with
that workflow. Use a meaningful scope such as `agent` or `shell` when one
exists, and omit scope for cross-cutting documentation changes.
Example:
```
fix(store): resolve keywords limit validation bug

1. Strip both 'Keywords' and 'keywords' properties from listing objects.
2. Limit the array size to 7 items recursively.
```

### C. Pull Request Description Formatting
Pull request descriptions are separate from commit bodies. The canonical template
is `.github/pull_request_template.md`; use it and remove its instructions before
submitting. Keep the description in English and choose the structure by changed
file count:

* Fewer than 10 files: `## Summary` (1-2 sentences) plus a numbered list.
* 10-50 files: `## Summary`, `## Key Changes`, and `## Verification`.
* More than 50 files: `## Overview`, `## Key Changes` with numbered sections,
  and `## Verification`.

`Summary` or `Overview` explains what changed and why; it is not a list of file
names. `Key Changes` groups technical details by area. `Verification` uses a
`[x]`/`[ ]` checklist and must state what was and was not tested.
