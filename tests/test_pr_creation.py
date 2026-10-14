"""PR creation is separately authorized and checked against the remote SHA."""
import sys
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import pr_creation


class CreateTests(unittest.TestCase):
    def test_creation_rejects_pr_sequence_branch_before_external_reads(self):
        with patch.object(pr_creation, "_read") as reader:
            with self.assertRaisesRegex(ValueError, "PR sequence"):
                pr_creation.current_branch("user/repo", "refactor/storage-settings-pr2")
            reader.assert_not_called()

    @staticmethod
    def body_file(folder):
        body = Path(folder) / "pr.md"
        body.write_text(
            "## Summary\nA\n## Changes\nB\n## Validation\nC\n## PR Metadata\n"
            "- [x] **Assignee**: @owner\n- [x] **Label**: agent\n"
            "- [x] **Milestone**: Plan\n- [x] **External provider**: none\n",
            encoding="utf-8")
        return body

    def test_preview_and_wrong_authorization_never_create_pr(self):
        with TemporaryDirectory() as folder:
            body = self.body_file(folder)
            kwargs = dict(title="feat(agent): integrate PR governance", body_file=str(body),
                          label="agent", milestone="Plan")
            with patch.object(pr_creation, "current_branch", return_value="a" * 40), \
                 patch.object(pr_creation, "_metadata"), \
                 patch.object(pr_creation.subprocess, "run") as runner:
                preview = pr_creation.create("user/repo", "feature/workflow", **kwargs)
                self.assertIn("user/repo:feature/workflow:", preview["preview"])
                with self.assertRaisesRegex(ValueError, "authorization"):
                    pr_creation.create("user/repo", "feature/workflow", **kwargs,
                                       execute=True, authorization="incorrect")
                runner.assert_not_called()

    def test_authorized_create_verifies_pr_head_and_does_not_mutate_comments(self):
        with TemporaryDirectory() as folder:
            body = self.body_file(folder)
            kwargs = dict(title="feat(agent): integrate PR governance", body_file=str(body),
                          label="agent", milestone="Plan")
            sha = "a" * 40
            with patch.object(pr_creation, "current_branch", return_value=sha), \
                 patch.object(pr_creation, "_metadata"), \
                 patch.object(pr_creation, "_read", return_value=(
                     '{"number":123,"headRefOid":"' + sha + '","baseRefName":"main",'
                     '"url":"https://github.com/user/repo/pull/123"}')), \
                 patch.object(pr_creation, "executable", return_value="/bin/gh"), \
                 patch.object(pr_creation.subprocess, "run") as runner:
                runner.return_value = SimpleNamespace(
                    returncode=0, stdout="https://github.com/user/repo/pull/123\n")
                approval = pr_creation.create("user/repo", "feature/workflow", **kwargs)["preview"]
                result = pr_creation.create("user/repo", "feature/workflow", **kwargs,
                                            execute=True, authorization=approval, runner=runner)
            self.assertEqual(sha, result["head"])
            argv = runner.call_args.args[0]
            self.assertEqual(["/bin/gh", "pr", "create"], argv[:3])
            self.assertNotIn("comment", argv)
            self.assertNotIn("merge", argv)
            self.assertFalse(runner.call_args.kwargs["shell"])

    def test_existing_pr_fails_before_write(self):
        sha = "a" * 40
        values = {
            ("git", "rev-parse", "HEAD"): sha,
            ("git", "branch", "--show-current"): "feature/workflow",
            ("git", "status", "--porcelain"): "",
            ("git", "rev-parse", "origin/main"): "b" * 40,
            ("git", "ls-remote", "--heads", "origin", "main"): "b" * 40 + "\trefs/heads/main",
            ("gh", "pr", "list", "--state", "all", "--head", "feature/workflow",
             "--repo", "user/repo", "--json", "number"): '[{"number":123}]'}
        with self.assertRaisesRegex(RuntimeError, "existing"), \
             patch.object(pr_creation, "_read", side_effect=lambda argv: values[tuple(argv)]), \
             patch.object(pr_creation, "remote_head", return_value=sha):
            pr_creation.current_branch("user/repo", "feature/workflow")

    def test_invalid_milestone_fails_during_preview(self):
        with TemporaryDirectory() as folder:
            body = self.body_file(folder)
            with patch.object(pr_creation, "current_branch", return_value="a" * 40), \
                 patch.object(pr_creation, "executable", return_value="/bin/gh"), \
                 patch.object(pr_creation.subprocess, "run") as runner:
                runner.side_effect = [
                    SimpleNamespace(returncode=0, stdout='[[{"name":"agent"}]]'),
                    SimpleNamespace(returncode=0, stdout='[[{"title":"Real Milestone"}]]')]
                with self.assertRaisesRegex(ValueError, "milestone does not exist"):
                    pr_creation.create("user/repo", "feature/workflow",
                                       title="feat(agent): test", body_file=str(body),
                                       label="agent", milestone="Plan")
                self.assertEqual(2, runner.call_count)
                self.assertTrue(all("create" not in call.args[0] for call in runner.call_args_list))

    def test_unknown_create_outcome_is_not_retried(self):
        with TemporaryDirectory() as folder:
            body = self.body_file(folder)
            options = dict(title="feat(agent): test", body_file=str(body), label="agent")
            with patch.object(pr_creation, "current_branch", return_value="a" * 40), \
                 patch.object(pr_creation, "_metadata"), \
                 patch.object(pr_creation, "executable", return_value="/bin/gh"):
                approval = pr_creation.create("user/repo", "feature/workflow", **options)["preview"]
                runner = unittest.mock.Mock(side_effect=TimeoutError("response lost"))
                with self.assertRaisesRegex(RuntimeError, "outcome unknown"):
                    pr_creation.create("user/repo", "feature/workflow", **options,
                                       execute=True, authorization=approval, runner=runner)
                runner.assert_called_once()


if __name__ == "__main__":
    unittest.main()
