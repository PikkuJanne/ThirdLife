import hashlib
import json
from pathlib import Path
import struct
import tempfile
import unittest
from zipfile import ZipFile

from tools import verify_development_package as package


SOURCE = "a" * 40


def sha256(content):
    return hashlib.sha256(content).hexdigest()


def apphost(machine=0x8664):
    content = bytearray(72)
    content[:2] = b"MZ"
    struct.pack_into("<I", content, 0x3C, 64)
    struct.pack_into("<4sH", content, 64, b"PE\0\0", machine)
    return content


class DevelopmentPackageTests(unittest.TestCase):
    def setUp(self):
        directory = self.enterContext(tempfile.TemporaryDirectory())
        self.root = Path(directory)

    def write_package(self, entries, *, bom_revision=SOURCE):
        archive = self.root / package.ARCHIVE
        with ZipFile(archive, "w") as payload:
            for name, content in entries:
                payload.writestr(name, content)
        archive_data = archive.read_bytes()
        sbom_data = json.dumps({
            "bomFormat": "CycloneDX",
            "specVersion": "1.6",
            "metadata": {"properties": [{"name": "thirdlife:source-revision", "value": bom_revision}]},
        }).encode("utf-8")
        (self.root / package.SBOM).write_bytes(sbom_data)
        self.receipt = {
            "classification": "private_unsigned_development",
            "source": SOURCE,
            "archive": package.ARCHIVE,
            "archive_sha256": sha256(archive_data),
            "archive_bytes": len(archive_data),
            "apphost": package.APPHOST,
            "apphost_sha256": sha256(dict(entries)[package.APPHOST]),
            "sbom": package.SBOM,
            "sbom_sha256": sha256(sbom_data),
            "entries": [name for name, _ in entries],
        }
        self.write_receipt()

    def write_receipt(self):
        (self.root / package.RECEIPT).write_text(json.dumps(self.receipt), encoding="utf-8")

    def verify(self):
        return package.verify(self.root, SOURCE, self.receipt["archive_sha256"], self.receipt["sbom_sha256"])

    def test_valid_private_package_with_source_bound_sbom(self):
        self.write_package([("ThirdLife.UI.dll", b"assembly"), (package.APPHOST, apphost())])
        self.assertEqual(self.verify(), 2)

    def test_archive_or_sbom_substitution_disagrees_with_reviewed_digest(self):
        for artifact in (package.ARCHIVE, package.SBOM):
            with self.subTest(artifact=artifact):
                self.write_package([(package.APPHOST, apphost())])
                with (self.root / artifact).open("ab") as file:
                    file.write(b"substitution")
                with self.assertRaisesRegex(ValueError, "independently supplied digest"):
                    self.verify()

    def test_digest_match_without_exact_sbom_source_fails(self):
        self.write_package([(package.APPHOST, apphost())], bom_revision="b" * 40)
        with self.assertRaisesRegex(ValueError, "same reviewed source"):
            self.verify()

    def test_path_aliases_are_rejected_even_with_matching_receipt_and_digests(self):
        for alias in ("../Extra.dll", "C:Extra.dll", "CON.dll", "Thirdlife.UI.exe"):
            with self.subTest(alias=alias):
                self.write_package(sorted([(alias, b"unexpected"), (package.APPHOST, apphost())]))
                with self.assertRaisesRegex(ValueError, "bounded private payload layout"):
                    self.verify()

    def test_altered_receipt_entry_list_fails(self):
        self.write_package([(package.APPHOST, apphost())])
        self.receipt["entries"].append("Extra.dll")
        self.write_receipt()
        with self.assertRaisesRegex(ValueError, "entries differ"):
            self.verify()

    def test_x86_apphost_fails(self):
        self.write_package([(package.APPHOST, apphost(0x14C))])
        with self.assertRaisesRegex(ValueError, "AMD64"):
            self.verify()


if __name__ == "__main__":
    unittest.main()
