#!/usr/bin/env python3
"""Regression tests for ThirdLife governance validation."""

from __future__ import annotations

import re
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
TESTING_DOCUMENTS = (
    "docs/testing/accessibility-matrix.md",
    "docs/testing/device-matrix.md",
    "docs/testing/failure-injection.md",
    "docs/testing/manual-hardware-tests.md",
    "LOW_SPEC.md",
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


class TestingDocumentContractTests(unittest.TestCase):
    REVIEWED_COMMIT = "0123456789abcdef0123456789abcdef01234567"
    REVIEW_DATE = "2026-08-14"
    OWNER_AND_ROLE = "PikkuJanne — Workshop test owner"
    REFERENCE_DEVICE = "LAB-DEVICE-001"

    def copy_testing_documents(self, root: Path) -> None:
        for relative in TESTING_DOCUMENTS:
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
        reviewed_sources = getattr(self, "reviewed_sources", {}).get(root)

        def commit_is_reachable(commit: str) -> bool:
            return reviewed_sources is not None and commit == self.REVIEWED_COMMIT

        def text_at_commit(commit: str, relative: str) -> str | None:
            if commit != self.REVIEWED_COMMIT or reviewed_sources is None:
                return None
            return reviewed_sources.get(relative)

        with patch.object(validate_bundle, "ROOT", root):
            validate_bundle.validate_testing_documents(
                validation,
                task_by_id or TASK_BY_ID,
                DECISION_IDS,
                commit_is_reachable,
                text_at_commit,
            )
        return validation

    def replace_once(self, path: Path, old: str, new: str) -> None:
        text = path.read_text(encoding="utf-8")
        self.assertEqual(text.count(old), 1, f"expected one occurrence of {old!r}")
        path.write_text(text.replace(old, new, 1), encoding="utf-8")

    def refresh_recorded_digests(
        self,
        root: Path,
        task_by_id: dict[str, dict[str, object]],
        *,
        procedure: bool = False,
    ) -> dict[str, dict[str, object]]:
        device_path = root / "docs/testing/device-matrix.md"
        manual_path = root / "docs/testing/manual-hardware-tests.md"
        device_text = device_path.read_text(encoding="utf-8")
        manual_text = manual_path.read_text(encoding="utf-8")
        if procedure:
            procedure_digest = validate_bundle.testing_procedure_digest(
                {
                    relative: (root / relative).read_text(encoding="utf-8")
                    for relative in TESTING_DOCUMENTS[:4]
                },
                (root / "LOW_SPEC.md").read_text(encoding="utf-8"),
            )
            device_text = re.sub(
                r"(?m)^\*\*Procedure digest:\*\* [0-9a-f]{64}$",
                f"**Procedure digest:** {procedure_digest}",
                device_text,
            )
            device_path.write_text(device_text, encoding="utf-8")
        evidence_digest = validate_bundle.testing_evidence_digest(
            device_text,
            manual_text,
        )
        for path in (device_path, manual_path):
            text = path.read_text(encoding="utf-8")
            text = re.sub(
                r"(?m)^\*\*Evidence digest:\*\* [0-9a-f]{64}$",
                f"**Evidence digest:** {evidence_digest}",
                text,
            )
            path.write_text(text, encoding="utf-8")

        updated_tasks = {task_id: dict(task) for task_id, task in task_by_id.items()}
        task = dict(updated_tasks["TL-0008"])
        evidence = [dict(item) for item in task.get("evidence", [])]
        for item in evidence:
            reference = str(item.get("reference", ""))
            if procedure:
                reference = re.sub(
                    r"procedure sha256:[0-9a-f]{64}",
                    f"procedure sha256:{procedure_digest}",
                    reference,
                )
            item["reference"] = re.sub(
                r"evidence sha256:[0-9a-f]{64}",
                f"evidence sha256:{evidence_digest}",
                reference,
            )
        task["evidence"] = evidence
        updated_tasks["TL-0008"] = task
        return updated_tasks

    def make_recorded_testing_evidence(
        self,
        root: Path,
    ) -> dict[str, dict[str, object]]:
        if not hasattr(self, "reviewed_sources"):
            self.reviewed_sources: dict[Path, dict[str, str]] = {}
        self.reviewed_sources[root] = {
            relative: (root / relative).read_text(encoding="utf-8")
            for relative in TESTING_DOCUMENTS
        }

        recorded_status = validate_bundle.TESTING_STATUS_RECORDED
        for relative in TESTING_DOCUMENTS[:4]:
            self.replace_once(
                root / relative,
                "**Status:** Draft procedure; human evidence pending",
                f"**Status:** {recorded_status}",
            )
        self.replace_once(
            root / "LOW_SPEC.md",
            "**Procedure status:** Draft procedure; human evidence pending",
            f"**Procedure status:** {recorded_status}",
        )

        device_path = root / "docs/testing/device-matrix.md"
        device_text = device_path.read_text(encoding="utf-8")
        updated_lines: list[str] = []
        gap_rows: list[str] = []
        covered_requirements = {
            "DMX-001": "Supported reference state",
            "DMX-004": "Laptop",
            "DMX-008": "8 GB",
            "DMX-011": "NVMe",
            "DMX-013": "Present on AC; immediate charging indication observed",
            "DMX-014": "Brief battery operation observed",
            "DMX-017": "TPM present and ready",
            "DMX-020": "Secure Boot enabled",
            "DMX-023": "Wired network observed",
            "DMX-024": "Wi-Fi observed",
            "DMX-031": "Full powered-off cold boot observed",
        }
        coverage_result_rows: list[str] = []
        for line in device_text.splitlines():
            match = re.match(r"^\| `(DMX-\d{3})` \|", line)
            if match is None:
                updated_lines.append(line)
                continue
            requirement_id = match.group(1)
            cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
            if requirement_id in covered_requirements:
                cells[5] = f"{self.REFERENCE_DEVICE} / RUN-001"
                cells[6] = "Covered"
                cells[7] = "None"
                coverage_result_rows.append(
                    f"| {self.REFERENCE_DEVICE} | {requirement_id} | "
                    f"{covered_requirements[requirement_id]} | Physical | Interactive lab | "
                    "None | Direct physical observation | "
                    "docs/testing/manual-hardware-tests.md#human-walkthrough-and-sign-off | "
                    "`Pass` | `Human confirmed` | Point-in-time evidence only |"
                )
            else:
                gap_id = f"GAP-{requirement_id}"
                cells[5] = "None"
                cells[6] = "Missing"
                cells[7] = f"`{gap_id}`"
                gap_rows.append(
                    f"| `{gap_id}` | `{requirement_id}` | Equipment not in current pool | "
                    "Explicit pilot coverage gap | Acquire approved equipment or retain limitation | "
                    f"{self.OWNER_AND_ROLE} | {self.REVIEW_DATE} | Open |"
                )
            updated_lines.append("| " + " | ".join(cells) + " |")
        device_path.write_text("\n".join(updated_lines) + "\n", encoding="utf-8")

        replacements = (
            (
                "**Actual device-pool record:** Pending",
                "**Actual device-pool record:** docs/testing/device-matrix.md#actual-device-pool",
            ),
            (
                "**Physical reference device:** Pending",
                f"**Physical reference device:** {self.REFERENCE_DEVICE}",
            ),
            (
                "**Human recorder and role:** Pending",
                f"**Human recorder and role:** {self.OWNER_AND_ROLE}",
            ),
            ("**Walkthrough result:** Pending", "**Walkthrough result:** Pass"),
            (
                "**Walkthrough date:** Pending",
                f"**Walkthrough date:** {self.REVIEW_DATE}",
            ),
            (
                "**Reviewed source commit:** Pending",
                f"**Reviewed source commit:** {self.REVIEWED_COMMIT}",
            ),
            (
                "| Pending — no pool entry recorded | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Human evidence required |",
                (
                    f"| {self.REFERENCE_DEVICE} | Available | Yes | Laptop | Windows 11 build 26100; Supported | "
                    "x64 | Supported | 8 GB | NVMe | Present on AC; immediate charging indication observed | "
                    "Present and ready | Enabled | Wired and Wi-Fi available | None known | "
                    + ", ".join(covered_requirements)
                    + " | "
                    f"PikkuJanne / {self.REVIEW_DATE} | Point-in-time reference |"
                ),
            ),
            (
                "| Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | `Not run` | `Not available` | Human pool record and test run pending |",
                "\n".join(coverage_result_rows),
            ),
            (
                "| Pending | Actual pool not yet recorded | Human inventory pending | Pilot coverage cannot yet be assessed | Record the sanitized pool and map every `DMX-*` requirement | Pending | Pending | Pending |",
                "\n".join(gap_rows),
            ),
            (
                "The actual device pool, reference-device availability, equipment gaps, and representative-device walkthrough are all **Pending**. This draft supplies no physical-device, cold-boot, workshop, accessibility, or long-term reliability evidence. `TL-0008` must remain short of `done` until the required human records exist and the repository verification and documented human walkthrough have both passed.",
                "The sanitized device pool, reference-device availability, equipment gaps, and representative-device walkthrough are recorded for this procedure revision. This is point-in-time evidence only; it is not release authorization, accessibility conformance, a minimum-spec claim, or proof of long-term reliability.",
            ),
        )
        for old, new in replacements:
            self.replace_once(device_path, old, new)

        manual_path = root / "docs/testing/manual-hardware-tests.md"
        gap_summary = ", ".join(
            re.findall(r"GAP-DMX-\d{3}", "\n".join(gap_rows))
        )
        manual_replacements = (
            ("**Human walkthrough result:** Pending", "**Human walkthrough result:** Pass"),
            (
                "**Walkthrough owner and role:** Pending",
                f"**Walkthrough owner and role:** {self.OWNER_AND_ROLE}",
            ),
            (
                "**Walkthrough date:** Pending",
                f"**Walkthrough date:** {self.REVIEW_DATE}",
            ),
            (
                "**Reviewed source commit:** Pending",
                f"**Reviewed source commit:** {self.REVIEWED_COMMIT}",
            ),
            ("**Reference device ID:** Pending", f"**Reference device ID:** {self.REFERENCE_DEVICE}"),
            (
                "**Walkthrough evidence reference:** Pending",
                "**Walkthrough evidence reference:** docs/testing/manual-hardware-tests.md#human-walkthrough-and-sign-off",
            ),
            ("| Walkthrough result | Pending |", "| Walkthrough result | Pass |"),
            ("| Human recorder and role | Pending |", f"| Human recorder and role | {self.OWNER_AND_ROLE} |"),
            ("| Date and timestamp with offset | Pending |", f"| Date and timestamp with offset | {self.REVIEW_DATE}T12:00:00+02:00 |"),
            ("| Reviewed source commit | Pending |", f"| Reviewed source commit | {self.REVIEWED_COMMIT} |"),
            ("| Physical reference device ID | Pending |", f"| Physical reference device ID | {self.REFERENCE_DEVICE} |"),
            ("| Windows edition/build/architecture | Pending |", "| Windows edition/build/architecture | Windows 11 build 26100 / x64 / supported |"),
            ("| Tests physically executed | Pending |", "| Tests physically executed | MHT-001 through MHT-021 |"),
            ("| Tests reviewed only or not run | Pending |", "| Tests reviewed only or not run | None |"),
            ("| Cold-boot result and evidence class | Pending |", "| Cold-boot result and evidence class | Pass / Human confirmed |"),
            ("| Interruption/resume result and evidence class | Pending |", "| Interruption/resume result and evidence class | Pass / Human confirmed |"),
            ("| Missing equipment and pilot blockers | Pending |", f"| Missing equipment and pilot blockers | {gap_summary} |"),
            ("| Limitations and defects | Pending |", "| Limitations and defects | Point-in-time evidence only |"),
            ("| Sanitized evidence reference/hash | Pending |", "| Sanitized evidence reference/hash | docs/testing/manual-hardware-tests.md#human-walkthrough-and-sign-off |"),
            ("| Cleanup/recovery result | Pending |", "| Cleanup/recovery result | Pass; no unexpected residue |"),
            (
                "The actual representative-device walkthrough is **Pending**. The placeholder below is not evidence and must not be changed to `Pass` without a real human run on the exact referenced procedure and source commit.",
                "The representative-device walkthrough below was completed by the named workshop test owner on the exact referenced procedure and source commit.",
            ),
            (
                "This draft records no actual device pool, physical run, cold boot, workshop observation, accessibility review, or reliability evidence. `TL-0008` must remain short of `done` until a human records the real pool, confirms an available reference device, completes the representative walkthrough, records equipment gaps as explicit pilot blockers, and repository verification passes.",
                "This record proves only the documented point-in-time pool and representative walkthrough. It is not accessibility conformance, release authorization, a minimum-spec claim, or proof of long-term reliability.",
            ),
        )
        for old, new in manual_replacements:
            self.replace_once(manual_path, old, new)

        manual_lines = manual_path.read_text(encoding="utf-8").splitlines()
        in_result_table = False
        for index, line in enumerate(manual_lines):
            if line == (
                "| Test ID | Record/run ID | Test result | Evidence class | "
                "Hardware/context/source | Timestamp with offset | Observation and criterion | "
                "Artifact reference/hash or none | Continuity/checkpoint | Cleanup/recovery | "
                "Defect, blocker, or limitation |"
            ):
                in_result_table = True
                continue
            if in_result_table and line.startswith("| `MHT-"):
                cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
                test_id = cells[0].strip("`")
                cells[1] = f"RUN-001-{test_id}"
                if test_id == "MHT-021":
                    cells[2] = "`Not available`"
                    cells[3] = "`Not available`"
                else:
                    cells[2] = "`Pass`"
                    cells[3] = "`Human confirmed`"
                cells[4] = "Physical / Interactive lab / Direct physical observation"
                cells[5] = f"{self.REVIEW_DATE}T12:00:00+02:00"
                cells[6] = (
                    "No detected or pre-existing partial failure; applicability checked"
                    if test_id == "MHT-021"
                    else "Criterion met in bounded walkthrough"
                )
                cells[7] = "none"
                if test_id == "MHT-019":
                    cells[8] = "Cold-boot checkpoint linked"
                elif test_id == "MHT-020":
                    cells[8] = "Resume checkpoint linked"
                else:
                    cells[8] = "RUN-001 checkpoint linked"
                cells[9] = "Pass; safe final state"
                cells[10] = (
                    "capability_absent; no partial failure to exercise"
                    if test_id == "MHT-021"
                    else "Point-in-time physical walkthrough"
                )
                manual_lines[index] = "| " + " | ".join(cells) + " |"
        manual_path.write_text("\n".join(manual_lines) + "\n", encoding="utf-8")

        procedure_texts = {
            relative: (root / relative).read_text(encoding="utf-8")
            for relative in TESTING_DOCUMENTS[:4]
        }
        procedure_digest = validate_bundle.testing_procedure_digest(
            procedure_texts,
            (root / "LOW_SPEC.md").read_text(encoding="utf-8"),
        )
        self.replace_once(
            device_path,
            "**Procedure digest:** Pending",
            f"**Procedure digest:** {procedure_digest}",
        )
        evidence_digest = validate_bundle.testing_evidence_digest(
            device_path.read_text(encoding="utf-8"),
            manual_path.read_text(encoding="utf-8"),
        )
        self.replace_once(
            device_path,
            "**Evidence digest:** Pending",
            f"**Evidence digest:** {evidence_digest}",
        )
        self.replace_once(
            manual_path,
            "**Evidence digest:** Pending",
            f"**Evidence digest:** {evidence_digest}",
        )

        task_by_id = {task_id: dict(task) for task_id, task in TASK_BY_ID.items()}
        task = dict(task_by_id["TL-0008"])
        task["status"] = "done"
        task["evidence"] = [
            {
                "summary": (
                    f"{self.OWNER_AND_ROLE} confirmed the sanitized actual device pool and "
                    f"completed the representative-device walkthrough with result Pass on {self.REFERENCE_DEVICE}."
                ),
                "result": "passed",
                "environment": f"Physical Windows 11 x64 reference device {self.REFERENCE_DEVICE}",
                "date": self.REVIEW_DATE,
                "reference": (
                    "docs/testing/manual-hardware-tests.md#human-walkthrough-and-sign-off; "
                    f"reviewed commit {self.REVIEWED_COMMIT}; procedure sha256:{procedure_digest}; "
                    f"evidence sha256:{evidence_digest}"
                ),
            }
        ]
        task_by_id["TL-0008"] = task
        return task_by_id

    def test_current_testing_documents_satisfy_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            validation = self.validate_documents(root)
            self.assertEqual(validation.errors, [])

    def test_coherent_recorded_device_evidence_can_validate(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            task_by_id = self.make_recorded_testing_evidence(root)
            validation = self.validate_documents(root, task_by_id)
            self.assertEqual(validation.errors, [])

    def test_missing_or_reordered_matrix_id_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/device-matrix.md"
            path.write_text(
                path.read_text(encoding="utf-8").replace(
                    "`DMX-009`",
                    "`DMX-099`",
                    1,
                ),
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(any("DMX rows must exactly equal" in error for error in validation.errors))

    def test_shared_procedure_revision_is_enforced(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/accessibility-matrix.md"
            path.write_text(
                path.read_text(encoding="utf-8").replace(
                    "**Procedure revision:** TL-0008 draft 1",
                    "**Procedure revision:** TL-0008 draft 2",
                    1,
                ),
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(any("must share procedure revision" in error for error in validation.errors))

    def test_human_confirmation_cannot_become_a_result(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/device-matrix.md"
            path.write_text(
                path.read_text(encoding="utf-8").replace(
                    "`Human confirmed` is an evidence class, not a result",
                    "`Human confirmed` is a passing result",
                    1,
                ),
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(any("not a result" in error for error in validation.errors))

    def test_cold_boot_cannot_be_replaced_by_restart(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/manual-hardware-tests.md"
            path.write_text(
                path.read_text(encoding="utf-8").replace(
                    "Full powered-off cold boot and recheck",
                    "Warm restart and recheck",
                    1,
                ),
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(any("Full powered-off cold boot" in error for error in validation.errors))

    def test_low_spec_test_classes_cannot_become_minimum_claims(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "LOW_SPEC.md"
            path.write_text(
                path.read_text(encoding="utf-8").replace(
                    (
                        "A 4 GB or 8 GB device in the test matrix is a **test class**, "
                        "not an automatic support promise."
                    ),
                    "A 4 GB device is the supported minimum.",
                    1,
                ),
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(any("test class" in error for error in validation.errors))

    def test_missing_matrix_class_requires_gap_blocker(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/device-matrix.md"
            path.write_text(
                path.read_text(encoding="utf-8").replace(
                    "| Pending | Pending | Pending |",
                    "| Pending | Missing | Pending |",
                    1,
                ),
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(any("requires a GAP-DMX blocker" in error for error in validation.errors))

    def test_done_task_cannot_retain_pending_human_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            task_by_id = dict(TASK_BY_ID)
            task = dict(task_by_id["TL-0008"])
            task["status"] = "done"
            task["evidence"] = []
            task_by_id["TL-0008"] = task
            validation = self.validate_documents(root, task_by_id)
            self.assertTrue(any("TL-0008 done requires" in error for error in validation.errors))

    def test_forged_or_negated_done_evidence_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            task_by_id = self.make_recorded_testing_evidence(root)
            device_path = root / "docs/testing/device-matrix.md"
            replacements = (
                (
                    "**Actual device-pool record:** docs/testing/device-matrix.md#actual-device-pool",
                    "**Actual device-pool record:** No pool was recorded",
                ),
                (
                    f"**Human recorder and role:** {self.OWNER_AND_ROLE}",
                    "**Human recorder and role:** Robot — CI",
                ),
                ("**Walkthrough result:** Pass", "**Walkthrough result:** Pass not performed"),
                (
                    f"**Reviewed source commit:** {self.REVIEWED_COMMIT}",
                    "**Reviewed source commit:** 0000000000000000000000000000000000000000",
                ),
            )
            for old, new in replacements:
                self.replace_once(device_path, old, new)
            task = dict(task_by_id["TL-0008"])
            task["evidence"] = [
                {
                    "summary": "Physical walkthrough was not performed.",
                    "result": "passed",
                    "environment": "Nonphysical simulation only",
                    "date": self.REVIEW_DATE,
                    "reference": "fabricated",
                }
            ]
            task_by_id["TL-0008"] = task
            validation = self.validate_documents(root, task_by_id)
            self.assertGreaterEqual(len(validation.errors), 4)

    def test_recorded_manual_signoff_and_per_test_evidence_are_enforced(self) -> None:
        mutations = (
            (
                "| Cold-boot result and evidence class | Pass / Human confirmed |",
                "| Cold-boot result and evidence class | Fail / Not available |",
                "exact passed physical walkthrough",
            ),
            (
                f"| Date and timestamp with offset | {self.REVIEW_DATE}T12:00:00+02:00 |",
                f"| Date and timestamp with offset | {self.REVIEW_DATE}T99:99:99+99:99 |",
                "timestamp with UTC offset",
            ),
            (
                "| `MHT-019` | RUN-001-MHT-019 | `Pass` | `Human confirmed` |",
                "| `MHT-019` | RUN-001-MHT-019 | `Not run` | `Not available` |",
                "invalid or unrun result for MHT-019",
            ),
        )
        for old, new, expected_error in mutations:
            with self.subTest(old=old), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_testing_documents(root)
                task_by_id = self.make_recorded_testing_evidence(root)
                self.replace_once(
                    root / "docs/testing/manual-hardware-tests.md",
                    old,
                    new,
                )
                task_by_id = self.refresh_recorded_digests(root, task_by_id)
                validation = self.validate_documents(root, task_by_id)
                self.assertTrue(
                    any(expected_error in error for error in validation.errors),
                    validation.errors,
                )

    def test_recorded_gap_rows_require_attributable_open_blockers(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            task_by_id = self.make_recorded_testing_evidence(root)
            path = root / "docs/testing/device-matrix.md"
            text = path.read_text(encoding="utf-8")
            text = re.sub(
                r"(?m)^\| `GAP-DMX-002` \|.*$",
                "| `GAP-DMX-002` | `DMX-002` | None | None | None | Robot | not-a-date | Closed |",
                text,
                count=1,
            )
            path.write_text(text, encoding="utf-8")
            task_by_id = self.refresh_recorded_digests(root, task_by_id)
            validation = self.validate_documents(root, task_by_id)
            self.assertTrue(
                any("attributable open pilot blocker" in error for error in validation.errors)
            )

    def test_recorded_tables_require_meaningful_physical_evidence(self) -> None:
        mutations = (
            (
                "docs/testing/manual-hardware-tests.md",
                "| `MHT-002` | RUN-001-MHT-002 | `Pass` | `Human confirmed` | Physical / Interactive lab / Direct physical observation | 2026-08-14T12:00:00+02:00 | Criterion met in bounded walkthrough |",
                "| `MHT-002` | RUN-001-MHT-002 | `Pass` | `Human confirmed` | Physical / Interactive lab / Direct physical observation | 2026-08-14T12:00:00+02:00 | . |",
                "bounded observation",
            ),
            (
                "docs/testing/device-matrix.md",
                "| LAB-DEVICE-001 | DMX-001 | Supported reference state |",
                "| LAB-DEVICE-001 | DMX-001 | . |",
                "meaningful actual state",
            ),
            (
                "docs/testing/device-matrix.md",
                "| `GAP-DMX-002` | `DMX-002` | Equipment not in current pool | Explicit pilot coverage gap | Acquire approved equipment or retain limitation |",
                "| `GAP-DMX-002` | `DMX-002` | . | . | . |",
                "attributable open pilot blocker",
            ),
        )
        for relative, old, new, expected_error in mutations:
            with self.subTest(relative=relative, old=old), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_testing_documents(root)
                task_by_id = self.make_recorded_testing_evidence(root)
                self.replace_once(root / relative, old, new)
                task_by_id = self.refresh_recorded_digests(root, task_by_id)
                validation = self.validate_documents(root, task_by_id)
                self.assertTrue(
                    any(expected_error in error for error in validation.errors),
                    validation.errors,
                )

    def test_recorded_reference_cannot_pass_a_physical_safety_failure(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            task_by_id = self.make_recorded_testing_evidence(root)
            path = root / "docs/testing/manual-hardware-tests.md"
            line_pattern = r"(?m)^\| `MHT-001` \| RUN-001-MHT-001 \|.*$"
            line = re.search(line_pattern, path.read_text(encoding="utf-8"))
            self.assertIsNotNone(line)
            cells = [cell.strip() for cell in line.group(0).strip().strip("|").split("|")]
            cells[2] = "`Fail`"
            cells[3] = "`Human confirmed`"
            cells[6] = "Battery swelling observed; safety criterion failed"
            cells[10] = "DEFECT-001 safety stop"
            text = re.sub(
                line_pattern,
                "| " + " | ".join(cells) + " |",
                path.read_text(encoding="utf-8"),
                count=1,
            )
            path.write_text(text, encoding="utf-8")
            task_by_id = self.refresh_recorded_digests(root, task_by_id)
            validation = self.validate_documents(root, task_by_id)
            self.assertTrue(
                any("MHT-001 requires a physical human-confirmed Pass" in error for error in validation.errors)
            )

    def test_testing_tables_require_real_markdown_separators(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/device-matrix.md"
            text = path.read_text(encoding="utf-8")
            pool_section = validate_bundle.numbered_markdown_section(text, 5)
            separator = next(
                line
                for line in pool_section.splitlines()
                if line.startswith("|---")
            )
            self.replace_once(path, separator, "| this is not a Markdown separator |")
            validation = self.validate_documents(root)
            self.assertTrue(any("actual-pool table" in error for error in validation.errors))

    def test_recorded_reviewed_commit_must_be_reachable(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            task_by_id = self.make_recorded_testing_evidence(root)
            fake_commit = "fedcba9876543210fedcba9876543210fedcba98"
            for relative in (
                "docs/testing/device-matrix.md",
                "docs/testing/manual-hardware-tests.md",
            ):
                path = root / relative
                path.write_text(
                    path.read_text(encoding="utf-8").replace(
                        self.REVIEWED_COMMIT,
                        fake_commit,
                    ),
                    encoding="utf-8",
                )
            task = dict(task_by_id["TL-0008"])
            task["evidence"] = [
                {
                    **dict(task["evidence"][0]),
                    "reference": str(task["evidence"][0]["reference"]).replace(
                        self.REVIEWED_COMMIT,
                        fake_commit,
                    ),
                }
            ]
            task_by_id["TL-0008"] = task
            task_by_id = self.refresh_recorded_digests(root, task_by_id)
            validation = self.validate_documents(root, task_by_id)
            self.assertTrue(any("reachable Git ancestor" in error for error in validation.errors))

    def test_reviewed_normative_content_cannot_drift_after_signoff(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            task_by_id = self.make_recorded_testing_evidence(root)
            path = root / "docs/testing/accessibility-matrix.md"
            path.write_text(
                path.read_text(encoding="utf-8")
                + "\nAccessibility may be disabled during constrained runs.\n",
                encoding="utf-8",
            )
            task_by_id = self.refresh_recorded_digests(
                root,
                task_by_id,
                procedure=True,
            )
            validation = self.validate_documents(root, task_by_id)
            self.assertTrue(
                any("exact reviewed source commit" in error for error in validation.errors),
                validation.errors,
            )

    def test_recorded_task_evidence_reference_must_be_exact_and_affirmative(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            task_by_id = self.make_recorded_testing_evidence(root)
            task = dict(task_by_id["TL-0008"])
            task["evidence"] = [
                {
                    **dict(task["evidence"][0]),
                    "reference": "NOT APPROVED; binding is false; "
                    + str(task["evidence"][0]["reference"]),
                }
            ]
            task_by_id["TL-0008"] = task
            validation = self.validate_documents(root, task_by_id)
            self.assertTrue(any("passed physical" in error for error in validation.errors))

    def test_pass_requires_available_observed_or_human_evidence(self) -> None:
        mutations = (
            (
                "docs/testing/failure-injection.md",
                "| `TL-0105`, `TL-0112` | `Not run` | `Not available` |",
                "| `TL-0105`, `TL-0112` | `Pass` | `Not available` |",
            ),
            (
                "docs/testing/accessibility-matrix.md",
                "| `AXE-001` | `TL-0608` | `Not run` | `Not available` |",
                "| `AXE-001` | `TL-0608` | `Pass` | `Not available` |",
            ),
        )
        for relative, old, new in mutations:
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_testing_documents(root)
                self.replace_once(root / relative, old, new)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any("requires Observed or Human confirmed evidence" in error for error in validation.errors)
                )

    def test_low_spec_template_is_structurally_validated(self) -> None:
        mutations = (
            ("record_kind: template_not_evidence", "record_kind: benchmark_result"),
            ("device_id: Pending\n", ""),
            ("source_revision: Pending", "source_revision: ["),
            ("device_id: Pending", "device_id: LAB-DEVICE-001"),
            ("device_id: Pending", '"device_id": Pending\ndevice_id: Pending'),
            ("device_id: Pending", 'device_id: Pending\n"unexpected": value'),
            ("device_id: Pending", "device_id: &hidden Pending"),
        )
        for old, new in mutations:
            with self.subTest(old=old), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_testing_documents(root)
                self.replace_once(root / "LOW_SPEC.md", old, new)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any("provisional resource template" in error or "template " in error for error in validation.errors)
                )

    def test_unknown_cross_document_reference_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/accessibility-matrix.md"
            path.write_text(
                path.read_text(encoding="utf-8") + "\nRequired follow-up: `A11Y-999`.\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(any("unknown testing IDs" in error for error in validation.errors))

    def test_sensitive_or_encoded_physical_evidence_is_rejected(self) -> None:
        examples = (
            "operator@example.invalid",
            "192.0.2.10",
            "02:00:5e:10:00:00",
            "gh" + "p_" + "ABCDEFGHIJKLMNOPQRSTUVWXYZ123456",
            "%43%3A%5CUsers%5CDonor%5Csecret.txt",
            "hostname=donor-device",
            "2001:db8::1",
            "aabb.ccdd.eeff",
            "serial number PF3ABC123",
            "service tag ABC123",
            "Bearer ABCDEFGHIJKLMNOPQRSTUVWX",
            "xox" + "b-1234567890-ABCDEFGHIJ",
            "../../private/secret.txt",
            "AK" + "IAABCDEFGHIJKLMNOP",
            "Asset tag: DONOR-123456",
            "//fileserver/private-share/evidence.log",
            "%TEMP%\\donor-evidence.log",
        )
        for example in examples:
            with self.subTest(example=example), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_testing_documents(root)
                path = root / "docs/testing/device-matrix.md"
                path.write_text(
                    path.read_text(encoding="utf-8") + f"\nEvidence value: {example}\n",
                    encoding="utf-8",
                )
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(
                        "machine-specific path" in error
                        or "path-traversal" in error
                        or "prohibited" in error
                        for error in validation.errors
                    )
                )

    def test_affirmative_minimum_or_reliability_claim_is_rejected(self) -> None:
        claims = (
            "A 4 GB physical device is the supported minimum.",
            "VM evidence proves physical reliability.",
            "Automation proves long-term reliability.",
            "Human confirmed is a passing result.",
            "Warm restart satisfies cold boot.",
            "The procedure certifies the device.",
            "Accessibility may be disabled during constrained runs.",
        )
        for claim in claims:
            with self.subTest(claim=claim), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_testing_documents(root)
                path = root / "LOW_SPEC.md"
                path.write_text(
                    path.read_text(encoding="utf-8") + f"\n{claim}\n",
                    encoding="utf-8",
                )
                validation = self.validate_documents(root)
                self.assertTrue(any("prohibited support/reliability claim" in error for error in validation.errors))

    def test_unknown_task_reference_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/failure-injection.md"
            path.write_text(
                path.read_text(encoding="utf-8").replace(
                    "`TL-0105`",
                    "`TL-9999`",
                    1,
                ),
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(any("unknown task IDs" in error for error in validation.errors))

    def test_machine_specific_evidence_path_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/manual-hardware-tests.md"
            path.write_text(
                path.read_text(encoding="utf-8")
                + "\nEvidence: C:\\Users\\ExamplePerson\\device.log\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(any("machine-specific path" in error for error in validation.errors))

    def test_duplicate_status_metadata_is_rejected(self) -> None:
        duplicates = (
            "**Status:** Procedure recorded; reference-device evidence complete",
            " **sTaTuS:** Procedure recorded; reference-device evidence complete",
        )
        for duplicate in duplicates:
            with self.subTest(duplicate=duplicate), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_testing_documents(root)
                path = root / "docs/testing/accessibility-matrix.md"
                path.write_text(
                    path.read_text(encoding="utf-8") + f"\n{duplicate}\n",
                    encoding="utf-8",
                )
                validation = self.validate_documents(root)
                self.assertTrue(any("exactly one Status" in error for error in validation.errors))

    def test_governed_metadata_cannot_be_hidden_in_a_code_fence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/device-matrix.md"
            self.replace_once(
                path,
                "**Status:** Draft procedure; human evidence pending",
                "```markdown\n**Status:** Draft procedure; human evidence pending\n```",
            )
            validation = self.validate_documents(root)
            self.assertTrue(any("fenced code blocks" in error for error in validation.errors))


if __name__ == "__main__":
    unittest.main()
