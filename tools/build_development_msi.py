"""Build an unsigned, per-user ThirdLife prototype with Windows Installer tables."""

from __future__ import annotations

import argparse
from contextlib import contextmanager
import ctypes
from ctypes import wintypes
from datetime import datetime
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import uuid
import zipfile

from tools.verify_development_package import ARCHIVE, MAX_ARCHIVE, read_bounded, verify


UPGRADE_NAMESPACE = uuid.UUID("f9d1d0d5-3c36-471f-9b78-89951d6a9aef")
PRODUCT_NAME = "ThirdLife Setup Core (Private Preview)"
CABINET = "thirdlife.cab"
CABINET_MEMBER_DATE = datetime(2000, 1, 1)
PACKAGE_CODE_PROPERTY = 9
UNASSIGNED_PACKAGE_CODE = "{00000000-0000-0000-0000-000000000000}"
INSTALL_DIR = "PrivateInstall"
REGISTRY_KEY = r"Software\ThirdLife\PrivateInstaller"
# Windows Installer can expose compatibility-version values for Windows 11.
# Use the native machine registry build and the installer-owned client and x64 indicators.
BUILD_PROPERTY = "TL_NATIVE_BUILD"
BUILD_REGISTRY_KEY = r"SOFTWARE\Microsoft\Windows NT\CurrentVersion"
REQUIRED_PLATFORM = f'VersionNT64 AND Msix64 AND MsiNTProductType = 1 AND {BUILD_PROPERTY} = "26300"'


def checked(result: int, action: str) -> None:
    if result != 0:
        raise OSError(f"{action} failed with Windows Installer result {result}.")


def identifier(value: uuid.UUID) -> str:
    return "{" + str(value).upper() + "}"


def table(path: Path, name: str, columns: str, types: str, keys: str, rows: list[tuple]) -> str:
    filename = f"{name}.idt"
    lines = [columns, types, f"{name}\t{keys}"]
    lines.extend("\t".join(str(cell) for cell in row) for row in rows)
    (path / filename).write_bytes(("\r\n".join(lines) + "\r\n").encode("ascii"))
    return filename


def author_tables(path: Path, payload: dict[str, bytes], version: str) -> list[str]:
    product_code = identifier(uuid.uuid5(UPGRADE_NAMESPACE, "version:" + version))
    upgrade_code = identifier(UPGRADE_NAMESPACE)
    records = [(f"F{index:03}", name, content) for index, (name, content) in enumerate(payload.items(), 1)]
    return [
        table(path, "Property", "Property\tValue", "s72\tl0", "Property", [
            ("ProductName", PRODUCT_NAME), ("Manufacturer", "ThirdLife"),
            ("ProductCode", product_code), ("UpgradeCode", upgrade_code),
            ("ProductLanguage", 1033), ("ProductVersion", version),
            ("REBOOT", "ReallySuppress"),
            ("SecureCustomProperties", "RELATEDPREVIEW;NEWERPREVIEW"),
        ]),
        table(path, "Directory", "Directory\tDirectory_Parent\tDefaultDir", "s72\tS72\tl255", "Directory", [
            ("TARGETDIR", "", "SourceDir"), ("LocalAppDataFolder", "TARGETDIR", "."),
            ("PrivatePrograms", "LocalAppDataFolder", "Programs"),
            (INSTALL_DIR, "PrivatePrograms", "TLPRIV~1|ThirdLife Private Preview"),
        ]),
        table(path, "Component", "Component\tComponentId\tDirectory_\tAttributes\tCondition\tKeyPath",
              "s72\tS38\ts72\ti2\tS255\tS72", "Component", [
                  (file_id, identifier(uuid.uuid5(UPGRADE_NAMESPACE, "file:" + name.lower())),
                   INSTALL_DIR, 260, "", file_id)
                  for file_id, name, _ in records
              ]),
        table(path, "Registry", "Registry\tRoot\tKey\tName\tValue\tComponent_",
              "s72\ti2\tl255\tL255\tL0\ts72", "Registry", [
                  (file_id, 1, REGISTRY_KEY, name, "#1", file_id) for file_id, name, _ in records
              ]),
        table(path, "File", "File\tComponent_\tFileName\tFileSize\tVersion\tLanguage\tAttributes\tSequence",
              "s72\ts72\tl255\ti4\tS72\tS20\tI2\ti2", "File", [
                  (file_id, file_id, f"{file_id}{Path(name).suffix[:4]}|{name}", len(content), "", "", 512, index)
                  for index, (file_id, name, content) in enumerate(records, 1)
              ]),
        table(path, "Media", "DiskId\tLastSequence\tDiskPrompt\tCabinet\tVolumeLabel\tSource",
              "i2\ti2\tL64\tS255\tS32\tS72", "DiskId", [(1, len(records), "", f"#{CABINET}", "", "")]),
        table(path, "Feature", "Feature\tFeature_Parent\tTitle\tDescription\tDisplay\tLevel\tDirectory_\tAttributes",
              "s38\tS38\tL64\tL255\tI2\ti2\tS72\ti2", "Feature",
              [("Desktop", "", "ThirdLife desktop", "", 1, 1, INSTALL_DIR, 0)]),
        table(path, "FeatureComponents", "Feature_\tComponent_", "s38\ts72", "Feature_\tComponent_", [
            ("Desktop", file_id) for file_id, _, _ in records
        ]),
        table(path, "Signature", "Signature\tFileName\tMinVersion\tMaxVersion\tMinSize\tMaxSize\tMinDate\tMaxDate\tLanguages",
              "s72\ts255\tS20\tS20\tI4\tI4\tI4\tI4\tS255", "Signature", []),
        table(path, "AppSearch", "Property\tSignature_", "s72\ts72", "Property\tSignature_", [
            (BUILD_PROPERTY, "NativeBuildFromRegistry"),
        ]),
        table(path, "RegLocator", "Signature_\tRoot\tKey\tName\tType", "s72\ti2\ts255\tS255\tI2", "Signature_", [
            ("NativeBuildFromRegistry", 2, BUILD_REGISTRY_KEY, "CurrentBuildNumber", 18),
        ]),
        table(path, "CustomAction", "Action\tType\tSource\tTarget", "s72\ti2\tS72\tL255", "Action", [
            # A fixed property assignment clears any externally supplied value before the registry read.
            ("ResetNativeBuild", 51, BUILD_PROPERTY, "unavailable"),
        ]),
        table(path, "LaunchCondition", "Condition\tDescription", "s255\tl255", "Condition", [
            (f"Installed OR ({REQUIRED_PLATFORM})", "This preview requires Windows 11 client build 26300 on native x64."),
            ("(NOT ALLUSERS) AND (NOT NEWERPREVIEW)", "This private preview installs for one user and requires a current version."),
        ]),
        table(path, "Upgrade", "UpgradeCode\tVersionMin\tVersionMax\tLanguage\tAttributes\tRemove\tActionProperty",
              "s38\tS20\tS20\tS255\ti4\tS255\ts72",
              "UpgradeCode\tVersionMin\tVersionMax\tLanguage\tAttributes", [
                  (upgrade_code, "", version, "", 0, "", "RELATEDPREVIEW"),
                  (upgrade_code, version, "", "", 2, "", "NEWERPREVIEW"),
              ]),
        table(path, "InstallExecuteSequence", "Action\tCondition\tSequence", "s72\tS255\ti2", "Action", [
            ("FindRelatedProducts", "", 25), ("ResetNativeBuild", "", 40),
            ("AppSearch", "", 50), ("LaunchConditions", "", 100),
            ("CostInitialize", "", 800), ("FileCost", "", 900), ("CostFinalize", "", 1000),
            ("InstallValidate", "", 1400), ("InstallInitialize", "", 1500),
            ("RemoveExistingProducts", "", 1550), ("ProcessComponents", "", 1600),
            ("UnpublishFeatures", "", 1800), ("RemoveRegistryValues", "", 2600),
            ("RemoveFiles", "", 3500), ("InstallFiles", "", 4000), ("WriteRegistryValues", "", 5000),
            ("RegisterUser", "", 6000), ("RegisterProduct", "", 6100),
            ("PublishFeatures", "", 6300), ("PublishProduct", "", 6400), ("InstallFinalize", "", 6600),
        ]),
    ]


def clear_storage_timestamp(path: Path) -> None:
    """Clear the generated compound file's embedded root modification time."""
    ole = ctypes.WinDLL("ole32", winmode=0x800)
    ole.StgOpenStorage.argtypes = [
        wintypes.LPCWSTR, ctypes.c_void_p, wintypes.DWORD, ctypes.c_void_p,
        wintypes.DWORD, ctypes.POINTER(ctypes.c_void_p),
    ]
    ole.StgOpenStorage.restype = wintypes.LONG
    storage = ctypes.c_void_p()
    # STGM_DIRECT | STGM_READWRITE | STGM_SHARE_EXCLUSIVE opens our generated file.
    checked(ole.StgOpenStorage(str(path), None, 0x12, None, 0, ctypes.byref(storage)), "open MSI storage")
    vtable = ctypes.cast(ctypes.cast(storage, ctypes.POINTER(ctypes.c_void_p))[0],
                         ctypes.POINTER(ctypes.c_void_p))
    # IUnknown::Release and IStorage::Commit / SetElementTimes in interface order.
    release = ctypes.WINFUNCTYPE(wintypes.ULONG, ctypes.c_void_p)(vtable[2])
    commit = ctypes.WINFUNCTYPE(wintypes.LONG, ctypes.c_void_p, wintypes.DWORD)(vtable[9])
    filetime_pointer = ctypes.POINTER(wintypes.FILETIME)
    set_times = ctypes.WINFUNCTYPE(wintypes.LONG, ctypes.c_void_p, wintypes.LPCWSTR,
                                  filetime_pointer, filetime_pointer, filetime_pointer)(vtable[14])
    try:
        empty = wintypes.FILETIME()
        checked(set_times(storage, None, None, None, ctypes.byref(empty)), "clear MSI storage timestamp")
        checked(commit(storage, 0), "commit MSI storage")
    finally:
        release(storage)


class WindowsInstaller:
    def __init__(self):
        msi = ctypes.WinDLL("msi", winmode=0x800)
        handle = wintypes.UINT
        wide = wintypes.LPCWSTR
        out = ctypes.POINTER(handle)
        signatures = {
            "MsiCloseHandle": [handle],
            "MsiOpenDatabaseW": [wide, ctypes.c_void_p, out],
            "MsiDatabaseImportW": [handle, wide, wide],
            "MsiDatabaseOpenViewW": [handle, wide, out],
            "MsiViewExecute": [handle, handle],
            "MsiRecordSetStringW": [handle, handle, wide],
            "MsiRecordSetStreamW": [handle, handle, wide],
            "MsiGetSummaryInformationW": [handle, wide, handle, out],
            "MsiSummaryInfoSetPropertyW": [handle, handle, handle, ctypes.c_int, ctypes.c_void_p, wide],
            "MsiSummaryInfoPersist": [handle],
            "MsiDatabaseCommit": [handle],
            "MsiCreateRecord": [handle],
            "MsiVerifyPackageW": [wide],
        }
        for name, arguments in signatures.items():
            native = getattr(msi, name)
            native.argtypes = arguments
            native.restype = handle
        self.api = msi

    @contextmanager
    def open_handle(self, function, *arguments):
        handle = wintypes.UINT()
        checked(function(*arguments, ctypes.byref(handle)), function.__name__)
        try:
            yield handle
        finally:
            checked(self.api.MsiCloseHandle(handle), "close MSI handle")

    def assemble(self, destination: Path, staging: Path, tables: list[str]) -> None:
        with self.open_handle(self.api.MsiOpenDatabaseW, str(destination), 3) as database:
            for name in tables:
                checked(self.api.MsiDatabaseImportW(database, str(staging), name), f"import {name}")
            with self.open_handle(self.api.MsiDatabaseOpenViewW, database,
                                  "INSERT INTO `_Streams` (`Name`, `Data`) VALUES (?, ?)") as view:
                record = self.api.MsiCreateRecord(2)
                if not record:
                    raise OSError("Could not allocate an MSI cabinet record.")
                try:
                    checked(self.api.MsiRecordSetStringW(record, 1, CABINET), "set cabinet name")
                    checked(self.api.MsiRecordSetStreamW(record, 2, str(staging / CABINET)), "attach cabinet")
                    checked(self.api.MsiViewExecute(view, record), "insert cabinet")
                finally:
                    checked(self.api.MsiCloseHandle(record), "close cabinet record")
            with self.open_handle(self.api.MsiGetSummaryInformationW, database, None, 12) as summary:
                for property_id, value in {
                    2: "Installation Database", 3: PRODUCT_NAME, 4: "ThirdLife",
                    7: "x64;1033", PACKAGE_CODE_PROPERTY: UNASSIGNED_PACKAGE_CODE,
                }.items():
                    checked(self.api.MsiSummaryInfoSetPropertyW(summary, property_id, 30, 0, None, value),
                            "set MSI text metadata")
                for property_id, value in {1: 1252, 14: 500, 15: 10, 19: 2}.items():
                    data_type = 2 if property_id == 1 else 3
                    checked(self.api.MsiSummaryInfoSetPropertyW(summary, property_id, data_type, value, None, None),
                            "set MSI numeric metadata")
                checked(self.api.MsiSummaryInfoPersist(summary), "write MSI metadata")
            checked(self.api.MsiDatabaseCommit(database), "commit MSI")
        # Hash the complete rendered database and cabinet with the package code blanked.
        # A changed payload or installer table then receives a distinct package code.
        clear_storage_timestamp(destination)
        with destination.open("rb") as handle:
            digest = hashlib.file_digest(handle, "sha256").hexdigest()
        package_code = identifier(uuid.uuid5(UPGRADE_NAMESPACE, "package:" + digest))
        with self.open_handle(self.api.MsiOpenDatabaseW, str(destination), 2) as database:
            with self.open_handle(self.api.MsiGetSummaryInformationW, database, None, 1) as summary:
                checked(self.api.MsiSummaryInfoSetPropertyW(
                    summary, PACKAGE_CODE_PROPERTY, 30, 0, None, package_code), "set MSI package code")
                checked(self.api.MsiSummaryInfoPersist(summary), "persist MSI package code")
            checked(self.api.MsiDatabaseCommit(database), "commit MSI package code")
        clear_storage_timestamp(destination)
        checked(self.api.MsiVerifyPackageW(str(destination)), "verify the assembled MSI")


def make_cabinet(staging: Path, payload: dict[str, bytes]) -> None:
    members = staging / "payload"
    members.mkdir()
    spec = [
        ".OPTION EXPLICIT", f".Set CabinetNameTemplate={CABINET}", ".Set DiskDirectoryTemplate=.",
        ".Set MaxDiskSize=0", ".Set Cabinet=ON", ".Set Compress=ON", ".Set CompressionType=MSZIP",
    ]
    member_timestamp = CABINET_MEMBER_DATE.timestamp()
    for index, (name, content) in enumerate(payload.items(), 1):
        member = members / name
        member.write_bytes(content)
        os.utime(member, (member_timestamp, member_timestamp))
        spec.append(f'"payload\\{name}" F{index:03}')
    (staging / "payload.ddf").write_bytes(("\r\n".join(spec) + "\r\n").encode("ascii"))

    kernel = ctypes.WinDLL("kernel32", winmode=0x800)
    system = ctypes.create_unicode_buffer(1024)
    count = kernel.GetSystemDirectoryW(system, len(system))
    if not 0 < count < len(system):
        raise OSError("Could not identify the Windows system directory.")
    compiler = Path(system.value) / "makecab.exe"
    result = subprocess.run([str(compiler), "/F", "payload.ddf"], cwd=staging,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
    if result.returncode:
        details = result.stdout[-1000:].decode(errors="replace").strip()
        raise OSError(f"The Windows cabinet compiler failed ({result.returncode}). {details}")


def build(package: Path, output: Path, source: str, archive_sha256: str, sbom_sha256: str,
          version: str) -> Path:
    match = re.fullmatch(r"([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,5})", version)
    if not match or any(str(int(value)) != value or int(value) > limit
                        for value, limit in zip(match.groups(), (255, 255, 65535), strict=True)):
        raise ValueError("Supply a three-part Windows Installer product version.")
    verify(package, source, archive_sha256, sbom_sha256)
    snapshot = read_bounded(package / ARCHIVE, MAX_ARCHIVE)
    if hashlib.sha256(snapshot).hexdigest() != archive_sha256:
        raise ValueError("The verified archive changed before MSI authoring.")
    with zipfile.ZipFile(io.BytesIO(snapshot)) as archive:
        payload = {name: archive.read(name) for name in archive.namelist()}

    output.mkdir(parents=True, exist_ok=False)
    staging = output / "authoring"
    staging.mkdir()
    tables = author_tables(staging, payload, version)
    make_cabinet(staging, payload)
    destination = output / "ThirdLife-SetupCore-Private-x64.msi"
    staged_msi = staging / destination.name
    WindowsInstaller().assemble(staged_msi, staging, tables)
    staged_msi.replace(destination)
    receipt = {
        "classification": "private_unsigned_development_installer",
        "source": source, "app_archive_sha256": archive_sha256, "sbom_sha256": sbom_sha256,
        "version": version, "msi_sha256": hashlib.sha256(destination.read_bytes()).hexdigest(),
    }
    (output / "installer-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return destination


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--source", required=True)
    parser.add_argument("--archive-sha256", required=True)
    parser.add_argument("--sbom-sha256", required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    try:
        generated = build(args.package, args.output.resolve(), args.source, args.archive_sha256,
                          args.sbom_sha256, args.version)
    except (OSError, ValueError, RuntimeError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Private MSI authoring failed: {error}\n")
    print(generated)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
