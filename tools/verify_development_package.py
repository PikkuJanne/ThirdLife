"""Verify a private ThirdLife development package and its optional per-user installer."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import ntpath
from pathlib import Path
import re
import struct
import zipfile
import zlib


RECEIPT = "development-package-manifest.json"
ARCHIVE = "thirdlife-development-preview-x64.zip"
APPHOST = "ThirdLife.UI.exe"
SBOM = "thirdlife-development-preview.cdx.json"
INSTALLER = "ThirdLife-SetupCore-Private-x64.msi"
INSTALLER_RECEIPT = "installer-receipt.json"
MAX_ARCHIVE = 32 * 1024 * 1024
MAX_PAYLOAD = 128 * 1024 * 1024
MAX_INSTALLER = MAX_PAYLOAD + 8 * 1024 * 1024
MAX_ENTRIES = 128
MEMBER_NAME = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,95}\.(?:dll|exe|json)")


def read_bounded(path: Path, limit: int) -> bytes:
    with path.open("rb") as stream:
        content = stream.read(limit + 1)
    if not 0 < len(content) <= limit:
        raise ValueError(f"{path.name} is empty or exceeds its size limit.")
    return content


def unique_properties(pairs: list[tuple[str, object]]) -> dict[str, object]:
    properties: dict[str, object] = {}
    for name, value in pairs:
        if name in properties:
            raise ValueError("Package metadata repeats a JSON property.")
        properties[name] = value
    return properties


def read_json(content: bytes) -> dict[str, object]:
    result = json.loads(content, object_pairs_hook=unique_properties)
    if not isinstance(result, dict):
        raise ValueError("Package metadata must be a JSON object.")
    return result


def verify_payload(content: bytes, expected_entries: object, apphost_sha256: object) -> int:
    with zipfile.ZipFile(io.BytesIO(content)) as archive:
        members = archive.infolist()
        names = [member.orig_filename for member in members]
        if (
            not 1 <= len(names) <= MAX_ENTRIES
            or names != sorted(names)
            or len(set(name.lower() for name in names)) != len(names)
            or any(not MEMBER_NAME.fullmatch(name) or ntpath.isreserved(name) for name in names)
            or any(name.lower().endswith(".exe") and name != APPHOST for name in names)
            or any((member.external_attr >> 16) & 0o170000 not in (0, 0o100000) for member in members)
            or any(not 0 < member.file_size <= MAX_ARCHIVE for member in members)
            or sum(member.file_size for member in members) > MAX_PAYLOAD
        ):
            raise ValueError("The archive does not match the bounded private payload layout.")
        if names != expected_entries or APPHOST not in names:
            raise ValueError("The archive entries differ from the package receipt.")
        if archive.testzip() is not None:
            raise ValueError("An archive entry failed its integrity check.")

        apphost = archive.read(APPHOST)
        if hashlib.sha256(apphost).hexdigest() != apphost_sha256:
            raise ValueError("The apphost digest does not match the receipt.")
        if len(apphost) < 64 or not apphost.startswith(b"MZ"):
            raise ValueError("The apphost has no Windows executable header.")
        pe_offset = struct.unpack_from("<I", apphost, 0x3C)[0]
        if pe_offset < 64 or apphost[pe_offset:pe_offset + 6] != b"PE\0\0\x64\x86":
            raise ValueError("The apphost is not a Windows AMD64 executable.")
        return len(names)


def verify(directory: Path, source: str, archive_sha256: str, sbom_sha256: str) -> int:
    for value, length in ((source, 40), (archive_sha256, 64), (sbom_sha256, 64)):
        if not re.fullmatch(f"[0-9a-f]{{{length}}}", value):
            raise ValueError("The reviewed source and digests must be lowercase hexadecimal values.")

    archive = read_bounded(directory / ARCHIVE, MAX_ARCHIVE)
    sbom = read_bounded(directory / SBOM, 1024 * 1024)
    if hashlib.sha256(archive).hexdigest() != archive_sha256 or hashlib.sha256(sbom).hexdigest() != sbom_sha256:
        raise ValueError("The archive or SBOM does not match the independently supplied digest.")

    receipt = read_json(read_bounded(directory / RECEIPT, 32 * 1024))
    required = {
        "classification": "private_unsigned_development",
        "source": source,
        "archive": ARCHIVE,
        "archive_sha256": archive_sha256,
        "archive_bytes": len(archive),
        "apphost": APPHOST,
        "sbom": SBOM,
        "sbom_sha256": sbom_sha256,
    }
    if any(receipt.get(name) != value for name, value in required.items()):
        raise ValueError("The receipt does not match the reviewed private development package.")

    bom = read_json(sbom)
    metadata = bom.get("metadata")
    properties = metadata.get("properties") if isinstance(metadata, dict) else None
    if bom.get("bomFormat") != "CycloneDX" or bom.get("specVersion") != "1.6" or not isinstance(properties, list):
        raise ValueError("The package requires a source-bound CycloneDX 1.6 SBOM.")
    revisions = [
        item.get("value")
        for item in properties
        if isinstance(item, dict) and item.get("name") == "thirdlife:source-revision"
    ]
    if revisions != [source]:
        raise ValueError("The SBOM does not identify the same reviewed source.")
    return verify_payload(archive, receipt.get("entries"), receipt.get("apphost_sha256"))


def require_installer_version(version: str) -> None:
    match = re.fullmatch(r"([0-9]{1,3})\.([0-9]{1,3})\.([0-9]{1,5})", version)
    if not match or any(str(int(value)) != value or int(value) > limit
                        for value, limit in zip(match.groups(), (255, 255, 65535), strict=True)):
        raise ValueError("Supply a three-part Windows Installer product version.")


def verify_installer(directory: Path, source: str, archive_sha256: str, sbom_sha256: str,
                     installer_sha256: str, version: str) -> None:
    require_installer_version(version)
    if not re.fullmatch(r"[0-9a-f]{64}", installer_sha256):
        raise ValueError("The reviewed installer digest must be a lowercase hexadecimal value.")

    installer = read_bounded(directory / INSTALLER, MAX_INSTALLER)
    if hashlib.sha256(installer).hexdigest() != installer_sha256:
        raise ValueError("The installer does not match the independently supplied digest.")
    if not installer.startswith(b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1"):
        raise ValueError("The installer does not have a Windows compound-file header.")

    receipt = read_json(read_bounded(directory / INSTALLER_RECEIPT, 32 * 1024))
    expected = {
        "classification": "private_unsigned_development_installer",
        "source": source,
        "app_archive_sha256": archive_sha256,
        "sbom_sha256": sbom_sha256,
        "version": version,
        "msi_sha256": installer_sha256,
    }
    if any(receipt.get(name) != value for name, value in expected.items()):
        raise ValueError("The installer receipt does not match the reviewed version and package.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--source", required=True, help="Reviewed full Git revision from a separate record")
    parser.add_argument("--archive-sha256", required=True, help="Reviewed archive digest from a separate record")
    parser.add_argument("--sbom-sha256", required=True, help="Reviewed SBOM digest from a separate record")
    parser.add_argument("--installer-directory", type=Path, help="Directory holding the private MSI and installer receipt")
    parser.add_argument("--installer-sha256", help="Reviewed installer digest from a separate record")
    parser.add_argument("--installer-version", help="Reviewed three-part Windows Installer product version")
    args = parser.parse_args()
    installer_inputs = (args.installer_directory, args.installer_sha256, args.installer_version)
    if any(value is not None for value in installer_inputs) and any(value is None for value in installer_inputs):
        parser.error("Supply --installer-directory, --installer-sha256 and --installer-version together.")
    try:
        count = verify(args.directory, args.source, args.archive_sha256, args.sbom_sha256)
        if args.installer_directory is not None:
            verify_installer(args.installer_directory, args.source, args.archive_sha256, args.sbom_sha256,
                             args.installer_sha256, args.installer_version)
    except (OSError, ValueError, RuntimeError, zipfile.BadZipFile, zlib.error) as error:
        parser.exit(1, f"Package verification failed: {error}\n")
    print(f"OK: private development package for {args.source}, {count} checked entries, AMD64 apphost")
    if args.installer_directory is not None:
        print(f"OK: private MSI {args.installer_version} matches the reviewed installer digest and package receipt")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
