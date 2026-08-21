# Active Codex Reference-Machine Profile

**Profile ID:** `REF-CODEX-001`  
**Revision:** `2026-08-21.1`  
**Captured:** `2026-08-21T13:52:54+02:00`  
**Captured by:** `Codex session / sanitized role`  
**Purpose:** reproducible development and test context only; not asset inventory or hardware certification

## Operating system

| Field | Sanitized value |
|---|---|
| Windows edition | Microsoft Windows 11 Pro |
| Windows version/build | 10.0.26200 / 26200 |
| Architecture | 64-bit x64 |
| Supported target status | Windows 11 x64 target present; release support remains governed by policy and later gates |
| Virtualization status relevant to tests | Hypervisor reported present; the processor provider reported firmware virtualization disabled, so hosted-environment availability was not inferred |

## Compute and storage

| Field | Sanitized value |
|---|---|
| CPU model/class | Intel Core i7-10750H, 2.60 GHz class |
| Logical processor count | 12 |
| Installed memory | 15.8 GiB reported |
| Storage classes reported | NVMe/SSD and USB/unspecified; provider classifications only |
| Test-volume free-space class | 100–199 GiB at capture |
| GPU status | Integrated Intel UHD Graphics and discrete NVIDIA Quadro T1000 adapters reported present |
| Acceleration path used for TL-0008 | None; TL-0008 ran documentation/schema/static checks only |

## Toolchain

| Tool | Version / source |
|---|---|
| .NET SDK | 10.0.400, selected by `global.json` |
| .NET runtimes | .NET 9.0.7 and 10.0.11 runtime families reported |
| PowerShell | 7.6.4 |
| Python | 3.14.7 in the repository-local ignored environment |
| PyYAML | 6.0.3 |
| Git | 2.55.0.windows.4 |
| Package tooling | WinGet 1.29.290; no product package/update action run for TL-0008 |
| Database tooling | Standalone SQLite CLI not available; no database work required by TL-0008 |
| Accessibility tools used | None; the later accessibility audit was not triggered |

## Clean-environment methods on this machine

| Method | Current evidence |
|---|---|
| Separate clean clone | Supported by Git; not run for TL-0008 because no clean-clone gate was triggered |
| Separate worktree | Supported by Git; not run for TL-0008 |
| Clean virtual machine | Not verified; hypervisor presence alone is not a successful VM test |
| Windows Sandbox | Not verified for TL-0008 |
| Container for portable tests | No container command was available during capture; not required for TL-0008 |
| Disposable virtual disk or isolated test volume | Not exercised; low-space and extended resource scenarios were not triggered |

## Known evidence limitations

- This is one active-machine snapshot. It does not establish behavior on another processor, memory size, storage device, firmware implementation, GPU, peripheral set, or manufacturer.
- Provider-reported virtualization and storage classes were recorded without resolving device identity or serial information.
- Free space is deliberately recorded as a bucket, not an exact capacity or volume path.
- No physical manual-test walkthrough, cold boot, accessibility audit, broad failure injection, or low-resource extended matrix ran for TL-0008.

## Sanitization review

The repository record was reviewed and contains none of the following:

- [x] serial number
- [x] donor or corporate asset tag
- [x] username or email address
- [x] device or computer name
- [x] SSID or IP address
- [x] credential, token, key, or recovery material
- [x] personal or machine-specific path
- [x] screenshot, photo, audio, or video
- [x] raw diagnostic log

**Reviewer/result:** `Codex sanitized-field review — pass, 2026-08-21`
