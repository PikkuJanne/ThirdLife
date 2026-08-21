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
    "TESTING.md",
    "DEVELOPMENT_WORKFLOW.md",
    "STATUS.md",
    "LOW_SPEC.md",
    "docs/testing/accessibility-matrix.md",
    "docs/testing/capability-risk-matrix.md",
    "docs/testing/failure-injection.md",
    "docs/testing/manual-hardware-tests.md",
    "docs/testing/reference-machine-profile.md",
    "docs/testing/same-machine-constraints.md",
    "docs/history/TL-0008-draft-1-superseded.md",
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
        with patch.object(validate_bundle, "ROOT", root):
            validate_bundle.validate_testing_documents(
                validation,
                task_by_id or TASK_BY_ID,
                DECISION_IDS,
            )
        return validation

    def replace_once(self, path: Path, old: str, new: str) -> None:
        body = path.read_text(encoding="utf-8")
        self.assertEqual(body.count(old), 1, f"expected one occurrence of {old!r}")
        path.write_text(body.replace(old, new, 1), encoding="utf-8")

    def test_current_testing_documents_satisfy_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            validation = self.validate_documents(root)
            self.assertEqual(validation.errors, [])

    def test_matrix_ids_must_be_complete_unique_and_ordered(self) -> None:
        mutations = (
            ("docs/testing/capability-risk-matrix.md", "| `CRM-006` |", "| `CRM-007` |", "CRM IDs"),
            ("docs/testing/failure-injection.md", "| `FI-006` |", "| `FI-007` |", "FI IDs"),
            ("docs/testing/accessibility-matrix.md", "| `A11Y-006` |", "| `A11Y-007` |", "A11Y IDs"),
        )
        for relative, old, new, expected in mutations:
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_testing_documents(root)
                self.replace_once(root / relative, old, new)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

    def test_same_machine_profiles_must_be_complete_unique_and_ordered(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/same-machine-constraints.md"
            self.replace_once(path, "## 5. `SMC-NO-GPU`", "## 5. `SMC-OFFLINE`")
            validation = self.validate_documents(root)
            self.assertTrue(
                any("SMC profiles" in error for error in validation.errors),
                validation.errors,
            )

    def test_required_same_machine_marker_is_enforced(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/reference-machine-profile.md"
            self.replace_once(
                path,
                "not asset inventory or hardware certification",
                "general-purpose asset record",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any("hardware certification" in error for error in validation.errors),
                validation.errors,
            )

    def test_active_hardware_lab_obligation_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "TESTING.md"
            path.write_text(
                path.read_text(encoding="utf-8")
                + "\nA hardware lab is required before TL-0008 can be completed.\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any("obsolete active hardware-lab obligation" in error for error in validation.errors),
                validation.errors,
            )

    def test_supersession_and_negative_hardware_text_are_allowed(self) -> None:
        allowed = (
            "The former hardware lab is superseded.",
            "A second physical machine is not required.",
            "Search for active requirements for lab machines.",
            "Do not assemble a device pool.",
            "This release has no hardware lab dependency.",
        )
        for line in allowed:
            with self.subTest(line=line):
                self.assertFalse(
                    validate_bundle.has_obsolete_active_hardware_obligation(line)
                )

    def test_exact_superseded_draft_commands_are_rejected_when_active(self) -> None:
        for phrase in validate_bundle.OBSOLETE_ACTIVE_PHRASES:
            with self.subTest(phrase=phrase):
                self.assertTrue(
                    validate_bundle.has_obsolete_active_hardware_obligation(phrase)
                )

    def test_obsolete_device_matrix_file_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            obsolete = root / "docs/testing/device-matrix.md"
            obsolete.write_text("obsolete live inventory\n", encoding="utf-8")
            validation = self.validate_documents(root)
            self.assertTrue(
                any("obsolete active device inventory" in error for error in validation.errors),
                validation.errors,
            )

    def test_sensitive_or_encoded_reference_machine_data_is_rejected(self) -> None:
        examples = (
            "operator@example.invalid",
            "192.0.2.10",
            "02:00:5e:10:00:00",
            "serial number PF3ABC123",
            "Asset tag: DONOR-123456",
            "%43%3A%5CUsers%5CDonor%5Csecret.txt",
            "../../private/secret.txt",
        )
        for example in examples:
            with self.subTest(example=example), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_testing_documents(root)
                path = root / "docs/testing/reference-machine-profile.md"
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
                        or "percent-encoded" in error
                        for error in validation.errors
                    ),
                    validation.errors,
                )

    def test_machine_specific_evidence_path_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/reference-machine-profile.md"
            path.write_text(
                path.read_text(encoding="utf-8")
                + "\nEvidence: C:\\Users\\ExamplePerson\\device.log\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any("machine-specific path" in error for error in validation.errors),
                validation.errors,
            )

    def test_affirmative_cross_hardware_claim_is_rejected(self) -> None:
        claims = (
            "ThirdLife is certified for low-end PCs.",
            "ThirdLife works on all Windows 11 devices.",
            "The product is hardware independent.",
            "VM evidence proves physical compatibility.",
            "The minimum RAM is 4 GB.",
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
                self.assertTrue(
                    any("unsupported hardware claim" in error for error in validation.errors),
                    validation.errors,
                )

    def test_explicit_hardware_claim_limits_are_allowed(self) -> None:
        limits = (
            "Claims that ThirdLife is certified for low-end PCs are not permitted.",
            "The project does not claim it works on all Windows 11 devices.",
            "VM evidence cannot prove physical compatibility.",
            "A minimum RAM value is not permitted without separate evidence.",
        )
        for line in limits:
            with self.subTest(line=line):
                self.assertFalse(validate_bundle.has_prohibited_hardware_claim(line))

    def test_unknown_testing_reference_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_testing_documents(root)
            path = root / "docs/testing/failure-injection.md"
            path.write_text(
                path.read_text(encoding="utf-8") + "\nRelated scenario: FI-999.\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any("unknown testing IDs" in error for error in validation.errors),
                validation.errors,
            )

    def test_unknown_task_and_decision_references_are_rejected(self) -> None:
        additions = (
            ("docs/testing/failure-injection.md", "Follow-up: TL-9999.", "unknown task IDs"),
            ("TESTING.md", "Binding decision: D-999.", "unknown decision IDs"),
        )
        for relative, addition, expected in additions:
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_testing_documents(root)
                path = root / relative
                path.write_text(
                    path.read_text(encoding="utf-8") + f"\n{addition}\n",
                    encoding="utf-8",
                )
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

    def test_tl0008_rejects_human_physical_evidence_requirement(self) -> None:
        tasks = {task_id: dict(task) for task_id, task in TASK_BY_ID.items()}
        task = dict(tasks["TL-0008"])
        task["human_evidence_required"] = [
            "Complete a physical device-pool walkthrough."
        ]
        tasks["TL-0008"] = task
        validation = validate_bundle.Validation()
        validate_bundle.validate_tl0008_contract(validation, tasks)
        self.assertTrue(
            any("must not require physical-device" in error for error in validation.errors),
            validation.errors,
        )

    def test_tl0008_rejects_obsolete_device_matrix_deliverable(self) -> None:
        tasks = {task_id: dict(task) for task_id, task in TASK_BY_ID.items()}
        task = dict(tasks["TL-0008"])
        task["deliverables"] = [
            *task["deliverables"],
            "docs/testing/device-matrix.md",
        ]
        tasks["TL-0008"] = task
        validation = validate_bundle.Validation()
        validate_bundle.validate_tl0008_contract(validation, tasks)
        self.assertTrue(
            any("obsolete docs/testing/device-matrix.md" in error for error in validation.errors),
            validation.errors,
        )

    def test_tl0008_done_accepts_nonphysical_evidence(self) -> None:
        tasks = {task_id: dict(task) for task_id, task in TASK_BY_ID.items()}
        task = dict(tasks["TL-0008"])
        task["status"] = "done"
        task["evidence"] = [
            *task.get("evidence", []),
            {
                "summary": "Same-machine governance and validator quick tier passed.",
                "result": "passed",
                "environment": "Active Codex machine; documentation validation",
                "date": "2026-08-21",
                "reference": "tools/validate_bundle.py",
            },
        ]
        tasks["TL-0008"] = task
        validation = validate_bundle.Validation()
        validate_bundle.validate_tl0008_contract(validation, tasks)
        self.assertEqual(validation.errors, [])

    def test_expected_test_tier_and_trigger_lists_are_required(self) -> None:
        invalid_tasks = (
            {"expected_test_tier": "broad", "full_test_triggers": [], "extended_test_triggers": []},
            {"expected_test_tier": "quick", "full_test_triggers": "none", "extended_test_triggers": []},
            {"expected_test_tier": "quick", "full_test_triggers": [], "extended_test_triggers": None},
        )
        for task in invalid_tasks:
            with self.subTest(task=task):
                validation = validate_bundle.Validation()
                validate_bundle.validate_task_test_tier("TL-TEST", task, validation)
                self.assertTrue(validation.errors)

    def test_extended_tier_requires_named_trigger(self) -> None:
        validation = validate_bundle.Validation()
        validate_bundle.validate_task_test_tier(
            "TL-TEST",
            {
                "kind": "test",
                "expected_test_tier": "extended",
                "full_test_triggers": [],
                "extended_test_triggers": [],
            },
            validation,
        )
        self.assertTrue(
            any("extended tier requires" in error for error in validation.errors),
            validation.errors,
        )

    def test_full_gate_or_release_requires_named_trigger(self) -> None:
        for kind in ("gate", "release"):
            with self.subTest(kind=kind):
                validation = validate_bundle.Validation()
                validate_bundle.validate_task_test_tier(
                    "TL-TEST",
                    {
                        "kind": kind,
                        "expected_test_tier": "full",
                        "full_test_triggers": [],
                        "extended_test_triggers": [],
                    },
                    validation,
                )
                self.assertTrue(
                    any("full gate/release" in error for error in validation.errors),
                    validation.errors,
                )

    def test_v030_required_files_and_authority_are_frozen(self) -> None:
        self.assertIn("DEVELOPMENT_WORKFLOW.md", validate_bundle.REQUIRED_FILES)
        self.assertIn("TESTING.md", validate_bundle.REQUIRED_FILES)
        self.assertIn("STATUS.md", validate_bundle.REQUIRED_FILES)
        self.assertIn(
            "docs/testing/capability-risk-matrix.md",
            validate_bundle.REQUIRED_FILES,
        )
        self.assertNotIn(
            "docs/testing/device-matrix.md",
            validate_bundle.REQUIRED_FILES,
        )
        self.assertNotIn(
            "BUNDLE_MANIFEST.sha256",
            validate_bundle.REQUIRED_FILES,
        )
        self.assertEqual(
            validate_bundle.BUNDLE_MANIFEST_FILE,
            "BUNDLE_MANIFEST.sha256",
        )
        self.assertEqual(
            validate_bundle.AUTHORITY_ORDER[6:11],
            (
                "DEVELOPMENT_WORKFLOW.md",
                "TESTING.md",
                "AGENTS.md",
                "TASKS.yaml",
                "STATUS.md",
            ),
        )

    def test_live_task_metadata_matches_v030_contract(self) -> None:
        self.assertEqual(TASK_DOCUMENT["bundle_version"], "0.3.0")
        self.assertEqual(str(TASK_DOCUMENT["generated_on"]), "2026-08-15")
        self.assertEqual(TASK_DOCUMENT["portfolio"]["roadmap_version"], "2.1")
        self.assertEqual(
            TASK_DOCUMENT["portfolio"]["test_tiers"],
            ["quick", "targeted", "full", "extended"],
        )
        self.assertEqual(len(TASK_DOCUMENT["tasks"]), 91)


if __name__ == "__main__":
    unittest.main()
