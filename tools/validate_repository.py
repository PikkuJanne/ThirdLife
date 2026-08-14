#!/usr/bin/env python3
"""Validate deterministic build controls and the ThirdLife project boundary."""

from __future__ import annotations

import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path
from typing import Any

import yaml

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


def validate_workflow(validation: Validation) -> None:
    workflow_path = ROOT / ".github" / "workflows" / "verify.yml"
    try:
        workflow = yaml.load(
            workflow_path.read_text(encoding="utf-8"), Loader=yaml.BaseLoader
        )
    except (OSError, yaml.YAMLError) as exc:
        validation.error(f".github/workflows/verify.yml: cannot parse YAML: {exc}")
        return
    if not isinstance(workflow, dict):
        validation.error(".github/workflows/verify.yml: top level must be a mapping")
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
                        "run": (
                            "python -m pip install --no-deps "
                            "--requirement tools/requirements.txt"
                        ),
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
    if (
        "python -m pip install --no-deps --requirement tools/requirements.txt"
        not in runs
    ):
        validation.error(
            ".github/workflows/verify.yml: CI must install the pinned validator dependency"
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


def validate_no_machine_paths(validation: Validation) -> None:
    paths = [
        ROOT / "Directory.Build.props",
        ROOT / "Directory.Packages.props",
        ROOT / "NuGet.Config",
        ROOT / "global.json",
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
    validate_tool_and_source_configuration(validation)
    validate_workflow(validation)
    validate_no_machine_paths(validation)

    if validation.errors:
        for error in validation.errors:
            print(f"ERROR: {error}", file=sys.stderr)
        print(f"FAILED: {len(validation.errors)} error(s)", file=sys.stderr)
        return 1
    print(
        "OK: repository controls valid; "
        f"{len(project_paths)} projects, {len(project_paths)} lock files, "
        "central packages and pinned Windows CI verified"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(validate())
