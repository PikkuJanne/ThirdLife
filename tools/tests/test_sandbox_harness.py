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
TL0109_NATIVE_METHODS = (
    "ThirdLife.Inventory.Tests.BatteryReportWorkspaceTests.ActiveMachineCancellationTerminatesTheOwnedJobAndVerifiesCleanup",
    "ThirdLife.Inventory.Tests.BatteryReportWorkspaceTests.ActiveMachineProductionSourceHasBoundedSanitizedMetricsAndVerifiedCleanup",
    "ThirdLife.Inventory.Tests.BatteryReportProviderTests.SandboxBatteryCollectionRecordsOnlyAvailabilityAndBoundedMetrics",
)
TL0109_NATIVE_STEP = {
    "label": "tl0109-native-cleanup",
    "test_project": "tests/ThirdLife.Inventory.Tests/ThirdLife.Inventory.Tests.csproj",
    "filter": "|".join(f"FullyQualifiedName={method}" for method in TL0109_NATIVE_METHODS),
    "success_marker": "Test Run Successful.",
    "required_success_markers": [f"Passed {method} [" for method in TL0109_NATIVE_METHODS],
    "command_timeout_seconds": 120,
    "evidence_classification": "same_machine_sandbox_native_cleanup",
}


class SandboxHarnessContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.host = HOST_HARNESS.read_text(encoding="utf-8")
        cls.guest = GUEST_HARNESS.read_text(encoding="utf-8")

    def test_tl0104_stages_only_its_diagnostics_working_inputs(self):
        self.assertIn(
            '[ValidateSet("TL-0102", "TL-0103", "TL-0104", "TL-0107", "TL-0109", "TL-0111", '
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

    def test_git_config_guard_spans_guest_commands_without_device_path_fallback(self):
        self.assertNotIn('$env:GIT_CONFIG_GLOBAL = "NUL"', self.guest)
        self.assertIn('$gitConfigurationGuard = $null', self.guest)
        self.assertLess(
            self.guest.index('$gitConfigurationGuard = Open-IsolatedGitConfiguration -StatePath $stateDirectory'),
            self.guest.index('Invoke-BoundedCommand -Label "git-version"'),
        )
        self.assertIn(
            'finally {\n        if ($null -ne $gitConfigurationGuard) { $gitConfigurationGuard.Dispose() }',
            self.guest,
        )

    def test_shared_quick_is_separate_from_legacy_quick_and_retains_sandbox_bounds(self):
        self.assertIn(
            'if ($TaskId -eq "TL-0107" -and $Phase -ne "Quick")', self.host
        )
        self.assertLess(
            self.guest.index('if ($TaskId -in @($RepositoryStabilizationTaskId, "TL-0107", "TL-0109", "TL-0111"))'),
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
            [f"tests/ThirdLife.{name}.Tests/ThirdLife.{name}.Tests.csproj" for name in ("Core", "Inventory", "UI")],
        )
        self.assertTrue(all(
            project["filter"] == "Category!=ActiveMachine&Category!=Extended&Category!=SandboxOnly"
            for project in manifest["tiers"]["quick"]["test_projects"]
        ))
        self.assertIn(f'$SandboxMemoryMb = {profile["memory_mb"]}', self.host)
        self.assertIn(f'$SandboxMemoryMb = {profile["memory_mb"]}', self.guest)
        self.assertIn(f'$SandboxTimeoutMinutes = {profile["host_timeout_minutes"]}', self.host)

    def test_tl0109_profiles_bind_selection_bootstrap_and_task_working_inputs(self):
        manifest = json.loads((REPOSITORY_ROOT / "eng/test-manifest.json").read_text(encoding="utf-8"))
        quick = manifest["sandbox_profiles"]["tl0109_quick"]
        historical = dict(manifest["sandbox_profiles"]["tl0107_quick"], task_id="TL-0109")
        self.assertEqual(quick, historical)
        targeted = manifest["sandbox_profiles"]["tl0109_targeted"]
        self.assertEqual(targeted, {
            "task_id": "TL-0109", "tier": "Targeted",
            "test_projects": [f"tests/ThirdLife.{name}.Tests/ThirdLife.{name}.Tests.csproj" for name in ("Inventory", "UI")],
            "filter_ref": "tiers.targeted.default_filter",
            "includes_quick_ref": "tiers.targeted.includes_quick",
            "required_native_step": TL0109_NATIVE_STEP,
            "hosted_environment": "active_machine_windows_sandbox",
            "risk_trigger": "host_application_control_blocks_required_targeted",
            "target_seconds": 600, "command_timeout_seconds": 600,
            "host_timeout_minutes": 30, "memory_mb": 4096,
            "network_reason": "governed_nuget_audit",
        })
        self.assertTrue(all((REPOSITORY_ROOT / project).is_file() for project in targeted["test_projects"]))
        for field, expected in (
            ("filter_ref", "Category!=ActiveMachine&Category!=Extended&Category!=SandboxOnly"),
            ("includes_quick_ref", True),
        ):
            resolved = manifest
            for key in targeted[field].split("."):
                resolved = resolved[key]
            self.assertEqual(resolved, expected)
        self.assertEqual(targeted["target_seconds"], manifest["tiers"]["targeted"]["target_seconds"])
        self.assertIn('if ($TaskId -eq "TL-0109" -and $Phase -notin @("Quick", "Targeted"))', self.host)
        self.assertIn('-Required:($Phase -in @("Quick", "Full") -or $TaskId -eq "TL-0109" -or $TaskId -eq "TL-0111")', self.host)
        self.assertIn('$sourceHistoryIncluded = $Phase -in @("Quick", "Full") -or $TaskId -eq "TL-0109"', self.host)
        self.assertIn('if ($phase -in @("Quick", "Full") -or $TaskId -eq "TL-0109" -or $TaskId -eq "TL-0111")', self.guest)
        self.assertIn('$RunRequest.phase -notin @("Quick", "Full") -and $RunRequest.task_id -ne "TL-0109"', self.guest)
        self.assertLess(
            self.guest.index('if ($TaskId -in @($RepositoryStabilizationTaskId, "TL-0107", "TL-0109", "TL-0111"))'),
            self.guest.index('elseif ($phase -in @("RoundTrip", "Integration", "Migration", "PathSecurity", "Interruption", "Targeted"))'),
        )
        selection = re.search(r'if \(\$TaskId -eq "TL-0109"\) \{\s*\$taskWorkingPaths = @\((.*?)\)\s*\}', self.host, re.DOTALL)
        self.assertIsNotNone(selection)
        self.assertEqual(re.findall(r'"([^"]+)"', selection.group(1)), [
            "src/ThirdLife.Inventory/Windows", "tests/ThirdLife.Inventory.Tests",
            "src/ThirdLife.UI", "tests/ThirdLife.UI.Tests",
        ])


    def test_tl0111_profiles_keep_bounds_and_stage_only_inventory_working_inputs(self):
        manifest = json.loads((REPOSITORY_ROOT / "eng/test-manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(manifest["sandbox_profiles"]["tl0111_quick"],
                         dict(manifest["sandbox_profiles"]["tl0109_quick"], task_id="TL-0111"))
        targeted = dict(manifest["sandbox_profiles"]["tl0109_targeted"], task_id="TL-0111")
        del targeted["required_native_step"]
        self.assertEqual(manifest["sandbox_profiles"]["tl0111_targeted"], targeted)
        selection = re.search(r'if \(\$TaskId -eq "TL-0111"\) \{\s*\$taskWorkingPaths = @\((.*?)\)\s*\}', self.host, re.DOTALL)
        self.assertIsNotNone(selection)
        self.assertEqual(re.findall(r'"([^"]+)"', selection.group(1)), [
            "src/ThirdLife.Inventory/Windows", "tests/ThirdLife.Inventory.Tests",
        ])
        self.assertIn('if ($TaskId -eq "TL-0111" -and $Phase -notin @("Quick", "Targeted"))', self.host)
        self.assertIn('-Required:($Phase -in @("Quick", "Full") -or $TaskId -eq "TL-0109" -or $TaskId -eq "TL-0111")', self.host)
        self.assertIn('$sourceHistoryIncluded = $Phase -in @("Quick", "Full") -or $TaskId -eq "TL-0109" -or $TaskId -eq "TL-0111"', self.host)
        self.assertIn('if ($PersistEvidence) {\n        if (-not $trackedState.clean)', self.host)


@unittest.skipUnless(os.name == "nt" and POWERSHELL, "Windows PowerShell is required")
class RepositorySandboxProfileTests(unittest.TestCase):
    def invoke_functions(
        self, body: str, *, harness: Path = GUEST_HARNESS,
        function_names: tuple[str, ...] = (
            "Invoke-SharedVerifierProfile", "Get-CommandTimeoutSeconds",
            "Assert-RunRequest", "Assert-ExactObjectKeys",
        ),
    ) -> subprocess.CompletedProcess[str]:
        # Parse and load only selected test functions; never execute
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

    @unittest.skipUnless(shutil.which("git"), "Git is required")
    def test_git_config_is_empty_locked_and_ignores_external_configuration(self) -> None:
        with tempfile.TemporaryDirectory(prefix="thirdlife-git-isolation-") as temporary:
            root = Path(temporary)
            (root / "state").mkdir()
            (root / "xdg/git").mkdir(parents=True)
            for path in (root / "system.config", root / "global.config", root / "xdg/git/config"):
                path.write_text("[thirdlife]\nunsafe = inherited\n", encoding="utf-8")
            root_literal = "'" + str(root).replace("'", "''") + "'"
            git_literal = "'" + shutil.which("git").replace("'", "''") + "'"
            result = self.invoke_functions(
                f"$fixture = {root_literal}\n$git = {git_literal}\n"
                "$env:GIT_CONFIG_SYSTEM = Join-Path $fixture 'system.config'\n"
                "$env:GIT_CONFIG_GLOBAL = Join-Path $fixture 'global.config'\n"
                "$env:XDG_CONFIG_HOME = Join-Path $fixture 'xdg'\n"
                "$guard = Open-IsolatedGitConfiguration -StatePath (Join-Path $fixture 'state')\n"
                "try {\n"
                " if ($env:GIT_CONFIG_NOSYSTEM -ne '1' -or $env:GIT_TERMINAL_PROMPT -ne '0') { throw 'Isolation disabled' }\n"
                " $expected = Join-Path $fixture 'state/git-global-empty.config'\n"
                " if ($env:GIT_CONFIG_GLOBAL -ne $expected -or (Get-Item -LiteralPath $expected).Length -ne 0) { throw 'Unexpected config' }\n"
                " $version = & $git --version\n"
                " if ($LASTEXITCODE -ne 0 -or $version -notmatch '^git version ') { throw 'Git preflight failed' }\n"
                " $global = @(& $git config --global --list)\n"
                " if ($LASTEXITCODE -ne 0 -or $global.Count -ne 0) { throw 'Global config not empty' }\n"
                " $unsafe = @(& $git -C $fixture config --get thirdlife.unsafe)\n"
                " if ($LASTEXITCODE -ne 1 -or $unsafe.Count -ne 0) { throw 'External config was read' }\n"
                " $writeDenied = $false; $deleteDenied = $false; $duplicateDenied = $false\n"
                " try { [IO.File]::WriteAllText($expected, 'unsafe') } catch [IO.IOException] { $writeDenied = $true }\n"
                " try { [IO.File]::Delete($expected) } catch [IO.IOException] { $deleteDenied = $true }\n"
                " try { $duplicate = Open-IsolatedGitConfiguration -StatePath (Join-Path $fixture 'state'); $duplicate.Dispose() } catch [IO.IOException] { $duplicateDenied = $true }\n"
                " if (-not $writeDenied -or -not $deleteDenied -or -not $duplicateDenied) { throw 'Config protection failed' }\n"
                " if ((Get-Item -LiteralPath $expected).Length -ne 0) { throw 'Config was modified' }\n"
                "} finally { $guard.Dispose() }\n"
                "[IO.File]::Delete($expected)\n"
                "if (Test-Path -LiteralPath $expected) { throw 'Config guard leaked' }\n"
                "Write-Output 'isolated'\n",
                function_names=("Open-IsolatedGitConfiguration",),
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(result.stdout.strip(), "isolated")

    def test_git_config_rejects_existing_content_without_truncation_or_fallback(self) -> None:
        with tempfile.TemporaryDirectory(prefix="thirdlife-git-existing-") as temporary:
            root = Path(temporary)
            config = root / "git-global-empty.config"
            config.write_bytes(b"synthetic-existing-config")
            root_literal = "'" + str(root).replace("'", "''") + "'"
            result = self.invoke_functions(
                "$env:GIT_CONFIG_GLOBAL = 'synthetic-existing-binding'\n"
                "$rejected = $false\n"
                f"try {{ $guard = Open-IsolatedGitConfiguration -StatePath {root_literal}; $guard.Dispose() }}\n"
                "catch [IO.IOException] { $rejected = $true }\n"
                "if (-not $rejected -or $env:GIT_CONFIG_GLOBAL -ne 'synthetic-existing-binding') { throw 'Existing config accepted' }\n"
                "Write-Output 'rejected'\n",
                function_names=("Open-IsolatedGitConfiguration",),
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(result.stdout.strip(), "rejected")
            self.assertEqual(config.read_bytes(), b"synthetic-existing-config")

    def dispatch(
        self, fail_label: str = "", *, task_id: str = STABILIZATION_OWNER,
        phase: str = "Full",
    ) -> dict:
        result = self.invoke_functions(
            "$calls = [Collections.Generic.List[object]]::new()\n"
            "function Invoke-RequiredCommand {\n"
            " param($Label, $FilePath, $Arguments, $Failure, $SuccessMarker, $RequiredSuccessMarkers)\n"
            " $calls.Add(@{label=$Label; executable=$FilePath; arguments=@($Arguments); failure=$Failure; marker=$SuccessMarker; required_markers=@($RequiredSuccessMarkers)})\n"
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

    def test_tl0109_profiles_restore_with_audit_and_dispatch_exact_shared_tiers(self) -> None:
        for phase in ("Quick", "Targeted"):
            with self.subTest(phase=phase):
                result = self.dispatch(task_id="TL-0109", phase=phase)
                self.assertFalse(result["failed"])
                restore, verification = result["calls"][:2]
                self.assertEqual(restore["label"], f"locked-{phase.lower()}-restore-and-audit")
                self.assertEqual(restore["arguments"], [
                    "restore", "ThirdLife.sln", "--locked-mode", "--configfile", "staged.config", "--nologo",
                    "-p:NuGetAudit=true", "-p:NuGetAuditMode=all",
                ])
                self.assertEqual(verification["label"], f"tl0109-shared-{phase.lower()}")
                self.assertEqual(verification["executable"], "powershell.exe")
                self.assertEqual(verification["marker"], f"OK: ThirdLife {phase} verification passed in ")
                self.assertEqual(verification["failure"], "verification_failed")
                if phase == "Quick":
                    self.assertEqual(len(result["calls"]), 2)
                    self.assertEqual(verification["arguments"], [
                        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "eng\\verify.ps1", "-Tier", "Quick", "-Restore",
                    ])
                else:
                    self.assertEqual(len(result["calls"]), 3)
                    self.assertEqual(verification["arguments"], [
                        "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
                        "& '.\\eng\\verify.ps1' -Tier Targeted -TestProject @('tests\\ThirdLife.Inventory.Tests\\ThirdLife.Inventory.Tests.csproj', 'tests\\ThirdLife.UI.Tests\\ThirdLife.UI.Tests.csproj') -Restore",
                    ])
                    native = result["calls"][2]
                    self.assertEqual(native["label"], TL0109_NATIVE_STEP["label"])
                    self.assertEqual(native["executable"], "dotnet.exe")
                    self.assertEqual(native["arguments"], [
                        "test", TL0109_NATIVE_STEP["test_project"].replace("/", "\\"),
                        "--configuration", "Release", "--no-build", "--no-restore", "--nologo",
                        "--filter", TL0109_NATIVE_STEP["filter"], "--logger", "console;verbosity=normal",
                    ])
                    self.assertEqual(native["failure"], "test_failed")
                    self.assertEqual(native["marker"], TL0109_NATIVE_STEP["success_marker"])
                    self.assertEqual(native["required_markers"], TL0109_NATIVE_STEP["required_success_markers"])

    def test_tl0109_targeted_expression_preserves_array_binding_and_exit_status(self) -> None:
        dispatched = self.dispatch(task_id="TL-0109", phase="Targeted")["calls"][1]
        with tempfile.TemporaryDirectory(prefix="thirdlife-verifier-binding-") as temporary:
            root = Path(temporary)
            (root / "eng").mkdir()
            verifier = root / "eng/verify.ps1"
            verifier.write_text(
                "param([string] $Tier, [string[]] $TestProject, [switch] $Restore)\n"
                "@{tier=$Tier; projects=@($TestProject); restore=[bool]$Restore} | ConvertTo-Json -Compress\n",
                encoding="utf-8",
            )
            result = subprocess.run(
                [POWERSHELL, *dispatched["arguments"]], cwd=root,
                capture_output=True, text=True, encoding="utf-8", timeout=30, check=False,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(json.loads(result.stdout), {
                "tier": "Targeted", "restore": True,
                "projects": [f"tests\\ThirdLife.{name}.Tests\\ThirdLife.{name}.Tests.csproj" for name in ("Inventory", "UI")],
            })
            verifier.write_text("throw 'Synthetic verification failure'\n", encoding="utf-8")
            failed = subprocess.run(
                [POWERSHELL, *dispatched["arguments"]], cwd=root,
                capture_output=True, text=True, encoding="utf-8", timeout=30, check=False,
            )
            self.assertNotEqual(failed.returncode, 0)

    def test_tl0109_profiles_stop_on_restore_or_verification_failure(self) -> None:
        for phase in ("Quick", "Targeted"):
            failures = [(f"locked-{phase.lower()}-restore-and-audit", 1), (f"tl0109-shared-{phase.lower()}", 2)]
            if phase == "Targeted":
                failures.append(("tl0109-native-cleanup", 3))
            for label, count in failures:
                with self.subTest(phase=phase, label=label):
                    result = self.dispatch(label, task_id="TL-0109", phase=phase)
                    self.assertTrue(result["failed"])
                    self.assertEqual(len(result["calls"]), count)

    def test_tl0109_timeout_is_scoped_to_exact_task_tier_and_verifier_label(self) -> None:
        result = self.invoke_functions(
            "$values = @(\n"
            " Get-CommandTimeoutSeconds -Label 'tl0109-shared-quick' -EvidenceTaskId 'TL-0109' -SelectedPhase 'Quick'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0109-shared-targeted' -EvidenceTaskId 'TL-0109' -SelectedPhase 'Targeted'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0109-shared-targeted' -EvidenceTaskId 'TL-0107' -SelectedPhase 'Targeted'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0109-shared-targeted' -EvidenceTaskId 'TL-0109' -SelectedPhase 'Quick'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0109-shared-quick' -EvidenceTaskId 'TL-0109' -SelectedPhase 'Targeted'\n"
            " Get-CommandTimeoutSeconds -Label 'locked-targeted-restore-and-audit' -EvidenceTaskId 'TL-0109' -SelectedPhase 'Targeted'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0109-native-cleanup' -EvidenceTaskId 'TL-0109' -SelectedPhase 'Targeted'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0109-native-cleanup' -EvidenceTaskId 'TL-0109' -SelectedPhase 'Quick'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0109-native-cleanup' -EvidenceTaskId 'TL-0107' -SelectedPhase 'Targeted'\n"
            ")\nConvertTo-Json -InputObject $values -Compress\n"
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(json.loads(result.stdout), [180, 600, 300, 300, 300, 300, 120, 300, 300])

    def test_native_command_requires_every_pass_marker_and_propagates_failure(self) -> None:
        required = json.dumps(TL0109_NATIVE_STEP["required_success_markers"])
        result = self.invoke_functions(
            f"$markers = '{required}' | ConvertFrom-Json\n"
            "$script:failureCode = 'none'; $script:exitCode = 0\n"
            "function Invoke-BoundedCommand { param($Label, $FilePath, $Arguments, $SuccessMarker); return $script:command }\n"
            "$results = @()\n"
            "foreach ($case in @('success', 'missing_first', 'missing_second', 'missing_provider', 'missing_summary', 'exit_failure', 'timeout', 'output_limit')) {\n"
            " $script:command = [pscustomobject]@{exit_code=0; timed_out=$false; output_exceeded=$false; marker_found=$true; tail_text=($markers -join [Environment]::NewLine)}\n"
            " switch ($case) {\n"
            "  'missing_first' { $script:command.tail_text = $markers[1,2] -join [Environment]::NewLine }\n"
            "  'missing_second' { $script:command.tail_text = $markers[0,2] -join [Environment]::NewLine }\n"
            "  'missing_provider' { $script:command.tail_text = $markers[0,1] -join [Environment]::NewLine }\n"
            "  'missing_summary' { $script:command.marker_found = $false }\n"
            "  'exit_failure' { $script:command.exit_code = 1 }\n"
            "  'timeout' { $script:command.timed_out = $true }\n"
            "  'output_limit' { $script:command.output_exceeded = $true }\n"
            " }\n"
            " $script:failureCode = 'none'; $failed = $false\n"
            " try { Invoke-RequiredCommand -Label 'tl0109-native-cleanup' -FilePath 'synthetic' -Arguments @('synthetic') -Failure 'test_failed' -SuccessMarker 'Test Run Successful.' -RequiredSuccessMarkers $markers } catch { $failed = $true }\n"
            " $results += @{case=$case; failed=$failed; code=$script:failureCode}\n"
            "}\nConvertTo-Json -InputObject $results -Compress\n",
            function_names=("Invoke-RequiredCommand",),
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(json.loads(result.stdout), [
            {"case": "success", "failed": False, "code": "none"},
            *({"case": case, "failed": True, "code": "success_marker_missing"} for case in (
                "missing_first", "missing_second", "missing_provider", "missing_summary",
            )),
            {"case": "exit_failure", "failed": True, "code": "test_failed"},
            {"case": "timeout", "failed": True, "code": "command_timeout"},
            {"case": "output_limit", "failed": True, "code": "output_limit_exceeded"},
        ])

    def test_tl0109_requests_require_exact_tiers_audit_history_and_bounded_closure(self) -> None:
        request = {
            "schema_version": 2, "task_id": "TL-0109",
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
        valid = [dict(request, phase=phase) for phase in ("Quick", "Targeted")]
        altered = [dict(request, phase=phase) for phase in (
            "RoundTrip", "Integration", "Migration", "PathSecurity", "Interruption", "Full",
        )]
        for approved in valid:
            altered += [dict(approved, **change) for change in (
                {"networking_enabled": False}, {"nuget_audit_enabled": False},
                {"network_reason": "disabled_offline_phase"}, {"source_history_included": False},
                {"nuget_package_count": 0}, {"nuget_package_count": 65}, {"nuget_package_count": True},
                {"task_id": "TL-0107"}, {"expected_source_digest": "invalid"},
                {"filter": "Category=ActiveMachine"}, {"test_projects": ["unapproved.csproj"]},
            )]
        valid_json = json.dumps(valid).replace("'", "''")
        invalid_json = json.dumps(altered).replace("'", "''")
        result = self.invoke_functions(
            "$TaskId = 'TL-0109'\n"
            f"foreach ($request in ('{valid_json}' | ConvertFrom-Json)) {{ Assert-RunRequest -RunRequest $request }}\n"
            f"$requests = '{invalid_json}' | ConvertFrom-Json\n"
            "$rejected = 0\n"
            "foreach ($request in $requests) {\n"
            " $failed = $false\n"
            " try { Assert-RunRequest -RunRequest $request } catch { $failed = $true }\n"
            " if (-not $failed) { throw 'Unsafe request accepted' }; $rejected++\n"
            "}\nWrite-Output $rejected\n"
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(int(result.stdout.strip()), len(altered))

    def test_tl0111_dispatch_runs_only_restore_and_shared_verification(self) -> None:
        for phase in ("Quick", "Targeted"):
            with self.subTest(phase=phase):
                result = self.dispatch(task_id="TL-0111", phase=phase)
                self.assertFalse(result["failed"])
                self.assertEqual(len(result["calls"]), 2)
                historical = self.dispatch(task_id="TL-0109", phase=phase)["calls"][:2]
                historical[1]["label"] = f"tl0111-shared-{phase.lower()}"
                self.assertEqual(result["calls"], historical)
                for label, count in ((f"locked-{phase.lower()}-restore-and-audit", 1),
                                     (f"tl0111-shared-{phase.lower()}", 2)):
                    failed = self.dispatch(label, task_id="TL-0111", phase=phase)
                    self.assertTrue(failed["failed"])
                    self.assertEqual(len(failed["calls"]), count)

    def test_tl0111_timeouts_apply_only_to_the_exact_task_phase_and_label(self) -> None:
        result = self.invoke_functions(
            "$values = @(\n"
            " Get-CommandTimeoutSeconds -Label 'tl0111-shared-quick' -EvidenceTaskId 'TL-0111' -SelectedPhase 'Quick'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0111-shared-targeted' -EvidenceTaskId 'TL-0111' -SelectedPhase 'Targeted'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0111-shared-targeted' -EvidenceTaskId 'TL-0109' -SelectedPhase 'Targeted'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0111-shared-targeted' -EvidenceTaskId 'TL-0111' -SelectedPhase 'Quick'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0111-shared-quick' -EvidenceTaskId 'TL-0111' -SelectedPhase 'Targeted'\n"
            " Get-CommandTimeoutSeconds -Label 'locked-targeted-restore-and-audit' -EvidenceTaskId 'TL-0111' -SelectedPhase 'Targeted'\n"
            " Get-CommandTimeoutSeconds -Label 'tl0109-native-cleanup' -EvidenceTaskId 'TL-0111' -SelectedPhase 'Targeted'\n"
            ")\nConvertTo-Json -InputObject $values -Compress\n"
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(json.loads(result.stdout), [180, 600, 300, 300, 300, 300, 300])

    def test_tl0111_transient_requests_allow_dirty_source_and_preserve_all_binding_guards(self) -> None:
        request = {
            "schema_version": 2, "task_id": "TL-0111", "phase": "Targeted",
            "expected_head_commit": "a" * 40, "run_id": "b" * 32,
            "expected_tracked_clean": False, "source_history_included": True,
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
        valid = [dict(request, phase=phase, expected_tracked_clean=clean)
                 for phase in ("Quick", "Targeted") for clean in (False, True)]
        altered = [dict(request, phase=phase) for phase in (
            "RoundTrip", "Integration", "Migration", "PathSecurity", "Interruption", "Full",
        )]
        for approved in valid:
            altered += [dict(approved, **change) for change in (
                {"networking_enabled": False}, {"nuget_audit_enabled": False},
                {"network_reason": "disabled_offline_phase"}, {"source_history_included": False},
                {"nuget_package_count": 0}, {"nuget_package_count": 65}, {"nuget_package_count": True},
                {"task_id": "TL-0109"}, {"expected_source_digest": "invalid"},
                {"filter": "Category=ActiveMachine"}, {"test_projects": ["unapproved.csproj"]},
            )]
        valid_json = json.dumps(valid).replace("'", "''")
        invalid_json = json.dumps(altered).replace("'", "''")
        result = self.invoke_functions(
            "$TaskId = 'TL-0111'\n"
            f"foreach ($request in ('{valid_json}' | ConvertFrom-Json)) {{ Assert-RunRequest -RunRequest $request }}\n"
            f"$requests = '{invalid_json}' | ConvertFrom-Json\n"
            "$rejected = 0\n"
            "foreach ($request in $requests) {\n"
            " $failed = $false\n"
            " try { Assert-RunRequest -RunRequest $request } catch { $failed = $true }\n"
            " if (-not $failed) { throw 'Unsafe request accepted' }; $rejected++\n"
            "}\nWrite-Output $rejected\n"
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(int(result.stdout.strip()), len(altered))

    def test_dispatch_rejects_unapproved_task_phase_pairs_before_restore(self) -> None:
        for task, phase in (
            ("TL-0107", "Full"), ("TL-0107", "Targeted"),
            ("TL-0104", "Quick"), (STABILIZATION_OWNER, "Quick"),
            *(("TL-0109", phase) for phase in ("Full", "RoundTrip", "Integration", "Migration", "PathSecurity", "Interruption")),
            *(("TL-0111", phase) for phase in ("Full", "RoundTrip", "Integration", "Migration", "PathSecurity", "Interruption")),
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
            "$batteryQuick = @(Get-LockedNuGetClosure -SourceRoot $source -SelectedTaskId 'TL-0109' -SelectedPhase 'Quick')\n"
            "$batteryTargeted = @(Get-LockedNuGetClosure -SourceRoot $source -SelectedTaskId 'TL-0109' -SelectedPhase 'Targeted')\n"
            "$devicesQuick = @(Get-LockedNuGetClosure -SourceRoot $source -SelectedTaskId 'TL-0111' -SelectedPhase 'Quick')\n"
            "$devicesTargeted = @(Get-LockedNuGetClosure -SourceRoot $source -SelectedTaskId 'TL-0111' -SelectedPhase 'Targeted')\n"
            "$full = @(Get-LockedNuGetClosure -SourceRoot $source -SelectedTaskId $RepositoryStabilizationTaskId -SelectedPhase 'Full')\n"
            "$legacy = @(Get-LockedNuGetClosure -SourceRoot $source -SelectedTaskId 'TL-0104' -SelectedPhase 'Quick')\n"
            "@{quick=$quick; batteryQuick=$batteryQuick; batteryTargeted=$batteryTargeted; devicesQuick=$devicesQuick; devicesTargeted=$devicesTargeted; full=$full; legacy=$legacy} | ConvertTo-Json -Depth 5 -Compress\n",
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
        self.assertEqual(value["batteryQuick"], value["full"])
        self.assertEqual(value["batteryTargeted"], value["full"])
        self.assertEqual(value["devicesQuick"], value["full"])
        self.assertEqual(value["devicesTargeted"], value["full"])
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
