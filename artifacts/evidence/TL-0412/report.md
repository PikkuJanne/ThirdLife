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

## Authorized launched transaction

On 2026-11-25 the project owner authorized one machine-wide installation of
the reviewed Notepad++ 8.9.9.1 x64 vendor MSI on the active Windows 11 host,
affirmed lawful control and confirmed verified replacement storage. The app
recorded the replacement method using an opaque media code and the owner
attestation date. The underlying physical replacement date was not inferred.
Codex operated the unelevated application and recorded that scoped approval
with the authorized operator attribution.

The tested production app was built from `100aeb82eb352e781eee7c92808a7c4f53b4efce`
and matched the apphost and UI assembly digests above. The first UI Automation
run took 14.078 s and saved intake and the live assessment. It stopped before
approval because Windows exposed the named profile's SelectionItem provider on
the enclosing ListItem. The driver selected that provider on reopening the same
job. No production code or approved action changed between runs.

The resumed launched flow completed in 14.404 s. The app confirmed absence,
resolved the signed installer and showed the exact package plan for all users.
It saved approval, launched Windows Installer for the pinned x64 MSI with the
updater disabled and registered the operation as applied and independently
verified. The plan SHA-256 was
`29fae5c61d8c9bf0b39065af9a1e5395df222ba33ac90adaae2517c56836ea16`.
No package Windows consent prompt was observed or operated by the driver.

After the operation, a separate unelevated `MsiGetProductInfoExW` query took
0.712 s including interop setup. The reviewed ProductCode
`{A23B657D-838B-4C9B-8F9D-386E50C1A76B}` returned success for each property in
machine context with a null SID. Windows reported installed state `5`, exact
version `8.9.9.1`, publisher `Notepad++ team (MSI installer)`, exact PackageCode
`{18CD7780-F2FF-4F13-9070-BBE80D108501}` and machine assignment `1`. The same
properties returned `ERROR_UNKNOWN_PRODUCT` before dispatch.

The app closed and reopened the job, displayed the saved verified transaction
and generated and opened the workshop report. In a separate 0.678 s read-only
SQLite check, one saved plan and one approval preceded four ordered entries.

| Actor role | Saved state | Evidence |
| --- | --- | --- |
| Operator | Approved | Exact package plan approval |
| Coordinator | Started | Durable package dispatch intent |
| Package backend | Applied | Windows Installer application result |
| Verifier | Verified | Independent exact package identity |

All four payload digests matched. One action and approval were bound to the
journal, and the dispatch, application and verification shared a correlation.
The generated report and reopened journal agreed on the exact version, machine
scope and verified outcome. The saved report matched its preview and had SHA-256
`08e56ecde3a5db0c6c1ab0bdb6649c051e9495a6e6e027f5069e0ce898e02abb`.
The report was recorded at `2026-11-25T16:08:09.5535551Z`. The protected job and
report remain in the local application store. The repository records their
bounded evidence and digests.

The application exited normally. No ThirdLife.UI process or package attempt
staging directory remained. Notepad++ remains installed as authorized. The
opt-in absent-state preflight above describes the state before the transaction.
The selected package workflow underwent technical keyboard and accessibility
checks. Physical or perceptual accessibility behavior remains unobserved for
this transaction. Whole-device readiness, recovery scenarios and first
write-capable exposure remain governed by their separate criteria.
