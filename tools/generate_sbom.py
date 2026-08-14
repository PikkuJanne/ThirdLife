#!/usr/bin/env python3
"""Generate ThirdLife's deterministic, offline CycloneDX dependency SBOM."""

from __future__ import annotations

import argparse
import base64
import binascii
import csv
import hashlib
import io
import json
import os
import re
import sys
import tempfile
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable
from urllib.parse import quote, urlsplit


CYCLONEDX_SPEC_VERSION = "1.7"
DEVELOPMENT_VERSION = "0.0.0-development"
DEVELOPMENT_SCOPE = "source-and-development"
MAX_FILE_BYTES = 4 * 1024 * 1024
MAX_TOTAL_INPUT_BYTES = 64 * 1024 * 1024
MAX_PROJECTS = 512
MAX_COMPONENTS = 4096
MAX_CSV_ROWS = 4096
MAX_SCALAR_CHARS = 4096

FIXED_INPUTS = (
    ".github/workflows/verify.yml",
    "Directory.Build.props",
    "Directory.Packages.props",
    "NuGet.Config",
    "ThirdLife.sln",
    "docs/supply-chain/license-matrix.csv",
    "eng/generate-sbom.ps1",
    "global.json",
    "tools/generate_sbom.py",
    "tools/requirements.txt",
)

MATRIX_COLUMNS = (
    "component_id",
    "ecosystem",
    "name",
    "version",
    "dependency_class",
    "directness",
    "internal_owner",
    "upstream_owner",
    "source_uri",
    "provenance_reference",
    "content_hash_algorithm",
    "content_hash",
    "purpose",
    "license_expression",
    "license_evidence",
    "installation_rights",
    "redistribution_rights",
    "bundled_in_release",
    "notice_requirements",
    "review_status",
    "review_notes",
)

ECOSYSTEMS = frozenset({"nuget", "pypi", "github_action", "toolchain"})
DEPENDENCY_CLASSES = frozenset(
    {"runtime", "build_only", "test_only", "catalog_application"}
)
DIRECTNESS_VALUES = frozenset({"direct", "transitive", "platform"})
RIGHTS_VALUES = frozenset(
    {"permitted", "prohibited", "pending_human_review", "not_applicable"}
)
BUNDLED_VALUES = frozenset({"yes", "no", "pending"})
REVIEW_VALUES = frozenset({"pending", "approved", "rejected"})
HASH_ALGORITHMS = frozenset({"SHA-256", "SHA-512", "not_recorded"})

PACKAGE_NAME_RE = re.compile(r"^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,199})$")
VERSION_RE = re.compile(r"^[0-9A-Za-z](?:[0-9A-Za-z.+_-]{0,199})$")
ACTION_RE = re.compile(
    r"^\s*uses:\s*([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)@([0-9a-f]{40})"
    r"(?:\s+#.*)?\s*$"
)
USES_LINE_RE = re.compile(r"^\s*uses:\s*(\S+)(?:\s+#.*)?\s*$")
PYTHON_VERSION_RE = re.compile(
    r'^\s*python-version:\s*["\']?([0-9]+\.[0-9]+\.[0-9]+)["\']?\s*(?:#.*)?$'
)
REQUIREMENT_RE = re.compile(
    r"^([A-Za-z0-9](?:[A-Za-z0-9._-]{0,199}))=="
    r"([0-9A-Za-z](?:[0-9A-Za-z.+_-]{0,199}))"
    r"\s+--hash=sha256:([0-9a-f]{64})$"
)
RELEASE_VERSION_RE = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$")
SOURCE_REVISION_RE = re.compile(r"^[0-9a-f]{40}$")
CONTROL_RE = re.compile(r"[\x00-\x1f\x7f]")


class SbomError(Exception):
    """A deterministic, user-actionable generation failure."""


def _duplicate_json_key(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise SbomError(f"JSON input contains duplicate key {key!r}.")
        result[key] = value
    return result


@dataclass(frozen=True)
class Snapshot:
    root: Path
    files: dict[str, bytes]

    @classmethod
    def load(cls, root: Path) -> "Snapshot":
        root = root.resolve(strict=True)
        relative_paths = set(FIXED_INPUTS)

        projects = sorted(
            _repository_relative(root, path)
            for base in (root / "src", root / "tests")
            for path in base.rglob("*.csproj")
        )
        locks = sorted(
            _repository_relative(root, path)
            for base in (root / "src", root / "tests")
            for path in base.rglob("packages.lock.json")
        )
        if not projects or len(projects) > MAX_PROJECTS:
            raise SbomError(
                f"Expected 1..{MAX_PROJECTS} project files; found {len(projects)}."
            )
        if len(locks) != len(projects):
            raise SbomError(
                f"Every project must have one local packages.lock.json; found "
                f"{len(projects)} projects and {len(locks)} locks."
            )

        project_directories = {str(Path(path).parent).replace("\\", "/") for path in projects}
        lock_directories = {str(Path(path).parent).replace("\\", "/") for path in locks}
        if project_directories != lock_directories:
            missing = sorted(project_directories - lock_directories)
            extra = sorted(lock_directories - project_directories)
            raise SbomError(
                "Project/lock directory mismatch: "
                f"missing locks={missing}; orphan locks={extra}."
            )

        relative_paths.update(projects)
        relative_paths.update(locks)
        files: dict[str, bytes] = {}
        total = 0
        for relative_path in sorted(relative_paths):
            path = _safe_input_path(root, relative_path)
            try:
                size = path.stat().st_size
            except OSError as exc:
                raise SbomError(f"Unable to stat input {relative_path}: {exc}") from exc
            if size > MAX_FILE_BYTES:
                raise SbomError(
                    f"Input {relative_path} exceeds the {MAX_FILE_BYTES}-byte limit."
                )
            try:
                content = path.read_bytes()
            except OSError as exc:
                raise SbomError(f"Unable to read input {relative_path}: {exc}") from exc
            if len(content) != size:
                raise SbomError(f"Input {relative_path} changed while it was read.")
            total += len(content)
            if total > MAX_TOTAL_INPUT_BYTES:
                raise SbomError(
                    f"SBOM inputs exceed the {MAX_TOTAL_INPUT_BYTES}-byte aggregate limit."
                )
            files[relative_path] = content
        return cls(root=root, files=files)

    def bytes(self, relative_path: str) -> bytes:
        try:
            return self.files[relative_path]
        except KeyError as exc:
            raise SbomError(f"Required snapshotted input is missing: {relative_path}.") from exc

    def text(self, relative_path: str) -> str:
        raw = self.bytes(relative_path)
        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError as exc:
            raise SbomError(f"Input {relative_path} is not valid UTF-8: {exc}") from exc
        if text.startswith("\ufeff"):
            raise SbomError(f"Input {relative_path} must not contain a UTF-8 BOM.")
        if "\x00" in text:
            raise SbomError(f"Input {relative_path} contains a NUL character.")
        return text

    def json(self, relative_path: str) -> Any:
        try:
            return json.loads(
                self.text(relative_path), object_pairs_hook=_duplicate_json_key
            )
        except SbomError:
            raise
        except (json.JSONDecodeError, RecursionError) as exc:
            raise SbomError(f"Invalid JSON in {relative_path}: {exc}") from exc

    def input_digest(self) -> str:
        digest = hashlib.sha256()
        for relative_path in sorted(self.files):
            path_bytes = relative_path.encode("utf-8")
            content = self.files[relative_path]
            digest.update(len(path_bytes).to_bytes(4, "big"))
            digest.update(path_bytes)
            digest.update(len(content).to_bytes(8, "big"))
            digest.update(content)
        return digest.hexdigest()


def _repository_relative(root: Path, path: Path) -> str:
    try:
        return path.relative_to(root).as_posix()
    except ValueError as exc:
        raise SbomError(f"Repository input escapes the repository root: {path}.") from exc


def _safe_input_path(root: Path, relative_path: str) -> Path:
    candidate = root / Path(relative_path)
    cursor = candidate
    while cursor != root:
        if cursor.is_symlink():
            raise SbomError(f"SBOM input must not be a symbolic link: {relative_path}.")
        cursor = cursor.parent
    try:
        resolved = candidate.resolve(strict=True)
        resolved.relative_to(root)
    except (OSError, ValueError) as exc:
        raise SbomError(
            f"Required SBOM input is missing or outside the repository: {relative_path}."
        ) from exc
    if not resolved.is_file():
        raise SbomError(f"SBOM input is not a regular file: {relative_path}.")
    return resolved


@dataclass
class DetectedComponent:
    component_id: str
    ecosystem: str
    name: str
    version: str
    dependency_class: str
    directness: str
    content_hash_algorithm: str
    content_hash: str
    dependencies: set[str] = field(default_factory=set)


@dataclass(frozen=True)
class MatrixRecord:
    values: dict[str, str]

    def __getitem__(self, key: str) -> str:
        return self.values[key]


def _clean_scalar(value: Any, context: str) -> str:
    if not isinstance(value, str):
        raise SbomError(f"{context} must be a string.")
    if not value or value != value.strip():
        raise SbomError(f"{context} must be non-empty and have no outer whitespace.")
    if len(value) > MAX_SCALAR_CHARS:
        raise SbomError(f"{context} exceeds {MAX_SCALAR_CHARS} characters.")
    if "\n" in value or "\r" in value or CONTROL_RE.search(value):
        raise SbomError(f"{context} contains a prohibited control character.")
    return value


def _validate_name_and_version(name: str, version: str, context: str) -> None:
    if not PACKAGE_NAME_RE.fullmatch(name):
        raise SbomError(f"{context} has invalid package name {name!r}.")
    if not VERSION_RE.fullmatch(version):
        raise SbomError(f"{context} has invalid version {version!r}.")


def _parse_xml(snapshot: Snapshot, relative_path: str) -> ET.Element:
    raw = snapshot.bytes(relative_path)
    lowered = raw.lower()
    if b"<!doctype" in lowered or b"<!entity" in lowered:
        raise SbomError(f"DTD/entity declarations are prohibited in {relative_path}.")
    try:
        return ET.fromstring(raw)
    except (ET.ParseError, RecursionError) as exc:
        raise SbomError(f"Invalid XML in {relative_path}: {exc}") from exc


def _local_xml_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def _central_package_versions(snapshot: Snapshot) -> dict[str, tuple[str, str]]:
    root = _parse_xml(snapshot, "Directory.Packages.props")
    result: dict[str, tuple[str, str]] = {}
    for element in root.iter():
        if _local_xml_name(element.tag) != "PackageVersion":
            continue
        name = _clean_scalar(element.attrib.get("Include"), "central package Include")
        version = _clean_scalar(element.attrib.get("Version"), f"central version for {name}")
        _validate_name_and_version(name, version, "central package pin")
        normalized = name.casefold()
        if normalized in result:
            raise SbomError(f"Duplicate central package pin for {name}.")
        result[normalized] = (name, version)
    if not result:
        raise SbomError("Directory.Packages.props contains no PackageVersion entries.")
    return result


def _project_direct_references(snapshot: Snapshot, project_path: str) -> set[str]:
    root = _parse_xml(snapshot, project_path)
    result: set[str] = set()
    for element in root.iter():
        if _local_xml_name(element.tag) != "PackageReference":
            continue
        name = element.attrib.get("Include") or element.attrib.get("Update")
        name = _clean_scalar(name, f"PackageReference in {project_path}")
        if not PACKAGE_NAME_RE.fullmatch(name):
            raise SbomError(f"Invalid PackageReference {name!r} in {project_path}.")
        if "Version" in element.attrib or any(
            _local_xml_name(child.tag) == "Version" for child in element
        ):
            raise SbomError(
                f"{project_path} bypasses central package pinning for {name}."
            )
        normalized = name.casefold()
        if normalized in result:
            raise SbomError(f"Duplicate PackageReference {name} in {project_path}.")
        result.add(normalized)
    return result


def _decode_nuget_hash(value: Any, context: str) -> str:
    value = _clean_scalar(value, context)
    try:
        decoded = base64.b64decode(value, validate=True)
    except (ValueError, binascii.Error) as exc:
        raise SbomError(f"{context} is not canonical base64.") from exc
    if len(decoded) != 64 or base64.b64encode(decoded).decode("ascii") != value:
        raise SbomError(f"{context} must encode exactly one canonical SHA-512 digest.")
    return decoded.hex()


def _merge_component(
    target: dict[str, DetectedComponent], component: DetectedComponent
) -> None:
    existing = target.get(component.component_id)
    if existing is None:
        if len(target) >= MAX_COMPONENTS:
            raise SbomError(f"Detected component count exceeds {MAX_COMPONENTS}.")
        target[component.component_id] = component
        return
    immutable_fields = (
        "ecosystem",
        "name",
        "version",
        "content_hash_algorithm",
        "content_hash",
    )
    for field_name in immutable_fields:
        if getattr(existing, field_name) != getattr(component, field_name):
            raise SbomError(
                f"Conflicting {field_name} values for {component.component_id}."
            )
    if component.dependency_class == "runtime":
        existing.dependency_class = "runtime"
    if component.directness == "direct":
        existing.directness = "direct"
    existing.dependencies.update(component.dependencies)


def _detect_nuget(snapshot: Snapshot) -> dict[str, DetectedComponent]:
    central = _central_package_versions(snapshot)
    detected: dict[str, DetectedComponent] = {}
    all_project_direct: set[str] = set()
    project_paths = sorted(path for path in snapshot.files if path.endswith(".csproj"))
    for project_path in project_paths:
        project_direct = _project_direct_references(snapshot, project_path)
        all_project_direct.update(project_direct)
        lock_path = f"{Path(project_path).parent.as_posix()}/packages.lock.json"
        document = snapshot.json(lock_path)
        if not isinstance(document, dict) or document.get("version") != 2:
            raise SbomError(f"{lock_path} must be a NuGet lock file at version 2.")
        frameworks = document.get("dependencies")
        if not isinstance(frameworks, dict) or not frameworks:
            raise SbomError(f"{lock_path} has no target-framework dependency map.")

        lock_direct: set[str] = set()
        for framework, entries in frameworks.items():
            _clean_scalar(framework, f"target framework in {lock_path}")
            if not isinstance(entries, dict):
                raise SbomError(f"Dependency map in {lock_path}/{framework} must be an object.")
            normalized_entries = {name.casefold(): name for name in entries}
            if len(normalized_entries) != len(entries):
                raise SbomError(f"Case-colliding package IDs exist in {lock_path}/{framework}.")
            for package_name, entry in entries.items():
                package_name = _clean_scalar(
                    package_name, f"package ID in {lock_path}/{framework}"
                )
                if not PACKAGE_NAME_RE.fullmatch(package_name):
                    raise SbomError(f"Invalid package ID {package_name!r} in {lock_path}.")
                if not isinstance(entry, dict):
                    raise SbomError(f"Lock entry for {package_name} in {lock_path} is not an object.")
                entry_type = entry.get("type")
                if entry_type == "Project":
                    continue
                if entry_type not in {"Direct", "Transitive"}:
                    raise SbomError(
                        f"Unsupported lock entry type {entry_type!r} for {package_name}."
                    )
                version = _clean_scalar(
                    entry.get("resolved"), f"resolved version for {package_name}"
                )
                _validate_name_and_version(package_name, version, lock_path)
                content_hash = _decode_nuget_hash(
                    entry.get("contentHash"), f"contentHash for {package_name}"
                )
                normalized_name = package_name.casefold()
                if entry_type == "Direct":
                    central_pin = central.get(normalized_name)
                    if central_pin is None:
                        raise SbomError(
                            f"Direct lock entry {package_name} has no central package pin."
                        )
                    requested = _clean_scalar(
                        entry.get("requested"), f"requested range for {package_name}"
                    )
                    expected_requested = f"[{central_pin[1]}, )"
                    if requested != expected_requested:
                        raise SbomError(
                            f"Direct lock entry {package_name} requests {requested!r}; "
                            f"expected {expected_requested!r}."
                        )
                    lock_direct.add(normalized_name)
                dependency_ids: set[str] = set()
                child_dependencies = entry.get("dependencies", {})
                if not isinstance(child_dependencies, dict):
                    raise SbomError(
                        f"dependencies for {package_name} in {lock_path} must be an object."
                    )
                for child_name, constraint in child_dependencies.items():
                    child_name = _clean_scalar(
                        child_name, f"dependency of {package_name} in {lock_path}"
                    )
                    _clean_scalar(
                        constraint, f"version constraint for {package_name}/{child_name}"
                    )
                    child_normalized = child_name.casefold()
                    if child_normalized not in normalized_entries:
                        raise SbomError(
                            f"{package_name} references missing lock entry {child_name} "
                            f"in {lock_path}/{framework}."
                        )
                    dependency_ids.add(f"nuget:{child_normalized}")

                dependency_class = (
                    "runtime" if project_path.startswith("src/") else "test_only"
                )
                component = DetectedComponent(
                    component_id=f"nuget:{normalized_name}",
                    ecosystem="nuget",
                    name=package_name,
                    version=version,
                    dependency_class=dependency_class,
                    directness="direct" if entry_type == "Direct" else "transitive",
                    content_hash_algorithm="SHA-512",
                    content_hash=content_hash,
                    dependencies=dependency_ids,
                )
                _merge_component(detected, component)

        if project_direct != lock_direct:
            raise SbomError(
                f"Direct PackageReference/lock mismatch for {project_path}: "
                f"project={sorted(project_direct)}, lock={sorted(lock_direct)}."
            )

    if set(central) != all_project_direct:
        raise SbomError(
            "Central package pins must equal the direct PackageReference set: "
            f"pins={sorted(central)}, references={sorted(all_project_direct)}."
        )
    for normalized_name, (_, pinned_version) in central.items():
        component = detected.get(f"nuget:{normalized_name}")
        if component is None or component.version != pinned_version:
            raise SbomError(
                f"Central pin {normalized_name} {pinned_version} does not match lock files."
            )
    return detected


def _detect_requirements(snapshot: Snapshot) -> dict[str, DetectedComponent]:
    detected: dict[str, DetectedComponent] = {}
    for line_number, raw_line in enumerate(
        snapshot.text("tools/requirements.txt").splitlines(), start=1
    ):
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        if line != raw_line:
            raise SbomError(
                f"tools/requirements.txt:{line_number} has outer whitespace."
            )
        match = REQUIREMENT_RE.fullmatch(line)
        if match is None:
            raise SbomError(
                "Every Python requirement must be one exact name==version entry with "
                f"one lowercase SHA-256 hash; invalid line {line_number}."
            )
        name, version, content_hash = match.groups()
        normalized = re.sub(r"[-_.]+", "-", name).casefold()
        component_id = f"pypi:{normalized}"
        _merge_component(
            detected,
            DetectedComponent(
                component_id=component_id,
                ecosystem="pypi",
                name=name,
                version=version,
                dependency_class="build_only",
                directness="direct",
                content_hash_algorithm="SHA-256",
                content_hash=content_hash,
            ),
        )
    if not detected:
        raise SbomError("tools/requirements.txt contains no pinned requirements.")
    return detected


def _detect_toolchain(snapshot: Snapshot) -> dict[str, DetectedComponent]:
    global_json = snapshot.json("global.json")
    if not isinstance(global_json, dict) or not isinstance(global_json.get("sdk"), dict):
        raise SbomError("global.json must contain an sdk object.")
    sdk = global_json["sdk"]
    sdk_version = _clean_scalar(sdk.get("version"), "global.json sdk.version")
    if sdk.get("rollForward") != "disable" or sdk.get("allowPrerelease") is not False:
        raise SbomError(
            "global.json must disable SDK roll-forward and prerelease selection."
        )
    if not VERSION_RE.fullmatch(sdk_version):
        raise SbomError(f"Invalid .NET SDK version {sdk_version!r}.")

    workflow = snapshot.text(".github/workflows/verify.yml")
    action_matches: list[tuple[str, str]] = []
    python_versions: list[str] = []
    for line_number, line in enumerate(workflow.splitlines(), start=1):
        uses_match = USES_LINE_RE.match(line)
        if uses_match is not None:
            action_match = ACTION_RE.match(line)
            if action_match is None:
                raise SbomError(
                    f"Workflow action at line {line_number} is not pinned to a lowercase "
                    "40-hex commit."
                )
            action_matches.append(action_match.groups())
        python_match = PYTHON_VERSION_RE.match(line)
        if python_match is not None:
            python_versions.append(python_match.group(1))
    if not action_matches:
        raise SbomError("The authoritative workflow contains no pinned actions.")
    if len(python_versions) != 1:
        raise SbomError(
            "The authoritative workflow must contain exactly one literal python-version."
        )

    detected: dict[str, DetectedComponent] = {}
    for action_name, revision in action_matches:
        normalized = action_name.casefold()
        _merge_component(
            detected,
            DetectedComponent(
                component_id=f"github_action:{normalized}",
                ecosystem="github_action",
                name=action_name,
                version=revision,
                dependency_class="build_only",
                directness="direct",
                content_hash_algorithm="not_recorded",
                content_hash="not_recorded",
            ),
        )
    python_version = python_versions[0]
    detected["toolchain:dotnet-sdk"] = DetectedComponent(
        component_id="toolchain:dotnet-sdk",
        ecosystem="toolchain",
        name="Microsoft .NET SDK",
        version=sdk_version,
        dependency_class="build_only",
        directness="platform",
        content_hash_algorithm="not_recorded",
        content_hash="not_recorded",
    )
    detected["toolchain:python"] = DetectedComponent(
        component_id="toolchain:python",
        ecosystem="toolchain",
        name="CPython",
        version=python_version,
        dependency_class="build_only",
        directness="platform",
        content_hash_algorithm="not_recorded",
        content_hash="not_recorded",
    )
    return detected


def detect_components(snapshot: Snapshot) -> dict[str, DetectedComponent]:
    detected: dict[str, DetectedComponent] = {}
    for source in (
        _detect_nuget(snapshot),
        _detect_requirements(snapshot),
        _detect_toolchain(snapshot),
    ):
        for component in source.values():
            _merge_component(detected, component)
    if not detected or len(detected) > MAX_COMPONENTS:
        raise SbomError(f"Expected 1..{MAX_COMPONENTS} detected components.")
    for component in detected.values():
        unknown_dependencies = component.dependencies - set(detected)
        if unknown_dependencies:
            raise SbomError(
                f"{component.component_id} has unknown dependencies "
                f"{sorted(unknown_dependencies)}."
            )
    return detected


def _validate_https_uri(value: str, context: str) -> None:
    parsed = urlsplit(value)
    if (
        parsed.scheme != "https"
        or not parsed.netloc
        or parsed.username is not None
        or parsed.password is not None
    ):
        raise SbomError(f"{context} must be an HTTPS URI without user information.")


def read_matrix(snapshot: Snapshot) -> dict[str, MatrixRecord]:
    text = snapshot.text("docs/supply-chain/license-matrix.csv")
    try:
        reader = csv.DictReader(io.StringIO(text, newline=""), strict=True)
        if reader.fieldnames is None or tuple(reader.fieldnames) != MATRIX_COLUMNS:
            raise SbomError(
                "license-matrix.csv header must exactly match the governed 21-column schema."
            )
        records: dict[str, MatrixRecord] = {}
        for row_number, row in enumerate(reader, start=2):
            if row_number > MAX_CSV_ROWS + 1:
                raise SbomError(f"license-matrix.csv exceeds {MAX_CSV_ROWS} rows.")
            if None in row or set(row) != set(MATRIX_COLUMNS):
                raise SbomError(f"Malformed CSV row {row_number}.")
            values = {
                key: _clean_scalar(row[key], f"license-matrix.csv:{row_number}:{key}")
                for key in MATRIX_COLUMNS
            }
            component_id = values["component_id"]
            if component_id in records:
                raise SbomError(f"Duplicate matrix component_id {component_id!r}.")
            if component_id != component_id.casefold():
                raise SbomError(f"Matrix component_id must be lowercase: {component_id}.")
            if values["ecosystem"] not in ECOSYSTEMS:
                raise SbomError(f"Invalid ecosystem for {component_id}.")
            if values["dependency_class"] not in DEPENDENCY_CLASSES:
                raise SbomError(f"Invalid dependency_class for {component_id}.")
            if values["directness"] not in DIRECTNESS_VALUES:
                raise SbomError(f"Invalid directness for {component_id}.")
            for column in ("installation_rights", "redistribution_rights"):
                if values[column] not in RIGHTS_VALUES:
                    raise SbomError(f"Invalid {column} for {component_id}.")
            if values["bundled_in_release"] not in BUNDLED_VALUES:
                raise SbomError(f"Invalid bundled_in_release for {component_id}.")
            if values["review_status"] not in REVIEW_VALUES:
                raise SbomError(f"Invalid review_status for {component_id}.")
            algorithm = values["content_hash_algorithm"]
            content_hash = values["content_hash"]
            if algorithm not in HASH_ALGORITHMS:
                raise SbomError(f"Invalid content_hash_algorithm for {component_id}.")
            expected_length = {"SHA-256": 64, "SHA-512": 128}.get(algorithm)
            if expected_length is None:
                if content_hash != "not_recorded":
                    raise SbomError(
                        f"{component_id} must use content_hash=not_recorded."
                    )
            elif not re.fullmatch(f"[0-9a-f]{{{expected_length}}}", content_hash):
                raise SbomError(
                    f"{component_id} must have a lowercase {algorithm} hex digest."
                )
            _validate_https_uri(values["source_uri"], f"source_uri for {component_id}")
            records[component_id] = MatrixRecord(values)
    except csv.Error as exc:
        raise SbomError(f"Invalid license-matrix.csv: {exc}") from exc
    if not records:
        raise SbomError("license-matrix.csv contains no component rows.")
    return records


def reconcile(
    detected: dict[str, DetectedComponent], matrix: dict[str, MatrixRecord]
) -> None:
    detected_ids = set(detected)
    matrix_ids = set(matrix)
    if detected_ids != matrix_ids:
        raise SbomError(
            "Detected components must exactly equal license-matrix.csv rows: "
            f"missing rows={sorted(detected_ids - matrix_ids)}; "
            f"stale/extra rows={sorted(matrix_ids - detected_ids)}."
        )
    comparable_fields = (
        "ecosystem",
        "name",
        "version",
        "dependency_class",
        "directness",
        "content_hash_algorithm",
        "content_hash",
    )
    for component_id in sorted(detected):
        component = detected[component_id]
        record = matrix[component_id]
        for field_name in comparable_fields:
            if getattr(component, field_name) != record[field_name]:
                raise SbomError(
                    f"Detected {field_name} for {component_id} is "
                    f"{getattr(component, field_name)!r}, but matrix records "
                    f"{record[field_name]!r}."
                )


def _release_identity(product_version: str, source_revision: str) -> tuple[str, str]:
    if not RELEASE_VERSION_RE.fullmatch(product_version):
        raise SbomError(
            "Release generation requires --product-version as an exact semantic version."
        )
    if not SOURCE_REVISION_RE.fullmatch(source_revision):
        raise SbomError(
            "Release generation requires --source-revision as a lowercase "
            "40-hex Git object ID."
        )
    return product_version, source_revision


def _enforce_release_gate(matrix: dict[str, MatrixRecord]) -> None:
    failures: list[str] = []
    for component_id, record in sorted(matrix.items()):
        reasons: list[str] = []
        if record["review_status"] != "approved":
            reasons.append(f"review={record['review_status']}")
        if record["installation_rights"] not in {"permitted", "not_applicable"}:
            reasons.append(f"installation={record['installation_rights']}")
        if record["redistribution_rights"] not in {
            "permitted",
            "prohibited",
            "not_applicable",
        }:
            reasons.append(f"redistribution={record['redistribution_rights']}")
        if record["bundled_in_release"] == "pending":
            reasons.append("bundled=pending")
        if record["bundled_in_release"] == "yes" and record[
            "redistribution_rights"
        ] != "permitted":
            reasons.append(
                f"bundled=yes with redistribution={record['redistribution_rights']}"
            )
        if record["notice_requirements"] == "pending_human_review":
            reasons.append("notice=pending_human_review")
        if record["license_expression"] == "NOASSERTION":
            reasons.append("license=NOASSERTION")
        if reasons:
            failures.append(f"{component_id} ({', '.join(reasons)})")
    if failures:
        preview = "; ".join(failures[:8])
        suffix = "" if len(failures) <= 8 else f"; and {len(failures) - 8} more"
        raise SbomError(
            "Release SBOM is blocked by incomplete human licence/rights review: "
            f"{preview}{suffix}."
        )


def _purl(component: DetectedComponent) -> str:
    version = quote(component.version, safe=".-_")
    if component.ecosystem == "nuget":
        return f"pkg:nuget/{quote(component.name, safe='.-_')}@{version}"
    if component.ecosystem == "pypi":
        normalized = re.sub(r"[-_.]+", "-", component.name).casefold()
        return f"pkg:pypi/{quote(normalized, safe='.-_')}@{version}"
    if component.ecosystem == "github_action":
        owner, name = component.name.split("/", 1)
        return (
            f"pkg:github/{quote(owner, safe='.-_')}/"
            f"{quote(name, safe='.-_')}@{version}"
        )
    generic_name = component.component_id.split(":", 1)[1]
    return f"pkg:generic/{quote(generic_name, safe='.-_')}@{version}"


def _component_type(ecosystem: str) -> str:
    if ecosystem in {"nuget", "pypi"}:
        return "library"
    if ecosystem == "toolchain":
        return "framework"
    return "application"


def _component_scope(dependency_class: str) -> str:
    if dependency_class == "runtime":
        return "required"
    if dependency_class == "catalog_application":
        return "optional"
    return "excluded"


def _cyclonedx_license(license_expression: str) -> dict[str, Any]:
    if license_expression == "NOASSERTION":
        return {"license": {"name": "NOASSERTION"}}
    return {"expression": license_expression}


def _properties(values: dict[str, str], component_id: str) -> list[dict[str, str]]:
    selected = {
        "thirdlife:component-id": component_id,
        "thirdlife:dependency-class": values["dependency_class"],
        "thirdlife:directness": values["directness"],
        "thirdlife:internal-owner": values["internal_owner"],
        "thirdlife:upstream-owner": values["upstream_owner"],
        "thirdlife:provenance-reference": values["provenance_reference"],
        "thirdlife:purpose": values["purpose"],
        "thirdlife:license-evidence": values["license_evidence"],
        "thirdlife:installation-rights": values["installation_rights"],
        "thirdlife:redistribution-rights": values["redistribution_rights"],
        "thirdlife:bundled-in-release": values["bundled_in_release"],
        "thirdlife:notice-requirements": values["notice_requirements"],
        "thirdlife:review-status": values["review_status"],
        "thirdlife:review-notes": values["review_notes"],
    }
    return [
        {"name": name, "value": selected[name]}
        for name in sorted(selected)
    ]


def build_sbom(
    snapshot: Snapshot,
    release: bool,
    product_version: str | None = None,
    source_revision: str | None = None,
) -> bytes:
    detected = detect_components(snapshot)
    matrix = read_matrix(snapshot)
    reconcile(detected, matrix)

    if release:
        _enforce_release_gate(matrix)
        product_version, source_revision = _release_identity(
            product_version or "", source_revision or ""
        )
        mode = "release"
        inventory_scope = "release-source-and-reviewed-dependencies"
    else:
        if product_version is not None or source_revision is not None:
            raise SbomError(
                "Product version and source revision are accepted only with --release."
            )
        product_version = DEVELOPMENT_VERSION
        source_revision = ""
        mode = "development"
        inventory_scope = DEVELOPMENT_SCOPE

    component_refs = {
        component_id: _purl(component)
        for component_id, component in detected.items()
    }
    components: list[dict[str, Any]] = []
    for component_id in sorted(detected):
        detected_component = detected[component_id]
        record = matrix[component_id]
        entry: dict[str, Any] = {
            "bom-ref": component_refs[component_id],
            "type": _component_type(detected_component.ecosystem),
            "name": detected_component.name,
            "version": detected_component.version,
            "scope": _component_scope(detected_component.dependency_class),
            "purl": component_refs[component_id],
            "licenses": [_cyclonedx_license(record["license_expression"])],
            "externalReferences": [
                {"type": "website", "url": record["source_uri"]}
            ],
            "properties": _properties(record.values, component_id),
        }
        if detected_component.content_hash_algorithm != "not_recorded":
            entry["hashes"] = [
                {
                    "alg": detected_component.content_hash_algorithm,
                    "content": detected_component.content_hash,
                }
            ]
        components.append(entry)

    root_ref = f"pkg:generic/thirdlife-setup-core@{quote(product_version, safe='.-_')}"
    dependency_entries: list[dict[str, Any]] = [
        {
            "ref": root_ref,
            "dependsOn": sorted(
                component_refs[component_id]
                for component_id, component in detected.items()
                if component.directness in {"direct", "platform"}
            ),
        }
    ]
    for component_id in sorted(detected):
        dependency_entries.append(
            {
                "ref": component_refs[component_id],
                "dependsOn": sorted(
                    component_refs[dependency_id]
                    for dependency_id in detected[component_id].dependencies
                ),
            }
        )

    metadata_properties = {
        "thirdlife:generation-mode": mode,
        "thirdlife:input-sha256": snapshot.input_digest(),
        "thirdlife:inventory-scope": inventory_scope,
        "thirdlife:requirements-file-sha256": hashlib.sha256(
            snapshot.bytes("tools/requirements.txt")
        ).hexdigest(),
    }
    if source_revision:
        metadata_properties["thirdlife:source-revision"] = source_revision

    document: dict[str, Any] = {
        "$schema": (
            "https://cyclonedx.org/schema/"
            f"bom-{CYCLONEDX_SPEC_VERSION}.schema.json"
        ),
        "bomFormat": "CycloneDX",
        "specVersion": CYCLONEDX_SPEC_VERSION,
        "version": 1,
        "metadata": {
            "component": {
                "bom-ref": root_ref,
                "type": "application",
                "name": "ThirdLife Setup Core",
                "version": product_version,
                "purl": root_ref,
            },
            "properties": [
                {"name": name, "value": metadata_properties[name]}
                for name in sorted(metadata_properties)
            ],
        },
        "components": components,
        "dependencies": dependency_entries,
    }
    return (
        json.dumps(
            document,
            ensure_ascii=False,
            indent=2,
            sort_keys=True,
            separators=(",", ": "),
        )
        + "\n"
    ).encode("utf-8")


def _assert_self_test(snapshot: Snapshot) -> tuple[int, str]:
    first = build_sbom(snapshot, release=False)
    second = build_sbom(snapshot, release=False)
    if first != second:
        raise SbomError("Repeated in-memory SBOM generation was not byte-for-byte stable.")
    try:
        parsed = json.loads(first, object_pairs_hook=_duplicate_json_key)
    except (json.JSONDecodeError, SbomError) as exc:
        raise SbomError(f"Generated SBOM is not valid unique-key JSON: {exc}") from exc
    if parsed.get("bomFormat") != "CycloneDX" or parsed.get("specVersion") != "1.7":
        raise SbomError("Generated document does not identify CycloneDX 1.7.")
    prohibited_keys = {"timestamp", "serialnumber"}
    machine_paths = {
        str(snapshot.root).casefold(),
        str(snapshot.root).replace("\\", "/").casefold(),
        str(Path.home()).casefold(),
        str(Path.home()).replace("\\", "/").casefold(),
    }
    pending: list[Any] = [parsed]
    while pending:
        current = pending.pop()
        if isinstance(current, dict):
            if any(str(key).casefold() in prohibited_keys for key in current):
                raise SbomError("Generated SBOM contains a prohibited host/time field.")
            pending.extend(current.values())
        elif isinstance(current, list):
            pending.extend(current)
        elif isinstance(current, str):
            normalized = current.casefold()
            if any(path and path in normalized for path in machine_paths):
                raise SbomError("Generated SBOM contains prohibited host-derived data.")
    components = parsed.get("components")
    if not isinstance(components, list):
        raise SbomError("Generated SBOM has no components array.")
    references = [component.get("bom-ref") for component in components]
    if len(references) != len(set(references)):
        raise SbomError("Generated component bom-ref values are not unique.")
    return len(components), hashlib.sha256(first).hexdigest()


def _atomic_write(output_path: Path, content: bytes, input_paths: Iterable[Path]) -> None:
    try:
        expanded_output_path = output_path.expanduser()
        if expanded_output_path.is_symlink():
            raise SbomError("Output path must not be a symbolic link.")
        output_path = expanded_output_path.resolve(strict=False)
    except OSError as exc:
        raise SbomError("Unable to resolve output path.") from exc
    if output_path.suffix.casefold() != ".json" or not output_path.name.casefold().endswith(
        ".cdx.json"
    ):
        raise SbomError("Output path must end in .cdx.json.")
    if output_path in set(input_paths):
        raise SbomError("Output path must not overwrite a generator input.")
    parent = output_path.parent
    try:
        parent.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        raise SbomError("Unable to create the output directory.") from exc
    if not parent.is_dir():
        raise SbomError("Output directory must be a directory.")

    temporary_name: str | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb", prefix=f".{output_path.name}.", suffix=".tmp", dir=parent, delete=False
        ) as temporary:
            temporary_name = temporary.name
            temporary.write(content)
            temporary.flush()
            os.fsync(temporary.fileno())
        os.replace(temporary_name, output_path)
        temporary_name = None
    except OSError as exc:
        raise SbomError(f"Unable to atomically write {output_path.name}.") from exc
    finally:
        if temporary_name is not None:
            try:
                Path(temporary_name).unlink(missing_ok=True)
            except OSError:
                pass


def _parse_arguments(arguments: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate an offline deterministic CycloneDX SBOM from checked-in inputs."
    )
    output_group = parser.add_mutually_exclusive_group(required=True)
    output_group.add_argument("--output", type=Path, help="Destination ending in .cdx.json")
    output_group.add_argument(
        "--self-test", action="store_true", help="Generate twice in memory and compare bytes"
    )
    parser.add_argument(
        "--release",
        action="store_true",
        help="Require approved release rights and exact release identity",
    )
    parser.add_argument(
        "--product-version",
        help="Exact product version; required with --release",
    )
    parser.add_argument(
        "--source-revision",
        help="Lowercase 40-hex Git object ID; required with --release",
    )
    parsed = parser.parse_args(arguments)
    if parsed.self_test and parsed.release:
        parser.error("--self-test and --release cannot be combined")
    return parsed


def main(arguments: list[str] | None = None) -> int:
    parsed = _parse_arguments(sys.argv[1:] if arguments is None else arguments)
    try:
        repository_root = Path(__file__).resolve(strict=True).parent.parent
        snapshot = Snapshot.load(repository_root)
        if parsed.self_test:
            count, digest = _assert_self_test(snapshot)
            print(f"OK: deterministic CycloneDX 1.7 SBOM ({count} components, sha256:{digest}).")
            return 0
        content = build_sbom(
            snapshot,
            release=parsed.release,
            product_version=parsed.product_version,
            source_revision=parsed.source_revision,
        )
        input_paths = (
            (snapshot.root / relative_path).resolve(strict=True)
            for relative_path in snapshot.files
        )
        _atomic_write(parsed.output, content, input_paths)
        print(
            f"OK: wrote deterministic {'release' if parsed.release else 'development'} "
            f"CycloneDX 1.7 SBOM to {parsed.output.name}."
        )
        return 0
    except SbomError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
