import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]


class StoreAcquisitionTests(unittest.TestCase):
    def test_vendored_store_helper_has_pinned_mpl_provenance(self):
        vendor = ROOT / "third_party" / "microsoft-store-package-downloader"
        provenance = (vendor / "PROVENANCE.md").read_text(encoding="utf-8")
        self.assertIn("d1c508cc458223ec7ae684e685625c5b7cb62286", provenance)
        self.assertIn("Mozilla Public License 2.0", provenance)
        self.assertTrue((vendor / "LICENSE").is_file())
        self.assertTrue((vendor / "references" / "storelib" / "LICENSE").is_file())
        self.assertTrue((vendor / "references" / "storelib" / "FE3Handler.cs").is_file())
        self.assertTrue((vendor / "scripts" / "download-store-package.ps1").is_file())

    def test_production_acquisition_uses_only_local_vendored_helper(self):
        wrapper = (ROOT / "scripts" / "acquire_store_package.ps1").read_text(encoding="utf-8")
        self.assertIn("third_party\\microsoft-store-package-downloader", wrapper)
        self.assertNotIn("store.rg-adguard.net", wrapper)
        self.assertNotIn("git clone", wrapper.lower())
        self.assertNotIn("& powershell -NoProfile", wrapper)

    def test_workflow_no_longer_requires_self_hosted_runner(self):
        workflow = (ROOT / ".github" / "workflows" / "store-release-enrichment.yml").read_text(encoding="utf-8")
        self.assertIn("runs-on: windows-latest", workflow)
        self.assertNotIn("runs-on: [self-hosted", workflow)
        self.assertNotIn("clickra-store]", workflow)

    def test_sonar_excludes_only_vendored_store_source(self):
        sonar = (ROOT / ".sonarcloud.properties").read_text(encoding="utf-8").strip()
        self.assertEqual(
            "sonar.exclusions=third_party/microsoft-store-package-downloader/**",
            sonar,
        )


if __name__ == "__main__":
    unittest.main()
