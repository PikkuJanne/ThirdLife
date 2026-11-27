#!/usr/bin/env python3
"""Regression tests for ThirdLife governance validation."""

from __future__ import annotations

import copy
import hashlib
import itertools
import json
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

import merge_task_contracts  # noqa: E402
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
    "docs/privacy/privacy-model.md",
    "docs/privacy/logging-standard.md",
    "docs/privacy/redaction-test-cases.yaml",
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
    ) -> validate_bundle.Validation:
        effective_tasks = TASK_BY_ID if task_by_id is None else task_by_id
        if task_by_id is None and root != REPOSITORY_ROOT:
            effective_tasks = dict(TASK_BY_ID)
            privacy_task = dict(effective_tasks["TL-0005"])
            privacy_task["status"] = "review"
            privacy_task["evidence"] = []
            effective_tasks["TL-0005"] = privacy_task
        validation = validate_bundle.Validation()
        with patch.object(validate_bundle, "ROOT", root):
            validate_bundle.validate_privacy_documents(
                validation,
                effective_tasks,
                DECISION_IDS,
            )
        return validation

    def fixture_path(self, root: Path) -> Path:
        return root / "docs/privacy/redaction-test-cases.yaml"

    def read_fixture(self, root: Path) -> dict[str, object]:
        return validate_bundle.yaml.safe_load(
            self.fixture_path(root).read_text(encoding="utf-8")
        )

    def write_fixture(self, root: Path, fixture: dict[str, object]) -> None:
        self.fixture_path(root).write_text(
            validate_bundle.yaml.safe_dump(
                fixture,
                sort_keys=False,
                allow_unicode=True,
            ),
            encoding="utf-8",
        )

    def replace_once(self, path: Path, old: str, new: str) -> None:
        text = path.read_text(encoding="utf-8")
        self.assertIn(old, text)
        path.write_text(text.replace(old, new, 1), encoding="utf-8")

    def set_markdown_field(
        self, path: Path, field: str, value: str
    ) -> None:
        text = path.read_text(encoding="utf-8")
        text, count = re.subn(
            rf"^\*\*{re.escape(field)}:\*\*.*$",
            f"**{field}:** {value}",
            text,
            count=1,
            flags=re.MULTILINE,
        )
        self.assertEqual(count, 1, field)
        path.write_text(text, encoding="utf-8")

    def make_pending_privacy_review(self, root: Path) -> None:
        fixture = self.read_fixture(root)
        review = fixture["review"]
        self.assertIsInstance(review, dict)
        review.update(
            {
                "privacy_owner": None,
                "owner_role": None,
                "status": "pending",
                "result": None,
                "approved_at_utc": None,
                "approval_reference": None,
                "approval_scope": None,
                "note": (
                    "A named privacy owner must approve the classifications and "
                    "default retention guidance before TL-0005 can be marked done."
                ),
            }
        )
        self.write_fixture(root, fixture)

        model_path = root / "docs/privacy/privacy-model.md"
        self.set_markdown_field(
            model_path, "Status", validate_bundle.PRIVACY_PENDING_STATUS
        )
        self.set_markdown_field(model_path, "Model revision", "TL-0005 review 1")
        self.set_markdown_field(
            model_path,
            "Approval result",
            "Pending — this document does not satisfy the human evidence required by `TL-0005`",
        )
        model_text = model_path.read_text(encoding="utf-8")
        model_text = model_text.replace(
            "The following values are the reviewed safe defaults for later "
            "implementation. They are approved by the named privacy owner for "
            "the exact reviewed commit; changes require review of the "
            "classifications and durations.",
            "The following values are the proposed safe defaults for later "
            "implementation. They are deliberately marked **not approved** until "
            "a named privacy owner reviews the classifications and durations.",
        ).replace("this reviewed default", "this pending default")
        model_path.write_text(model_text, encoding="utf-8")
        pending_fields = {
            "Current privacy-owner": "Pending",
            "Current privacy-owner role": "Pending",
            "Current review date": "Pending",
            "Reviewed commit/reference": "Pending",
            "Approval scope": (
                "Pending — field/context classifications, default retention "
                "guidance, redaction/omission, and support-export allowlist"
            ),
            "Conditions/residual risks": "Pending",
            "Current result": (
                "Pending — automated checks cannot supply this human evidence."
            ),
        }
        for field, value in pending_fields.items():
            self.set_markdown_field(model_path, field, value)

        logging_path = root / "docs/privacy/logging-standard.md"
        self.set_markdown_field(
            logging_path, "Status", validate_bundle.PRIVACY_PENDING_STATUS
        )
        self.set_markdown_field(
            logging_path, "Standard revision", "TL-0005 review 1"
        )
        logging_text = logging_path.read_text(encoding="utf-8")
        logging_text = logging_text.replace(
            "the approved 14-day sanitized-log default from `privacy-model.md` together",
            "the proposed 14-day sanitized-log default from `privacy-model.md` "
            "only after privacy-owner approval, together",
        ).replace(
            "They do not claim a production redactor, logger, retention job, or "
            "support exporter exists; the separate approval record covers only "
            "human contract review.",
            "They do not claim a production redactor, logger, retention job, "
            "support exporter, or human privacy approval exists.",
        ).replace(
            "it is approved for the exact reviewed commit.",
            "it is currently pending.",
        )
        logging_path.write_text(logging_text, encoding="utf-8")

    def apply_approved_privacy_review(self, root: Path) -> None:
        fixture = self.read_fixture(root)
        review = fixture["review"]
        self.assertIsInstance(review, dict)
        review.update(
            {
                "privacy_owner": "Contract Test Reviewer",
                "owner_role": "Privacy owner",
                "status": "approved",
                "result": "approved",
                "approved_at_utc": "2026-08-21T12:00:00Z",
                "approval_reference": None,
                "approval_scope": list(
                    validate_bundle.REDACTION_REQUIRED_APPROVAL_SCOPE
                ),
                "note": "Named privacy owner approved the exact fixture contract.",
            }
        )
        self.write_fixture(root, fixture)

        model_path = root / "docs/privacy/privacy-model.md"
        self.set_markdown_field(
            model_path, "Status", validate_bundle.PRIVACY_APPROVED_STATUS
        )
        self.set_markdown_field(model_path, "Model revision", "TL-0005 approved 1")
        self.set_markdown_field(
            model_path,
            "Approval result",
            "Approved — named privacy-owner review recorded for the exact commit",
        )
        model_text = model_path.read_text(encoding="utf-8")
        model_text = model_text.replace(
            "The following values are the proposed safe defaults for later "
            "implementation. They are deliberately marked **not approved** until "
            "a named privacy owner reviews the classifications and durations.",
            "The following values are the reviewed safe defaults for later "
            "implementation. They are approved by the named privacy owner for "
            "the exact reviewed commit; changes require review of the "
            "classifications and durations.",
        ).replace("this pending default", "this reviewed default")
        model_path.write_text(model_text, encoding="utf-8")
        approved_fields = {
            "Current privacy-owner": "Contract Test Reviewer",
            "Current privacy-owner role": "Privacy owner",
            "Current review date": "2026-08-21",
            "Reviewed commit/reference": "Binding added after reviewed commit",
            "Approval scope": (
                "Approved — classifications, retention, redaction, and "
                "support-export allowlist"
            ),
            "Conditions/residual risks": "None recorded",
            "Current result": "Approved — exact-commit contract review recorded.",
        }
        for field, value in approved_fields.items():
            self.set_markdown_field(model_path, field, value)

        logging_path = root / "docs/privacy/logging-standard.md"
        self.set_markdown_field(
            logging_path, "Status", validate_bundle.PRIVACY_APPROVED_STATUS
        )
        self.set_markdown_field(
            logging_path, "Standard revision", "TL-0005 approved 1"
        )
        logging_text = logging_path.read_text(encoding="utf-8")
        logging_text = logging_text.replace(
            "the proposed 14-day sanitized-log default from `privacy-model.md` "
            "only after privacy-owner approval, together",
            "the approved 14-day sanitized-log default from `privacy-model.md` together",
        ).replace(
            "They do not claim a production redactor, logger, retention job, "
            "support exporter, or human privacy approval exists.",
            "They do not claim a production redactor, logger, retention job, or "
            "support exporter exists; the separate approval record covers only "
            "human contract review.",
        ).replace(
            "it is currently pending.",
            "it is approved for the exact reviewed commit.",
        )
        logging_path.write_text(logging_text, encoding="utf-8")

    def commit_privacy_documents(self, root: Path, message: str) -> str:

        git_commands = (
            ("init", "--quiet"),
            ("config", "user.name", "Contract Test Reviewer"),
            ("config", "user.email", "reviewer@example.test"),
            ("add", "docs/privacy"),
            ("commit", "--quiet", "-m", message),
        )
        for arguments in git_commands:
            result = subprocess.run(
                ["git", "-C", str(root), *arguments],
                check=False,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            )
            self.assertEqual(result.returncode, 0, result.stderr)
        reviewed_commit_result = subprocess.run(
            ["git", "-C", str(root), "rev-parse", "HEAD"],
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        self.assertEqual(reviewed_commit_result.returncode, 0)
        reviewed_commit = reviewed_commit_result.stdout.strip()
        self.assertRegex(reviewed_commit, r"^[0-9a-f]{40}$")
        return reviewed_commit

    def make_approved_privacy_review(self, root: Path) -> str:
        self.make_pending_privacy_review(root)
        self.apply_approved_privacy_review(root)
        reviewed_commit = self.commit_privacy_documents(
            root, "Approved privacy contract fixture"
        )
        self.set_privacy_approval_commit(root, reviewed_commit)
        return reviewed_commit

    def make_preapproval_bound_privacy_review(self, root: Path) -> str:
        self.make_pending_privacy_review(root)
        reviewed_commit = self.commit_privacy_documents(
            root, "Pre-approval privacy contract fixture"
        )
        self.apply_approved_privacy_review(root)
        self.set_privacy_approval_commit(root, reviewed_commit)
        return reviewed_commit

    def set_privacy_approval_commit(self, root: Path, reviewed_commit: str) -> None:
        fixture = self.read_fixture(root)
        fixture["review"]["approval_reference"] = (
            f"reviewed commit {reviewed_commit}"
        )
        self.write_fixture(root, fixture)
        model_path = root / "docs/privacy/privacy-model.md"
        model_text = model_path.read_text(encoding="utf-8")
        model_text, count = re.subn(
            r"^\*\*Reviewed commit/reference:\*\*.*$",
            f"**Reviewed commit/reference:** reviewed commit {reviewed_commit}",
            model_text,
            count=1,
            flags=re.MULTILINE,
        )
        self.assertEqual(count, 1)
        model_path.write_text(model_text, encoding="utf-8")

    def test_current_privacy_documents_satisfy_contract(self) -> None:
        validation = self.validate_documents(REPOSITORY_ROOT)
        self.assertEqual(validation.errors, [])

    def test_required_runtime_and_human_claim_limitations_cannot_be_removed(self) -> None:
        mutations = (
            (
                "docs/privacy/privacy-model.md",
                "It does not claim that those controls are implemented",
                "Those controls are implemented",
            ),
            (
                "docs/privacy/logging-standard.md",
                "It is a design contract, not a claim that a logger or redactor exists",
                "A production logger and redactor exist",
            ),
            (
                "docs/privacy/logging-standard.md",
                "Automated fixture/schema checks in `TL-0005` validate the design artifacts only",
                "Automated checks validate production controls",
            ),
        )
        for relative, old, new in mutations:
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                self.replace_once(root / relative, old, new)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any("missing required contract phrase" in error for error in validation.errors),
                    validation.errors,
                )

    def test_normative_support_and_retention_tables_cannot_drift(self) -> None:
        support_row = (
            "| `schema_version` | Exact reviewed support schema version. |"
        )
        support_mutations = (
            (support_row, "", "support table fields must exactly equal"),
            (
                support_row,
                support_row
                + "\n| `raw_message` | Arbitrary troubleshooting text. |",
                "support table fields must exactly equal",
            ),
            (
                support_row,
                "| `raw_message` | Arbitrary troubleshooting text. |",
                "support table fields must exactly equal",
            ),
            (
                support_row,
                "| `schema_version` | Arbitrary unreviewed troubleshooting payload permitted. |",
                "support table rows and constraints must exactly match",
            ),
        )
        for old, new, expected in support_mutations:
            with self.subTest(new=new), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                self.replace_once(
                    root / "docs/privacy/logging-standard.md",
                    old,
                    new,
                )
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            self.replace_once(
                root / "docs/privacy/privacy-model.md",
                "| Sanitized operational logs | 14 days |",
                "| Sanitized operational logs | Indefinitely |",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any("retention rows and values must exactly match" in error for error in validation.errors),
                validation.errors,
            )

    def test_unknown_schema_version_and_fields_are_rejected(self) -> None:
        mutations = (
            (
                lambda fixture: fixture.__setitem__("schema_version", "unknown.v9"),
                "schema_version must equal",
            ),
            (
                lambda fixture: fixture.__setitem__("undeclared_top_level", True),
                "contains unknown fields",
            ),
            (
                lambda fixture: fixture["cases"][0].__setitem__("debug_value", "unsafe"),
                "contains unknown fields",
            ),
            (
                lambda fixture: fixture["cases"][0]["expected"].__setitem__(
                    "fallback", "raw"
                ),
                "contains unknown fields",
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

    def test_duplicate_yaml_mapping_key_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture_path = self.fixture_path(root)
            self.replace_once(
                fixture_path,
                'schema_version: "thirdlife.redaction-fixtures.v1"',
                (
                    'schema_version: "thirdlife.redaction-fixtures.v1"\n'
                    'schema_version: "thirdlife.redaction-fixtures.v1"'
                ),
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any("duplicate mapping key" in error for error in validation.errors),
                validation.errors,
            )

    def test_yaml_anchors_aliases_merge_and_non_string_keys_are_rejected(self) -> None:
        mutations = (
            (
                lambda text: text.replace(
                    "description: >-",
                    "description: &fixture_description >-",
                    1,
                ),
                "anchors and aliases are prohibited",
            ),
            (
                lambda text: text
                + "\nhostile_cycle: &hostile_cycle\n  self: *hostile_cycle\n",
                "anchors and aliases are prohibited",
            ),
            (
                lambda text: text.replace(
                    "policy:\n",
                    "policy:\n  <<: {hostile: value}\n",
                    1,
                ),
                "YAML merge keys are prohibited",
            ),
            (
                lambda text: text + "\n1: hostile-non-string-key\n",
                "mapping keys must be strings",
            ),
        )
        for mutate, expected in mutations:
            with self.subTest(expected=expected), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture_path = self.fixture_path(root)
                fixture_path.write_text(
                    mutate(fixture_path.read_text(encoding="utf-8")),
                    encoding="utf-8",
                )
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

    def test_yaml_resource_bounds_fail_closed(self) -> None:
        def deep_nesting(text: str) -> str:
            lines = ["\nhostile_depth:"]
            for depth in range(validate_bundle.PRIVACY_FIXTURE_MAX_DEPTH + 2):
                lines.append("  " * (depth + 1) + f"level_{depth}:")
            lines.append(
                "  " * (validate_bundle.PRIVACY_FIXTURE_MAX_DEPTH + 3)
                + "value: end"
            )
            return text + "\n".join(lines) + "\n"

        def node_flood(text: str) -> str:
            count = validate_bundle.PRIVACY_FIXTURE_MAX_NODES + 10
            return text + "\nhostile_nodes:\n" + "  - x\n" * count

        def aggregate_scalars(text: str) -> str:
            scalar = "x" * 15_000
            return text + "\nhostile_aggregate:\n" + "".join(
                f'  - "{scalar}"\n' for _ in range(9)
            )

        text_mutations = (
            (deep_nesting, "YAML nesting exceeds"),
            (node_flood, "YAML exceeds 8192 node limit"),
            (aggregate_scalars, "YAML aggregate scalar content exceeds"),
            (
                lambda text: text
                + "\n#"
                + "x" * validate_bundle.PRIVACY_FIXTURE_MAX_BYTES,
                "YAML exceeds 262144 byte limit",
            ),
        )
        for mutate, expected in text_mutations:
            with self.subTest(expected=expected), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture_path = self.fixture_path(root)
                fixture_path.write_text(
                    mutate(fixture_path.read_text(encoding="utf-8")),
                    encoding="utf-8",
                )
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            fixture["cases"][0]["input"]["value"] = "x" * (
                validate_bundle.PRIVACY_FIXTURE_MAX_SCALAR_CHARS + 1
            )
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("YAML scalar exceeds" in error for error in validation.errors),
                validation.errors,
            )

    def test_malformed_nested_types_report_errors_without_crashing(self) -> None:
        mutations = (
            (
                lambda fixture: fixture["cases"][0]["classification"].__setitem__(
                    "field", []
                ),
                "classification.field has unapproved value",
            ),
            (
                lambda fixture: fixture["cases"][0]["input"].__setitem__(
                    "field", {}
                ),
                "input: field must be a non-empty string",
            ),
            (
                lambda fixture: fixture["cases"][0]["expected"].__setitem__(
                    "action", []
                ),
                "expected.action has unapproved value",
            ),
            (
                lambda fixture: fixture["cases"][0]["support_export"].__setitem__(
                    "outcome", {}
                ),
                "support_export.outcome has unapproved value",
            ),
            (
                lambda fixture: fixture["cases"][0]["input"].__setitem__(
                    "value", b"synthetic-binary"
                ),
                "input.value must be a non-empty string",
            ),
            (
                lambda fixture: fixture["cases"][0]["input"].__setitem__(
                    "value", validate_bundle.date(2026, 8, 21)
                ),
                "input.value must be a non-empty string",
            ),
            (
                lambda fixture: fixture["cases"][0]["input"].__setitem__(
                    "value", 1.25
                ),
                "input.value must be a non-empty string",
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

    def test_extra_cases_are_rejected_before_unbounded_case_processing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            fixture["cases"].append({"hostile": ["x"] * 100})
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("cases must contain exactly 56 entries" in error for error in validation.errors),
                validation.errors,
            )

    def test_case_ids_must_be_unique_contiguous_and_ordered(self) -> None:
        def duplicate(fixture: dict[str, object]) -> None:
            fixture["cases"][1]["id"] = fixture["cases"][0]["id"]

        def reorder(fixture: dict[str, object]) -> None:
            fixture["cases"][0], fixture["cases"][1] = (
                fixture["cases"][1],
                fixture["cases"][0],
            )

        def remove(fixture: dict[str, object]) -> None:
            fixture["cases"].pop()

        for mutate in (duplicate, reorder, remove):
            with self.subTest(mutate=mutate.__name__), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture = self.read_fixture(root)
                mutate(fixture)
                self.write_fixture(root, fixture)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any("case IDs must exactly equal" in error for error in validation.errors),
                    validation.errors,
                )
                if mutate is duplicate:
                    self.assertTrue(
                        any("case IDs must be unique" in error for error in validation.errors)
                    )

    def test_every_input_must_be_wholly_and_explicitly_synthetic(self) -> None:
        def top_level_false(fixture: dict[str, object]) -> None:
            fixture["synthetic_data"] = False

        def case_false(fixture: dict[str, object]) -> None:
            fixture["cases"][0]["input"]["synthetic"] = False

        def live_looking_name(fixture: dict[str, object]) -> None:
            fixture["cases"][0]["input"]["value"] = "Actual Person"

        def misclassified_email(fixture: dict[str, object]) -> None:
            case = fixture["cases"][28]
            case["input"]["value"] = "person@real.example.com"
            case["expected"]["redacted_form"] = "person@real.example.com"
            case["support_export"]["exported_form"] = "person@real.example.com"

        mutations = (
            (top_level_false, "synthetic_data must be true"),
            (case_false, "input.synthetic must be true"),
            (live_looking_name, "not an explicitly synthetic"),
            (misclassified_email, "classified as operational metadata"),
        )
        for mutate, expected in mutations:
            with self.subTest(mutate=mutate.__name__), tempfile.TemporaryDirectory() as temporary_directory:
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

    def test_synthetic_marker_never_exempts_high_risk_secret_shapes(self) -> None:
        secret_values = (
            (
                "SYNTHETIC ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890",
                "GitHub token",
            ),
            (
                "SYNTHETIC -----BEGIN PRIVATE KEY-----",
                "private-key material",
            ),
            (
                "SYNTHETIC 123456-123456-123456-123456-123456-123456-123456-123456",
                "recovery-key shaped value",
            ),
            (
                "SYNTHETIC Bearer ABCDEFGHIJKLMNOPQRSTUV",
                "bearer token",
            ),
            (
                "SYNTHETIC AKIAABCDEFGHIJKLMNOP",
                "cloud access key",
            ),
        )
        for value, label in secret_values:
            with self.subTest(label=label), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture = self.read_fixture(root)
                fixture["cases"][12]["input"]["value"] = value
                self.write_fixture(root, fixture)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(f"contains prohibited {label}" in error for error in validation.errors),
                    validation.errors,
                )

    def test_synthetic_marker_never_exempts_live_identifiers_in_raw_output(self) -> None:
        live_values = (
            "SYNTHETIC person@gmail.com",
            "SYNTHETIC 8.8.8.8",
            "SYNTHETIC hostname=REAL-LAPTOP",
        )
        for value in live_values:
            with self.subTest(value=value), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture = self.read_fixture(root)
                fixture["cases"][19]["input"]["value"] = value
                self.write_fixture(root, fixture)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(
                        "not an explicitly synthetic or reserved test value" in error
                        for error in validation.errors
                    ),
                    validation.errors,
                )

    def test_cases_require_action_redacted_form_and_exported_form(self) -> None:
        mutations = (
            ("expected", "action", "expected.action has unapproved value"),
            ("expected", "redacted_form", "expected.redacted_form must be present"),
            (
                "support_export",
                "exported_form",
                "support_export.exported_form must be present",
            ),
        )
        for section, field, expected in mutations:
            with self.subTest(field=field), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture = self.read_fixture(root)
                del fixture["cases"][0][section][field]
                self.write_fixture(root, fixture)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

    def test_fixture_cannot_self_authorize_unknown_enums(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            fixture = self.read_fixture(root)
            fixture["schema"]["expected_action_values"].append("upload_raw")
            fixture["cases"][0]["expected"]["action"] = "upload_raw"
            fixture["cases"][0]["classification"]["context"] = "remote_service"
            fixture["cases"][0]["support_export"]["outcome"] = "automatic_upload"
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            for expected in (
                "schema.expected_action_values must exactly equal",
                "expected.action has unapproved value",
                "classification.context has unapproved value",
                "support_export.outcome has unapproved value",
            ):
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

    def test_allowlist_is_exact_and_every_field_has_one_preserve_case(self) -> None:
        def broaden(fixture: dict[str, object]) -> None:
            fixture["support_export_allowlist"].append("raw_message")

        def duplicate_coverage(fixture: dict[str, object]) -> None:
            fixture["cases"][25]["input"]["field"] = "manifest_version"

        def non_string_field(fixture: dict[str, object]) -> None:
            fixture["support_export_allowlist"][0] = {"raw": "field"}

        mutations = (
            (broaden, "support_export_allowlist must exactly equal"),
            (duplicate_coverage, "cover each support field exactly once"),
            (non_string_field, "must contain only non-empty strings"),
        )
        for mutate, expected in mutations:
            with self.subTest(mutate=mutate.__name__), tempfile.TemporaryDirectory() as temporary_directory:
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

    def test_preview_and_export_digests_are_valid_equal_synthetic_hashes(self) -> None:
        def invalid_shape(fixture: dict[str, object]) -> None:
            case = fixture["cases"][47]
            case["input"]["value"] = "SYNTHETIC-SHA256-NOT-HEX"
            case["expected"]["redacted_form"] = "SYNTHETIC-SHA256-NOT-HEX"
            case["support_export"]["exported_form"] = "SYNTHETIC-SHA256-NOT-HEX"

        def mismatched_digest(fixture: dict[str, object]) -> None:
            case = fixture["cases"][48]
            case["input"]["value"] = "f" * 64
            case["expected"]["redacted_form"] = "f" * 64
            case["support_export"]["exported_form"] = "f" * 64

        mutations = (
            (invalid_shape, "must be a lowercase 64-hex SHA-256 digest"),
            (mismatched_digest, "preview and export content digests must be equal"),
        )
        for mutate, expected in mutations:
            with self.subTest(mutate=mutate.__name__), tempfile.TemporaryDirectory() as temporary_directory:
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

    def test_sensitive_values_cannot_leak_outside_explicit_input(self) -> None:
        def seeded_leak(fixture: dict[str, object]) -> None:
            fixture["cases"][0]["rationale"] += " SYNTHETIC-NOT-A-REAL-TOKEN"

        def unseeded_email(fixture: dict[str, object]) -> None:
            fixture["cases"][0]["rationale"] += " person@outside.example.com"

        def full_serial_export(fixture: dict[str, object]) -> None:
            case = fixture["cases"][7]
            case["support_export"]["outcome"] = (
                "include_unchanged_if_allowlisted_and_previewed"
            )
            case["support_export"]["exported_form"] = case["input"]["value"]

        mutations = (
            (seeded_leak, "sensitive seed from RDX-014 leaks"),
            (unseeded_email, "prohibited email address appears outside synthetic input"),
            (full_serial_export, "full serial must never have a support exported_form"),
        )
        for mutate, expected in mutations:
            with self.subTest(mutate=mutate.__name__), tempfile.TemporaryDirectory() as temporary_directory:
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

    def test_raw_sibling_unknown_and_telemetry_actions_fail_closed(self) -> None:
        mutations = (
            (19, "redact", "raw untrusted output must use the typed-projection action"),
            (22, "redact", "sibling-private data must be rejected as out of scope"),
            (50, "redact", "unknown fields must be omitted"),
        )
        for index, action, expected in mutations:
            with self.subTest(index=index), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                fixture = self.read_fixture(root)
                case = fixture["cases"][index]
                case["expected"]["action"] = action
                case["expected"]["persistence"] = "redacted_value_only"
                case["expected"]["redacted_form"] = "[REDACTED:test-value]"
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
            telemetry_case = fixture["cases"][55]
            telemetry_case["support_export"]["outcome"] = (
                "include_unchanged_if_allowlisted_and_previewed"
            )
            telemetry_case["support_export"]["exported_form"] = (
                telemetry_case["input"]["value"]
            )
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            self.assertTrue(
                any("suppressed telemetry must have no support-export output surface" in error for error in validation.errors),
                validation.errors,
            )

    def test_pending_review_cannot_support_done_or_approval_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            self.make_pending_privacy_review(root)
            task_by_id = dict(TASK_BY_ID)
            privacy_task = dict(task_by_id["TL-0005"])
            privacy_task["status"] = "done"
            privacy_task["evidence"] = []
            task_by_id["TL-0005"] = privacy_task
            validation = self.validate_documents(root, task_by_id)
            self.assertIn(
                "TL-0005 cannot be done while privacy-owner approval is Pending",
                validation.errors,
            )
            self.assertTrue(
                any("done evidence must record" in error for error in validation.errors)
            )

            privacy_task["status"] = "review"
            privacy_task["evidence"] = [
                {
                    "summary": "Privacy owner approved the classifications and retention.",
                    "result": "passed",
                    "reference": "unbound review",
                }
            ]
            validation = self.validate_documents(root, task_by_id)
            self.assertIn(
                "TL-0005 evidence cannot claim privacy-owner approval while the review is Pending",
                validation.errors,
            )

    def test_incoherent_approved_review_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            self.make_pending_privacy_review(root)
            fixture = self.read_fixture(root)
            fixture["review"]["status"] = "approved"
            self.write_fixture(root, fixture)
            validation = self.validate_documents(root)
            for expected in (
                "non-placeholder named privacy owner",
                "exact 'reviewed commit <40-lowercase-hex>' metadata",
                "Approved status in both Markdown contracts",
            ):
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

    def test_not_approved_never_satisfies_exact_approval_semantics(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            self.make_approved_privacy_review(root)
            model_path = root / "docs/privacy/privacy-model.md"
            logging_path = root / "docs/privacy/logging-standard.md"
            self.replace_once(
                model_path,
                "**Status:** Approved initial privacy contract",
                "**Status:** Not approved",
            )
            self.replace_once(
                logging_path,
                "**Status:** Approved initial privacy contract",
                "**Status:** Not approved",
            )
            self.replace_once(
                model_path,
                "**Approval result:** Approved — named privacy-owner review recorded for the exact commit",
                "**Approval result:** Not approved",
            )
            self.replace_once(
                model_path,
                "**Current result:** Approved — exact-commit contract review recorded.",
                "**Current result:** Not approved",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any("requires Approved status" in error for error in validation.errors),
                validation.errors,
            )

    def test_approved_review_rejects_nonexistent_and_unbound_commits(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            self.make_approved_privacy_review(root)
            self.set_privacy_approval_commit(root, "f" * 40)
            validation = self.validate_documents(root)
            self.assertIn(
                "Privacy approval reference must name an existing reviewed Git commit",
                validation.errors,
            )

        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            self.make_approved_privacy_review(root)
            logging_path = root / "docs/privacy/logging-standard.md"
            current_logging = logging_path.read_text(encoding="utf-8")
            logging_path.write_text(
                current_logging + "\nUnbound reviewed mutation.\n",
                encoding="utf-8",
            )
            for arguments in (
                ("add", "docs/privacy/logging-standard.md"),
                ("commit", "--quiet", "-m", "Unbound privacy mutation"),
            ):
                result = subprocess.run(
                    ["git", "-C", str(root), *arguments],
                    check=False,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    text=True,
                )
                self.assertEqual(result.returncode, 0, result.stderr)
            commit_result = subprocess.run(
                ["git", "-C", str(root), "rev-parse", "HEAD"],
                check=False,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            )
            self.assertEqual(commit_result.returncode, 0, commit_result.stderr)
            unbound_commit = commit_result.stdout.strip()
            logging_path.write_text(current_logging, encoding="utf-8")
            self.set_privacy_approval_commit(root, unbound_commit)
            validation = self.validate_documents(root)
            self.assertIn(
                "Privacy approval commit does not bind current contract docs/privacy/logging-standard.md",
                validation.errors,
            )

    def test_preapproval_commit_binds_only_substantive_contract_bytes(self) -> None:
        mutations = (
            (
                "docs/privacy/privacy-model.md",
                "| Sanitized operational logs | 14 days |",
                "| Sanitized operational logs | 15 days |",
            ),
            (
                "docs/privacy/logging-standard.md",
                "There is no telemetry or background upload path.",
                "A telemetry path may be enabled.",
            ),
            (
                "docs/privacy/redaction-test-cases.yaml",
                None,
                None,
            ),
        )
        for relative, old, new in mutations:
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_privacy_documents(root)
                reviewed_commit = self.make_preapproval_bound_privacy_review(root)
                self.assertRegex(reviewed_commit, r"^[0-9a-f]{40}$")
                validation = self.validate_documents(root)
                self.assertEqual(validation.errors, [])

                if old is None:
                    fixture = self.read_fixture(root)
                    fixture["policy"]["telemetry_default"] = "on"
                    self.write_fixture(root, fixture)
                else:
                    self.replace_once(root / relative, old, new)
                validation = self.validate_documents(root)
                self.assertIn(
                    f"Privacy approval commit does not bind current contract {relative}",
                    validation.errors,
                )

    def test_post_approval_contract_mutation_breaks_commit_binding(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            self.make_approved_privacy_review(root)
            logging_path = root / "docs/privacy/logging-standard.md"
            logging_path.write_text(
                logging_path.read_text(encoding="utf-8")
                + "\nPost-approval contract mutation.\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertIn(
                "Privacy approval commit does not bind current contract docs/privacy/logging-standard.md",
                validation.errors,
            )

    def test_coherent_approved_state_requires_commit_bound_passed_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_privacy_documents(root)
            reviewed_commit = self.make_approved_privacy_review(root)
            validation = self.validate_documents(root)
            self.assertEqual(validation.errors, [])

            task_by_id = dict(TASK_BY_ID)
            privacy_task = dict(task_by_id["TL-0005"])
            privacy_task["status"] = "done"
            privacy_task["evidence"] = [
                {
                    "summary": "Named privacy owner approved the classifications and retention guidance.",
                    "result": "failed",
                    "reference": f"reviewed commit {reviewed_commit}",
                }
            ]
            task_by_id["TL-0005"] = privacy_task
            validation = self.validate_documents(root, task_by_id)
            self.assertTrue(
                any("done evidence must record" in error for error in validation.errors)
            )

            privacy_task["evidence"][0]["result"] = "passed"
            validation = self.validate_documents(root, task_by_id)
            self.assertEqual(validation.errors, [])


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

    def test_product_contract_links_required_specs(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_governance_documents(root)
            path = root / "docs/product-contract.md"
            path.write_text(
                path.read_text(encoding="utf-8").replace("../SECURITY.md", "../missing.md"),
                encoding="utf-8",
            )
            self.assertIn(
                "docs/product-contract.md: missing specification link ../SECURITY.md",
                self.validate_documents(root).errors,
            )

    def test_pilot_and_stable_scope_remain_in_product_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_governance_documents(root)
            path = root / "docs/product-contract.md"
            path.write_text(
                path.read_text(encoding="utf-8").replace("TL-0611", "TL-9999"),
                encoding="utf-8",
            )
            self.assertTrue(any("TL-0611" in error for error in self.validate_documents(root).errors))


class ArchitectureDecisionRecordContractTests(unittest.TestCase):
    def copy_architecture_documents(self, root: Path) -> None:
        relatives = set(validate_bundle.REQUIRED_FILES) | {"ThirdLife.sln"}
        for relative in relatives:
            source = REPOSITORY_ROOT / relative
            if not source.is_file():
                continue
            destination = root / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(source.read_bytes())

    def replace_once(self, path: Path, old: str, new: str) -> None:
        body = path.read_text(encoding="utf-8")
        self.assertEqual(body.count(old), 1, f"expected one occurrence of {old!r}")
        path.write_text(body.replace(old, new, 1), encoding="utf-8")

    def validate_documents(
        self,
        root: Path,
        task_by_id: dict[str, dict[str, object]] | None = None,
    ) -> validate_bundle.Validation:
        validation = validate_bundle.Validation()
        with patch.object(validate_bundle, "ROOT", root):
            validate_bundle.validate_architecture_decision_records(
                validation,
                TASK_BY_ID if task_by_id is None else task_by_id,
                DECISION_IDS,
            )
            validate_bundle.validate_tl0401_adr_reservation(
                validation,
                TASK_BY_ID if task_by_id is None else task_by_id,
            )
        return validation

    def test_current_architecture_decision_records_satisfy_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            validation = self.validate_documents(root)
            self.assertEqual(validation.errors, [])

    def test_missing_required_section_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "docs/adr/0001-windows-wpf-stack.md"
            self.replace_once(path, "## Consequences\n", "## Outcomes\n")
            validation = self.validate_documents(root)
            self.assertIn(
                "docs/adr/0001-windows-wpf-stack.md: missing or empty required "
                "section 'Consequences'",
                validation.errors,
            )

    def test_missing_topic_decision_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "docs/adr/0005-package-adapter.md"
            citation = (
                "- [D-024](../../DECISIONS.md) — Structured WinGet integration\n"
            )
            self.replace_once(path, citation, f"<!--\n{citation}-->\n")
            validation = self.validate_documents(root)
            self.assertTrue(
                any(
                    "Decision IDs section is missing" in error and "'D-024'" in error
                    for error in validation.errors
                ),
                validation.errors,
            )

    def test_unclosed_html_comment_cannot_supply_a_decision_citation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "docs/adr/0005-package-adapter.md"
            citation = (
                "- [D-024](../../DECISIONS.md) — Structured WinGet integration\n"
            )
            self.replace_once(path, citation, f"<!--\n{citation}")
            validation = self.validate_documents(root)
            self.assertTrue(
                any(
                    "Decision IDs section is missing" in error and "'D-024'" in error
                    for error in validation.errors
                ),
                validation.errors,
            )

    def test_indented_code_cannot_supply_a_contract_phrase(self) -> None:
        for indentation in ("    ", " \t"):
            with (
                self.subTest(indentation=repr(indentation)),
                tempfile.TemporaryDirectory() as temporary_directory,
            ):
                root = Path(temporary_directory)
                self.copy_architecture_documents(root)
                path = root / "docs/adr/0005-package-adapter.md"
                phrase = "Profiles select reviewed generic capabilities."
                self.replace_once(path, phrase, f"{indentation}{phrase}")
                validation = self.validate_documents(root)
                self.assertIn(
                    "docs/adr/0005-package-adapter.md: indented Markdown lines are "
                    "not permitted in governed ADRs",
                    validation.errors,
                )

    def test_semantic_contract_phrase_is_required(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "docs/adr/0005-package-adapter.md"
            self.replace_once(
                path,
                "Profiles select reviewed generic capabilities.",
                "Profiles select exact package identities.",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any(
                    "Profiles select reviewed generic capabilities" in error
                    for error in validation.errors
                )
            )

    def test_unknown_decision_and_task_references_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "docs/adr/0002-evidence-policy-separation.md"
            path.write_text(
                path.read_text(encoding="utf-8")
                + "\nUnknown references must fail: D-999 and TL-9999.\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertIn(
                "docs/adr/0002-evidence-policy-separation.md: references unknown decision D-999",
                validation.errors,
            )
            self.assertIn(
                "docs/adr/0002-evidence-policy-separation.md: references unknown task TL-9999",
                validation.errors,
            )

    def test_broken_local_link_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "docs/adr/0004-ephemeral-broker.md"
            self.replace_once(
                path,
                "../../SECURITY.md#7-privileged-broker-requirements",
                "../../MISSING.md#broker",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any(
                    "Markdown link target does not exist" in error
                    for error in validation.errors
                )
            )

    def test_broken_local_fragment_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "docs/adr/0004-ephemeral-broker.md"
            self.replace_once(
                path,
                "../../SECURITY.md#7-privileged-broker-requirements",
                "../../SECURITY.md#missing-broker-heading",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any(
                    "Markdown fragment does not exist" in error
                    for error in validation.errors
                )
            )

    def test_broken_same_document_fragment_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "docs/adr/0001-windows-wpf-stack.md"
            path.write_text(
                path.read_text(encoding="utf-8")
                + "\n[Broken same-document link](#missing-heading)\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any(
                    "Markdown fragment does not exist" in error
                    for error in validation.errors
                )
            )

    def test_unsafe_link_forms_are_rejected(self) -> None:
        mutations = (
            (
                "../../SECURITY.md#7-privileged-broker-requirements",
                "file:///unsafe.md",
                "unsupported scheme",
            ),
            (
                "../../SECURITY.md#7-privileged-broker-requirements",
                "../../../outside.md",
                "leaves the repository",
            ),
        )
        for old, new, expected in mutations:
            with self.subTest(new=new), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_architecture_documents(root)
                path = root / "docs/adr/0004-ephemeral-broker.md"
                self.replace_once(path, old, new)
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

    def test_reference_style_links_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "docs/adr/0004-ephemeral-broker.md"
            path.write_text(
                path.read_text(encoding="utf-8")
                + "\n[broker-reference]: ../../SECURITY.md\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any(
                    "ADR references must use inline Markdown links" in error
                    for error in validation.errors
                )
            )

    def test_missing_readme_navigation_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "README.md"
            self.replace_once(
                path,
                "(docs/adr/0008-minimal-release-interface-envelope.md)",
                "(docs/adr/unlisted.md)",
            )
            path.write_text(
                path.read_text(encoding="utf-8")
                + "\n<!--\n- [hidden](docs/adr/0008-minimal-release-interface-envelope.md) — hidden\n-->\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertIn(
                "README.md: missing navigation link to "
                "docs/adr/0008-minimal-release-interface-envelope.md",
                validation.errors,
            )

    def test_fenced_code_cannot_supply_a_decision_citation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "docs/adr/0005-package-adapter.md"
            citation = (
                "- [D-024](../../DECISIONS.md) — Structured WinGet integration\n"
            )
            self.replace_once(path, citation, f"```text\n{citation}```\n")
            validation = self.validate_documents(root)
            self.assertTrue(
                any(
                    "Decision IDs section is missing ['D-024']" in error
                    for error in validation.errors
                )
            )

    def test_shorter_fence_run_cannot_reveal_hidden_decision_citation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "docs/adr/0005-package-adapter.md"
            citation = (
                "- [D-024](../../DECISIONS.md) — Structured WinGet integration\n"
            )
            self.replace_once(
                path,
                citation,
                f"````text\n```\n{citation}````\n",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any(
                    "Decision IDs section is missing ['D-024']" in error
                    for error in validation.errors
                )
            )

    def test_comment_marker_inside_fence_cannot_hide_following_bad_link(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            path = root / "docs/adr/0005-package-adapter.md"
            path.write_text(
                path.read_text(encoding="utf-8")
                + "\n```text\n<!--\n```\n[Bad link](../../MISSING.md)\n",
                encoding="utf-8",
            )
            validation = self.validate_documents(root)
            self.assertTrue(
                any(
                    "Markdown link target does not exist" in error
                    for error in validation.errors
                ),
                validation.errors,
            )

    def test_unsupported_rendered_link_forms_are_rejected(self) -> None:
        mutations = (
            (
                '<a href="../../MISSING.md">bad</a>',
                "raw HTML is not permitted in governed ADRs",
            ),
            (
                "![bad](../../MISSING.png)",
                "Markdown images are not permitted in governed ADRs",
            ),
        )
        for addition, expected in mutations:
            with self.subTest(addition=addition), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_architecture_documents(root)
                path = root / "docs/adr/0005-package-adapter.md"
                path.write_text(
                    path.read_text(encoding="utf-8") + f"\n{addition}\n",
                    encoding="utf-8",
                )
                validation = self.validate_documents(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

    def test_task_deliverable_drift_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            task_by_id = dict(TASK_BY_ID)
            task_by_id["TL-0009"] = dict(TASK_BY_ID["TL-0009"])
            task_by_id["TL-0009"]["deliverables"] = [
                "docs/adr/0001-windows-wpf-stack.md"
            ]
            validation = self.validate_documents(root, task_by_id)
            self.assertIn(
                "TL-0009: deliverables must exactly match the governed initial ADR set",
                validation.errors,
            )

    def test_tl0401_partial_legacy_adr_contract_is_rejected(self) -> None:
        mutations = (
            (
                "deliverables",
                "docs/adr/0009-winget-backend.md",
                "docs/adr/ADR-004-winget-backend.md",
                "WinGet ADR deliverable must be exactly",
            ),
            (
                "verification",
                "ADR 0009",
                "ADR-004",
                "superseded ADR 0004 marker is not permitted",
            ),
        )
        for field, old, new, expected in mutations:
            with (
                self.subTest(field=field),
                tempfile.TemporaryDirectory() as temporary_directory,
            ):
                root = Path(temporary_directory)
                self.copy_architecture_documents(root)
                task_by_id = dict(TASK_BY_ID)
                task = dict(TASK_BY_ID["TL-0401"])
                values = list(task[field])
                matching_indexes = [
                    index for index, value in enumerate(values) if old in value
                ]
                self.assertEqual(len(matching_indexes), 1)
                index = matching_indexes[0]
                values[index] = values[index].replace(old, new, 1)
                task[field] = values
                task_by_id["TL-0401"] = task
                validation = self.validate_documents(root, task_by_id)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

    def test_duplicate_adr_0009_reservation_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            task_by_id = dict(TASK_BY_ID)
            task = dict(TASK_BY_ID["TL-0702"])
            task["deliverables"] = [
                *task["deliverables"],
                "docs/adr/ADR-009-backup-adapter.md",
            ]
            task_by_id["TL-0702"] = task
            validation = self.validate_documents(root, task_by_id)
            self.assertTrue(
                any(
                    "ADR 0009: reservation must belong only" in error
                    for error in validation.errors
                ),
                validation.errors,
            )

    def test_noncanonical_duplicate_adr_0009_reservations_are_rejected(self) -> None:
        duplicate_paths = (
            "./docs/adr/0009-backup-adapter.md",
            r"docs\adr\0009-backup-adapter.md",
            "Create docs/adr/0009-backup-adapter.md",
            "docs/adr/0009-backup-adapter.md#draft",
        )
        for duplicate_path in duplicate_paths:
            with (
                self.subTest(duplicate_path=duplicate_path),
                tempfile.TemporaryDirectory() as temporary_directory,
            ):
                root = Path(temporary_directory)
                self.copy_architecture_documents(root)
                task_by_id = dict(TASK_BY_ID)
                task = dict(TASK_BY_ID["TL-0702"])
                task["deliverables"] = [
                    *task["deliverables"],
                    duplicate_path,
                ]
                task_by_id["TL-0702"] = task
                validation = self.validate_documents(root, task_by_id)
                self.assertTrue(
                    any(
                        "ADR 0009: reservation must belong only" in error
                        for error in validation.errors
                    ),
                    validation.errors,
                )

    def test_tl0401_rejects_legacy_alias_alongside_current_reservation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            task_by_id = dict(TASK_BY_ID)
            task = dict(TASK_BY_ID["TL-0401"])
            task["notes"] = [
                *task["notes"],
                "Legacy alias: ADR-0004.",
            ]
            task_by_id["TL-0401"] = task
            validation = self.validate_documents(root, task_by_id)
            self.assertTrue(
                any(
                    "superseded ADR 0004 marker is not permitted" in error
                    for error in validation.errors
                ),
                validation.errors,
            )

    def test_tl0401_rejects_obfuscated_legacy_aliases(self) -> None:
        aliases = (
            "ADR_004",
            "ADR–004",
            "ADR\t004",
            "ADR-**004**",
            "ADR-\u200b004",
        )
        for alias in aliases:
            with (
                self.subTest(alias=repr(alias)),
                tempfile.TemporaryDirectory() as temporary_directory,
            ):
                root = Path(temporary_directory)
                self.copy_architecture_documents(root)
                task_by_id = dict(TASK_BY_ID)
                task = dict(TASK_BY_ID["TL-0401"])
                task["notes"] = [
                    *task["notes"],
                    f"Superseded alias: {alias}.",
                ]
                task_by_id["TL-0401"] = task
                validation = self.validate_documents(root, task_by_id)
                self.assertTrue(
                    any(
                        "superseded ADR 0004 marker is not permitted" in error
                        for error in validation.errors
                    ),
                    validation.errors,
                )

    def test_tl0401_accepts_a_draft_or_completed_route_adr_at_reserved_path(self) -> None:
        for status in ("ready", "done"):
            with self.subTest(status=status), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_architecture_documents(root)
                path = root / validate_bundle.TL0401_WINGET_ADR_PATH
                path.write_text("# Package route\n", encoding="utf-8")
                tasks = copy.deepcopy(TASK_BY_ID)
                tasks["TL-0401"]["status"] = status
                self.assertEqual(self.validate_documents(root, tasks).errors, [])

    def test_tl0401_rejects_a_conflicting_adr_path(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            (root / "docs/adr/ADR-009-backup.md").write_text("# Conflicting decision\n", encoding="utf-8")
            errors = self.validate_documents(root).errors
            self.assertTrue(any("ADR 0009: conflicting paths" in error for error in errors), errors)

    def test_tl0401_done_requires_route_adr(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_architecture_documents(root)
            (root / validate_bundle.TL0401_WINGET_ADR_PATH).unlink(missing_ok=True)
            tasks = copy.deepcopy(TASK_BY_ID)
            tasks["TL-0401"]["status"] = "done"
            errors = self.validate_documents(root, tasks).errors
            self.assertTrue(any("completed route decision requires" in error for error in errors), errors)

    def test_adr_paths_are_required_manifest_inputs(self) -> None:
        record_paths = tuple(
            relative
            for relative, _, _ in validate_bundle.ARCHITECTURE_DECISION_RECORDS
        )
        self.assertEqual(record_paths, validate_bundle.ARCHITECTURE_DECISION_PATHS)
        self.assertTrue(
            set(validate_bundle.ARCHITECTURE_DECISION_PATHS)
            <= set(validate_bundle.REQUIRED_FILES)
        )
        self.assertIn(
            validate_bundle.ADR_NUMBERING_AMENDMENT_PATH,
            validate_bundle.REQUIRED_FILES,
        )


class M0SandboxHarnessContractTests(unittest.TestCase):
    script_paths = (
        validate_bundle.M0_SANDBOX_HOST_PATH,
        validate_bundle.M0_SANDBOX_GUEST_PATH,
    )

    def copy_scripts(self, root: Path) -> None:
        for relative in self.script_paths:
            source = REPOSITORY_ROOT / relative
            destination = root / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(source.read_bytes())

    def validate_scripts(self, root: Path) -> validate_bundle.Validation:
        validation = validate_bundle.Validation()
        with patch.object(validate_bundle, "ROOT", root):
            validate_bundle.validate_m0_sandbox_scripts(validation)
        return validation

    def valid_result(self) -> dict[str, object]:
        return {
            "schema_version": 2,
            "task": "TL-0010",
            "run_id": "a" * 32,
            "candidate_commit": validate_bundle.M0_SANDBOX_CANDIDATE,
            "source_branch": "codex/tl-0010-m0-foundation-gate",
            "gate_record_sha256": validate_bundle.M0_SANDBOX_GATE_DIGEST,
            "harness_commit": "b" * 40,
            "launcher_sha256": "e" * 64,
            "runner_sha256": "c" * 64,
            "sandbox_config_sha256": "d" * 64,
            "environment": "Windows Sandbox",
            "reference_profile": "REF-CODEX-001 revision 2026-08-21.1",
            "hosted_constraint_profile": "TL0010-WSB-2026-08-27.1",
            "sandbox_memory_mb": 8192,
            "sandbox_executable_version": "10.0.26100.1",
            "windows_build": "10.0.26100.1",
            "architecture": "AMD64",
            "git_version": "2.55.0.windows.5",
            "dotnet_sdk": "10.0.400",
            "python_version": "3.14.7",
            "pyyaml_version": "6.0.3",
            "smart_app_control_before": "not_detected",
            "smart_app_control_after": "not_detected",
            "code_integrity_policy_fingerprint_before": "f" * 64,
            "code_integrity_policy_fingerprint_after": "f" * 64,
            "code_integrity_observation_method_before": "registry_and_system_policy_files",
            "code_integrity_observation_method_after": "registry_and_system_policy_files",
            "code_integrity_query_before": "succeeded",
            "code_integrity_query_after": "succeeded",
            "guest_policy_state_unchanged": True,
            "security_mutation_attempted": False,
            "networking_enabled": True,
            "protected_client_enabled": True,
            "source_mapping_read_only": True,
            "tool_mappings_read_only": True,
            "only_result_mapping_writable": True,
            "started_utc": "2026-08-27T12:00:00Z",
            "completed_utc": "2026-08-27T12:05:00Z",
            "quick_command": (
                "powershell.exe -NoProfile -ExecutionPolicy Bypass -File "
                ".\\eng\\verify.ps1 -Tier Quick"
            ),
            "quick_result": "passed",
            "quick_exit_code": 0,
            "quick_duration_seconds": 100.0,
            "quick_success_marker": True,
            "full_command": (
                "powershell.exe -NoProfile -ExecutionPolicy Bypass -File "
                ".\\eng\\verify.ps1 -Tier Full"
            ),
            "full_result": "passed",
            "full_exit_code": 0,
            "full_duration_seconds": 180.0,
            "full_success_marker": True,
            "full_last_completed_stage": "tests",
            "tracked_clean_before": True,
            "tracked_clean_after": True,
            "candidate_unchanged_after": True,
            "gate_record_unchanged_after": True,
            "overall_result": "passed",
            "not_run_reason": "none",
            "failure_phase": "none",
            "failure_code": "none",
            "affected_assemblies": [],
            "limitation": (
                "One Windows Sandbox session on the active physical Codex machine; "
                "no cross-hardware certification or host-compatibility claim. Guest "
                "policy-change evidence covers the SAC registry state and readable "
                "system-volume Code Integrity policy files; EFI-resident policy "
                "enumeration is not claimed."
            ),
            "sandbox_closed": True,
            "host_staging_cleanup": "passed",
            "host_result_validated": True,
        }

    def run_result_validator(self, result: dict[str, object]) -> subprocess.CompletedProcess[str]:
        with tempfile.TemporaryDirectory() as temporary_directory:
            path = Path(temporary_directory) / "result.json"
            path.write_text(json.dumps(result, separators=(",", ":")), encoding="utf-8")
            return subprocess.run(
                [
                    "powershell.exe",
                    "-NoLogo",
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    str(REPOSITORY_ROOT / validate_bundle.M0_SANDBOX_HOST_PATH),
                    "-ValidateResultOnly",
                    str(path),
                ],
                check=False,
                capture_output=True,
                text=True,
                timeout=20,
            )

    def test_scripts_are_governed_required_inputs(self) -> None:
        for relative in self.script_paths:
            with self.subTest(relative=relative):
                self.assertIn(relative, validate_bundle.REQUIRED_FILES)

    def test_current_scripts_satisfy_the_fail_closed_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_scripts(root)
            validation = self.validate_scripts(root)
            self.assertEqual(validation.errors, [])

    def test_host_accepts_a_valid_bounded_final_result(self) -> None:
        completed = self.run_result_validator(self.valid_result())
        self.assertEqual(completed.returncode, 0, completed.stderr)
        self.assertIn("PASS: validated bounded TL-0010 result", completed.stdout)

    def test_host_rejects_contradictory_or_unbound_results(self) -> None:
        mutations = (
            ("quick_exit_code", 1),
            ("candidate_commit", "0" * 40),
            ("run_id", "0" * 32),
            ("security_mutation_attempted", True),
            ("code_integrity_query_before", "unavailable"),
            ("code_integrity_observation_method_before", "unavailable"),
            ("code_integrity_policy_fingerprint_after", "0" * 64),
            ("tracked_clean_after", False),
            ("candidate_unchanged_after", False),
            ("full_last_completed_stage", "build"),
            ("sandbox_memory_mb", True),
            ("started_utc", "2026-08-27T12:00:00+00:00"),
        )
        for field, value in mutations:
            with self.subTest(field=field):
                result = self.valid_result()
                result[field] = value
                completed = self.run_result_validator(result)
                self.assertNotEqual(completed.returncode, 0, completed.stdout)

    def test_host_rejects_impossible_or_changed_policy_observations(self) -> None:
        impossible_citool_state = self.valid_result()
        impossible_citool_state.update(
            {
                "code_integrity_observation_method_before": "citool",
                "code_integrity_observation_method_after": "citool",
                "smart_app_control_before": "off",
                "smart_app_control_after": "off",
            }
        )
        completed = self.run_result_validator(impossible_citool_state)
        self.assertNotEqual(completed.returncode, 0, completed.stdout)

        changed_method = self.valid_result()
        changed_method["code_integrity_observation_method_after"] = "citool"
        completed = self.run_result_validator(changed_method)
        self.assertNotEqual(completed.returncode, 0, completed.stdout)

    def test_host_rejects_extra_result_fields(self) -> None:
        result = self.valid_result()
        result["raw_log"] = "not permitted"
        completed = self.run_result_validator(result)
        self.assertNotEqual(completed.returncode, 0, completed.stdout)

    def test_host_accepts_schema_valid_preflight_and_full_failures(self) -> None:
        preflight = self.valid_result()
        preflight.update(
            {
                "git_version": "unavailable",
                "dotnet_sdk": "unavailable",
                "python_version": "unavailable",
                "pyyaml_version": "unavailable",
                "smart_app_control_before": "unavailable",
                "smart_app_control_after": "unavailable",
                "code_integrity_policy_fingerprint_before": "0" * 64,
                "code_integrity_policy_fingerprint_after": "0" * 64,
                "code_integrity_observation_method_before": "unavailable",
                "code_integrity_observation_method_after": "unavailable",
                "code_integrity_query_before": "unavailable",
                "code_integrity_query_after": "unavailable",
                "guest_policy_state_unchanged": False,
                "quick_result": "not_run",
                "quick_exit_code": None,
                "quick_duration_seconds": 0.0,
                "quick_success_marker": False,
                "full_result": "not_run",
                "full_exit_code": None,
                "full_duration_seconds": 0.0,
                "full_success_marker": False,
                "full_last_completed_stage": "not_started",
                "tracked_clean_before": False,
                "tracked_clean_after": False,
                "candidate_unchanged_after": False,
                "gate_record_unchanged_after": False,
                "overall_result": "failed",
                "not_run_reason": "preflight_failed",
                "failure_phase": "preflight",
                "failure_code": "preflight_failed",
            }
        )
        completed = self.run_result_validator(preflight)
        self.assertEqual(completed.returncode, 0, completed.stderr)

        full_failure = self.valid_result()
        full_failure.update(
            {
                "full_result": "failed",
                "full_exit_code": -1073741819,
                "full_success_marker": False,
                "full_last_completed_stage": "build",
                "overall_result": "failed",
                "failure_phase": "full",
                "failure_code": "full_failed",
            }
        )
        completed = self.run_result_validator(full_failure)
        self.assertEqual(completed.returncode, 0, completed.stderr)

        marker_failure = self.valid_result()
        marker_failure.update(
            {
                "quick_result": "failed",
                "quick_exit_code": 0,
                "quick_success_marker": False,
                "full_result": "not_run",
                "full_exit_code": None,
                "full_duration_seconds": 0.0,
                "full_success_marker": False,
                "full_last_completed_stage": "not_started",
                "overall_result": "failed",
                "not_run_reason": "quick_failed",
                "failure_phase": "quick",
                "failure_code": "quick_marker_missing",
            }
        )
        completed = self.run_result_validator(marker_failure)
        self.assertEqual(completed.returncode, 0, completed.stderr)

    def test_host_rejects_blocked_result_without_postflight_integrity(self) -> None:
        blocked = self.valid_result()
        blocked.update(
            {
                "full_result": "blocked",
                "full_exit_code": 1,
                "full_success_marker": False,
                "full_last_completed_stage": "build",
                "overall_result": "blocked",
                "failure_phase": "full",
                "failure_code": "0x800711C7",
                "affected_assemblies": ["ThirdLife.Packages.dll"],
            }
        )
        completed = self.run_result_validator(blocked)
        self.assertEqual(completed.returncode, 0, completed.stderr)

        blocked["guest_policy_state_unchanged"] = False
        completed = self.run_result_validator(blocked)
        self.assertNotEqual(completed.returncode, 0, completed.stdout)

    def test_host_rejects_a_false_postflight_failure_claim(self) -> None:
        contradictory = self.valid_result()
        contradictory.update(
            {
                "overall_result": "failed",
                "failure_phase": "postflight",
                "failure_code": "postflight_failed",
            }
        )
        completed = self.run_result_validator(contradictory)
        self.assertNotEqual(completed.returncode, 0, completed.stdout)

        contradictory["tracked_clean_after"] = False
        completed = self.run_result_validator(contradictory)
        self.assertEqual(completed.returncode, 0, completed.stderr)

    def test_powershell_scripts_parse_without_errors(self) -> None:
        for relative in self.script_paths:
            with self.subTest(relative=relative):
                script_path = str(REPOSITORY_ROOT / relative).replace("'", "''")
                command = (
                    "$tokens=$null; $errors=$null; "
                    "[void][System.Management.Automation.Language.Parser]::ParseFile("
                    f"'{script_path}',[ref]$tokens,[ref]$errors); "
                    "if ($errors.Count -ne 0) { $errors | ForEach-Object { "
                    "[Console]::Error.WriteLine($_.Message) }; exit 1 }"
                )
                completed = subprocess.run(
                    [
                        "powershell.exe",
                        "-NoLogo",
                        "-NoProfile",
                        "-Command",
                        command,
                    ],
                    check=False,
                    capture_output=True,
                    text=True,
                    timeout=15,
                )
                self.assertEqual(completed.returncode, 0, completed.stderr)

    def test_only_the_result_mapping_may_be_writable(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_scripts(root)
            path = root / validate_bundle.M0_SANDBOX_HOST_PATH
            body = path.read_text(encoding="utf-8")
            self.assertIn("<ReadOnly>true</ReadOnly>", body)
            path.write_text(
                body.replace(
                    "<ReadOnly>true</ReadOnly>",
                    "<ReadOnly>false</ReadOnly>",
                    1,
                ),
                encoding="utf-8",
            )
            validation = self.validate_scripts(root)
            self.assertTrue(
                any("six explicit read-only mappings" in error for error in validation.errors),
                validation.errors,
            )

    def test_quick_must_gate_full(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_scripts(root)
            path = root / validate_bundle.M0_SANDBOX_GUEST_PATH
            body = path.read_text(encoding="utf-8")
            guard = 'if ($result.quick_result -eq "passed")'
            self.assertIn(guard, body)
            path.write_text(body.replace(guard, "if ($true)", 1), encoding="utf-8")
            validation = self.validate_scripts(root)
            self.assertTrue(
                any("Quick must run before, and gate, Full" in error for error in validation.errors),
                validation.errors,
            )

    def test_security_or_trust_mutation_is_rejected(self) -> None:
        probes = (
            "Set-ExecutionPolicy Unrestricted",
            "Unblock-File .\\ThirdLife.Core.dll",
            "Set-MpPreference -DisableRealtimeMonitoring $true",
            "bcdedit.exe /set testsigning on",
            "CiTool.exe -rp 00000000-0000-0000-0000-000000000000",
            "dotnet test --filter SafeSubset",
        )
        for probe in probes:
            with self.subTest(probe=probe), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_scripts(root)
                path = root / validate_bundle.M0_SANDBOX_GUEST_PATH
                path.write_text(
                    path.read_text(encoding="utf-8") + f"\n{probe}\n",
                    encoding="utf-8",
                )
                validation = self.validate_scripts(root)
                self.assertTrue(
                    any("prohibited" in error for error in validation.errors),
                    validation.errors,
                )

    def test_candidate_or_gate_digest_drift_is_rejected(self) -> None:
        mutations = (
            (validate_bundle.M0_SANDBOX_CANDIDATE, "0" * 40),
            (validate_bundle.M0_SANDBOX_GATE_DIGEST, "1" * 64),
        )
        for old, new in mutations:
            with self.subTest(old=old), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_scripts(root)
                path = root / validate_bundle.M0_SANDBOX_GUEST_PATH
                body = path.read_text(encoding="utf-8")
                self.assertIn(old, body)
                path.write_text(body.replace(old, new, 1), encoding="utf-8")
                validation = self.validate_scripts(root)
                self.assertTrue(
                    any("missing required contract phrase" in error for error in validation.errors),
                    validation.errors,
                )

    def test_remote_publication_and_citool_types_are_governed(self) -> None:
        mutations = (
            (
                validate_bundle.M0_SANDBOX_HOST_PATH,
                'if ($remoteBranchHash -ne $harnessCommit)',
                'if ($false)',
            ),
            (
                validate_bundle.M0_SANDBOX_HOST_PATH,
                '"WindowsSandboxRemoteSession"',
                '"UnobservedSandboxRemoteSession"',
            ),
            (
                validate_bundle.M0_SANDBOX_HOST_PATH,
                '"WindowsSandboxServer"',
                '"UnobservedSandboxServer"',
            ),
            (
                validate_bundle.M0_SANDBOX_GUEST_PATH,
                '$isEnforcedProperty.Value -isnot [bool]',
                '$false',
            ),
            (
                validate_bundle.M0_SANDBOX_GUEST_PATH,
                '$sacDocument = Get-ItemProperty `\n'
                '                -LiteralPath $sacRegistryPath `\n'
                '                -ErrorAction Stop',
                '$sacDocument = Get-ItemProperty `\n'
                '                -LiteralPath $sacRegistryPath `\n'
                '                -ErrorAction SilentlyContinue',
            ),
            (
                validate_bundle.M0_SANDBOX_GUEST_PATH,
                'Where-Object { $_.Extension -ieq ".p7b" }',
                'Where-Object { $false }',
            ),
            (
                validate_bundle.M0_SANDBOX_GUEST_PATH,
                'if (-not $sandboxIdentityVerified)',
                'if ($false)',
            ),
            (
                validate_bundle.M0_SANDBOX_GUEST_PATH,
                'if ($sandboxMappedInvocation -and $sandboxIdentityVerified)',
                'if ($sandboxMappedInvocation)',
            ),
        )
        for relative, old, new in mutations:
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_scripts(root)
                path = root / relative
                body = path.read_text(encoding="utf-8")
                self.assertIn(old, body)
                path.write_text(body.replace(old, new, 1), encoding="utf-8")
                validation = self.validate_scripts(root)
                self.assertTrue(
                    any("missing required contract phrase" in error for error in validation.errors),
                    validation.errors,
                )


class M0FoundationGateContractTests(unittest.TestCase):
    verification_candidate = "17975419badd4154b82895d9d92a4a904790c7c0"
    verification_candidate_digest = (
        "b4dfbc2fd66bd869ee10a4332ab8089c9f5c3586b378d3a99a095763e18df153"
    )

    def copy_contract(self, root: Path) -> None:
        for relative in (
            validate_bundle.M0_FOUNDATION_GATE_PATH,
            "docs/supply-chain/dependencies.md",
            "docs/supply-chain/license-matrix.csv",
        ):
            source = REPOSITORY_ROOT / relative
            destination = root / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(source.read_bytes())

    def validate_contract(
        self,
        root: Path,
        tasks: dict[str, dict[str, object]] | None = None,
    ) -> validate_bundle.Validation:
        validation = validate_bundle.Validation()
        with patch.object(validate_bundle, "ROOT", root):
            validate_bundle.validate_m0_foundation_gate(
                validation,
                TASK_BY_ID if tasks is None else tasks,
            )
        return validation

    def gate_path(self, root: Path) -> Path:
        return root / validate_bundle.M0_FOUNDATION_GATE_PATH

    def replace_gate_text(self, root: Path, old: str, new: str) -> None:
        path = self.gate_path(root)
        body = path.read_text(encoding="utf-8")
        self.assertIn(old, body)
        path.write_text(body.replace(old, new, 1), encoding="utf-8")

    def test_candidate_accepts_pending_verification_and_acknowledgements(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_contract(root)
            self.replace_gate_text(
                root,
                "**Record status:** Approved — M0 gate complete",
                "**Record status:** Candidate — verification or human evidence pending",
            )
            self.replace_gate_text(root, "**Decision:** Approved", "**Decision:** Pending")
            tasks = dict(TASK_BY_ID)
            gate_task = dict(tasks["TL-0010"])
            gate_task["status"] = "in_progress"
            tasks["TL-0010"] = gate_task
            validation = self.validate_contract(root, tasks)
            self.assertEqual(validation.errors, [])

    def test_gate_artifact_is_governed_required_input(self) -> None:
        self.assertIn(
            validate_bundle.M0_FOUNDATION_GATE_PATH,
            validate_bundle.REQUIRED_FILES,
        )

    def test_current_approved_gate_satisfies_contract(self) -> None:
        validation = validate_bundle.Validation()
        validate_bundle.validate_m0_foundation_gate(validation, TASK_BY_ID)
        self.assertEqual(validation.errors, [])

    def test_task_and_record_lifecycle_must_match(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_contract(root)
            tasks = dict(TASK_BY_ID)
            gate_task = dict(tasks["TL-0010"])
            gate_task["status"] = "review"
            tasks["TL-0010"] = gate_task
            validation = self.validate_contract(root, tasks)
            self.assertTrue(
                any("record state" in error for error in validation.errors),
                validation.errors,
            )
            self.assertTrue(
                any("Decision must be 'Review'" in error for error in validation.errors),
                validation.errors,
            )

    def test_approved_state_requires_an_existing_candidate_commit(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_contract(root)
            self.replace_gate_text(
                root,
                f"| Verification candidate commit | {self.verification_candidate} |",
                f"| Verification candidate commit | {'0' * 40} |",
            )
            self.replace_gate_text(
                root,
                "| Gate-record candidate SHA-256 | "
                f"{self.verification_candidate_digest} |",
                f"| Gate-record candidate SHA-256 | {'1' * 64} |",
            )
            tasks = dict(TASK_BY_ID)
            gate_task = dict(tasks["TL-0010"])
            gate_task["status"] = "done"
            tasks["TL-0010"] = gate_task
            validation = self.validate_contract(root, tasks)
            self.assertTrue(
                any("existing Git commit" in error for error in validation.errors),
                validation.errors,
            )

    def test_approved_digest_must_match_candidate_gate_blob(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_contract(root)
            subprocess.run(
                ["git", "init", "--quiet", str(root)],
                check=True,
                capture_output=True,
            )
            subprocess.run(
                ["git", "-C", str(root), "config", "user.name", "Synthetic Reviewer"],
                check=True,
                capture_output=True,
            )
            subprocess.run(
                [
                    "git",
                    "-C",
                    str(root),
                    "config",
                    "user.email",
                    "synthetic@example.invalid",
                ],
                check=True,
                capture_output=True,
            )
            subprocess.run(
                ["git", "-C", str(root), "add", "."],
                check=True,
                capture_output=True,
            )
            subprocess.run(
                ["git", "-C", str(root), "commit", "--quiet", "-m", "candidate"],
                check=True,
                capture_output=True,
            )
            candidate_commit = subprocess.run(
                ["git", "-C", str(root), "rev-parse", "HEAD"],
                check=True,
                capture_output=True,
                text=True,
            ).stdout.strip()
            self.replace_gate_text(
                root,
                f"| Verification candidate commit | {self.verification_candidate} |",
                f"| Verification candidate commit | {candidate_commit} |",
            )
            self.replace_gate_text(
                root,
                "| Gate-record candidate SHA-256 | "
                f"{self.verification_candidate_digest} |",
                f"| Gate-record candidate SHA-256 | {'0' * 64} |",
            )
            tasks = dict(TASK_BY_ID)
            gate_task = dict(tasks["TL-0010"])
            gate_task["status"] = "done"
            tasks["TL-0010"] = gate_task
            validation = self.validate_contract(root, tasks)
            self.assertTrue(
                any(
                    "gate digest does not match the candidate commit blob" in error
                    for error in validation.errors
                ),
                validation.errors,
            )

    def test_approved_signature_must_name_the_declared_owner(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_contract(root)
            candidate_commit = "0" * 40
            candidate_digest = "1" * 64
            self.replace_gate_text(
                root,
                f"| Verification candidate commit | {self.verification_candidate} |",
                f"| Verification candidate commit | {candidate_commit} |",
            )
            self.replace_gate_text(
                root,
                "| Gate-record candidate SHA-256 | "
                f"{self.verification_candidate_digest} |",
                f"| Gate-record candidate SHA-256 | {candidate_digest} |",
            )
            self.replace_gate_text(
                root,
                "| Project-owner decision | Signed — Janne Vuorela; 2026-08-27; "
                f"candidate {self.verification_candidate}; gate SHA-256 "
                f"{self.verification_candidate_digest} |",
                "| Project-owner decision | Signed — x; "
                f"{candidate_commit}; {candidate_digest} |",
            )
            tasks = dict(TASK_BY_ID)
            gate_task = dict(tasks["TL-0010"])
            gate_task["status"] = "done"
            tasks["TL-0010"] = gate_task
            validation = self.validate_contract(root, tasks)
            self.assertTrue(
                any(
                    "Project-owner decision" in error
                    and "declared owner 'Janne Vuorela'" in error
                    for error in validation.errors
                ),
                validation.errors,
            )

    def test_blocked_state_accepts_a_named_unresolved_gate_blocker(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_contract(root)
            self.replace_gate_text(
                root,
                "**Record status:** Approved — M0 gate complete",
                "**Record status:** Blocked — synthetic environment blocker",
            )
            self.replace_gate_text(root, "**Decision:** Approved", "**Decision:** Blocked")
            self.replace_gate_text(
                root,
                "**Unresolved blockers:** None",
                "**Unresolved blockers:** Synthetic environment blocker prevents the "
                "required verification run.",
            )
            tasks = dict(TASK_BY_ID)
            gate_task = dict(tasks["TL-0010"])
            gate_task["status"] = "blocked"
            tasks["TL-0010"] = gate_task
            validation = self.validate_contract(root, tasks)
            self.assertEqual(validation.errors, [])

    def test_predecessor_count_must_match_live_append_only_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_contract(root)
            self.replace_gate_text(
                root,
                "`TL-0008` | `done`; 4 evidence entries",
                "`TL-0008` | `done`; 3 evidence entries",
            )
            validation = self.validate_contract(root)
            self.assertTrue(
                any("TL-0008 state must equal" in error for error in validation.errors),
                validation.errors,
            )

    def test_exact_later_ci_clarification_preserves_historical_predecessor_count(self) -> None:
        self.assertIn(
            "`TL-0002` | `done`; 3 evidence entries",
            self.gate_path(REPOSITORY_ROOT).read_text(encoding="utf-8"),
        )
        self.assertEqual(self.validate_contract(REPOSITORY_ROOT).errors, [])

    def test_ci_clarification_cannot_hide_changed_predecessor_history(self) -> None:
        for mutation in ("remove_history", "append", "alter_clarification", "duplicate_clarification"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_contract(root)
                tasks = copy.deepcopy(TASK_BY_ID)
                entries = tasks["TL-0002"]["evidence"]
                clarification = next(entry for entry in entries if entry.get("tier") == "governance clarification")
                if mutation == "remove_history":
                    entries.pop(0)
                elif mutation == "append":
                    entries.append({"summary": "Unbound additional evidence."})
                elif mutation == "duplicate_clarification":
                    entries.append(copy.deepcopy(clarification))
                else:
                    clarification["reference"] = "D-068; unrelated.md"
                errors = self.validate_contract(root, tasks).errors
                self.assertTrue(any("TL-0002 state must equal" in error for error in errors), errors)

    def test_unfinished_or_evidence_free_predecessor_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_contract(root)
            tasks = dict(TASK_BY_ID)
            predecessor = dict(tasks["TL-0009"])
            predecessor["status"] = "review"
            predecessor["evidence"] = []
            tasks["TL-0009"] = predecessor
            validation = self.validate_contract(root, tasks)
            self.assertTrue(
                any(
                    "predecessor TL-0009 must be done with evidence" in error
                    for error in validation.errors
                ),
                validation.errors,
            )

    def test_later_matrix_change_does_not_rewrite_historical_gate_binding(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_contract(root)
            for relative in (
                "docs/supply-chain/dependencies.md",
                "docs/supply-chain/license-matrix.csv",
            ):
                historical = subprocess.run(
                    [
                        "git",
                        "-C",
                        str(REPOSITORY_ROOT),
                        "show",
                        f"{self.verification_candidate}:{relative}",
                    ],
                    check=True,
                    capture_output=True,
                ).stdout
                (root / relative).write_bytes(historical)
            subprocess.run(
                ["git", "init", "--quiet", str(root)],
                check=True,
                capture_output=True,
            )
            subprocess.run(
                ["git", "-C", str(root), "config", "user.name", "Synthetic Reviewer"],
                check=True,
                capture_output=True,
            )
            subprocess.run(
                [
                    "git",
                    "-C",
                    str(root),
                    "config",
                    "user.email",
                    "synthetic@example.invalid",
                ],
                check=True,
                capture_output=True,
            )
            subprocess.run(
                ["git", "-C", str(root), "add", "."],
                check=True,
                capture_output=True,
            )
            subprocess.run(
                ["git", "-C", str(root), "commit", "--quiet", "-m", "candidate"],
                check=True,
                capture_output=True,
            )
            candidate_commit = subprocess.run(
                ["git", "-C", str(root), "rev-parse", "HEAD"],
                check=True,
                capture_output=True,
                text=True,
            ).stdout.strip()
            candidate_gate_digest = hashlib.sha256(
                self.gate_path(root).read_bytes()
            ).hexdigest()

            gate_path = self.gate_path(root)
            gate_text = gate_path.read_text(encoding="utf-8")
            gate_text = gate_text.replace(
                self.verification_candidate,
                candidate_commit,
            ).replace(
                self.verification_candidate_digest,
                candidate_gate_digest,
            )
            gate_path.write_text(gate_text, encoding="utf-8")

            matrix_path = root / "docs/supply-chain/license-matrix.csv"
            matrix_path.write_bytes(
                matrix_path.read_bytes() + b"# later governed matrix revision\n"
            )
            dependency_path = root / "docs/supply-chain/dependencies.md"
            dependency_text = dependency_path.read_text(encoding="utf-8")
            dependency_text = dependency_text.replace(
                "7afc6c7599523fb56a66774a29e9107e6a9a0aac",
                "0" * 40,
            ).replace(
                "32ff63e4e6deb703f978efad368ba54cdc898004106fa443e211d046126ee193",
                "1" * 64,
            )
            dependency_path.write_text(dependency_text, encoding="utf-8")

            tasks = json.loads(json.dumps(TASK_BY_ID))
            gate_task = tasks["TL-0010"]
            for entry in gate_task["evidence"]:
                for field, value in entry.items():
                    if isinstance(value, str):
                        entry[field] = value.replace(
                            self.verification_candidate,
                            candidate_commit,
                        ).replace(
                            self.verification_candidate_digest,
                            candidate_gate_digest,
                        )
            validation = self.validate_contract(root, tasks)
            self.assertEqual(validation.errors, [])

    def test_supply_chain_limitations_cannot_be_removed(self) -> None:
        mutations = (
            (
                "remains mutable upstream evidence",
                "has final and complete upstream evidence",
                "missing required contract phrase 'remains mutable upstream evidence'",
            ),
            (
                "Redistribution of the .NET SDK and CPython toolchains remains withheld",
                "Redistribution of the .NET SDK and CPython toolchains is approved",
                "missing required contract phrase 'Redistribution of the .NET SDK and CPython toolchains remains withheld'",
            ),
        )
        for old, new, expected in mutations:
            with self.subTest(new=new), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_contract(root)
                self.replace_gate_text(root, old, new)
                validation = self.validate_contract(root)
                self.assertTrue(
                    any(expected in error for error in validation.errors),
                    validation.errors,
                )

    def test_affirmative_blanket_redistribution_claim_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            self.copy_contract(root)
            path = self.gate_path(root)
            path.write_text(
                path.read_text(encoding="utf-8")
                + "\nBlanket redistribution rights are granted.\n",
                encoding="utf-8",
            )
            validation = self.validate_contract(root)
            self.assertTrue(
                any("affirmative contradiction" in error for error in validation.errors),
                validation.errors,
            )

    def test_comments_and_fences_cannot_hide_gate_claims(self) -> None:
        probes = ("<!-- hidden -->\n", "```text\nhidden\n```\n")
        for probe in probes:
            with self.subTest(probe=probe), tempfile.TemporaryDirectory() as temporary_directory:
                root = Path(temporary_directory)
                self.copy_contract(root)
                path = self.gate_path(root)
                path.write_text(
                    path.read_text(encoding="utf-8") + "\n" + probe,
                    encoding="utf-8",
                )
                validation = self.validate_contract(root)
                self.assertTrue(validation.errors)


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

    def test_product_and_safety_specifications_are_integrity_inputs(self) -> None:
        self.assertLessEqual(
            {
                "docs/product-contract.md", "TASKS.yaml", "TASKS.schema.json",
                "SECURITY.md", "ACCESSIBILITY.md", "LOW_SPEC.md",
                "docs/testing/capability-risk-matrix.md",
                *PRIVACY_DOCUMENTS,
            },
            set(validate_bundle.REQUIRED_FILES),
        )
        self.assertNotIn("BUNDLE_MANIFEST.sha256", validate_bundle.REQUIRED_FILES)


class Windows11PlatformContractTests(unittest.TestCase):
    @staticmethod
    def errors_for(tasks: dict[str, dict[str, object]]) -> list[str]:
        validation = validate_bundle.Validation()
        dependencies = {task_id: task["depends_on"] for task_id, task in tasks.items()}
        validate_bundle.validate_windows_11_task_contract(validation, tasks, dependencies)
        return validation.errors

    def test_current_platform_amendment_and_delivery_contract(self) -> None:
        self.assertIn(validate_bundle.WINDOWS_11_AMENDMENT_PATH, validate_bundle.REQUIRED_FILES)
        self.assertEqual(TASK_DOCUMENT["project"]["platform"], "Windows 11 client on native x64")
        self.assertEqual(self.errors_for(TASK_BY_ID), [])

    def test_broker_must_reconcile_independent_migration_and_historical_contract(self) -> None:
        tasks = copy.deepcopy(TASK_BY_ID)
        tasks["TL-0315"]["depends_on"] = ["TL-0311"]
        tasks["TL-0311"]["depends_on"].remove("TL-0315")
        tasks["TL-0107"]["notes"] = []
        errors = self.errors_for(tasks)
        for task_id in ("TL-0311", "TL-0107"):
            self.assertTrue(any(task_id in error for error in errors), errors)

    def test_future_package_criteria_require_the_supported_platform(self) -> None:
        tasks = copy.deepcopy(TASK_BY_ID)
        for task_id in ("TL-0609", "TL-0610", "TL-0704", "TL-0706", "TL-0707", "TL-0708", "TL-0710"):
            tasks[task_id]["acceptance_criteria"] = [
                item.replace("Windows 11", "Windows")
                for item in tasks[task_id]["acceptance_criteria"]
            ]
        errors = self.errors_for(tasks)
        self.assertEqual(len(errors), 7, errors)


class DevelopmentContractResetTests(unittest.TestCase):
    def setUp(self) -> None:
        self.task_doc = copy.deepcopy(TASK_DOCUMENT)
        self.tasks = {task["id"]: task for task in self.task_doc["tasks"]}
        # Recreate the pending work and permissions independently of live progress.
        pending_ids = {
            "TL-0311", "TL-0312", "TL-0316", "TL-0317",
            "TL-0401", "TL-0402", "TL-0403", "TL-0404",
            "TL-0610", "TL-0611", "TL-0710",
        }
        for task_id in pending_ids:
            task = self.tasks[task_id]
            task["status"] = "backlog"
            for field in validate_bundle.PREREQUISITE_FIELDS:
                for entry in task.get(field, []):
                    entry.pop("evidence", None)
        self.tasks["TL-0401"]["status"] = "ready"
        self.tasks["TL-0311"]["status"] = "blocked"
        while True:
            done = {task_id for task_id, task in self.tasks.items() if task["status"] == "done"}
            inconsistent = [
                task for task in self.tasks.values()
                if task["status"] == "done" and not set(task["depends_on"]) <= done
            ]
            if not inconsistent:
                break
            for task in inconsistent:
                task["status"] = "backlog"

    def validate_contract(self) -> list[str]:
        validation = validate_bundle.Validation()
        dependencies = {task_id: task["depends_on"] for task_id, task in self.tasks.items()}
        validate_bundle.validate_engineering_dependencies(validation, self.tasks, dependencies)
        validate_bundle.validate_prerequisites(validation, self.tasks)
        validate_bundle.validate_exposure_authorization(validation, self.tasks, self.task_doc["project"])
        return validation.errors

    def test_blocked_broker_leaves_package_engineering_selectable(self) -> None:
        self.tasks["TL-0311"]["status"] = "blocked"
        self.assertIn("TL-0401", validate_bundle.select_engineering_tasks(
            self.tasks, outcome="package-installation",
        ))
        self.assertNotIn("TL-0312", validate_bundle.select_engineering_tasks(self.tasks))
        self.assertEqual(self.validate_contract(), [])

    def test_integrated_checkpoint_keeps_qualification_pending(self) -> None:
        broker = self.tasks["TL-0311"]
        broker["status"] = "blocked"
        broker["checkpoints"][0]["state"] = "integrated"
        self.assertEqual(self.validate_contract(), [])
        self.assertTrue(validate_bundle.unmet_prerequisites(self.tasks, broker, "execution_prerequisites"))
        broker["checkpoints"][0]["unqualified_execution"] = "available"
        self.assertTrue(any("isolate unqualified execution" in error for error in self.validate_contract()))

    def test_missing_privilege_evidence_prevents_execution_completion(self) -> None:
        broker = self.tasks["TL-0317"]
        self.tasks["TL-0316"]["status"] = "done"
        self.tasks["TL-0316"]["evidence"] = [{"summary": "Fixture checkpoint verification."}]
        self.assertIn("TL-0317", validate_bundle.select_engineering_tasks(self.tasks))
        self.assertTrue(any(
            entry["kind"] == "authorization"
            for entry in validate_bundle.unmet_prerequisites(self.tasks, broker, "execution_prerequisites")
        ))
        broker["status"] = "done"
        broker["evidence"] = [{"summary": "Fixture record without qualification and consent."}]
        self.assertIn("TL-0317: done requires satisfied execution_prerequisites", self.validate_contract())

    def test_pending_application_qualification_only_blocks_broker_exposure(self) -> None:
        for task_id in (
            "TL-0311", "TL-0312", "TL-0313", "TL-0316", "TL-0317",
            "TL-0401", "TL-0402", "TL-0403", "TL-0404",
        ):
            self.tasks[task_id]["status"] = "done"
            self.tasks[task_id]["evidence"] = [{"summary": "Fixture prerequisite verification."}]
        for entry in self.tasks["TL-0317"]["execution_prerequisites"]:
            if entry["kind"] != "task":
                entry["evidence"] = "Fixture protected broker qualification."

        gate = self.tasks["TL-0314"]
        gate["status"] = "backlog"
        qualification = self.tasks["TL-0318"]
        qualification["status"] = "blocked"
        self.tasks["TL-0405"]["status"] = "backlog"
        for entry in gate["release_prerequisites"]:
            if entry["kind"] == "authorization":
                entry["evidence"] = "Fixture security-owner decision."
        for entry in qualification["execution_prerequisites"]:
            if entry["kind"] == "authorization":
                entry.pop("evidence", None)

        selectable = validate_bundle.select_engineering_tasks(self.tasks)
        self.assertIn("TL-0314", selectable)
        self.assertIn("TL-0405", selectable)
        self.assertNotIn("TL-0318", selectable)
        self.assertEqual(self.validate_contract(), [])

        gate["status"] = "done"
        gate["evidence"] = [{"summary": "Fixture gate preparation."}]
        self.assertIn("TL-0314: done requires satisfied release_prerequisites", self.validate_contract())

        qualification["status"] = "done"
        qualification["evidence"] = [{"summary": "Fixture application preparation."}]
        self.assertIn("TL-0318: done requires satisfied execution_prerequisites", self.validate_contract())

    def test_missing_exposure_consent_keeps_release_unqualified(self) -> None:
        pilot = self.tasks["TL-0611"]
        candidate = self.tasks["TL-0610"]
        candidate["status"] = "done"
        candidate["evidence"] = [{"summary": "Fixture verified candidate."}]
        self.assertIn("TL-0611", validate_bundle.select_engineering_tasks(self.tasks))
        pilot["status"] = "done"
        pilot["evidence"] = [{"summary": "Fixture candidate evidence alone."}]
        self.assertIn("TL-0611: done requires satisfied release_prerequisites", self.validate_contract())
        pilot["release_prerequisites"] = [
            entry for entry in pilot["release_prerequisites"] if entry.get("authority") != "pilot-partner"
        ]
        self.assertIn("TL-0611: missing exposure authorization roles ['pilot-partner']", self.validate_contract())

    def test_engineering_completion_uses_objective_evidence(self) -> None:
        action = self.tasks["TL-0404"]
        for task_id in ("TL-0401", "TL-0402", "TL-0403", "TL-0404"):
            self.tasks[task_id]["status"] = "done"
            self.tasks[task_id]["evidence"] = [{"summary": "Fixture engineering verification."}]
        self.assertNotIn("human_evidence_required", action)
        self.assertIn("human_evidence_required", self.tasks["TL-0113"])
        self.assertTrue(validate_bundle.unmet_prerequisites(self.tasks, action, "execution_prerequisites"))
        self.assertEqual(self.validate_contract(), [])

    def test_actual_dependencies_can_cross_milestones_and_must_be_acyclic(self) -> None:
        self.tasks["TL-0316"]["depends_on"].append("TL-0401")
        self.assertEqual(self.validate_contract(), [])
        self.tasks["TL-0401"]["depends_on"].append("TL-0316")
        self.assertTrue(any("cycle" in error for error in self.validate_contract()))

    def test_active_task_cannot_reintroduce_historical_human_gate(self) -> None:
        self.tasks["TL-0401"]["human_evidence_required"] = ["Maintainer signs the route choice."]
        self.assertIn("TL-0401: legacy human evidence belongs only to completed history", self.validate_contract())


class SingleGuestContinuationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tasks = copy.deepcopy(TASK_BY_ID)

    def test_unavailable_update_offer_does_not_block_independent_engineering(self) -> None:
        self.tasks["TL-0504"]["status"] = "blocked"
        for task_id in ("TL-0502", "TL-0503", "TL-0309", "TL-0112", "TL-0004"):
            self.tasks[task_id]["status"] = "done"
        for task_id in ("TL-0505", "TL-0506"):
            self.tasks[task_id]["status"] = "backlog"
        selectable = validate_bundle.select_engineering_tasks(self.tasks)
        self.assertIn("TL-0505", selectable)
        self.assertIn("TL-0506", selectable)
        self.assertTrue(validate_bundle.unmet_prerequisites(
            self.tasks, self.tasks["TL-0504"], "execution_prerequisites",
        ))

    def test_pending_policy_adoption_allows_candidate_matrix_and_guide(self) -> None:
        for task_id in ("TL-0503", "TL-0505", "TL-0507", "TL-0509", "TL-0603", "TL-0301"):
            self.tasks[task_id]["status"] = "done"
        self.tasks["TL-0508"]["status"] = "backlog"
        for task_id in ("TL-0510", "TL-0605"):
            self.tasks[task_id]["status"] = "backlog"
        selectable = validate_bundle.select_engineering_tasks(self.tasks)
        for task_id in ("TL-0510", "TL-0605"):
            self.assertIn(task_id, selectable)
            self.assertTrue(any(
                item.get("task") == "TL-0508"
                for item in validate_bundle.unmet_prerequisites(
                    self.tasks, self.tasks[task_id], "release_prerequisites",
                )
            ))
        # Engineering completion does not transitively satisfy an exposure decision.
        self.assertTrue(any(
            item.get("task") == "TL-0508"
            for item in validate_bundle.unmet_prerequisites(
                self.tasks, self.tasks["TL-0611"], "release_prerequisites",
            )
        ))

    def test_implemented_update_code_does_not_substitute_for_native_evidence(self) -> None:
        gate = self.tasks["TL-0511"]
        for prerequisite in gate["execution_prerequisites"]:
            if prerequisite["kind"] == "task":
                self.tasks[prerequisite["task"]]["status"] = "done"
            elif "Windows Update" not in prerequisite["scope"]:
                prerequisite["evidence"] = "Controlled fixture for the other prerequisite."
        pending = validate_bundle.unmet_prerequisites(self.tasks, gate, "execution_prerequisites")
        self.assertEqual(len(pending), 1, pending)
        self.assertEqual(pending[0]["kind"], "capability")
        self.assertIn("Windows Update", pending[0]["scope"])
        gate["status"] = "done"
        gate["evidence"] = [{"summary": "Controlled engineering evidence only."}]
        validation = validate_bundle.Validation()
        validate_bundle.validate_prerequisites(validation, self.tasks)
        self.assertIn("TL-0511: done requires satisfied execution_prerequisites", validation.errors)


class StabilizationContractTests(unittest.TestCase):
    def validate_contract(
        self,
        *,
        decisions: str | None = None,
        tasks: dict[str, dict[str, object]] | None = None,
    ) -> validate_bundle.Validation:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "DECISIONS.md").write_text(
                decisions if decisions is not None else
                (REPOSITORY_ROOT / "DECISIONS.md").read_text(encoding="utf-8"),
                encoding="utf-8",
            )
            validation = validate_bundle.Validation()
            with patch.object(validate_bundle, "ROOT", root):
                validate_bundle.validate_stabilization_contract(
                    validation, tasks if tasks is not None else TASK_BY_ID
                )
        return validation

    def test_current_stabilization_contract_is_valid(self) -> None:
        self.assertEqual(self.validate_contract().errors, [])

    def test_each_new_decision_is_required_in_its_own_visible_section(self) -> None:
        decisions = (REPOSITORY_ROOT / "DECISIONS.md").read_text(encoding="utf-8")
        for decision_number in range(68, 73):
            decision_id = f"D-{decision_number:03d}"
            match = re.search(
                rf"(?ms)^### {decision_id} —.*?(?=^### D-|\Z)", decisions
            )
            self.assertIsNotNone(match)
            assert match is not None
            for wrapper in ("<!--\n{}\n-->", "```text\n{}\n```", "{}"):
                with self.subTest(decision_id=decision_id, wrapper=wrapper):
                    replacement = wrapper.format(match.group(0))
                    if wrapper == "{}":
                        replacement = replacement.replace(decision_id, "D-099", 1)
                    mutated = decisions[:match.start()] + replacement + decisions[match.end():]
                    errors = self.validate_contract(decisions=mutated).errors
                    self.assertTrue(any(decision_id in error for error in errors), errors)

    def test_unsafe_decision_amendments_are_rejected(self) -> None:
        decisions = (REPOSITORY_ROOT / "DECISIONS.md").read_text(encoding="utf-8")
        mutations = (
            ("not authoritative runtime", "authoritative runtime", "D-068"),
            ("not rewritten without a separate migration review", "rewritten freely", "D-069"),
            ("New scenarios are independently invokable", "New scenarios cannot be selected independently", "D-070"),
            ("Duplicate keys with incompatible meanings are rejected", "Duplicate keys use the last observed value", "D-071"),
            ("cannot satisfy a required support rule", "can satisfy a required support rule", "D-072"),
        )
        for old, new, decision_id in mutations:
            with self.subTest(decision_id=decision_id):
                self.assertIn(old, decisions)
                errors = self.validate_contract(decisions=decisions.replace(old, new)).errors
                self.assertTrue(any(decision_id in error for error in errors), errors)

    def test_task_clarifications_cannot_be_deleted_duplicated_or_weakened(self) -> None:
        for task_id in ("TL-0107", "TL-0112"):
            for field in ("acceptance_criteria", "verification"):
                clauses = [
                    entry for entry in TASK_BY_ID[task_id][field]
                    if entry in validate_bundle.STABILIZATION_TASK_CONTRACTS[task_id][field]
                ]
                for clause in clauses:
                    for operation in ("remove", "duplicate", "weaken"):
                        with self.subTest(task_id=task_id, field=field, clause=clause, operation=operation):
                            tasks = copy.deepcopy(TASK_BY_ID)
                            entries = tasks[task_id][field]
                            if operation == "duplicate":
                                entries.append(clause)
                            elif operation == "remove":
                                entries.remove(clause)
                            else:
                                entries[entries.index(clause)] = "Optional: " + clause
                            errors = self.validate_contract(tasks=tasks).errors
                            self.assertTrue(any(task_id in error and field in error for error in errors), errors)

    def test_reconciliation_and_lifecycle_decision_references_are_required(self) -> None:
        for task_id in ("TL-0107", "TL-0112"):
            for decision_id in ("D-071", "D-072"):
                with self.subTest(task_id=task_id, decision_id=decision_id):
                    tasks = copy.deepcopy(TASK_BY_ID)
                    tasks[task_id]["decision_refs"].remove(decision_id)
                    errors = self.validate_contract(tasks=tasks).errors
                    self.assertTrue(any(task_id in error for error in errors), errors)

    def test_historical_ci_clarification_cannot_be_removed_or_made_authoritative(self) -> None:
        for replacement in (None, "Historical CI is authoritative runtime evidence."):
            with self.subTest(replacement=replacement):
                tasks = copy.deepcopy(TASK_BY_ID)
                evidence = tasks["TL-0002"]["evidence"]
                entry = next(item for item in evidence if item.get("tier") == "governance clarification")
                if replacement is None:
                    evidence.remove(entry)
                else:
                    entry["summary"] = replacement
                errors = self.validate_contract(tasks=tasks).errors
                self.assertTrue(any("TL-0002" in error for error in errors), errors)

    def test_task_amendment_revises_inputs_and_preserves_execution_records(self) -> None:
        live = {"tasks": [
            {"id": "TL-0401", "status": "done", "depends_on": [], "evidence": [{"summary": "Original review."}]},
            {"id": "TL-0404", "status": "blocked", "blocked_reason": "Awaiting scoped consent",
             "depends_on": ["TL-0401"], "evidence": [{"summary": "Prepared candidate."}],
             "execution_prerequisites": [
                 {"kind": "authorization", "authority": "operator", "scope": "fixture A", "evidence": "first consent"}
             ]},
        ]}
        candidate = {"tasks": [
            {"id": "TL-0401", "status": "backlog", "depends_on": ["TL-0404"], "evidence": []},
            {"id": "TL-0404", "status": "ready", "depends_on": ["TL-0402"],
             "evidence": [{"summary": "Follow-up observation."}],
             "execution_prerequisites": [
                 {"kind": "authorization", "authority": "operator", "scope": "fixture B"}
             ]},
            {"id": "TL-0402", "status": "backlog", "depends_on": [], "evidence": []},
        ]}
        merged, changes = merge_task_contracts.build_merged(candidate, live)
        created, bridge, updated = merged["tasks"]
        self.assertEqual(changes, ["TL-0402", "TL-0404"])
        self.assertEqual(created, live["tasks"][0])
        self.assertEqual(bridge, candidate["tasks"][2])
        self.assertEqual(updated["depends_on"], ["TL-0402"])
        self.assertEqual(updated["status"], "blocked")
        self.assertEqual(updated["blocked_reason"], "Awaiting scoped consent")
        self.assertEqual(updated["evidence"][:2], live["tasks"][1]["evidence"] + candidate["tasks"][1]["evidence"])
        self.assertEqual(updated["evidence"][2]["reference"], "first consent")
        self.assertNotIn("evidence", updated["execution_prerequisites"][0])

    def test_malformed_task_fields_report_errors_without_crashing(self) -> None:
        for field in ("decision_refs", "acceptance_criteria", "verification"):
            for value in (None, 42, {"unexpected": "value"}, [{}]):
                with self.subTest(field=field, value=value):
                    tasks = copy.deepcopy(TASK_BY_ID)
                    tasks["TL-0107"][field] = value
                    self.assertTrue(self.validate_contract(tasks=tasks).errors)

    def test_runner_workflow_and_amendment_are_governed_inputs(self) -> None:
        for relative in (
            "docs/amendments/2026-09-12-repository-stabilization.md",
            "eng/test-manifest.json",
            "eng/verify.ps1",
            ".github/workflows/verify.yml",
            "tools/validate_repository.py",
            "tools/tests/test_validate_repository.py",
        ):
            with self.subTest(relative=relative):
                self.assertIn(relative, validate_bundle.REQUIRED_FILES)


if __name__ == "__main__":
    unittest.main()
