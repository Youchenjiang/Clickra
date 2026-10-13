import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from gh_guard import validate_gh_args


class GhGuardTests(unittest.TestCase):
    def test_safe_read_only_calls(self):
        for args in (["pr", "checks", "177"], ["pr", "view", "177", "--json", "state"],
                     ["run", "view", "123", "--log-failed"], ["pr", "list", "--state", "open"]):
            with self.subTest(args=args):
                self.assertIsNone(validate_gh_args(args))

    def test_mutations_are_denied(self):
        for args in (["pr", "create"], ["pr", "merge", "177"],
                     ["pr", "edit", "177"], ["api", "graphql", "-f", "query=mutation"],
                     ["api", "repos/org/repo/pulls/177/comments", "-f", "body=reply"],
                     ["pr", "comment", "177"], ["run", "rerun", "123"],
                     ["pr", "review", "177", "--comment"]):
            with self.subTest(args=args):
                self.assertIsNotNone(validate_gh_args(args))

    def test_disallow_shell_shaped_and_unknown_options(self):
        self.assertIsNotNone(validate_gh_args(["pr", "view", "177;echo"] ))
        self.assertIsNotNone(validate_gh_args(["pr", "view", "177", "--web"]))
        self.assertIsNotNone(validate_gh_args(["pr", "view", "177", "--json"]))


if __name__ == "__main__":
    unittest.main()