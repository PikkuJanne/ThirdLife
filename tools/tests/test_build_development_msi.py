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
        self.input = self.root / "input"
        self.input.mkdir()
        self.output = self.root / "output"
        self.write_input(apphost())

    def write_input(self, content):
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

    def test_rebuilds_identically_and_assigns_new_code_for_changed_payload(self):
        first = installer.build(self.input, self.output, SOURCE, self.archive_digest, self.sbom_digest, "0.3.0")
        second = installer.build(self.input, self.root / "rebuild-with-a-longer-directory",
                                 SOURCE, self.archive_digest, self.sbom_digest, "0.3.0")
        self.assertEqual(first.read_bytes(), second.read_bytes())

        content = apphost()
        self.write_input(content[:-1] + bytes([content[-1] ^ 1]))
        changed = installer.build(self.input, self.root / "changed",
                                  SOURCE, self.archive_digest, self.sbom_digest, "0.3.0")
        self.assertNotEqual(self.read_package_code(first), self.read_package_code(changed))

    def read_package_code(self, msi):
        engine = installer.WindowsInstaller()
        api = engine.api
        api.MsiSummaryInfoGetPropertyW.argtypes = [
            wintypes.UINT, wintypes.UINT, ctypes.POINTER(wintypes.UINT), ctypes.POINTER(ctypes.c_int),
            ctypes.POINTER(wintypes.FILETIME), wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD),
        ]
        api.MsiSummaryInfoGetPropertyW.restype = wintypes.UINT
        with engine.open_handle(api.MsiGetSummaryInformationW, 0, str(msi), 0) as summary:
            value = ctypes.create_unicode_buffer(39)
            size = wintypes.DWORD(len(value))
            installer.checked(api.MsiSummaryInfoGetPropertyW(
                summary, installer.PACKAGE_CODE_PROPERTY, None, None, None, value, ctypes.byref(size)),
                "read MSI package code")
            self.assertRegex(value.value, r"^\{[0-9A-F-]{36}\}$")
            return value.value

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
            for table in ("RemoveFile", "ServiceInstall", "ServiceControl"):
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
                 'Installed OR (VersionNT64 AND Msix64 AND MsiNTProductType = 1 AND TL_NATIVE_BUILD = "26300")'),
                ("SELECT `Action` FROM `CustomAction` WHERE `Action`='ResetNativeBuild'", "ResetNativeBuild"),
                ("SELECT `Type` FROM `CustomAction` WHERE `Action`='ResetNativeBuild'", "51"),
                ("SELECT `Source` FROM `CustomAction` WHERE `Action`='ResetNativeBuild'", "TL_NATIVE_BUILD"),
                ("SELECT `Target` FROM `CustomAction` WHERE `Action`='ResetNativeBuild'", "unavailable"),
                ("SELECT `Property` FROM `AppSearch` WHERE `Signature_`='NativeBuildFromRegistry'", "TL_NATIVE_BUILD"),
                ("SELECT `Root` FROM `RegLocator` WHERE `Signature_`='NativeBuildFromRegistry'", "2"),
                ("SELECT `Key` FROM `RegLocator` WHERE `Signature_`='NativeBuildFromRegistry'",
                 r"SOFTWARE\Microsoft\Windows NT\CurrentVersion"),
                ("SELECT `Name` FROM `RegLocator` WHERE `Signature_`='NativeBuildFromRegistry'", "CurrentBuildNumber"),
                ("SELECT `Type` FROM `RegLocator` WHERE `Signature_`='NativeBuildFromRegistry'", "18"),
                ("SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`='ResetNativeBuild'", "40"),
                ("SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`='AppSearch'", "50"),
                ("SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`='LaunchConditions'", "100"),
                ("SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`='InstallExecute'", "6550"),
                ("SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`='RemoveExistingProducts'", "6575"),
                ("SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`='InstallFinalize'", "6600"),
                ("SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`='ClearRelatedPreview'", "5"),
                ("SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`='ClearNewerPreview'", "10"),
                ("SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`='FindRelatedProducts'", "25"),
                ("SELECT `Target` FROM `CustomAction` WHERE `Action`='ClearRelatedPreview'",
                 "[ThirdLifeEmptyUpgradeList]"),
                ("SELECT `Target` FROM `CustomAction` WHERE `Action`='ClearNewerPreview'",
                 "[ThirdLifeEmptyUpgradeList]"),
                ("SELECT `Target` FROM `CustomAction` WHERE `Action`='UseExactUpgradeFiles'", "amus"),
                ("SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action`='UseExactUpgradeFiles'",
                 "RELATEDPREVIEW AND NOT Installed"),
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

    def test_preflight_binds_exact_installer_and_receipt_to_reviewed_package(self):
        msi = installer.build(self.input, self.output, SOURCE, self.archive_digest, self.sbom_digest, "0.3.0")
        reviewed = sha256(msi.read_bytes())
        package.verify_installer(self.output, SOURCE, self.archive_digest, self.sbom_digest, reviewed, "0.3.0")

        with self.assertRaisesRegex(ValueError, "independently supplied digest"):
            package.verify_installer(self.output, SOURCE, self.archive_digest, self.sbom_digest,
                                     "0" * 64, "0.3.0")
        with self.assertRaisesRegex(ValueError, "receipt does not match"):
            package.verify_installer(self.output, SOURCE, self.archive_digest, self.sbom_digest,
                                     reviewed, "0.3.1")

        receipt_path = self.output / package.INSTALLER_RECEIPT
        receipt = json.loads(receipt_path.read_bytes())
        receipt["app_archive_sha256"] = "0" * 64
        receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "receipt does not match"):
            package.verify_installer(self.output, SOURCE, self.archive_digest, self.sbom_digest,
                                     reviewed, "0.3.0")

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
