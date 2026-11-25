# TL-0401 through TL-0404 and TL-0412 — machine-wide package path

**Date** 2026-11-25  
**Baseline** `af51598904b60f6f4fadf0bc09efe6857a51a95b`, integrated Windows 11 amendment  
**Environment** Active Codex machine, Windows 11 Pro 26H2 build 26300.9974, native x64, unelevated.
Windows Installer 5.0.26100.9711, WinGet 1.30.310, .NET SDK 10.0.400, Python 3.14.7.

## Verified preparation

- Baseline bundle validation exposed only TL-0311 as eligible. All 158 baseline validator
  tests and seven plan review tests passed. The project-local Python environment provides the
  pinned PyYAML dependency.
- The reset permits TL-0401 with TL-0311 blocked. Focused regressions cover independent
  selection, verified checkpoint integration, privilege and exposure prerequisites, and
  technical completion without owner QA. PR #36 retains its five unique broker commits at
  `642e6295fe913015f8da0563e3f93d33d0d1f267`.
- The [backend ADR](../../../docs/adr/0009-winget-backend.md) records the route comparison and
  review. The adapter independently observed Notepad++ absent, resolved and verified the
  vendor-signed installer, and observed absence again. The catalog binds
  `Notepad++.Notepad++`, version `8.9.9.1`, source `notepad-plus-plus-releases`, native x64
  and machine scope to SHA-256
  `e3b0607a092ce2604fbdac027db2c94915995e8e568ebd226f8c5493c2f61a80`.
- Isolated application tests use a fixture installer and known observations. They exercise
  approval and expiry at dispatch, apply and independently verified states, declined
  Windows consent, a missing postcondition, SQLite reopen and report output. Fixture
  evidence covers those branches and report continuity under controlled inputs.

## Candidate verification

Source commit `100aeb82eb352e781eee7c92808a7c4f53b4efce` was verified in a separate,
clean worktree on the active machine. The checkpoint's evidence follow-up changes task
state and reporting only.

| Check | Result |
| --- | --- |
| `eng/verify.ps1 -Tier Quick -Restore` | Passed in 76.792 s. Seven focused governance cases and 1,165 .NET tests passed. |
| Focused package and plan checks | 14 UI tests in 10 s. Package adapter tests passed, including the read-only host probe. |
| `eng/verify.ps1 -Tier Full -Restore` | Passed in 331.593 s from the clean candidate. 289 governance and 1,965 .NET tests. |
| `eng/run-ui-smoke.ps1` | Production app launched unelevated, navigated through UI Automation, and closed normally in 1.325 s. Observed peak working set was 214,700,032 bytes. |
| Package tests, `Category=ActiveMachine` | One read-only test passed in 2.220 s. Absent before and after, reviewed signed MSI resolved successfully. |
| `eng/generate-sbom.ps1` | Source-bound CycloneDX 1.6 SBOM, 29 components, review pending. SHA-256 `2bb72ecdd0428061505d2bcf6c0cc24035624ba6a92c328abf7cfcc699278af4`. |

Candidate apphost SHA-256 `49f0d3d83f34b2e74a2079588347c5a130ac55e88b615c8e6dd5cebb40e4581a`.
UI assembly SHA-256 `8c77605d14db835fa787021000917d8750d015c4e504cdd7df470a6b7d5080bb`.
An SBOM regression rejected the new runtime catalog until it was included in the governed
source revision set. The focused fix and clean Full passed. No Extended trigger applies to
the verified engineering slice.

## Active-machine preflight after toolchain installation

At integrated reset source `0492cb74253f7978787cf8ed2adafdb9c92dfabe`, the
read-only production package probe passed in 1 s. It observed the exact package
absent before and after resolving the reviewed signed MSI. It ran via `dotnet test`
on `tests/ThirdLife.Packages.Tests/ThirdLife.Packages.Tests.csproj` in Release
with filter `Category=ActiveMachine`. The complementary 18 deterministic tests
passed in 172 ms. Windows 11 Pro 26H2 build 26300.9974, native x64,
the unelevated process and Windows Installer 5.0.26100.9711 matched the prepared
environment.

A read-only native Windows Installer database check took 5.027 s, including
acquisition and Authenticode. The exact 7,933,952-byte vendor MSI matched the
recorded SHA-256 and valid signer certificate
`1E8E0D13B608BA908572C1A129FAEC5D228DF8A2`. Its template was `x64;1033`,
`ALLUSERS` was `1`, and its ProductVersion was `8.9.9.1`. The ProductCode was
`{A23B657D-838B-4C9B-8F9D-386E50C1A76B}` and the PackageCode was
`{18CD7780-F2FF-4F13-9070-BBE80D108501}`. The temporary review file was removed.
An initial COM query produced a parameter type mismatch. The native query
opened the database read-only and returned the expected metadata.

An independent `MsiGetProductInfoExW` query in machine context also returned
`ERROR_UNKNOWN_PRODUCT` (1605) for the reviewed ProductCode's state, version,
publisher, package code and assignment type. The query passes a literal null
SID as required for machine context. [Microsoft API contract](https://learn.microsoft.com/en-us/windows/win32/api/msi/nf-msi-msigetproductinfoexw)

A separate read-only UI Automation launch of the production app at its verified
candidate source `100aeb82eb352e781eee7c92808a7c4f53b4efce` passed in 3.137 s.
It opened the audit workspace, confirmed assessment and preparation were
disabled without intake, and closed normally. No job was opened or created.
Six focused package UI tests passed in 4 s and the WPF control accessibility
test passed in 3 s. The split isolated a transient CLR testhost shutdown assert
seen after six passing tests in the initial combined selection.

Quick with locked restore passed on the preflight checkpoint in 128.738 s,
with seven governance cases and 1,165 .NET tests. The run exceeded the 120 s
target while other session worktrees were active. The Quick WPF test slice
reported 40 s, and the Release build reported 13.52 s. No runtime source or
dependency changed. Reuse the earlier clean Full for the unchanged candidate.

## Launched transaction

Pending action-specific authorization for one machine-wide installation on the active
Windows 11 host and its applicable intake fact. The expected postcondition is Windows
Installer machine context, product code `{A23B657D-838B-4C9B-8F9D-386E50C1A76B}`,
installed state and the exact reviewed version. Use the normal unelevated application,
check any Windows consent, and confirm the saved journal and report agree after reopening.
No installer was launched for the read-only observations.

The narrow package admission policy checks intake and supported native-x64 Windows 11.
The backend checks disk headroom. The existing whole-device disposition and finalization
rules govern device readiness. Unobserved behavior includes the live installer, standard
Windows consent, a physical accessibility observation and broker qualification. Pilot and
release authorization remain separate.
