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
            '[ValidateSet("TL-0102", "TL-0103", "TL-0104", '
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
        self.assertIn('$networkingEnabled = $Phase -eq "Full"', self.host)
        self.assertIn(
            'if ($sourceDigest -ne $request.expected_source_digest)', self.guest
        )
        self.assertIn('raw_output_retained = $false', self.guest)
        self.assertIn(
            'if ($RunRequest.task_id -eq "TL-0104" -and '
            '$RunRequest.phase -notin @("Targeted", "Quick", "Full"))',
            self.guest,
        )


@unittest.skipUnless(os.name == "nt" and POWERSHELL, "Windows PowerShell is required")
class RepositorySandboxProfileTests(unittest.TestCase):
    def invoke_functions(self, body: str) -> subprocess.CompletedProcess[str]:
        # Parse and load only pure validation/dispatch functions; never execute
        # guest top-level initialization, staging, or a Sandbox launch.
        harness_literal = "'" + str(GUEST_HARNESS).replace("'", "''") + "'"
        script = (
            "$ErrorActionPreference = 'Stop'\n"
            "$tokens = $null; $parseErrors = $null\n"
            f"$ast = [Management.Automation.Language.Parser]::ParseFile({harness_literal}, [ref]$tokens, [ref]$parseErrors)\n"
            "if ($parseErrors.Count -ne 0) { throw 'Harness parse failed' }\n"
            "$names = @('Invoke-RepositoryStabilizationFull', 'Get-CommandTimeoutSeconds', 'Assert-RunRequest', 'Assert-ExactObjectKeys')\n"
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

    def dispatch(self, fail_label: str = "") -> dict:
        result = self.invoke_functions(
            "$calls = [Collections.Generic.List[object]]::new()\n"
            "function Invoke-RequiredCommand {\n"
            " param($Label, $FilePath, $Arguments, $Failure, $SuccessMarker)\n"
            " $calls.Add(@{label=$Label; executable=$FilePath; arguments=@($Arguments); failure=$Failure; marker=$SuccessMarker})\n"
            f" if ($Label -eq '{fail_label}') {{ throw 'Synthetic command failure' }}\n"
            "}\n"
            "$failed = $false\n"
            "try { Invoke-RepositoryStabilizationFull -DotnetExecutable 'dotnet.exe' -NuGetConfigPath 'staged.config' }\n"
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
