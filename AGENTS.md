# Clickra Agent Rules

This file is the entry point for all AI agents working in Clickra.

The complete, authoritative rules are in [.agent/guidelines.md](.agent/guidelines.md).

## Quick Reference

- **Read-only** inspection is allowed by default.
- **Local edits** only when the user requested that change.
- **External mutations** (branch, commit, push, PR, release, Store) require explicit authorization per operation.
- Authorization is not transitive. Each operation needs its own approval.
- If the user objects, stop immediately. Do not "clean up" without separate authorization.