import contextlib
import io
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
import shutil
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import git_tool


class GitToolTests(unittest.TestCase):
    @staticmethod
    def state(action="sync"):
        return {"action": action, "branch": "feature/work", "head": "a" * 40,
                "main": "b" * 40, "remote_main": "c" * 40}

    def test_tokens_bind_branch_head_and_remote(self):
        state = self.state()
        token = git_tool.authorization(state)
        self.assertEqual(token, git_tool.authorization(dict(state)))
        for field in ("branch", "head", "main", "remote_main"):
            changed = dict(state)
            changed[field] += "z"
            self.assertNotEqual(token, git_tool.authorization(changed))

    @patch.object(git_tool, "snapshot")
    @patch.object(git_tool, "git")
    def test_preview_never_mutates(self, command, snapshot):
        snapshot.return_value = self.state()
        with contextlib.redirect_stdout(io.StringIO()) as captured:
            git_tool.run("sync")
        self.assertIn("preview", captured.getvalue())
        command.assert_not_called()

    @patch.object(git_tool, "snapshot")
    @patch.object(git_tool, "git")
    def test_execute_rejects_missing_or_stale_approval(self, command, snapshot):
        snapshot.return_value = self.state()
        with self.assertRaisesRegex(ValueError, "authorization"):
            git_tool.run("sync", execute=True)
        with self.assertRaisesRegex(ValueError, "authorization"):
            git_tool.run("sync", execute=True, authorize="sync:feature/work:invalid")
        command.assert_not_called()

    @patch.object(git_tool, "snapshot")
    @patch.object(git_tool, "git")
    def test_sync_uses_compare_and_swap_not_reset(self, command, snapshot):
        state = self.state()
        snapshot.return_value = state
        with contextlib.redirect_stdout(io.StringIO()):
            git_tool.run("sync", True, git_tool.authorization(state))
        command.assert_called_once_with("update-ref", "refs/heads/main", "c" * 40, "b" * 40)

    @patch.object(git_tool, "snapshot")
    @patch.object(git_tool, "git")
    def test_rebase_only_current_worktree_without_autostash(self, command, snapshot):
        state = self.state("rebase")
        snapshot.return_value = state
        command.return_value.returncode = 0
        with contextlib.redirect_stdout(io.StringIO()):
            git_tool.run("rebase", True, git_tool.authorization(state))
        command.assert_called_once_with("rebase", "--no-autostash", "origin/main", check=False)

    @patch.object(git_tool, "snapshot")
    @patch.object(git_tool, "git")
    def test_race_detected_before_execution(self, command, snapshot):
        state = self.state()
        changed = dict(state, remote_main="d" * 40)
        snapshot.side_effect = (state, changed)
        token = git_tool.authorization(state)
        with self.assertRaisesRegex(ValueError, "changed"):
            git_tool.run("sync", True, token)
        command.assert_not_called()

    @patch.object(git_tool, "lifecycle_snapshot")
    @patch.object(git_tool, "git")
    def test_lifecycle_preview_does_not_mutate(self, command, snapshot):
        snapshot.return_value = dict(self.state(), action="next", target_branch="feature/new")
        with contextlib.redirect_stdout(io.StringIO()) as captured:
            git_tool.lifecycle_run("next", branch="feature/new")
        self.assertIn("authorize", captured.getvalue())
        command.assert_not_called()

    @patch.object(git_tool, "lifecycle_snapshot")
    @patch.object(git_tool, "git")
    def test_lifecycle_requires_matching_approval(self, command, snapshot):
        snapshot.return_value = dict(self.state(), action="cleanup", target_branch="feature/old")
        with self.assertRaisesRegex(ValueError, "authorization"):
            git_tool.lifecycle_run("cleanup", pr=185, execute=True, authorize="wrong")
        command.assert_not_called()

    @patch.object(git_tool, "lifecycle_snapshot")
    @patch.object(git_tool, "git")
    def test_cleanup_removes_only_verified_worktree(self, command, snapshot):
        state = dict(self.state(), action="cleanup", target_branch="feature/old",
                     target_path="C:/safe/Clickra-pr-old", merge_mode="ancestry")
        snapshot.return_value = state
        with contextlib.redirect_stdout(io.StringIO()):
            git_tool.lifecycle_run("cleanup", pr=185, execute=True,
                                   authorize=git_tool.authorization(state))
        self.assertEqual([("update-ref", git_tool.MAIN_BRANCH_REF),
                          ("worktree", "remove"), ("branch", "-d"), ("worktree", "prune")],
                         [call.args[:2] for call in command.call_args_list])

    @patch.object(git_tool, "lifecycle_snapshot")
    @patch.object(git_tool, "git")
    def test_next_creates_from_remote_main(self, command, snapshot):
        state = dict(self.state(), action="next", target_branch="feature/new",
                     target_path="C:/safe/Clickra-pr-new", main="c" * 40)
        snapshot.return_value = state
        with contextlib.redirect_stdout(io.StringIO()):
            git_tool.lifecycle_run("next", branch="feature/new", execute=True,
                                   authorize=git_tool.authorization(state))
        command.assert_called_once_with("worktree", "add", "-b", "feature/new",
                                        "C:/safe/Clickra-pr-new", "c" * 40)

    @patch.object(git_tool, "lifecycle_snapshot")
    @patch.object(git_tool, "git")
    def test_lifecycle_rejects_state_drift(self, command, snapshot):
        state = dict(self.state(), action="next", target_branch="feature/new")
        snapshot.side_effect = (state, dict(state, remote="changed"))
        token = git_tool.authorization(state)
        with self.assertRaisesRegex(ValueError, "changed"):
            git_tool.lifecycle_run("next", branch="feature/new", execute=True,
                                   authorize=token)
        command.assert_not_called()

    @patch.object(git_tool, "ancestor", return_value=True)
    def test_merge_equivalence_direct_ancestry(self, ancestry):
        self.assertEqual("ancestry", git_tool.merge_equivalence("a", "b", "c"))
        ancestry.assert_called_once_with("a", "c")

    @patch.object(git_tool, "output")
    @patch.object(git_tool, "ancestor", return_value=False)
    def test_merge_equivalence_squash(self, ancestry, read):
        read.side_effect = ["merge parent", "base", "same patch", "same patch"]
        self.assertEqual("squash", git_tool.merge_equivalence("head", "merge", "main"))

    @patch.object(git_tool, "output")
    @patch.object(git_tool, "ancestor", return_value=False)
    def test_merge_equivalence_rebase_and_unmatched_changes(self, ancestry, read):
        read.side_effect = ["merge parent", "base", "original", "different",
                            "- commit1\n- commit2"]
        self.assertEqual("rebase", git_tool.merge_equivalence("head", "merge", "main"))
        read.side_effect = ["merge parent", "base", "original", "different",
                            "- commit1\n+ commit2"]
        with self.assertRaisesRegex(ValueError, "cannot prove"):
            git_tool.merge_equivalence("head", "merge", "main")

    @patch.object(git_tool, "output", return_value="merge parent1 parent2")
    @patch.object(git_tool, "ancestor", return_value=False)
    def test_nonancestral_merge_commit_shape_fails_closed(self, ancestry, read):
        with self.assertRaisesRegex(ValueError, "parent structure"):
            git_tool.merge_equivalence("head", "merge", "main")

    @patch.object(git_tool, "lifecycle_snapshot")
    @patch.object(git_tool, "git")
    def test_patch_equivalent_cleanup_uses_exact_ref_cas(self, command, snapshot):
        state = dict(self.state(), action="cleanup", target_branch="feature/old",
                     target_path="C:/safe/Clickra-pr-old", target_head="a" * 40,
                     merge_mode="squash", main="c" * 40)
        snapshot.return_value = state
        with contextlib.redirect_stdout(io.StringIO()):
            git_tool.lifecycle_run("cleanup", pr=185, execute=True,
                                   authorize=git_tool.authorization(state))
        command.assert_any_call("update-ref", "-d", "refs/heads/feature/old", "a" * 40)


class GitToolIntegrationTests(unittest.TestCase):
    def test_real_repositories_refuse_published_and_dirty_branches(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            remote = base / "remote.git"
            seed = base / "seed"
            feature = base / "feature"

            def call(cwd, *argv):
                git_exe = shutil.which("git")
                self.assertIsNotNone(git_exe)
                result = subprocess.run([str(Path(git_exe).resolve()), *argv], cwd=cwd, text=True,
                                        capture_output=True, timeout=15, check=False)
                self.assertEqual(0, result.returncode, result.stderr)
                return result.stdout.strip()

            call(base, "init", "--bare", str(remote))
            call(base, "init", "-b", "main", str(seed))
            call(seed, "config", "user.email", "test@example.invalid")
            call(seed, "config", "user.name", "Integration Test")
            (seed / "file.txt").write_text("initial", encoding="utf-8")
            call(seed, "add", "file.txt")
            call(seed, "commit", "-m", "initial")
            call(seed, "remote", "add", "origin", str(remote))
            call(seed, "push", "-u", "origin", "main")
            call(base, "clone", "-b", "main", str(remote), str(feature))
            call(feature, "checkout", "-b", "feature/local")
            old = os.getcwd()
            try:
                os.chdir(feature)
                state = git_tool.snapshot("rebase")
                self.assertEqual("feature/local", state["branch"])
                (feature / "untracked.txt").write_text("dirty", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "untracked"):
                    git_tool.snapshot("rebase")
                (feature / "untracked.txt").unlink()
                call(feature, "push", "-u", "origin", "feature/local")
                with self.assertRaisesRegex(ValueError, "published history"):
                    git_tool.snapshot("rebase")
            finally:
                os.chdir(old)


if __name__ == "__main__":
    unittest.main()
