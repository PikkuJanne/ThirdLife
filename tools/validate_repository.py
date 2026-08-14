#!/usr/bin/env python3
"""Validate deterministic build controls and the ThirdLife project boundary."""

from __future__ import annotations

import base64
import binascii
import csv
import hashlib
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from datetime import date
from pathlib import Path
from typing import Any
from urllib.parse import urlsplit

import yaml

import generate_sbom
from validate_bundle import REQUIRED_FILES


ROOT = Path(__file__).resolve().parents[1]
SOLUTION = ROOT / "ThirdLife.sln"
EXPECTED_SDK = {
    "version": "10.0.400",
    "rollForward": "disable",
    "allowPrerelease": False,
}
REQUIRED_PACKAGE_IDS = {
    "coverlet.collector",
    "Microsoft.NET.Test.Sdk",
    "xunit",
    "xunit.runner.visualstudio",
}
EXPECTED_PRODUCTION_PROJECTS = {
    "ThirdLife.Actions",
    "ThirdLife.Broker",
    "ThirdLife.Broker.Protocol",
    "ThirdLife.Catalog",
    "ThirdLife.Core",
    "ThirdLife.Diagnostics",
    "ThirdLife.Inventory",
    "ThirdLife.Packages",
    "ThirdLife.Persistence",
    "ThirdLife.Policy",
    "ThirdLife.Reports",
    "ThirdLife.UI",
    "ThirdLife.Verification",
}
EXPECTED_TEST_PROJECTS = {
    "ThirdLife.Actions.Tests",
    "ThirdLife.Broker.SecurityTests",
    "ThirdLife.Broker.Tests",
    "ThirdLife.Catalog.Tests",
    "ThirdLife.Core.Tests",
    "ThirdLife.Diagnostics.Tests",
    "ThirdLife.Inventory.Tests",
    "ThirdLife.Packages.Tests",
    "ThirdLife.Persistence.Tests",
    "ThirdLife.Policy.Tests",
    "ThirdLife.Reports.Tests",
    "ThirdLife.UI.Tests",
    "ThirdLife.Verification.Tests",
}
EXPECTED_ACTIONS = {
    "actions/checkout": "3d3c42e5aac5ba805825da76410c181273ba90b1",
    "actions/setup-dotnet": "a98b56852c35b8e3190ac28c8c2271da59106c68",
    "actions/setup-python": "5fda3b95a4ea91299a34e894583c3862153e4b97",
}
EXPECTED_PIP_INSTALL_COMMAND = (
    "python -m pip install --no-deps --require-hashes --only-binary=:all: "
    "--index-url https://pypi.org/simple --requirement tools/requirements.txt"
)
EXPECTED_README_PIP_INSTALL_COMMAND = (
    r".\.venv\Scripts\python.exe -m pip install --no-deps --require-hashes "
    r"--only-binary=:all: --index-url https://pypi.org/simple "
    r"--requirement tools\requirements.txt"
)
LICENSE_MATRIX_HEADER = (
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
ALLOWED_MATRIX_VALUES = {
    "ecosystem": {"nuget", "pypi", "github_action", "toolchain"},
    "dependency_class": {
        "runtime",
        "build_only",
        "test_only",
        "catalog_application",
    },
    "directness": {"direct", "transitive", "platform"},
    "installation_rights": {
        "permitted",
        "prohibited",
        "pending_human_review",
        "not_applicable",
    },
    "redistribution_rights": {
        "permitted",
        "prohibited",
        "pending_human_review",
        "not_applicable",
    },
    "bundled_in_release": {"yes", "no", "pending"},
    "review_status": {"pending", "approved", "rejected"},
    "content_hash_algorithm": {"SHA-256", "SHA-512", "not_recorded"},
}
EXPECTED_MATRIX_COMPONENT_COUNT = 20
MATRIX_ECOSYSTEM_ORDER = {
    "nuget": 0,
    "pypi": 1,
    "github_action": 2,
    "toolchain": 3,
}
SUPPLY_CHAIN_METADATA_FIELDS = (
    "Status",
    "Review result",
    "Reviewing owner and role",
    "Review date",
    "Reviewed source commit",
    "Reviewed matrix SHA-256",
    "Generated SBOM SHA-256",
    "Approval reference",
)
MACHINE_PATH_RE = re.compile(
    r"(?i)(?:\b[a-z]:[\\/]|\\\\[^\\/\s]+[\\/][^\\/\s]+|"
    r"file:/+(?:[a-z]:|/)|/(?:home|users)/[^/\s]+/)"
)


class Validation:
    def __init__(self) -> None:
        self.errors: list[str] = []

    def error(self, message: str) -> None:
        self.errors.append(message)


def relative(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


def load_xml(path: Path, validation: Validation) -> ET.Element | None:
    try:
        return ET.parse(path).getroot()
    except (OSError, ET.ParseError) as exc:
        validation.error(f"{relative(path)}: cannot parse XML: {exc}")
        return None


def load_json(path: Path, validation: Validation) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        validation.error(f"{relative(path)}: cannot parse JSON: {exc}")
        return {}
    if not isinstance(value, dict):
        validation.error(f"{relative(path)}: top-level value must be an object")
        return {}
    return value


def validate_bundle_manifest(validation: Validation) -> None:
    manifest_path = ROOT / "BUNDLE_MANIFEST.sha256"
    expected_paths = set(REQUIRED_FILES) | {"tools/validate_bundle.py"}
    entries: dict[str, str] = {}

    try:
        lines = manifest_path.read_text(encoding="utf-8").splitlines()
    except OSError as exc:
        validation.error(f"BUNDLE_MANIFEST.sha256: cannot read: {exc}")
        return

    for line_number, line in enumerate(lines, start=1):
        match = re.fullmatch(r"([0-9a-f]{64})  ([^\r\n]+)", line)
        if match is None:
            validation.error(
                f"BUNDLE_MANIFEST.sha256:{line_number}: invalid SHA-256 entry"
            )
            continue
        digest, path_text = match.groups()
        path = Path(path_text)
        if path.is_absolute() or ".." in path.parts:
            validation.error(
                f"BUNDLE_MANIFEST.sha256:{line_number}: unsafe path {path_text!r}"
            )
            continue
        if path_text in entries:
            validation.error(f"BUNDLE_MANIFEST.sha256: duplicate path {path_text}")
            continue
        entries[path_text] = digest

    if list(entries) != sorted(entries):
        validation.error("BUNDLE_MANIFEST.sha256: entries must be path-sorted")
    if set(entries) != expected_paths:
        missing = sorted(expected_paths - set(entries))
        unexpected = sorted(set(entries) - expected_paths)
        validation.error(
            "BUNDLE_MANIFEST.sha256: coverage mismatch; "
            f"missing={missing}, unexpected={unexpected}"
        )

    for path_text, expected_digest in entries.items():
        path = ROOT / path_text
        try:
            actual_digest = hashlib.sha256(path.read_bytes()).hexdigest()
        except OSError as exc:
            validation.error(f"{path_text}: cannot hash: {exc}")
            continue
        if actual_digest != expected_digest:
            validation.error(
                f"BUNDLE_MANIFEST.sha256: digest mismatch for {path_text}"
            )


def solution_project_paths(validation: Validation) -> set[str]:
    try:
        text = SOLUTION.read_text(encoding="utf-8-sig")
    except OSError as exc:
        validation.error(f"ThirdLife.sln: cannot read: {exc}")
        return set()
    matches = re.findall(
        r'^Project\("\{[^}]+\}"\) = "[^"]+", "([^"]+\.csproj)"',
        text,
        flags=re.MULTILINE | re.IGNORECASE,
    )
    return {Path(match.replace("\\", "/")).as_posix() for match in matches}


def validate_project_graph(validation: Validation) -> list[Path]:
    project_paths = sorted((ROOT / "src").rglob("*.csproj")) + sorted(
        (ROOT / "tests").rglob("*.csproj")
    )
    disk_paths = {relative(path) for path in project_paths}
    listed_paths = solution_project_paths(validation)
    if disk_paths != listed_paths:
        validation.error(
            "ThirdLife.sln: project membership differs from disk; "
            f"missing={sorted(disk_paths - listed_paths)}, "
            f"unexpected={sorted(listed_paths - disk_paths)}"
        )

    production_names = {
        path.stem for path in project_paths if relative(path).startswith("src/")
    }
    test_names = {
        path.stem for path in project_paths if relative(path).startswith("tests/")
    }
    if production_names != EXPECTED_PRODUCTION_PROJECTS:
        validation.error(
            "src: production project boundary mismatch; "
            f"missing={sorted(EXPECTED_PRODUCTION_PROJECTS - production_names)}, "
            f"unexpected={sorted(production_names - EXPECTED_PRODUCTION_PROJECTS)}"
        )
    if test_names != EXPECTED_TEST_PROJECTS:
        validation.error(
            "tests: test project boundary mismatch; "
            f"missing={sorted(EXPECTED_TEST_PROJECTS - test_names)}, "
            f"unexpected={sorted(test_names - EXPECTED_TEST_PROJECTS)}"
        )

    project_by_path = {path.resolve(): path for path in project_paths}
    edges: dict[Path, set[Path]] = defaultdict(set)
    for project_path in project_paths:
        root = load_xml(project_path, validation)
        if root is None:
            continue
        use_wpf = [
            (element.text or "").strip().lower()
            for element in root.findall(".//UseWPF")
        ]
        expected_wpf = project_path.stem == "ThirdLife.UI"
        if any(value == "true" for value in use_wpf) != expected_wpf:
            validation.error(
                f"{relative(project_path)}: UseWPF=true must appear only on ThirdLife.UI"
            )

        for reference in root.findall(".//ProjectReference"):
            include = reference.get("Include")
            if not include:
                validation.error(
                    f"{relative(project_path)}: ProjectReference must have Include"
                )
                continue
            target = (project_path.parent / include).resolve()
            if target not in project_by_path:
                validation.error(
                    f"{relative(project_path)}: ProjectReference leaves the solution: {include}"
                )
                continue
            if relative(project_path).startswith("src/") and relative(
                project_by_path[target]
            ).startswith("tests/"):
                validation.error(
                    f"{relative(project_path)}: production code cannot reference tests"
                )
            edges[project_path.resolve()].add(target)

    visiting: set[Path] = set()
    visited: set[Path] = set()

    def visit(node: Path, trail: list[Path]) -> None:
        if node in visiting:
            cycle = trail[trail.index(node) :] + [node]
            validation.error(
                "ProjectReference cycle: "
                + " -> ".join(project_by_path[item].stem for item in cycle)
            )
            return
        if node in visited:
            return
        visiting.add(node)
        for target in sorted(edges[node]):
            visit(target, trail + [target])
        visiting.remove(node)
        visited.add(node)

    for project in sorted(project_by_path):
        visit(project, [project])
    return project_paths


def property_text(root: ET.Element, name: str) -> str | None:
    element = root.find(f".//{name}")
    return None if element is None or element.text is None else element.text.strip()


def validate_central_packages(
    project_paths: list[Path], validation: Validation
) -> None:
    build_props_path = ROOT / "Directory.Build.props"
    package_props_path = ROOT / "Directory.Packages.props"
    build_props = load_xml(build_props_path, validation)
    package_props = load_xml(package_props_path, validation)
    if build_props is None or package_props is None:
        return

    if property_text(build_props, "RestorePackagesWithLockFile") != "true":
        validation.error(
            "Directory.Build.props: RestorePackagesWithLockFile must equal true"
        )
    expected_central_properties = {
        "ManagePackageVersionsCentrally": "true",
        "CentralPackageVersionOverrideEnabled": "false",
        "CentralPackageTransitivePinningEnabled": "false",
    }
    for name, expected in expected_central_properties.items():
        if property_text(package_props, name) != expected:
            validation.error(
                f"Directory.Packages.props: {name} must equal {expected}"
            )

    versions: dict[str, tuple[str, str]] = {}
    for element in package_props.findall(".//PackageVersion"):
        package_id = element.get("Include")
        version = element.get("Version")
        if not package_id or not version:
            validation.error(
                "Directory.Packages.props: PackageVersion needs Include and Version"
            )
            continue
        key = package_id.casefold()
        if key in versions:
            validation.error(
                f"Directory.Packages.props: duplicate package {package_id}"
            )
        versions[key] = (package_id, version)

    for package_id in REQUIRED_PACKAGE_IDS:
        if package_id.casefold() not in versions:
            validation.error(
                f"Directory.Packages.props: required package {package_id} is missing"
            )
    exact_version_re = re.compile(
        r"^[0-9]+(?:\.[0-9]+){1,3}"
        r"(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?"
        r"(?:\+[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$"
    )
    for package_id, version in versions.values():
        if exact_version_re.fullmatch(version) is None:
            validation.error(
                f"Directory.Packages.props: {package_id} must use an exact version"
            )

    for project_path in project_paths:
        root = load_xml(project_path, validation)
        if root is None:
            continue
        references: list[str] = []
        for reference in root.findall(".//PackageReference"):
            package_id = reference.get("Include")
            if not package_id:
                validation.error(
                    f"{relative(project_path)}: PackageReference needs Include"
                )
                continue
            if (
                reference.get("Version") is not None
                or reference.get("VersionOverride") is not None
                or reference.find("Version") is not None
                or reference.find("VersionOverride") is not None
            ):
                validation.error(
                    f"{relative(project_path)}: {package_id} bypasses central versioning"
                )
            if package_id.casefold() not in versions:
                validation.error(
                    f"{relative(project_path)}: {package_id} lacks a central version"
                )
            references.append(package_id)

        lock_path = project_path.with_name("packages.lock.json")
        lock = load_json(lock_path, validation)
        if not lock:
            continue
        if lock.get("version") != 2:
            validation.error(f"{relative(lock_path)}: lock version must equal 2")
        frameworks = lock.get("dependencies")
        if not isinstance(frameworks, dict) or not frameworks:
            validation.error(
                f"{relative(lock_path)}: dependencies must contain a target framework"
            )
            continue
        for framework_name, dependencies in frameworks.items():
            if not isinstance(dependencies, dict):
                validation.error(
                    f"{relative(lock_path)}: {framework_name} dependencies must be an object"
                )
                continue
            lowered = {key.casefold(): value for key, value in dependencies.items()}
            for package_id in references:
                entry = lowered.get(package_id.casefold())
                central = versions.get(package_id.casefold())
                if not isinstance(entry, dict) or entry.get("type") != "Direct":
                    validation.error(
                        f"{relative(lock_path)}: {package_id} is not locked as Direct"
                    )
                elif central is not None and entry.get("resolved") != central[1]:
                    validation.error(
                        f"{relative(lock_path)}: {package_id} resolved version differs from central pin"
                    )
            for package_id, entry in dependencies.items():
                if not isinstance(entry, dict):
                    validation.error(
                        f"{relative(lock_path)}: invalid entry for {package_id}"
                    )
                elif entry.get("type") != "Project" and not entry.get("contentHash"):
                    validation.error(
                        f"{relative(lock_path)}: {package_id} lacks a content hash"
                    )


def validate_nuget_audit_policy(
    project_paths: list[Path], validation: Validation
) -> None:
    """Require one repository-wide audit policy and reject local bypasses."""

    build_props_path = ROOT / "Directory.Build.props"
    build_props = load_xml(build_props_path, validation)
    if build_props is None:
        return

    expected_properties = {
        "NuGetAudit": "true",
        "NuGetAuditMode": "all",
        "NuGetAuditLevel": "low",
        "TreatWarningsAsErrors": "true",
    }
    for property_name, expected_value in expected_properties.items():
        elements = build_props.findall(f".//{property_name}")
        if len(elements) != 1 or (elements[0].text or "").strip() != expected_value:
            validation.error(
                f"Directory.Build.props: {property_name} must occur exactly once "
                f"and equal {expected_value}"
            )
        elif elements[0].attrib:
            validation.error(
                f"Directory.Build.props: {property_name} must be unconditional"
            )

    configuration_paths = {
        ROOT / "Directory.Build.props",
        ROOT / "Directory.Packages.props",
        *project_paths,
    }
    excluded_build_directories = {".git", ".venv", "bin", "obj"}
    for pattern in ("*.csproj", "*.props", "*.targets"):
        for path in ROOT.rglob(pattern):
            parts = path.relative_to(ROOT).parts
            if not any(part.casefold() in excluded_build_directories for part in parts):
                configuration_paths.add(path)
    audit_warning_re = re.compile(r"(?i)(?:\bNU19|\bNU(?:1)?\*)")
    for path in sorted(configuration_paths):
        root = load_xml(path, validation)
        if root is None:
            continue
        def reject_conditioned_audit_properties(
            element: ET.Element, inherited_condition: bool = False
        ) -> None:
            has_condition = inherited_condition or any(
                attribute.rsplit("}", 1)[-1].casefold() == "condition"
                and value.strip()
                for attribute, value in element.attrib.items()
            )
            tag = element.tag.rsplit("}", 1)[-1]
            if tag in expected_properties and has_condition:
                validation.error(
                    f"{relative(path)}: {tag} must not be inside a conditional XML context"
                )
            for child in element:
                reject_conditioned_audit_properties(child, has_condition)

        reject_conditioned_audit_properties(root)
        for element in root.iter():
            tag = element.tag.rsplit("}", 1)[-1]
            text = (element.text or "").strip()
            if tag.casefold() == "import":
                validation.error(
                    f"{relative(path)}: explicit MSBuild Import elements are prohibited"
                )
            if tag == "NuGetAuditSuppress":
                validation.error(
                    f"{relative(path)}: NuGetAuditSuppress entries are prohibited"
                )
            if tag in {"NoWarn", "WarningsNotAsErrors"} and audit_warning_re.search(
                text
            ):
                validation.error(
                    f"{relative(path)}: {tag} must not suppress NuGet audit warnings"
                )
            if path != ROOT / "Directory.Build.props" and tag in {
                "NuGetAudit",
                "NuGetAuditMode",
                "NuGetAuditLevel",
            }:
                validation.error(
                    f"{relative(path)}: project-local {tag} overrides are prohibited"
                )

    command_paths = [
        ROOT / ".github" / "workflows" / "verify.yml",
        ROOT / "eng" / "verify.ps1",
        ROOT / "eng" / "verify.sh",
    ]
    bypass_re = re.compile(
        r"(?i)(?:NuGetAudit\s*(?:=|:)\s*false|"
        r"NuGetAuditMode\s*(?:=|:)\s*direct|"
        r"NuGetAuditLevel\s*(?:=|:)\s*(?:moderate|high|critical)|"
        r"(?:NoWarn|WarningsNotAsErrors)[^\r\n]*(?:\bNU19|\bNU(?:1)?\*))"
    )
    audit_override_token_re = re.compile(
        r"(?i)\bNuGetAudit(?:Mode|Level)?\b"
    )
    for path in command_paths:
        try:
            text = path.read_text(encoding="utf-8-sig")
        except OSError as exc:
            validation.error(f"{relative(path)}: cannot scan audit policy: {exc}")
            continue
        if audit_override_token_re.search(text):
            validation.error(
                f"{relative(path)}: NuGetAudit override tokens are prohibited in "
                "verification command surfaces"
            )
        if bypass_re.search(text):
            validation.error(
                f"{relative(path)}: NuGet audit suppression or weakening is prohibited"
            )


def validate_tool_and_source_configuration(validation: Validation) -> None:
    global_json = load_json(ROOT / "global.json", validation)
    if global_json.get("sdk") != EXPECTED_SDK:
        validation.error(
            f"global.json: sdk must exactly equal {EXPECTED_SDK!r}"
        )

    config_path = ROOT / "NuGet.Config"
    config = load_xml(config_path, validation)
    if config is not None:
        expected_source = (
            "nuget.org",
            "https://api.nuget.org/v3/index.json",
            "3",
        )
        package_sources = config.findall("./packageSources/add")
        actual_sources = [
            (source.get("key"), source.get("value"), source.get("protocolVersion"))
            for source in package_sources
        ]
        expected_sources = {
            expected_source
        }
        if set(actual_sources) != expected_sources or len(actual_sources) != 1 or config.find(
            "./packageSources/clear"
        ) is None:
            validation.error(
                "NuGet.Config: packageSources must contain only cleared, mapped nuget.org v3"
            )
        audit_sources = config.findall("./auditSources/add")
        actual_audit_sources = [
            (source.get("key"), source.get("value"), source.get("protocolVersion"))
            for source in audit_sources
        ]
        if (
            set(actual_audit_sources) != expected_sources
            or len(actual_audit_sources) != 1
            or config.find("./auditSources/clear") is None
        ):
            validation.error(
                "NuGet.Config: auditSources must contain only cleared nuget.org v3"
            )
        mappings = config.findall("./packageSourceMapping/packageSource")
        if (
            len(mappings) != 1
            or mappings[0].get("key") != "nuget.org"
            or [package.attrib for package in mappings[0].findall("package")]
            != [{"pattern": "*"}]
        ):
            validation.error(
                "NuGet.Config: source mapping must contain only nuget.org pattern *"
            )
        disabled_children = list(config.findall("./disabledPackageSources/*"))
        if len(disabled_children) != 1 or disabled_children[0].tag != "clear":
            validation.error("NuGet.Config: disabled package sources must be cleared")
        fallback_children = list(config.findall("./fallbackPackageFolders/*"))
        if len(fallback_children) != 1 or fallback_children[0].tag != "clear":
            validation.error("NuGet.Config: fallback package folders must be cleared")
        if config.find("./packageSourceCredentials") is not None:
            validation.error("NuGet.Config: package source credentials are prohibited")


def _decode_nuget_content_hash(
    value: object, lock_path: Path, package_id: str, validation: Validation
) -> str:
    if not isinstance(value, str) or not value:
        validation.error(
            f"{relative(lock_path)}: {package_id} needs a base64 SHA-512 contentHash"
        )
        return ""
    try:
        decoded = base64.b64decode(value, validate=True)
    except (ValueError, binascii.Error):
        validation.error(
            f"{relative(lock_path)}: {package_id} contentHash is not valid base64"
        )
        return ""
    if len(decoded) != 64:
        validation.error(
            f"{relative(lock_path)}: {package_id} contentHash must decode to SHA-512"
        )
        return ""
    return decoded.hex()


def _normalized_python_name(name: str) -> str:
    return re.sub(r"[-_.]+", "-", name).casefold()


def _load_python_requirements(validation: Validation) -> list[dict[str, str]]:
    requirements_path = ROOT / "tools" / "requirements.txt"
    try:
        lines = requirements_path.read_text(encoding="utf-8").splitlines()
    except OSError as exc:
        validation.error(f"tools/requirements.txt: cannot read: {exc}")
        return []

    requirement_re = re.compile(
        r"(?P<name>[A-Za-z0-9][A-Za-z0-9._-]*)=="
        r"(?P<version>[0-9]+(?:\.[0-9]+){1,3}(?:[-+][0-9A-Za-z.-]+)?)"
        r"\s+--hash=sha256:(?P<digest>[0-9a-f]{64})"
    )
    requirements: list[dict[str, str]] = []
    seen: set[str] = set()
    for line_number, line in enumerate(lines, start=1):
        if not line or line.startswith("#"):
            continue
        match = requirement_re.fullmatch(line)
        if match is None:
            validation.error(
                "tools/requirements.txt:"
                f"{line_number}: requirement must be exact and carry one lowercase SHA-256 hash"
            )
            continue
        requirement = match.groupdict()
        normalized_name = _normalized_python_name(requirement["name"])
        if normalized_name in seen:
            validation.error(
                f"tools/requirements.txt:{line_number}: duplicate requirement {requirement['name']}"
            )
            continue
        seen.add(normalized_name)
        requirements.append(requirement)
    if not requirements:
        validation.error("tools/requirements.txt: at least one pinned requirement is required")
    return requirements


def _collect_nuget_inventory(validation: Validation) -> dict[str, dict[str, str]]:
    observed: dict[str, dict[str, Any]] = {}
    lock_paths = sorted((ROOT / "src").rglob("packages.lock.json")) + sorted(
        (ROOT / "tests").rglob("packages.lock.json")
    )
    if not lock_paths:
        validation.error("Supply-chain inventory: no NuGet lock files were found")
        return {}

    for lock_path in lock_paths:
        lock = load_json(lock_path, validation)
        frameworks = lock.get("dependencies")
        if not isinstance(frameworks, dict):
            continue
        location_class = (
            "runtime" if relative(lock_path).startswith("src/") else "test_only"
        )
        for dependencies in frameworks.values():
            if not isinstance(dependencies, dict):
                continue
            for package_id, entry in dependencies.items():
                if not isinstance(package_id, str) or not isinstance(entry, dict):
                    continue
                package_type = entry.get("type")
                if package_type == "Project":
                    continue
                if package_type not in {"Direct", "Transitive"}:
                    validation.error(
                        f"{relative(lock_path)}: {package_id} has unsupported lock type {package_type!r}"
                    )
                    continue
                version = entry.get("resolved")
                if not isinstance(version, str) or not version:
                    validation.error(
                        f"{relative(lock_path)}: {package_id} lacks a resolved version"
                    )
                    continue
                content_hash = _decode_nuget_content_hash(
                    entry.get("contentHash"), lock_path, package_id, validation
                )
                key = package_id.casefold()
                current = observed.setdefault(
                    key,
                    {
                        "name": package_id,
                        "version": version,
                        "content_hash": content_hash,
                        "types": set(),
                        "classes": set(),
                    },
                )
                if (
                    current["name"] != package_id
                    or current["version"] != version
                    or current["content_hash"] != content_hash
                ):
                    validation.error(
                        f"Supply-chain inventory: inconsistent NuGet lock data for {package_id}"
                    )
                current["types"].add(package_type)
                current["classes"].add(location_class)

    result: dict[str, dict[str, str]] = {}
    for key, package in observed.items():
        dependency_class = (
            "runtime" if "runtime" in package["classes"] else "test_only"
        )
        directness = "direct" if "Direct" in package["types"] else "transitive"
        component_id = f"nuget:{key}"
        result[component_id] = {
            "component_id": component_id,
            "ecosystem": "nuget",
            "name": package["name"],
            "version": package["version"],
            "dependency_class": dependency_class,
            "directness": directness,
            "source_uri": (
                "https://www.nuget.org/packages/"
                f"{package['name']}/{package['version']}"
            ),
            "content_hash_algorithm": "SHA-512",
            "content_hash": package["content_hash"],
            "bundled_in_release": "no",
            "pin_marker": "packages.lock.json",
        }
    return result


def _load_workflow_document(validation: Validation) -> dict[str, Any]:
    workflow_path = ROOT / ".github" / "workflows" / "verify.yml"
    try:
        workflow = yaml.load(
            workflow_path.read_text(encoding="utf-8"), Loader=yaml.BaseLoader
        )
    except (OSError, yaml.YAMLError) as exc:
        validation.error(f".github/workflows/verify.yml: cannot parse YAML: {exc}")
        return {}
    if not isinstance(workflow, dict):
        validation.error(".github/workflows/verify.yml: top level must be a mapping")
        return {}
    return workflow


def expected_supply_chain_inventory(
    validation: Validation,
) -> dict[str, dict[str, str]]:
    expected = _collect_nuget_inventory(validation)

    for requirement in _load_python_requirements(validation):
        normalized_name = _normalized_python_name(requirement["name"])
        component_id = f"pypi:{normalized_name}"
        expected[component_id] = {
            "component_id": component_id,
            "ecosystem": "pypi",
            "name": requirement["name"],
            "version": requirement["version"],
            "dependency_class": "build_only",
            "directness": "direct",
            "source_uri": (
                f"https://pypi.org/project/{requirement['name']}/"
                f"{requirement['version']}/"
            ),
            "content_hash_algorithm": "SHA-256",
            "content_hash": requirement["digest"],
            "bundled_in_release": "no",
            "pin_marker": "tools/requirements.txt",
        }

    global_json = load_json(ROOT / "global.json", validation)
    sdk = global_json.get("sdk")
    sdk_version = sdk.get("version") if isinstance(sdk, dict) else None
    if isinstance(sdk_version, str) and sdk_version:
        expected["toolchain:dotnet-sdk"] = {
            "component_id": "toolchain:dotnet-sdk",
            "ecosystem": "toolchain",
            "name": "Microsoft .NET SDK",
            "version": sdk_version,
            "dependency_class": "build_only",
            "directness": "platform",
            "source_uri": "https://github.com/dotnet/sdk",
            "content_hash_algorithm": "not_recorded",
            "content_hash": "not_recorded",
            "bundled_in_release": "no",
            "pin_marker": "global.json",
        }

    workflow = _load_workflow_document(validation)
    jobs = workflow.get("jobs")
    job = jobs.get("verify") if isinstance(jobs, dict) else None
    steps = job.get("steps") if isinstance(job, dict) else None
    python_version = ""
    if isinstance(steps, list):
        for step in steps:
            if not isinstance(step, dict):
                continue
            use = step.get("uses")
            if isinstance(use, str) and "@" in use:
                action, revision = use.rsplit("@", 1)
                component_id = f"github_action:{action.casefold()}"
                expected[component_id] = {
                    "component_id": component_id,
                    "ecosystem": "github_action",
                    "name": action,
                    "version": revision,
                    "dependency_class": "build_only",
                    "directness": "direct",
                    "source_uri": f"https://github.com/{action}",
                    "content_hash_algorithm": "not_recorded",
                    "content_hash": "not_recorded",
                    "bundled_in_release": "no",
                    "pin_marker": ".github/workflows/verify.yml",
                }
            if isinstance(use, str) and use.startswith("actions/setup-python@"):
                with_values = step.get("with")
                if isinstance(with_values, dict):
                    value = with_values.get("python-version")
                    if isinstance(value, str):
                        python_version = value
    if python_version:
        expected["toolchain:python"] = {
            "component_id": "toolchain:python",
            "ecosystem": "toolchain",
            "name": "CPython",
            "version": python_version,
            "dependency_class": "build_only",
            "directness": "platform",
            "source_uri": "https://github.com/python/cpython",
            "content_hash_algorithm": "not_recorded",
            "content_hash": "not_recorded",
            "bundled_in_release": "no",
            "pin_marker": ".github/workflows/verify.yml",
        }
    else:
        validation.error(
            "Supply-chain inventory: setup-python must expose the governed Python version"
        )
    return expected


def _read_license_matrix(validation: Validation) -> list[dict[str, str]]:
    matrix_path = ROOT / "docs" / "supply-chain" / "license-matrix.csv"
    try:
        raw = matrix_path.read_bytes()
        text = raw.decode("utf-8")
    except (OSError, UnicodeDecodeError) as exc:
        validation.error(f"docs/supply-chain/license-matrix.csv: cannot read UTF-8: {exc}")
        return []
    if raw.startswith(b"\xef\xbb\xbf"):
        validation.error(
            "docs/supply-chain/license-matrix.csv: UTF-8 BOM is prohibited"
        )
    if b"\r" in raw:
        validation.error(
            "docs/supply-chain/license-matrix.csv: line endings must be LF"
        )
    try:
        parsed = list(csv.reader(text.splitlines()))
    except csv.Error as exc:
        validation.error(f"docs/supply-chain/license-matrix.csv: invalid CSV: {exc}")
        return []
    if not parsed:
        validation.error("docs/supply-chain/license-matrix.csv: file is empty")
        return []
    if tuple(parsed[0]) != LICENSE_MATRIX_HEADER:
        validation.error(
            "docs/supply-chain/license-matrix.csv: header must exactly equal the governed 21-column schema"
        )
        return []

    rows: list[dict[str, str]] = []
    for line_number, values in enumerate(parsed[1:], start=2):
        if len(values) != len(LICENSE_MATRIX_HEADER):
            validation.error(
                f"docs/supply-chain/license-matrix.csv:{line_number}: expected "
                f"{len(LICENSE_MATRIX_HEADER)} fields, found {len(values)}"
            )
            continue
        row = dict(zip(LICENSE_MATRIX_HEADER, values, strict=True))
        for field, value in row.items():
            if not value:
                validation.error(
                    f"docs/supply-chain/license-matrix.csv:{line_number}: {field} must not be empty"
                )
            if value != value.strip():
                validation.error(
                    f"docs/supply-chain/license-matrix.csv:{line_number}: {field} has surrounding whitespace"
                )
            if len(value) > 2048 or any(ord(character) < 32 for character in value):
                validation.error(
                    f"docs/supply-chain/license-matrix.csv:{line_number}: {field} contains unsafe or unbounded text"
                )
        rows.append(row)
    return rows


def _validate_reference_text(
    value: str, field: str, component_id: str, validation: Validation
) -> None:
    if MACHINE_PATH_RE.search(value):
        validation.error(
            f"docs/supply-chain/license-matrix.csv: {component_id} {field} contains a machine path"
        )
    if "?" in value or re.search(r"https://[^/\s:@]+:[^@/\s]+@", value):
        validation.error(
            f"docs/supply-chain/license-matrix.csv: {component_id} {field} contains a query or credentials"
        )


def validate_license_matrix(
    expected: dict[str, dict[str, str]], validation: Validation
) -> list[dict[str, str]]:
    rows = _read_license_matrix(validation)
    if len(rows) != EXPECTED_MATRIX_COMPONENT_COUNT:
        validation.error(
            "docs/supply-chain/license-matrix.csv: expected exactly "
            f"{EXPECTED_MATRIX_COMPONENT_COUNT} component rows, found {len(rows)}"
        )

    component_ids = [row["component_id"] for row in rows]
    expected_order = sorted(
        rows,
        key=lambda row: (
            MATRIX_ECOSYSTEM_ORDER.get(row["ecosystem"], 99),
            row["component_id"],
        ),
    )
    if rows != expected_order:
        validation.error(
            "docs/supply-chain/license-matrix.csv: rows must be sorted by governed ecosystem order and component_id"
        )
    if len(component_ids) != len(set(component_ids)):
        validation.error(
            "docs/supply-chain/license-matrix.csv: component_id values must be unique"
        )
    matrix_by_id = {row["component_id"]: row for row in rows}
    if set(matrix_by_id) != set(expected):
        validation.error(
            "docs/supply-chain/license-matrix.csv: inventory mismatch; "
            f"missing={sorted(set(expected) - set(matrix_by_id))}, "
            f"unexpected={sorted(set(matrix_by_id) - set(expected))}"
        )

    seen_coordinates: set[tuple[str, str, str]] = set()
    placeholder_re = re.compile(r"(?i)^(?:pending|tbd|unknown|n/?a)$")
    for row in rows:
        component_id = row["component_id"]
        if re.fullmatch(r"[a-z0-9._-]+:[a-z0-9._/-]+", component_id) is None:
            validation.error(
                f"docs/supply-chain/license-matrix.csv: invalid normalized component_id {component_id!r}"
            )
        coordinate = (
            row["ecosystem"],
            row["name"].casefold(),
            row["version"],
        )
        if coordinate in seen_coordinates:
            validation.error(
                f"docs/supply-chain/license-matrix.csv: duplicate component coordinate {coordinate!r}"
            )
        seen_coordinates.add(coordinate)

        for field, allowed in ALLOWED_MATRIX_VALUES.items():
            if row[field] not in allowed:
                validation.error(
                    f"docs/supply-chain/license-matrix.csv: {component_id} {field} "
                    f"must be one of {sorted(allowed)}"
                )
        algorithm = row["content_hash_algorithm"]
        content_hash = row["content_hash"]
        if algorithm == "not_recorded":
            if content_hash != "not_recorded":
                validation.error(
                    f"docs/supply-chain/license-matrix.csv: {component_id} must pair not_recorded hash fields"
                )
        else:
            expected_length = 64 if algorithm == "SHA-256" else 128
            if re.fullmatch(rf"[0-9a-f]{{{expected_length}}}", content_hash) is None:
                validation.error(
                    f"docs/supply-chain/license-matrix.csv: {component_id} has an invalid {algorithm} digest"
                )

        for field in (
            "internal_owner",
            "upstream_owner",
            "purpose",
            "license_expression",
            "license_evidence",
            "notice_requirements",
            "review_notes",
        ):
            if placeholder_re.fullmatch(row[field]):
                validation.error(
                    f"docs/supply-chain/license-matrix.csv: {component_id} {field} cannot be a placeholder"
                )
        if re.fullmatch(r"(?:[A-Za-z0-9.+-]+|LicenseRef-[A-Za-z0-9.-]+)(?:\s+(?:AND|OR)\s+(?:[A-Za-z0-9.+-]+|LicenseRef-[A-Za-z0-9.-]+))*", row["license_expression"]) is None:
            validation.error(
                f"docs/supply-chain/license-matrix.csv: {component_id} license_expression is not a bounded SPDX-style expression"
            )

        source = urlsplit(row["source_uri"])
        if (
            source.scheme != "https"
            or not source.netloc
            or source.username is not None
            or source.password is not None
            or source.query
            or source.fragment
        ):
            validation.error(
                f"docs/supply-chain/license-matrix.csv: {component_id} source_uri must be credential-free HTTPS without query or fragment"
            )
        _validate_reference_text(
            row["provenance_reference"],
            "provenance_reference",
            component_id,
            validation,
        )
        _validate_reference_text(
            row["license_evidence"],
            "license_evidence",
            component_id,
            validation,
        )
        expected_row = expected.get(component_id)
        if expected_row is not None:
            for field in (
                "ecosystem",
                "name",
                "version",
                "dependency_class",
                "directness",
                "source_uri",
                "content_hash_algorithm",
                "content_hash",
                "bundled_in_release",
            ):
                if row[field] != expected_row[field]:
                    validation.error(
                        f"docs/supply-chain/license-matrix.csv: {component_id} {field} "
                        f"must equal {expected_row[field]!r}"
                    )
            if expected_row["pin_marker"] not in row["provenance_reference"]:
                validation.error(
                    f"docs/supply-chain/license-matrix.csv: {component_id} provenance_reference "
                    f"must cite {expected_row['pin_marker']}"
                )
        if row["review_status"] == "approved" and (
            row["installation_rights"] == "pending_human_review"
            or row["redistribution_rights"] == "pending_human_review"
        ):
            validation.error(
                f"docs/supply-chain/license-matrix.csv: {component_id} approved review cannot retain pending rights"
            )
        if row["review_status"] == "rejected" and (
            row["installation_rights"] != "prohibited"
            and row["redistribution_rights"] != "prohibited"
        ):
            validation.error(
                f"docs/supply-chain/license-matrix.csv: {component_id} rejected review needs a prohibited right"
            )
        if row["bundled_in_release"] == "yes" and (
            row["redistribution_rights"] != "permitted"
            or row["review_status"] != "approved"
        ):
            validation.error(
                f"docs/supply-chain/license-matrix.csv: {component_id} cannot be bundled without approved redistribution rights"
            )
    return rows


def _supply_chain_metadata(
    document_text: str, validation: Validation
) -> dict[str, str]:
    metadata: dict[str, str] = {}
    for field in SUPPLY_CHAIN_METADATA_FIELDS:
        matches = re.findall(
            rf"^\*\*{re.escape(field)}:\*\*\s*(.+?)\s*$",
            document_text,
            flags=re.MULTILINE,
        )
        if len(matches) != 1:
            validation.error(
                "docs/supply-chain/dependencies.md: "
                f"metadata field {field!r} must occur exactly once"
            )
            metadata[field] = ""
        else:
            metadata[field] = matches[0].strip()
    return metadata


def _git_commit_is_reachable(commit: str) -> bool:
    try:
        exists = subprocess.run(
            ["git", "-C", str(ROOT), "cat-file", "-e", f"{commit}^{{commit}}"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        if exists.returncode != 0:
            return False
        reachable = subprocess.run(
            ["git", "-C", str(ROOT), "merge-base", "--is-ancestor", commit, "HEAD"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
    except OSError:
        return False
    return reachable.returncode == 0


def _load_supply_chain_task(validation: Validation) -> dict[str, Any]:
    task_path = ROOT / "TASKS.yaml"
    try:
        document = yaml.safe_load(task_path.read_text(encoding="utf-8"))
    except (OSError, yaml.YAMLError) as exc:
        validation.error(f"TASKS.yaml: cannot load TL-0006 approval state: {exc}")
        return {}
    tasks = document.get("tasks") if isinstance(document, dict) else None
    if not isinstance(tasks, list):
        validation.error("TASKS.yaml: tasks must be a list for TL-0006 approval state")
        return {}
    matches = [
        task
        for task in tasks
        if isinstance(task, dict) and task.get("id") == "TL-0006"
    ]
    if len(matches) != 1:
        validation.error("TASKS.yaml: TL-0006 must occur exactly once")
        return {}
    return matches[0]


def _validate_supply_chain_document_markers(
    document_text: str, validation: Validation
) -> None:
    lowered = document_text.casefold()
    literal_markers = (
        "**authority:** derived control record",
        "not a new authority tier",
        "| `runtime` | 0 | 0 | 0 | 0 |",
        "| `catalog_application` | 0 | 0 | 0 | 0 |",
        "directory.packages.props",
        "packages.lock.json",
        "nuget.config",
        "--locked-mode",
        "installation rights",
        "redistribution rights",
        "installation and redistribution are separate decisions",
        "cyclonedx 1.7",
        "offline, deterministic cyclonedx 1.7",
        "development sbom",
        "claim that the development sbom describes a future installer",
        "release_interface.md",
        "## vulnerability review",
        "### limitations",
    )
    for marker in literal_markers:
        if marker not in lowered:
            validation.error(
                f"docs/supply-chain/dependencies.md: missing required marker {marker!r}"
            )

    vulnerability_date_match = re.search(
        r"(?im)^On\s+(\d{4}-\d{2}-\d{2}),\s+the following Windows command",
        document_text,
    )
    if vulnerability_date_match is None:
        validation.error(
            "docs/supply-chain/dependencies.md: missing ISO vulnerability review date"
        )
    else:
        try:
            review_date = date.fromisoformat(vulnerability_date_match.group(1))
            if review_date > date.today():
                validation.error(
                    "docs/supply-chain/dependencies.md: vulnerability review date cannot be in the future"
                )
        except ValueError:
            validation.error(
                "docs/supply-chain/dependencies.md: vulnerability review date is invalid"
            )


def _approval_evidence_matches(
    task: dict[str, Any], metadata: dict[str, str]
) -> bool:
    evidence = task.get("evidence")
    if not isinstance(evidence, list):
        return False
    owner = re.split(
        r"\s+(?:—|-|\||,)\s+",
        metadata["Reviewing owner and role"],
        maxsplit=1,
    )[0].strip()
    required_values = (
        owner.casefold(),
        metadata["Reviewed source commit"].casefold(),
        metadata["Reviewed matrix SHA-256"].casefold(),
        metadata["Generated SBOM SHA-256"].casefold(),
    )
    for entry in evidence:
        if not isinstance(entry, dict) or entry.get("result") != "passed":
            continue
        if str(entry.get("date", "")) != metadata["Review date"]:
            continue
        environment = str(entry.get("environment", "")).strip()
        if not environment or environment.casefold() in {"pending", "tbd", "unknown"}:
            continue
        combined = " ".join(
            str(entry.get(field, ""))
            for field in ("summary", "reference", "environment")
        ).casefold()
        if "approv" not in combined or _approval_is_negated_or_declined(combined):
            continue
        if all(value and value in combined for value in required_values):
            return True
    return False


NEGATED_OR_DECLINED_APPROVAL_RE = re.compile(
    r"(?ix)(?:"
    r"\b(?:did|does|do|has|have|had|is|are|was|were)\s+not\s+"
    r"(?:yet\s+)?approv(?:e|ed|ing)\b|"
    r"\bnot\s+(?:yet\s+)?approved\b|"
    r"\bno\s+(?:(?:named|human|owner|licen[cs]e(?:-owner)?|supply-chain)\s+){0,3}"
    r"approval\b|"
    r"\bno\s+(?:(?:named|human|owner|licen[cs]e(?:-owner)?|supply-chain)\s+){0,3}"
    r"approv(?:e|ed)\b|"
    r"\bwithout\s+(?:(?:named|human|owner|licen[cs]e(?:-owner)?|supply-chain)\s+){0,3}"
    r"approval\b|"
    r"\b(?:approval|review)\s+(?:is\s+|was\s+|remains?\s+)?"
    r"(?:pending|declined|denied|rejected|withheld|revoked)\b|"
    r"\bapproval\s+(?:is\s+|was\s+|has\s+been\s+)?not\s+(?:yet\s+)?"
    r"(?:approved|given|granted|recorded)\b|"
    r"\b(?:pending|declined|denied|rejected|withheld|revoked)\s+"
    r"(?:(?:named|human|owner|licen[cs]e(?:-owner)?|supply-chain)\s+){0,3}"
    r"approval\b|"
    r"\b(?:refused?|failed)\s+to\s+approve\b"
    r")"
)


def _approval_is_negated_or_declined(text: str) -> bool:
    return NEGATED_OR_DECLINED_APPROVAL_RE.search(text) is not None


RETAINED_PENDING_APPROVAL_WORDING_RE = re.compile(
    r"(?im)(?:"
    r"^\*\*Control revision:\*\*[^\r\n]*\bdraft\b|"
    r"^\*\*Review state:\*\*[^\r\n]*\bPending\b|"
    r"\bno approval has been recorded\b|"
    r"\bno licence or redistribution approval is recorded\b|"
    r"\bevery current rights field is `pending_human_review`\b|"
    r"\b(?:expression|row)\s+remains\s+`NOASSERTION`|"
    r"\bat this draft revision\b|"
    r"\b(?:human licence(?: and separate-rights)?|licen[cs]e-owner|supply-chain) "
    r"review (?:is|remains) pending\b|"
    r"\bNOASSERTION licence expressions pending exact review\b"
    r")"
)


def _has_retained_pending_approval_wording(document_text: str) -> bool:
    return RETAINED_PENDING_APPROVAL_WORDING_RE.search(document_text) is not None


def _current_development_sbom_digest(validation: Validation) -> str:
    try:
        snapshot = generate_sbom.Snapshot.load(ROOT)
        content = generate_sbom.build_sbom(snapshot, release=False)
    except (OSError, generate_sbom.SbomError) as exc:
        validation.error(
            "Unable to compute the current deterministic development SBOM digest: "
            f"{exc}"
        )
        return ""
    return hashlib.sha256(content).hexdigest()


def validate_supply_chain_approval(
    rows: list[dict[str, str]], validation: Validation
) -> None:
    document_path = ROOT / "docs" / "supply-chain" / "dependencies.md"
    try:
        document_text = document_path.read_text(encoding="utf-8")
    except OSError as exc:
        validation.error(f"docs/supply-chain/dependencies.md: cannot read: {exc}")
        return
    _validate_supply_chain_document_markers(document_text, validation)
    metadata = _supply_chain_metadata(document_text, validation)
    task = _load_supply_chain_task(validation)
    if any(not metadata[field] for field in SUPPLY_CHAIN_METADATA_FIELDS):
        return

    status = metadata["Status"]
    review_result = metadata["Review result"].removesuffix(".")
    task_status = task.get("status")
    pending_fields = SUPPLY_CHAIN_METADATA_FIELDS[2:]
    no_approval = re.search(
        r"(?is)\bno\b.{0,120}\b(?:licen[cs]e|supply-chain)\b.{0,80}\bapproval\b",
        document_text,
    )
    pending_review_disclaimer = re.search(
        r"(?im)(?:^\*\*Review state:\*\*\s*\*\*?Pending\b|\bno approval has been recorded\b)",
        document_text,
    )
    if review_result == "Pending":
        if status != "Draft for licence-owner review":
            validation.error(
                "docs/supply-chain/dependencies.md: Pending review must retain Draft for licence-owner review status"
            )
        if any(metadata[field].removesuffix(".") != "Pending" for field in pending_fields):
            validation.error(
                "docs/supply-chain/dependencies.md: Pending review metadata must remain Pending"
            )
        no_release = re.search(
            r"(?is)\bnot\b.{0,140}\b(?:a\s+)?release (?:approval|authorization|authorisation)\b",
            document_text,
        )
        if no_approval is None or no_release is None:
            validation.error(
                "docs/supply-chain/dependencies.md: Pending review needs an explicit no-approval/no-release disclaimer"
            )
        if any(
            row["review_status"] != "pending"
            or row["installation_rights"] != "pending_human_review"
            or row["redistribution_rights"] != "pending_human_review"
            for row in rows
        ):
            validation.error(
                "docs/supply-chain/license-matrix.csv: Pending document requires pending review and both pending rights on every row"
            )
        if task_status == "done":
            validation.error(
                "TL-0006 cannot be done while licence-owner review is Pending"
            )
        return

    if review_result not in {"Approved", "ApprovedWithConditions"}:
        validation.error(
            "docs/supply-chain/dependencies.md: Review result must be Pending, Approved, or ApprovedWithConditions"
        )
        return
    if status != "Approved":
        validation.error(
            "docs/supply-chain/dependencies.md: an approved review requires Status Approved"
        )
    if (
        no_approval is not None
        or pending_review_disclaimer is not None
        or _has_retained_pending_approval_wording(document_text)
    ):
        validation.error(
            "docs/supply-chain/dependencies.md: approved review must remove "
            "draft/Pending/no-approval current-state wording"
        )

    owner_and_role = metadata["Reviewing owner and role"]
    if (
        owner_and_role.casefold() == "pending"
        or len(owner_and_role) < 8
        or re.search(r"(?i)\b(?:licen[cs]e|supply-chain)\b", owner_and_role) is None
    ):
        validation.error(
            "docs/supply-chain/dependencies.md: approved review needs a named licence or supply-chain owner and role"
        )
    try:
        review_date = date.fromisoformat(metadata["Review date"])
        if review_date > date.today():
            validation.error(
                "docs/supply-chain/dependencies.md: approval date cannot be in the future"
            )
    except ValueError:
        validation.error(
            "docs/supply-chain/dependencies.md: approved review needs a real ISO review date"
        )

    reviewed_commit = metadata["Reviewed source commit"]
    if re.fullmatch(r"[0-9a-f]{40}", reviewed_commit) is None:
        validation.error(
            "docs/supply-chain/dependencies.md: approved review needs an exact lowercase 40-hex source commit"
        )
    elif not _git_commit_is_reachable(reviewed_commit):
        validation.error(
            "docs/supply-chain/dependencies.md: reviewed source commit must be reachable from HEAD"
        )
    matrix_digest = metadata["Reviewed matrix SHA-256"]
    sbom_digest = metadata["Generated SBOM SHA-256"]
    for label, digest in (
        ("Reviewed matrix SHA-256", matrix_digest),
        ("Generated SBOM SHA-256", sbom_digest),
    ):
        if re.fullmatch(r"[0-9a-f]{64}", digest) is None:
            validation.error(
                f"docs/supply-chain/dependencies.md: {label} must be lowercase 64-hex"
            )
    if re.fullmatch(r"[0-9a-f]{64}", sbom_digest):
        current_sbom_digest = _current_development_sbom_digest(validation)
        if current_sbom_digest and current_sbom_digest != sbom_digest:
            validation.error(
                "docs/supply-chain/dependencies.md: generated SBOM digest does not "
                "match the current deterministic development SBOM"
            )
    matrix_path = ROOT / "docs" / "supply-chain" / "license-matrix.csv"
    try:
        actual_matrix_digest = hashlib.sha256(matrix_path.read_bytes()).hexdigest()
        if re.fullmatch(r"[0-9a-f]{64}", matrix_digest) and (
            actual_matrix_digest != matrix_digest
        ):
            validation.error(
                "docs/supply-chain/dependencies.md: reviewed matrix digest does not match the current matrix"
            )
    except OSError as exc:
        validation.error(
            f"docs/supply-chain/license-matrix.csv: cannot hash approval artifact: {exc}"
        )

    approval_reference = metadata["Approval reference"]
    owner_name = re.split(
        r"\s+(?:—|-|\||,)\s+", owner_and_role, maxsplit=1
    )[0].strip()
    if (
        len(approval_reference) < 12
        or "approv" not in approval_reference.casefold()
        or owner_name.casefold() not in approval_reference.casefold()
        or _approval_is_negated_or_declined(approval_reference)
    ):
        validation.error(
            "docs/supply-chain/dependencies.md: approval reference must affirm the named owner's approval"
        )
    if any(
        row["review_status"] != "approved"
        or row["installation_rights"] == "pending_human_review"
        or row["redistribution_rights"] == "pending_human_review"
        for row in rows
    ):
        validation.error(
            "docs/supply-chain/license-matrix.csv: approved document requires approved rows and nonpending separate rights"
        )
    for row in rows:
        component_id = row["component_id"]
        if row["installation_rights"] not in {"permitted", "not_applicable"}:
            validation.error(
                "docs/supply-chain/license-matrix.csv: approved review must satisfy "
                f"the release gate; {component_id} installation_rights cannot be "
                f"{row['installation_rights']}"
            )
        if row["redistribution_rights"] not in {
            "permitted",
            "prohibited",
            "not_applicable",
        }:
            validation.error(
                "docs/supply-chain/license-matrix.csv: approved review must satisfy "
                f"the release gate; {component_id} redistribution_rights cannot be "
                f"{row['redistribution_rights']}"
            )
        if row["bundled_in_release"] == "pending":
            validation.error(
                "docs/supply-chain/license-matrix.csv: approved review must satisfy "
                f"the release gate; {component_id} bundled_in_release cannot be pending"
            )
        if (
            row["bundled_in_release"] == "yes"
            and row["redistribution_rights"] != "permitted"
        ):
            validation.error(
                "docs/supply-chain/license-matrix.csv: approved review cannot bundle "
                f"{component_id} without permitted redistribution rights"
            )
        if (
            row["redistribution_rights"] == "prohibited"
            and row["bundled_in_release"] != "no"
        ):
            validation.error(
                "docs/supply-chain/license-matrix.csv: approved review may prohibit "
                f"redistribution for {component_id} only when bundled_in_release=no"
            )
        if row["notice_requirements"] == "pending_human_review":
            validation.error(
                "docs/supply-chain/license-matrix.csv: approved review must satisfy "
                f"the release gate; {component_id} notice_requirements cannot be "
                "pending_human_review"
            )
        if row["license_expression"] == "NOASSERTION":
            validation.error(
                "docs/supply-chain/license-matrix.csv: approved review must satisfy "
                f"the release gate; {component_id} license_expression cannot be "
                "NOASSERTION"
            )
    if review_result == "ApprovedWithConditions":
        if "condition" not in approval_reference.casefold():
            validation.error(
                "docs/supply-chain/dependencies.md: conditional approval reference must identify conditions"
            )
        if task_status == "done":
            validation.error(
                "TL-0006 cannot be done with ApprovedWithConditions; downstream conditions remain gated"
            )
    elif task_status == "done":
        if any(row["installation_rights"] != "permitted" for row in rows):
            validation.error(
                "TL-0006 done state requires permitted installation rights for every current dependency"
            )
        if not _approval_evidence_matches(task, metadata):
            validation.error(
                "TL-0006 done evidence must bind passed licence-owner approval to owner, date, reachable commit, matrix/SBOM digests, and environment"
            )


def validate_supply_chain_controls(validation: Validation) -> None:
    expected = expected_supply_chain_inventory(validation)
    if len(expected) != EXPECTED_MATRIX_COMPONENT_COUNT:
        validation.error(
            "Supply-chain inventory: expected the governed current set of "
            f"{EXPECTED_MATRIX_COMPONENT_COUNT} components, found {len(expected)}"
        )
    rows = validate_license_matrix(expected, validation)
    validate_supply_chain_approval(rows, validation)


def validate_workflow(validation: Validation) -> None:
    workflow_path = ROOT / ".github" / "workflows" / "verify.yml"
    workflow = _load_workflow_document(validation)
    if not workflow:
        return

    expected_workflow = {
        "name": "Verify",
        "on": {
            "push": {"branches": ["main"]},
            "pull_request": {"branches": ["main"]},
            "workflow_dispatch": "",
        },
        "permissions": {"contents": "read"},
        "concurrency": {
            "group": "verify-${{ github.workflow }}-${{ github.ref }}",
            "cancel-in-progress": "true",
        },
        "jobs": {
            "verify": {
                "name": "Windows authoritative verification",
                "runs-on": "windows-2025",
                "timeout-minutes": "30",
                "defaults": {"run": {"shell": "pwsh"}},
                "env": {
                    "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
                    "DOTNET_NOLOGO": "1",
                    "NUGET_XMLDOC_MODE": "skip",
                    "PIP_DISABLE_PIP_VERSION_CHECK": "1",
                    "PIP_NO_INPUT": "1",
                },
                "steps": [
                    {
                        "name": "Check out source",
                        "uses": (
                            "actions/checkout@"
                            + EXPECTED_ACTIONS["actions/checkout"]
                        ),
                        "with": {
                            "persist-credentials": "false",
                            "fetch-depth": "0",
                        },
                    },
                    {
                        "name": "Set up .NET",
                        "uses": (
                            "actions/setup-dotnet@"
                            + EXPECTED_ACTIONS["actions/setup-dotnet"]
                        ),
                        "with": {
                            "global-json-file": "global.json",
                            "architecture": "x64",
                        },
                    },
                    {
                        "name": "Set up Python",
                        "uses": (
                            "actions/setup-python@"
                            + EXPECTED_ACTIONS["actions/setup-python"]
                        ),
                        "with": {
                            "python-version": "3.14.7",
                            "architecture": "x64",
                        },
                    },
                    {
                        "name": "Install validator dependency",
                        "run": EXPECTED_PIP_INSTALL_COMMAND,
                    },
                    {"name": "Verify", "run": "./eng/verify.ps1"},
                ],
            }
        },
    }
    if workflow != expected_workflow:
        validation.error(
            ".github/workflows/verify.yml: workflow differs from the hardened exact contract"
        )

    triggers = workflow.get("on")
    if not isinstance(triggers, dict):
        validation.error(".github/workflows/verify.yml: on must be a mapping")
    else:
        required_triggers = {"push", "pull_request", "workflow_dispatch"}
        if not required_triggers.issubset(triggers):
            validation.error(
                ".github/workflows/verify.yml: push, pull_request, and workflow_dispatch are required"
            )
        if "pull_request_target" in triggers:
            validation.error(
                ".github/workflows/verify.yml: pull_request_target is prohibited"
            )

    permissions = workflow.get("permissions")
    if permissions != {"contents": "read"}:
        validation.error(
            ".github/workflows/verify.yml: workflow permissions must be contents: read only"
        )
    jobs = workflow.get("jobs")
    job = jobs.get("verify") if isinstance(jobs, dict) else None
    if not isinstance(job, dict):
        validation.error(".github/workflows/verify.yml: jobs.verify is required")
        return
    if job.get("runs-on") != "windows-2025":
        validation.error(
            ".github/workflows/verify.yml: jobs.verify must run on windows-2025"
        )
    if job.get("timeout-minutes") != "30":
        validation.error(
            ".github/workflows/verify.yml: jobs.verify timeout-minutes must equal 30"
        )

    steps = job.get("steps")
    if not isinstance(steps, list):
        validation.error(".github/workflows/verify.yml: jobs.verify.steps must be a list")
        return
    found_actions: dict[str, str] = {}
    runs: list[str] = []
    for step in steps:
        if not isinstance(step, dict):
            validation.error(".github/workflows/verify.yml: every step must be a mapping")
            continue
        use = step.get("uses")
        if isinstance(use, str):
            if "@" not in use:
                validation.error(f".github/workflows/verify.yml: unpinned action {use}")
                continue
            action, revision = use.rsplit("@", 1)
            if not re.fullmatch(r"[0-9a-f]{40}", revision):
                validation.error(
                    f".github/workflows/verify.yml: {action} must use a full commit SHA"
                )
            found_actions[action] = revision
            if action == "actions/upload-artifact":
                validation.error(
                    ".github/workflows/verify.yml: CI artifacts are intentionally prohibited"
                )
        run = step.get("run")
        if isinstance(run, str):
            runs.append(run.strip())

    for action, expected_revision in EXPECTED_ACTIONS.items():
        if found_actions.get(action) != expected_revision:
            validation.error(
                f".github/workflows/verify.yml: {action} must use {expected_revision}"
            )
    checkout = next(
        (
            step
            for step in steps
            if isinstance(step, dict)
            and str(step.get("uses", "")).startswith("actions/checkout@")
        ),
        None,
    )
    if not isinstance(checkout, dict) or checkout.get("with") != {
        "persist-credentials": "false",
        "fetch-depth": "0",
    }:
        validation.error(
            ".github/workflows/verify.yml: checkout must disable persisted credentials and fetch full history"
        )
    setup_dotnet = next(
        (
            step
            for step in steps
            if isinstance(step, dict)
            and str(step.get("uses", "")).startswith("actions/setup-dotnet@")
        ),
        None,
    )
    if not isinstance(setup_dotnet, dict) or setup_dotnet.get("with") != {
        "global-json-file": "global.json",
        "architecture": "x64",
    }:
        validation.error(
            ".github/workflows/verify.yml: setup-dotnet must use global.json on x64"
        )
    setup_python = next(
        (
            step
            for step in steps
            if isinstance(step, dict)
            and str(step.get("uses", "")).startswith("actions/setup-python@")
        ),
        None,
    )
    if not isinstance(setup_python, dict) or setup_python.get("with") != {
        "python-version": "3.14.7",
        "architecture": "x64",
    }:
        validation.error(
            ".github/workflows/verify.yml: setup-python must pin Python 3.14.7 on x64"
        )
    defaults = job.get("defaults")
    if defaults != {"run": {"shell": "pwsh"}}:
        validation.error(
            ".github/workflows/verify.yml: job command shell must be pwsh"
        )
    if EXPECTED_PIP_INSTALL_COMMAND not in runs:
        validation.error(
            ".github/workflows/verify.yml: CI must use the exact hash-pinned, wheel-only single-index pip command"
        )
    if "./eng/verify.ps1" not in runs:
        validation.error(
            ".github/workflows/verify.yml: CI must invoke ./eng/verify.ps1"
        )

    workflow_text = workflow_path.read_text(encoding="utf-8")
    if "secrets." in workflow_text:
        validation.error(
            ".github/workflows/verify.yml: verification must not consume repository secrets"
        )
    prohibited_pip_options = (
        "--extra-index-url",
        "--trusted-host",
        "--find-links",
        "--no-index",
    )
    if any(option in workflow_text for option in prohibited_pip_options):
        validation.error(
            ".github/workflows/verify.yml: alternate pip sources and trust bypasses are prohibited"
        )


def validate_readme_tool_install(validation: Validation) -> None:
    readme_path = ROOT / "README.md"
    try:
        text = readme_path.read_text(encoding="utf-8")
    except OSError as exc:
        validation.error(f"README.md: cannot validate pip instructions: {exc}")
        return
    if text.count(EXPECTED_README_PIP_INSTALL_COMMAND) != 2:
        validation.error(
            "README.md: both Python setup examples must use the exact hash-pinned, "
            "wheel-only https://pypi.org/simple install command"
        )
    for option in ("--extra-index-url", "--trusted-host", "--find-links"):
        if option in text:
            validation.error(f"README.md: prohibited pip source option {option}")


def validate_no_machine_paths(validation: Validation) -> None:
    paths = [
        ROOT / "README.md",
        ROOT / "Directory.Build.props",
        ROOT / "Directory.Packages.props",
        ROOT / "NuGet.Config",
        ROOT / "global.json",
        ROOT / "tools" / "requirements.txt",
        ROOT / "tools" / "generate_sbom.py",
        ROOT / "docs" / "supply-chain" / "dependencies.md",
        ROOT / "docs" / "supply-chain" / "license-matrix.csv",
        ROOT / "eng" / "generate-sbom.ps1",
        ROOT / "eng" / "verify.ps1",
        ROOT / "eng" / "verify.sh",
        ROOT / ".github" / "workflows" / "verify.yml",
    ]
    paths.extend((ROOT / "src").rglob("packages.lock.json"))
    paths.extend((ROOT / "tests").rglob("packages.lock.json"))
    for path in paths:
        try:
            text = path.read_text(encoding="utf-8-sig")
        except OSError as exc:
            validation.error(f"{relative(path)}: cannot scan: {exc}")
            continue
        if MACHINE_PATH_RE.search(text):
            validation.error(f"{relative(path)}: contains a machine-specific path")


def validate() -> int:
    validation = Validation()
    validate_bundle_manifest(validation)
    project_paths = validate_project_graph(validation)
    validate_central_packages(project_paths, validation)
    validate_nuget_audit_policy(project_paths, validation)
    validate_tool_and_source_configuration(validation)
    validate_workflow(validation)
    validate_readme_tool_install(validation)
    validate_supply_chain_controls(validation)
    validate_no_machine_paths(validation)

    if validation.errors:
        for error in validation.errors:
            print(f"ERROR: {error}", file=sys.stderr)
        print(f"FAILED: {len(validation.errors)} error(s)", file=sys.stderr)
        return 1
    print(
        "OK: repository controls valid; "
        f"{len(project_paths)} projects, {len(project_paths)} lock files, "
        "central packages, supply-chain matrix, and pinned Windows CI verified"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(validate())
