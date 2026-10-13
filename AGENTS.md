# Clickra Agent Rules

This file is the entry point for all AI agents working in Clickra.

The complete, authoritative rules are in [.agent/guidelines.md](.agent/guidelines.md).

## Mandatory Authorization Gate

- Before every tool call, check whether it is read-only, a requested local edit, or an external mutation authorized for this specific operation.
- Read-only inspection is allowed by default; local edits require a user request.
- External mutations require explicit authorization per operation; authorization is not transitive.
- A capability requirement must never be converted into a vendor/provider decision. Introducing or binding the repo to a named external provider requires the user's explicit provider choice first.
- Before each external write, state the exact authorized operation and the single operation to execute.
- If the user objects or withdraws authorization, stop immediately; do not perform cleanup.

The complete, authoritative rules are in [.agent/guidelines.md](.agent/guidelines.md).

- Prefer `python scripts/pr_tool.py status [PR]` and `review [PR]` for consolidated read-only GitHub inspection, and `ready --body-file PATH` for the full local gate. See `docs/development/pr_tool.md`. The lower-level `gh_guard.py` remains available; mutations are denied except explicitly authorized per-thread Resolve. Comment operations are always denied. These interfaces are cooperative guidance, not OS-enforced interception of direct `gh` or API calls.
## PR Review Safety (Non-negotiable)

- Never create, reply to, edit, repost, or delete GitHub PR review comments or conversation comments. The user explicitly prohibits autonomous replies. Do not use comments to announce a fix.
- After verifying a finding is fixed, only resolve the existing review thread via GitHub GraphQL, and only with explicit authorization for that exact thread-resolution operation. If unresolved or uncertain, report locally and stop.
- Before proposing any PR, fresh-fetch `origin/main`, reconstruct the next scope on top of it, run the full push gate, and re-check the remote base. Do not reuse a stale stacked branch.
