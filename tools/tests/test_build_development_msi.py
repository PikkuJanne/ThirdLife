import ctypes
from ctypes import wintypes
import json
from pathlib import Path
import sys
import tempfile
import unittest
from zipfile import ZipFile

from tools import build_development_msi as installer
from tools import verify_development_package as package
from tools.tests.test_verify_development_package import SOURCE, apphost, sha256


@unittest.skipUnless(sys.platform == "win32", "Windows Installer authoring requires Windows")
class DevelopmentMsiTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(self.enterContext(tempfile.TemporaryDirectory()))
        content = apphost()
        self.input = self.root / "input"
        self.input.mkdir()
        with ZipFile(self.input / package.ARCHIVE, "w") as archive:
            archive.writestr(package.APPHOST, content)
        artifact = (self.input / package.ARCHIVE).read_bytes()
        sbom = json.dumps({
            "bomFormat": "CycloneDX",
            "specVersion": "1.6",
            "metadata": {"properties": [{"name": "thirdlife:source-revision", "value": SOURCE}]},
        }).encode("utf-8")
        (self.input / package.SBOM).write_bytes(sbom)
        self.archive_digest = sha256(artifact)
        self.sbom_digest = sha256(sbom)
        (self.input / package.RECEIPT).write_text(json.dumps({
            "classification": "private_unsigned_development",
            "source": SOURCE,
            "archive": package.ARCHIVE,
            "archive_sha256": self.archive_digest,
            "archive_bytes": len(artifact),
            "apphost": package.APPHOST,
            "apphost_sha256": sha256(content),
            "sbom": package.SBOM,
            "sbom_sha256": self.sbom_digest,
            "entries": [package.APPHOST],
        }), encoding="utf-8")
        self.output = self.root / "output"

    def test_writes_a_native_readable_package_with_only_owned_resources(self):
        msi = installer.build(self.input, self.output, SOURCE, self.archive_digest, self.sbom_digest, "0.3.0")
        receipts = json.loads((self.output / "installer-receipt.json").read_text(encoding="utf-8"))
        self.assertEqual(sha256(msi.read_bytes()), receipts["msi_sha256"])

        engine = installer.WindowsInstaller()
        api = engine.api
        api.MsiDatabaseIsTablePersistentW.argtypes = [wintypes.UINT, wintypes.LPCWSTR]
        api.MsiDatabaseIsTablePersistentW.restype = ctypes.c_int
        api.MsiViewFetch.argtypes = [wintypes.UINT, ctypes.POINTER(wintypes.UINT)]
        api.MsiViewFetch.restype = wintypes.UINT
        api.MsiRecordGetStringW.argtypes = [
            wintypes.UINT, wintypes.UINT, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD),
        ]
        api.MsiRecordGetStringW.restype = wintypes.UINT
        with engine.open_handle(api.MsiOpenDatabaseW, str(msi), None) as database:
            for table in ("CustomAction", "RemoveFile", "ServiceInstall", "ServiceControl"):
                self.assertEqual(2, api.MsiDatabaseIsTablePersistentW(database, table))
            for query, expected in [
                ("SELECT `FileName` FROM `File`", f"F001.exe|{package.APPHOST}"),
                ("SELECT `Key` FROM `Registry`", installer.REGISTRY_KEY),
                ("SELECT `DefaultDir` FROM `Directory` WHERE `Directory`='PrivateInstall'",
                 "TLPRIV~1|ThirdLife Private Preview"),
                ("SELECT `Name` FROM `_Streams` WHERE `Name`='thirdlife.cab'", installer.CABINET),
                ("SELECT `Value` FROM `Property` WHERE `Property`='REBOOT'", "ReallySuppress"),
                ("SELECT `Condition` FROM `LaunchCondition` WHERE `Description` = "
                 "'This preview requires Windows 11 client build 26300 on native x64.'",
                 "Installed OR (VersionNT64 = 1000 AND Msix64 AND WindowsBuild = 26300 AND MsiNTProductType = 1)"),
            ]:
                with self.subTest(query=query):
                    self.assertEqual(expected, self.read_cell(engine, database, query))

    def read_cell(self, engine, database, query):
        api = engine.api
        with engine.open_handle(api.MsiDatabaseOpenViewW, database, query) as view:
            installer.checked(api.MsiViewExecute(view, 0), "run MSI read query")
            with engine.open_handle(api.MsiViewFetch, view) as row:
                value = ctypes.create_unicode_buffer(1024)
                size = wintypes.DWORD(len(value))
                installer.checked(api.MsiRecordGetStringW(row, 1, value, ctypes.byref(size)), "read MSI cell")
            second = wintypes.UINT()
            self.assertEqual(259, api.MsiViewFetch(view, ctypes.byref(second)))
            return value.value

    def test_refuses_unreviewed_input_before_creating_an_output_directory(self):
        with self.assertRaisesRegex(ValueError, "independently supplied digest"):
            installer.build(self.input, self.output, SOURCE, "0" * 64, self.sbom_digest, "0.3.0")
        self.assertFalse(self.output.exists())

    def test_msi_version_must_be_canonical_and_in_range(self):
        for version in ("0.03.0", "0.3.0-dev", "0.0.65536"):
            with self.subTest(version=version):
                with self.assertRaisesRegex(ValueError, "three-part"):
                    installer.build(self.input, self.output, SOURCE, self.archive_digest, self.sbom_digest, version)
                self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
