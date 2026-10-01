#!/usr/bin/env python3
"""Focused regressions for repository workflow and supply-chain governance."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import yaml


TOOLS_ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = TOOLS_ROOT.parent
sys.path.insert(0, str(TOOLS_ROOT))

import supply_chain  # noqa: E402
import validate_repository  # noqa: E402


CURRENT_DIGEST = "a" * 64
HISTORICAL_DIGEST = "b" * 64


def approval_evidence(matrix_digest: str) -> dict[str, str]:
    return {
        "summary": (
            "Named human licence and rights reviewer approved the proposal "
            "without granting blanket redistribution rights."
        ),
        "result": "passed",
        "reference": f"matrix sha256:{matrix_digest}",
    }


class SupplyChainApprovalGovernanceTests(unittest.TestCase):
    def validate(
        self,
        *,
        approval_state: str,
        evidence: list[dict[str, str]],
        status: str = "done",
    ) -> validate_repository.Validation:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "TASKS.yaml").write_text(
                yaml.safe_dump(
                    {
                        "tasks": [
                            {
                                "id": "TL-0006",
                                "status": status,
                                "evidence": evidence,
                            }
                        ]
                    },
                    sort_keys=False,
                ),
                encoding="utf-8",
            )
            result = supply_chain.SupplyChainResult(
                root=root,
                inventory=(),
                dependency_graph={},
                lock_digest="c" * 64,
                matrix_digest=CURRENT_DIGEST,
                approval_state=approval_state,
                errors=(),
            )
            validation = validate_repository.Validation()
            with (
                patch.object(validate_repository, "ROOT", root),
                patch.object(
                    validate_repository.supply_chain,
                    "validate_supply_chain",
                    return_value=result,
                ),
            ):
                validate_repository.validate_supply_chain_contract(validation)
            return validation

    def test_pending_current_matrix_allows_digest_bound_historical_approval(self) -> None:
        validation = self.validate(
            approval_state="pending",
            evidence=[approval_evidence(HISTORICAL_DIGEST)],
        )

        self.assertEqual(validation.errors, [])

    def test_pending_current_matrix_rejects_current_digest_approval_claim(self) -> None:
        validation = self.validate(
            approval_state="pending",
            evidence=[
                approval_evidence(HISTORICAL_DIGEST),
                approval_evidence(CURRENT_DIGEST),
            ],
        )

        self.assertTrue(
            any(
                "current matrix while the governed review is Pending" in error
                for error in validation.errors
            ),
            validation.errors,
        )

    def test_pending_done_task_requires_digest_bound_historical_approval(self) -> None:
        validation = self.validate(approval_state="pending", evidence=[])

        self.assertTrue(
            any(
                "requires prior named human licence/rights approval" in error
                for error in validation.errors
            ),
            validation.errors,
        )

    def test_approval_claim_without_matrix_digest_is_rejected(self) -> None:
        evidence = approval_evidence(HISTORICAL_DIGEST)
        evidence["reference"] = "reviewed commit " + "d" * 40

        validation = self.validate(approval_state="pending", evidence=[evidence])

        self.assertTrue(
            any("must bind an exact matrix SHA-256" in error for error in validation.errors),
            validation.errors,
        )

    def test_approved_current_matrix_rejects_historical_only_evidence(self) -> None:
        validation = self.validate(
            approval_state="approved",
            evidence=[approval_evidence(HISTORICAL_DIGEST)],
        )

        self.assertTrue(
            any(
                "approved review evidence must bind" in error
                for error in validation.errors
            ),
            validation.errors,
        )

    def test_approved_current_matrix_accepts_current_digest_evidence(self) -> None:
        validation = self.validate(
            approval_state="approved",
            evidence=[
                approval_evidence(HISTORICAL_DIGEST),
                approval_evidence(CURRENT_DIGEST),
            ],
        )

        self.assertEqual(validation.errors, [])


class ProjectReferenceLockTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.projects = []
        for name, references in (
            ("Core", []),
            ("Inventory", ["Core"]),
            ("Tests", ["Core", "Inventory"]),
        ):
            path = self.root / name / f"{name}.csproj"
            path.parent.mkdir()
            items = "".join(
                f'<ProjectReference Include="../{target}/{target}.csproj" />'
                for target in references
            )
            path.write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<TargetFramework>net10.0</TargetFramework></PropertyGroup>'
                f'<ItemGroup>{items}</ItemGroup></Project>', encoding="utf-8"
            )
            self.projects.append(path)
        self.locks = {
            "Core": {},
            "Inventory": {"core": {"type": "Project"}},
            "Tests": {
                "core": {"type": "Project"},
                "inventory": {"type": "Project", "dependencies": {"Core": "[1.0.0, )"}},
            },
        }

    def validate(self) -> validate_repository.Validation:
        for name, dependencies in self.locks.items():
            (self.root / name / "packages.lock.json").write_text(
                json.dumps({"version": 2, "dependencies": {"net10.0": dependencies}}),
                encoding="utf-8",
            )
        validation = validate_repository.Validation()
        with patch.object(validate_repository, "ROOT", self.root):
            validate_repository.validate_project_reference_locks(self.projects, validation)
        return validation

    def test_current_direct_and_transitive_project_graph_is_accepted(self) -> None:
        self.assertEqual(self.validate().errors, [])

    def test_missing_direct_project_entry_is_rejected(self) -> None:
        self.locks["Inventory"].clear()
        errors = self.validate().errors
        self.assertTrue(any("Inventory/packages.lock.json" in error and "missing=['core']" in error for error in errors), errors)

    def test_missing_transitive_project_entry_is_rejected(self) -> None:
        path = self.projects[-1]
        path.write_text(path.read_text().replace('<ProjectReference Include="../Core/Core.csproj" />', ""))
        self.locks["Tests"].pop("core")
        errors = self.validate().errors
        self.assertTrue(any("Tests/packages.lock.json" in error and "missing=['core']" in error for error in errors), errors)

    def test_missing_nested_project_dependency_is_rejected(self) -> None:
        self.locks["Tests"]["inventory"].pop("dependencies")
        errors = self.validate().errors
        self.assertTrue(any("Project inventory dependencies differ" in error for error in errors), errors)

    def test_stale_or_misclassified_project_entry_is_rejected(self) -> None:
        self.locks["Tests"]["orphan"] = {"type": "Project"}
        self.locks["Inventory"]["core"] = {"type": "Transitive", "contentHash": "synthetic"}
        errors = self.validate().errors
        self.assertTrue(any("unexpected=['orphan']" in error for error in errors), errors)
        self.assertTrue(any("missing=['core']" in error for error in errors), errors)

    def test_nested_package_dependencies_are_not_treated_as_project_references(self) -> None:
        self.locks["Tests"]["inventory"]["dependencies"]["Example.Package"] = "[1.0.0, )"
        self.assertEqual(self.validate().errors, [])

    def test_conditional_project_reference_requires_explicit_validator_support(self) -> None:
        path = self.projects[1]
        path.write_text(path.read_text().replace('<ItemGroup>', '<ItemGroup Condition="synthetic">'))
        errors = self.validate().errors
        self.assertTrue(any("literal unconditional" in error for error in errors), errors)

    def test_malformed_nested_dependency_is_reported_without_crashing(self) -> None:
        self.locks["Tests"]["inventory"]["dependencies"] = []
        errors = self.validate().errors
        self.assertTrue(any("dependencies must be an object" in error for error in errors), errors)


class TestManifestContractTests(unittest.TestCase):
    def read_manifest(self) -> dict[str, object]:
        return json.loads((REPOSITORY_ROOT / "eng/test-manifest.json").read_text(encoding="utf-8"))

    def validate(self, manifest: object, *, raw: bool = False) -> validate_repository.Validation:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "eng").mkdir()
            (root / "eng/test-manifest.json").write_text(
                manifest if raw else json.dumps(manifest), encoding="utf-8"
            )
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository.validate_test_manifest(validation)
        return validation

    def test_current_manifest_satisfies_governed_selection(self) -> None:
        self.assertEqual(self.validate(self.read_manifest()).errors, [])

    def test_sandbox_profile_task_selection_authority_trigger_and_bounds_cannot_drift(self) -> None:
        for field, replacement in (
            ("task_id", "TL-0104"),
            ("tier", "Full"),
            ("selection_ref", "tiers.full.filter"),
            ("selection_ref", "arbitrary.command"),
            ("hosted_environment", "remote_runner"),
            ("risk_trigger", ""),
            ("target_seconds", 600),
            ("command_timeout_seconds", 1200),
            ("command_timeout_seconds", True),
            ("host_timeout_minutes", 60),
            ("memory_mb", 8192),
            ("network_reason", "disabled_offline_phase"),
        ):
            with self.subTest(field=field, replacement=replacement):
                manifest = self.read_manifest()
                manifest["sandbox_profiles"]["tl0107_quick"][field] = replacement
                self.assertTrue(self.validate(manifest).errors)
        for remove_profile in (False, True):
            manifest = self.read_manifest()
            if remove_profile:
                manifest["sandbox_profiles"].clear()
            else:
                del manifest["sandbox_profiles"]
            self.assertTrue(self.validate(manifest).errors)

    def test_tl0109_sandbox_profiles_reject_selection_and_budget_drift(self) -> None:
        for profile_name in ("tl0109_quick", "tl0109_targeted", "tl0111_quick", "tl0111_targeted", "tl0112_quick", "tl0112_targeted", "tl0113_quick", "tl0113_targeted"):
            for field, replacement in (
                ("task_id", "TL-0107"), ("tier", "Full"),
                ("hosted_environment", "remote_runner"), ("risk_trigger", ""),
                ("target_seconds", 1200), ("command_timeout_seconds", 1200),
                ("command_timeout_seconds", True), ("host_timeout_minutes", 60),
                ("memory_mb", 8192), ("network_reason", "disabled_offline_phase"),
            ):
                with self.subTest(profile=profile_name, field=field, replacement=replacement):
                    manifest = self.read_manifest()
                    manifest["sandbox_profiles"][profile_name][field] = replacement
                    self.assertTrue(self.validate(manifest).errors)
            manifest = self.read_manifest()
            del manifest["sandbox_profiles"][profile_name]
            self.assertTrue(self.validate(manifest).errors)
        for field, replacement in (
            ("selection_ref", "tiers.full.filter"), ("selection_ref", "arbitrary.command"),
        ):
            manifest = self.read_manifest()
            manifest["sandbox_profiles"]["tl0109_quick"][field] = replacement
            self.assertTrue(self.validate(manifest).errors)
        for field, replacement in (
            ("filter_ref", "tiers.full.filter"), ("filter_ref", "Category=ActiveMachine"),
            ("includes_quick_ref", "tiers.full.includes"), ("includes_quick_ref", False),
            ("test_projects", []),
            ("test_projects", ["tests/ThirdLife.Inventory.Tests/ThirdLife.Inventory.Tests.csproj"]),
            ("test_projects", ["tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj"] * 2),
            ("test_projects", ["arbitrary.csproj"]),
        ):
            with self.subTest(field=field, replacement=replacement):
                manifest = self.read_manifest()
                manifest["sandbox_profiles"]["tl0109_targeted"][field] = replacement
                self.assertTrue(self.validate(manifest).errors)

    def test_tl0111_profiles_reject_selection_or_native_execution_expansion(self) -> None:
        for profile_name in ("tl0111_quick", "tl0111_targeted"):
            manifest = self.read_manifest()
            manifest["sandbox_profiles"][profile_name]["required_native_step"] = manifest["sandbox_profiles"]["tl0109_targeted"]["required_native_step"]
            self.assertTrue(self.validate(manifest).errors)
        for field, replacement in (
            ("filter_ref", "Category=ActiveMachine"), ("includes_quick_ref", False),
            ("test_projects", []), ("test_projects", ["arbitrary.csproj"]),
            ("test_projects", ["tests/ThirdLife.Inventory.Tests/ThirdLife.Inventory.Tests.csproj"]),
        ):
            with self.subTest(field=field, replacement=replacement):
                manifest = self.read_manifest()
                manifest["sandbox_profiles"]["tl0111_targeted"][field] = replacement
                self.assertTrue(self.validate(manifest).errors)
        manifest = self.read_manifest()
        manifest["sandbox_profiles"]["tl0111_quick"]["selection_ref"] = "tiers.full.filter"
        self.assertTrue(self.validate(manifest).errors)

    def test_tl0112_profiles_reject_selection_or_native_execution_expansion(self) -> None:
        for profile_name in ("tl0112_quick", "tl0112_targeted"):
            manifest = self.read_manifest()
            manifest["sandbox_profiles"][profile_name]["required_native_step"] = manifest["sandbox_profiles"]["tl0109_targeted"]["required_native_step"]
            self.assertTrue(self.validate(manifest).errors)
        for field, replacement in (
            ("filter_ref", "Category=ActiveMachine"), ("includes_quick_ref", False),
            ("test_projects", []), ("test_projects", ["arbitrary.csproj"]),
            ("test_projects", ["tests/ThirdLife.Inventory.Tests/ThirdLife.Inventory.Tests.csproj"]),
        ):
            with self.subTest(field=field, replacement=replacement):
                manifest = self.read_manifest()
                manifest["sandbox_profiles"]["tl0112_targeted"][field] = replacement
                self.assertTrue(self.validate(manifest).errors)
        manifest = self.read_manifest()
        manifest["sandbox_profiles"]["tl0112_quick"]["selection_ref"] = "tiers.full.filter"
        self.assertTrue(self.validate(manifest).errors)

    def test_tl0113_profiles_reject_selection_or_native_execution_expansion(self) -> None:
        for profile_name in ("tl0113_quick", "tl0113_targeted"):
            manifest = self.read_manifest()
            manifest["sandbox_profiles"][profile_name]["required_native_step"] = manifest["sandbox_profiles"]["tl0109_targeted"]["required_native_step"]
            self.assertTrue(self.validate(manifest).errors)
        for field, replacement in (
            ("filter_ref", "Category=ActiveMachine"), ("includes_quick_ref", False),
            ("test_projects", []), ("test_projects", ["arbitrary.csproj"]),
            ("test_projects", ["tests/ThirdLife.Inventory.Tests/ThirdLife.Inventory.Tests.csproj"]),
        ):
            with self.subTest(field=field, replacement=replacement):
                manifest = self.read_manifest()
                manifest["sandbox_profiles"]["tl0113_targeted"][field] = replacement
                self.assertTrue(self.validate(manifest).errors)
        manifest = self.read_manifest()
        manifest["sandbox_profiles"]["tl0113_quick"]["selection_ref"] = "tiers.full.filter"
        self.assertTrue(self.validate(manifest).errors)

    def test_tl0112_targeted_requires_each_of_the_four_reviewed_projects(self) -> None:
        for removed in range(4):
            with self.subTest(removed=removed):
                manifest = self.read_manifest()
                del manifest["sandbox_profiles"]["tl0112_targeted"]["test_projects"][removed]
                self.assertTrue(self.validate(manifest).errors)

    def test_tl0113_targeted_rejects_adding_projects_to_the_ui_selection(self) -> None:
        for project in ("Inventory", "Core", "Persistence"):
            with self.subTest(project=project):
                manifest = self.read_manifest()
                manifest["sandbox_profiles"]["tl0113_targeted"]["test_projects"].append(
                    f"tests/ThirdLife.{project}.Tests/ThirdLife.{project}.Tests.csproj")
                self.assertTrue(self.validate(manifest).errors)

    def test_tl0109_native_step_rejects_filter_marker_budget_and_scope_drift(self) -> None:
        for field, replacement in (
            ("label", "arbitrary"), ("test_project", "arbitrary.csproj"),
            ("filter", "Category=ActiveMachine"),
            ("filter", "FullyQualifiedName~BatteryReportWorkspaceTests"),
            ("success_marker", ""), ("required_success_markers", []),
            ("required_success_markers", ["Test Run Successful."]),
            ("command_timeout_seconds", 600), ("command_timeout_seconds", True),
            ("evidence_classification", "physical_host_battery"),
        ):
            with self.subTest(field=field, replacement=replacement):
                manifest = self.read_manifest()
                manifest["sandbox_profiles"]["tl0109_targeted"]["required_native_step"][field] = replacement
                self.assertTrue(self.validate(manifest).errors)
        manifest = self.read_manifest()
        del manifest["sandbox_profiles"]["tl0109_targeted"]["required_native_step"]
        self.assertTrue(self.validate(manifest).errors)
        for removed in range(3):
            manifest = self.read_manifest()
            manifest["sandbox_profiles"]["tl0109_targeted"]["required_native_step"]["required_success_markers"].pop(removed)
            self.assertTrue(self.validate(manifest).errors)
        manifest = self.read_manifest()
        manifest["sandbox_profiles"]["tl0109_quick"]["required_native_step"] = manifest["sandbox_profiles"]["tl0109_targeted"]["required_native_step"]
        self.assertTrue(self.validate(manifest).errors)

    def test_missing_duplicate_or_arbitrary_quick_projects_are_rejected(self) -> None:
        for mutation in ("empty", "remove_core", "remove_inventory", "remove_ui", "duplicate", "path", "fields"):
            with self.subTest(mutation=mutation):
                manifest = self.read_manifest()
                projects = manifest["tiers"]["quick"]["test_projects"]
                if mutation == "empty":
                    projects.clear()
                elif mutation == "remove_core":
                    projects.pop(0)
                elif mutation == "remove_inventory":
                    projects.pop(1)
                elif mutation == "remove_ui":
                    projects.pop(2)
                elif mutation == "duplicate":
                    projects.append(projects[0])
                elif mutation == "path":
                    projects[0]["path"] = "../unreviewed.csproj"
                else:
                    projects[0]["command"] = "arbitrary"
                self.assertTrue(self.validate(manifest).errors)

    def test_filters_cannot_expand_scope_or_skip_all_tests(self) -> None:
        for tier in ("quick", "targeted", "full"):
            for test_filter in ("", "Category=Missing", "Category!=Extended", "Category!=ActiveMachine|Category!=Extended"):
                with self.subTest(tier=tier, test_filter=test_filter):
                    manifest = self.read_manifest()
                    selection = manifest["tiers"][tier]
                    if tier == "quick":
                        selection["test_projects"][0]["filter"] = test_filter
                    else:
                        selection["default_filter" if tier == "targeted" else "filter"] = test_filter
                    self.assertTrue(self.validate(manifest).errors)

    def test_authority_certification_schema_and_quick_inclusion_are_frozen(self) -> None:
        mutations = (
            (("schema_version",), 2),
            (("schema_version",), True),
            (("authority", "runtime"), "remote_runner"),
            (("authority", "remote_ci"), "authoritative"),
            (("authority", "cross_hardware_certification"), True),
            (("authority", "cross_hardware_certification"), 0),
            (("tiers", "targeted", "includes_quick"), False),
            (("tiers", "targeted", "includes_quick"), 1),
        )
        for path, value in mutations:
            with self.subTest(path=path, value=value):
                manifest = self.read_manifest()
                owner = manifest
                for key in path[:-1]:
                    owner = owner[key]
                owner[path[-1]] = value
                self.assertTrue(self.validate(manifest).errors)

    def test_budgets_and_gate_scenario_controls_cannot_be_removed(self) -> None:
        for tier, field in (
            ("quick", "target_seconds"),
            ("targeted", "target_seconds"),
            ("full", "trigger"),
            ("full", "includes"),
            ("full", "separate_checks"),
            ("extended", "entry_point"),
            ("extended", "requirements"),
        ):
            with self.subTest(tier=tier, field=field):
                manifest = self.read_manifest()
                manifest["tiers"][tier].pop(field)
                self.assertTrue(self.validate(manifest).errors)
        for value in (0, -1, 600, "120", 120.0, True):
            with self.subTest(value=value):
                manifest = self.read_manifest()
                manifest["tiers"]["quick"]["target_seconds"] = value
                self.assertTrue(self.validate(manifest).errors)

    def test_unknown_fields_and_duplicate_json_keys_are_rejected(self) -> None:
        manifest = self.read_manifest()
        manifest["tiers"]["quick"]["skip_build"] = True
        self.assertTrue(self.validate(manifest).errors)
        body = json.dumps(self.read_manifest()).replace('"schema_version": 1', '"schema_version": 2, "schema_version": 1')
        errors = self.validate(body, raw=True).errors
        self.assertTrue(any("unique object keys" in error for error in errors), errors)

    def test_malformed_and_oversized_manifest_is_reported_without_crashing(self) -> None:
        for value in (None, [], 1, "invalid"):
            with self.subTest(value=value):
                self.assertTrue(self.validate(value).errors)
        for body in ("{", " " * 65537, "[" * 2000 + "]" * 2000):
            with self.subTest(length=len(body)):
                self.assertTrue(self.validate(body, raw=True).errors)


class SupplementaryWorkflowContractTests(unittest.TestCase):
    def read_workflow(self) -> dict[str, object]:
        return yaml.load(
            (REPOSITORY_ROOT / ".github/workflows/verify.yml").read_text(encoding="utf-8"),
            Loader=yaml.BaseLoader,
        )

    def validate(self, workflow: object) -> validate_repository.Validation:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            destination = root / ".github/workflows/verify.yml"
            destination.parent.mkdir(parents=True)
            destination.write_text(yaml.safe_dump(workflow), encoding="utf-8")
            validation = validate_repository.Validation()
            with patch.object(validate_repository, "ROOT", root):
                validate_repository.validate_workflow(validation)
        return validation

    def job(self, workflow: dict[str, object]) -> dict[str, object]:
        return workflow["jobs"]["supplementary-windows-check"]

    def test_current_workflow_is_hardened_and_supplementary(self) -> None:
        self.assertEqual(self.validate(self.read_workflow()).errors, [])

    def test_authoritative_workflow_or_job_name_is_rejected(self) -> None:
        for target in ("workflow", "job"):
            with self.subTest(target=target):
                workflow = self.read_workflow()
                owner = workflow if target == "workflow" else self.job(workflow)
                owner["name"] = "Windows authoritative verification"
                self.assertTrue(self.validate(workflow).errors)

    def test_ci_requires_quick_restore_and_supplementary_mode(self) -> None:
        for argument in (" -Tier Quick", " -Restore", " -SupplementaryCi"):
            with self.subTest(argument=argument):
                workflow = self.read_workflow()
                step = self.job(workflow)["steps"][-1]
                self.assertIn(argument, step["run"])
                step["run"] = step["run"].replace(argument, "")
                errors = self.validate(workflow).errors
                self.assertTrue(any("supplementary Quick tier" in error for error in errors), errors)

    def test_remote_runtime_or_extended_execution_is_rejected(self) -> None:
        for command in (
            "./eng/verify.ps1 -Tier Full -Restore -SupplementaryCi",
            "./eng/verify.ps1 -Tier Extended -Restore -SupplementaryCi",
            "./eng/run-tl0010-sandbox.ps1",
        ):
            with self.subTest(command=command):
                workflow = self.read_workflow()
                self.job(workflow)["steps"][-1]["run"] = command
                self.assertTrue(self.validate(workflow).errors)

    def test_privileged_triggers_and_write_permissions_are_rejected(self) -> None:
        for mutation in ("pull_request_target", "write", "job_write"):
            with self.subTest(mutation=mutation):
                workflow = self.read_workflow()
                if mutation == "pull_request_target":
                    workflow["on"]["pull_request_target"] = {"branches": ["main"]}
                elif mutation == "write":
                    workflow["permissions"] = {"contents": "write"}
                else:
                    self.job(workflow)["permissions"] = {"contents": "write"}
                self.assertTrue(self.validate(workflow).errors)

    def test_persisted_credentials_and_shallow_history_are_rejected(self) -> None:
        for field, value in (("persist-credentials", "true"), ("fetch-depth", "1")):
            with self.subTest(field=field):
                workflow = self.read_workflow()
                self.job(workflow)["steps"][0]["with"][field] = value
                errors = self.validate(workflow).errors
                self.assertTrue(any("disable persisted credentials" in error for error in errors), errors)

    def test_action_tags_and_unknown_pins_are_rejected(self) -> None:
        for index in range(3):
            for revision in ("v7", "f" * 40):
                with self.subTest(index=index, revision=revision):
                    workflow = self.read_workflow()
                    step = self.job(workflow)["steps"][index]
                    step["uses"] = step["uses"].split("@", 1)[0] + "@" + revision
                    self.assertTrue(self.validate(workflow).errors)

    def test_extra_commands_artifact_uploads_secrets_and_bypasses_are_rejected(self) -> None:
        mutations = (
            {"name": "Extra command", "run": "Write-Output unexpected"},
            {"name": "Upload", "uses": "actions/upload-artifact@" + "a" * 40},
            {"name": "Secret", "run": "Write-Output '${{ secrets.TOKEN }}'"},
        )
        for step in mutations:
            with self.subTest(step=step):
                workflow = self.read_workflow()
                self.job(workflow)["steps"].append(step)
                self.assertTrue(self.validate(workflow).errors)
        for bypass in ("continue-on-error", "if"):
            with self.subTest(bypass=bypass):
                workflow = self.read_workflow()
                self.job(workflow)["steps"][-1][bypass] = "true"
                self.assertTrue(self.validate(workflow).errors)

    def test_pinned_toolchain_and_dependencies_cannot_drift(self) -> None:
        for target in ("dotnet", "python", "pip"):
            with self.subTest(target=target):
                workflow = self.read_workflow()
                steps = self.job(workflow)["steps"]
                if target == "dotnet":
                    steps[1]["with"] = {"dotnet-version": "10.0.x"}
                elif target == "python":
                    steps[2]["with"]["python-version"] = "3.x"
                else:
                    steps[3]["run"] = "python -m pip install PyYAML"
                self.assertTrue(self.validate(workflow).errors)

    def test_malformed_job_or_step_is_reported_without_crashing(self) -> None:
        for value in (None, [], "invalid"):
            for target in ("workflow", "jobs", "job", "steps", "step"):
                with self.subTest(value=value, target=target):
                    workflow = self.read_workflow()
                    if target == "workflow":
                        workflow = value
                    elif target == "jobs":
                        workflow["jobs"] = value
                    elif target == "job":
                        workflow["jobs"]["supplementary-windows-check"] = value
                    elif target == "steps":
                        self.job(workflow)["steps"] = value
                    else:
                        self.job(workflow)["steps"][0] = value
                    self.assertTrue(self.validate(workflow).errors)


if __name__ == "__main__":
    unittest.main()
