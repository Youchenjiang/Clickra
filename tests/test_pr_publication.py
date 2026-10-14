"""Guarded publication must not bypass approval or Git hooks."""
import sys
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import pr_publication


class PublicationTests(unittest.TestCase):
    def test_preview_does_not_write_and_exact_authorization_required(self):
        with TemporaryDirectory() as folder:
            body = Path(folder) / "body.md"
            body.write_text("draft", encoding="utf-8")

            def read(argv):
                value = {("branch", "--show-current"): "feature/workflow",
                         ("rev-parse", "HEAD"): "a" * 40,
                         ("status", "--porcelain"): "",
                         ("remote", "get-url", "origin"): "https://github.com/a/b.git"}[tuple(argv[1:])]
                return SimpleNamespace(stdout=value, returncode=0)

            with patch.object(pr_publication, "invoke", side_effect=read), \
                 patch.object(pr_publication, "executable", return_value="/bin/git"), \
                 patch.object(pr_publication.subprocess, "run") as runner:
                self.assertEqual({"preview": "feature/workflow:" + "a" * 40},
                                 pr_publication.push(str(body), None))
                with self.assertRaisesRegex(ValueError, "exact"):
                    pr_publication.push(str(body), "wrong", execute=True)
                runner.assert_not_called()

    def test_remote_head_requires_exact_ref(self):
        with patch.object(pr_publication, "executable", return_value="/bin/git"), \
             patch.object(pr_publication.subprocess, "run",
                          return_value=SimpleNamespace(returncode=0,
                                                       stdout="a" * 40 + "\trefs/heads/other\n")):
            with self.assertRaisesRegex(RuntimeError, "verified"):
                pr_publication.remote_head("feature/workflow")

    def test_successful_push_runs_hook_without_force_and_verifies_remote(self):
        with TemporaryDirectory() as folder:
            body = Path(folder) / "body.md"
            body.write_text("draft", encoding="utf-8")
            sha = "a" * 40
            lookup = {("branch", "--show-current"): "feature/workflow",
                      ("rev-parse", "HEAD"): sha, ("status", "--porcelain"): "",
                      ("remote", "get-url", "origin"): "https://github.com/a/b.git"}
            with patch.object(pr_publication, "invoke",
                              side_effect=lambda argv: SimpleNamespace(stdout=lookup[tuple(argv[1:])],
                                                                       returncode=0)), \
                 patch.object(pr_publication, "executable", return_value="/bin/git"), \
                 patch.object(pr_publication, "remote_head", return_value=sha), \
                 patch.object(pr_publication.subprocess, "run",
                              return_value=SimpleNamespace(returncode=0)) as runner:
                self.assertEqual({"pushed": f"feature/workflow:{sha}"},
                                 pr_publication.push(str(body), f"feature/workflow:{sha}", execute=True,
                                                     runner=runner))
            argv = runner.call_args.args[0]
            self.assertEqual(["/bin/git", "-c", "core.hooksPath=scripts/hooks", "push", "origin",
                              "refs/heads/feature/workflow:refs/heads/feature/workflow"], argv)
            self.assertFalse(runner.call_args.kwargs["shell"])
            self.assertNotIn("--no-verify", argv)
            self.assertEqual(str(body.resolve()), runner.call_args.kwargs["env"]["CLICKRA_PR_BODY_FILE"])

    def test_protected_branch_denied_even_in_preview(self):
        with TemporaryDirectory() as folder:
            body = Path(folder) / "body.md"
            body.write_text("draft", encoding="utf-8")
            with patch.object(pr_publication, "invoke", side_effect=[
                    SimpleNamespace(stdout="main", returncode=0),
                    SimpleNamespace(stdout="a" * 40, returncode=0)]):
                with self.assertRaisesRegex(ValueError, "protected"):
                    pr_publication.push(str(body), None)
