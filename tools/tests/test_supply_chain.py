#!/usr/bin/env python3
"""Focused regression tests for TL-0006 supply-chain controls."""

from __future__ import annotations

import csv
import json
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import urllib.parse
from collections import Counter
from contextlib import contextmanager
from copy import deepcopy
from pathlib import Path
from typing import Callable, Iterator


TOOLS_ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = TOOLS_ROOT.parent
sys.path.insert(0, str(TOOLS_ROOT))

import supply_chain  # noqa: E402


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
IGNORED_COPY_NAMES = {
    ".git",
    ".venv",
    ".cache",
    ".pytest_cache",
    "__pycache__",
    "artifacts",
    "bin",
    "obj",
}


def _copy_ignore(_directory: str, names: list[str]) -> set[str]:
    return {
        name
        for name in names
        if name in IGNORED_COPY_NAMES
        or name.endswith((".pyc", ".pyo", ".docx"))
    }


@contextmanager
def repository_copy() -> Iterator[Path]:
    """Copy governed inputs without host-local caches or Git state."""

    with tempfile.TemporaryDirectory() as temporary_directory:
        root = Path(temporary_directory) / "repository"
        shutil.copytree(REPOSITORY_ROOT, root, ignore=_copy_ignore)
        yield root


def read_matrix(root: Path) -> list[dict[str, str]]:
    path = root / "docs" / "supply-chain" / "license-matrix.csv"
    with path.open(encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle)
        if tuple(reader.fieldnames or ()) != MATRIX_HEADERS:
            raise AssertionError(f"unexpected matrix headers: {reader.fieldnames!r}")
        return [dict(row) for row in reader]


def write_matrix(root: Path, rows: list[dict[str, str]]) -> None:
    path = root / "docs" / "supply-chain" / "license-matrix.csv"
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(
            handle,
            fieldnames=MATRIX_HEADERS,
            lineterminator="\n",
        )
        writer.writeheader()
        writer.writerows(rows)


def replace_review_value(root: Path, field: str, value: str) -> None:
    path = root / "docs" / "supply-chain" / "dependencies.md"
    text = path.read_text(encoding="utf-8")
    pattern = re.compile(
        rf"(?m)^\|\s*{re.escape(field)}\s*\|\s*[^|\r\n]*\|\s*$"
    )
    replacement = f"| {field} | {value} |"
    text, count = pattern.subn(replacement, text)
    if count != 1:
        raise AssertionError(f"expected one {field!r} review row, found {count}")
    path.write_text(text, encoding="utf-8", newline="\n")


def run_git(root: Path, *arguments: str) -> str:
    completed = subprocess.run(
        ["git", "-C", str(root), *arguments],
        check=True,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        timeout=15,
    )
    return completed.stdout.strip()


def initialize_git_repository(root: Path) -> str:
    """Create a clean, deterministic repository only inside a test copy."""

    run_git(root, "init", "--initial-branch=main")
    run_git(root, "config", "user.name", "ThirdLife Test")
    run_git(root, "config", "user.email", "thirdlife-test@example.invalid")
    run_git(root, "config", "core.autocrlf", "false")
    paths = sorted(
        path.relative_to(root).as_posix()
        for path in root.rglob("*")
        if path.is_file() and ".git" not in path.relative_to(root).parts
    )
    for offset in range(0, len(paths), 50):
        run_git(root, "add", "--", *paths[offset : offset + 50])
    run_git(root, "commit", "-m", "Initial governed inputs")
    return run_git(root, "rev-parse", "HEAD")


def commit_paths(root: Path, message: str, *relative_paths: str) -> str:
    run_git(root, "add", "--", *relative_paths)
    run_git(root, "commit", "-m", message)
    return run_git(root, "rev-parse", "HEAD")


def add_catalog_matrix_row(
    rows: list[dict[str, str]],
    component_id: str,
    version: str,
) -> None:
    row = deepcopy(rows[0])
    row.update(
        component_type="catalog-application",
        component_id=component_id,
        version=version,
        relationship="direct",
        scope="catalog-application",
        upstream_publisher="Synthetic fixture publisher",
        source=f"https://example.invalid/catalog/{component_id}/{version}",
        purpose="Exercise exact catalogue supply-chain identity matching",
        declared_license="MIT",
        license_evidence="https://example.invalid/catalog/LICENSE",
        proposed_license_conclusion="Proposed MIT",
        proposed_installation_rights="Proposed allowed for synthetic fixture use",
        proposed_redistribution_rights="Proposed allowed under MIT notice terms",
        distribution_plan="not-shipped",
        integrity_algorithm="sha256",
        integrity_value="1" * 64,
        provenance_reference=(
            f"https://example.invalid/catalog/{component_id}/{version}/provenance"
        ),
        limitations="Synthetic catalogue identity used only by bounded tests.",
    )
    rows.append(row)
    rows.sort(
        key=lambda item: tuple(
            item[field].casefold()
            for field in (
                "component_type",
                "component_id",
                "version",
                "relationship",
                "scope",
            )
        )
    )


def error_text(result: supply_chain.SupplyChainResult) -> str:
    return "\n".join(result.errors).casefold()


class SupplyChainTestCase(unittest.TestCase):
    def assert_invalid(
        self,
        root: Path,
        *expected_terms: str,
    ) -> supply_chain.SupplyChainResult:
        result = supply_chain.validate_supply_chain(root)
        self.assertFalse(result.ok)
        self.assertTrue(result.errors)
        actual = error_text(result)
        for term in expected_terms:
            self.assertIn(term.casefold(), actual, result.errors)
        return result

    def mutate_matrix(
        self,
        mutation: Callable[[list[dict[str, str]]], None],
        *expected_terms: str,
    ) -> supply_chain.SupplyChainResult:
        with repository_copy() as root:
            rows = read_matrix(root)
            mutation(rows)
            write_matrix(root, rows)
            return self.assert_invalid(root, *expected_terms)


class CurrentInventoryTests(SupplyChainTestCase):
    def test_real_repository_inventory_has_current_complete_counts(self) -> None:
        result = supply_chain.validate_supply_chain(REPOSITORY_ROOT)

        self.assertTrue(result.ok, "\n".join(result.errors))
        self.assertEqual(len(result.inventory), 20)
        self.assertEqual(
            Counter(component.component_type for component in result.inventory),
            Counter(
                {
                    "github-action": 3,
                    "nuget": 14,
                    "pypi": 1,
                    "toolchain": 2,
                }
            ),
        )
        self.assertEqual(
            Counter(component.scope for component in result.inventory),
            Counter({"build-only": 6, "test-only": 14}),
        )
        self.assertEqual(
            Counter(component.relationship for component in result.inventory),
            Counter({"ci": 3, "direct": 5, "toolchain": 2, "transitive": 10}),
        )
        self.assertEqual(result.approval_state.casefold(), "pending")
        self.assertRegex(result.lock_digest, r"\A[0-9a-f]{64}\Z")
        self.assertRegex(result.matrix_digest, r"\A[0-9a-f]{64}\Z")
        self.assertTrue(result.dependency_graph)

        bom_refs = [component.bom_ref for component in result.inventory]
        self.assertEqual(len(bom_refs), len(set(bom_refs)))
        self.assertTrue(all(bom_refs))

        by_identity = {
            (component.component_type, component.component_id): component
            for component in result.inventory
        }
        self.assertEqual(
            by_identity[("toolchain", ".NET SDK")].integrity_algorithm,
            "version-pin",
        )
        self.assertEqual(
            by_identity[("toolchain", ".NET SDK")].integrity_value,
            "global.json#sdk.version",
        )
        self.assertEqual(
            by_identity[("toolchain", "CPython")].integrity_algorithm,
            "version-pin",
        )
        self.assertEqual(
            by_identity[("toolchain", "CPython")].integrity_value,
            ".github/workflows/verify.yml#python-version",
        )
        self.assertTrue(
            all(
                component.integrity_algorithm == "nuget-content-sha512"
                for component in result.inventory
                if component.component_type == "nuget"
            )
        )

    def test_cyclonedx_is_deterministic_and_ignores_local_state(self) -> None:
        with repository_copy() as root:
            first_result = supply_chain.validate_supply_chain(root)
            self.assertTrue(first_result.ok, "\n".join(first_result.errors))
            first_output = root / "first.cdx.json"
            supply_chain.write_cyclonedx(first_result, first_output)

            ignored_files = {
                ".git/objects/fake/packages.lock.json": "not json\n",
                ".venv/Lib/site-packages/fake/packages.lock.json": "not json\n",
                ".cache/dependency/license-matrix.csv": "malicious,cache,row\n",
                ".cache/requirements-cache.txt": "unpinned-cache-package>=1\n",
                "artifacts/sbom/old.cdx.json": '{"timestamp":"host-state"}\n',
                "artifacts/audit/requirements-generated.txt": (
                    "unpinned-artifact-package>=1\n"
                ),
                "tools/__pycache__/supply_chain.pyc": "host cache\n",
            }
            for relative, content in ignored_files.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")

            second_result = supply_chain.validate_supply_chain(root)
            self.assertTrue(second_result.ok, "\n".join(second_result.errors))
            second_output = root / "second.cdx.json"
            supply_chain.write_cyclonedx(second_result, second_output)

            self.assertEqual(first_output.read_bytes(), second_output.read_bytes())
            self.assertEqual(first_result.lock_digest, second_result.lock_digest)
            self.assertEqual(first_result.matrix_digest, second_result.matrix_digest)

    def test_cyclonedx_references_resolve_and_contains_no_host_identity(self) -> None:
        with repository_copy() as root:
            result = supply_chain.validate_supply_chain(root)
            self.assertTrue(result.ok, "\n".join(result.errors))
            document = supply_chain.build_cyclonedx(result)

            root_ref = document["metadata"]["component"]["bom-ref"]
            component_refs = {
                component["bom-ref"] for component in document["components"]
            }
            all_refs = component_refs | {root_ref}
            dependency_refs = {
                dependency["ref"] for dependency in document["dependencies"]
            }
            referenced = {
                reference
                for dependency in document["dependencies"]
                for reference in dependency.get("dependsOn", [])
            }

            self.assertLessEqual(dependency_refs, all_refs)
            self.assertLessEqual(referenced, all_refs)
            self.assertIn(root_ref, dependency_refs)
            self.assertEqual(
                len(document["components"]),
                len(component_refs),
                "CycloneDX bom-ref values must be unique",
            )

            serialized = json.dumps(document, sort_keys=True, separators=(",", ":"))
            serialized_casefold = serialized.casefold()
            self.assertNotIn("timestamp", serialized_casefold)
            self.assertNotIn("serialnumber", serialized_casefold)
            self.assertNotIn("urn:uuid", serialized_casefold)
            self.assertNotIn(str(root).casefold(), serialized_casefold)
            self.assertNotIn(str(root).replace("\\", "/").casefold(), serialized_casefold)

            self.assertEqual(
                document["$schema"],
                "http://cyclonedx.org/schema/bom-1.6.schema.json",
            )
            inventory_by_ref = {
                component.bom_ref: component for component in result.inventory
            }
            expected_property_names = {
                "thirdlife:scope",
                "thirdlife:proposed-installation-rights",
                "thirdlife:proposed-redistribution-rights",
            }
            for component in document["components"]:
                with self.subTest(component=component["name"]):
                    property_names = {
                        item["name"] for item in component["properties"]
                    }
                    self.assertLessEqual(expected_property_names, property_names)

                    for reference in component["externalReferences"]:
                        url = reference["url"]
                        parsed = urllib.parse.urlsplit(url)
                        self.assertNotIn(";", url)
                        self.assertEqual(parsed.scheme, "https")
                        self.assertTrue(parsed.hostname)

                    declared_license = inventory_by_ref[
                        component["bom-ref"]
                    ].declared_license
                    for choice in component["licenses"]:
                        if "expression" in choice:
                            expression = choice["expression"]
                            self.assertNotIn("proposed", expression.casefold())
                            self.assertNotIn("pending", expression.casefold())
                        elif declared_license not in {"MIT", "Apache-2.0"}:
                            self.assertEqual(
                                choice["license"]["name"], declared_license
                            )
                    licenses_json = json.dumps(component["licenses"]).casefold()
                    self.assertNotIn("proposed", licenses_json)
                    self.assertNotIn("pending human review", licenses_json)

    def test_only_real_artifact_hashes_are_emitted(self) -> None:
        result = supply_chain.validate_supply_chain(REPOSITORY_ROOT)
        self.assertTrue(result.ok, "\n".join(result.errors))
        document = supply_chain.build_cyclonedx(result)
        components = {item["name"].casefold(): item for item in document["components"]}

        pyyaml = components["pyyaml"]
        self.assertEqual(
            pyyaml["hashes"],
            [
                {
                    "alg": "SHA-256",
                    "content": (
                        "4a2e8cebe2ff6ab7d1050ecd59c25d4c8bd7e6f400f5f82b96557ac0abafd0ac"
                    ),
                }
            ],
        )
        nuget_names = {
            component.component_id.casefold()
            for component in result.inventory
            if component.component_type == "nuget"
        }
        for name in nuget_names:
            with self.subTest(component=name):
                self.assertNotIn("hashes", components[name])


class MatrixValidationTests(SupplyChainTestCase):
    def test_missing_stale_duplicate_case_collision_and_order_are_rejected(self) -> None:
        cases: tuple[
            tuple[str, Callable[[list[dict[str, str]]], None], tuple[str, ...]], ...
        ] = (
            ("missing", lambda rows: rows.pop(0), ("missing",)),
            (
                "stale",
                lambda rows: rows.append(
                    {
                        **deepcopy(rows[-1]),
                        "component_id": "Synthetic stale tool",
                        "version": "1.0.0",
                        "source": "https://example.invalid/synthetic-stale-tool",
                        "provenance_reference": "https://example.invalid/synthetic-stale-tool/1.0.0",
                    }
                ),
                ("stale",),
            ),
            ("duplicate", lambda rows: rows.append(deepcopy(rows[0])), ("duplicate",)),
            (
                "case collision",
                lambda rows: rows.append(
                    {
                        **deepcopy(rows[0]),
                        "component_id": rows[0]["component_id"].swapcase(),
                    }
                ),
                ("case", "collid"),
            ),
            ("unsorted", lambda rows: rows.reverse(), ("sort",)),
            (
                "oversized",
                lambda rows: rows[0].__setitem__("limitations", "x" * 65_537),
                ("limit",),
            ),
        )

        for name, mutation, terms in cases:
            with self.subTest(case=name):
                self.mutate_matrix(mutation, *terms)

    def test_blank_required_metadata_is_rejected(self) -> None:
        required_fields = (
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
        for field in required_fields:
            with self.subTest(field=field):
                self.mutate_matrix(
                    lambda rows, field=field: rows[0].__setitem__(field, " "),
                    "blank",
                )

    def test_unknown_or_conflated_scope_and_rights_are_rejected(self) -> None:
        cases: tuple[
            tuple[str, Callable[[list[dict[str, str]]], None], tuple[str, ...]], ...
        ] = (
            (
                "unknown scope",
                lambda rows: rows[0].__setitem__("scope", "unknown"),
                ("scope",),
            ),
            (
                "conflated scope",
                lambda rows: rows[0].__setitem__("scope", "build-only,test-only"),
                ("scope",),
            ),
            (
                "unknown installation rights",
                lambda rows: rows[0].__setitem__(
                    "proposed_installation_rights", "unknown"
                ),
                ("installation", "rights"),
            ),
            (
                "unknown redistribution rights",
                lambda rows: rows[0].__setitem__(
                    "proposed_redistribution_rights", "unknown"
                ),
                ("redistribution", "rights"),
            ),
            (
                "conflated rights",
                lambda rows: rows[0].__setitem__(
                    "proposed_installation_rights",
                    (
                        "Proposed installation and redistribution allowed; "
                        "pending human review"
                    ),
                ),
                ("rights",),
            ),
        )
        for name, mutation, terms in cases:
            with self.subTest(case=name):
                self.mutate_matrix(mutation, *terms)

    def test_pending_proposal_fields_reject_approval_like_wording(self) -> None:
        cases = (
            ("proposed_license_conclusion", "Approved MIT"),
            (
                "proposed_installation_rights",
                "Approved for CI use under MIT",
            ),
            (
                "proposed_redistribution_rights",
                "Approved under MIT notice terms",
            ),
        )
        for field, value in cases:
            with self.subTest(field=field):
                self.mutate_matrix(
                    lambda rows, field=field, value=value: rows[0].__setitem__(
                        field, value
                    ),
                    "proposed",
                )

    def test_observed_scope_relationship_version_and_hash_must_match(self) -> None:
        def mutate_named(
            rows: list[dict[str, str]],
            component_id: str,
            field: str,
            value: str,
        ) -> None:
            row = next(
                item
                for item in rows
                if item["component_id"].casefold() == component_id.casefold()
            )
            row[field] = value

        cases = (
            (
                "production versus test scope",
                lambda rows: mutate_named(rows, "xunit", "scope", "runtime"),
                ("nuget:xunit", "scope=runtime"),
            ),
            (
                "direct versus transitive",
                lambda rows: mutate_named(rows, "xunit", "relationship", "transitive"),
                ("nuget:xunit", "relationship=transitive"),
            ),
            (
                "version mismatch",
                lambda rows: mutate_named(rows, "PyYAML", "version", "0.0.0"),
                ("pypi:pyyaml", "version=0.0.0"),
            ),
            (
                "hash mismatch",
                lambda rows: mutate_named(
                    rows, "PyYAML", "integrity_value", "0" * 64
                ),
                ("pypi:pyyaml", "integrity=sha256:000000"),
            ),
        )
        for name, mutation, terms in cases:
            with self.subTest(case=name):
                self.mutate_matrix(mutation, *terms)

    def test_unsafe_urls_paths_and_csv_formulae_are_rejected(self) -> None:
        cases = (
            ("plain HTTP", "source", "http://example.invalid/archive", ("https",)),
            (
                "credentials",
                "source",
                "https://user:secret@example.invalid/archive",
                ("credential",),
            ),
            (
                "latest alias",
                "source",
                "https://example.invalid/releases/latest",
                ("latest",),
            ),
            (
                "URL fragment",
                "source",
                "https://example.invalid/archive#mutable",
                ("fragment",),
            ),
            (
                "machine path",
                "source",
                r"C:\Users\Somebody\Downloads\package.zip",
                ("machine", "path"),
            ),
            (
                "CSV formula",
                "owner",
                '=HYPERLINK("https://example.invalid","owner")',
                ("formula",),
            ),
        )
        for name, field, value, terms in cases:
            with self.subTest(case=name):
                self.mutate_matrix(
                    lambda rows, field=field, value=value: rows[0].__setitem__(
                        field, value
                    ),
                    *terms,
                )


class ManifestAndApprovalTests(SupplyChainTestCase):
    def test_dependency_digest_covers_central_and_project_manifests(self) -> None:
        mutations = (
            ("Directory.Packages.props", "\n<!-- digest regression -->\n"),
            (
                "tests/ThirdLife.Core.Tests/ThirdLife.Core.Tests.csproj",
                "\n<!-- digest regression -->\n",
            ),
        )
        for relative, suffix in mutations:
            with self.subTest(path=relative), repository_copy() as root:
                baseline = supply_chain.validate_supply_chain(root)
                self.assertTrue(baseline.ok, "\n".join(baseline.errors))
                path = root / relative
                path.write_text(
                    path.read_text(encoding="utf-8") + suffix,
                    encoding="utf-8",
                    newline="\n",
                )

                changed = supply_chain.validate_supply_chain(root)
                self.assertTrue(changed.ok, "\n".join(changed.errors))
                self.assertNotEqual(baseline.lock_digest, changed.lock_digest)

    def test_inconsistent_nuget_lock_hashes_are_rejected(self) -> None:
        with repository_copy() as root:
            lock_paths = sorted((root / "tests").rglob("packages.lock.json"))
            target = lock_paths[1]
            document = json.loads(target.read_text(encoding="utf-8"))
            dependencies = next(iter(document["dependencies"].values()))
            dependencies["xunit"]["contentHash"] = "A" * 88
            target.write_text(
                json.dumps(document, indent=2) + "\n",
                encoding="utf-8",
                newline="\n",
            )

            self.assert_invalid(root, "xunit", "contenthash")

    def test_unhashed_or_unpinned_python_requirements_are_rejected(self) -> None:
        mutations = (
            (
                "missing package hash",
                lambda text: "\n".join(
                    line for line in text.splitlines() if "--hash=" not in line
                )
                + "\n",
                ("hash",),
            ),
            (
                "missing require-hashes mode",
                lambda text: text.replace("--require-hashes\n", ""),
                ("require-hashes",),
            ),
            (
                "range instead of exact pin",
                lambda text: text.replace("PyYAML==6.0.3", "PyYAML>=6.0.3"),
                ("pin",),
            ),
        )
        for name, mutation, terms in mutations:
            with self.subTest(case=name), repository_copy() as root:
                path = root / "tools" / "requirements.txt"
                path.write_text(
                    mutation(path.read_text(encoding="utf-8")),
                    encoding="utf-8",
                    newline="\n",
                )
                self.assert_invalid(root, *terms)

    def test_pypi_index_must_be_official_present_and_unique(self) -> None:
        def replace_index(text: str) -> str:
            return text.replace(
                "--index-url=https://pypi.org/simple",
                "--index-url=https://mirror.example.invalid/simple",
            )

        def remove_index(text: str) -> str:
            return "\n".join(
                line for line in text.splitlines() if not line.startswith("--index-url=")
            ) + "\n"

        def duplicate_index(text: str) -> str:
            index_line = "--index-url=https://pypi.org/simple"
            return text.replace(index_line, f"{index_line}\n{index_line}", 1)

        cases = (
            ("non-official", replace_index),
            ("missing", remove_index),
            ("duplicate", duplicate_index),
        )
        for name, mutation in cases:
            with self.subTest(case=name), repository_copy() as root:
                path = root / "tools" / "requirements.txt"
                path.write_text(
                    mutation(path.read_text(encoding="utf-8")),
                    encoding="utf-8",
                    newline="\n",
                )
                self.assert_invalid(root, "index")

    def test_action_sha_and_toolchain_version_drift_are_rejected(self) -> None:
        with self.subTest(case="action SHA"), repository_copy() as root:
            path = root / ".github" / "workflows" / "verify.yml"
            text = path.read_text(encoding="utf-8")
            text, count = re.subn(
                r"(uses:\s*actions/checkout@)[0-9a-f]{40}",
                rf"\g<1>{'0' * 39}1",
                text,
                count=1,
            )
            self.assertEqual(count, 1)
            path.write_text(text, encoding="utf-8", newline="\n")
            self.assert_invalid(root, "actions/checkout")

        with self.subTest(case="action version label"), repository_copy() as root:
            path = root / ".github" / "workflows" / "verify.yml"
            text = path.read_text(encoding="utf-8")
            text = text.replace("# v7.0.1", "# v7.0.2", 1)
            path.write_text(text, encoding="utf-8", newline="\n")
            self.assert_invalid(root, "actions/checkout", "version=7.0.2")

        with self.subTest(case=".NET SDK version"), repository_copy() as root:
            path = root / "global.json"
            document = json.loads(path.read_text(encoding="utf-8"))
            document["sdk"]["version"] = "10.0.401"
            path.write_text(
                json.dumps(document, indent=2) + "\n",
                encoding="utf-8",
                newline="\n",
            )
            self.assert_invalid(root, ".net sdk", "version=10.0.401")

        with self.subTest(case="CPython version"), repository_copy() as root:
            path = root / ".github" / "workflows" / "verify.yml"
            text = path.read_text(encoding="utf-8")
            text = text.replace('python-version: "3.14.7"', 'python-version: "3.14.8"')
            path.write_text(text, encoding="utf-8", newline="\n")
            self.assert_invalid(root, "cpython", "version=3.14.8")

    def test_source_revision_requires_exact_clean_head(self) -> None:
        with self.subTest(case="fabricated revision"), repository_copy() as root:
            initialize_git_repository(root)
            result = supply_chain.validate_supply_chain(root)
            self.assertTrue(result.ok, "\n".join(result.errors))
            with self.assertRaisesRegex(ValueError, r"(?i)(source revision|head)"):
                supply_chain.build_cyclonedx(result, source_revision="0" * 40)

        with self.subTest(case="non-HEAD revision"), repository_copy() as root:
            previous_head = initialize_git_repository(root)
            readme_path = root / "README.md"
            readme_path.write_text(
                readme_path.read_text(encoding="utf-8")
                + "\n<!-- source-revision regression -->\n",
                encoding="utf-8",
                newline="\n",
            )
            current_head = commit_paths(
                root,
                "Advance HEAD without changing governed dependency inputs",
                "README.md",
            )
            self.assertNotEqual(previous_head, current_head)
            result = supply_chain.validate_supply_chain(root)
            self.assertTrue(result.ok, "\n".join(result.errors))
            with self.assertRaisesRegex(ValueError, r"(?i)(source revision|head)"):
                supply_chain.build_cyclonedx(
                    result,
                    source_revision=previous_head,
                )

        with self.subTest(case="dirty governed input"), repository_copy() as root:
            head = initialize_git_repository(root)
            path = root / "Directory.Packages.props"
            path.write_text(
                path.read_text(encoding="utf-8")
                + "\n<!-- dirty governed input -->\n",
                encoding="utf-8",
                newline="\n",
            )
            result = supply_chain.validate_supply_chain(root)
            self.assertTrue(result.ok, "\n".join(result.errors))
            with self.assertRaisesRegex(ValueError, r"(?i)(governed|dirty|commit)"):
                supply_chain.build_cyclonedx(result, source_revision=head)

        with self.subTest(case="generator changed after validation"), repository_copy() as root:
            head = initialize_git_repository(root)
            result = supply_chain.validate_supply_chain(root)
            self.assertTrue(result.ok, "\n".join(result.errors))
            path = root / "tools" / "supply_chain.py"
            path.write_text(
                path.read_text(encoding="utf-8")
                + "\n# dirty generator control\n",
                encoding="utf-8",
                newline="\n",
            )
            with self.assertRaisesRegex(ValueError, r"(?i)(governed|dirty|commit)"):
                supply_chain.build_cyclonedx(result, source_revision=head)

        with self.subTest(case="exact clean HEAD"), repository_copy() as root:
            head = initialize_git_repository(root)
            result = supply_chain.validate_supply_chain(root)
            self.assertTrue(result.ok, "\n".join(result.errors))
            document = supply_chain.build_cyclonedx(
                result,
                source_revision=head,
            )
            metadata = {
                item["name"]: item["value"]
                for item in document["metadata"]["properties"]
            }
            self.assertEqual(metadata["thirdlife:source-revision"], head)

    def test_catalog_file_without_catalog_matrix_row_is_rejected(self) -> None:
        with repository_copy() as root:
            catalog_path = root / "fixtures" / "catalog" / "synthetic.yaml"
            catalog_path.parent.mkdir(parents=True)
            catalog_path.write_text(
                "applications:\n"
                "  - id: generic.synthetic.editor\n"
                "    version: 1.0.0\n",
                encoding="utf-8",
                newline="\n",
            )

            self.assert_invalid(root, "catalog", "matrix")

    def test_catalog_identities_match_matrix_rows_one_to_one(self) -> None:
        fixture_id = "generic.synthetic.editor"
        fixture_version = "1.0.0"

        with self.subTest(case="mismatched row"), repository_copy() as root:
            catalog_path = root / "fixtures" / "catalog" / "synthetic.yaml"
            catalog_path.parent.mkdir(parents=True)
            catalog_path.write_text(
                "applications:\n"
                f"  - id: {fixture_id}\n"
                f"    version: {fixture_version}\n",
                encoding="utf-8",
                newline="\n",
            )
            rows = read_matrix(root)
            add_catalog_matrix_row(rows, "generic.different.editor", fixture_version)
            write_matrix(root, rows)

            self.assert_invalid(root, "catalog", "missing", "stale")

        with self.subTest(case="exact row"), repository_copy() as root:
            catalog_path = root / "fixtures" / "catalog" / "synthetic.yaml"
            catalog_path.parent.mkdir(parents=True)
            catalog_path.write_text(
                "applications:\n"
                f"  - id: {fixture_id}\n"
                f"    version: {fixture_version}\n",
                encoding="utf-8",
                newline="\n",
            )
            rows = read_matrix(root)
            add_catalog_matrix_row(rows, fixture_id, fixture_version)
            write_matrix(root, rows)

            result = supply_chain.validate_supply_chain(root)
            self.assertTrue(result.ok, "\n".join(result.errors))
            component = next(
                item
                for item in result.inventory
                if item.component_type == "catalog-application"
            )
            self.assertEqual(component.component_id, fixture_id)
            self.assertEqual(component.version, fixture_version)
            self.assertEqual(
                component.evidence_paths,
                ("fixtures/catalog/synthetic.yaml",),
            )

    def test_malformed_and_false_approval_metadata_are_rejected(self) -> None:
        with self.subTest(case="missing review row"), repository_copy() as root:
            path = root / "docs" / "supply-chain" / "dependencies.md"
            text = path.read_text(encoding="utf-8")
            text, count = re.subn(
                r"(?m)^\|\s*Reviewer\s*\|[^\r\n]*\r?\n",
                "",
                text,
                count=1,
            )
            self.assertEqual(count, 1)
            path.write_text(text, encoding="utf-8", newline="\n")
            self.assert_invalid(root, "reviewer")

        with self.subTest(case="approval with placeholders"), repository_copy() as root:
            replace_review_value(root, "Review status", "Approved")
            self.assert_invalid(root, "approved")

        with self.subTest(case="approval bound to wrong matrix"), repository_copy() as root:
            baseline = supply_chain.validate_supply_chain(root)
            self.assertTrue(baseline.ok, "\n".join(baseline.errors))
            approved_values = {
                "Review status": "Approved",
                "Reviewer": "Janne Vuorela",
                "Role": "Principal Software Architect & Sole Project Owner",
                "Review date": "2026-08-21",
                "Result": "Approved without conditions",
                "Reviewed commit": "a" * 40,
                "Matrix SHA-256": "0" * 64,
            }
            for field, value in approved_values.items():
                replace_review_value(root, field, value)
            self.assertNotEqual(approved_values["Matrix SHA-256"], baseline.matrix_digest)
            self.assert_invalid(root, "matrix sha-256", "does not match")

    def test_approved_review_binds_exact_committed_matrix(self) -> None:
        with repository_copy() as root:
            reviewed_commit = initialize_git_repository(root)
            baseline = supply_chain.validate_supply_chain(root)
            self.assertTrue(baseline.ok, "\n".join(baseline.errors))
            approved_values = {
                "Review status": "Approved",
                "Reviewer": "Janne Vuorela",
                "Role": "Principal Software Architect & Sole Project Owner",
                "Review date": "2026-08-21",
                "Result": "Approved without conditions",
                "Reviewed commit": reviewed_commit,
                "Matrix SHA-256": baseline.matrix_digest,
            }
            for field, value in approved_values.items():
                replace_review_value(root, field, value)
            commit_paths(
                root,
                "Record exact human licence and rights approval",
                "docs/supply-chain/dependencies.md",
            )

            approved = supply_chain.validate_supply_chain(root)
            self.assertTrue(approved.ok, "\n".join(approved.errors))
            self.assertEqual(approved.approval_state, "approved")

            rows = read_matrix(root)
            rows[0]["limitations"] += " Materially changed after approval."
            write_matrix(root, rows)
            commit_paths(
                root,
                "Change the matrix after its recorded approval",
                "docs/supply-chain/license-matrix.csv",
            )

            stale = supply_chain.validate_supply_chain(root)
            self.assertFalse(stale.ok)
            self.assertEqual(stale.approval_state, "invalid")
            self.assertIn("current matrix", error_text(stale))


if __name__ == "__main__":
    unittest.main()
