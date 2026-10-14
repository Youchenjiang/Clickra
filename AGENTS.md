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

- Use `python scripts/pr_tool.py` as the default PR operations entry point: `prepare` for fast preflight, `push` for separately authorized branch publication, `create` for separately authorized PR creation, `verify` for combined CI/reviews/SonarQube acceptance, and `diagnose` for failure details. `ready` remains an optional **full** local gate when no push follows; do not run it immediately before an approved push because `push` always executes the full versioned hook. Direct `gh`/GraphQL or manual push is an exceptional diagnostic/fallback only when the wrapper cannot perform the operation; explain and record that exception. See `docs/development/pr_tool.md`. These are cooperative rules, not credential isolation or OS-enforced command interception.
For PR acceptance, `pr_tool.py verify` now includes each check's live status,
required flag and URL in `checks`. Do not routinely run `gh pr checks` after it.
Use `pr_tool.py diagnose` for failures. Before exceptional native `gh`, identify
the specific missing diagnostic capability and record the reason; being
read-only does not automatically justify bypassing the high-level tool.

## PR Review Safety (Non-negotiable)

- Never create, reply to, edit, repost, or delete GitHub PR review comments or conversation comments. The user explicitly prohibits autonomous replies. Do not use comments to announce a fix.
- After verifying a finding is fixed, only resolve the existing review thread via GitHub GraphQL, and only with explicit authorization for that exact thread-resolution operation. If unresolved or uncertain, report locally and stop.
- Before proposing any PR, fresh-fetch `origin/main`, reconstruct the next scope on top of it, run the full push gate, and re-check the remote base. Do not reuse a stale stacked branch.
