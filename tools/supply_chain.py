#!/usr/bin/env python3
"""Validate ThirdLife dependency governance and generate a deterministic SBOM.

The validator deliberately uses only the Python standard library.  It discovers
dependency facts from governed repository inputs and compares them with the
human-curated licence matrix.  Expected input errors are collected and returned
instead of leaking parser tracebacks or machine-specific paths.
"""

from __future__ import annotations

import argparse
import base64
import binascii
import csv
import datetime as dt
import hashlib
import io
import ipaddress
import json
import os
import re
import subprocess
import sys
import tempfile
import urllib.parse
from dataclasses import dataclass, replace
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence


MATRIX_PATH = Path("docs/supply-chain/license-matrix.csv")
DEPENDENCIES_PATH = Path("docs/supply-chain/dependencies.md")
MATRIX_HEADERS = (
    "component_type",
    "component_id",
    "version",
    "relationship",
    "scope",
    "owner",
    "upstream_publisher",
    "source",
    "purpose",
    "declared_license",
    "license_evidence",
    "proposed_license_conclusion",
    "proposed_installation_rights",
    "proposed_redistribution_rights",
    "distribution_plan",
    "integrity_algorithm",
    "integrity_value",
    "provenance_reference",
    "limitations",
)

ALLOWED_SCOPES = frozenset(
    {"runtime", "build-only", "test-only", "catalog-application"}
)
ALLOWED_RELATIONSHIPS: Mapping[str, frozenset[str]] = {
    "nuget": frozenset({"direct", "transitive"}),
    "pypi": frozenset({"direct", "transitive"}),
    "github-action": frozenset({"ci"}),
    "toolchain": frozenset({"toolchain"}),
    "catalog-application": frozenset({"direct", "transitive"}),
}
EXPECTED_SCOPE_BY_TYPE: Mapping[str, frozenset[str]] = {
    "nuget": frozenset({"runtime", "test-only"}),
    "pypi": frozenset({"runtime", "build-only", "test-only"}),
    "github-action": frozenset({"build-only"}),
    "toolchain": frozenset({"build-only"}),
    "catalog-application": frozenset({"catalog-application"}),
}

REVIEW_FIELDS = (
    "Review status",
    "Reviewer",
    "Role",
    "Review date",
    "Result",
    "Reviewed commit",
    "Matrix SHA-256",
)
PENDING_REVIEW = {
    "Review status": "Pending",
    "Reviewer": "Not recorded",
    "Role": "Not recorded",
    "Review date": "Not recorded",
    "Result": "Not recorded",
    "Reviewed commit": "Not recorded",
    "Matrix SHA-256": "Not recorded",
}

MAX_TEXT_BYTES = 4 * 1024 * 1024
MAX_MATRIX_BYTES = 1024 * 1024
MAX_DOCUMENT_BYTES = 512 * 1024
MAX_INPUT_FILES = 256
MAX_COMPONENTS = 2048
MAX_FIELD_LENGTH = 4096
MAX_ERRORS = 200
GIT_TIMEOUT_SECONDS = 5
MAX_GIT_LIST_BYTES = 4 * 1024 * 1024

DISCOVERY_EXCLUDED_PARTS = frozenset(
    {
        ".cache",
        ".git",
        ".mypy_cache",
        ".nox",
        ".pytest_cache",
        ".ruff_cache",
        ".tox",
        ".venv",
        "__pycache__",
        "artifacts",
        "bin",
        "node_modules",
        "obj",
    }
)

SHA256_RE = re.compile(r"[0-9a-f]{64}\Z")
GIT_SHA1_RE = re.compile(r"[0-9a-f]{40}\Z")
GIT_OBJECT_RE = re.compile(r"(?:[0-9a-f]{40}|[0-9a-f]{64})\Z")
PACKAGE_NAME_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]*\Z")
VERSION_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9.+_-]*\Z")
MACHINE_PATH_RE = re.compile(
    r"(?i)(?:\b[a-z]:[\\/]|\\\\[^\\/\s]+[\\/][^\\/\s]+|"
    r"file:/+(?:[a-z]:|/)|/(?:home|users)/[^/\s]+/)"
)
LATEST_RE = re.compile(r"(?i)(?:^|[^a-z0-9])latest(?:$|[^a-z0-9])")
FORMULA_PREFIXES = ("=", "+", "-", "@")
REQUIREMENT_RE = re.compile(
    r"^(?P<name>[A-Za-z0-9][A-Za-z0-9._-]*)"
    r"(?:\[(?P<extras>[A-Za-z0-9_,.-]+)\])?"
    r"==(?P<version>[A-Za-z0-9][A-Za-z0-9.+_-]*)"
    r"(?P<tail>(?:\s+--hash=sha256:[0-9a-fA-F]{64})+)\s*$"
)
HASH_OPTION_RE = re.compile(r"--hash=sha256:([0-9a-fA-F]{64})")
USES_RE = re.compile(
    r"^\s*(?:-\s*)?uses\s*:\s*[\"']?"
    r"(?P<target>[^\"'\s#]+)[\"']?"
    r"(?:\s*#\s*(?P<comment>[^\r\n]*))?\s*$",
    re.IGNORECASE,
)
PYTHON_VERSION_RE = re.compile(
    r"^\s*python-version\s*:\s*[\"']?(?P<version>[^\"'\s#]+)[\"']?"
    r"(?:\s*#.*)?$",
    re.IGNORECASE | re.MULTILINE,
)
VERSION_COMMENT_RE = re.compile(r"(?:^|\s)v?(\d+(?:\.\d+)+(?:[-+][A-Za-z0-9.-]+)?)\b")


@dataclass(frozen=True, slots=True)
class SupplyChainComponent:
    """One curated dependency row plus its discovered dependency edges."""

    component_type: str
    component_id: str
    version: str
    relationship: str
    scope: str
    owner: str
    upstream_publisher: str
    source: str
    purpose: str
    declared_license: str
    license_evidence: str
    proposed_license_conclusion: str
    proposed_installation_rights: str
    proposed_redistribution_rights: str
    distribution_plan: str
    integrity_algorithm: str
    integrity_value: str
    provenance_reference: str
    limitations: str
    dependencies: tuple[str, ...] = ()
    evidence_paths: tuple[str, ...] = ()

    @property
    def identity(self) -> tuple[str, str, str, str, str]:
        return (
            self.component_type,
            self.component_id,
            self.version,
            self.relationship,
            self.scope,
        )

    @property
    def discovery_identity(self) -> tuple[str, str, str, str, str, str, str]:
        return self.identity + (self.integrity_algorithm, self.integrity_value)

    @property
    def bom_ref(self) -> str:
        encoded_id = urllib.parse.quote(self.component_id, safe="._-")
        encoded_version = urllib.parse.quote(self.version, safe="._-+")
        return f"urn:thirdlife:{self.component_type}:{encoded_id}@{encoded_version}"


@dataclass(frozen=True, slots=True)
class SupplyChainResult:
    """Complete validation result; expected failures are represented by errors."""

    root: Path
    inventory: tuple[SupplyChainComponent, ...]
    dependency_graph: Mapping[str, tuple[str, ...]]
    lock_digest: str
    matrix_digest: str
    approval_state: str
    errors: tuple[str, ...]
    input_digests: tuple[tuple[str, str], ...] = ()

    @property
    def ok(self) -> bool:
        return not self.errors


@dataclass(frozen=True, slots=True)
class _DiscoveredComponent:
    component_type: str
    component_id: str
    version: str
    relationship: str
    scope: str
    integrity_algorithm: str
    integrity_value: str
    dependencies: tuple[str, ...]
    evidence_paths: tuple[str, ...]

    @property
    def identity(self) -> tuple[str, str, str, str, str, str, str]:
        return (
            self.component_type,
            self.component_id,
            self.version,
            self.relationship,
            self.scope,
            self.integrity_algorithm,
            self.integrity_value,
        )


@dataclass(frozen=True, slots=True)
class _CatalogIdentity:
    component_id: str
    version: str
    evidence_path: str
    integrity_algorithm: str
    integrity_value: str

    @property
    def identity(self) -> tuple[str, str]:
        return (self.component_id, self.version)


class _Validation:
    def __init__(self, root: Path) -> None:
        self.root = root
        self.errors: list[str] = []
        self.input_bytes: dict[str, bytes] = {}

    def error(self, message: str) -> None:
        if len(self.errors) < MAX_ERRORS:
            self.errors.append(message)
        elif len(self.errors) == MAX_ERRORS:
            self.errors.append("too many validation errors; remaining errors omitted")

    def relative(self, path: Path) -> str:
        try:
            return path.resolve().relative_to(self.root).as_posix()
        except (OSError, ValueError):
            return path.name

    def read_bytes(
        self,
        path: Path,
        *,
        maximum: int = MAX_TEXT_BYTES,
        required: bool = True,
        record_input: bool = True,
    ) -> bytes | None:
        label = self.relative(path)
        try:
            resolved = path.resolve(strict=True)
            try:
                resolved.relative_to(self.root)
            except ValueError:
                self.error(f"{label}: input resolves outside the repository root")
                return None
            if path.is_symlink():
                self.error(f"{label}: symbolic-link inputs are not allowed")
                return None
            size = resolved.stat().st_size
            if size > maximum:
                self.error(f"{label}: input exceeds the {maximum}-byte limit")
                return None
            data = resolved.read_bytes()
        except FileNotFoundError:
            if required:
                self.error(f"{label}: required input is missing")
            return None
        except OSError as exc:
            self.error(f"{label}: cannot read input ({exc.__class__.__name__})")
            return None
        if b"\x00" in data:
            self.error(f"{label}: NUL bytes are not allowed")
            return None
        if record_input:
            self.input_bytes[label] = data
        return data

    def read_text(
        self,
        path: Path,
        *,
        maximum: int = MAX_TEXT_BYTES,
        required: bool = True,
        record_input: bool = True,
    ) -> str | None:
        data = self.read_bytes(
            path,
            maximum=maximum,
            required=required,
            record_input=record_input,
        )
        if data is None:
            return None
        try:
            return data.decode("utf-8-sig")
        except UnicodeDecodeError:
            self.error(f"{self.relative(path)}: input must be valid UTF-8")
            return None


def _has_excluded_part(path: Path, root: Path) -> bool:
    try:
        parts = path.relative_to(root).parts
    except ValueError:
        return True
    return any(part.casefold() in DISCOVERY_EXCLUDED_PARTS for part in parts)


def _bounded_rglob(
    base: Path,
    pattern: str,
    validation: _Validation,
    label: str,
) -> list[Path]:
    paths: list[Path] = []
    try:
        for path in base.rglob(pattern):
            if _has_excluded_part(path, validation.root):
                continue
            paths.append(path)
            if len(paths) > MAX_INPUT_FILES:
                validation.error(f"{label}: too many input files")
                return paths[:MAX_INPUT_FILES]
    except (OSError, RuntimeError):
        validation.error(f"{label}: cannot enumerate inputs")
    return sorted(paths)


def _component_sort_key(component: SupplyChainComponent | _DiscoveredComponent) -> tuple[str, ...]:
    return (
        component.component_type.casefold(),
        component.component_id.casefold(),
        component.version.casefold(),
        component.relationship.casefold(),
        component.scope.casefold(),
    )


def _matrix_row_sort_key(row: Mapping[str, str]) -> tuple[str, ...]:
    return (
        row["component_type"].casefold(),
        row["component_id"].casefold(),
        row["version"].casefold(),
        row["relationship"].casefold(),
        row["scope"].casefold(),
    )


def _safe_json(
    path: Path, validation: _Validation, *, maximum: int = MAX_TEXT_BYTES
) -> dict[str, Any] | None:
    text = validation.read_text(path, maximum=maximum)
    if text is None:
        return None
    try:
        value = json.loads(text)
    except (json.JSONDecodeError, RecursionError):
        validation.error(f"{validation.relative(path)}: malformed JSON")
        return None
    if not isinstance(value, dict):
        validation.error(f"{validation.relative(path)}: top-level JSON must be an object")
        return None
    return value


def _validate_version(value: str, label: str, validation: _Validation) -> bool:
    if not VERSION_RE.fullmatch(value) or LATEST_RE.search(value):
        validation.error(f"{label}: version must be an exact non-latest token")
        return False
    return True


def _validate_nuget_content_hash(value: Any, label: str, validation: _Validation) -> str | None:
    if not isinstance(value, str):
        validation.error(f"{label}: NuGet contentHash must be a string")
        return None
    try:
        decoded = base64.b64decode(value, validate=True)
    except (ValueError, binascii.Error):
        validation.error(f"{label}: NuGet contentHash is not valid base64")
        return None
    if len(decoded) != 64:
        validation.error(f"{label}: NuGet contentHash must encode a SHA-512 value")
        return None
    return value


def _nuget_dependency_version(value: Any) -> str | None:
    if not isinstance(value, str):
        return None
    candidate = value.strip()
    exact_bracket = re.fullmatch(r"\[([^,\]]+)\]", candidate)
    if exact_bracket:
        return exact_bracket.group(1)
    if VERSION_RE.fullmatch(candidate):
        return candidate
    return None


def _discover_nuget(validation: _Validation) -> list[_DiscoveredComponent]:
    paths = sorted(
        path
        for base in (validation.root / "src", validation.root / "tests")
        for path in _bounded_rglob(
            base, "packages.lock.json", validation, "src/tests NuGet locks"
        )
    )
    if not paths:
        validation.error("src/tests: no NuGet lock files were found")
        return []
    if len(paths) > MAX_INPUT_FILES:
        validation.error("src/tests: too many NuGet lock files")
        return []
    occurrences: dict[
        tuple[str, str, str, str, str, str, str], list[_DiscoveredComponent]
    ] = {}
    casing_by_id: dict[str, str] = {}

    for path in paths:
        label = validation.relative(path)
        value = _safe_json(path, validation)
        if value is None:
            continue
        if value.get("version") != 2:
            validation.error(f"{label}: NuGet lock version must be 2")
        frameworks = value.get("dependencies")
        if not isinstance(frameworks, dict) or not frameworks:
            validation.error(f"{label}: dependencies must be a non-empty object")
            continue
        scope = "test-only" if label.startswith("tests/") else "runtime"
        for framework, packages in sorted(frameworks.items(), key=lambda item: str(item[0])):
            if not isinstance(framework, str) or not isinstance(packages, dict):
                validation.error(f"{label}: malformed target-framework dependency map")
                continue
            if len(packages) > MAX_COMPONENTS:
                validation.error(f"{label}: target framework has too many dependencies")
                continue
            for package_id, metadata in sorted(
                packages.items(), key=lambda item: str(item[0]).casefold()
            ):
                package_label = f"{label}:{framework}:{package_id}"
                if not isinstance(package_id, str) or not PACKAGE_NAME_RE.fullmatch(package_id):
                    validation.error(f"{package_label}: invalid NuGet package ID")
                    continue
                if not isinstance(metadata, dict):
                    validation.error(f"{package_label}: dependency metadata must be an object")
                    continue
                relationship_value = metadata.get("type")
                if relationship_value == "Project":
                    continue
                relationship_map = {"Direct": "direct", "Transitive": "transitive"}
                relationship = relationship_map.get(relationship_value)
                if relationship is None:
                    validation.error(f"{package_label}: unsupported NuGet relationship")
                    continue
                version = metadata.get("resolved")
                if not isinstance(version, str) or not _validate_version(
                    version, package_label, validation
                ):
                    continue
                content_hash = _validate_nuget_content_hash(
                    metadata.get("contentHash"), package_label, validation
                )
                if content_hash is None:
                    continue

                normalized_id = package_id.casefold()
                prior_casing = casing_by_id.setdefault(normalized_id, package_id)
                if prior_casing != package_id:
                    validation.error(
                        f"{package_label}: package ID casing differs from {prior_casing!r}"
                    )

                dependencies_value = metadata.get("dependencies", {})
                dependency_specs: list[str] = []
                if not isinstance(dependencies_value, dict):
                    validation.error(f"{package_label}: dependencies must be an object")
                else:
                    for child_id, child_version_value in sorted(
                        dependencies_value.items(), key=lambda item: str(item[0]).casefold()
                    ):
                        if not isinstance(child_id, str) or not PACKAGE_NAME_RE.fullmatch(child_id):
                            validation.error(f"{package_label}: invalid child package ID")
                            continue
                        child_version = _nuget_dependency_version(child_version_value)
                        if child_version is None:
                            validation.error(
                                f"{package_label}: child {child_id!r} does not have an exact version"
                            )
                            continue
                        dependency_specs.append(f"{child_id.casefold()}@{child_version}")

                discovered = _DiscoveredComponent(
                    component_type="nuget",
                    component_id=package_id,
                    version=version,
                    relationship=relationship,
                    scope=scope,
                    integrity_algorithm="nuget-content-sha512",
                    integrity_value=content_hash,
                    dependencies=tuple(sorted(set(dependency_specs))),
                    evidence_paths=(label,),
                )
                occurrences.setdefault(discovered.identity, []).append(discovered)

    grouped: dict[tuple[str, str], list[_DiscoveredComponent]] = {}
    for matches in occurrences.values():
        for component in matches:
            grouped.setdefault(
                (component.component_id.casefold(), component.version), []
            ).append(component)

    components: list[_DiscoveredComponent] = []
    for (_normalized_id, _version), matches in sorted(grouped.items()):
        first = matches[0]
        casings = {item.component_id for item in matches}
        hashes = {item.integrity_value for item in matches}
        if len(casings) != 1:
            validation.error(
                f"NuGet lock files have a case collision for {first.component_id}@{first.version}"
            )
        if len(hashes) != 1:
            validation.error(
                f"NuGet lock files disagree on contentHash for {first.component_id}@{first.version}"
            )
        # One SBOM component represents one package/version.  Runtime and direct
        # use take precedence when the same package also appears in test locks or
        # transitively; evidence paths and child edges remain the sorted union.
        components.append(
            replace(
                first,
                relationship=(
                    "direct"
                    if any(item.relationship == "direct" for item in matches)
                    else "transitive"
                ),
                scope=(
                    "runtime"
                    if any(item.scope == "runtime" for item in matches)
                    else "test-only"
                ),
                dependencies=tuple(
                    sorted(
                        {
                            dependency
                            for item in matches
                            for dependency in item.dependencies
                        }
                    )
                ),
                evidence_paths=tuple(
                    sorted({path for item in matches for path in item.evidence_paths})
                ),
            )
        )
    return sorted(components, key=_component_sort_key)


def _logical_requirement_lines(text: str, label: str, validation: _Validation) -> list[str]:
    logical: list[str] = []
    pending = ""
    for number, raw in enumerate(text.splitlines(), start=1):
        stripped = raw.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if stripped.endswith("\\"):
            pending += stripped[:-1].rstrip() + " "
            continue
        current = (pending + stripped).strip()
        pending = ""
        if current:
            logical.append(current)
        if len(current) > MAX_FIELD_LENGTH * 2:
            validation.error(f"{label}:{number}: requirement line is too long")
    if pending:
        validation.error(
            f"{label}: unterminated or unhashed requirement continuation"
        )
    return logical


def _discover_pypi(validation: _Validation) -> list[_DiscoveredComponent]:
    paths = _bounded_rglob(
        validation.root, "requirements*.txt", validation, "requirements"
    )

    found: dict[tuple[str, str, str], _DiscoveredComponent] = {}
    for path in paths:
        label = validation.relative(path)
        text = validation.read_text(path)
        if text is None:
            continue
        lines = _logical_requirement_lines(text, label, validation)
        required_options = (
            "--index-url=https://pypi.org/simple",
            "--only-binary=:all:",
            "--require-hashes",
        )
        for option in required_options:
            count = lines.count(option)
            if count != 1:
                validation.error(
                    f"{label}: {option} is required exactly once (found {count})"
                )
        for line in lines:
            if line.startswith("--"):
                if line not in required_options:
                    validation.error(f"{label}: unsupported requirements option")
                continue
            match = REQUIREMENT_RE.fullmatch(line)
            if match is None:
                validation.error(
                    f"{label}: requirements must use exact pinned == versions and SHA-256 hashes"
                )
                continue
            name = match.group("name")
            version = match.group("version")
            if not _validate_version(version, f"{label}:{name}", validation):
                continue
            hashes = HASH_OPTION_RE.findall(match.group("tail"))
            if len(hashes) != 1:
                validation.error(
                    f"{label}:{name}: exactly one governed SHA-256 hash is required"
                )
                continue
            digest = hashes[0].lower()
            key = (name.casefold(), version, digest)
            component = _DiscoveredComponent(
                component_type="pypi",
                component_id=name,
                version=version,
                relationship="direct",
                scope="build-only",
                integrity_algorithm="sha256",
                integrity_value=digest,
                dependencies=(),
                evidence_paths=(label,),
            )
            prior = found.get(key)
            if prior is not None and prior.component_id != name:
                validation.error(f"{label}:{name}: package ID casing is inconsistent")
            found[key] = component
    return sorted(found.values(), key=_component_sort_key)


def _extract_action_version(comment: str | None) -> str | None:
    if not comment:
        return None
    match = VERSION_COMMENT_RE.search(comment)
    return match.group(1) if match else None


def _discover_workflow_inputs(
    validation: _Validation,
) -> tuple[list[_DiscoveredComponent], list[_DiscoveredComponent]]:
    workflow_root = validation.root / ".github" / "workflows"
    paths = sorted(workflow_root.glob("*.yml")) + sorted(workflow_root.glob("*.yaml"))
    if not paths:
        validation.error(".github/workflows: no workflow files were found")
        return [], []
    if len(paths) > MAX_INPUT_FILES:
        validation.error(".github/workflows: too many workflow files")
        return [], []

    actions: dict[tuple[str, str, str], _DiscoveredComponent] = {}
    python_versions: set[str] = set()
    setup_python_seen = False
    for path in paths:
        label = validation.relative(path)
        text = validation.read_text(path)
        if text is None:
            continue
        for line_number, line in enumerate(text.splitlines(), start=1):
            match = USES_RE.fullmatch(line)
            if match is None:
                if re.match(r"^\s*(?:-\s*)?uses\s*:", line, re.IGNORECASE):
                    validation.error(
                        f"{label}:{line_number}: malformed or ungoverned action reference"
                    )
                continue
            target = match.group("target")
            if target.startswith("./"):
                continue
            if target.startswith("docker://"):
                validation.error(f"{label}:{line_number}: Docker actions are not governed")
                continue
            if "@" not in target:
                validation.error(f"{label}:{line_number}: action is missing a commit pin")
                continue
            action_id, revision = target.rsplit("@", 1)
            if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.\-/]+", action_id):
                validation.error(f"{label}:{line_number}: invalid GitHub action identity")
                continue
            revision = revision.lower()
            if not GIT_SHA1_RE.fullmatch(revision):
                validation.error(
                    f"{label}:{line_number}: GitHub actions must use a 40-character commit pin"
                )
                continue
            version = _extract_action_version(match.group("comment"))
            if version is None or not _validate_version(
                version, f"{label}:{line_number}", validation
            ):
                validation.error(
                    f"{label}:{line_number}: pinned action requires an exact version comment"
                )
                continue
            if action_id.casefold() == "actions/setup-python":
                setup_python_seen = True
            key = (action_id.casefold(), version, revision)
            component = _DiscoveredComponent(
                component_type="github-action",
                component_id=action_id,
                version=version,
                relationship="ci",
                scope="build-only",
                integrity_algorithm="git-commit-sha1",
                integrity_value=revision,
                dependencies=(),
                evidence_paths=(label,),
            )
            prior = actions.get(key)
            if prior is not None and prior.component_id != action_id:
                validation.error(f"{label}:{line_number}: action ID casing is inconsistent")
            if prior is not None:
                component = replace(
                    component,
                    evidence_paths=tuple(
                        sorted(set(prior.evidence_paths + component.evidence_paths))
                    ),
                )
            actions[key] = component

        for match in PYTHON_VERSION_RE.finditer(text):
            python_versions.add(match.group("version"))

    toolchains: list[_DiscoveredComponent] = []
    if setup_python_seen:
        if len(python_versions) != 1:
            validation.error(
                ".github/workflows: setup-python requires one exact governed Python version"
            )
        else:
            version = next(iter(python_versions))
            if _validate_version(version, ".github/workflows:python-version", validation):
                toolchains.append(
                    _DiscoveredComponent(
                        component_type="toolchain",
                        component_id="CPython",
                        version=version,
                        relationship="toolchain",
                        scope="build-only",
                        integrity_algorithm="version-pin",
                        integrity_value=".github/workflows/verify.yml#python-version",
                        dependencies=(),
                        evidence_paths=tuple(validation.relative(path) for path in paths),
                    )
                )
    return sorted(actions.values(), key=_component_sort_key), toolchains


def _discover_dotnet_sdk(validation: _Validation) -> list[_DiscoveredComponent]:
    path = validation.root / "global.json"
    value = _safe_json(path, validation, maximum=MAX_DOCUMENT_BYTES)
    if value is None:
        return []
    sdk = value.get("sdk")
    if not isinstance(sdk, dict):
        validation.error("global.json: sdk must be an object")
        return []
    version = sdk.get("version")
    if not isinstance(version, str) or not _validate_version(
        version, "global.json:sdk.version", validation
    ):
        return []
    return [
        _DiscoveredComponent(
            component_type="toolchain",
            component_id=".NET SDK",
            version=version,
            relationship="toolchain",
            scope="build-only",
            integrity_algorithm="version-pin",
            integrity_value="global.json#sdk.version",
            dependencies=(),
            evidence_paths=("global.json",),
        )
    ]


def _parse_catalog_identities(
    path: Path,
    text: str,
    integrity_value: str,
    validation: _Validation,
) -> list[_CatalogIdentity]:
    """Extract application identities safely before the TL-0301 catalog schema."""

    label = validation.relative(path)
    meaningful: list[tuple[int, str]] = []
    for line_number, line in enumerate(text.splitlines(), start=1):
        if "\t" in line or any(ord(character) < 32 for character in line):
            validation.error(f"{label}:{line_number}: catalog YAML contains a control character")
            continue
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if len(line) > MAX_FIELD_LENGTH:
            validation.error(f"{label}:{line_number}: catalog YAML line exceeds the length limit")
            continue
        if any(character in line for character in "[]{}"):
            validation.error(
                f"{label}:{line_number}: catalog YAML flow syntax is not allowed"
            )
            continue
        if re.search(r"(?:^|[\s:])[&*!]\S", line):
            validation.error(
                f"{label}:{line_number}: catalog YAML tags, anchors, and aliases are not allowed"
            )
            continue
        if re.search(r":\s*[>|][+-]?(?:\s+#.*)?$", line):
            validation.error(
                f"{label}:{line_number}: catalog YAML multiline scalars are not allowed"
            )
            continue
        meaningful.append((line_number, line))

    application_headers = [
        index for index, (_line_number, line) in enumerate(meaningful) if line == "applications:"
    ]
    if len(application_headers) != 1:
        validation.error(
            f"{label}: catalog YAML must contain exactly one top-level 'applications:' key"
        )
        return []

    item_re = re.compile(
        r"^  -(?: (?P<field>[A-Za-z0-9_-]+):(?: (?P<value>.*))?)?$"
    )
    field_re = re.compile(
        r"^    (?P<field>[A-Za-z0-9_-]+):(?: (?P<value>.*))?$"
    )
    exact_value_re = re.compile(r"[A-Za-z0-9][A-Za-z0-9._+-]*")
    identities: list[_CatalogIdentity] = []
    current: dict[str, str] | None = None
    current_line = 0

    def finish_current() -> None:
        nonlocal current
        if current is None:
            return
        if set(current) != {"id", "version"}:
            missing = sorted({"id", "version"} - set(current))
            validation.error(
                f"{label}:{current_line}: catalog application is missing fields {missing}"
            )
            current = None
            return
        component_id = current["id"]
        version = current["version"]
        if not PACKAGE_NAME_RE.fullmatch(component_id):
            validation.error(
                f"{label}:{current_line}: catalog application ID is not a safe exact token"
            )
        elif _validate_version(version, f"{label}:{current_line}:version", validation):
            identities.append(
                _CatalogIdentity(
                    component_id=component_id,
                    version=version,
                    evidence_path=label,
                    integrity_algorithm="sha256",
                    integrity_value=integrity_value,
                )
            )
        current = None

    in_applications = False
    for index, (line_number, line) in enumerate(meaningful):
        if index == application_headers[0]:
            in_applications = True
            continue
        if not in_applications:
            continue
        if line and not line[0].isspace():
            finish_current()
            in_applications = False
            continue
        match = item_re.fullmatch(line)
        if match is not None:
            finish_current()
            current = {}
            current_line = line_number
            field = match.group("field")
            if field in {"id", "version"}:
                value = match.group("value") or ""
                if exact_value_re.fullmatch(value) is None:
                    validation.error(
                        f"{label}:{line_number}: catalog {field} must be a safe exact token"
                    )
                else:
                    current[field] = value
            continue
        match = field_re.fullmatch(line)
        if match is not None and current is not None:
            field = match.group("field")
            if field not in {"id", "version"}:
                continue
            value = match.group("value") or ""
            if exact_value_re.fullmatch(value) is None:
                validation.error(
                    f"{label}:{line_number}: catalog {field} must be a safe exact token"
                )
            elif field in current:
                validation.error(
                    f"{label}:{line_number}: duplicate catalog application field {field!r}"
                )
            else:
                current[field] = value
            continue
        if current is None or not line.startswith("    "):
            validation.error(
                f"{label}:{line_number}: catalog application structure is ambiguous"
            )
    finish_current()
    if not identities:
        validation.error(f"{label}: catalog YAML must contain at least one application identity")
    return identities


def _catalog_inputs(validation: _Validation) -> tuple[_CatalogIdentity, ...]:
    catalog_root = validation.root / "fixtures" / "catalog"
    if not catalog_root.exists():
        return ()
    paths = _bounded_rglob(catalog_root, "*", validation, "fixtures/catalog")
    identities: list[_CatalogIdentity] = []
    for path in paths:
        if not path.is_file():
            continue
        label = validation.relative(path)
        if path.suffix.casefold() not in {".yaml", ".yml"}:
            validation.error(f"{label}: catalog bootstrap inputs must be YAML files")
            continue
        data = validation.read_bytes(path)
        if data is None:
            continue
        try:
            text = data.decode("utf-8-sig")
        except UnicodeDecodeError:
            validation.error(f"{label}: input must be UTF-8")
            continue
        identities.extend(
            _parse_catalog_identities(
                path,
                text,
                hashlib.sha256(data).hexdigest(),
                validation,
            )
        )

    seen: dict[tuple[str, str], _CatalogIdentity] = {}
    folded_ids: dict[tuple[str, str], tuple[str, str]] = {}
    for identity in identities:
        key = identity.identity
        folded_key = (identity.component_id.casefold(), identity.version.casefold())
        if key in seen:
            validation.error(
                f"fixtures/catalog: duplicate application identity {identity.component_id}@{identity.version}"
            )
        elif folded_key in folded_ids and folded_ids[folded_key] != key:
            validation.error(
                f"fixtures/catalog: case-colliding application identity {identity.component_id}@{identity.version}"
            )
        else:
            seen[key] = identity
            folded_ids[folded_key] = key
    return tuple(sorted(seen.values(), key=lambda item: (item.component_id.casefold(), item.version)))


def _record_build_graph_inputs(validation: _Validation) -> None:
    """Add central build and project graph files to the governed input digest."""

    required = (
        ".gitattributes",
        "Directory.Build.props",
        "Directory.Packages.props",
        "NuGet.Config",
        "ThirdLife.sln",
        "eng/generate-sbom.ps1",
        "eng/verify.ps1",
        "tools/supply_chain.py",
    )
    for relative_path in required:
        validation.read_bytes(validation.root / relative_path)
    project_paths = _bounded_rglob(
        validation.root / "src", "*.csproj", validation, "src project files"
    ) + _bounded_rglob(
        validation.root / "tests", "*.csproj", validation, "tests project files"
    )
    if not project_paths:
        validation.error("src/tests: no project files were found")
        return
    if len(project_paths) > MAX_INPUT_FILES:
        validation.error("src/tests: too many project files")
        return
    for path in project_paths:
        validation.read_bytes(path)


def _validate_https_url(value: str, label: str, validation: _Validation) -> None:
    if len(value) > MAX_FIELD_LENGTH:
        validation.error(f"{label}: URL is too long")
        return
    if (
        MACHINE_PATH_RE.search(value)
        or LATEST_RE.search(value)
        or "\\" in value
        or any(ord(character) < 32 or ord(character) == 127 for character in value)
    ):
        validation.error(f"{label}: URL contains an unsafe path or latest reference")
        return
    try:
        parsed = urllib.parse.urlsplit(value)
    except ValueError:
        validation.error(f"{label}: malformed URL")
        return
    if parsed.scheme != "https" or not parsed.hostname:
        validation.error(f"{label}: URL must use HTTPS with a host")
        return
    if parsed.username is not None or parsed.password is not None:
        validation.error(f"{label}: URL must not contain credentials")
    try:
        parsed.port
    except ValueError:
        validation.error(f"{label}: URL contains an invalid port")
    if parsed.query or parsed.fragment:
        validation.error(f"{label}: URL must not contain query or fragment data")
    try:
        host_ip = ipaddress.ip_address(parsed.hostname)
    except ValueError:
        host_ip = None
    if parsed.hostname.casefold() == "localhost" or (
        host_ip is not None
        and (
            host_ip.is_private
            or host_ip.is_loopback
            or host_ip.is_link_local
            or host_ip.is_unspecified
        )
    ):
        validation.error(f"{label}: URL host must be public")
    decoded_path = urllib.parse.unquote(parsed.path)
    if (
        ".." in Path(decoded_path).parts
        or "\\" in decoded_path
        or LATEST_RE.search(decoded_path)
        or MACHINE_PATH_RE.search(decoded_path)
    ):
        validation.error(f"{label}: URL path is unsafe")


def _validate_reference_list(
    value: str,
    label: str,
    validation: _Validation,
    *,
    allow_repository_paths: bool,
) -> None:
    """Validate semicolon-separated HTTPS and, where allowed, repository refs."""

    references = [part.strip() for part in value.split(";")]
    if not references or any(not part for part in references):
        validation.error(f"{label}: reference list contains a blank entry")
        return
    for reference in references:
        if reference.startswith("https://") or re.match(r"(?i)^https?:", reference):
            _validate_https_url(reference, label, validation)
            continue
        if not allow_repository_paths:
            validation.error(f"{label}: reference must use HTTPS")
            continue
        if (
            len(reference) > 512
            or reference.startswith(("/", "\\", ".\\", "../"))
            or "\\" in reference
            or ".." in Path(reference).parts
            or MACHINE_PATH_RE.search(reference)
            or LATEST_RE.search(reference)
            or not re.fullmatch(r"[A-Za-z0-9_.*#/+-]+(?:\.[A-Za-z0-9_.*#/+-]+)*", reference)
        ):
            validation.error(f"{label}: unsafe repository-relative provenance reference")


def _validate_rights(row: Mapping[str, str], row_number: int, validation: _Validation) -> None:
    installation = row["proposed_installation_rights"]
    redistribution = row["proposed_redistribution_rights"]
    placeholder_values = {"unknown", "not recorded", "tbd", "n/a", "none"}
    if installation.casefold() in placeholder_values:
        validation.error(
            f"{MATRIX_PATH.as_posix()}:{row_number}: installation rights must be explicit"
        )
    if redistribution.casefold() in placeholder_values:
        validation.error(
            f"{MATRIX_PATH.as_posix()}:{row_number}: redistribution rights must be explicit"
        )
    if re.search(r"(?i)\bredistribut(?:e|ion|able|ing)\b", installation):
        validation.error(
            f"{MATRIX_PATH.as_posix()}:{row_number}: installation rights must not assert redistribution rights"
        )
    if re.search(r"(?i)\binstall(?:ation|ed|ing)?\b", redistribution):
        validation.error(
            f"{MATRIX_PATH.as_posix()}:{row_number}: redistribution rights must not assert installation rights"
        )


def _validate_matrix_value(
    header: str, value: str, row_number: int, validation: _Validation
) -> None:
    label = f"{MATRIX_PATH.as_posix()}:{row_number}:{header}"
    if not value or not value.strip():
        validation.error(f"{label}: value must not be blank")
        return
    if value != value.strip():
        validation.error(f"{label}: leading or trailing whitespace is not allowed")
    if len(value) > MAX_FIELD_LENGTH:
        validation.error(f"{label}: value is too long")
    if any(ord(character) < 32 or ord(character) == 127 for character in value):
        validation.error(f"{label}: control characters are not allowed")
    if value.lstrip().startswith(FORMULA_PREFIXES):
        validation.error(f"{label}: spreadsheet-formula prefixes are not allowed")
    if MACHINE_PATH_RE.search(value):
        validation.error(f"{label}: machine-specific paths are not allowed")
    if LATEST_RE.search(value):
        validation.error(f"{label}: latest references are not allowed")


def _load_matrix(
    validation: _Validation,
) -> tuple[list[SupplyChainComponent], str]:
    path = validation.root / MATRIX_PATH
    data = validation.read_bytes(path, maximum=MAX_MATRIX_BYTES, record_input=False)
    if data is None:
        return [], ""
    matrix_digest = hashlib.sha256(data).hexdigest()
    try:
        text = data.decode("utf-8-sig")
    except UnicodeDecodeError:
        validation.error(f"{MATRIX_PATH.as_posix()}: input must be valid UTF-8")
        return [], matrix_digest
    try:
        reader = csv.DictReader(io.StringIO(text, newline=""), strict=True)
        if tuple(reader.fieldnames or ()) != MATRIX_HEADERS:
            validation.error(
                f"{MATRIX_PATH.as_posix()}: headers must exactly match the governed schema"
            )
            return [], matrix_digest
        raw_rows = list(reader)
    except csv.Error:
        validation.error(f"{MATRIX_PATH.as_posix()}: malformed CSV")
        return [], matrix_digest
    if len(raw_rows) > MAX_COMPONENTS:
        validation.error(f"{MATRIX_PATH.as_posix()}: too many component rows")
        return [], matrix_digest

    rows: list[dict[str, str]] = []
    for index, raw in enumerate(raw_rows, start=2):
        if None in raw or any(value is None for value in raw.values()):
            validation.error(f"{MATRIX_PATH.as_posix()}:{index}: malformed row width")
            continue
        row = {header: raw[header] for header in MATRIX_HEADERS}
        for header, value in row.items():
            _validate_matrix_value(header, value, index, validation)
        component_type = row["component_type"]
        relationship = row["relationship"]
        scope = row["scope"]
        if component_type not in ALLOWED_RELATIONSHIPS:
            validation.error(
                f"{MATRIX_PATH.as_posix()}:{index}: unsupported component_type"
            )
        elif relationship not in ALLOWED_RELATIONSHIPS[component_type]:
            validation.error(
                f"{MATRIX_PATH.as_posix()}:{index}: relationship is invalid for component_type"
            )
        if scope not in ALLOWED_SCOPES:
            validation.error(f"{MATRIX_PATH.as_posix()}:{index}: unsupported scope")
        elif component_type in EXPECTED_SCOPE_BY_TYPE and scope not in EXPECTED_SCOPE_BY_TYPE[
            component_type
        ]:
            validation.error(
                f"{MATRIX_PATH.as_posix()}:{index}: scope is invalid for component_type"
            )
        if (
            not re.fullmatch(r"[A-Za-z0-9.][A-Za-z0-9 ._/-]*", row["component_id"])
            or ".." in Path(row["component_id"]).parts
            or "\\" in row["component_id"]
        ):
            validation.error(f"{MATRIX_PATH.as_posix()}:{index}: invalid component_id")
        _validate_version(
            row["version"], f"{MATRIX_PATH.as_posix()}:{index}:version", validation
        )
        _validate_reference_list(
            row["source"],
            f"{MATRIX_PATH.as_posix()}:{index}:source",
            validation,
            allow_repository_paths=False,
        )
        _validate_reference_list(
            row["license_evidence"],
            f"{MATRIX_PATH.as_posix()}:{index}:license_evidence",
            validation,
            allow_repository_paths=False,
        )
        _validate_reference_list(
            row["provenance_reference"],
            f"{MATRIX_PATH.as_posix()}:{index}:provenance_reference",
            validation,
            allow_repository_paths=True,
        )
        _validate_rights(row, index, validation)
        for proposal_field in (
            "proposed_license_conclusion",
            "proposed_installation_rights",
            "proposed_redistribution_rights",
        ):
            if not row[proposal_field].startswith("Proposed "):
                validation.error(
                    f"{MATRIX_PATH.as_posix()}:{index}:{proposal_field}: "
                    "proposal values must begin with 'Proposed '"
                )
        rows.append(row)

    keys = [_matrix_row_sort_key(row) for row in rows]
    if keys != sorted(keys):
        validation.error(
            f"{MATRIX_PATH.as_posix()}: rows must be sorted by type, ID, version, relationship, and scope"
        )
    folded_identity: dict[tuple[str, ...], tuple[str, ...]] = {}
    for index, row in enumerate(rows, start=2):
        key = _matrix_row_sort_key(row)
        exact_key = (
            row["component_type"],
            row["component_id"],
            row["version"],
            row["relationship"],
            row["scope"],
        )
        if key in folded_identity:
            if folded_identity[key] != exact_key:
                validation.error(
                    f"{MATRIX_PATH.as_posix()}:{index}: case-colliding component row"
                )
            else:
                validation.error(f"{MATRIX_PATH.as_posix()}:{index}: duplicate component row")
        else:
            folded_identity[key] = exact_key

    components = [
        SupplyChainComponent(**{header: row[header] for header in MATRIX_HEADERS})
        for row in rows
    ]
    return components, matrix_digest


def _git_commit_exists(root: Path, revision: str) -> bool:
    try:
        completed = subprocess.run(
            ["git", "-C", os.fspath(root), "cat-file", "-e", f"{revision}^{{commit}}"],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
            timeout=GIT_TIMEOUT_SECONDS,
        )
    except (OSError, subprocess.TimeoutExpired):
        return False
    return completed.returncode == 0


def _git_file_digest(root: Path, revision: str, path: Path) -> str | None:
    object_name = f"{revision}:{path.as_posix()}"
    try:
        size_result = subprocess.run(
            ["git", "-C", os.fspath(root), "cat-file", "-s", object_name],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            check=False,
            timeout=GIT_TIMEOUT_SECONDS,
        )
        if size_result.returncode != 0:
            return None
        size = int(size_result.stdout.strip())
        if size > MAX_MATRIX_BYTES:
            return None
        content_result = subprocess.run(
            ["git", "-C", os.fspath(root), "show", object_name],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            check=False,
            timeout=GIT_TIMEOUT_SECONDS,
        )
    except (OSError, ValueError, subprocess.TimeoutExpired):
        return None
    if content_result.returncode != 0 or len(content_result.stdout) != size:
        return None
    return hashlib.sha256(content_result.stdout).hexdigest()


def _git_output(
    root: Path,
    arguments: Sequence[str],
    *,
    maximum: int = MAX_DOCUMENT_BYTES,
) -> bytes:
    try:
        completed = subprocess.run(
            ["git", "-C", os.fspath(root), *arguments],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            check=False,
            timeout=GIT_TIMEOUT_SECONDS,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise ValueError("source revision verification requires an available Git repository") from exc
    if completed.returncode != 0:
        raise ValueError("source revision verification failed for the checked-out Git repository")
    if len(completed.stdout) > maximum:
        raise ValueError("source revision verification exceeded its bounded Git output limit")
    return completed.stdout


def _is_governed_git_path(path_text: str) -> bool:
    path = Path(path_text)
    if path.is_absolute() or ".." in path.parts or _has_excluded_part(path, Path(".")):
        return False
    normalized = path.as_posix()
    if normalized in {
        "Directory.Build.props",
        "Directory.Packages.props",
        "NuGet.Config",
        "ThirdLife.sln",
        "global.json",
        ".gitattributes",
        "eng/generate-sbom.ps1",
        "eng/verify.ps1",
        "tools/supply_chain.py",
        DEPENDENCIES_PATH.as_posix(),
        MATRIX_PATH.as_posix(),
    }:
        return True
    if normalized.startswith(".github/workflows/"):
        return path.parent.as_posix() == ".github/workflows" and path.suffix.casefold() in {
            ".yml",
            ".yaml",
        }
    if path.name.casefold().startswith("requirements") and path.suffix.casefold() == ".txt":
        return True
    if normalized.startswith(("src/", "tests/")):
        return path.suffix.casefold() == ".csproj" or path.name == "packages.lock.json"
    return normalized.startswith("fixtures/catalog/")


def _committed_governed_paths(root: Path, revision: str) -> set[str]:
    output = _git_output(
        root,
        [
            "ls-tree",
            "-r",
            "--name-only",
            "-z",
            revision,
            "--",
            ".github/workflows",
            "src",
            "tests",
            "fixtures/catalog",
            "tools",
            "docs/supply-chain",
            "eng",
            ".gitattributes",
            "Directory.Build.props",
            "Directory.Packages.props",
            "NuGet.Config",
            "ThirdLife.sln",
            "global.json",
        ],
        maximum=MAX_GIT_LIST_BYTES,
    )
    try:
        paths = [item.decode("utf-8") for item in output.split(b"\0") if item]
    except UnicodeDecodeError as exc:
        raise ValueError("governed Git paths must be valid UTF-8") from exc
    governed = {path for path in paths if _is_governed_git_path(path)}
    if len(governed) > MAX_INPUT_FILES:
        raise ValueError("source revision contains too many governed input files")
    return governed


def _verify_result_inputs_unchanged(result: SupplyChainResult) -> set[str]:
    expected = dict(result.input_digests)
    expected[MATRIX_PATH.as_posix()] = result.matrix_digest
    for path_text, expected_digest in sorted(expected.items()):
        path = result.root / Path(path_text)
        try:
            resolved = path.resolve(strict=True)
            resolved.relative_to(result.root)
            data = resolved.read_bytes()
        except (OSError, ValueError) as exc:
            raise ValueError(
                f"governed input changed after validation: {path_text}"
            ) from exc
        if len(data) > MAX_TEXT_BYTES or hashlib.sha256(data).hexdigest() != expected_digest:
            raise ValueError(f"governed input changed after validation: {path_text}")
    return set(expected)


def _verify_source_revision(result: SupplyChainResult, revision: str) -> None:
    current_paths = _verify_result_inputs_unchanged(result)
    head = _git_output(
        result.root,
        ["rev-parse", "--verify", "HEAD^{commit}"],
        maximum=256,
    ).decode("ascii", errors="strict").strip()
    if head != revision:
        raise ValueError("source revision must exactly equal the checked-out HEAD commit")

    committed_paths = _committed_governed_paths(result.root, revision)
    if current_paths != committed_paths:
        missing = sorted(committed_paths - current_paths)
        uncommitted = sorted(current_paths - committed_paths)
        raise ValueError(
            "governed input set does not match the source revision commit; "
            f"missing={missing}, uncommitted={uncommitted}"
        )

    for path_text in sorted(current_paths):
        committed_oid = _git_output(
            result.root,
            ["rev-parse", "--verify", f"{revision}:{path_text}"],
            maximum=256,
        ).decode("ascii", errors="strict").strip()
        working_oid = _git_output(
            result.root,
            [
                "hash-object",
                f"--path={path_text}",
                os.fspath(result.root / Path(path_text)),
            ],
            maximum=256,
        ).decode("ascii", errors="strict").strip()
        if (
            GIT_OBJECT_RE.fullmatch(committed_oid) is None
            or GIT_OBJECT_RE.fullmatch(working_oid) is None
            or committed_oid != working_oid
        ):
            raise ValueError(
                f"governed input is dirty relative to the source revision commit: {path_text}"
            )


def _parse_review_table(
    validation: _Validation, matrix_digest: str
) -> str:
    path = validation.root / DEPENDENCIES_PATH
    text = validation.read_text(path, maximum=MAX_DOCUMENT_BYTES)
    if text is None:
        return "invalid"
    heading = "## Human licence and rights review"
    heading_positions = [
        index for index, line in enumerate(text.splitlines()) if line.strip() == heading
    ]
    if len(heading_positions) != 1:
        validation.error(
            f"{DEPENDENCIES_PATH.as_posix()}: expected one {heading!r} heading"
        )
        return "invalid"
    lines = text.splitlines()
    index = heading_positions[0] + 1
    expected_header = ["Field", "Value"]

    def cells(line: str) -> list[str]:
        stripped = line.strip()
        if not (stripped.startswith("|") and stripped.endswith("|")):
            return []
        return [part.strip() for part in stripped[1:-1].split("|")]

    while index < len(lines):
        stripped = lines[index].strip()
        if stripped.startswith("## "):
            break
        if cells(lines[index]) == expected_header:
            break
        index += 1
    if index >= len(lines) or cells(lines[index]) != expected_header:
        validation.error(
            f"{DEPENDENCIES_PATH.as_posix()}: review table header must be 'Field | Value'"
        )
        return "invalid"
    index += 1
    if index >= len(lines):
        validation.error(f"{DEPENDENCIES_PATH.as_posix()}: review table separator is missing")
        return "invalid"
    separator = cells(lines[index])
    if len(separator) != 2 or not all(re.fullmatch(r":?-{3,}:?", part) for part in separator):
        validation.error(f"{DEPENDENCIES_PATH.as_posix()}: malformed review table separator")
        return "invalid"
    index += 1

    values: dict[str, str] = {}
    while index < len(lines):
        row_cells = cells(lines[index])
        if not row_cells:
            break
        if len(row_cells) != 2:
            validation.error(f"{DEPENDENCIES_PATH.as_posix()}: malformed review table row")
            return "invalid"
        field, value = row_cells
        if field in values:
            validation.error(f"{DEPENDENCIES_PATH.as_posix()}: duplicate review field")
            return "invalid"
        values[field] = value
        index += 1
    if tuple(values) != REVIEW_FIELDS:
        missing = [field for field in REVIEW_FIELDS if field not in values]
        unexpected = [field for field in values if field not in REVIEW_FIELDS]
        validation.error(
            f"{DEPENDENCIES_PATH.as_posix()}: review fields must exactly match the governed schema; "
            f"missing={missing}, unexpected={unexpected}"
        )
        return "invalid"
    if values == PENDING_REVIEW:
        return "pending"
    if values["Review status"] != "Approved":
        validation.error(
            f"{DEPENDENCIES_PATH.as_posix()}: review status must be the exact pending tuple or Approved"
        )
        return "invalid"

    placeholders = {
        "",
        "n/a",
        "none",
        "not recorded",
        "pending",
        "tbd",
        "unknown",
        "unspecified",
    }
    for field in ("Reviewer", "Role"):
        value = values[field]
        if value.casefold() in placeholders or len(value) > 200:
            validation.error(f"{DEPENDENCIES_PATH.as_posix()}: approved {field} is invalid")
        if value.lstrip().startswith(FORMULA_PREFIXES) or MACHINE_PATH_RE.search(value):
            validation.error(f"{DEPENDENCIES_PATH.as_posix()}: approved {field} is unsafe")
    try:
        dt.date.fromisoformat(values["Review date"])
    except ValueError:
        validation.error(f"{DEPENDENCIES_PATH.as_posix()}: approved Review date must be ISO YYYY-MM-DD")
    if values["Result"] != "Approved without conditions":
        validation.error(
            f"{DEPENDENCIES_PATH.as_posix()}: approved Result must be 'Approved without conditions'"
        )
    revision = values["Reviewed commit"]
    reviewed_commit_matrix_digest: str | None = None
    if not GIT_SHA1_RE.fullmatch(revision):
        validation.error(f"{DEPENDENCIES_PATH.as_posix()}: Reviewed commit must be 40 lowercase hex characters")
    elif not _git_commit_exists(validation.root, revision):
        validation.error(f"{DEPENDENCIES_PATH.as_posix()}: Reviewed commit does not exist in this repository")
    else:
        reviewed_commit_matrix_digest = _git_file_digest(
            validation.root, revision, MATRIX_PATH
        )
        if reviewed_commit_matrix_digest is None:
            validation.error(
                f"{DEPENDENCIES_PATH.as_posix()}: cannot read the licence matrix at Reviewed commit"
            )
    reviewed_matrix = values["Matrix SHA-256"]
    if not SHA256_RE.fullmatch(reviewed_matrix):
        validation.error(f"{DEPENDENCIES_PATH.as_posix()}: Matrix SHA-256 must be 64 lowercase hex characters")
    elif not matrix_digest or reviewed_matrix != matrix_digest:
        validation.error(
            f"{DEPENDENCIES_PATH.as_posix()}: approved Matrix SHA-256 digest does not match current matrix"
        )
    if (
        reviewed_commit_matrix_digest is not None
        and SHA256_RE.fullmatch(reviewed_matrix)
        and reviewed_commit_matrix_digest != reviewed_matrix
    ):
        validation.error(
            f"{DEPENDENCIES_PATH.as_posix()}: Matrix SHA-256 does not match the matrix at Reviewed commit"
        )
    return "approved" if not validation.errors else "invalid"


def _aggregate_digest(items: Iterable[tuple[str, bytes]]) -> str:
    digest = hashlib.sha256()
    for path, data in sorted(items):
        content_digest = hashlib.sha256(data).hexdigest()
        digest.update(path.encode("utf-8"))
        digest.update(b"\0")
        digest.update(content_digest.encode("ascii"))
        digest.update(b"\n")
    return digest.hexdigest()


def _compare_inventory(
    discovered: Sequence[_DiscoveredComponent],
    matrix: Sequence[SupplyChainComponent],
    catalog_inputs: Sequence[_CatalogIdentity],
    validation: _Validation,
) -> tuple[SupplyChainComponent, ...]:
    discovered_by_key = {component.identity: component for component in discovered}
    matrix_by_key = {
        component.discovery_identity: component
        for component in matrix
        if component.component_type != "catalog-application"
    }
    discovered_keys = set(discovered_by_key)
    matrix_keys = set(matrix_by_key)
    missing = sorted(discovered_keys - matrix_keys)
    unexpected = sorted(matrix_keys - discovered_keys)
    for key in missing:
        validation.error(
            "license matrix is missing discovered dependency "
            f"{key[0]}:{key[1]}; version={key[2]}; relationship={key[3]}; "
            f"scope={key[4]}; integrity={key[5]}:{key[6]}"
        )
    for key in unexpected:
        validation.error(
            "license matrix has a stale/undiscovered dependency "
            f"{key[0]}:{key[1]}; version={key[2]}; relationship={key[3]}; "
            f"scope={key[4]}; integrity={key[5]}:{key[6]}"
        )

    catalog_rows = [item for item in matrix if item.component_type == "catalog-application"]
    catalog_by_identity = {item.identity: item for item in catalog_inputs}
    catalog_matrix_by_identity = {
        (item.component_id, item.version): item for item in catalog_rows
    }
    for identity in sorted(set(catalog_by_identity) - set(catalog_matrix_by_identity)):
        validation.error(
            "license matrix is missing catalog application "
            f"{identity[0]}@{identity[1]} discovered in fixtures/catalog"
        )
    for identity in sorted(set(catalog_matrix_by_identity) - set(catalog_by_identity)):
        validation.error(
            "license matrix has a stale/undiscovered catalog application "
            f"{identity[0]}@{identity[1]}"
        )
    for identity in sorted(set(catalog_by_identity) & set(catalog_matrix_by_identity)):
        catalog_identity = catalog_by_identity[identity]
        matrix_component = catalog_matrix_by_identity[identity]
        if matrix_component.integrity_algorithm != catalog_identity.integrity_algorithm:
            validation.error(
                "license matrix catalog application "
                f"{identity[0]}@{identity[1]} integrity_algorithm must equal sha256 "
                f"for {catalog_identity.evidence_path}"
            )
        elif matrix_component.integrity_value != catalog_identity.integrity_value:
            validation.error(
                "license matrix catalog application "
                f"{identity[0]}@{identity[1]} sha256 does not match exact source "
                f"fixture bytes at {catalog_identity.evidence_path}"
            )

    enriched: list[SupplyChainComponent] = []
    for component in matrix:
        if component.component_type == "catalog-application":
            catalog_identity = catalog_by_identity.get(
                (component.component_id, component.version)
            )
            enriched.append(
                replace(
                    component,
                    evidence_paths=(catalog_identity.evidence_path,)
                    if catalog_identity is not None
                    else (),
                )
            )
            continue
        discovered_component = discovered_by_key.get(component.discovery_identity)
        if discovered_component is None:
            enriched.append(component)
            continue
        enriched.append(
            replace(
                component,
                dependencies=discovered_component.dependencies,
                evidence_paths=discovered_component.evidence_paths,
            )
        )
    return tuple(sorted(enriched, key=_component_sort_key))


def _build_dependency_graph(
    inventory: Sequence[SupplyChainComponent], validation: _Validation
) -> dict[str, tuple[str, ...]]:
    refs: dict[str, SupplyChainComponent] = {}
    for component in inventory:
        prior = refs.get(component.bom_ref)
        if prior is not None:
            validation.error(
                "dependency inventory would emit a duplicate bom-ref for "
                f"{prior.component_id}@{prior.version}"
            )
        else:
            refs[component.bom_ref] = component
    by_nuget_target: dict[tuple[str, str], SupplyChainComponent] = {}
    for component in inventory:
        if component.component_type == "nuget":
            key = (component.component_id.casefold(), component.version)
            if key in by_nuget_target and by_nuget_target[key].bom_ref != component.bom_ref:
                validation.error(
                    f"dependency graph has ambiguous NuGet target {component.component_id}@{component.version}"
                )
            by_nuget_target[key] = component

    graph: dict[str, tuple[str, ...]] = {}
    for component in inventory:
        targets: list[str] = []
        for dependency in component.dependencies:
            child_id, separator, child_version = dependency.rpartition("@")
            target = by_nuget_target.get((child_id.casefold(), child_version))
            if not separator or target is None:
                validation.error(
                    f"dependency graph cannot resolve {component.component_id} -> {dependency}"
                )
                continue
            targets.append(target.bom_ref)
        graph[component.bom_ref] = tuple(sorted(set(targets)))
    return dict(sorted(graph.items()))


def validate_supply_chain(root: Path) -> SupplyChainResult:
    """Validate governed inputs under *root* without raising for expected errors."""

    try:
        normalized_root = Path(root).resolve(strict=True)
    except (OSError, RuntimeError):
        normalized_root = Path(root).absolute()
        return SupplyChainResult(
            root=normalized_root,
            inventory=(),
            dependency_graph={},
            lock_digest="",
            matrix_digest="",
            approval_state="invalid",
            errors=("repository root does not exist or cannot be resolved",),
        )
    if not normalized_root.is_dir():
        return SupplyChainResult(
            root=normalized_root,
            inventory=(),
            dependency_graph={},
            lock_digest="",
            matrix_digest="",
            approval_state="invalid",
            errors=("repository root is not a directory",),
        )

    validation = _Validation(normalized_root)
    _record_build_graph_inputs(validation)
    discovered: list[_DiscoveredComponent] = []
    discovered.extend(_discover_nuget(validation))
    discovered.extend(_discover_pypi(validation))
    actions, python_toolchains = _discover_workflow_inputs(validation)
    discovered.extend(actions)
    discovered.extend(python_toolchains)
    discovered.extend(_discover_dotnet_sdk(validation))
    catalog_inputs = _catalog_inputs(validation)

    if len(discovered) > MAX_COMPONENTS:
        validation.error("discovered dependency inventory exceeds the component limit")
        discovered = discovered[:MAX_COMPONENTS]
    discovered = sorted(discovered, key=_component_sort_key)

    matrix, matrix_digest = _load_matrix(validation)
    inventory = _compare_inventory(discovered, matrix, catalog_inputs, validation)
    graph = _build_dependency_graph(inventory, validation)
    approval_state = _parse_review_table(validation, matrix_digest)

    dependency_source_items = [
        (path, data)
        for path, data in validation.input_bytes.items()
        if path != DEPENDENCIES_PATH.as_posix()
    ]
    lock_digest = _aggregate_digest(dependency_source_items)
    input_digests = tuple(
        sorted(
            (path, hashlib.sha256(data).hexdigest())
            for path, data in validation.input_bytes.items()
        )
    )
    return SupplyChainResult(
        root=normalized_root,
        inventory=inventory,
        dependency_graph=graph,
        lock_digest=lock_digest,
        matrix_digest=matrix_digest,
        approval_state=approval_state,
        errors=tuple(validation.errors),
        input_digests=input_digests,
    )


def _component_properties(component: SupplyChainComponent) -> list[dict[str, str]]:
    values = {
        "thirdlife:declared-license": component.declared_license,
        "thirdlife:dependency-owner": component.owner,
        "thirdlife:distribution-plan": component.distribution_plan,
        "thirdlife:integrity-algorithm": component.integrity_algorithm,
        "thirdlife:integrity-value": component.integrity_value,
        "thirdlife:license-evidence": component.license_evidence,
        "thirdlife:limitations": component.limitations,
        "thirdlife:proposed-installation-rights": component.proposed_installation_rights,
        "thirdlife:proposed-license-conclusion": component.proposed_license_conclusion,
        "thirdlife:proposed-redistribution-rights": component.proposed_redistribution_rights,
        "thirdlife:provenance-reference": component.provenance_reference,
        "thirdlife:purpose": component.purpose,
        "thirdlife:relationship": component.relationship,
        "thirdlife:scope": component.scope,
        "thirdlife:source": component.source,
        "thirdlife:upstream-publisher": component.upstream_publisher,
    }
    for index, path in enumerate(component.evidence_paths, start=1):
        values[f"thirdlife:evidence-input:{index}"] = path
    return [{"name": name, "value": value} for name, value in sorted(values.items())]


def _purl(component: SupplyChainComponent) -> str | None:
    safe_name_characters = "/._-" if component.component_type == "github-action" else "._-"
    encoded_name = urllib.parse.quote(
        component.component_id, safe=safe_name_characters
    )
    encoded_version = urllib.parse.quote(component.version, safe="._-+")
    if component.component_type == "nuget":
        return f"pkg:nuget/{encoded_name}@{encoded_version}"
    if component.component_type == "pypi":
        return f"pkg:pypi/{encoded_name}@{encoded_version}"
    if component.component_type == "github-action":
        return f"pkg:github/{encoded_name}@{component.integrity_value}"
    return None


def _cyclonedx_component(component: SupplyChainComponent) -> dict[str, Any]:
    type_map = {
        "nuget": "library",
        "pypi": "library",
        "github-action": "application",
        "toolchain": "framework",
        "catalog-application": "application",
    }
    if component.declared_license in {"MIT", "Apache-2.0"}:
        license_choice: dict[str, Any] = {
            "expression": component.declared_license
        }
    else:
        license_choice = {"license": {"name": component.declared_license}}

    external_references: list[dict[str, str]] = []
    for reference_type, reference_list in (
        ("distribution", component.source),
        ("license", component.license_evidence),
        ("website", component.provenance_reference),
    ):
        for reference in (item.strip() for item in reference_list.split(";")):
            if reference.startswith("https://"):
                external_references.append(
                    {"type": reference_type, "url": reference}
                )

    value: dict[str, Any] = {
        "type": type_map[component.component_type],
        "bom-ref": component.bom_ref,
        "name": component.component_id,
        "version": component.version,
        "scope": (
            "required"
            if component.scope in {"runtime", "catalog-application"}
            else "excluded"
        ),
        "licenses": [license_choice],
        "properties": _component_properties(component),
        "externalReferences": [
            {"type": reference_type, "url": url}
            for reference_type, url in sorted(
                {(item["type"], item["url"]) for item in external_references}
            )
        ],
    }
    purl = _purl(component)
    if purl is not None:
        value["purl"] = purl
    # Only an actual artifact SHA-256 is a CycloneDX hash.  NuGet contentHash
    # and Git commit identities remain namespaced ThirdLife properties.
    if component.component_type == "pypi" and component.integrity_algorithm == "sha256":
        value["hashes"] = [{"alg": "SHA-256", "content": component.integrity_value}]
    return value


def _validate_output_metadata(product_version: str, source_revision: str | None) -> None:
    if not product_version or len(product_version) > 200 or not VERSION_RE.fullmatch(product_version):
        raise ValueError("product_version must be a bounded exact version token")
    if LATEST_RE.search(product_version) or MACHINE_PATH_RE.search(product_version):
        raise ValueError("product_version must not contain latest or a machine path")
    if source_revision is not None and not GIT_SHA1_RE.fullmatch(source_revision):
        raise ValueError("source_revision must be 40 lowercase hexadecimal characters")


def _canonicalize(value: Any) -> Any:
    if isinstance(value, dict):
        return {key: _canonicalize(value[key]) for key in sorted(value)}
    if isinstance(value, list):
        return [_canonicalize(item) for item in value]
    return value


def build_cyclonedx(
    result: SupplyChainResult,
    product_version: str = "0.3.0-dev",
    source_revision: str | None = None,
) -> dict[str, Any]:
    """Build a deterministic CycloneDX 1.6 object from a valid result."""

    if result.errors:
        raise ValueError("cannot build SBOM from an invalid supply-chain result")
    _validate_output_metadata(product_version, source_revision)
    if source_revision is not None:
        _verify_source_revision(result, source_revision)
    root_ref = "urn:thirdlife:product:setup-core"
    metadata_properties = {
        "thirdlife:dependency-input-sha256": result.lock_digest,
        "thirdlife:license-matrix-sha256": result.matrix_digest,
        "thirdlife:license-review-status": result.approval_state,
    }
    for path, digest in result.input_digests:
        metadata_properties[f"thirdlife:input-sha256:{path}"] = digest
    if source_revision is not None:
        metadata_properties["thirdlife:source-revision"] = source_revision

    direct_refs = sorted(
        component.bom_ref
        for component in result.inventory
        if component.relationship in {"direct", "ci", "toolchain"}
    )
    dependencies = [{"ref": root_ref, "dependsOn": direct_refs}]
    for component in result.inventory:
        dependencies.append(
            {
                "ref": component.bom_ref,
                "dependsOn": list(result.dependency_graph.get(component.bom_ref, ())),
            }
        )
    bom: dict[str, Any] = {
        "$schema": "http://cyclonedx.org/schema/bom-1.6.schema.json",
        "bomFormat": "CycloneDX",
        "specVersion": "1.6",
        "version": 1,
        "metadata": {
            "component": {
                "type": "application",
                "bom-ref": root_ref,
                "name": "ThirdLife Setup Core",
                "version": product_version,
            },
            "properties": [
                {"name": name, "value": value}
                for name, value in sorted(metadata_properties.items())
            ],
        },
        "components": [
            _cyclonedx_component(component)
            for component in sorted(result.inventory, key=lambda item: item.bom_ref)
        ],
        "dependencies": sorted(dependencies, key=lambda item: item["ref"]),
    }
    return _canonicalize(bom)


def write_cyclonedx(
    result: SupplyChainResult,
    output: Path,
    product_version: str = "0.3.0-dev",
    source_revision: str | None = None,
) -> Path:
    """Atomically write canonical UTF-8/LF CycloneDX JSON and return its path."""

    bom = build_cyclonedx(result, product_version, source_revision)
    payload = (
        json.dumps(bom, ensure_ascii=False, indent=2, sort_keys=True, allow_nan=False)
        + "\n"
    ).encode("utf-8")
    destination = Path(output)
    try:
        destination.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(
            mode="wb",
            prefix=f".{destination.name}.",
            suffix=".tmp",
            dir=destination.parent,
            delete=False,
        ) as temporary:
            temporary.write(payload)
            temporary.flush()
            os.fsync(temporary.fileno())
            temporary_path = Path(temporary.name)
        os.replace(temporary_path, destination)
    except OSError:
        try:
            temporary_path.unlink(missing_ok=True)
        except (OSError, UnboundLocalError):
            pass
        raise
    return destination


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Validate ThirdLife supply-chain controls and generate CycloneDX 1.6."
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
        help="repository root (defaults to the script's repository)",
    )
    parser.add_argument(
        "--output",
        type=Path,
        help="write an SBOM; omit to perform validation only",
    )
    parser.add_argument("--product-version", default="0.3.0-dev")
    parser.add_argument("--source-revision")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    result = validate_supply_chain(args.root)
    if result.errors:
        print("Supply-chain validation failed:", file=sys.stderr)
        for error in result.errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    if args.output is None:
        print(
            "Supply-chain validation passed: "
            f"components={len(result.inventory)}; "
            f"review={result.approval_state}; "
            f"input_sha256={result.lock_digest}; "
            f"matrix_sha256={result.matrix_digest}"
        )
        return 0
    try:
        destination = write_cyclonedx(
            result,
            args.output,
            product_version=args.product_version,
            source_revision=args.source_revision,
        )
    except (OSError, ValueError) as exc:
        print(f"SBOM generation failed: {exc}", file=sys.stderr)
        return 1
    print(
        f"Wrote CycloneDX 1.6 SBOM: {destination}; "
        f"components={len(result.inventory)}; review={result.approval_state}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
