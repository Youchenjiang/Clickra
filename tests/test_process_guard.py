"""Guarded process execution must fail before launching unsafe commands."""
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import process_guard


class ProcessGuardTests(unittest.TestCase):
    def test_rejects_mutations_unknown_tools_and_control_bytes(self):
        for args in (["gh", "pr", "merge", "178"], ["git", "push", "origin"],
                     ["git", "remote", "add", "evil", "https://evil.invalid"],
                     ["git", "branch", "-D", "main"],
                     ["git", "reset", "--hard", "HEAD"],
                     ["git", "log", "--exec=evil"],
                     ["python", "-c", "print(1)"], ["gh", "api", "repos/a/b/pulls/1"],
                     ["gh", "pr", "view", "178\n--web"], ["git", "log", "bad\x00argument"]):
            with self.subTest(args=args), patch.object(process_guard.subprocess, "run") as call:
                with self.assertRaises(ValueError):
                    process_guard.invoke(args)
                call.assert_not_called()

    def test_git_allowlist_accepts_only_exact_read_operations(self):
        fake = Path(__file__).resolve()
        with patch.object(process_guard.shutil, "which", return_value=str(fake)), \
             patch.object(process_guard.subprocess, "run") as run:
            for args in process_guard.GIT_READ_OPERATIONS:
                with self.subTest(args=args):
                    process_guard.invoke(["git", *args])
            self.assertEqual(len(process_guard.GIT_READ_OPERATIONS), run.call_count)

    def test_allowed_gh_uses_absolute_binary_and_no_shell(self):
        fake = Path(__file__).resolve()
        with patch.object(process_guard.shutil, "which", return_value=str(fake)), \
             patch.object(process_guard.subprocess, "run") as run:
            process_guard.invoke(["gh", "pr", "checks", "178"])
            self.assertEqual(str(fake), run.call_args.args[0][0])
            self.assertIs(False, run.call_args.kwargs["shell"])
            self.assertIs(False, run.call_args.kwargs["check"])


if __name__ == "__main__":
    unittest.main()
