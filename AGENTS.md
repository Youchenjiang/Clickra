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
