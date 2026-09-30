import importlib.util
import pathlib
import subprocess
import tempfile
import unittest
from unittest import mock


SCRIPT_DIR = pathlib.Path(__file__).resolve().parents[1] / "scripts"
SCRIPT_PATH = SCRIPT_DIR / "enrich_store_release.py"
SPEC = importlib.util.spec_from_file_location("enrich_store_release", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load Store enrichment module from {SCRIPT_PATH}")
enrich = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(enrich)


class StoreReleaseEnrichmentTests(unittest.TestCase):
    def test_run_command_surfaces_captured_failure_details(self):
        failure = subprocess.CalledProcessError(
            1,
            ["powershell"],
            output="verifier stdout",
            stderr="verifier stderr",
        )
        with mock.patch.object(enrich.subprocess, "run", side_effect=failure), \
             self.assertRaisesRegex(RuntimeError, r"(?s)verifier stdout.*verifier stderr"):
            enrich.run_command(["powershell"])

    def test_gh_stdout_is_decoded_as_utf8(self):
        completed = subprocess.CompletedProcess(
            ["gh"],
            0,
            stdout='{"body":"### 📦 Installation\\n下載安裝"}',
            stderr="",
        )
        with mock.patch.object(enrich.subprocess, "run", return_value=completed) as runner:
            result = enrich.run_command(["gh", "release", "view"])
        self.assertEqual('{"body":"### 📦 Installation\\n下載安裝"}', result.stdout)
        self.assertEqual("utf-8", runner.call_args.kwargs["encoding"])
        self.assertEqual("strict", runner.call_args.kwargs["errors"])

    def test_non_gh_commands_do_not_force_text_encoding(self):
        completed = subprocess.CompletedProcess(["powershell"], 0, stdout="ok", stderr="")
        with mock.patch.object(enrich.subprocess, "run", return_value=completed) as runner:
            enrich.run_command(["powershell", "-NoProfile"])
        self.assertIsNone(runner.call_args.kwargs["encoding"])
        self.assertIsNone(runner.call_args.kwargs["errors"])

    def test_windows_powershell_gets_isolated_module_path(self):
        source = {
            "USERPROFILE": r"C:\Users\runneradmin",
            "ProgramFiles": r"C:\Program Files",
            "SystemRoot": r"C:\Windows",
            "PSModulePath": (
                r"C:\Program Files\PowerShell\7\Modules;"
                r"C:\Program Files\WindowsPowerShell\Modules"
            ),
        }
        with mock.patch.dict(enrich.os.environ, source, clear=True):
            env = enrich.command_environment(["powershell", "-NoProfile"], platform_name="nt")
        self.assertIsNotNone(env)
        assert env is not None
        self.assertNotIn(r"PowerShell\7\Modules", env["PSModulePath"])
        self.assertIn(r"WindowsPowerShell\Modules", env["PSModulePath"])
        self.assertIn(r"WindowsPowerShell\v1.0\Modules", env["PSModulePath"])

    def test_non_powershell_commands_keep_default_environment(self):
        self.assertIsNone(enrich.command_environment(["gh", "release", "view"]))

    def test_non_windows_powershell_keeps_default_environment(self):
        self.assertIsNone(
            enrich.command_environment(["powershell", "-NoProfile"], platform_name="posix")
        )

    def test_parse_release_version_requires_v_four_part_numeric_version(self):
        self.assertEqual("3.11.0.0", enrich.parse_release_version("v3.11.0.0"))
        for value in ("3.11.0.0", "v3.11", "v3.11.0.x", "v"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                enrich.parse_release_version(value)

    def test_absent_asset_requests_upload(self):
        release = {"assets": []}
        self.assertEqual("upload", enrich.ensure_asset_state(release, "Clickra-store.msix", "abc"))

    def test_matching_asset_is_idempotently_present(self):
        release = {"assets": [{"name": "Clickra-store.msix", "digest": "sha256:ABC"}]}
        self.assertEqual("present", enrich.ensure_asset_state(release, "Clickra-store.msix", "abc"))

    def test_different_existing_asset_fails_closed(self):
        release = {"assets": [{"name": "Clickra-store.msix", "digest": "sha256:def"}]}
        with self.assertRaises(RuntimeError):
            enrich.ensure_asset_state(release, "Clickra-store.msix", "abc")

    def test_existing_asset_without_digest_fails_closed(self):
        release = {"assets": [{"name": "Clickra-store.msix", "digest": None}]}
        with self.assertRaises(RuntimeError):
            enrich.ensure_asset_state(release, "Clickra-store.msix", "abc")

    def test_release_notes_append_once(self):
        section = enrich.build_store_section("Clickra-store.msix", "abc")
        existing = "### 📦 Installation\n下載安裝：\n"
        body, changed = enrich.merge_release_body(existing, section)
        self.assertTrue(changed)
        self.assertIn("📦 Installation", body)
        self.assertIn("下載安裝", body)
        self.assertIn(enrich.STORE_SECTION_MARKER, body)
        body_again, changed_again = enrich.merge_release_body(body, section)
        self.assertFalse(changed_again)
        self.assertEqual(body, body_again)

    def test_release_notes_file_is_utf8_and_lf_only(self):
        release = {
            "assets": [{"name": "Clickra-store.msix", "digest": "sha256:abc"}],
            "body": "### 📦 Installation\n下載安裝：\n",
        }
        with tempfile.TemporaryDirectory() as temp_dir, \
             mock.patch.object(enrich, "run_command") as runner:
            package = pathlib.Path(temp_dir) / "Clickra-store.msix"
            package.write_bytes(b"placeholder")
            enrich.update_release(
                "Youchenjiang/Clickra",
                "v3.11.0.0",
                package,
                "Clickra-store.msix",
                "abc",
                release,
            )
            notes_path = pathlib.Path(temp_dir) / "release-notes.md"
            raw = notes_path.read_bytes()
            decoded = raw.decode("utf-8")
            self.assertIn("📦 Installation", decoded)
            self.assertIn("下載安裝", decoded)
            self.assertNotIn(b"\r\n", raw)
            self.assertIn("--notes-file", runner.call_args.args[0])

    def test_acquisition_invokes_official_helper_and_validates_evidence(self):
        completed = subprocess.CompletedProcess(["powershell"], 0, stdout="Downloading...", stderr="")
        destination = pathlib.Path("Clickra-store.msix")
        payload = (
            '{"ProductId":"9NGLBF6P1KLD","Identity":"g1014308.Clickra",'
            '"Version":"3.11.0.0","SourceFileName":"g1014308.Clickra_3.11.0.0_neutral__mgcm3zc7fc0ty.Msix",'
            '"SHA1":"ABC","SHA256":"DEF"}'
        )
        with mock.patch.object(enrich, "run_command", return_value=completed) as runner, \
             mock.patch.object(pathlib.Path, "is_file", return_value=True), \
             mock.patch.object(pathlib.Path, "read_text", return_value=payload), \
             mock.patch.object(pathlib.Path, "unlink"):
            evidence = enrich.acquire_store_package(destination, version="3.11.0.0", product_id="9NGLBF6P1KLD")
        self.assertEqual("DEF", evidence["SHA256"])
        command = runner.call_args.args[0]
        self.assertIn("acquire_store_package.ps1", " ".join(command))
        self.assertIn("9NGLBF6P1KLD", command)
        self.assertIn("3.11.0.0", command)
        self.assertIn("Clickra-store.acquisition.json", command)

    def test_acquisition_fails_closed_on_unexpected_identity(self):
        completed = subprocess.CompletedProcess(["powershell"], 0, stdout="", stderr="")
        with mock.patch.object(enrich, "run_command", return_value=completed), \
             mock.patch.object(pathlib.Path, "is_file", return_value=True), \
             mock.patch.object(pathlib.Path, "read_text", return_value='{"ProductId":"9NGLBF6P1KLD","Identity":"Other.App","Version":"3.11.0.0"}'), \
             self.assertRaises(RuntimeError):
            enrich.acquire_store_package(
                pathlib.Path("Clickra-store.msix"),
                version="3.11.0.0",
                product_id="9NGLBF6P1KLD",
            )

    def test_verifier_requires_acquisition_filename_and_sha1(self):
        with self.assertRaises(RuntimeError):
            enrich.verify_store_package(pathlib.Path("Clickra-store.msix"), "3.11.0.0", {})


if __name__ == "__main__":
    unittest.main()
