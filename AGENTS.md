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
If the temporary PR body was lost, use `pr_tool.py body <PR> --output <new-file>`
to recover it before `push`, instead of invoking `gh pr view` directly.

## PR Lifecycle Red Line & Intent Interpretation (Non-negotiable)

- **Strict Intent Interpretation**: User instructions such as "修" (fix), "改" (change), or "處理" (handle) mean strictly editing local code and fixing the reported issues. Never interpret them as authorization to close, abandon, recreate, or start a new branch/PR.
- **Never autonomously close or abandon PRs**: An active PR must NEVER be closed (`gh pr close`), abandoned, or replaced with a new branch unless the user explicitly and verbatim commands it (e.g. "關閉 PR", "close PR", "放棄此 PR"). All review findings, CI failures, and static analysis issues must be resolved by adding atomic follow-up commits to the existing branch.
- **Zero-tolerance for unauthorized PR mutation commands**: Never invoke `gh pr close`, `gh pr edit`, `gh pr merge`, `git push --delete`, or `git push --force` autonomously. Direct PR mutations require explicit, separate, per-operation authorization.

## PR Review Safety & Closure Protocol (Non-negotiable)

- Never create, reply to, edit, repost, or delete GitHub PR review comments or conversation comments. The user explicitly prohibits autonomous replies. Do not use comments to announce a fix.
- **Mandatory Review Thread Closure Protocol**:
  - Whenever `pr_tool.py verify` reports `"ready": false` due to open review threads, the agent **MUST NEVER report the check as "passed" or "clean"**.
  - Review threads never resolve automatically; after pushing verified fixes and verifying CI is green, the agent **MUST immediately and proactively** list every open thread (whether active or outdated), cite the commit that fixed it, and explicitly request user authorization to resolve that thread ID via `pr_tool.py resolve`.
  - Never wait for the user to notice or remind about open threads.
- After verifying a finding is fixed, only resolve the existing review thread via GitHub GraphQL, and only with explicit authorization for that exact thread-resolution operation. If unresolved or uncertain, report locally and stop.
- Before proposing any PR, fresh-fetch `origin/main`, reconstruct the next scope on top of it, run the full push gate, and re-check the remote base. Do not reuse a stale stacked branch.

## Atomic Commit Discipline (Non-negotiable)

- **Domain Isolation**: Changes across disparate subsystems (e.g. `scripts/` tooling, `src/` core logic/compatibility, and `tests/` architecture guards) must NEVER be bundled into a single commit.
- **Strict Staging**: Never run bulk staging (`git add -A` or `git add .`). Always stage explicitly by target file or path.
- **Revert Independence**: If two changes address different review findings, static analysis rules, or layers that can be reverted independently, they MUST be committed separately.

## Branch Scope & Naming Discipline (Non-negotiable)

- **Single Scope Rule**: Every branch must address exactly ONE focused capability or problem. If a task requires addressing multiple topics, it MUST be split into multiple PRs.
- **No Compound Suffixes**: Never concatenate multiple unrelated topic words with hyphens (e.g. `architecture-integrity-shared-classifiers` is strictly forbidden).
- **Strict Word & Length Limits**:
  - The branch suffix must consist of **2 to 3 words** only.
  - The branch suffix length must **NOT exceed 25 characters** (e.g., `refactor/usecase-options` is 15 chars, `refactor/module-guards` is 13 chars).
- **Pre-Branch Agreement**: Before creating or pushing any branch, state the proposed name and character count clearly to the user.
