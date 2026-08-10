#!/bin/sh
# Installs Clickra's versioned commit-msg hook for this clone.
# Git loads hooks from scripts/hooks instead of the (untracked) .git/hooks dir.
set -e

git config core.hooksPath scripts/hooks
echo "Installed: git hooks now load from scripts/hooks (core.hooksPath)."
echo "Verify with: git config core.hooksPath"
