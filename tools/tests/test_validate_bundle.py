#!/usr/bin/env python3
"""Regression tests for ThirdLife governance validation."""

from __future__ import annotations

import copy
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from urllib.parse import quote
from unittest.mock import patch


TOOLS_ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = TOOLS_ROOT.parent
sys.path.insert(0, str(TOOLS_ROOT))

import validate_bundle  # noqa: E402


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
PRIVACY_DOCUMENTS = (
    "docs/privacy/logging-standard.md",
    "docs/privacy/privacy-model.md",
    "docs/privacy/redaction-test-cases.yaml",
)
PRIVACY_REVIEWED_COMMIT = "e1880667619793e0a784020d1234f58c37ac2b5f"
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


class PrivacyDocumentContractTests(unittest.TestCase):
    def copy_privacy_documents(self, root: Path) -> None:
        for relative in PRIVACY_DOCUMENTS:
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
        reviewed_sources: dict[str, str] | None = None,
        reviewed_commit_reachable: bool = True,
    ) -> validate_bundle.Validation:
        validation = validate_bundle.Validation()
        if reviewed_sources is None:
            reviewed_sources = self.reviewed_privacy_sources()
        with patch.object(validate_bundle, "ROOT", root):
            with patch.object(
                validate_bundle,
                "git_commit_is_reachable",
                return_value=reviewed_commit_reachable,
            ), patch.object(
                validate_bundle,
                "read_git_text_at_commit",
                side_effect=lambda _commit, relative: reviewed_sources.get(relative),
            ):
                validate_bundle.validate_privacy_documents(
                    validation,
                    TASK_BY_ID if task_by_id is None else task_by_id,
                    DECISION_IDS,
                )
        return validation

    def read_fixture(self, root: Path) -> dict[str, object]:
        return validate_bundle.yaml.safe_load(
            (root / "docs/privacy/redaction-test-cases.yaml").read_text(
                encoding="utf-8"
            )
        )

    def write_fixture(self, root: Path, fixture: dict[str, object]) -> None:
        (root / "docs/privacy/redaction-test-cases.yaml").write_text(
            validate_bundle.yaml.safe_dump(
                fixture,
                allow_unicode=True,
                sort_keys=False,
                width=100,
            ),
            encoding="utf-8",
        )

    def fixture_case(
        self,
        fixture: dict[str, object],
        *,
        category: str | None = None,
        sink: str | None = None,
        tag: str | None = None,
    ) -> dict[str, object]:
        cases = fixture["cases"]
        self.assertIsInstance(cases, list)
        for case in cases:
            self.assertIsInstance(case, dict)
            if category is not None and category not in case.get("categories", []):
                continue
            if sink is not None and case.get("sink") != sink:
                continue
            if tag is not None and tag not in case.get("tags", []):
                continue
            return case
        self.fail(
            f"No fixture case found for category={category!r}, sink={sink!r}, tag={tag!r}"
        )

    def replace_once(self, path: Path, old: str, new: str) -> None:
        text = path.read_text(encoding="utf-8")
        self.assertEqual(text.count(old), 1, f"Expected one occurrence of {old!r}")
        path.write_text(text.replace(old, new, 1), encoding="utf-8")

    def reviewed_privacy_sources(self) -> dict[str, str]:
        cached_sources = getattr(self, "_reviewed_privacy_sources_cache", None)
        if cached_sources is not None:
            return dict(cached_sources)
        self.assertTrue(
            validate_bundle.git_commit_is_reachable(PRIVACY_REVIEWED_COMMIT),
            f"Reviewed privacy commit is not reachable: {PRIVACY_REVIEWED_COMMIT}",
        )
        reviewed_sources: dict[str, str] = {}
        for relative in PRIVACY_DOCUMENTS:
            content = validate_bundle.read_git_text_at_commit(
                PRIVACY_REVIEWED_COMMIT, relative
            )
            if content is None:
                self.fail(
                    f"Reviewed privacy source is unavailable at "
                    f"{PRIVACY_REVIEWED_COMMIT}:{relative}"
                )
            reviewed_sources[relative] = content
        self._reviewed_privacy_sources_cache = dict(reviewed_sources)
        return dict(reviewed_sources)

    def restore_reviewed_privacy_sources(
        self, root: Path, reviewed_sources: dict[str, str]
    ) -> None:
        for relative, content in reviewed_sources.items():
            destination = root / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(content, encoding="utf-8")

    def make_pending_privacy_review(self, root: Path) -> None:
        self.restore_reviewed_privacy_sources(
            root, self.reviewed_privacy_sources()
        )

    def make_approved_privacy_review(
        self, root: Path
    ) -> tuple[str, dict[str, str]]:
        reviewed_commit = PRIVACY_REVIEWED_COMMIT
        reviewed_sources = self.reviewed_privacy_sources()
        self.restore_reviewed_privacy_sources(root, reviewed_sources)
        model_path = root / "docs/privacy/privacy-model.md"
        model_replacements = (
            ("**Status:** Draft for privacy-owner review", "**Status:** Approved privacy model"),
            ("**Privacy-owner approval:** **Pending**", "**Privacy-owner approval:** **Approved**"),
            ("**Approving owner and role:** Pending", "**Approving owner and role:** PikkuJanne — Privacy owner"),
            ("**Approval date:** Pending", "**Approval date:** 2026-08-14"),
            ("**Reviewed source commit:** Pending", f"**Reviewed source commit:** {reviewed_commit}"),
            (
                "**Approval reference:** Pending",
                "**Approval reference:** TASKS.yaml TL-0005 privacy-owner approval evidence",
            ),
            (
                "No privacy-owner approval is recorded in this draft.",
                "Named privacy-owner approval is recorded for this exact revision.",
            ),
            (
                "No approval is present in this draft.",
                "Named privacy-owner approval covers the classifications and default retention guidance for this exact committed revision.",
            ),
            (
                "`TL-0005` must remain in review until a named privacy owner approves the classifications and default retention guidance for an exact committed revision.",
                "",
            ),
        )
        for old, new in model_replacements:
            self.replace_once(model_path, old, new)
        model_text = model_path.read_text(encoding="utf-8")
        model_text, disposition_count = re.subn(
            r"^- \[ \] (`PR-\d{2}` — .+)$",
            r"- [x] \1 **Disposition:** Approve — reviewed without conditions.",
            model_text,
            flags=re.MULTILINE,
        )
        self.assertEqual(disposition_count, 16)
        model_path.write_text(model_text, encoding="utf-8")

        logging_path = root / "docs/privacy/logging-standard.md"
        logging_replacements = (
            ("**Status:** Draft for privacy-owner review", "**Status:** Approved privacy model"),
            (
                "**Review result:** Pending",
                "**Review result:** Approved",
            ),
            (
                "No named privacy owner has approved this exact revision, its classifications, prohibited and allowed fields, sink contracts, or proposed default retention guidance.",
                "Named privacy-owner approval covers this exact revision, its classifications, prohibited and allowed fields, sink contracts, and proposed default retention guidance.",
            ),
            (
                "This pending draft does not satisfy the human evidence required by `TL-0005`.",
                "Approval evidence is recorded for the exact reviewed source and named owner.",
            ),
            (
                "Human approval of the classifications and default retention guidance remains pending.",
                "Named privacy-owner approval covers the classifications and default retention guidance.",
            ),
        )
        for old, new in logging_replacements:
            self.replace_once(logging_path, old, new)

        fixture = self.read_fixture(root)
        fixture["privacy_owner_approval"] = "Approved"
        self.write_fixture(root, fixture)
        return reviewed_commit, reviewed_sources

    def test_current_privacy_documents_satisfy_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            validation = self.validate_documents(root)
            self.assertEqual(validation.errors, [])

    def test_data_map_and_project_vacuum_exclusion_are_required(self) -> None:
        mutations = (
            ("| PD-22 |", "| PD-21 |", "data-map rows must exactly equal"),
            ("sibling workspaces", "external folders", "sibling workspaces"),
            ("assessment evidence", "review material", "assessment evidence"),
        )
        for old, new, expected in mutations:
            with self.subTest(old=old), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                model_path = root / "docs/privacy/privacy-model.md"
                if old.startswith("|"):
                    self.replace_once(model_path, old, new)
                else:
                    text = model_path.read_text(encoding="utf-8")
                    self.assertIn(old, text)
                    model_path.write_text(text.replace(old, new), encoding="utf-8")
                validation = self.validate_documents(root)
                self.assertTrue(any(expected in error for error in validation.errors))

    def test_fixture_schema_is_strict_and_case_ids_are_contiguous(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            first_case = fixture["cases"][0]
            first_case["unexpected"] = True
            fixture["cases"][1]["id"] = first_case["id"]
            del first_case["expectation"]["output"]
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(any("unknown fields" in error for error in validation.errors))
            self.assertTrue(any("case IDs must be unique" in error for error in validation.errors))
            self.assertTrue(any("expectation is missing fields" in error for error in validation.errors))

    def test_prohibited_literal_cannot_survive_expected_output(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            case = next(
                case
                for case in fixture["cases"]
                if case["expectation"]["prohibited_literals_absent"]
            )
            literal = case["expectation"]["prohibited_literals_absent"][0]
            case["expectation"]["decision"] = "replace"
            case["expectation"]["output"] = f"unsafe {literal}"
            case["expectation"]["required_literals_present"] = [literal]
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("survives expected output" in error for error in validation.errors)
            )

    def test_full_serial_is_workshop_only_and_recipient_identity_is_unnecessary(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            serial_case = self.fixture_case(
                fixture, category="full_serial", sink="support_export"
            )
            serial_case["expectation"]["decision"] = "allow"
            serial_case["expectation"]["output"] = copy.deepcopy(
                serial_case["input"]["value"]
            )
            recipientless_case = self.fixture_case(
                fixture, tag="recipientless"
            )
            recipientless_case["input"]["value"]["nested"] = {
                "recipient_identity": "SYNTHETIC_PERSON"
            }
            recipientless_case["expectation"]["output"]["recipient_name"] = "SYNTHETIC_PERSON"
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(any("full serial may be allowed only" in error for error in validation.errors))
            self.assertTrue(any("recipient-less normal contract" in error for error in validation.errors))

    def test_raw_output_external_private_data_and_telemetry_fail_closed(self) -> None:
        mutations = (
            ("raw_output", None, "allow", "raw output cannot be allowed"),
            ("external_private_content", None, "drop", "external private data must be rejected"),
            (None, "telemetry", "allow", "telemetry cases must fail closed"),
        )
        for category, sink, decision, expected in mutations:
            with self.subTest(category=category, sink=sink), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture = self.read_fixture(root)
                case = self.fixture_case(fixture, category=category, sink=sink)
                case["expectation"]["decision"] = decision
                case["expectation"]["output"] = copy.deepcopy(case["input"]["value"])
                self.write_fixture(root, fixture)
                validation = self.validate_documents(root)
                self.assertTrue(any(expected in error for error in validation.errors))

    def test_required_category_and_sink_coverage_cannot_be_removed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            for case in fixture["cases"]:
                case["categories"] = [
                    "removed_personal_path" if value == "personal_path" else value
                    for value in case["categories"]
                ]
                if case["sink"] == "local_crash":
                    case["sink"] = "ordinary_log"
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(any("personal_path" in error for error in validation.errors))
            self.assertTrue(any("local_crash" in error for error in validation.errors))

    def test_unknown_decision_revision_and_unsynthetic_input_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            fixture["model_revision"] = "TL-0005 draft 2"
            fixture["cases"][0]["decision_refs"] = ["D-999"]
            unsynthetic_case = self.fixture_case(fixture, category="recipient_identity")
            unsynthetic_case["input"]["value"] = "ordinary value"
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(any("unknown decision reference" in error for error in validation.errors))
            self.assertTrue(any("share one exact model revision" in error for error in validation.errors))
            self.assertTrue(any("reserved synthetic marker" in error for error in validation.errors))

    def test_privacy_documents_reject_machine_specific_paths(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            model_path = root / "docs/privacy/privacy-model.md"
            model_path.write_text(
                model_path.read_text(encoding="utf-8")
                + "\nExample local path: C:\\Users\\Example\\private.txt\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertIn(
                "docs/privacy/privacy-model.md: contains a machine-specific path",
                validation.errors,
            )

            fixture = self.read_fixture(root)
            fixture["cases"][0]["input"]["value"]["local_path"] = (
                "C:\\Users\\RealPerson\\private-SYNTHETIC_.txt"
            )
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("non-synthetic machine-specific path" in error for error in validation.errors)
            )

    def test_done_task_cannot_retain_pending_privacy_review(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            self.make_pending_privacy_review(root)
            task_by_id = dict(TASK_BY_ID)
            privacy_task = dict(task_by_id["TL-0005"])
            privacy_task["status"] = "review"
            privacy_task["evidence"] = []
            task_by_id["TL-0005"] = privacy_task
            validation = self.validate_documents(root, task_by_id)
            self.assertEqual(validation.errors, [])

            privacy_task["status"] = "done"
            validation = self.validate_documents(root, task_by_id)
            self.assertIn(
                "TL-0005 cannot be done while privacy-owner approval is Pending",
                validation.errors,
            )

    def test_coherent_approved_state_requires_affirmative_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            reviewed_commit, reviewed_sources = self.make_approved_privacy_review(root)
            task_by_id = dict(TASK_BY_ID)
            privacy_task = dict(task_by_id["TL-0005"])
            privacy_task["status"] = "done"
            privacy_task["evidence"] = [
                {
                    "summary": "Privacy owner approval missing for the privacy model.",
                    "result": "failed",
                    "reference": (
                        f"reviewed commit {reviewed_commit}; "
                        "TASKS.yaml TL-0005 privacy-owner approval evidence"
                    ),
                }
            ]
            task_by_id["TL-0005"] = privacy_task
            validation = self.validate_documents(
                root, task_by_id, reviewed_sources=reviewed_sources
            )
            self.assertTrue(any("evidence must record" in error for error in validation.errors))

            privacy_task["evidence"][0] = {
                "summary": "DifferentReviewer — Privacy owner approved the model.",
                "result": "passed",
                "date": "2026-08-13",
                "reference": (
                    f"reviewed commit {reviewed_commit}; "
                    "TASKS.yaml TL-0005 privacy-owner approval evidence"
                ),
            }
            validation = self.validate_documents(
                root, task_by_id, reviewed_sources=reviewed_sources
            )
            self.assertTrue(any("evidence must record" in error for error in validation.errors))

            privacy_task["evidence"][0] = {
                "summary": "PikkuJanne — Privacy owner approved the TL-0005 classifications and default retention guidance.",
                "result": "passed",
                "date": "2026-08-14",
                "reference": (
                    f"reviewed commit {reviewed_commit}; "
                    "TASKS.yaml TL-0005 privacy-owner approval evidence"
                ),
            }
            validation = self.validate_documents(
                root, task_by_id, reviewed_sources=reviewed_sources
            )
            self.assertTrue(any("evidence must record" in error for error in validation.errors))

            privacy_task["evidence"][0]["environment"] = (
                "Human privacy-owner review by PikkuJanne"
            )
            validation = self.validate_documents(
                root, task_by_id, reviewed_sources=reviewed_sources
            )
            self.assertEqual(validation.errors, [])

    def test_fixture_contract_vocabularies_and_bounds_are_exact(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            fixture["unexpected_root"] = True
            fixture["synthetic_value_policy"]["requirements"] = ["junk"] * 3
            fixture["category_vocabulary"].append("unknown_category")
            fixture["expectation_output_contract"]["ordinary_log"] = "sink ready"
            oversized_case = self.fixture_case(fixture, category="oversized_input")
            oversized_case["bounds"]["not_a_governed_bound"] = 1
            oversized_case["bounds"]["actual_scalar_utf8_bytes"] = 1
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(any("top-level fields" in error for error in validation.errors))
            self.assertTrue(any("exact safety requirements" in error for error in validation.errors))
            self.assertTrue(any("governed categories" in error for error in validation.errors))
            self.assertTrue(any("stage semantics" in error for error in validation.errors))
            self.assertTrue(any("unknown bounds" in error for error in validation.errors))
            self.assertTrue(any("deterministic synthetic generator" in error for error in validation.errors))

    def test_support_and_log_field_contracts_reject_unsafe_values(self) -> None:
        mutations = (
            ("PRV-054", "publisher", "https://packages.example.invalid/secret", "cannot contain a URI"),
            ("PRV-054", "publisher", "data:text/plain,secret", "cannot contain a URI"),
            ("PRV-054", "publisher", "urn:synthetic:package", "cannot contain a URI"),
            ("PRV-054", "publisher", "https%3A%2F%2Fpackages.example.invalid", "cannot contain a URI"),
            ("PRV-054", "publisher", "https%25253A%25252F%25252Fpackages.example.invalid", "percent-encoded data"),
            ("PRV-054", "publisher", "ｈｔｔｐｓ：／／packages.example.invalid", "cannot contain a URI"),
            ("PRV-054", "publisher", "person@example.invalid", "email address"),
            ("PRV-054", "publisher", "SYNTHETIC_PASSWORD_DO_NOT_USE", "secret material"),
            ("PRV-054", "publisher", "x" * 513, "512-byte"),
            ("PRV-054", "publisher", {"name": "Synthetic Publisher"}, "bounded scalar"),
            ("PRV-054", "architecture", "Recipient Alice Smith", "governed enum"),
            ("PRV-054", "scope", "password=SuperSecret123", "governed enum"),
            ("PRV-054", "package_id", "this is arbitrary free form!", "normalized identifier"),
            ("PRV-054", "duration_bucket", "Recipient_Alice", "governed enum"),
            ("PRV-056", "result_code", "https://logs.example.invalid/value", "cannot contain a URI"),
            ("PRV-056", "result_code", "x" * 9_000, "8-KiB"),
            ("PRV-056", "event_id", "x", "random UUIDv4"),
            ("PRV-056", "event_id", "00000000-0000-1000-8000-000000000001", "random UUIDv4"),
            ("PRV-056", "event_id", "0" * 32, "random UUIDv4"),
            ("PRV-056", "event_code", "Recipient Alice Smith", "stable compiled code"),
            ("PRV-056", "result_code", "password=SuperSecret123", "stable compiled code"),
            ("PRV-056", "correlation_ref", "Recipient_Alice_Smith", "synthetic fixture reference"),
            ("PRV-056", "native_error_code", "5", "must be an integer"),
            ("PRV-059", "content_sha256", "not-a-sha256", "lowercase SHA-256"),
            ("PRV-059", "generated_at_utc", "not-a-time", "RFC 3339"),
            ("PRV-059", "relative_name", ".", "bounded internal relative name"),
            ("PRV-059", "relative_name", "safe/", "bounded internal relative name"),
            ("PRV-059", "relative_name", "safe//item.json", "bounded internal relative name"),
            ("PRV-059", "relative_name", "CON", "bounded internal relative name"),
            ("PRV-059", "relative_name", "aux.txt", "bounded internal relative name"),
            ("PRV-059", "relative_name", "safe.", "bounded internal relative name"),
            ("PRV-061", "support_id", "Recipient_Alice_Smith", "synthetic fixture ID"),
        )
        for case_id, field, value, expected in mutations:
            with self.subTest(case_id=case_id, field=field), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture = self.read_fixture(root)
                case = next(case for case in fixture["cases"] if case["id"] == case_id)
                case["input"]["value"][field] = copy.deepcopy(value)
                case["expectation"]["output"][field] = copy.deepcopy(value)
                self.write_fixture(root, fixture)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            normal_case = next(case for case in fixture["cases"] if case["id"] == "PRV-001")
            secret_value = "UNMARKED_" + "PASSWORD_SHAPE_123!"
            normal_case["input"]["value"]["password"] = secret_value
            normal_case["expectation"]["output"]["password"] = secret_value
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("declared secret field" in error for error in validation.errors),
                validation.errors,
            )
            self.assertTrue(
                any("secret-labeled field" in error for error in validation.errors),
                validation.errors,
            )

        secret_key_mutations = (
            ("api_key", "UNMARKED_" + "SECRET_SHAPE_123!", "declared secret field"),
            ("ｐａｓｓｗｏｒｄ", "UNMARKED_" + "SECRET_SHAPE_123!", "declared secret field"),
            ("note", "password=UNMARKED_SECRET_SHAPE_123!", "embedded credential/query value"),
        )
        for field, secret_value, expected in secret_key_mutations:
            with self.subTest(secret_field=field), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture = self.read_fixture(root)
                normal_case = next(case for case in fixture["cases"] if case["id"] == "PRV-001")
                normal_case["input"]["value"][field] = secret_value
                normal_case["expectation"]["output"][field] = secret_value
                self.write_fixture(root, fixture)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            normal_case = next(case for case in fixture["cases"] if case["id"] == "PRV-001")
            normal_case["input"]["value"]["recipient_full_name"] = "Alice Smith"
            normal_case["expectation"]["output"]["recipient_full_name"] = "Alice Smith"
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("exactly job_id and device_state" in error for error in validation.errors),
                validation.errors,
            )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            identity_case = next(case for case in fixture["cases"] if case["id"] == "PRV-006")
            identity_case["sink"] = "workshop_record"
            identity_case["expectation"]["decision"] = "allow"
            identity_case["expectation"]["output"] = copy.deepcopy(
                identity_case["input"]["value"]
            )
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("recipient identity is unnecessary" in error for error in validation.errors),
                validation.errors,
            )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            telemetry_case = next(case for case in fixture["cases"] if case["id"] == "PRV-052")
            telemetry_case["sink"] = "workshop_record"
            telemetry_case["expectation"]["decision"] = "allow"
            telemetry_case["expectation"]["output"] = copy.deepcopy(
                telemetry_case["input"]["value"]
            )
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("cannot be laundered" in error for error in validation.errors),
                validation.errors,
            )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            secret_case = next(case for case in fixture["cases"] if case["id"] == "PRV-024")
            secret_case["input"] = {
                "kind": "structured_record",
                "value": {
                    "marker": "SYNTHETIC_PASSWORD_DO_NOT_USE",
                    "value": "UNMARKED_" + "PASSWORD_SHAPE_123!",
                },
            }
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("inspectable secret input form" in error for error in validation.errors),
                validation.errors,
            )

        hostile_scalars = (
            b"SYNTHETIC_BINARY_DO_NOT_USE",
            validate_bundle.date(2026, 1, 1),
            float("nan"),
        )
        for hostile_scalar in hostile_scalars:
            with self.subTest(hostile_type=type(hostile_scalar).__name__), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture = self.read_fixture(root)
                normal_case = next(case for case in fixture["cases"] if case["id"] == "PRV-001")
                normal_case["input"]["value"]["unexpected"] = hostile_scalar
                self.write_fixture(root, fixture)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any("JSON-compatible" in error for error in validation.errors),
                    validation.errors,
                )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            stack_case = next(case for case in fixture["cases"] if case["id"] == "PRV-034")
            stack_case["input"]["value"] = "\n".join(
                f"SYNTHETIC_FRAME_{index}" for index in range(40)
            )
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("max_stack_frames" in error for error in validation.errors),
                validation.errors,
            )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            stack_case = next(case for case in fixture["cases"] if case["id"] == "PRV-034")
            stack_case["input"]["value"] = " ".join(
                f"at SYNTHETIC_FRAME_{index};" for index in range(40)
            )
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("max_stack_frames" in error for error in validation.errors),
                validation.errors,
            )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            support_case = next(case for case in fixture["cases"] if case["id"] == "PRV-059")
            for payload in (
                support_case["input"]["value"],
                support_case["expectation"]["output"],
            ):
                payload["started_at_utc"] = "2026-01-02T00:00:00Z"
                payload["completed_at_utc"] = "2026-01-01T00:00:00Z"
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("cannot be later" in error for error in validation.errors),
                validation.errors,
            )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            support_case = next(case for case in fixture["cases"] if case["id"] == "PRV-059")
            for payload in (
                support_case["input"]["value"],
                support_case["expectation"]["output"],
            ):
                payload["started_at_utc"] = "2026-01-01T00:00:00Z"
                payload["completed_at_utc"] = "2026-01-02T00:00:00Z"
                payload["duration_bucket"] = "Under1Second"
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("duration_bucket does not match" in error for error in validation.errors),
                validation.errors,
            )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            password_case = self.fixture_case(fixture, category="password")
            password_case["input"]["value"] = {
                "marker": "SYNTHETIC_PASSWORD_DO_NOT_USE",
                "password": "UNMARKED_" + "PASSWORD_SHAPE_123!",
            }
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("every secret-labeled scalar" in error for error in validation.errors),
                validation.errors,
            )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            log_case = next(case for case in fixture["cases"] if case["id"] == "PRV-008")
            log_case["expectation"]["output"]["redaction_flags"] = [
                "ValueDropped",
                "ValueDropped",
            ]
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("redaction_flags" in error for error in validation.errors),
                validation.errors,
            )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            log_case = next(case for case in fixture["cases"] if case["id"] == "PRV-056")
            log_case["input"]["value"]["count_capped"] = True
            log_case["expectation"]["output"]["count_capped"] = True
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("requires item_count" in error for error in validation.errors),
                validation.errors,
            )

    def test_malformed_yaml_value_types_report_errors_without_crashing(self) -> None:
        mutations = (
            (lambda fixture: fixture.__setitem__("privacy_owner_approval", {}), "privacy_owner_approval"),
            (lambda fixture: fixture["sink_vocabulary"].append({}), "sink_vocabulary"),
            (lambda fixture: fixture["cases"][0].__setitem__("sink", {}), "unknown sink"),
            (lambda fixture: fixture["cases"][0]["input"].__setitem__("kind", []), "unsupported input kind"),
            (lambda fixture: fixture["cases"][0]["expectation"].__setitem__("decision", []), "unsupported decision"),
            (lambda fixture: fixture["cases"][0].__setitem__(1, True), "unknown fields"),
            (
                lambda fixture: next(
                    case for case in fixture["cases"] if case["id"] == "PRV-056"
                )["expectation"]["output"].__setitem__(
                    "result_code", validate_bundle.date(2026, 1, 1)
                ),
                "non-scalar YAML type",
            ),
            (
                lambda fixture: next(
                    case for case in fixture["cases"] if case["id"] == "PRV-046"
                )["bounds"].__setitem__(
                    "actual_scalar_utf8_bytes", validate_bundle.date(2026, 1, 1)
                ),
                "positive integer",
            ),
        )
        for mutate, expected in mutations:
            with self.subTest(expected=expected), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture = self.read_fixture(root)
                mutate(fixture)
                self.write_fixture(root, fixture)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

    def test_fixture_values_are_canonical_synthetic_bounded_and_typed(self) -> None:
        deeply_encoded_path = (
            "C:\\Users\\RealPerson\\secret.txt-SYNTHETIC_"
        )
        for _ in range(33):
            deeply_encoded_path = quote(deeply_encoded_path, safe="")
        mutations = (
            (
                "PRV-024",
                lambda case: case["input"].__setitem__(
                    "value",
                    {
                        "field": "SYNTHETIC_PASSWORD_DO_NOT_USE",
                        "value": "UNMARKED_" + "PASSWORD_SHAPE_123!",
                    },
                ),
                "secret-category structured field",
            ),
            (
                "PRV-024",
                lambda case: case["input"]["value"].__setitem__(
                    "value",
                    "UNMARKED_PASSWORD_SHAPE_123!_SYNTHETIC_DO_NOT_USE",
                ),
                "secret-category structured field",
            ),
            (
                "PRV-021",
                lambda case: case["input"].__setitem__(
                    "value",
                    "https://packages.example.invalid/SYNTHETIC_PACKAGE?token="
                    "UNMARKED_TOKEN_SHAPE_123#SYNTHETIC_TOKEN_DO_NOT_USE",
                ),
                "embedded credential/query value",
            ),
            (
                "PRV-017",
                lambda case: case["input"].__setitem__(
                    "value",
                    "C%3A%5CUsers%5CRealPerson%5Csecret.txt-SYNTHETIC_",
                ),
                "non-synthetic machine-specific path",
            ),
            (
                "PRV-017",
                lambda case: case["input"].__setitem__(
                    "value",
                    "Ｃ：＼Ｕｓｅｒｓ＼RealPerson＼secret-SYNTHETIC_.txt",
                ),
                "non-synthetic machine-specific path",
            ),
            (
                "PRV-017",
                lambda case: case["input"].__setitem__(
                    "value", deeply_encoded_path
                ),
                "normalization cap",
            ),
            (
                "PRV-017",
                lambda case: case["input"].__setitem__(
                    "value",
                    "C:\\SYNTHETIC_ROOT\\RealPerson\\secret.txt",
                ),
                "non-synthetic machine-specific path",
            ),
            (
                "PRV-031",
                lambda case: case["input"].__setitem__(
                    "value", {"synthetic": "SYNTHETIC_XML"}
                ),
                "requires one string value",
            ),
            (
                "PRV-031",
                lambda case: case["input"].__setitem__(
                    "value", "SYNTHETIC_" + "x" * 5_000
                ),
                "literal input exceeds",
            ),
            (
                "PRV-033",
                lambda case: case["expectation"]["output"].__setitem__(
                    "message", "Synthetic failure " + "x" * 600
                ),
                "declared max_output_utf8_bytes",
            ),
        )
        for case_id, mutate, expected in mutations:
            with self.subTest(case_id=case_id, expected=expected), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture = self.read_fixture(root)
                case = next(case for case in fixture["cases"] if case["id"] == case_id)
                mutate(case)
                self.write_fixture(root, fixture)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture_path = root / "docs/privacy/redaction-test-cases.yaml"
            fixture_path.write_text(
                fixture_path.read_text(encoding="utf-8")
                + "# "
                + "x" * validate_bundle.PRIVACY_FIXTURE_MAX_BYTES,
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any("source limit" in error for error in validation.errors),
                validation.errors,
            )

    def test_strict_fixture_yaml_rejects_duplicate_keys_and_aliases(self) -> None:
        mutations = (
            ("schema_version: 1\n", "schema_version: 1\nschema_version: 1\n", "duplicate key"),
            ("schema_version: 1", "schema_version: &shared 1", "anchors and aliases"),
        )
        for old, new, expected in mutations:
            with self.subTest(expected=expected), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture_path = root / "docs/privacy/redaction-test-cases.yaml"
                fixture_path.write_text(
                    fixture_path.read_text(encoding="utf-8").replace(old, new, 1),
                    encoding="utf-8",
                )
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

    def test_privacy_review_dates_use_matching_calendar_form(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            for relative in (
                "docs/privacy/privacy-model.md",
                "docs/privacy/logging-standard.md",
            ):
                path = root / relative
                self.replace_once(path, "**Draft date:** 2026-08-14", "**Draft date:** not-a-date")
            validation = self.validate_documents(root)
            self.assertEqual(
                sum("exact YYYY-MM-DD form" in error for error in validation.errors),
                2,
            )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            logging_path = root / "docs/privacy/logging-standard.md"
            self.replace_once(
                logging_path,
                "**Draft date:** 2026-08-14",
                "**Draft date:** 2026-08-13",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any("share one exact Draft date" in error for error in validation.errors),
                validation.errors,
            )

        for invalid_approval_date in ("20260814", "2026-W33-5"):
            with self.subTest(approval_date=invalid_approval_date), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                _, reviewed_sources = self.make_approved_privacy_review(root)
                model_path = root / "docs/privacy/privacy-model.md"
                self.replace_once(
                    model_path,
                    "**Approval date:** 2026-08-14",
                    f"**Approval date:** {invalid_approval_date}",
                )
                validation = self.validate_documents(
                    root, reviewed_sources=reviewed_sources
                )
                self.assertTrue(
                    any("approval date in exact YYYY-MM-DD form" in error for error in validation.errors),
                    validation.errors,
                )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            _, reviewed_sources = self.make_approved_privacy_review(root)
            logging_relative = "docs/privacy/logging-standard.md"
            logging_path = root / logging_relative
            self.replace_once(
                logging_path,
                "**Draft date:** 2026-08-14",
                "**Draft date:** not-a-date",
            )
            reviewed_sources[logging_relative] = reviewed_sources[
                logging_relative
            ].replace(
                "**Draft date:** 2026-08-14",
                "**Draft date:** not-a-date",
                1,
            )
            validation = self.validate_documents(
                root, reviewed_sources=reviewed_sources
            )
            self.assertTrue(
                any(
                    "logging-standard.md: Draft date" in error
                    for error in validation.errors
                ),
                validation.errors,
            )

    def test_review_source_helper_requires_a_reachable_commit_object(self) -> None:
        head = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=REPOSITORY_ROOT,
            capture_output=True,
            text=True,
            check=True,
        ).stdout.strip()
        blob = subprocess.run(
            ["git", "rev-parse", "HEAD:README.md"],
            cwd=REPOSITORY_ROOT,
            capture_output=True,
            text=True,
            check=True,
        ).stdout.strip()
        self.assertTrue(validate_bundle.git_commit_is_reachable(head))
        self.assertIsNotNone(
            validate_bundle.read_git_text_at_commit(head, "README.md")
        )
        self.assertFalse(validate_bundle.git_commit_is_reachable(blob))
        self.assertFalse(validate_bundle.git_commit_is_reachable("0" * 40))

    def test_approved_review_requires_reachable_source_and_complete_checklist(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            _, reviewed_sources = self.make_approved_privacy_review(root)
            model_path = root / "docs/privacy/privacy-model.md"
            self.replace_once(model_path, "- [x] `PR-01`", "- [ ] `PR-01`")
            self.replace_once(model_path, "**Approval date:** 2026-08-14", "**Approval date:** 2999-12-31")
            model_text = model_path.read_text(encoding="utf-8")
            disposition = "**Disposition:** Approve — reviewed without conditions."
            first_disposition = model_text.index(disposition)
            second_disposition = model_text.index(disposition, first_disposition + 1)
            model_path.write_text(
                model_text[:second_disposition]
                + "**Disposition:** Condition — condition=repeat review"
                + model_text[second_disposition + len(disposition) :],
                encoding="utf-8",
            )
            model_text = model_path.read_text(encoding="utf-8")
            placeholder_position = model_text.index(disposition)
            placeholder_position = model_text.index(
                disposition, placeholder_position + len(disposition)
            )
            model_path.write_text(
                model_text[:placeholder_position]
                + "**Disposition:** Condition — owner=Pending; gate=TL-0104; condition=ignored"
                + model_text[placeholder_position + len(disposition) :],
                encoding="utf-8",
            )
            self.replace_once(
                model_path,
                "**Approving owner and role:** PikkuJanne — Privacy owner",
                "**Approving owner and role:** PikkuJanne — not a privacy owner",
            )
            self.replace_once(
                model_path,
                "**Approval reference:** TASKS.yaml TL-0005 privacy-owner approval evidence",
                "**Approval reference:** approval explicitly denied by owner",
            )
            lines = model_path.read_text(encoding="utf-8").splitlines()
            for index, line in enumerate(lines):
                if line.startswith("- [x] `PR-04`"):
                    lines[index] = line.replace(
                        disposition,
                        "**Disposition:** Reject — approve telemetry instead " + disposition,
                    )
                elif line.startswith("- [x] `PR-05`"):
                    lines[index] = line.replace(
                        disposition,
                        "**Disposition:** Condition — owner=Privacy engineering; gate=TL-0005; condition=close this task.",
                    )
                elif line.startswith("- [x] `PR-06`"):
                    lines[index] = line.replace(
                        disposition,
                        "**Disposition:** Condition — owner=Privacy engineering; gate=TL-0003; condition=reuse the completed governance task.",
                    )
                elif line.startswith("- [x] `PR-07`"):
                    lines[index] = line.replace(
                        disposition,
                        "**Disposition:** Condition — owner=Privacy engineering; gate=TL-0006; condition=park this condition on an unrelated task.",
                    )
                elif line.startswith("- [x] `PR-08`"):
                    lines[index] = line.replace(
                        disposition,
                        "**Disposition:** Condition — owner=   ; gate=TL-0104; condition=   ",
                    )
            model_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
            task_by_id = dict(TASK_BY_ID)
            privacy_task = dict(task_by_id["TL-0005"])
            privacy_task["status"] = "in_progress"
            privacy_task["evidence"] = []
            task_by_id["TL-0005"] = privacy_task
            validation = self.validate_documents(
                root,
                task_by_id,
                reviewed_sources=reviewed_sources,
                reviewed_commit_reachable=False,
            )
            self.assertTrue(any("must be checked" in error for error in validation.errors))
            self.assertTrue(any("cannot be in the future" in error for error in validation.errors))
            self.assertTrue(any("invalid disposition grammar" in error for error in validation.errors))
            self.assertGreaterEqual(
                sum(
                    "unfinished downstream task gate" in error
                    for error in validation.errors
                ),
                4,
            )
            self.assertTrue(any("exactly one" in error for error in validation.errors))
            self.assertTrue(any("named privacy owner and role" in error for error in validation.errors))
            self.assertTrue(any("durable non-local approval reference" in error for error in validation.errors))
            self.assertTrue(any("existing reachable Git commit" in error for error in validation.errors))
            self.assertTrue(any("in review or done" in error for error in validation.errors))
            self.assertTrue(any("evidence must record" in error for error in validation.errors))

    def test_coherent_conditional_approval_uses_a_real_task_gate(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            reviewed_commit, reviewed_sources = self.make_approved_privacy_review(root)
            model_path = root / "docs/privacy/privacy-model.md"
            self.replace_once(
                model_path,
                "**Privacy-owner approval:** **Approved**",
                "**Privacy-owner approval:** **Approved with conditions**",
            )
            model_text = model_path.read_text(encoding="utf-8")
            disposition = "**Disposition:** Approve — reviewed without conditions."
            model_path.write_text(
                model_text.replace(
                    disposition,
                    "**Disposition:** Condition — owner=Privacy engineering; gate=TL-0104; condition=implement the closed logging envelope before persistence.",
                    1,
                ),
                encoding="utf-8",
            )
            logging_path = root / "docs/privacy/logging-standard.md"
            self.replace_once(
                logging_path,
                "**Review result:** Approved",
                "**Review result:** Approved with conditions",
            )
            fixture = self.read_fixture(root)
            fixture["privacy_owner_approval"] = "ApprovedWithConditions"
            self.write_fixture(root, fixture)
            task_by_id = dict(TASK_BY_ID)
            privacy_task = dict(task_by_id["TL-0005"])
            privacy_task["status"] = "review"
            privacy_task["evidence"] = [
                {
                    "summary": "PikkuJanne — Privacy owner approved with conditions the TL-0005 classifications and default retention guidance.",
                    "result": "passed",
                    "date": "2026-08-14",
                    "environment": "Human privacy-owner review by PikkuJanne",
                    "reference": (
                        f"reviewed commit {reviewed_commit}; "
                        "TASKS.yaml TL-0005 privacy-owner approval evidence"
                    ),
                }
            ]
            task_by_id["TL-0005"] = privacy_task
            validation = self.validate_documents(
                root, task_by_id, reviewed_sources=reviewed_sources
            )
            self.assertEqual(validation.errors, [])

    def test_approval_metadata_fields_cannot_be_duplicated(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            _, reviewed_sources = self.make_approved_privacy_review(root)
            model_path = root / "docs/privacy/privacy-model.md"
            model_path.write_text(
                model_path.read_text(encoding="utf-8")
                + "\n**Privacy-owner approval:** **Pending**\n"
                + "**Privacy-owner Approval:** **Approved**\n"
                + " **Privacy-owner approval:** **Approved**\n"
                + "**Approving owner and role:** Mallory — Privacy owner\n",
                encoding="utf-8",
            )
            logging_path = root / "docs/privacy/logging-standard.md"
            logging_path.write_text(
                logging_path.read_text(encoding="utf-8")
                + "\n**Review result:** Pending\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(
                root, reviewed_sources=reviewed_sources
            )
            self.assertGreaterEqual(
                sum("must occur exactly once" in error for error in validation.errors),
                3,
            )

    def test_approved_review_binds_every_normative_artifact_and_revision(self) -> None:
        mutations = (
            (
                "docs/privacy/privacy-model.md",
                "review 180 days after finalization",
                "review 18000 days after finalization",
            ),
            (
                "docs/privacy/logging-standard.md",
                "4 MiB per local operational-log file",
                "40 MiB per local operational-log file",
            ),
            (
                "docs/privacy/redaction-test-cases.yaml",
                "A normal workshop job succeeds without a recipient name or contact field.",
                "A normal workshop job may require a recipient identity field.",
            ),
            (
                "docs/privacy/privacy-model.md",
                "does not weaken D-011",
                "may weaken D-011",
            ),
            (
                "docs/privacy/logging-standard.md",
                "the complete prohibited-field list",
                "permission to retain recipient identity",
            ),
        )
        for relative, old, new in mutations:
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                _, reviewed_sources = self.make_approved_privacy_review(root)
                path = root / relative
                text = path.read_text(encoding="utf-8")
                self.assertIn(old, text)
                path.write_text(text.replace(old, new, 1), encoding="utf-8")
                validation = self.validate_documents(
                    root, reviewed_sources=reviewed_sources
                )
                self.assertTrue(
                    any("normative content differs" in error for error in validation.errors),
                    validation.errors,
                )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            _, reviewed_sources = self.make_approved_privacy_review(root)
            for relative in PRIVACY_DOCUMENTS:
                path = root / relative
                text = path.read_text(encoding="utf-8")
                if relative.endswith(".yaml"):
                    text = text.replace("model_revision: TL-0005 draft 1", "model_revision: TL-0005 approved 1", 1)
                else:
                    text = text.replace("**Model revision:** TL-0005 draft 1", "**Model revision:** TL-0005 approved 1", 1)
                path.write_text(text, encoding="utf-8")
            validation = self.validate_documents(root, reviewed_sources=reviewed_sources)
            self.assertTrue(any("retain the exact model revision" in error for error in validation.errors))


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


if __name__ == "__main__":
    unittest.main()
