#!/usr/bin/env python3
"""Focused regressions for task-bound Windows Sandbox verification."""

from __future__ import annotations

import re
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
HOST_HARNESS = REPOSITORY_ROOT / "eng" / "run-tl0102-sandbox.ps1"
GUEST_HARNESS = REPOSITORY_ROOT / "eng" / "run-tl0102-sandbox-guest.ps1"


class SandboxHarnessContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.host = HOST_HARNESS.read_text(encoding="utf-8")
        cls.guest = GUEST_HARNESS.read_text(encoding="utf-8")

    def test_tl0104_stages_only_its_diagnostics_working_inputs(self):
        self.assertIn('[ValidateSet("TL-0102", "TL-0103", "TL-0104")]', self.host)
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


if __name__ == "__main__":
    unittest.main()
