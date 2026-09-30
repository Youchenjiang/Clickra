import importlib.util
import pathlib
import subprocess
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
        body, changed = enrich.merge_release_body("Existing notes", section)
        self.assertTrue(changed)
        self.assertIn(enrich.STORE_SECTION_MARKER, body)
        body_again, changed_again = enrich.merge_release_body(body, section)
        self.assertFalse(changed_again)
        self.assertEqual(body, body_again)

    def test_acquisition_invokes_official_helper_and_validates_evidence(self):
        completed = subprocess.CompletedProcess(
            ["powershell"],
            0,
            stdout=(
                '{"ProductId":"9NGLBF6P1KLD","Identity":"g1014308.Clickra",'
                '"Version":"3.11.0.0","SourceFileName":"g1014308.Clickra_3.11.0.0_neutral__mgcm3zc7fc0ty.Msix",'
                '"SHA1":"ABC","SHA256":"DEF"}'
            ),
            stderr="",
        )
        with mock.patch.object(enrich, "run_command", return_value=completed) as runner:
            evidence = enrich.acquire_store_package(
                pathlib.Path("Clickra-store.msix"),
                version="3.11.0.0",
                product_id="9NGLBF6P1KLD",
            )
        self.assertEqual("DEF", evidence["SHA256"])
        command = runner.call_args.args[0]
        self.assertIn("acquire_store_package.ps1", " ".join(command))
        self.assertIn("9NGLBF6P1KLD", command)
        self.assertIn("3.11.0.0", command)

    def test_acquisition_fails_closed_on_unexpected_identity(self):
        completed = subprocess.CompletedProcess(
            ["powershell"],
            0,
            stdout='{"ProductId":"9NGLBF6P1KLD","Identity":"Other.App","Version":"3.11.0.0"}',
            stderr="",
        )
        with mock.patch.object(enrich, "run_command", return_value=completed), self.assertRaises(RuntimeError):
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
