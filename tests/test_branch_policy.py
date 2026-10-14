import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from branch_policy import validate_branch_name


class BranchPolicyTests(unittest.TestCase):
    def test_rejects_pr_sequence_suffix(self):
        for branch in ("refactor/storage-settings-ownership-pr2", "feat/dashboard-pr3",
                       "fix/thing-pr10", "feature/PR1", "refactor/thing_pr23"):
            with self.subTest(branch=branch):
                self.assertTrue(validate_branch_name(branch))

    def test_preserves_meaningful_numbers(self):
        for branch in ("fix/issue-180", "feat/oauth2-support", "refactor/net8-migration",
                       "fix/cve-2026-12345", "refactor/presentation-layer-ownership"):
            with self.subTest(branch=branch):
                self.assertEqual([], validate_branch_name(branch))


if __name__ == "__main__":
    unittest.main()
