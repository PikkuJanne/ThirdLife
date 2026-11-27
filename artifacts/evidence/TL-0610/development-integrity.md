# TL-0610 private package integrity and runbook

On 27 November 2026, `tools/verify_development_package.py` verified two
local private development archives on the active Windows 11 x64 host.
The tool takes the source and digests from a separate reviewed record
and checks receipt, bounded ZIP structure, ZIP member integrity, AMD64
apphost and matching CycloneDX SBOM. It reads the package without
extracting or starting the app.

The packaging command `eng/package-development-preview.ps1 -RunName
post-toggle-keys` used reviewed source
`c212728172dab2ac6810839f22268382ba6bc047`. Its 19-member archive measured
1,239,213 bytes. The local receipt records these exact digests.

| Artifact | SHA-256 |
| --- | --- |
| Private ZIP | `f00e05239e87123c857dab7c6c1e9fb31a766ab3c1f3f6854053c599167e37a8` |
| Windows apphost | `fcf84954199ad2763c183c8b661b20cd13c59583444331b9395f2404e7edc84b` |
| CycloneDX SBOM | `2cab8e79a7768b2494b7b1b0aa0b9ad15f937c52ab3a6a10dcf780fd3c06f254` |

The verifier also checked the earlier `c1f92cc4` handover-composed
archive against its prior reviewed digests. The six Python tests cover
the valid package, artifact substitution, mismatched SBOM source,
Windows path aliases, altered member inventory and x86 apphost.

The operator runbook documents current behavior, data preservation
and incident handling. The exact private artifact remains in the
ignored local engineering output. The standalone installer, rollback,
optional aggregate metrics export and authorized pilot exposure still
have their own gates.
[Deployment](../../../docs/pilot/deployment-rollback.md) · [Runbook](../../../docs/pilot/operator-runbook.md)
