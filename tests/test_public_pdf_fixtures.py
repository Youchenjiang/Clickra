"""Contracts for the public PDF fixtures; no internet is used by these unit tests."""
import hashlib
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from fetch_public_pdf_fixtures import SOURCES, verify


class PublicPdfFixtureTests(unittest.TestCase):
    def test_three_pinned_public_sources(self):
        self.assertEqual(set(SOURCES), {"2407", "pentest", "togll"})
        for url, filename, checksum in SOURCES.values():
            self.assertTrue(url.startswith("https://"))
            self.assertTrue(filename.endswith(".pdf"))
            self.assertEqual(len(checksum), 64)

    def test_rejects_other_content(self):
        with self.assertRaisesRegex(ValueError, "not a PDF"):
            verify(b"<html>wrong</html>", "0" * 64)

    def test_rejects_different_pdf(self):
        with self.assertRaisesRegex(ValueError, "mismatch"):
            verify(b"%PDF-1.7\n", "0" * 64)

    def test_accepts_pinned_pdf(self):
        data = b"%PDF-1.7\nfixture"
        verify(data, hashlib.sha256(data).hexdigest())


if __name__ == "__main__":
    unittest.main()
