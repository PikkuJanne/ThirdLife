import ctypes
from ctypes import wintypes
import json
from pathlib import Path
import sys
import tempfile
import unittest
import uuid
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

    def write_input(self, content, additional_entries=None):
        entries = {package.APPHOST: content}
        if additional_entries:
            entries.update(additional_entries)
        with ZipFile(self.input / package.ARCHIVE, "w") as archive:
            for name in sorted(entries):
                archive.writestr(name, entries[name])
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
            "entries": sorted(entries),
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
        apphost_content = apphost()
        runtime_name = "ThirdLife.UI.runtimeconfig.json"
        runtime_content = b"{}"
        self.write_input(apphost_content, {runtime_name: runtime_content})
        msi = installer.build(self.input, self.output, SOURCE, self.archive_digest, self.sbom_digest, "0.3.0")
        receipts = json.loads((self.output / "installer-receipt.json").read_text(encoding="utf-8"))
        self.assertEqual(sha256(msi.read_bytes()), receipts["msi_sha256"])
        self.assert_owned_resources(msi, {package.APPHOST: len(apphost_content), runtime_name: len(runtime_content)},
                                    "0.3.0")

    def assert_owned_resources(self, msi, expected_file_sizes, version):
        family = uuid.UUID("f9d1d0d5-3c36-471f-9b78-89951d6a9aef")
        upgrade_code = f"{{{str(family).upper()}}}"
        files = [(f"F{index:03}", name, size)
                 for index, (name, size) in enumerate(sorted(expected_file_sizes.items()), 1)]
        engine = installer.WindowsInstaller()
        api = engine.api
        api.MsiViewFetch.argtypes = [wintypes.UINT, ctypes.POINTER(wintypes.UINT)]
        api.MsiViewFetch.restype = wintypes.UINT
        api.MsiRecordGetFieldCount.argtypes = [wintypes.UINT]
        api.MsiRecordGetFieldCount.restype = wintypes.UINT
        api.MsiRecordGetStringW.argtypes = [
            wintypes.UINT, wintypes.UINT, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD),
        ]
        api.MsiRecordGetStringW.restype = wintypes.UINT
        with engine.open_handle(api.MsiOpenDatabaseW, str(msi), None) as database:
            # Allow only the authored tables. This excludes extra cleanup and executable tables.
            tables = self.read_rows(engine, database, "SELECT `Name` FROM `_Tables`")
            self.assertCountEqual([row[0] for row in tables], [
                "AppSearch", "Component", "CustomAction", "Directory", "Feature", "FeatureComponents", "File",
                "InstallExecuteSequence", "LaunchCondition", "Media", "Property", "RegLocator", "Registry",
                "Signature", "Upgrade",
            ])
            for query, expected in [
                ("SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`", [
                    ("TARGETDIR", "", "SourceDir"),
                    ("LocalAppDataFolder", "TARGETDIR", "."),
                    ("PrivatePrograms", "LocalAppDataFolder", "Programs"),
                    ("PrivateInstall", "PrivatePrograms", "TLPRIV~1|ThirdLife Private Preview"),
                ]),
                ("SELECT `Component`, `ComponentId`, `Directory_`, `Attributes`, `Condition`, `KeyPath` "
                 "FROM `Component`", [
                    (file_id, f"{{{str(uuid.uuid5(family, 'file:' + name.lower())).upper()}}}",
                     "PrivateInstall", "260", "", file_id)
                    for file_id, name, _ in files
                ]),
                ("SELECT `File`, `Component_`, `FileName`, `FileSize`, `Attributes`, `Sequence` FROM `File`", [
                    (file_id, file_id, f"{file_id}{Path(name).suffix[:4]}|{name}", str(size), "512", str(index))
                    for index, (file_id, name, size) in enumerate(files, 1)
                ]),
                ("SELECT `Feature`, `Feature_Parent`, `Directory_` FROM `Feature`", [("Desktop", "", "PrivateInstall")]),
                ("SELECT `Feature_`, `Component_` FROM `FeatureComponents`", [
                    ("Desktop", file_id) for file_id, _, _ in files
                ]),
                ("SELECT `DiskId`, `LastSequence`, `DiskPrompt`, `Cabinet`, `VolumeLabel`, `Source` FROM `Media`", [
                    ("1", str(len(files)), "", "#thirdlife.cab", "", ""),
                ]),
                ("SELECT `Registry`, `Root`, `Key`, `Name`, `Value`, `Component_` FROM `Registry`", [
                    (file_id, "1", r"Software\ThirdLife\PrivateInstaller", name, "#1", file_id)
                    for file_id, name, _ in files
                ]),
                ("SELECT `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`, `Remove`, "
                 "`ActionProperty` FROM `Upgrade`", [
                    (upgrade_code, "", version, "", "0", "", "RELATEDPREVIEW"),
                    (upgrade_code, version, "", "", "2", "", "NEWERPREVIEW"),
                ]),
                ("SELECT `Property`, `Signature_` FROM `AppSearch`", [("TL_NATIVE_BUILD", "NativeBuildFromRegistry")]),
                ("SELECT `Signature_`, `Root`, `Key`, `Name`, `Type` FROM `RegLocator`", [
                    ("NativeBuildFromRegistry", "2", r"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                     "CurrentBuildNumber", "18"),
                ]),
                ("SELECT `Signature` FROM `Signature`", []),
                ("SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction`", [
                    ("ResetNativeBuild", "51", "TL_NATIVE_BUILD", "unavailable"),
                    ("ClearRelatedPreview", "51", "RELATEDPREVIEW", "[ThirdLifeEmptyUpgradeList]"),
                    ("ClearNewerPreview", "51", "NEWERPREVIEW", "[ThirdLifeEmptyUpgradeList]"),
                    ("UseExactUpgradeFiles", "51", "REINSTALLMODE", "amus"),
                ]),
                ("SELECT `Condition` FROM `LaunchCondition`", [
                    ('Installed OR (VersionNT64 AND Msix64 AND MsiNTProductType = 1 AND TL_NATIVE_BUILD = "26300")',),
                    ("(NOT ALLUSERS) AND (NOT NEWERPREVIEW)",),
                ]),
                ("SELECT `Property`, `Value` FROM `Property`", [
                    ("ProductName", "ThirdLife Setup Core (Private Preview)"),
                    ("Manufacturer", "ThirdLife"),
                    ("ProductCode", f"{{{str(uuid.uuid5(family, 'version:' + version)).upper()}}}"),
                    ("UpgradeCode", upgrade_code),
                    ("ProductLanguage", "1033"),
                    ("ProductVersion", version),
                    ("REBOOT", "ReallySuppress"),
                    ("SecureCustomProperties", "RELATEDPREVIEW;NEWERPREVIEW"),
                ]),
                ("SELECT `Action`, `Condition`, `Sequence` FROM `InstallExecuteSequence`", [
                    ("ClearRelatedPreview", "", "5"), ("ClearNewerPreview", "", "10"),
                    ("FindRelatedProducts", "", "25"), ("ResetNativeBuild", "", "40"),
                    ("AppSearch", "", "50"), ("LaunchConditions", "", "100"),
                    ("UseExactUpgradeFiles", "RELATEDPREVIEW AND NOT Installed", "110"),
                    ("CostInitialize", "", "800"), ("FileCost", "", "900"), ("CostFinalize", "", "1000"),
                    ("InstallValidate", "", "1400"), ("InstallInitialize", "", "1500"),
                    ("ProcessComponents", "", "1600"), ("UnpublishFeatures", "", "1800"),
                    ("RemoveRegistryValues", "", "2600"), ("RemoveFiles", "", "3500"),
                    ("InstallFiles", "", "4000"), ("WriteRegistryValues", "", "5000"),
                    ("RegisterUser", "", "6000"), ("RegisterProduct", "", "6100"),
                    ("PublishFeatures", "", "6300"), ("PublishProduct", "", "6400"),
                    ("InstallExecute", "", "6550"), ("RemoveExistingProducts", "", "6575"),
                    ("InstallFinalize", "", "6600"),
                ]),
                ("SELECT `Name` FROM `_Streams` WHERE `Name`='thirdlife.cab'", [("thirdlife.cab",)]),
            ]:
                with self.subTest(query=query):
                    self.assertCountEqual(self.read_rows(engine, database, query), expected)

    def read_rows(self, engine, database, query):
        api = engine.api
        rows = []
        with engine.open_handle(api.MsiDatabaseOpenViewW, database, query) as view:
            installer.checked(api.MsiViewExecute(view, 0), "run MSI read query")
            while True:
                row = wintypes.UINT()
                result = api.MsiViewFetch(view, ctypes.byref(row))
                if result == 259:  # ERROR_NO_MORE_ITEMS
                    return rows
                installer.checked(result, "fetch MSI row")
                try:
                    fields = []
                    for index in range(1, api.MsiRecordGetFieldCount(row) + 1):
                        value = ctypes.create_unicode_buffer(1024)
                        size = wintypes.DWORD(len(value))
                        installer.checked(api.MsiRecordGetStringW(row, index, value, ctypes.byref(size)), "read MSI cell")
                        fields.append(value.value)
                    rows.append(tuple(fields))
                finally:
                    installer.checked(api.MsiCloseHandle(row), "close MSI row")

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
