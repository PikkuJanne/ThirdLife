# ADR 0009 — Reviewed vendor MSI through Windows Installer

**Status** Accepted engineering route. The local transaction requires separate action-specific consent.  
**Review date** 2026-11-25  
**Tasks** TL-0401, TL-0402, TL-0403, TL-0404

Use the installed Windows Installer and the publisher's released, signed x64 MSI for this catalogue entry. The unelevated ThirdLife app invokes the Windows-owned package mechanism after the normal plan and journal approval. Windows owns any privilege prompt and the machine installation. The ThirdLife broker remains a separate unavailable capability for operations that require ThirdLife-owned privileged code. [Development contract amendment](../amendments/2026-11-25-development-contract-reset.md)

## Package and review

| Fact | Reviewed value |
|---|---|
| Catalogue entry and capability | `generic.plain-text-editor`, `document_editing` |
| Source and package | `notepad-plus-plus-releases`, `Notepad++.Notepad++` |
| Exact release | `8.9.9.1`, upstream tag `v8.9.9.1`, source commit `066c93ac06e2f8d59c5e5f442e60d8306b603163` |
| Installer | `npp.8.9.9.1.Installer.x64.msi`, 7,933,952 bytes |
| SHA-256 | `e3b0607a092ce2604fbdac027db2c94915995e8e568ebd226f8c5493c2f61a80` |
| Authenticode | Windows reported Valid for signer `NOTEPAD++`, certificate thumbprint `1E8E0D13B608BA908572C1A129FAEC5D228DF8A2` |
| MSI identity | ProductCode `{A23B657D-838B-4C9B-8F9D-386E50C1A76B}`, UpgradeCode `{CDB13460-04DF-4708-A7FD-4CB4A0684605}`, `Notepad++ (x64)` |
| Machine properties | x64 template, `ALLUSERS=1`, manufacturer `Notepad++ team (MSI installer)`, `28,130,419` bytes of embedded files before registration and Windows Installer caching |

The published release and WinGet installer manifest agree on the MSI URL and digest. A read-only Windows Installer database review confirmed machine scope, product identity, the embedded cabinet, and the absence of a service-install table. ThirdLife's bounded download permits the vendor GitHub release endpoint and one HTTPS redirect to GitHub's release asset host. It checks the reviewed length and SHA-256, then checks Windows Authenticode before plan readiness and again before passing the exact read-locked file to Windows Installer. A source, identity, scope, artefact or digest change requires a new review and transaction approval. [Vendor release](https://github.com/notepad-plus-plus/notepad-plus-plus/releases/tag/v8.9.9.1) · [Pinned WinGet manifest](https://github.com/microsoft/winget-pkgs/blob/d1ead6eab9bdf9963b81af7059e65f065bc7d1ed/manifests/n/Notepad%2B%2B/Notepad%2B%2B/8.9.9.1/Notepad%2B%2B.Notepad%2B%2B.installer.yaml)

The upstream source header licenses Notepad++ under GPL-3.0-or-later. This review covers running the original vendor installer locally and requires the normal action-specific consent. ThirdLife obtains the MSI from the publisher at execution time. The repository, build and release files exclude the vendor binary. Mirroring, release bundling, redistribution, and pilot/public exposure remain withheld. The review expires on 2026-12-25. [Licence text](https://github.com/notepad-plus-plus/notepad-plus-plus/blob/066c93ac06e2f8d59c5e5f442e60d8306b603163/LICENSE) · [Source licence declaration](https://github.com/notepad-plus-plus/notepad-plus-plus/blob/066c93ac06e2f8d59c5e5f442e60d8306b603163/PowerEditor/src/Notepad_plus.cpp)

The fixed install properties `ALLUSERS=1` and `NOUPDATER=1` select machine registration and omit the updater feature. The reviewed MSI's condition sets `AutoUpdaterFeature` to level 0 when `NOUPDATER` is set. ThirdLife does not launch the editor, provide a document, install additional plugins, or send job data to the vendor. Fetches reveal ordinary connection information to GitHub and Windows may retrieve certificate-chain status. ThirdLife keeps package identity and outcomes, with no raw installer output, downloaded source tokens or user paths in the job. [Vendor MSI properties](https://github.com/notepad-plus-plus/npp-usermanual/blob/master/content/docs/command-prompt.md)

## Application and independent observation

The Windows adapter binds the existing typed install request and reviewed catalogue digest to its one compiled package identity. It uses the absolute Windows Installer executable and fixed `/package`, `/passive` and `/norestart` parameters. Elevated app execution is rejected. Package state is observed with `MsiGetProductInfoEx` in machine context using the exact ProductCode, installed marker, version and manufacturer. Related Windows Installer products and the vendor's conventional uninstall and installation locations can block work on an already occupied target. This checks the relevant Windows registrations and locations, with no scan of other profiles, portable copies or personal files. [Microsoft Installer command options](https://learn.microsoft.com/windows/win32/msi/standard-installer-command-line-options) · [Installation context](https://learn.microsoft.com/windows/win32/msi/installation-context)

The Microsoft-owned installer handles machine changes and may show ordinary Windows consent. The adapter reports apply success separately from installed observation, preserves restarts as pending and distinguishes pre-dispatch cancellation from an install that may still be running. The native transaction can outlast the UI wait. The app requires another observation before any retry. Normal resolve and install operations delete the staged MSI and its private attempt directory. An interrupted process can leave one bounded attempt artifact under the current user's local application data. ThirdLife never reuses that path. No installer output is retained by ThirdLife.

## Alternatives and evidence limits

The Microsoft.WinGet.Client module and the WinGet COM API remain candidates for additional package functionality. The structured WinGet APIs support exact catalog and scope matching, but this route needs the exact reviewed bytes and documented installer properties. WinGet CLI selection by source, version, machine scope, architecture and installer type also resolves mutable source metadata and does not accept a caller-supplied reviewed installer digest for the selected tuple. Windows Installer handles the signed vendor package directly after the adapter's artifact check. No CLI display output is used. No live mutation comparison of WinGet clients was performed. [WinGet package selection](https://learn.microsoft.com/windows/package-manager/winget/install) · [WinGet COM contract](https://github.com/microsoft/winget-cli/blob/master/src/Microsoft.Management.Deployment/PackageManager.idl)

On 2026-11-25 the active Windows 11 Pro client (native x64, build 26300) and Windows Installer `5.0.26100.9711` reported the target package absent before and after an opt-in production adapter resolution. The adapter returned Ready after the signed MSI passed verification. This observation covered acquisition and initial state. The app's install, independent postcondition and reopened report require their own runtime transaction evidence.

## TL-0406 verification extension

The original install and registration evidence retains its scope. The revised reviewed
profile discloses an automatic, empty launch check before approval. After a durable
Windows Installer `Applied` result, the verifier checks the exact machine ProductCode,
PackageCode, version, publisher and assignment. It obtains the executable and two
startup XML files from the fixed machine installation directory. The compiled lengths
and digests come from members of the same signed, pinned MSI.

The verifier stages only those checked files and a local-settings marker in an
access-restricted temporary directory. It starts the copied image unelevated on an
inactive Windows desktop with local settings, a fresh empty instance, and no session
restore or plugin loading. The launch uses the approved installed bytes in isolation
from the normal editor configuration. The Windows job contains the process and
provides bounded teardown. The verifier confirms cleanup or records it as unconfirmed.
See `artifacts/evidence/TL-0406/report.md` for scope and limits.
