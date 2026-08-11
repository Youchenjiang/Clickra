# Clickra Agent Authorization Gate

This file is the mandatory first-read rule for every agent working in Clickra.

## Before any tool call

Classify the intended action:

- **Read-only**: inspect files, Git status/history, CI status, or external state. Allowed by default.
- **Local edit**: modify files only when the user requested that change. Do not infer permission to commit or publish.
- **External mutation**: any branch operation, commit, push/force-push, tag change, PR operation, review-thread resolution, release operation, workflow dispatch/rerun/cancel, or Microsoft Store/Partner Center write. Requires explicit authorization for that exact operation in the current conversation.

Authorization is not transitive. “Force-push tag `vX.Y.Z.0`” authorizes only that tag push; it does not authorize creating a branch, opening or merging a PR, editing workflows, deleting a release, or submitting to the Store.

Before an external mutation, state the exact user authorization and the single operation about to be performed. If the next operation is not explicitly authorized, stop and ask. If an authorized operation fails and a new operation is needed, report the evidence and ask; never expand scope autonomously.

If the user objects to or revokes an action, stop all mutations immediately. Do not revert, delete, cancel, or force-push as “cleanup” without separate authorization.

The detailed project rules are in [`.agents/AGENTS.md`](.agents/AGENTS.md), [`.agent/guidelines.md`](.agent/guidelines.md), [`LOCAL_BUILD_NOTES.md`](LOCAL_BUILD_NOTES.md), and the [`docs/`](docs/) directory:
- [`ARCHITECTURE_AND_FRAMEWORK.md`](docs/ARCHITECTURE_AND_FRAMEWORK.md)
- [`WINDOWS_COMPATIBILITY_AND_MSIX_SANDBOX.md`](docs/WINDOWS_COMPATIBILITY_AND_MSIX_SANDBOX.md)
- [`TROUBLESHOOTING_AND_RESOLUTIONS.md`](docs/TROUBLESHOOTING_AND_RESOLUTIONS.md)
- [`CI_CD_DUAL_RELEASE_GUIDE.md`](docs/CI_CD_DUAL_RELEASE_GUIDE.md)
