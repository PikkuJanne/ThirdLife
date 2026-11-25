# TL-0315 Windows 11 platform migration

**Date** 2026-11-25  
**Integrated baseline** `8f1441be844536e8b4ef88193b3a3e4c1d9c453e`  
**Verified candidate** `b8314ef26ea628b233884bd5049ece92a41b5177`, tree `badc3816ca4e5d0ca8d5f51ae6cca7ff984b3f83`  
**Reference environment** REF-CODEX-001 rev 2026-08-29.1, Windows 11 Pro 26H2 build 26300.9974, workstation, native x64, unelevated. .NET SDK 10.0.400, runtime 10.0.11 and Python 3.14.7.

| Requirement | Verified coverage |
| --- | --- |
| Application admission | Windows product type, NT version, reviewed exact release build and native OS architecture precede the UI workspace. Synthetic cases cover Windows 10, overlapping Server builds, Arm64 emulation, missing and contradictory identity. Refusal tests check that the workspace factory is never called. The x64 UI manifest remains asInvoker and PerMonitorV2. |
| Servicing | The [offline catalog](../../../docs/inventory/windows-lifecycle-data.md) records Microsoft sources, 12 edition/build rows, and the retained 30-day bound. November 25 through December 25 inclusive is the current window. Unsupported, stale and unverified facts remain distinct. 26H1 is recognized for identity and has unreviewed servicing coverage. |
| Disposition and continuity | Current policy schema v2 and candidate.2 retire alternative-OS advice and retain evidence-based precedence. Two isolated SQLite histories verify pre-change Windows 11 and Windows 10 records, original policy bytes and digests, report readback, and explicit reevaluation without replacing old observations or exports. No SQLite migration. |
| Historical authority | Plan v1 snapshots, approvals, journal and reboot history remain readable. Deterministic tests reject fresh approval, dispatch intent and resume from those plans. The UI retains Abandon for a historical pending checkpoint. The existing broker path in integrated main is a placeholder. |
| Delivery | UI and broker apphosts are x64. The bundle, task graph, governance validators and future pilot/stable lifecycle qualification are aligned. No package or release exposure occurred. |

## Verification

- Pre-change focused disposition test passed 17 cases in 8.3 s. The default Python lacked
  PyYAML, so verification used the documented venv and installed hash-pinned PyYAML 6.0.3.
- Targeted passed 1,812 tests in 125.840 s with Quick included. Command
  `./eng/verify.ps1 -Tier Targeted -Restore -TestProject <Policy, Actions, Catalog, Reports, Persistence, Broker and Broker.SecurityTests project paths>`.
  Locked restore, formatting, Release build and repository controls passed.
- Clean-worktree `./eng/verify.ps1 -Tier Full -Restore` passed 290 governance and 1,941 .NET
  tests in 337.118 s. The clean source and candidate tree matched before and after the run.
  The repository's pinned structural validator ran, and a separate PowerShell
  `Test-Json -SchemaFile schemas/policy.schema.json` check accepted both exact fixtures
  and rejected the retired disposition in a v2 mutation in 0.203 s.
- The explicit `dotnet test -c Release --no-build --no-restore` active checks selected
  `ActiveMachineOsSmokeIsUnelevatedBoundedAndSilent`, `ShellProcessSmokeTests` and
  `SupportedDesktopAssessesEvaluatesReopensAndExportsAnIsolatedJob`. All three
  passed. The checks and artifact inspection took 7.451 s.
- The actual Release apphost opened and sent an external UI Automation live-region event.
  Its peak working set was 212,975,616 bytes, with 1,023 ms to observation and 1,046.875 ms
  CPU for this run. The broker placeholder exited successfully on the supported host.
  A separate window using the same compiled UI assembly and a temporary synthetic SQLite
  workspace exercised assessment, policy evaluation, close and reopen, and report preview.
  The saved decision and export agreed, including policy digest and original evidence.
  Keyboard focus, UI Automation label, polite report status, and an applied 200% layout
  scale were verified. App startup never opened the operator's registered store.
- The first Targeted run stopped on CRLF and switch indentation defects. Focused formatting
  passed after correction. The focused UI work also corrected a stale report announcement
  assertion and rebuilt the historical Windows 10 fixture to reproduce its original v1
  disposition under a valid intake policy.

| Exercised Release artifact | SHA-256 |
| --- | --- |
| `ThirdLife.UI.exe` | `9bfcd7dfd1a9096bb678edceb230fef92314096106b7f36037d3b67f607adde2` |
| `ThirdLife.UI.dll` | `4bdfeb9574e230d43b17a96219fb08dbce9e4686357a456e6d9fcf1018bf4283` |
| `ThirdLife.Broker.exe` | `1ab9789860a530637e6c877d9a37272b729549c41b08562e3c5fc02c4a7b510e` |
| `ThirdLife.UI.Tests.dll` | `63b3e3b187acb04fd8fbda58965be389c28a35eaf43e59c48f6f52886c89fa6a` |

## Limits

The supported host observation, isolated synthetic job and unsupported-platform fixtures
cover different aspects of the migration. No perceptual screen-reader or cross-hardware
qualification is claimed. Timeline-based servicing evidence carries no patch-installation or
hardware-eligibility claim. TL-0311 still owns trusted elevated startup and real privilege,
recovery and distinct-account evidence in draft PR #36. No pilot or release approval was exercised.
