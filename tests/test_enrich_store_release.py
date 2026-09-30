import importlib.util
import pathlib
import sys
import unittest
from unittest import mock


SCRIPT_DIR = pathlib.Path(__file__).resolve().parents[1] / "scripts"
RESOLVER_PATH = SCRIPT_DIR / "resolve_store_package.py"
RESOLVER_SPEC = importlib.util.spec_from_file_location("resolve_store_package", RESOLVER_PATH)
if RESOLVER_SPEC is None or RESOLVER_SPEC.loader is None:
    raise RuntimeError(f"Unable to load Store resolver module from {RESOLVER_PATH}")
resolve_store_package = importlib.util.module_from_spec(RESOLVER_SPEC)
sys.modules["resolve_store_package"] = resolve_store_package
RESOLVER_SPEC.loader.exec_module(resolve_store_package)

SCRIPT_PATH = SCRIPT_DIR / "enrich_store_release.py"
SPEC = importlib.util.spec_from_file_location("enrich_store_release", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load Store enrichment module from {SCRIPT_PATH}")
enrich = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(enrich)


class StoreReleaseEnrichmentTests(unittest.TestCase):
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

    def test_bundle_candidate_fails_closed_until_bundle_verification_exists(self):
        candidate = resolve_store_package.Candidate(
            name="g1014308.Clickra_3.11.0.0_neutral__mgcm3zc7fc0ty.msixbundle",
            url="https://dl.delivery.mp.microsoft.com/example",
            host="dl.delivery.mp.microsoft.com",
            https_transport=True,
            allowed_store_transport=True,
            extension=resolve_store_package.MSIX_BUNDLE_EXTENSION,
            reported_sha1="ABCDEF",
            reported_size="15 MB",
            identity=resolve_store_package.DEFAULT_IDENTITY,
            version="3.11.0.0",
            architecture=resolve_store_package.DEFAULT_ARCHITECTURE,
            microsoft_cdn=True,
            exact_identity=True,
            exact_version=True,
            intended_type=True,
            intended_architecture=True,
            selected=True,
        )
        with mock.patch.object(resolve_store_package, "query_resolver", return_value="fixture"), \
             mock.patch.object(resolve_store_package, "parse_candidates", return_value=[candidate]), \
             self.assertRaises(RuntimeError):
            enrich.select_exact_candidate("3.11.0.0", resolve_store_package.DEFAULT_PRODUCT_ID)


if __name__ == "__main__":
    unittest.main()
