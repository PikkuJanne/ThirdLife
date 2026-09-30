#!/usr/bin/env python3
"""Focused regressions for task-bound Windows Sandbox verification."""

from __future__ import annotations

import re
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
HOST_HARNESS = REPOSITORY_ROOT / "eng" / "run-tl0102-sandbox.ps1"
GUEST_HARNESS = REPOSITORY_ROOT / "eng" / "run-tl0102-sandbox-guest.ps1"
POWERSHELL = shutil.which("pwsh") or shutil.which("powershell.exe")
STABILIZATION_OWNER = "AMD-2026-09-12-REPOSITORY-STABILIZATION"


class SandboxHarnessContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.host = HOST_HARNESS.read_text(encoding="utf-8")
        cls.guest = GUEST_HARNESS.read_text(encoding="utf-8")

    def test_tl0104_stages_only_its_diagnostics_working_inputs(self):
        self.assertIn(
            '[ValidateSet("TL-0102", "TL-0103", "TL-0104", "TL-0107", '
            '"AMD-2026-09-12-REPOSITORY-STABILIZATION")]', self.host
        )
        selection = re.search(
            r'if \(\$TaskId -eq "TL-0104"\) \{\s*\$taskWorkingPaths \+= @\((.*?)\)\s*\}',
            self.host,
            re.DOTALL,
        )
        self.assertIsNotNone(selection)
        selected_paths = selection.group(1)
        self.assertIn('"src/ThirdLife.Diagnostics"', selected_paths)
        self.assertIn('"tests/ThirdLife.Diagnostics.Tests"', selected_paths)
        self.assertIn('"docs/privacy/redaction-test-cases.yaml"', selected_paths)

    def test_tl0104_targeted_uses_its_lock_and_runs_the_full_test_project(self):
        lock_selection = re.search(
            r'if \(\$SelectedTaskId -eq "TL-0104"\) \{(.*?)\}\s*else',
            self.host,
            re.DOTALL,
        )
        self.assertIsNotNone(lock_selection)
        self.assertIn(
            'tests\\ThirdLife.Diagnostics.Tests\\packages.lock.json',
            lock_selection.group(1),
        )

        targeted = re.search(
            r'if \(\$phase -eq "Targeted" -and \$TaskId -eq "TL-0104"\) \{(.*?)\}\s*elseif',
            self.guest,
            re.DOTALL,
        )
        self.assertIsNotNone(targeted)
        targeted_body = targeted.group(1)
        self.assertIn(
            'tests\\ThirdLife.Diagnostics.Tests\\ThirdLife.Diagnostics.Tests.csproj',
            targeted_body,
        )
        self.assertIn('"restore", $diagnosticsProject, "--locked-mode"', targeted_body)
        self.assertIn('"test", $diagnosticsProject', targeted_body)
        self.assertNotIn('"--filter"', targeted_body)

    def test_shared_fail_closed_boundaries_remain_enabled(self):
        self.assertIn(
            '$networkingEnabled = $Phase -eq "Full" -or '
            '($TaskId -eq "TL-0107" -and $Phase -eq "Quick")', self.host
        )
        self.assertIn(
            'if ($sourceDigest -ne $request.expected_source_digest)', self.guest
        )
        self.assertIn('raw_output_retained = $false', self.guest)
        self.assertIn(
            'if ($RunRequest.task_id -eq "TL-0104" -and '
            '$RunRequest.phase -notin @("Targeted", "Quick", "Full"))',
            self.guest,
        )

    def test_shared_quick_is_separate_from_legacy_quick_and_retains_sandbox_bounds(self):
        self.assertIn(
            'if ($TaskId -eq "TL-0107" -and $Phase -ne "Quick")', self.host
        )
        self.assertLess(
            self.guest.index('elseif ($TaskId -in @($RepositoryStabilizationTaskId, "TL-0107"))'),
            self.guest.index('elseif ($phase -eq "Quick")'),
        )
        self.assertEqual(self.host.count('<ReadOnly>true</ReadOnly>'), 9)
        self.assertEqual(self.host.count('<ReadOnly>false</ReadOnly>'), 1)
        self.assertIn('$SandboxMemoryMb = 4096', self.host)
        self.assertIn('$SandboxMemoryMb = 4096', self.guest)
        self.assertIn('$SandboxTimeoutMinutes = 30', self.host)
        self.assertIn('$ResultLimitBytes = 32768', self.host)
        self.assertIn('$ResultLimitBytes = 32768', self.guest)
        self.assertIn('$TailLimitBytes = 8192', self.host)
        self.assertIn('$TailLimitBytes = 8192', self.guest)
        self.assertIn('$sourceHistoryIncluded = $Phase -in @("Quick", "Full")', self.host)
        self.assertIn('$env:NUGET_PACKAGES = Join-Path $stateDirectory "nuget-packages"', self.guest)
        for script in (self.host, self.guest):
            for line in script.splitlines():
                if 'Start-Process ' in line:
                    self.assertIn('-WindowStyle Hidden', line)

    def test_manifest_declares_exact_quick_selection_environment_trigger_and_budgets(self):
        manifest = json.loads((REPOSITORY_ROOT / "eng/test-manifest.json").read_text(encoding="utf-8"))
        profile = manifest["sandbox_profiles"]["tl0107_quick"]
        self.assertEqual(profile, {
            "task_id": "TL-0107",
            "tier": "Quick",
            "selection_ref": "tiers.quick.test_projects",
            "hosted_environment": "active_machine_windows_sandbox",
            "risk_trigger": "host_application_control_blocks_required_quick",
            "target_seconds": 120,
            "command_timeout_seconds": 180,
            "host_timeout_minutes": 30,
            "memory_mb": 4096,
            "network_reason": "governed_nuget_audit",
        })
        self.assertEqual(profile["target_seconds"], manifest["tiers"]["quick"]["target_seconds"])
        selection = manifest
        for field in profile["selection_ref"].split("."):
            selection = selection[field]
        self.assertIs(selection, manifest["tiers"]["quick"]["test_projects"])
        self.assertTrue(all((REPOSITORY_ROOT / project["path"]).is_file() for project in selection))
        self.assertEqual(
            [project["path"] for project in manifest["tiers"]["quick"]["test_projects"]],
            [f"tests/ThirdLife.{name}.Tests/ThirdLife.{name}.Tests.csproj" for name in ("Core", "Inventory")],
        )
        self.assertTrue(all(
            project["filter"] == "Category!=ActiveMachine&Category!=Extended&Category!=SandboxOnly"
            for project in manifest["tiers"]["quick"]["test_projects"]
        ))
        self.assertIn(f'$SandboxMemoryMb = {profile["memory_mb"]}', self.host)
        self.assertIn(f'$SandboxMemoryMb = {profile["memory_mb"]}', self.guest)
        self.assertIn(f'$SandboxTimeoutMinutes = {profile["host_timeout_minutes"]}', self.host)


@unittest.skipUnless(os.name == "nt" and POWERSHELL, "Windows PowerShell is required")
class RepositorySandboxProfileTests(unittest.TestCase):
    def invoke_functions(
        self, body: str, *, harness: Path = GUEST_HARNESS,
        function_names: tuple[str, ...] = (
            "Invoke-SharedVerifierProfile", "Get-CommandTimeoutSeconds",
            "Assert-RunRequest", "Assert-ExactObjectKeys",
        ),
    ) -> subprocess.CompletedProcess[str]:
        # Parse and load only pure validation/dispatch functions; never execute
        # guest top-level initialization, staging, or a Sandbox launch.
        harness_literal = "'" + str(harness).replace("'", "''") + "'"
        names_literal = ", ".join("'" + name + "'" for name in function_names)
        script = (
            "$ErrorActionPreference = 'Stop'\n"
            "$tokens = $null; $parseErrors = $null\n"
            f"$ast = [Management.Automation.Language.Parser]::ParseFile({harness_literal}, [ref]$tokens, [ref]$parseErrors)\n"
            "if ($parseErrors.Count -ne 0) { throw 'Harness parse failed' }\n"
            f"$names = @({names_literal})\n"
            "$functions = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in $names }, $true))\n"
            "if ($functions.Count -ne $names.Count) { throw 'Required functions missing' }\n"
            "foreach ($function in $functions) { Invoke-Expression $function.Extent.Text }\n"
            f"$RepositoryStabilizationTaskId = '{STABILIZATION_OWNER}'\n"
            "$CommandTimeoutSeconds = 300\n"
            "$ResultSchemaVersion = 2\n"
            "$MaxNuGetPackages = 64\n"
            + body
        )
        with tempfile.TemporaryDirectory(prefix="thirdlife-sandbox-profile-") as temporary:
            path = Path(temporary) / "probe.ps1"
            path.write_text(script, encoding="utf-8")
            return subprocess.run(
                [POWERSHELL, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(path)],
                capture_output=True, text=True, encoding="utf-8", timeout=30, check=False,
            )

    def dispatch(
        self, fail_label: str = "", *, task_id: str = STABILIZATION_OWNER,
        phase: str = "Full",
    ) -> dict:
        result = self.invoke_functions(
            "$calls = [Collections.Generic.List[object]]::new()\n"
            "function Invoke-RequiredCommand {\n"
            " param($Label, $FilePath, $Arguments, $Failure, $SuccessMarker)\n"
            " $calls.Add(@{label=$Label; executable=$FilePath; arguments=@($Arguments); failure=$Failure; marker=$SuccessMarker})\n"
            f" if ($Label -eq '{fail_label}') {{ throw 'Synthetic command failure' }}\n"
            "}\n"
            "$failed = $false\n"
            "try { Invoke-SharedVerifierProfile -DotnetExecutable 'dotnet.exe' -NuGetConfigPath 'staged.config' "
            f"-EvidenceTaskId '{task_id}' -SelectedPhase '{phase}' }}\n"
            "catch { $failed = $true }\n"
            "@{failed=$failed; calls=@($calls)} | ConvertTo-Json -Depth 8 -Compress\n"
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return json.loads(result.stdout)

    def test_profile_restores_staged_locked_packages_then_runs_shared_full(self) -> None:
        result = self.dispatch()
        self.assertFalse(result["failed"])
        restore, full = result["calls"]
        self.assertEqual(restore["label"], "locked-full-restore-and-audit")
        self.assertEqual(restore["arguments"], [
            "restore", "ThirdLife.sln", "--locked-mode", "--configfile", "staged.config", "--nologo",
            "-p:NuGetAudit=true", "-p:NuGetAuditMode=all",
        ])
        self.assertEqual(full["label"], "repository-stabilization-full")
        self.assertEqual(full["executable"], "powershell.exe")
        self.assertEqual(full["arguments"], [
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "eng\\verify.ps1", "-Tier", "Full", "-Restore",
        ])
        self.assertEqual(full["marker"], "OK: ThirdLife Full verification passed in ")
        self.assertEqual(full["failure"], "verification_failed")

    def test_restore_failure_prevents_shared_full_and_full_failure_propagates(self) -> None:
        for label, count in (("locked-full-restore-and-audit", 1), ("repository-stabilization-full", 2)):
            with self.subTest(label=label):
                result = self.dispatch(label)
                self.assertTrue(result["failed"])
                self.assertEqual(len(result["calls"]), count)

    def test_quick_restores_complete_locked_solution_with_audit_then_shared_quick(self) -> None:
        result = self.dispatch(task_id="TL-0107", phase="Quick")
        self.assertFalse(result["failed"])
        restore, quick = result["calls"]
        self.assertEqual(restore["label"], "locked-quick-restore-and-audit")
        self.assertEqual(restore["arguments"], [
            "restore", "ThirdLife.sln", "--locked-mode", "--configfile", "staged.config", "--nologo",
            "-p:NuGetAudit=true", "-p:NuGetAuditMode=all",
        ])
        self.assertEqual(quick["label"], "tl0107-shared-quick")
        self.assertEqual(quick["arguments"], [
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "eng\\verify.ps1", "-Tier", "Quick", "-Restore",
        ])
        self.assertEqual(quick["executable"], "powershell.exe")
        self.assertEqual(quick["marker"], "OK: ThirdLife Quick verification passed in ")
        self.assertEqual(quick["failure"], "verification_failed")

    def test_quick_restore_and_verifier_failures_propagate_without_later_commands(self) -> None:
        for label, count in (("locked-quick-restore-and-audit", 1), ("tl0107-shared-quick", 2)):
            with self.subTest(label=label):
                result = self.dispatch(label, task_id="TL-0107", phase="Quick")
                self.assertTrue(result["failed"])
                self.assertEqual(len(result["calls"]), count)

    def test_dispatch_rejects_unapproved_task_phase_pairs_before_restore(self) -> None:
        for task, phase in (
            ("TL-0107", "Full"), ("TL-0107", "Targeted"),
            ("TL-0104", "Quick"), (STABILIZATION_OWNER, "Quick"),
        ):
            with self.subTest(task=task, phase=phase):
                result = self.dispatch(task_id=task, phase=phase)
                self.assertTrue(result["failed"])
                self.assertEqual(result["calls"], [])

    def test_new_quick_stages_the_complete_solution_lock_closure(self) -> None:
        source_literal = "'" + str(REPOSITORY_ROOT).replace("'", "''") + "'"
        result = self.invoke_functions(
            f"$source = {source_literal}\n"
            "$quick = @(Get-LockedNuGetClosure -SourceRoot $source -SelectedTaskId 'TL-0107' -SelectedPhase 'Quick')\n"
            "$full = @(Get-LockedNuGetClosure -SourceRoot $source -SelectedTaskId $RepositoryStabilizationTaskId -SelectedPhase 'Full')\n"
            "$legacy = @(Get-LockedNuGetClosure -SourceRoot $source -SelectedTaskId 'TL-0104' -SelectedPhase 'Quick')\n"
            "@{quick=$quick; full=$full; legacy=$legacy} | ConvertTo-Json -Depth 5 -Compress\n",
            harness=HOST_HARNESS, function_names=("Get-LockedNuGetClosure",),
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        value = json.loads(result.stdout)
        expected = set()
        for lock_path in REPOSITORY_ROOT.rglob("packages.lock.json"):
            if {part.lower() for part in lock_path.relative_to(REPOSITORY_ROOT).parts} & {"bin", "obj", ".venv", ".git"}:
                continue
            lock = json.loads(lock_path.read_text(encoding="utf-8"))
            for framework in lock["dependencies"].values():
                for name, dependency in framework.items():
                    if dependency["type"] != "Project":
                        expected.add((name.lower(), dependency["resolved"].lower(), dependency["contentHash"]))
        actual = {(item["id"].lower(), item["version"].lower(), item["content_hash"]) for item in value["quick"]}
        self.assertGreater(len(expected), 0)
        self.assertEqual(actual, expected)
        self.assertEqual(value["quick"], value["full"])
        self.assertEqual(value["legacy"], [])

    def test_only_exact_quick_command_gets_the_bounded_quick_timeout(self) -> None:
        result = self.invoke_functions(
            "$values = @(\n"
            " Get-CommandTimeoutSeconds -Label 'tl0107-shared-quick' -EvidenceTaskId 'TL-0107' -SelectedPhase 'Quick'\n"
            " Get-CommandTimeoutSeconds -Label 'locked-quick-restore-and-audit' -EvidenceTaskId 'TL-0107' -SelectedPhase 'Quick'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0107-shared-quick' -EvidenceTaskId 'TL-0104' -SelectedPhase 'Quick'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0107-shared-quick' -EvidenceTaskId 'TL-0107' -SelectedPhase 'Full'\n"
            ")\nConvertTo-Json -InputObject $values -Compress\n"
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(json.loads(result.stdout), [180, 300, 300, 300])

    def test_quick_request_requires_exact_phase_audit_history_and_nonempty_bounded_closure(self) -> None:
        request = {
            "schema_version": 2, "task_id": "TL-0107",
            "phase": "Quick", "expected_head_commit": "a" * 40, "run_id": "b" * 32,
            "expected_tracked_clean": True, "source_history_included": True,
            "networking_enabled": True, "nuget_audit_enabled": True,
            "network_reason": "governed_nuget_audit", "nuget_package_count": 18,
            "sandbox_executable_version": "1.2.3.4",
        }
        for name in (
            "expected_source_digest", "history_bundle_sha256", "repository_status_sha256",
            "launcher_sha256", "runner_sha256", "sandbox_config_sha256", "nuget_closure_sha256",
            "python_packages_sha256",
        ):
            request[name] = "c" * 64
        altered = [
            dict(request, phase=phase)
            for phase in ("RoundTrip", "Integration", "Migration", "PathSecurity", "Interruption", "Targeted", "Full")
        ] + [
            dict(request, networking_enabled=False), dict(request, nuget_audit_enabled=False),
            dict(request, network_reason="disabled_offline_phase"), dict(request, source_history_included=False),
            dict(request, nuget_package_count=0), dict(request, nuget_package_count=65),
            dict(request, task_id="TL-0104"), dict(request, expected_source_digest="invalid"),
        ]
        valid = json.dumps(request).replace("'", "''")
        invalid = json.dumps(altered).replace("'", "''")
        result = self.invoke_functions(
            "$TaskId = 'TL-0107'\n"
            f"Assert-RunRequest -RunRequest ('{valid}' | ConvertFrom-Json)\n"
            f"$requests = '{invalid}' | ConvertFrom-Json\n"
            "$rejected = 0\n"
            "foreach ($request in $requests) {\n"
            " $failed = $false\n"
            " try { Assert-RunRequest -RunRequest $request } catch { $failed = $true }\n"
            " if (-not $failed) { throw 'Unsafe request accepted' }; $rejected++\n"
            "}\nWrite-Output $rejected\n"
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(int(result.stdout.strip()), len(altered))

    def test_only_exact_profile_full_command_gets_extended_timeout(self) -> None:
        result = self.invoke_functions(
            "$values = @(\n"
            " Get-CommandTimeoutSeconds -Label 'repository-stabilization-full' -EvidenceTaskId $RepositoryStabilizationTaskId -SelectedPhase 'Full'\n"
            " Get-CommandTimeoutSeconds -Label 'locked-full-restore-and-audit' -EvidenceTaskId $RepositoryStabilizationTaskId -SelectedPhase 'Full'\n"
            " Get-CommandTimeoutSeconds -Label 'repository-stabilization-full' -EvidenceTaskId 'TL-0102' -SelectedPhase 'Full'\n"
            " Get-CommandTimeoutSeconds -Label 'repository-stabilization-full' -EvidenceTaskId $RepositoryStabilizationTaskId -SelectedPhase 'Quick'\n"
            ")\nConvertTo-Json -InputObject $values -Compress\n"
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(json.loads(result.stdout), [1200, 300, 300, 300])
        host = HOST_HARNESS.read_text(encoding="utf-8")
        guest = GUEST_HARNESS.read_text(encoding="utf-8")
        self.assertIn("$SandboxTimeoutMinutes = 30", host)
        self.assertIn("Get-CommandTimeoutSeconds -Label $Label -EvidenceTaskId $TaskId -SelectedPhase $phase", guest)

    def test_guest_admits_only_full_for_the_stabilization_owner(self) -> None:
        request = {
            "schema_version": 2, "task_id": STABILIZATION_OWNER,
            "phase": "Full", "expected_head_commit": "a" * 40, "run_id": "b" * 32,
            "expected_tracked_clean": True, "source_history_included": True,
            "networking_enabled": True, "nuget_audit_enabled": True,
            "network_reason": "governed_nuget_audit", "nuget_package_count": 18,
            "sandbox_executable_version": "1.2.3.4",
        }
        for name in (
            "expected_source_digest", "history_bundle_sha256", "repository_status_sha256",
            "launcher_sha256", "runner_sha256", "sandbox_config_sha256", "nuget_closure_sha256",
            "python_packages_sha256",
        ):
            request[name] = "c" * 64
        body = json.dumps(request).replace("'", "''")
        result = self.invoke_functions(
            f"$request = '{body}' | ConvertFrom-Json\n"
            "$TaskId = $RepositoryStabilizationTaskId\n"
            "Assert-RunRequest -RunRequest $request\n"
            "$rejected = @()\n"
            "foreach ($phase in @('RoundTrip','Integration','Migration','PathSecurity','Interruption','Targeted','Quick')) {\n"
            " $request.phase = $phase\n"
            " try { Assert-RunRequest -RunRequest $request; throw 'Unexpected acceptance' }\n"
            " catch { if ($_.Exception.Message -ne 'The repository stabilization run request requires the shared Full profile.') { throw }; $rejected += $phase }\n"
            "}\nConvertTo-Json -InputObject $rejected -Compress\n"
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(len(json.loads(result.stdout)), 7)
        self.assertIn(
            'if ($TaskId -eq $RepositoryStabilizationTaskId -and $Phase -ne "Full")',
            HOST_HARNESS.read_text(encoding="utf-8"),
        )


if __name__ == "__main__":
    unittest.main()
