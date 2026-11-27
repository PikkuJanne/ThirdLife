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

## File History composition package

The tool built source `2be6a208b1e26cb7d73b3e1bda58a554d582fe75`
using the same locked release publish. All 19 verified archive entries
matched the published bytes after extraction. The private ZIP calculated
SHA-256 `a92345b178c9588326a87fc8339acb2a5116b0726988ac1a81f288737735d679`
at 1,245,073 bytes. The SBOM hash is
`3710ea64e11c25133e79012e8466608bfa6f62184b379da277f34226091559cc`.

The exact unpacked apphost opened Inspect through external Windows UI
Automation at 1.544 s with an unelevated host token. It closed normally
by 1.728 s. The observed apphost hash is
`c346b9504497d1bce7f2adc9b77a3bae2cc708cd8811ffec4134773522e1d4a4`.
The archive, receipt and unpacked files are in an ignored local
engineering directory.
