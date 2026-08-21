#!/usr/bin/env python3
"""Focused regressions for the TL-0007 synthetic pilot fixture contract."""

from __future__ import annotations

import copy
import shutil
import sys
import tempfile
import unittest
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator


TOOLS_ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = TOOLS_ROOT.parent
sys.path.insert(0, str(TOOLS_ROOT))

import validate_bundle  # noqa: E402


TASK_DOCUMENT = validate_bundle.yaml.safe_load(
    (REPOSITORY_ROOT / "TASKS.yaml").read_text(encoding="utf-8")
)
TASK_BY_ID = {task["id"]: task for task in TASK_DOCUMENT["tasks"]}


class PilotFixtureContractTests(unittest.TestCase):
    @contextmanager
    def fixture_copy(self) -> Iterator[Path]:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            shutil.copytree(REPOSITORY_ROOT / "fixtures", root / "fixtures")
            yield root

    def read_yaml(self, root: Path, relative: str) -> dict[str, object]:
        return validate_bundle.yaml.safe_load(
            (root / relative).read_text(encoding="utf-8")
        )

    def write_yaml(
        self, root: Path, relative: str, document: dict[str, object]
    ) -> None:
        (root / relative).write_text(
            validate_bundle.yaml.safe_dump(
                document,
                sort_keys=False,
                allow_unicode=True,
            ),
            encoding="utf-8",
            newline="\n",
        )

    def validate(
        self,
        root: Path,
        task_by_id: dict[str, dict[str, object]] | None = None,
    ) -> tuple[validate_bundle.Validation, str]:
        validation = validate_bundle.Validation()
        digest = validate_bundle.validate_pilot_fixtures(
            validation,
            TASK_BY_ID if task_by_id is None else task_by_id,
            root,
        )
        return validation, digest

    def assert_rejected(
        self,
        root: Path,
        expected: str,
        task_by_id: dict[str, dict[str, object]] | None = None,
    ) -> list[str]:
        validation, _digest = self.validate(root, task_by_id)
        self.assertTrue(
            any(expected.casefold() in error.casefold() for error in validation.errors),
            validation.errors,
        )
        return validation.errors

    def test_current_fixture_set_is_valid_and_digest_is_deterministic(self) -> None:
        validation = validate_bundle.Validation()
        first = validate_bundle.validate_pilot_fixtures(
            validation, TASK_BY_ID, REPOSITORY_ROOT
        )
        second = validate_bundle.pilot_fixture_set_digest(REPOSITORY_ROOT)
        self.assertEqual(validation.errors, [])
        self.assertEqual(first, second)
        self.assertRegex(first, r"^[0-9a-f]{64}$")
        self.assertEqual(
            first,
            validate_bundle.pilot_fixture_set_digest(REPOSITORY_ROOT),
        )

    def test_required_inventory_is_exact_and_governed(self) -> None:
        self.assertTrue(
            set(validate_bundle.PILOT_FIXTURE_FILES).issubset(
                set(validate_bundle.REQUIRED_FILES)
            )
        )
        self.assertIn(validate_bundle.PILOT_FIXTURE_README, validate_bundle.REQUIRED_FILES)
        self.assertIn("tools/tests/test_pilot_fixtures.py", validate_bundle.REQUIRED_FILES)

        with self.fixture_copy() as root:
            (root / validate_bundle.PILOT_FIXTURE_FILES[0]).unlink()
            self.assert_rejected(root, "missing required pilot fixtures")
        with self.fixture_copy() as root:
            extra = root / "fixtures/jobs/unreviewed.yaml"
            extra.write_text("synthetic_data: true\n", encoding="utf-8")
            self.assert_rejected(root, "unexpected pilot fixture files")

    def test_digest_changes_when_any_fixture_byte_changes(self) -> None:
        with self.fixture_copy() as root:
            baseline = validate_bundle.pilot_fixture_set_digest(root)
            path = root / "fixtures/profiles/basic.yaml"
            path.write_text(
                path.read_text(encoding="utf-8") + "# deterministic mutation\n",
                encoding="utf-8",
                newline="\n",
            )
            self.assertNotEqual(baseline, validate_bundle.pilot_fixture_set_digest(root))

    def test_schema_shape_synthetic_marker_and_unknown_fields_fail_closed(self) -> None:
        mutations = (
            (
                "fixtures/profiles/basic.yaml",
                lambda document: document.__setitem__("schema_version", "unknown.v9"),
                "invalid candidate profile references",
            ),
            (
                "fixtures/profiles/basic.yaml",
                lambda document: document.__setitem__("synthetic_data", False),
                "invalid candidate profile references",
            ),
            (
                "fixtures/catalog/catalog.yaml",
                lambda document: document.__setitem__("unreviewed", True),
                "unknown fields",
            ),
            (
                "fixtures/policies/community-laptop-policy.yaml",
                lambda document: document.pop("target"),
                "missing required fields",
            ),
        )
        for relative, mutate, expected in mutations:
            with self.subTest(relative=relative, expected=expected), self.fixture_copy() as root:
                document = self.read_yaml(root, relative)
                mutate(document)
                self.write_yaml(root, relative, document)
                self.assert_rejected(root, expected)

    def test_duplicate_alias_and_resource_bound_inputs_are_rejected(self) -> None:
        with self.fixture_copy() as root:
            path = root / "fixtures/profiles/basic.yaml"
            text = path.read_text(encoding="utf-8")
            path.write_text(
                text.replace(
                    "schema_version: thirdlife.profile.v1",
                    "schema_version: thirdlife.profile.v1\n"
                    "schema_version: thirdlife.profile.v1",
                    1,
                ),
                encoding="utf-8",
            )
            self.assert_rejected(root, "duplicate mapping key")

        with self.fixture_copy() as root:
            path = root / "fixtures/profiles/basic.yaml"
            text = path.read_text(encoding="utf-8")
            path.write_text(
                text.replace("profile_id: basic", "profile_id: &profile basic", 1)
                + "alias: *profile\n",
                encoding="utf-8",
            )
            self.assert_rejected(root, "anchors and aliases are prohibited")

        with self.fixture_copy() as root:
            path = root / "fixtures/profiles/basic.yaml"
            path.write_text(
                path.read_text(encoding="utf-8")
                + "#"
                + "x" * validate_bundle.PRIVACY_FIXTURE_MAX_BYTES,
                encoding="utf-8",
            )
            self.assert_rejected(root, "YAML exceeds")

    def test_ordering_uniqueness_and_collection_bounds_are_enforced(self) -> None:
        with self.fixture_copy() as root:
            relative = "fixtures/catalog/catalog.yaml"
            document = self.read_yaml(root, relative)
            document["applications"][0], document["applications"][1] = (
                document["applications"][1],
                document["applications"][0],
            )
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "exact ordered generic capability set")

        with self.fixture_copy() as root:
            relative = "fixtures/catalog/catalog.yaml"
            document = self.read_yaml(root, relative)
            document["applications"][1]["id"] = document["applications"][0]["id"]
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "application IDs")

        with self.fixture_copy() as root:
            relative = "fixtures/policies/community-laptop-policy.yaml"
            document = self.read_yaml(root, relative)
            document["rules"] = [
                copy.deepcopy(document["rules"][0]) for _ in range(65)
            ]
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "64-item limit")

    def test_cross_references_and_scenario_outcomes_are_enforced(self) -> None:
        mutations = (
            (
                "fixtures/profiles/basic.yaml",
                lambda document: document["workshop"]["capabilities"][0].__setitem__(
                    "capability_id", "unreviewed_capability"
                ),
                "capabilities do not resolve exactly",
            ),
            (
                "fixtures/profiles/basic.yaml",
                lambda document: document["policy"].__setitem__("version", "9.9.9"),
                "invalid candidate profile references",
            ),
            (
                "fixtures/jobs/assessment-ready.yaml",
                lambda document: document["observations"].pop(),
                "evidence keys must exactly cover",
            ),
            (
                "fixtures/jobs/assessment-ready.yaml",
                lambda document: document["expected"].__setitem__(
                    "policy_disposition", "human_review_required"
                ),
                "invalid governed scenario metadata",
            ),
            (
                "fixtures/jobs/assessment-ready.yaml",
                lambda document: document["profile"].__setitem__(
                    "version", "9.9.9"
                ),
                "invalid candidate profile binding",
            ),
        )
        for relative, mutate, expected in mutations:
            with self.subTest(relative=relative, expected=expected), self.fixture_copy() as root:
                document = self.read_yaml(root, relative)
                mutate(document)
                self.write_yaml(root, relative, document)
                self.assert_rejected(root, expected)

    def test_windows_target_policy_conditions_and_decisions_fail_closed(self) -> None:
        mutations = (
            (
                lambda policy: policy["target"].__setitem__(
                    "operating_system", "windows_10"
                ),
                "invalid Windows 11 x64 candidate policy metadata",
            ),
            (
                lambda policy: policy["rules"][0]["condition"].__setitem__(
                    "expression", "arbitrary"
                ),
                "unknown fields",
            ),
            (
                lambda policy: policy["rules"][0]["condition"].__setitem__(
                    "type", "evaluate_script"
                ),
                "unapproved value",
            ),
            (
                lambda policy: policy["rules"][0]["decision"].__setitem__(
                    "blocks_ready", False
                ),
                "invalid explicit disposition semantics",
            ),
            (
                lambda policy: policy["rules"][0]["decision"].__setitem__(
                    "unmet_disposition", "ready_to_prepare"
                ),
                "invalid explicit disposition semantics",
            ),
        )
        relative = "fixtures/policies/community-laptop-policy.yaml"
        for mutate, expected in mutations:
            with self.subTest(expected=expected), self.fixture_copy() as root:
                document = self.read_yaml(root, relative)
                mutate(document)
                self.write_yaml(root, relative, document)
                self.assert_rejected(root, expected)

        with self.fixture_copy() as root:
            document = self.read_yaml(root, relative)
            advisory = next(
                rule
                for rule in document["rules"]
                if rule["requirement_type"] == "advisory"
            )
            advisory["condition"] = {"type": "equals", "value": []}
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "equals requires a bounded non-null primitive")

        with self.fixture_copy() as root:
            document = self.read_yaml(root, relative)
            advisory = next(
                rule
                for rule in document["rules"]
                if rule["requirement_type"] == "advisory"
            )
            advisory["severity"] = "critical"
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "invalid requirement semantics")

    def test_unavailable_evidence_never_becomes_an_observed_value(self) -> None:
        with self.fixture_copy() as root:
            relative = "fixtures/jobs/partial-observations.yaml"
            document = self.read_yaml(root, relative)
            unavailable = next(
                item
                for item in document["observations"]
                if item["evidence_state"] == "not_available"
            )
            unavailable["value"]["data"] = True
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "unavailable evidence must carry explicit null")

        with self.fixture_copy() as root:
            relative = "fixtures/jobs/partial-observations.yaml"
            document = self.read_yaml(root, relative)
            unavailable = next(
                item
                for item in document["observations"]
                if item["evidence_state"] == "not_available"
            )
            unavailable.pop("limitation_code")
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "limitation_code")

        with self.fixture_copy() as root:
            relative = "fixtures/jobs/assessment-ready.yaml"
            document = self.read_yaml(root, relative)
            document["observations"][0]["limitation_code"] = "not_needed"
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "must not carry a limitation")

        with self.fixture_copy() as root:
            relative = "fixtures/jobs/sanitization-blocked.yaml"
            document = self.read_yaml(root, relative)
            document["sanitization_evidence"]["operator_id"] = (
                "SYNTHETIC-OPERATOR-999"
            )
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "unavailable attribution must be null")

    def test_sanitization_scenario_tuples_are_exact(self) -> None:
        mutations = (
            (
                "fixtures/jobs/assessment-ready.yaml",
                "evidence_state",
                "inferred",
            ),
            (
                "fixtures/jobs/partial-observations.yaml",
                "method_code",
                "synthetic_external_sanitization",
            ),
            (
                "fixtures/jobs/sanitization-blocked.yaml",
                "verification_state",
                "verified",
            ),
        )
        for relative, field, value in mutations:
            with self.subTest(relative=relative, field=field), self.fixture_copy() as root:
                document = self.read_yaml(root, relative)
                document["sanitization_evidence"][field] = value
                self.write_yaml(root, relative, document)
                self.assert_rejected(root, "invalid exact D-007 scenario tuple")

    def test_partial_evidence_is_complete_and_identifiers_are_bounded(self) -> None:
        with self.fixture_copy() as root:
            relative = "fixtures/jobs/partial-observations.yaml"
            document = self.read_yaml(root, relative)
            document["observations"].pop(0)
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "must cover the candidate policy")

        mutations = (
            ("sanitization_evidence", "provider_id"),
            ("sanitization_evidence", "method_code"),
            ("observations", "provider_id"),
        )
        for section, field in mutations:
            with self.subTest(section=section, field=field), self.fixture_copy() as root:
                relative = "fixtures/jobs/assessment-ready.yaml"
                document = self.read_yaml(root, relative)
                target = (
                    document[section]
                    if section == "sanitization_evidence"
                    else document[section][0]
                )
                target[field] = "invalid token with spaces"
                self.write_yaml(root, relative, document)
                self.assert_rejected(root, "bounded stable token")

    def test_catalog_and_recipient_placeholders_remain_fail_closed(self) -> None:
        mutations = (
            (
                "fixtures/catalog/catalog.yaml",
                lambda document: document["applications"][0].__setitem__(
                    "production_eligible", True
                ),
                "catalog placeholder must remain non-production",
            ),
            (
                "fixtures/catalog/catalog.yaml",
                lambda document: document["applications"][0]["review"].__setitem__(
                    "redistribution_status", "allowed"
                ),
                "placeholder review must remain pending/withheld",
            ),
            (
                "fixtures/catalog/catalog.yaml",
                lambda document: document["applications"][0]["review"].__setitem__(
                    "declared_license", "fixture-only"
                ),
                "placeholder review must remain pending/withheld",
            ),
            (
                "fixtures/catalog/catalog.yaml",
                lambda document: document["applications"][0]["package"].__setitem__(
                    "external_artifact", True
                ),
                "placeholder package contract is invalid",
            ),
            (
                "fixtures/profiles/basic.yaml",
                lambda document: document["recipient"]["choices"][0].__setitem__(
                    "workshop_action", True
                ),
                "recipient choice must remain deferred",
            ),
        )
        for relative, mutate, expected in mutations:
            with self.subTest(relative=relative, expected=expected), self.fixture_copy() as root:
                document = self.read_yaml(root, relative)
                mutate(document)
                self.write_yaml(root, relative, document)
                self.assert_rejected(root, expected)

    def test_personal_secret_path_url_and_command_content_is_rejected_without_echo(self) -> None:
        seeded_values = (
            ("ghp_abcdefghijklmnopqrstuvwxyz123456", "secret"),
            ("person@example.com", "email"),
            ("SSID=PrivateNetwork", "labelled device identifier"),
            (r"C:\Users\Private\secret.txt", "path"),
            ("https://example.test/package", "URL"),
            ("powershell.exe -EncodedCommand synthetic", "command"),
        )
        relative = "fixtures/catalog/catalog.yaml"
        for value, expected in seeded_values:
            with self.subTest(expected=expected), self.fixture_copy() as root:
                document = self.read_yaml(root, relative)
                document["applications"][0]["review"]["publisher"] = value
                self.write_yaml(root, relative, document)
                errors = self.assert_rejected(root, expected)
                self.assertNotIn(value, "\n".join(errors))

        with self.fixture_copy() as root:
            document = self.read_yaml(root, relative)
            document["applications"][0]["package"]["command"] = "synthetic"
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "prohibited data or execution field")

    def test_sensitive_mapping_keys_are_redacted_from_diagnostics(self) -> None:
        relative = "fixtures/catalog/catalog.yaml"
        sensitive_key = "seeded.person@example.invalid"
        with self.fixture_copy() as root:
            document = self.read_yaml(root, relative)
            document["applications"][0]["review"][sensitive_key] = (
                "https://example.invalid/seed"
            )
            self.write_yaml(root, relative, document)
            errors = self.assert_rejected(root, "email")
            self.assertNotIn(sensitive_key, "\n".join(errors))
            self.assertTrue(
                any("<redacted-key>" in error for error in errors),
                errors,
            )

    def test_generic_namespaces_and_closed_capabilities_are_enforced(self) -> None:
        with self.fixture_copy() as root:
            relative = "fixtures/catalog/catalog.yaml"
            document = self.read_yaml(root, relative)
            document["applications"][0]["id"] = "external.placeholder"
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "catalog placeholder must remain non-production")

        with self.fixture_copy() as root:
            relative = "fixtures/catalog/catalog.yaml"
            document = self.read_yaml(root, relative)
            document["applications"][0]["capability_id"] = "unreviewed_capability"
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "exact ordered generic capability set")

        with self.fixture_copy() as root:
            relative = "fixtures/catalog/catalog.yaml"
            document = self.read_yaml(root, relative)
            application = document["applications"][0]
            application["package"]["package_id"] = (
                "generic.synthetic.package.alternate"
            )
            application["verification"]["expected_package_id"] = (
                "generic.synthetic.package.alternate"
            )
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "exact approved synthetic catalog tuples")

        with self.fixture_copy() as root:
            relative = "fixtures/catalog/catalog.yaml"
            document = self.read_yaml(root, relative)
            document["applications"][0]["review"]["publisher"] = (
                "synthetic-alternate-publisher"
            )
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "placeholder review must remain pending/withheld")

    def test_job_profiles_are_exact_and_partial_outcome_is_reproducible(self) -> None:
        with self.fixture_copy() as root:
            relative = "fixtures/jobs/sanitization-blocked.yaml"
            document = self.read_yaml(root, relative)
            document["profile"]["active_capability_ids"].append("video_calling")
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "invalid candidate profile binding")

        with self.fixture_copy() as root:
            relative = "fixtures/jobs/partial-observations.yaml"
            document = self.read_yaml(root, relative)
            for observation in document["observations"]:
                if observation["evidence_key"] in {"function.camera", "function.microphone"}:
                    observation["evidence_state"] = "human_confirmed"
                    observation["value"]["data"] = "passed"
                    observation.pop("limitation_code", None)
            self.write_yaml(root, relative, document)
            self.assert_rejected(root, "disposition is not reproducible")

    def test_optional_capability_selection_obeys_declared_bounds(self) -> None:
        declared = validate_bundle.PILOT_CATALOG_CAPABILITIES
        essential = declared[:3]
        self.assertTrue(
            validate_bundle._pilot_capability_selection_valid(
                declared, essential, essential
            )
        )
        self.assertTrue(
            validate_bundle._pilot_capability_selection_valid(
                declared, essential, declared
            )
        )
        self.assertFalse(
            validate_bundle._pilot_capability_selection_valid(
                declared, essential, essential[:2]
            )
        )
        self.assertFalse(
            validate_bundle._pilot_capability_selection_valid(
                declared, essential, declared + ("unreviewed_capability",)
            )
        )

    def test_ready_claim_satisfies_candidate_policy_conditions(self) -> None:
        mutations = (
            ("memory.installed_bytes", 0),
            ("operating_system.release", "windows_10"),
        )
        for evidence_key, value in mutations:
            with self.subTest(evidence_key=evidence_key), self.fixture_copy() as root:
                relative = "fixtures/jobs/assessment-ready.yaml"
                document = self.read_yaml(root, relative)
                observation = next(
                    item
                    for item in document["observations"]
                    if item["evidence_key"] == evidence_key
                )
                observation["value"]["data"] = value
                self.write_yaml(root, relative, document)
                self.assert_rejected(root, "ready claim does not satisfy")

    def test_done_requires_pilot_owner_approval_bound_to_current_digest(self) -> None:
        with self.fixture_copy() as root:
            tasks = copy.deepcopy(TASK_BY_ID)
            tasks["TL-0007"]["status"] = "done"
            tasks["TL-0007"]["evidence"] = []
            self.assert_rejected(root, "done evidence must bind", tasks)

        with self.fixture_copy() as root:
            digest = validate_bundle.pilot_fixture_set_digest(root)
            tasks = copy.deepcopy(TASK_BY_ID)
            tasks["TL-0007"]["status"] = "done"
            tasks["TL-0007"]["evidence"] = [
                {
                    "summary": (
                        "Pilot owner Janne Vuorela approved the candidate policy "
                        "values and initial capability set."
                    ),
                    "result": "passed",
                    "environment": "Named human governance review",
                    "reference": f"fixture-set sha256:{digest}",
                }
            ]
            validation, actual = self.validate(root, tasks)
            self.assertEqual(actual, digest)
            self.assertEqual(validation.errors, [])

            tasks["TL-0007"]["evidence"][0]["reference"] = "wrong digest"
            self.assert_rejected(root, "done evidence must bind", tasks)


if __name__ == "__main__":
    unittest.main()
