"""Focused checks for the administrator-only broker provisioning code."""

from __future__ import annotations

import base64
import hashlib
import os
import struct
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
INSTALLER = ROOT / "eng" / "install-thirdlife-broker.ps1"
POWERSHELL = Path(os.environ.get("SystemRoot", "C:/Windows")) / (
    "System32/WindowsPowerShell/v1.0/powershell.exe"
)


def ps_literal(value: Path | str) -> str:
    return "'" + str(value).replace("'", "''") + "'"


def pe_image(flags: int = 0x800) -> bytes:
    data = bytearray(1024)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 0x80)
    struct.pack_into("<IHH", data, 0x80, 0x4550, 0x8664, 1)
    struct.pack_into("<HHH", data, 0x94, 240, 0x0002, 0x20B)
    optional = 0x98
    struct.pack_into("<I", data, optional + 108, 16)
    struct.pack_into("<II", data, optional + 192, 4096, 80)
    struct.pack_into("<III", data, optional + 240 + 12, 4096, 512, 512)
    struct.pack_into("<I", data, 512, 80)
    struct.pack_into("<H", data, 512 + 78, flags)
    return bytes(data)


@unittest.skipUnless(os.name == "nt" and POWERSHELL.is_file(), "Windows PowerShell required")
class BrokerDeploymentTests(unittest.TestCase):
    def run_powershell(self, code: str, *, helpers: bool = False) -> subprocess.CompletedProcess:
        if helpers:
            # Load only the definitions and declarations before production checks.
            # The test process never invokes the installation entry point.
            code = f"""
            $tokens = $null
            $errors = $null
            $ast = [Management.Automation.Language.Parser]::ParseFile(
                {ps_literal(INSTALLER)}, [ref]$tokens, [ref]$errors)
            if ($errors.Count -ne 0) {{ throw 'Invalid installer syntax.' }}
            foreach ($statement in $ast.EndBlock.Statements) {{
                if ($statement -is [Management.Automation.Language.IfStatementAst]) {{ break }}
                . ([ScriptBlock]::Create($statement.Extent.Text))
            }}
            {code}
            """
        program = f"""
        $ErrorActionPreference = 'Stop'
        try {{
            {code}
            exit 0
        }}
        catch {{
            [Console]::WriteLine($_.Exception.Message)
            exit 3
        }}
        """
        encoded = base64.b64encode(program.encode("utf-16-le")).decode("ascii")
        return subprocess.run(
            [str(POWERSHELL), "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            capture_output=True,
            text=True,
            timeout=20,
            check=False,
        )

    def assert_success(self, result: subprocess.CompletedProcess) -> None:
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def assert_refused(self, result: subprocess.CompletedProcess, reason: str) -> None:
        self.assertEqual(result.returncode, 3, result.stdout + result.stderr)
        self.assertIn(reason, result.stdout)

    def test_standard_volume_creation_right_does_not_extend_to_installation_ancestor(self):
        result = self.run_powershell(
            """
            $acl = New-BrokerObjectAcl $true $true
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
                [Security.Principal.SecurityIdentifier]::new('S-1-5-11'),
                [Security.AccessControl.FileSystemRights]::CreateDirectories,
                [Security.AccessControl.AccessControlType]::Allow))
            Assert-BrokerAncestorAcl $acl $true
            Assert-BrokerAncestorAcl $acl $false
            """,
            helpers=True,
        )
        self.assert_refused(result, "allows untrusted modification")

    def test_volume_root_child_delete_right_is_not_accepted(self):
        result = self.run_powershell(
            """
            $acl = New-BrokerObjectAcl $true $true
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
                $Users, [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles,
                [Security.AccessControl.AccessControlType]::Allow))
            Assert-BrokerAncestorAcl $acl $true
            """,
            helpers=True,
        )
        self.assert_refused(result, "allows untrusted modification")

    def test_staging_is_private_and_published_objects_grant_users_read_execute(self):
        result = self.run_powershell(
            """
            $privateAcl = New-BrokerObjectAcl $true $false
            $publishedAcl = New-BrokerObjectAcl $false $true
            foreach ($acl in @($privateAcl, $publishedAcl)) {
                if (-not $acl.AreAccessRulesProtected -or
                    $acl.GetOwner([Security.Principal.SecurityIdentifier]) -ne $Administrators) {
                    throw 'The provisioning object must be protected and administrator-owned.'
                }
            }
            $privateRules = $privateAcl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])
            $publishedRules = $publishedAcl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])
            if ($privateRules.Count -ne 2 -or $publishedRules.Count -ne 3) {
                throw 'The provisioning object has an unexpected access rule.'
            }
            foreach ($rule in $publishedRules) {
                if ($rule.IdentityReference -eq $Users -and
                    [int]$rule.FileSystemRights -ne 0x1200A9) {
                    throw 'The published broker must grant ordinary users read and execute only.'
                }
            }
            """,
            helpers=True,
        )
        self.assert_success(result)

    def test_staging_accepts_exact_bytes_with_system32_load_policy_and_refuses_overwrite(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "source.exe"
            stage = Path(directory) / "staged.exe"
            source.write_bytes(pe_image())
            pin = hashlib.sha256(source.read_bytes()).hexdigest()
            code = f"""
            $inputArtifact = [IO.File]::OpenRead({ps_literal(source)})
            try {{ Write-StagedBrokerArtifact $inputArtifact {ps_literal(stage)} {ps_literal(pin)} }}
            finally {{ $inputArtifact.Dispose() }}
            """
            result = self.run_powershell(code, helpers=True)
            self.assert_success(result)
            self.assertEqual(stage.read_bytes(), source.read_bytes())

            result = self.run_powershell(code, helpers=True)
            self.assertEqual(result.returncode, 3, result.stdout + result.stderr)
            self.assertEqual(stage.read_bytes(), source.read_bytes())

    def test_staging_rejects_mismatched_hash_even_with_the_required_pe_shape(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "source.exe"
            stage = Path(directory) / "staged.exe"
            source.write_bytes(pe_image())
            result = self.run_powershell(
                f"""
                $inputArtifact = [IO.File]::OpenRead({ps_literal(source)})
                try {{ Write-StagedBrokerArtifact $inputArtifact {ps_literal(stage)} ('0' * 64) }}
                finally {{ $inputArtifact.Dispose() }}
                """,
                helpers=True,
            )
            self.assert_refused(result, "differs from the independently reviewed value")

    def test_matching_hash_with_unsafe_static_load_flags_is_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "source.exe"
            stage = Path(directory) / "staged.exe"
            source.write_bytes(pe_image(flags=0))
            pin = hashlib.sha256(source.read_bytes()).hexdigest()
            result = self.run_powershell(
                f"""
                $inputArtifact = [IO.File]::OpenRead({ps_literal(source)})
                try {{ Write-StagedBrokerArtifact $inputArtifact {ps_literal(stage)} {ps_literal(pin)} }}
                finally {{ $inputArtifact.Dispose() }}
                """,
                helpers=True,
            )
            self.assert_refused(result, "must restrict static dependency loading to System32")


if __name__ == "__main__":
    unittest.main()
