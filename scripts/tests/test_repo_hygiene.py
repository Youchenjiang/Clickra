"""Tests repository structure, file taxonomy, and hygiene rules.

Ensures that:
1. Legacy directories (such as tools/ and archive/) are strictly prohibited.
2. Tracked root files/directories comply with the repository whitelist.
3. Documentation adheres to the five-folder taxonomy under docs/.
4. Diagnostics and script tests remain segregated in their directories.
5. Temporary, build, and scratch artifacts are never tracked by git.
"""

import subprocess
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]

ALLOWED_ROOT_DIRS = {
    ".agent",
    ".github",
    "docs",
    "packaging",
    "scripts",
    "src",
    "tests",
    "third_party",
}

ALLOWED_ROOT_FILES = {
    ".deepsource.toml",
    ".gitattributes",
    ".gitignore",
    ".sonarcloud.properties",
    "AGENTS.md",
    "CHANGELOG.md",
    "LICENSE",
    "PRIVACY.md",
    "README.md",
    "README.zh-TW.md",
    "THIRD-PARTY-NOTICES.txt",
}

ALLOWED_DOCS_ROOT_FILES = {
    "README.md",
    "ROADMAP.md",
}

ALLOWED_DOCS_SUBDIRS = {
    "architecture",
    "development",
    "history",
    "specs",
    "store",
}


def get_git_tracked_files() -> list[str]:
    """Return all files tracked by git relative to repository root."""
    try:
        output = subprocess.check_output(
            ["git", "ls-files"],
            cwd=REPO_ROOT,
            text=True,
            encoding="utf-8",
        )
        return [
            line.strip().replace("\\", "/")
            for line in output.splitlines()
            if line.strip()
        ]
    except Exception as exc:
        raise RuntimeError(
            f"Failed to query git tracked files: {exc}"
        ) from exc


class RepoHygieneTests(unittest.TestCase):
    def test_no_banned_directories_exist(self):
        """Ensure deprecated root directories do not exist on disk."""
        banned = ["tools", "archive", "bin", "obj"]
        for dirname in banned:
            target = REPO_ROOT / dirname
            self.assertFalse(
                target.exists(),
                f"Banned directory '{dirname}' exists at repo root.",
            )

    def test_tracked_root_directories_whitelist(self):
        """Ensure git-tracked root directories match the allowed whitelist."""
        tracked_files = get_git_tracked_files()
        tracked_root_dirs = {
            f.split("/")[0] for f in tracked_files if "/" in f
        }
        unexpected = tracked_root_dirs - ALLOWED_ROOT_DIRS
        self.assertEqual(
            set(),
            unexpected,
            f"Unexpected tracked root directories found: {unexpected}",
        )

    def test_tracked_root_files_whitelist(self):
        """Ensure git-tracked root files match the allowed whitelist."""
        tracked_files = get_git_tracked_files()
        tracked_root_files = {
            f for f in tracked_files if "/" not in f
        }
        unexpected = tracked_root_files - ALLOWED_ROOT_FILES
        self.assertEqual(
            set(),
            unexpected,
            f"Unexpected tracked root files found: {unexpected}",
        )

    def test_docs_root_files_whitelist(self):
        """Ensure docs root only contains approved index and roadmap files."""
        docs_dir = REPO_ROOT / "docs"
        self.assertTrue(docs_dir.is_dir(), "docs directory must exist.")

        direct_files = {
            p.name for p in docs_dir.iterdir() if p.is_file()
        }
        unexpected = direct_files - ALLOWED_DOCS_ROOT_FILES
        self.assertEqual(
            set(),
            unexpected,
            f"docs/ root files must be README.md and ROADMAP.md: {unexpected}",
        )

    def test_docs_subdirectories_whitelist(self):
        """Ensure docs subdirectories strictly conform to the 5 domains."""
        tracked_files = get_git_tracked_files()
        tracked_subdirs = {
            f.split("/")[1]
            for f in tracked_files
            if f.startswith("docs/") and f.count("/") >= 2
        }
        unexpected = tracked_subdirs - ALLOWED_DOCS_SUBDIRS
        self.assertEqual(
            set(),
            unexpected,
            f"Unexpected tracked subdirectories in docs/: {unexpected}",
        )

        docs_dir = REPO_ROOT / "docs"
        direct_dirs = {
            p.name for p in docs_dir.iterdir()
            if p.is_dir() and any(p.iterdir())
        }
        unexpected_disk = direct_dirs - ALLOWED_DOCS_SUBDIRS
        self.assertEqual(
            set(),
            unexpected_disk,
            f"Unexpected subdirectories in docs/: {unexpected_disk}",
        )

    def test_docs_store_listings(self):
        """Ensure StoreListing files reside exclusively in docs/store/."""
        tracked_files = get_git_tracked_files()
        misplaced = [
            f for f in tracked_files
            if "StoreListing_" in f and not f.startswith("docs/store/")
        ]
        self.assertEqual(
            [],
            misplaced,
            f"StoreListing files found outside docs/store/: {misplaced}",
        )

    def test_no_test_scripts_in_scripts_root(self):
        """Ensure test files are in scripts/tests/, not scripts/ directly."""
        scripts_dir = REPO_ROOT / "scripts"
        misplaced = list(scripts_dir.glob("test_*.py"))
        self.assertEqual(
            [],
            misplaced,
            f"Test files must reside in scripts/tests/: {misplaced}",
        )

    def test_scripts_tests_naming_convention(self):
        """Ensure scripts/tests/ files follow test naming conventions."""
        tests_dir = REPO_ROOT / "scripts" / "tests"
        for p in tests_dir.glob("*.py"):
            name = p.name
            is_valid = (
                name.startswith("test_")
                or name.startswith("_")
                or name == "__init__.py"
            )
            msg = (
                f"File in scripts/tests/ must start with 'test_' or '_': "
                f"{name}"
            )
            self.assertTrue(is_valid, msg)

    def test_scripts_diagnostics_segregation(self):
        """Ensure diagnostics dir exists and contains diagnostic tools."""
        diag_dir = REPO_ROOT / "scripts" / "diagnostics"
        self.assertTrue(
            diag_dir.is_dir(),
            "scripts/diagnostics directory must exist.",
        )
        scripts = list(diag_dir.glob("*.py"))
        self.assertGreaterEqual(
            len(scripts),
            10,
            "scripts/diagnostics must contain relocated tools.",
        )

    def test_no_scratch_or_temporary_files_tracked(self):
        """Ensure temporary, binary, or scratch files are never tracked."""
        tracked_files = get_git_tracked_files()
        banned_patterns = [
            ".tmp",
            ".rar",
            ".msix",
            ".log",
            "__pycache__",
            ".suo",
            ".user",
        ]
        violations = [
            f for f in tracked_files
            if any(f.endswith(p) or f"/{p}/" in f for p in banned_patterns)
        ]
        self.assertEqual(
            [],
            violations,
            f"Banned temporary or binary files tracked by git: {violations}",
        )


if __name__ == "__main__":
    unittest.main()
