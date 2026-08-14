#!/usr/bin/env python3
"""Regression tests for ThirdLife governance validation."""

from __future__ import annotations

import csv
import hashlib
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


TOOLS_ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = TOOLS_ROOT.parent
sys.path.insert(0, str(TOOLS_ROOT))

import validate_bundle  # noqa: E402
import validate_repository  # noqa: E402


GOVERNANCE_DOCUMENTS = (
    "docs/change-control.md",
    "docs/glossary.md",
    "docs/non-goals.md",
    "docs/product-contract.md",
)
SECURITY_DOCUMENTS = (
    "docs/security/abuse-cases.md",
    "docs/security/data-flow.md",
    "docs/security/threat-model.md",
)
SUPPLY_CHAIN_DOCUMENTS = (
    "docs/supply-chain/dependencies.md",
    "docs/supply-chain/license-matrix.csv",
)
TASK_DOCUMENT = validate_bundle.yaml.safe_load(
    (REPOSITORY_ROOT / "TASKS.yaml").read_text(encoding="utf-8")
)
TASK_BY_ID = {task["id"]: task for task in TASK_DOCUMENT["tasks"]}
DECISION_IDS = set(
    re.findall(
        r"(?m)^### (D-\d{3})\s+—\s+",
        (REPOSITORY_ROOT / "DECISIONS.md").read_text(encoding="utf-8"),
    )
)


class PositioningRuleTests(unittest.TestCase):
    def setUp(self) -> None:
        self.legacy_first = "sec" + "ond"
        self.legacy_last = "li" + "fe"

    def test_legacy_name_variants_are_rejected(self) -> None:
        aliases = (
            self.legacy_first + self.legacy_last,
            self.legacy_first + " " + self.legacy_last,
            self.legacy_first + "-" + self.legacy_last,
            self.legacy_first + "_" + self.legacy_last,
            self.legacy_first + "**" + self.legacy_last,
            self.legacy_first + "." + self.legacy_last,
            "My" + self.legacy_first.title() + self.legacy_last.title() + "Setup",
        )
        for alias in aliases:
            with self.subTest(alias=alias):
                self.assertTrue(validate_bundle.contains_forbidden_legacy_name(alias))

    def test_ordinary_life_prefix_prose_is_not_a_legacy_alias(self) -> None:
        ordinary_prose = (
            "The benchmark records a second lifetime value.",
            "The first attempt failed; the second. Life-cycle checks continued.",
            "This is the second life-cycle stage.",
        )
        for text in ordinary_prose:
            with self.subTest(text=text):
                self.assertFalse(validate_bundle.contains_forbidden_legacy_name(text))

    def test_affirmative_optimizer_positioning_is_rejected(self) -> None:
        claims = (
            "ThirdLife Setup Core is a PC optimizer.",
            "ThirdLife Setup Core offers PC optimization and registry cleanup.",
            "ThirdLife provides a tune-up utility for cleaning and debloating Windows.",
            "A PC optimizer for every workshop.",
            "Speed up any PC with ThirdLife.",
            "ThirdLife is not only an optimizer; it is a registry cleaner too.",
        )
        for claim in claims:
            with self.subTest(claim=claim):
                self.assertTrue(
                    validate_bundle.has_prohibited_optimizer_positioning(claim)
                )

    def test_explicit_optimizer_denials_are_allowed(self) -> None:
        denials = (
            "ThirdLife Setup Core is not a PC optimizer.",
            "ThirdLife is designed not to be a PC optimizer.",
            "ThirdLife does not speed up any PC.",
            "Registry cleaning and debloating are prohibited.",
            "No optimizer, cleaner, or general IT toolbox positioning is allowed.",
            "Optimization of this internal algorithm is unrelated to product scope.",
            "ThirdLife is anything but a PC optimizer.",
            "ThirdLife cannot be considered a PC optimizer.",
            (
                "ThirdLife is not a PC cleaner, optimizer, debloater, registry "
                "cleaner, driver-download utility, or general IT toolbox."
            ),
        )
        for denial in denials:
            with self.subTest(denial=denial):
                self.assertFalse(
                    validate_bundle.has_prohibited_optimizer_positioning(denial)
                )

    def test_unrelated_denials_do_not_hide_optimizer_claims(self) -> None:
        mixed_claims = (
            "ThirdLife does not collect telemetry but is a PC optimizer.",
            "ThirdLife excludes malware cleanup but offers PC optimization.",
            "ThirdLife is a PC optimizer without telemetry.",
            "No telemetry is collected. ThirdLife is a PC optimizer.",
            "ThirdLife is not a backup tool; it is a PC optimizer.",
            "ThirdLife does not erase data and offers PC optimization.",
            "ThirdLife does not collect telemetry, yet it is a PC optimizer.",
            "ThirdLife is not a backup tool, it is a PC optimizer.",
            "ThirdLife does not collect telemetry \u2014 it is a PC optimizer.",
            "ThirdLife does not collect telemetry despite being a PC optimizer.",
            "ThirdLife is a PC optimizer, but registry cleaning is prohibited.",
        )
        for claim in mixed_claims:
            with self.subTest(claim=claim):
                self.assertTrue(
                    validate_bundle.has_prohibited_optimizer_positioning(claim)
                )

    def test_negative_markdown_heading_applies_to_its_section(self) -> None:
        self.assertFalse(
            validate_bundle.has_prohibited_optimizer_positioning(
                "- registry cleaning and generic optimization;",
                "## Does not own",
            )
        )
        self.assertTrue(
            validate_bundle.has_prohibited_optimizer_positioning(
                "ThirdLife is a PC optimizer.",
                "## Non-goals",
            )
        )


class TrackedTextDiscoveryTests(unittest.TestCase):
    def test_git_discovery_includes_unlisted_text_extensions(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            text_path = root / "contract.customext"
            text_path.write_text("governed text\n", encoding="utf-8")
            result = subprocess.CompletedProcess(
                args=[],
                returncode=0,
                stdout=b"contract.customext\0",
            )
            with (
                patch.object(validate_bundle, "ROOT", root),
                patch.object(validate_bundle, "REQUIRED_FILES", ()),
                patch.object(validate_bundle.subprocess, "run", return_value=result),
            ):
                self.assertEqual(
                    validate_bundle.repository_text_paths(),
                    [text_path],
                )

    def test_directory_fallback_includes_extensionless_text(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            notice_path = root / "NOTICE"
            notice_path.write_text("governed text\n", encoding="utf-8")
            ignored_path = root / "bin" / "generated.txt"
            ignored_path.parent.mkdir()
            ignored_path.write_text("ignored\n", encoding="utf-8")
            with (
                patch.object(validate_bundle, "ROOT", root),
                patch.object(validate_bundle, "REQUIRED_FILES", ()),
                patch.object(validate_bundle.subprocess, "run", side_effect=OSError),
            ):
                paths = validate_bundle.repository_text_paths()
            self.assertIn(notice_path, paths)
            self.assertNotIn(ignored_path, paths)

    def test_fallback_ignores_only_repository_relative_directory_names(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory) / "bin" / "repository"
            root.mkdir(parents=True)
            notice_path = root / "NOTICE"
            notice_path.write_text("governed text\n", encoding="utf-8")
            with (
                patch.object(validate_bundle, "ROOT", root),
                patch.object(validate_bundle, "REQUIRED_FILES", ()),
                patch.object(validate_bundle.subprocess, "run", side_effect=OSError),
            ):
                paths = validate_bundle.repository_text_paths()
            self.assertIn(notice_path, paths)

    def test_unlisted_tracked_text_is_scanned(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            text_path = root / "contract.customext"
            alias = ("sec" + "ond") + ("li" + "fe")
            text_path.write_text(f"Legacy alias: {alias}\n", encoding="utf-8")
            result = subprocess.CompletedProcess(
                args=[],
                returncode=0,
                stdout=b"contract.customext\0",
            )
            validation = validate_bundle.Validation()
            with (
                patch.object(validate_bundle, "ROOT", root),
                patch.object(validate_bundle, "REQUIRED_FILES", ()),
                patch.object(validate_bundle.subprocess, "run", return_value=result),
            ):
                validate_bundle.validate_tracked_text_positioning(validation)
            self.assertEqual(len(validation.errors), 1)
            self.assertIn("forbidden legacy product name", validation.errors[0])

    def test_binary_tracked_file_is_ignored(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            binary_path = root / "asset.unknown"
            alias = (("sec" + "ond") + ("li" + "fe")).encode("ascii")
            binary_path.write_bytes(b"\0" + alias)
            result = subprocess.CompletedProcess(
                args=[],
                returncode=0,
                stdout=b"asset.unknown\0",
            )
            validation = validate_bundle.Validation()
            with (
                patch.object(validate_bundle, "ROOT", root),
                patch.object(validate_bundle, "REQUIRED_FILES", ()),
                patch.object(validate_bundle.subprocess, "run", return_value=result),
            ):
                validate_bundle.validate_tracked_text_positioning(validation)
            self.assertEqual(validation.errors, [])

    def test_non_utf8_tracked_text_is_rejected_instead_of_skipped(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            alias = (("sec" + "ond") + ("li" + "fe")).encode("ascii")
            legacy_path = root / "legacy.txt"
            legacy_path.write_bytes(b"caf\xe9 alias: " + alias)
            utf16_path = root / "utf16.txt"
            utf16_path.write_text("governed text\n", encoding="utf-16")
            result = subprocess.CompletedProcess(
                args=[],
                returncode=0,
                stdout=b"legacy.txt\0utf16.txt\0",
            )
            validation = validate_bundle.Validation()
            with (
                patch.object(validate_bundle, "ROOT", root),
                patch.object(validate_bundle, "REQUIRED_FILES", ()),
                patch.object(validate_bundle.subprocess, "run", return_value=result),
            ):
                validate_bundle.validate_tracked_text_positioning(validation)
            self.assertEqual(len(validation.errors), 2)
            self.assertTrue(
                all("tracked text is not UTF-8" in error for error in validation.errors)
            )


class SecurityDocumentContractTests(unittest.TestCase):
    def copy_security_documents(self, root: Path) -> None:
        for relative in SECURITY_DOCUMENTS:
            destination = root / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(
                (REPOSITORY_ROOT / relative).read_text(encoding="utf-8"),
                encoding="utf-8",
            )

    def validate_documents(
        self,
        root: Path,
        task_by_id: dict[str, dict[str, object]] | None = None,
    ) -> validate_bundle.Validation:
        validation = validate_bundle.Validation()
        with patch.object(validate_bundle, "ROOT", root):
            validate_bundle.validate_security_documents(
                validation,
                TASK_BY_ID if task_by_id is None else task_by_id,
                DECISION_IDS,
            )
        return validation

    def replace_once(self, path: Path, old: str, new: str) -> None:
        text = path.read_text(encoding="utf-8")
        self.assertIn(old, text)
        path.write_text(text.replace(old, new, 1), encoding="utf-8")

    def make_pending_security_review(self, root: Path) -> None:
        threat_path = root / "docs/security/threat-model.md"
        threat_replacements = (
            ("**Status:** Approved initial model", "**Status:** Draft for security-owner review"),
            ("**Model revision:** TL-0004 approved 1", "**Model revision:** TL-0004 draft 1"),
            ("**Security-owner approval:** **Approved**", "**Security-owner approval:** **Pending**"),
            ("**Approving owner and role:** PikkuJanne — Security owner", "**Approving owner and role:** Pending"),
            ("**Approval date:** 2026-08-14", "**Approval date:** Pending"),
            (
                "**Approval reference:** reviewed commit 917b5ebd5f5e4cf273a087a05dd381da54324235",
                "**Approval reference:** Pending",
            ),
            (
                "The named security owner approved this initial model and selected a mitigation treatment for each residual risk; this does not approve an implemented control or authorize a release.",
                "No residual risk is accepted by this draft. A named security owner must approve this exact revision and the residual-risk list before `TL-0004` can be done.",
            ),
            (
                "These residuals were reviewed with the initial model. Each owner-decision cell uses `Accept`, `Mitigate`, `Avoid`, `Transfer`, or `Block`, followed by an em dash and a concrete rationale, condition, owner, or gate. Approval of this model is not release authorization.",
                "These are proposed residuals for owner review. **Pending** means neither approval nor release authorization. On approval, each owner-decision cell must use `Accept`, `Mitigate`, `Avoid`, `Transfer`, or `Block`, followed by an em dash and a concrete rationale, condition, owner, or gate. Approval of this model is not release authorization.",
            ),
            (
                "**Current review result:** Approved. The named security owner approved this initial model and recorded a treatment for every residual risk; this is not release authorization and does not replace later security reviews.",
                "**Current review result:** Pending. This draft does not satisfy the human evidence required by `TL-0004`, does not approve any implemented control, and does not replace later broker, package, privacy, pilot, or Core 1.0 security reviews.",
            ),
        )
        for old, new in threat_replacements:
            self.replace_once(threat_path, old, new)
        threat_text = threat_path.read_text(encoding="utf-8")
        threat_text, replacement_count = re.subn(
            r"(^\|\s*`RR-\d{3}`\s*\|.*\|)\s*(?:Accept|Mitigate|Avoid|Transfer|Block)\s+—\s+.*?\s*(\|\s*$)",
            r"\1 Pending \2",
            threat_text,
            flags=re.MULTILINE,
        )
        self.assertEqual(replacement_count, 8)
        threat_path.write_text(threat_text, encoding="utf-8")

        flow_path = root / "docs/security/data-flow.md"
        self.replace_once(
            flow_path,
            "**Status:** Approved initial model",
            "**Status:** Draft for security-owner review",
        )
        self.replace_once(
            flow_path,
            "**Model revision:** TL-0004 approved 1",
            "**Model revision:** TL-0004 draft 1",
        )

        abuse_path = root / "docs/security/abuse-cases.md"
        abuse_replacements = (
            ("**Status:** Approved initial model", "**Status:** Draft for security-owner review"),
            ("**Model revision:** TL-0004 approved 1", "**Model revision:** TL-0004 draft 1"),
            (
                "The named security owner recorded a treatment for every residual risk; this is not release authorization.",
                "Human acceptance of residual risks remains pending.",
            ),
            (
                "**Review result:** Approved. Named security-owner approval is recorded for the exact model revision.",
                "**Review result:** Pending. No security-owner approval or residual-risk acceptance is recorded in this draft.",
            ),
        )
        for old, new in abuse_replacements:
            self.replace_once(abuse_path, old, new)

    def test_current_security_documents_satisfy_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_security_documents(root)
            validation = self.validate_documents(root)
            self.assertEqual(validation.errors, [])

    def test_collapsed_trust_boundary_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_security_documents(root)
            flow_path = root / "docs/security/data-flow.md"
            self.replace_once(flow_path, "| `TB-EXPORT` |", "| `TB-JOB-STORE` |")
            validation = self.validate_documents(root)
            self.assertTrue(
                any("trust-boundary table" in error for error in validation.errors)
            )

    def test_missing_required_abuse_case_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_security_documents(root)
            abuse_path = root / "docs/security/abuse-cases.md"
            self.replace_once(abuse_path, "### AC-005 —", "### Removed package case —")
            validation = self.validate_documents(root)
            self.assertTrue(
                any("abuse-case IDs must exactly equal" in error for error in validation.errors)
            )

    def test_unknown_task_reference_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_security_documents(root)
            threat_path = root / "docs/security/threat-model.md"
            self.replace_once(threat_path, "`TL-0312`", "`TL-9999`")
            validation = self.validate_documents(root)
            self.assertIn(
                "Security documents reference unknown roadmap tasks ['TL-9999']",
                validation.errors,
            )

    def test_high_risk_threat_without_task_mapping_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_security_documents(root)
            threat_path = root / "docs/security/threat-model.md"
            self.replace_once(
                threat_path,
                (
                    "**Planned controls/tasks:** `TL-0201`, `TL-0301`, `TL-0302`, "
                    "`TL-0303`, `TL-0310`, `TL-0312`"
                ),
                "**Planned controls/tasks:** Pending",
            )
            validation = self.validate_documents(root)
            self.assertIn(
                "docs/security/threat-model.md: high-risk THR-001 has no roadmap task mapping",
                validation.errors,
            )

    def test_external_sanitization_and_b4_no_design_clauses_are_required(self) -> None:
        mutations = (
            (
                "docs/security/threat-model.md",
                "Sanitization is an external prerequisite",
                "Sanitization may be performed by the runtime",
                "Sanitization is an external prerequisite",
            ),
            (
                "docs/security/data-flow.md",
                "not an adapter specification",
                "an adapter specification",
                "not an adapter specification",
            ),
            (
                "docs/security/abuse-cases.md",
                "flows `F-01` through `F-19`",
                "flows `F-01` through `F-18`",
                "flows `F-01` through `F-19`",
            ),
            (
                "docs/security/data-flow.md",
                "durably commits a correlated started/dispatch-intent checkpoint before emitting",
                "emits before recording dispatch",
                "durably commits a correlated started/dispatch-intent checkpoint before emitting",
            ),
        )
        for relative, old, new, expected_error_fragment in mutations:
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_security_documents(root)
                self.replace_once(root / relative, old, new)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(
                        expected_error_fragment in error
                        for error in validation.errors
                    )
                )

    def test_broker_and_package_cases_need_relevant_task_mappings(self) -> None:
        mutations = (
            (
                "AC-002",
                "`TL-0303`, `TL-0310`, `TL-0311`, `TL-0312`, `TL-0313`",
                "`TL-0101`",
                "broker abuse case AC-002",
            ),
            (
                "AC-005",
                (
                    "`TL-0006`, `TL-0301`, `TL-0307`, `TL-0402`, `TL-0403`, "
                    "`TL-0404`, `TL-0508`"
                ),
                "`TL-0101`",
                "package/update abuse case AC-005",
            ),
        )
        for abuse_id, old, new, expected_error_fragment in mutations:
            with self.subTest(abuse_id=abuse_id), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_security_documents(root)
                abuse_path = root / "docs/security/abuse-cases.md"
                self.replace_once(abuse_path, old, new)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(
                        expected_error_fragment in error
                        for error in validation.errors
                    )
                )

    def test_broker_and_package_threats_need_relevant_task_mappings(self) -> None:
        mutations = (
            (
                (
                    "`TL-0303`, `TL-0307`, `TL-0309`, `TL-0310`, `TL-0311`, "
                    "`TL-0312`, `TL-0313`, `TL-0404`, `TL-0609`"
                ),
                "`TL-0101`",
                "broker threat THR-002",
            ),
            (
                (
                    "`TL-0006`, `TL-0301`, `TL-0307`, `TL-0401`, `TL-0402`, "
                    "`TL-0403`, `TL-0404`, `TL-0406`, `TL-0504`, `TL-0508`"
                ),
                "`TL-0101`",
                "package-source threat THR-003",
            ),
        )
        for old, new, expected_error_fragment in mutations:
            with self.subTest(expected_error_fragment=expected_error_fragment), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_security_documents(root)
                self.replace_once(root / "docs/security/threat-model.md", old, new)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(
                        expected_error_fragment in error
                        for error in validation.errors
                    )
                )

    def test_unknown_security_cross_references_are_rejected(self) -> None:
        mutations = (
            ("docs/security/threat-model.md", "D-022", "D-999", "unknown decision"),
            ("docs/security/threat-model.md", "AST-01", "AST-99", "unknown asset"),
            ("docs/security/threat-model.md", "ACT-01", "ACT-99", "unknown actor"),
            ("docs/security/threat-model.md", "`AC-001`", "`AC-999`", "unknown abuse-case"),
            ("docs/security/threat-model.md", "`TB-UI`", "`TB-UNKNOWN`", "unknown trust-boundary"),
            ("docs/security/abuse-cases.md", "`THR-001`", "`THR-999`", "unknown threat"),
            ("docs/security/abuse-cases.md", "`RR-002`", "`RR-999`", "unknown residual-risk"),
            ("docs/security/data-flow.md", "`E-01`", "`E-99`", "unknown external-entity"),
            ("docs/security/data-flow.md", "`P-01`", "`P-99`", "unknown process"),
            ("docs/security/data-flow.md", "`DS-01`", "`DS-99`", "unknown store"),
            ("docs/security/data-flow.md", "`F-01`", "`F-99`", "unknown flow"),
        )
        for relative, old, new, expected_error_fragment in mutations:
            with self.subTest(relative=relative, new=new), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_security_documents(root)
                self.replace_once(root / relative, old, new)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(
                        expected_error_fragment in error
                        for error in validation.errors
                    )
                )

    def test_threat_and_abuse_links_must_be_reciprocal(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_security_documents(root)
            abuse_path = root / "docs/security/abuse-cases.md"
            self.replace_once(
                abuse_path,
                "**Threats:** `THR-001`, `THR-002`, `THR-012`",
                "**Threats:** `THR-002`, `THR-012`",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any("THR-001 links AC-002" in error for error in validation.errors)
            )

    def test_security_semantic_fields_cannot_be_weakened(self) -> None:
        mutations = (
            (
                "docs/security/data-flow.md",
                "**Model revision:** TL-0004 approved 1",
                "**Model revision:** TL-0004 approved 2",
                "must share one exact model revision",
            ),
            (
                "docs/security/threat-model.md",
                "**Initial risk:** High",
                "**Initial risk:** Low",
                "Initial risk cannot be below Likelihood or Impact",
            ),
            (
                "docs/security/threat-model.md",
                "**Boundaries/flows:** `TB-UI`, `TB-BROKER`, catalogue/profile and approval flows",
                "**Boundaries/flows:** catalogue/profile and approval flows",
                "needs at least one typed trust-boundary reference",
            ),
            (
                "docs/security/threat-model.md",
                "**Decisions:** D-022, D-023, D-030",
                "**Decisions:** None",
                "needs at least one typed decision reference",
            ),
            (
                "docs/security/threat-model.md",
                "**Control status:** Planned",
                "**Control status:** Verified",
                "verified control status must cite only mapped done tasks",
            ),
            (
                "docs/security/threat-model.md",
                "**Control status:** Planned",
                "**Control status:** Verified by `TL-0002`",
                "verified control status must cite only mapped done tasks",
            ),
            (
                "docs/security/abuse-cases.md",
                "**Actor:** `ACT-05` malicious metadata source or a mistaken catalogue/profile author",
                "**Actor:** malicious metadata source",
                "needs at least one typed actor reference",
            ),
            (
                "docs/security/abuse-cases.md",
                "**Assets/impact:** `AST-04`, `AST-05`, `AST-06`;",
                "**Assets/impact:** policy and broker assets;",
                "needs at least one typed asset reference",
            ),
            (
                "docs/security/threat-model.md",
                "`RR-002` | `THR-001`, `THR-003`, `THR-014`",
                "`RR-002` | `THR-004`",
                "share no linked threat",
            ),
            (
                "docs/security/threat-model.md",
                "`RR-002` | `THR-001`, `THR-003`, `THR-014` | A curated catalogue",
                "`RR-002` | `THR-004` | `THR-001`, `THR-003`, `THR-014`; a curated catalogue",
                "share no linked threat",
            ),
        )
        for relative, old, new, expected_error_fragment in mutations:
            with self.subTest(new=new), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_security_documents(root)
                self.replace_once(root / relative, old, new)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(
                        expected_error_fragment in error
                        for error in validation.errors
                    )
                )

    def test_threat_and_abuse_entries_cannot_be_jointly_orphaned(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_security_documents(root)
            threat_path = root / "docs/security/threat-model.md"
            abuse_path = root / "docs/security/abuse-cases.md"
            self.replace_once(threat_path, "**Abuse cases:** `AC-019`", "**Abuse cases:** None")
            self.replace_once(abuse_path, "**Threats:** `THR-014`", "**Threats:** None")
            validation = self.validate_documents(root)
            self.assertTrue(
                any("THR-014 needs at least one typed abuse-case" in error for error in validation.errors)
            )
            self.assertTrue(
                any("AC-019 needs at least one typed threat" in error for error in validation.errors)
            )

    def test_security_documents_reject_machine_specific_paths(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_security_documents(root)
            abuse_path = root / "docs/security/abuse-cases.md"
            abuse_path.write_text(
                abuse_path.read_text(encoding="utf-8")
                + "\nSynthetic local path: C:\\Users\\Example\\evidence.txt\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertIn(
                "docs/security/abuse-cases.md: contains a machine-specific path",
                validation.errors,
            )

    def test_incoherent_approved_state_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_security_documents(root)
            self.make_pending_security_review(root)
            threat_path = root / "docs/security/threat-model.md"
            self.replace_once(
                threat_path,
                "**Security-owner approval:** **Pending**",
                "**Security-owner approval:** **Approved**",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any("approved model needs named owner/role" in error for error in validation.errors)
            )
            self.assertTrue(
                any("explicit treatment decision for every residual" in error for error in validation.errors)
            )
            self.assertTrue(
                any("real ISO approval date" in error for error in validation.errors)
            )
            self.assertTrue(
                any("immutable 40-character Git commit" in error for error in validation.errors)
            )
            self.assertTrue(
                any("Approved security documents must have status" in error for error in validation.errors)
            )

    def test_done_task_cannot_retain_pending_security_review(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_security_documents(root)
            self.make_pending_security_review(root)
            task_by_id = dict(TASK_BY_ID)
            threat_model_task = dict(task_by_id["TL-0004"])
            threat_model_task["status"] = "done"
            threat_model_task["evidence"] = []
            task_by_id["TL-0004"] = threat_model_task
            validation = self.validate_documents(root, task_by_id)
            self.assertIn(
                "TL-0004 cannot be done while security-owner approval is Pending",
                validation.errors,
            )
            self.assertTrue(
                any("done evidence must record" in error for error in validation.errors)
            )

    def test_coherent_approved_state_can_validate(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_security_documents(root)
            threat_path = root / "docs/security/threat-model.md"
            threat_text = threat_path.read_text(encoding="utf-8")
            approval_commit = re.search(r"\b[0-9a-fA-F]{40}\b", threat_text)
            self.assertIsNotNone(approval_commit)

            task_by_id = dict(TASK_BY_ID)
            threat_model_task = dict(task_by_id["TL-0004"])
            threat_model_task["status"] = "done"
            threat_model_task["evidence"] = [
                {
                    "summary": "Named security owner approved the initial model and residual-risk decisions.",
                    "result": "failed",
                    "reference": f"reviewed commit {approval_commit.group(0)}",
                }
            ]
            task_by_id["TL-0004"] = threat_model_task
            validation = self.validate_documents(root, task_by_id)
            self.assertTrue(
                any("done evidence must record" in error for error in validation.errors)
            )

            threat_model_task["evidence"][0]["result"] = "passed"
            validation = self.validate_documents(root, task_by_id)
            self.assertEqual(validation.errors, [])


class GovernanceDocumentContractTests(unittest.TestCase):
    def copy_governance_documents(self, root: Path) -> None:
        for relative in GOVERNANCE_DOCUMENTS:
            destination = root / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(
                (REPOSITORY_ROOT / relative).read_text(encoding="utf-8"),
                encoding="utf-8",
            )

    def validate_documents(self, root: Path) -> validate_bundle.Validation:
        validation = validate_bundle.Validation()
        with patch.object(validate_bundle, "ROOT", root):
            validate_bundle.validate_governance_documents(validation)
        return validation

    def test_current_governance_documents_satisfy_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_governance_documents(root)
            validation = self.validate_documents(root)
            self.assertEqual(validation.errors, [])

    def test_missing_glossary_term_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_governance_documents(root)
            glossary_path = root / "docs/glossary.md"
            glossary_path.write_text(
                glossary_path.read_text(encoding="utf-8").replace(
                    "## Verified\n",
                    "## Confirmed\n",
                    1,
                ),
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertIn(
                "docs/glossary.md: missing required term heading 'Verified'",
                validation.errors,
            )

    def test_reordered_authority_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_governance_documents(root)
            change_control_path = root / "docs/change-control.md"
            text = change_control_path.read_text(encoding="utf-8")
            text = text.replace(
                "1. `DECISIONS.md`\n2. `ROADMAP.md`",
                "1. `ROADMAP.md`\n2. `DECISIONS.md`",
                1,
            )
            change_control_path.write_text(text, encoding="utf-8")
            validation = self.validate_documents(root)
            self.assertIn(
                "docs/change-control.md: authority order must exactly match D-045",
                validation.errors,
            )

    def test_changed_release_cut_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_governance_documents(root)
            product_contract_path = root / "docs/product-contract.md"
            product_contract_path.write_text(
                product_contract_path.read_text(encoding="utf-8").replace(
                    "M0 through M6",
                    "M0 through M5",
                    1,
                ),
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any("M0 through M6" in error for error in validation.errors)
            )


class SupplyChainRepositoryContractTests(unittest.TestCase):
    def copy_file(self, root: Path, relative: str) -> Path:
        source = REPOSITORY_ROOT / relative
        destination = root / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        return destination

    def current_inventory(self) -> dict[str, dict[str, str]]:
        validation = validate_repository.Validation()
        inventory = validate_repository.expected_supply_chain_inventory(validation)
        self.assertEqual(validation.errors, [])
        self.assertEqual(
            len(inventory), validate_repository.EXPECTED_MATRIX_COMPONENT_COUNT
        )
        return inventory

    def read_current_matrix(self) -> list[dict[str, str]]:
        validation = validate_repository.Validation()
        rows = validate_repository._read_license_matrix(validation)
        self.assertEqual(validation.errors, [])
        return rows

    def write_matrix(self, path: Path, rows: list[dict[str, str]]) -> None:
        with path.open("w", encoding="utf-8", newline="") as handle:
            writer = csv.DictWriter(
                handle,
                fieldnames=validate_repository.LICENSE_MATRIX_HEADER,
                lineterminator="\n",
            )
            writer.writeheader()
            writer.writerows(rows)

    def release_ready_rows(self) -> list[dict[str, str]]:
        rows = [dict(row) for row in self.read_current_matrix()]
        for row in rows:
            row["review_status"] = "approved"
            row["installation_rights"] = "permitted"
            row["redistribution_rights"] = "not_applicable"
            row["bundled_in_release"] = "no"
            row["notice_requirements"] = "not_applicable"
            if row["license_expression"] == "NOASSERTION":
                row["license_expression"] = "MIT"
        return rows

    def validate_approved_review(
        self,
        root: Path,
        rows: list[dict[str, str]],
        *,
        generated_sbom_digest: str = "b" * 64,
        current_sbom_digest: str | None = None,
        approval_reference: str = (
            "PikkuJanne approved TL-0006 dependency rights"
        ),
        retained_current_state: str = "",
        task_status: str | None = None,
        task_evidence: list[dict[str, str]] | None = None,
    ) -> validate_repository.Validation:
        for relative in (*SUPPLY_CHAIN_DOCUMENTS, "TASKS.yaml"):
            self.copy_file(root, relative)
        matrix_path = root / "docs/supply-chain/license-matrix.csv"
        self.write_matrix(matrix_path, rows)
        matrix_digest = hashlib.sha256(matrix_path.read_bytes()).hexdigest()

        document_path = root / "docs/supply-chain/dependencies.md"
        document = document_path.read_text(encoding="utf-8")
        replacements = (
            (
                "**Status:** Draft for licence-owner review",
                "**Status:** Approved",
            ),
            (
                "**Control revision:** TL-0006 draft 1",
                "**Control revision:** TL-0006 approved test fixture 1",
            ),
            ("**Review result:** Pending", "**Review result:** Approved"),
            (
                "**Reviewing owner and role:** Pending",
                "**Reviewing owner and role:** PikkuJanne — Licence owner",
            ),
            (
                "**Review date:** Pending",
                f"**Review date:** {validate_repository.date.today().isoformat()}",
            ),
            (
                "**Reviewed source commit:** Pending",
                "**Reviewed source commit:** " + "a" * 40,
            ),
            (
                "**Reviewed matrix SHA-256:** Pending",
                f"**Reviewed matrix SHA-256:** {matrix_digest}",
            ),
            (
                "**Generated SBOM SHA-256:** Pending",
                f"**Generated SBOM SHA-256:** {generated_sbom_digest}",
            ),
            (
                "**Approval reference:** Pending",
                f"**Approval reference:** {approval_reference}",
            ),
            (
                "**Review state:** **Pending — no approval has been recorded.**",
                "**Review state:** **Approved.**",
            ),
            (
                "Every current rights field is `pending_human_review`, every current "
                "review status is `pending`, and every current component is "
                "`bundled_in_release=no`.",
                "Every current rights field and review status has an explicit reviewed "
                "value in this synthetic approved fixture; every current component is "
                "`bundled_in_release=no`.",
            ),
            (
                "Its expression remains `NOASSERTION` until an immutable licence source "
                "for the exact package is reviewed.",
                "Its expression and immutable licence evidence are resolved in this "
                "synthetic approved fixture.",
            ),
            (
                "the .NET SDK row remains `NOASSERTION` because source-code licensing "
                "and Microsoft binary-distribution terms must not be collapsed into one "
                "unreviewed expression.",
                "the .NET SDK row uses the exact expression and separate binary terms "
                "reviewed for this synthetic approved fixture.",
            ),
            (
                "At this draft revision those conditions are intentionally unsatisfied.",
                "In this synthetic approved fixture those conditions are satisfied.",
            ),
            (
                "Human licence and separate-rights review is pending, so release-mode "
                "generation and task completion remain gated.",
                "Named human licence and separate-rights review is recorded for this "
                "synthetic approved fixture.",
            ),
            (
                "`xunit.abstractions` and the .NET SDK have `NOASSERTION` licence "
                "expressions pending exact review.",
                "The synthetic approved matrix resolves the formerly unasserted licence "
                "expressions.",
            ),
        )
        for old, new in replacements:
            self.assertIn(old, document)
            document = document.replace(old, new, 1)
        document, substitution_count = re.subn(
            r"No licence or redistribution approval is recorded\..*?"
            r"release mode remains unavailable until then\.",
            "A named licence owner approved this synthetic review fixture.",
            document,
            count=1,
            flags=re.DOTALL,
        )
        self.assertEqual(substitution_count, 1)
        if retained_current_state:
            document += f"\n{retained_current_state}\n"
        document_path.write_text(document, encoding="utf-8")

        if task_status is not None or task_evidence is not None:
            task_path = root / "TASKS.yaml"
            task_document = validate_bundle.yaml.safe_load(
                task_path.read_text(encoding="utf-8")
            )
            task = next(
                task for task in task_document["tasks"] if task["id"] == "TL-0006"
            )
            if task_status is not None:
                task["status"] = task_status
            if task_evidence is not None:
                task["evidence"] = task_evidence
            task_path.write_text(
                validate_bundle.yaml.safe_dump(task_document, sort_keys=False),
                encoding="utf-8",
            )

        validation = validate_repository.Validation()
        with (
            patch.object(validate_repository, "ROOT", root),
            patch.object(
                validate_repository, "_git_commit_is_reachable", return_value=True
            ),
            patch.object(
                validate_repository,
                "_current_development_sbom_digest",
                return_value=current_sbom_digest or generated_sbom_digest,
            ),
        ):
            validate_repository.validate_supply_chain_approval(rows, validation)
        return validation

    def test_current_supply_chain_controls_satisfy_contract(self) -> None:
        validation = validate_repository.Validation()
        validate_repository.validate_supply_chain_controls(validation)
        self.assertEqual(validation.errors, [])

    def test_unhashed_python_requirement_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            requirements = root / "tools/requirements.txt"
            requirements.parent.mkdir(parents=True)
            requirements.write_text("PyYAML==6.0.3\n", encoding="utf-8")
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository._load_python_requirements(validation)
            self.assertTrue(
                any("carry one lowercase SHA-256 hash" in error for error in validation.errors)
            )

    def test_python_requirement_cannot_select_an_alternate_index(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            requirements = root / "tools/requirements.txt"
            requirements.parent.mkdir(parents=True)
            requirements.write_text(
                "--extra-index-url https://example.invalid/simple\n"
                "PyYAML==6.0.3 --hash=sha256:"
                "4a2e8cebe2ff6ab7d1050ecd59c25d4c8bd7e6f400f5f82b96557ac0abafd0ac\n",
                encoding="utf-8",
            )
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository._load_python_requirements(validation)
            self.assertTrue(
                any("requirement must be exact" in error for error in validation.errors)
            )

    def test_matrix_version_drift_is_rejected(self) -> None:
        expected = self.current_inventory()
        rows = self.read_current_matrix()
        rows[0] = dict(rows[0])
        rows[0]["version"] = "999.0.0"
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            matrix_path = root / "docs/supply-chain/license-matrix.csv"
            matrix_path.parent.mkdir(parents=True)
            self.write_matrix(matrix_path, rows)
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository.validate_license_matrix(expected, validation)
            self.assertTrue(
                any("version must equal" in error for error in validation.errors)
            )

    def test_matrix_hash_drift_is_rejected(self) -> None:
        expected = self.current_inventory()
        rows = self.read_current_matrix()
        hashed_index = next(
            index
            for index, row in enumerate(rows)
            if row["content_hash_algorithm"] == "SHA-512"
        )
        rows[hashed_index] = dict(rows[hashed_index])
        rows[hashed_index]["content_hash"] = "0" * 128
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            matrix_path = root / "docs/supply-chain/license-matrix.csv"
            matrix_path.parent.mkdir(parents=True)
            self.write_matrix(matrix_path, rows)
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository.validate_license_matrix(expected, validation)
            self.assertTrue(
                any("content_hash must equal" in error for error in validation.errors)
            )

    def test_matrix_rows_must_be_component_sorted(self) -> None:
        expected = self.current_inventory()
        rows = self.read_current_matrix()
        rows[0], rows[1] = rows[1], rows[0]
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            matrix_path = root / "docs/supply-chain/license-matrix.csv"
            matrix_path.parent.mkdir(parents=True)
            self.write_matrix(matrix_path, rows)
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository.validate_license_matrix(expected, validation)
            self.assertIn(
                "docs/supply-chain/license-matrix.csv: rows must be sorted by governed ecosystem order and component_id",
                validation.errors,
            )

    def test_bundled_component_needs_approved_redistribution_rights(self) -> None:
        expected = self.current_inventory()
        rows = self.read_current_matrix()
        rows[0] = dict(rows[0])
        rows[0]["bundled_in_release"] = "yes"
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            matrix_path = root / "docs/supply-chain/license-matrix.csv"
            matrix_path.parent.mkdir(parents=True)
            self.write_matrix(matrix_path, rows)
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository.validate_license_matrix(expected, validation)
            self.assertTrue(
                any("cannot be bundled" in error for error in validation.errors)
            )

    def copy_audit_files(self, root: Path) -> None:
        for relative in (
            "Directory.Build.props",
            "Directory.Packages.props",
            ".github/workflows/verify.yml",
            "eng/verify.ps1",
            "eng/verify.sh",
        ):
            self.copy_file(root, relative)

    def test_nuget_audit_suppression_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_audit_files(root)
            build_props = root / "Directory.Build.props"
            build_props.write_text(
                build_props.read_text(encoding="utf-8").replace(
                    "</Project>",
                    "  <ItemGroup><NuGetAuditSuppress Include=\"https://example.invalid/advisory\" /></ItemGroup>\n</Project>",
                ),
                encoding="utf-8",
            )
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository.validate_nuget_audit_policy([], validation)
            self.assertTrue(
                any("NuGetAuditSuppress entries are prohibited" in error for error in validation.errors)
            )

    def test_nuget_warning_suppression_is_rejected(self) -> None:
        for suppressed_code in ("NU1903", "NU19*", "NU1*", "NU*"):
            with self.subTest(suppressed_code=suppressed_code), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_audit_files(root)
                build_props = root / "Directory.Build.props"
                build_props.write_text(
                    build_props.read_text(encoding="utf-8").replace(
                        "</PropertyGroup>",
                        f"  <NoWarn>{suppressed_code}</NoWarn>\n  </PropertyGroup>",
                        1,
                    ),
                    encoding="utf-8",
                )
                validation = validate_repository.Validation()
                with patch.object(validate_repository, "ROOT", root):
                    validate_repository.validate_nuget_audit_policy([], validation)
                self.assertTrue(
                    any("must not suppress NuGet audit warnings" in error for error in validation.errors)
                )

    def test_nuget_audit_policy_cannot_be_conditioned_by_ancestor(self) -> None:
        mutations = (
            (
                "<PropertyGroup>",
                "<PropertyGroup Condition=\"'$(Configuration)' == 'Release'\">",
            ),
            (
                "<Project>",
                "<Project><Choose><When Condition=\"'$(Configuration)' == 'Release'\">",
            ),
        )
        for old, new in mutations:
            with self.subTest(new=new), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_audit_files(root)
                build_props = root / "Directory.Build.props"
                document = build_props.read_text(encoding="utf-8").replace(old, new, 1)
                if old == "<Project>":
                    document = document.replace(
                        "</PropertyGroup>",
                        "</PropertyGroup></When></Choose>",
                        1,
                    )
                build_props.write_text(document, encoding="utf-8")
                validation = validate_repository.Validation()
                with patch.object(validate_repository, "ROOT", root):
                    validate_repository.validate_nuget_audit_policy([], validation)
                self.assertTrue(
                    any("conditional XML context" in error for error in validation.errors)
                )

    def test_nuget_audit_override_in_additional_targets_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_audit_files(root)
            targets = root / "eng/override.targets"
            targets.parent.mkdir(parents=True, exist_ok=True)
            targets.write_text(
                "<Project><PropertyGroup><NuGetAudit>false</NuGetAudit>"
                "</PropertyGroup></Project>\n",
                encoding="utf-8",
            )
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository.validate_nuget_audit_policy([], validation)
            self.assertTrue(
                any("project-local NuGetAudit overrides" in error for error in validation.errors)
            )

    def test_explicit_msbuild_import_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_audit_files(root)
            packages_props = root / "Directory.Packages.props"
            packages_props.write_text(
                packages_props.read_text(encoding="utf-8").replace(
                    "</Project>",
                    '  <Import Project="eng\\override.targets" />\n</Project>',
                ),
                encoding="utf-8",
            )
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository.validate_nuget_audit_policy([], validation)
            self.assertTrue(
                any(
                    "explicit MSBuild Import elements are prohibited" in error
                    for error in validation.errors
                ),
                validation.errors,
            )

    def test_verify_surface_rejects_indirect_nuget_audit_override_token(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_audit_files(root)
            verify_script = root / "eng/verify.ps1"
            verify_script.write_text(
                verify_script.read_text(encoding="utf-8")
                + '\n$auditPropertyName = "NuGetAuditMode"\n',
                encoding="utf-8",
            )
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository.validate_nuget_audit_policy([], validation)
            self.assertTrue(
                any(
                    "NuGetAudit override tokens are prohibited" in error
                    for error in validation.errors
                ),
                validation.errors,
            )

    def test_workflow_requires_hash_binary_index_and_full_history_controls(self) -> None:
        mutations = (
            (" --require-hashes", ""),
            (" --only-binary=:all:", ""),
            ("          fetch-depth: 0\n", ""),
        )
        for old, new in mutations:
            with self.subTest(old=old), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                workflow = self.copy_file(root, ".github/workflows/verify.yml")
                text = workflow.read_text(encoding="utf-8")
                self.assertIn(old, text)
                workflow.write_text(text.replace(old, new, 1), encoding="utf-8")
                validation = validate_repository.Validation()
                with patch.object(validate_repository, "ROOT", root):
                    validate_repository.validate_workflow(validation)
                self.assertTrue(validation.errors)

    def test_readme_install_examples_cannot_drift_from_ci(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            readme = self.copy_file(root, "README.md")
            text = readme.read_text(encoding="utf-8")
            self.assertIn("--only-binary=:all:", text)
            readme.write_text(
                text.replace(" --only-binary=:all:", "", 1), encoding="utf-8"
            )
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository.validate_readme_tool_install(validation)
            self.assertTrue(
                any("both Python setup examples" in error for error in validation.errors)
            )

    def test_pending_licence_review_cannot_be_marked_done(self) -> None:
        rows = self.read_current_matrix()
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            for relative in (*SUPPLY_CHAIN_DOCUMENTS, "TASKS.yaml"):
                self.copy_file(root, relative)
            task_path = root / "TASKS.yaml"
            task_document = validate_bundle.yaml.safe_load(
                task_path.read_text(encoding="utf-8")
            )
            task = next(task for task in task_document["tasks"] if task["id"] == "TL-0006")
            task["status"] = "done"
            task_path.write_text(
                validate_bundle.yaml.safe_dump(task_document, sort_keys=False),
                encoding="utf-8",
            )
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository.validate_supply_chain_approval(rows, validation)
            self.assertIn(
                "TL-0006 cannot be done while licence-owner review is Pending",
                validation.errors,
            )

    def test_approved_review_rejects_release_gate_row_gaps(self) -> None:
        mutations = (
            (
                "license_expression",
                "NOASSERTION",
                "license_expression cannot be NOASSERTION",
            ),
            (
                "notice_requirements",
                "pending_human_review",
                "notice_requirements cannot be pending_human_review",
            ),
            (
                "bundled_in_release",
                "pending",
                "bundled_in_release cannot be pending",
            ),
            (
                "installation_rights",
                "prohibited",
                "installation_rights cannot be prohibited",
            ),
        )
        for field, value, expected_error in mutations:
            with self.subTest(field=field), tempfile.TemporaryDirectory() as temporary_directory:
                rows = self.release_ready_rows()
                rows[0][field] = value
                validation = self.validate_approved_review(
                    Path(temporary_directory), rows
                )
                self.assertTrue(
                    any(expected_error in error for error in validation.errors),
                    validation.errors,
                )

    def test_approved_review_allows_prohibited_redistribution_when_not_bundled(
        self,
    ) -> None:
        rows = self.release_ready_rows()
        rows[0]["redistribution_rights"] = "prohibited"
        rows[0]["bundled_in_release"] = "no"
        with tempfile.TemporaryDirectory() as temporary_directory:
            validation = self.validate_approved_review(Path(temporary_directory), rows)
        self.assertEqual(validation.errors, [])

    def test_approved_review_rejects_bundling_with_prohibited_redistribution(
        self,
    ) -> None:
        rows = self.release_ready_rows()
        rows[0]["redistribution_rights"] = "prohibited"
        rows[0]["bundled_in_release"] = "yes"
        with tempfile.TemporaryDirectory() as temporary_directory:
            validation = self.validate_approved_review(Path(temporary_directory), rows)
        self.assertTrue(
            any(
                "only when bundled_in_release=no" in error
                for error in validation.errors
            ),
            validation.errors,
        )

    def test_approved_review_requires_current_development_sbom_digest(self) -> None:
        rows = self.release_ready_rows()
        with tempfile.TemporaryDirectory() as temporary_directory:
            validation = self.validate_approved_review(
                Path(temporary_directory),
                rows,
                generated_sbom_digest="b" * 64,
                current_sbom_digest="c" * 64,
            )
        self.assertTrue(
            any(
                "does not match the current deterministic development SBOM" in error
                for error in validation.errors
            ),
            validation.errors,
        )

    def test_approved_review_rejects_negated_approval_reference(self) -> None:
        rows = self.release_ready_rows()
        with tempfile.TemporaryDirectory() as temporary_directory:
            validation = self.validate_approved_review(
                Path(temporary_directory),
                rows,
                approval_reference=(
                    "PikkuJanne did not approve TL-0006 dependency rights"
                ),
            )
        self.assertTrue(
            any(
                "approval reference must affirm the named owner's approval" in error
                for error in validation.errors
            ),
            validation.errors,
        )

    def test_done_evidence_rejects_negated_or_declined_approval(self) -> None:
        metadata = {
            "Reviewing owner and role": "PikkuJanne — Licence owner",
            "Review date": "2026-08-14",
            "Reviewed source commit": "a" * 40,
            "Reviewed matrix SHA-256": "b" * 64,
            "Generated SBOM SHA-256": "c" * 64,
        }
        required_values = " ".join(
            (
                "PikkuJanne",
                metadata["Reviewed source commit"],
                metadata["Reviewed matrix SHA-256"],
                metadata["Generated SBOM SHA-256"],
            )
        )
        for summary in (
            "PikkuJanne did not approve TL-0006",
            "PikkuJanne declined approval for TL-0006",
        ):
            with self.subTest(summary=summary):
                task = {
                    "evidence": [
                        {
                            "result": "passed",
                            "date": metadata["Review date"],
                            "environment": "Windows 11 repository review",
                            "summary": summary,
                            "reference": required_values,
                        }
                    ]
                }
                self.assertFalse(
                    validate_repository._approval_evidence_matches(task, metadata)
                )

    def test_approved_review_rejects_retained_pending_current_state(self) -> None:
        rows = self.release_ready_rows()
        with tempfile.TemporaryDirectory() as temporary_directory:
            validation = self.validate_approved_review(
                Path(temporary_directory),
                rows,
                retained_current_state="Human licence review remains pending.",
            )
        self.assertTrue(
            any(
                "must remove draft/Pending/no-approval current-state wording" in error
                for error in validation.errors
            ),
            validation.errors,
        )

    def test_approved_done_state_needs_digest_bound_passed_evidence(self) -> None:
        rows = self.release_ready_rows()
        with tempfile.TemporaryDirectory() as temporary_directory:
            validation = self.validate_approved_review(
                Path(temporary_directory),
                rows,
                task_status="done",
                task_evidence=[],
            )
            self.assertTrue(
                any("done evidence must bind" in error for error in validation.errors)
            )


if __name__ == "__main__":
    unittest.main()
