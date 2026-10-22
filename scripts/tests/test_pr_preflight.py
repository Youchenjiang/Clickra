import sys
import shutil
import subprocess
import unittest
from unittest.mock import patch
from subprocess import CompletedProcess, TimeoutExpired
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from pr_preflight import validate_message, validate_pr_body, check, _check_history


class PrPreflightTests(unittest.TestCase):
    def test_pr_sequence_branch_rejected_at_preflight(self):
        from pr_preflight import _check_pushed_ref
        with patch("pr_preflight.git", return_value=CompletedProcess([], 0, "feature/dash-pr2", "")):
            errors = _check_pushed_ref(None, None)
        self.assertTrue(any("PR sequence" in item for item in errors))

    def test_existing_remote_branch_without_pr_uses_draft_metadata(self):
        from pr_preflight import _publication_metadata
        def read(*args):
            if args[0] == "branch":
                return CompletedProcess(args, 0, "feature/new\n", "")
            return CompletedProcess(args, 0, "https://github.com/user/repo.git\n", "")
        with patch("pr_preflight.git", side_effect=read), \
             patch("pr_preflight.invoke", return_value=CompletedProcess([], 0, "[]", "")) as github, \
             patch("pr_preflight._github_metadata") as actual:
            self.assertIsNone(_publication_metadata())
            actual.assert_not_called()
        self.assertIn("--state", github.call_args.args[0])
        self.assertIn("all", github.call_args.args[0])
        args = github.call_args.args[0]
        self.assertEqual("user/repo", args[args.index("--repo") + 1])

    def test_existing_open_pr_requires_live_metadata(self):
        from pr_preflight import _publication_metadata
        def read(*args):
            value = "feature/new" if args[0] == "branch" else "git@github.com:user/repo.git"
            return CompletedProcess(args, 0, value, "")
        prs = '[{"number":42,"state":"OPEN","headRefName":"feature/new"}]'
        live = {"labels": [{"name": "agent"}], "milestone": {"title": "Roadmap"}}
        with patch("pr_preflight.git", side_effect=read), \
             patch("pr_preflight.invoke", return_value=CompletedProcess([], 0, prs, "")) as github, \
             patch("pr_preflight._github_metadata", return_value=live) as actual:
            self.assertEqual(live, _publication_metadata())
            actual.assert_called_once()
            args = github.call_args.args[0]
            self.assertEqual("user/repo", args[args.index("--repo") + 1])

    def test_failed_or_ambiguous_pr_lookup_fails_closed(self):
        from pr_preflight import _publication_metadata
        def read(*args):
            value = "feature/new" if args[0] == "branch" else "https://github.com/user/repo.git"
            return CompletedProcess(args, 0, value, "")
        for code, output in [(1, ""), (0, "broken"), (0, '[{"state":"CLOSED"}]'),
                             (0, '[{"state":"OPEN"},{"state":"OPEN"}]')]:
            with self.subTest(code=code, output=output), \
                 patch("pr_preflight.git", side_effect=read), \
                 patch("pr_preflight.invoke", return_value=CompletedProcess([], code, output, "")), \
                 patch("pr_preflight._github_metadata") as live:
                with self.assertRaises((OSError, ValueError)):
                    _publication_metadata()
                live.assert_not_called()

    def test_initial_branch_preflight_uses_draft_without_nonexistent_pr_metadata(self):
        from pr_preflight import main
        sha = "a" * 40
        with patch("sys.argv", ["pr_preflight.py", "--check", "--initial-push",
                                "--push-ref", "refs/heads/feature/new",
                                "--push-oid", sha]), \
             patch("pr_preflight._publication_metadata", return_value=None) as github, \
             patch("pr_preflight.check", return_value=[]) as checked:
            self.assertEqual(0, main())
        github.assert_called_once()
        self.assertIsNone(checked.call_args.args[-1])

    def test_initial_branch_preflight_needs_exact_ref(self):
        from pr_preflight import main
        with patch("sys.argv", ["pr_preflight.py", "--check", "--initial-push"]), \
             patch("pr_preflight._github_metadata") as github:
            self.assertEqual(1, main())
        github.assert_not_called()

    def test_commit_requires_numbered_body(self):
        self.assertTrue(validate_message("fix(core): repair fallback"))
        self.assertEqual([], validate_message("fix(core): repair fallback\n\n1. Repair fallback."))

    def test_invalid_commit_title(self):
        self.assertTrue(validate_message("a nonconventional subject\n\n1. Change."))

    def test_vague_commit_and_commented_body_rejected(self):
        self.assertTrue(validate_message("fix: update\n\n1. Explain."))
        self.assertTrue(validate_message("fix(core): repair\n\n# 1. Comment only."))
        self.assertIn("numbered commit body must be predominantly English/ASCII letters",
                      validate_message("fix(core): repair\n\n1. 修正流程與權限問題，並驗證測試"))

    VALID_CHECKLIST = (
        "- [x] **Behavior**: reviewed intentional behavior changes.\n"
        "- [x] **Regression**: assessed affected paths.\n"
        "- [x] **Compatibility**: reviewed compatibility.\n"
        "- [x] **Security**: assessed security implications.\n"
        "- [x] **Documentation**: updated relevant docs.\n"
        "- [x] **External provider**: No new provider.\n"
    )

    def test_pr_body_requires_provider_acknowledgement(self):
        body = "## Summary\nText\n## Changes\n- A\n## Validation\n- B\n## Review Checklist\n"
        self.assertIn("completed External provider acknowledgement required", validate_pr_body(body))
        complete = body + self.VALID_CHECKLIST
        self.assertEqual([], validate_pr_body(complete))

    def test_review_prompts_allow_unchecked_state_but_require_acknowledgement(self):
        body = ("## Summary\nA\n## Changes\nB\n## Validation\nC\n"
                "## Review Checklist\n"
                "- [ ] **Behavior**: not applicable to this documentation change\n"
                "- [ ] **Regression**: no runtime regression expected\n"
                "- [ ] **Compatibility**: no compatibility changes\n"
                "- [ ] **Security**: no security footprint changes\n"
                "- [ ] **Documentation**: docs updated as described\n"
                "- [x] **External provider**: No new provider.\n")
        self.assertEqual([], validate_pr_body(body))
        unchecked = body.replace("- [x] **External provider**", "- [ ] **External provider**")
        self.assertIn("completed External provider acknowledgement required",
                      validate_pr_body(unchecked))
        self.assertIn("## Review Checklist", validate_pr_body(body.replace("## Review Checklist", "## PR Metadata")))

    def test_rejects_missing_review_checklist_prompts(self):
        body = ("## Summary\nA\n## Changes\nB\n## Validation\nC\n"
                "## Review Checklist\n"
                "- [x] **Behavior**: intentional changes\n"
                "- [x] **External provider**: No new provider.\n")
        errors = validate_pr_body(body)
        self.assertIn("missing review checklist prompt: Regression", errors)
        self.assertIn("missing review checklist prompt: Compatibility", errors)
        self.assertIn("missing review checklist prompt: Security", errors)
        self.assertIn("missing review checklist prompt: Documentation", errors)

    def test_rejects_duplicated_metadata_checkboxes(self):
        body = ("## Summary\nA\n## Changes\nB\n## Validation\nC\n"
                "## Review Checklist\n"
                "- [x] **Assignee**: @owner\n"
                "- [x] **Label**: agent\n"
                "- [x] **Milestone**: Plan\n"
                + self.VALID_CHECKLIST)
        errors = validate_pr_body(body)
        self.assertIn(
            "assignees, labels and milestones must not be duplicated "
            "as PR-body checkboxes; verify via GitHub metadata",
            errors,
        )

    def test_rejects_placeholders_in_review_checklist_prompts(self):
        body = ("## Summary\nA\n## Changes\nB\n## Validation\nC\n"
                "## Review Checklist\n"
                "- [x] **Behavior**: TODO check later\n"
                "- [x] **Regression**: assessed affected paths.\n"
                "- [x] **Compatibility**: reviewed compatibility.\n"
                "- [x] **Security**: assessed security implications.\n"
                "- [x] **Documentation**: updated relevant docs.\n"
                "- [x] **External provider**: No new provider.\n")
        errors = validate_pr_body(body)
        self.assertIn("placeholder in review checklist prompt: Behavior", errors)

    def test_real_github_metadata_is_checked_without_self_reported_checkboxes(self):
        body = ("## Summary\nA\n## Changes\nB\n## Validation\nC\n"
                "## Review Checklist\n" + self.VALID_CHECKLIST)
        self.assertEqual([], validate_pr_body(body, {"labels": [{"name": "agent"}],
                                                       "milestone": {"title": "Plan"}}))
        self.assertIn("GitHub PR requires at least one label",
                      validate_pr_body(body, {"labels": [], "milestone": {"title": "Plan"}}))

    def test_rejects_unfinished_metadata_and_placeholders(self):
        body = ("## Summary\nText\n## Changes\n- Describe the change.\n"
                "## Validation\n- PASS\n## Review Checklist\n" + self.VALID_CHECKLIST)
        errors = validate_pr_body(body)
        self.assertIn("empty or placeholder ## Changes", errors)

    def test_rejects_empty_sections(self):
        body = ("## Summary\n\n## Changes\n\n## Validation\n\n## Review Checklist\n"
                + self.VALID_CHECKLIST)
        errors = validate_pr_body(body)
        self.assertIn("empty or placeholder ## Summary", errors)
        self.assertIn("empty or placeholder ## Changes", errors)

    def test_multiline_html_comment_and_uppercase_checkboxes(self):
        uppercase_checklist = self.VALID_CHECKLIST.replace("- [x]", "- [X]")
        body = ("## Summary\n<!-- hidden\nnot content -->\nActual change\n"
                "## Changes\n- Genuine change\n## Validation\nPASS\n## Review Checklist\n"
                + uppercase_checklist)
        self.assertEqual([], validate_pr_body(body))
        self.assertIn("empty or placeholder ## Summary", validate_pr_body(body.replace("Actual change", "")))

    def test_milestone_exemption_requires_eligible_label(self):
        body = ("## Summary\nA\n## Changes\nB\n## Validation\nC\n## Review Checklist\n"
                + self.VALID_CHECKLIST)
        self.assertEqual([], validate_pr_body(body, {"labels": [{"name": "docs"}], "milestone": None}))
        self.assertIn("GitHub PR requires milestone unless exempt",
                      validate_pr_body(body, {"labels": [{"name": "agent"}], "milestone": None}))

    def test_real_metadata_overrides_draft_checkboxes_and_exemption(self):
        body = ("## Summary\nA\n## Changes\nB\n## Validation\nC\n## Review Checklist\n"
                + self.VALID_CHECKLIST)
        self.assertEqual([], validate_pr_body(body, {"labels": [{"name": "docs"}], "milestone": None}))
        self.assertIn("GitHub PR requires milestone unless exempt",
                      validate_pr_body(body, {"labels": [{"name": "agent"}], "milestone": None}))
        self.assertIn("GitHub PR requires at least one label",
                      validate_pr_body(body, {"labels": [], "milestone": {"title": "Plan"}}))

    @patch("pr_preflight.git")
    def test_pushed_ref_and_diff_failure_fail_closed(self, mock_git):
        def command(*args):
            values = {"branch": "feature/test", "rev-parse": "a" * 40,
                      "ls-remote": "a" * 40 + "\trefs/heads/main\n"}
            if args[0] == "branch":
                return CompletedProcess(args, 0, values["branch"], "")
            if args[0] == "rev-parse":
                return CompletedProcess(args, 0, values["rev-parse"], "")
            if args[0] == "ls-remote":
                return CompletedProcess(args, 0, values["ls-remote"], "")
            if args[0] == "diff":
                return CompletedProcess(args, 128, "", "fatal")
            return CompletedProcess(args, 0, "", "")
        mock_git.side_effect = command
        errors = _check_history("origin/main", "refs/heads/feature/other", "b" * 40)
        self.assertIn("pushed branch does not match checked development branch", errors)
        self.assertIn("pushed object is not the validated HEAD", errors)
        self.assertIn("cannot verify branch changes against base", errors)

    @patch("pr_preflight.git", side_effect=TimeoutExpired("git", 30))
    def test_timeout_fails_closed(self, _mock_git):
        self.assertTrue(any("Git operation failed" in error for error in check("origin/main")))

    @patch("pr_preflight._check_history", return_value=[])
    @patch("pr_preflight.git")
    def test_status_failure_and_remote_base_drift_fail_closed(self, mock_git, _history):
        from tempfile import TemporaryDirectory
        with TemporaryDirectory() as directory:
            body_file = Path(directory) / "body.md"
            body_file.write_text("## Summary\nA\n## Changes\nB\n## Validation\nC\n"
                                 "## Review Checklist\n" + self.VALID_CHECKLIST, encoding="utf-8")
            def command(*args):
                if args[0] == "status":
                    return CompletedProcess(args, 128, "", "fatal")
                if args[0] == "ls-remote":
                    return CompletedProcess(args, 0, "b" * 40 + "\trefs/heads/main\n", "")
                return CompletedProcess(args, 0, "a" * 40 + "\n", "")
            mock_git.side_effect = command
            errors = check("origin/main", body_file)
            self.assertIn("cannot read worktree status", errors)
            self.assertIn("origin/main changed or cannot be verified at end of preflight", errors)


class PrePushHookRefTests(unittest.TestCase):
    def run_hook(self, updates):
        shell = shutil.which("sh")
        if not shell and shutil.which("git"):
            shell = str(Path(shutil.which("git")).resolve().parent.parent / "bin" / "sh.exe")
        if not shell or (not Path(shell).is_file() and not shutil.which(shell)):
            self.skipTest("Git Bash sh unavailable")
        hook = Path(__file__).resolve().parents[1] / "hooks" / "pre-push"
        return subprocess.run([shell, str(hook), "origin", "https://example.invalid/repo.git"],
                              input=updates, capture_output=True, text=True, check=False,
                              cwd=hook.parents[2], timeout=15)

    def test_destination_main_master_and_ref_remapping_rejected(self):
        sha = "a" * 40
        for source, destination in (
            ("refs/heads/main", "refs/heads/main"),
            ("refs/heads/master", "refs/heads/master"),
            ("refs/heads/feature/example", "refs/heads/main"),
            ("refs/heads/feature/example", "refs/heads/feature/different"),
            ("refs/tags/v1.2.3.0", "refs/heads/feature/example"),
        ):
            with self.subTest(source=source, destination=destination):
                result = self.run_hook(f"{source} {sha} {destination} {'0' * 40}\n")
                self.assertNotEqual(0, result.returncode)
                self.assertIn("ERROR", result.stderr)

    def test_tag_and_branch_combination_rejected(self):
        sha = "a" * 40
        lines = [f"refs/tags/v1.2.3.0 {sha} refs/tags/v1.2.3.0 {'0' * 40}",
                 f"refs/heads/feature/example {sha} refs/heads/feature/example {'0' * 40}"]
        result = self.run_hook("\n".join(lines) + "\n")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("separate operations", result.stderr)

    def test_tag_only_has_explicit_pr_gate_exemption(self):
        sha = "a" * 40
        result = self.run_hook(f"refs/tags/v1.2.3.0 {sha} refs/tags/v1.2.3.0 {'0' * 40}\n")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("release/tag authorization and gates", result.stdout)

    def test_branch_reaches_pr_body_gate(self):
        sha = "a" * 40
        result = self.run_hook(f"refs/heads/feature/example {sha} refs/heads/feature/example {'0' * 40}\n")
        self.assertNotEqual(0, result.returncode)
        self.assertTrue("CLICKRA_PR_BODY_FILE" in result.stderr or "PR PREFLIGHT FAIL" in result.stderr,
                        result.stderr)


if __name__ == "__main__":
    unittest.main()
