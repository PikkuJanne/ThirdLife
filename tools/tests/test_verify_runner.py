#!/usr/bin/env python3
"""Exercise tier selection and failure propagation with isolated command spies."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
POWERSHELL = shutil.which("pwsh") or shutil.which("powershell.exe")
CORE = "tests/ThirdLife.Core.Tests/ThirdLife.Core.Tests.csproj"
INVENTORY = "tests/ThirdLife.Inventory.Tests/ThirdLife.Inventory.Tests.csproj"
DIAGNOSTICS = "tests/ThirdLife.Diagnostics.Tests/ThirdLife.Diagnostics.Tests.csproj"
PERSISTENCE = "tests/ThirdLife.Persistence.Tests/ThirdLife.Persistence.Tests.csproj"
DEFAULT_FILTER = "Category!=ActiveMachine&Category!=Extended&Category!=SandboxOnly"


def powershell_literal(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


@unittest.skipUnless(os.name == "nt" and POWERSHELL, "Windows PowerShell is required")
class VerificationRunnerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = tempfile.TemporaryDirectory(prefix="thirdlife-runner-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        (self.root / "eng").mkdir()
        for relative in ("eng/verify.ps1", "eng/test-manifest.json", "global.json"):
            shutil.copyfile(REPOSITORY_ROOT / relative, self.root / relative)
        for relative in (CORE, INVENTORY, DIAGNOSTICS, PERSISTENCE):
            target = self.root / relative
            target.parent.mkdir(parents=True)
            target.write_text("<Project />\n", encoding="utf-8")
        self.commands = self.root / "commands.jsonl"
        self.shims = self.root / "command-spies"
        self.shims.mkdir()
        for tool in ("dotnet", "python"):
            (self.shims / f"{tool}.ps1").write_text(
                "$ErrorActionPreference = 'Stop'\n"
                f"$tool = '{tool}'\n"
                "$record = @{ tool = $tool; arguments = @($args) }\n"
                "$record | ConvertTo-Json -Compress -Depth 5 | "
                "Add-Content -LiteralPath $env:VERIFY_RUNNER_LOG -Encoding utf8\n"
                "$global:LASTEXITCODE = 0\n"
                "if ($env:VERIFY_RUNNER_FAIL -eq ($tool + ':' + $args[0])) {\n"
                "    $global:LASTEXITCODE = 17\n"
                "    return\n"
                "}\n"
                "if ($tool -eq 'dotnet' -and $args[0] -eq '--version') {\n"
                "    Write-Output '10.0.400'\n"
                "}\n",
                encoding="utf-8",
            )

    def run_tier(
        self,
        tier: str = "Quick",
        *,
        projects: tuple[str, ...] = (),
        test_filter: str | None = None,
        restore: bool = False,
        supplementary: bool = False,
        failure: str = "",
    ) -> tuple[subprocess.CompletedProcess[str], list[dict]]:
        invocation = "& './eng/verify.ps1' -Tier " + powershell_literal(tier)
        if projects:
            invocation += " -TestProject @(" + ",".join(
                powershell_literal(project) for project in projects
            ) + ")"
        if test_filter is not None:
            invocation += " -Filter " + powershell_literal(test_filter)
        if restore:
            invocation += " -Restore"
        if supplementary:
            invocation += " -SupplementaryCi"
        script = self.root / "invoke.ps1"
        script.write_text(invocation + "\n", encoding="utf-8")
        environment = os.environ.copy()
        environment["PATH"] = str(self.shims) + os.pathsep + environment["PATH"]
        environment["VERIFY_RUNNER_LOG"] = str(self.commands)
        environment["VERIFY_RUNNER_FAIL"] = failure
        result = subprocess.run(
            [POWERSHELL, "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script)],
            cwd=self.root,
            env=environment,
            capture_output=True,
            text=True,
            encoding="utf-8",
            timeout=30,
            check=False,
        )
        records = (
            [json.loads(line) for line in self.commands.read_text(encoding="utf-8-sig").splitlines()]
            if self.commands.exists()
            else []
        )
        return result, records

    def assert_passed(self, result: subprocess.CompletedProcess[str]) -> None:
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("verification passed in", result.stdout)
        self.assertIn("Duration:", result.stdout)

    @staticmethod
    def selected_tests(records: list[dict]) -> list[list[str]]:
        return [record["arguments"] for record in records if record["tool"] == "dotnet" and record["arguments"][0] == "test"]

    def test_quick_runs_real_feedback_and_restores_only_when_requested(self) -> None:
        result, records = self.run_tier(restore=True)
        self.assert_passed(result)
        operations = [record["arguments"][0] for record in records]
        self.assertEqual(operations, ["--version", "-c", "tools/validate_bundle.py", "tools/validate_repository.py", "restore", "format", "build", "test", "test"])
        calls = self.selected_tests(records)
        self.assertEqual([call[1] for call in calls], [CORE, INVENTORY])
        for call in calls:
            self.assertEqual(call[call.index("--filter") + 1], DEFAULT_FILTER)
            self.assertEqual(call[-2:], ["--", "RunConfiguration.TreatNoTestsAsError=true"])
        build = next(record["arguments"] for record in records if record["arguments"][0] == "build")
        self.assertIn("--warnaserror", build)
        restore = next(record["arguments"] for record in records if record["arguments"][0] == "restore")
        self.assertIn("--locked-mode", restore)

    def test_targeted_keeps_quick_then_runs_each_array_selection(self) -> None:
        selected_filter = "FullyQualifiedName~Failure|FullyQualifiedName~Cancellation"
        result, records = self.run_tier("Targeted", projects=(DIAGNOSTICS, PERSISTENCE), test_filter=selected_filter)
        self.assert_passed(result)
        calls = self.selected_tests(records)
        self.assertEqual([call[1] for call in calls], [CORE, INVENTORY, DIAGNOSTICS, PERSISTENCE])
        self.assertEqual([call[call.index("--filter") + 1] for call in calls], [DEFAULT_FILTER, DEFAULT_FILTER, selected_filter, selected_filter])
        self.assertNotIn("restore", [record["arguments"][0] for record in records])

    def test_targeted_default_excludes_active_observation(self) -> None:
        result, records = self.run_tier("Targeted", projects=(INVENTORY,))
        self.assert_passed(result)
        self.assertEqual(self.selected_tests(records)[-1][-4:], ["--filter", DEFAULT_FILTER, "--", "RunConfiguration.TreatNoTestsAsError=true"])

    def test_active_observation_requires_explicit_targeted_filter(self) -> None:
        result, records = self.run_tier("Targeted", projects=(INVENTORY,), test_filter="Category=ActiveMachine")
        self.assert_passed(result)
        calls = self.selected_tests(records)
        self.assertEqual(calls[1][calls[1].index("--filter") + 1], DEFAULT_FILTER)
        self.assertEqual(calls[-1][calls[-1].index("--filter") + 1], "Category=ActiveMachine")

    def test_full_runs_governance_regressions_and_locked_supported_suite(self) -> None:
        result, records = self.run_tier("Full")
        self.assert_passed(result)
        regression = next(record["arguments"] for record in records if record["arguments"][0] == "-m")
        self.assertEqual(regression, ["-m", "unittest", "discover", "-s", "tools/tests", "-p", "test_*.py"])
        self.assertIn("restore", [record["arguments"][0] for record in records])
        calls = self.selected_tests(records)
        self.assertEqual(len(calls), 1)
        self.assertEqual(Path(calls[0][1]).name, "ThirdLife.sln")
        self.assertEqual(calls[0][calls[0].index("--filter") + 1], DEFAULT_FILTER)

    def test_failure_stops_the_run_and_never_prints_success(self) -> None:
        for failure in ("python:tools/validate_bundle.py", "dotnet:format", "dotnet:build", "dotnet:test"):
            with self.subTest(failure=failure):
                self.commands.unlink(missing_ok=True)
                result, records = self.run_tier(failure=failure)
                self.assertNotEqual(result.returncode, 0)
                self.assertNotIn("verification passed", result.stdout)
                self.assertIn("Duration:", result.stdout)
                last = records[-1]
                self.assertEqual(last["tool"] + ":" + last["arguments"][0], failure)

    def test_invalid_selection_fails_before_any_tool_runs(self) -> None:
        for arguments in (
            {"tier": "Targeted"},
            {"projects": (CORE,)},
            {"tier": "Targeted", "projects": ("missing.csproj",)},
            {"tier": "Full", "supplementary": True},
        ):
            with self.subTest(arguments=arguments):
                result, records = self.run_tier(**arguments)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(records, [])

    def test_supplementary_quick_classifies_evidence(self) -> None:
        result, records = self.run_tier(supplementary=True)
        self.assert_passed(result)
        self.assertIn("supplementary evidence context", result.stdout)
        self.assertEqual(len(self.selected_tests(records)), 2)


if __name__ == "__main__":
    unittest.main()
