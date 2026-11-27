# TL-0601 workshop finalization

The ordinary TRACE Handover stage provides 11 checks for workshop accounts,
browser sessions, Wi-Fi profiles, credentials, shares, test media, diagnostic
exports, downloads and package residue, VPN and remote support, clipboard and
key material, and pending restart. Operators choose an outcome and use an
opaque workshop code for attribution. The applicable checks accept only
predefined reasons for a not-applicable outcome. Required checks with no
current clearance stay open. Pending restart uses independent profile
verification and accepts no manual override.

SQLite migration 15 appends the result, actor, UTC time, selected reason, and
job evidence version within the existing restricted store. The store checks
the displayed evidence version and history sequence when writing. A later
boot, archived job, or relevant new durable evidence makes the earlier result
historical. The projection retains the original confirmation with its
provenance. A rejected save preserves the earlier history. The per-job history
allows up to 512 confirmations of up to 1 KiB each. It does not take raw
provider data, personal paths, account names, passwords, or recovery material.
The existing guarded store and local access controls apply.

A checklist with no open required checks is one input to handover. It does not
establish current whole-device verification, actual handover, cleanup or pilot
authorization. The history binding cannot observe ambient changes that neither
the operator nor an application provider records. TL-0602 owns the bounded
artifact observations. TL-0604 and TL-0607 own the workshop record and
handover decision that consume this checklist. TL-0511 holds the full-profile
native qualification.

## Verification

The base is `7615a339a873613e87827c0530d2448b039f4ce6`. The active host is
Windows 11 Pro 26H2 native x64, build 26300.9974. Verification used .NET SDK
10.0.400 and Python 3.14.7 on 27 November 2026.

| Check | Observed result |
| --- | --- |
| Base Quick with locked restore | 1,355 deterministic .NET cases and eight work-selection regressions passed in 215.079 s. The clean isolated worktree initially lacked PyYAML. The repository's pinned 6.0.3 package was installed in that worktree's local venv. The baseline exceeded the 120 s Quick target during concurrent development. The cold Release rebuild took 33 s and the desktop suite took 105 s. |
| Focused Core, `FullyQualifiedName~FinalizationChecklistTests` | 11 passed in 147 ms of test time. Covered missing, passed, failed, unconfirmed, not-applicable, independent restart, ordered history, and stale state. |
| Focused Persistence, `FullyQualifiedName~FinalizationStoreTests\|FullyQualifiedName~FullMaintenanceReopensAtEveryBoundaryAndRequiresFreshStateAfterFinalRestart` | Eight passed in 12 s of test time. Covered schema 14 migration, persisted and archived jobs, state changes, cross-job and stale-write rejection, and independent restart evidence with controlled inputs. |

The focused WPF case `FullyQualifiedName~AuditFinalizationTests` passed in
2 s of test time. The ordinary Handover controls reported a missing reason,
then saved the selected reason with a workshop code. Reopening the same job
on the modeled boot displayed the same current entry. A later modeled boot
kept it in the history and required another check. The control test checked
keyboard focus, accessible names, and the polite status. It did not test
human screen-reader use or perform a physical restart.

An additional Handover and report selection passed 14 cases. The selected
ActiveMachine M1 case failed its Intake focus assertion before reaching
Handover. The same assertion failed in isolation on the earlier clean
`84e47d5` source with unchanged UI and UI test content relative to the
`7615a33` base. That case stays open as an active-desktop observation.

The Targeted command was `./eng/verify.ps1 -Tier Targeted -TestProject` with
the Persistence and Verification test projects. It passed in 310.976 s.
Quick and the related projects passed 1,594 .NET cases with zero skips,
eight work-selection regressions, both validators, formatting and a Release
build with zero warnings and errors.

Clean Full used `./eng/verify.ps1 -Tier Full -Restore` on detached
`be6069969f66b0971fd30454cef74cc9199e912a`. It passed in 700.420 s with
310 governance regressions in 449.322 s and 2,558 deterministic .NET tests
across 13 assemblies with zero skips. The workspace remained clean.
The baseline and candidate checks used the pinned PyYAML dependency and
the built-in structural validator. The clean pass excludes Native and
Extended cases under the standard filter.

The clean Release apphost opened unelevated and reached Inspect via external
UI Automation in 1.421 s. It exited normally in 1.608 s. The apphost SHA-256
was `946486f1e374d2f694c09081ba59aaf6ecc26912eec7d37c1ec940068d362f04`
and the UI assembly SHA-256 was
`1c4d4627416549c5660dee7f339895aa0fae8e04f4c6b3b29693ea80063e404c`.
The desktop smoke covered startup and navigation. The controlled Handover
case above covers checklist operation.

The supplementary GitHub Windows check passed the same source.
[Supplementary check](https://github.com/PikkuJanne/ThirdLife/actions/runs/46035322146/job/140629140243) ·
[Integration PR](https://github.com/PikkuJanne/ThirdLife/pull/83)

The evidence-only checkpoint passed continuity Quick in 274.025 s with
1,367 .NET cases and eight work-selection regressions. The 120 s target
was exceeded while the machine also ran other feature verification. The
WPF suite took 162 s and the Release build took 22.44 s. The code and
migration match the clean Full source.
