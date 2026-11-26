# TL-0409 package idempotency and retry

Date 2026-11-26. Base `a6be440c530972adcbebd2418f5c240973eecd35`.
Candidate `1463664aa3636b133110917c71c2d62b0d16a784` on
`codex/tl-0409-package-retry`. Tests ran unelevated on the active Windows 11
Pro 26H2 client, build 26300.9974, native x64, with .NET SDK 10.0.400.

## Application outcome

Reopening the saved job after an eligible failure exposes **Retry saved
package** once the delay elapses. Selecting it re-resolves the current plan.
The unchanged resolution can use its saved approval within the existing
10-minute window. An expired approval, a material change, or an authorization
change recorded during preparation returns the operator to explicit plan
review. The adapter rechecks installed state and the reviewed artifact before
the dispatch boundary. A repeated run of an independently verified plan
preserves the saved result and checks current registration without a download.
A missing or conflicting target directs the operator to review current device
state. If the exact target appears after approval and before installer dispatch,
the app rechecks authorization and runs independent registration and launch
verification. That result has its own checkpoint and makes no installer claim.

Eligible outcomes are connection loss, cancellation, or package-backend
unavailability with durable proof that Windows Installer never started. A
veto by the pre-dispatch authorization check requires fresh approval. An
installer failure, trust mismatch, ambiguous dispatch, or pending restart
keeps its recorded disposition and offers no ordinary retry. Legacy outcomes
without proof of a pre-dispatch failure keep their existing disposition.

SQLite enforces three starts per exact package target within a saved job, with
5 seconds after the first eligible failure and 15 seconds after the second.
The immediate write transaction checks the current sanitization gate, active
approval, prior failure ID, and delay before granting the attempt. Each retry
records its own correlation and links to the preceding failure, including
across plan keys for the same target after reapproval. The append-only journal,
reopened WPF history and workshop report preserve the earlier attempt. The
prior-attempt field is optional on historical entries and the change uses the
existing SQLite schema. Reports contain typed outcomes and omit backend text.

## Verification

| Check | Observed result |
| --- | --- |
| Baseline `eng/verify.ps1 -Tier Targeted -Restore` with UI and `FullyQualifiedName~PackageSetupTests` | Passed in 140.483 s on `a6be440`. Quick and 41 filtered WPF tests passed. The first invocation stopped at Python toolchain validation. The pinned local environment corrected the setup, with no baseline product failures. |
| Initial retry checkpoint `eng/verify.ps1 -Tier Targeted -Restore` with Actions, Packages, Persistence, Reports and UI | Passed in 159.316 s on `b9018ee`. Quick, eight governance checks, 1,625 .NET tests and the Release warnings-as-errors build passed. A prior formatting check flagged new-file line endings and a multiline pattern. The scoped formatter corrected both. Review of this checkpoint identified two installed-state gaps and the clean gate was stopped and restarted on the revised candidate. |
| Candidate `eng/verify.ps1 -Tier Targeted -Restore` with Actions, Packages, Persistence, Reports and UI | Passed in 177.171 s on `1463664`. Quick, eight governance checks, 1,630 .NET tests and the Release warnings-as-errors build passed. |
| Clean candidate `eng/verify.ps1 -Tier Full -Restore` | Passed in 385.221 s. Locked restore, formatting, the Release warnings-as-errors build, 304 governance regressions and 2,168 .NET tests passed. |
| Extended FI-004 during fixture apply | 1/1 passed in 5.987 s. Reopening the same job found the interrupted attempt in review after re-observation. |
| Extended FI-004 after the durable apply result | 1/1 passed in 5.155 s. Reopening completed independent verification on the original attempt. |
| Extended FI-005 for the owned fixture backend | 1/1 passed in 3.737 s. The interrupted backend attempt remained in review. |
| Extended FI-012 for the package checkpoint commit | 2/2 passed in 2.609 s. Both commit boundaries preserved the joint verification and checkpoint-token state. |

The WPF cases exercise one apply after network loss or unavailable backend
observation, the saved same-approval path after reopen, expiry, a changed
catalog that requires reapproval, the durable cap, and completed-plan repeat
with one installer call. They also check missing registration after saved
verification and fresh verification when the exact target appears before dispatch.
They check keyboard focus, the named retry control and its polite live region.
SQLite tests check competing claims,
backoff, stale and wrong predecessors, approval binding, and verification
after a cross-plan retry. An Actions regression round-trips the historical
payload shape without the optional predecessor field.

The separately invoked Extended selectors are in the
[failure-injection register](../../../docs/testing/failure-injection.md#tl-0408-package-interruption-and-restart).
The clean checkout remained tracked-clean after all runs.

Clean Release build SHA-256 digests

- `ThirdLife.UI.dll` `2ae6138730c4e56d952f08250c3926e2e7cdd08e75bb855a4989aeff4922418c`
- `ThirdLife.Actions.dll` `77674174d90947f9f7d28dce55b40160c4a3aae955b68dfa83aa068ab8268211`
- `ThirdLife.Persistence.dll` `3d4b35b30bc02e71e1515640b500904e6b3c0f79249268b55de301cf8ee06fd8`
- `ThirdLife.UI.Tests.dll` `a7392b5691fe5965b703ada579e5caa904483fa2e869615a4d711da7a80f62b0`

## Scope

The application scenarios use the normal WPF entry points, protected isolated
SQLite and controlled package adapters. Restart tests use modeled boot evidence
and the existing same-boot check. Technical UI Automation checks cover the
named controls, focus and live-region setting. Perceptual assistive technology,
a fresh native package mutation or restart, and concurrent independent jobs
or installer clients are outside these results. The earlier native
installation retains its [TL-0412 evidence](../TL-0412/report.md).

TL-0410 continues with preflight of a disposable Windows 11 native x64 guest
and a clean restoration point. The exact candidate, guest, saved plan, Windows
consent and any authorized interruption or orderly restart govern the live
actions. [Native qualification procedure](../../../docs/testing/failure-injection.md#tl-0408-native-package-route-and-windows-restart)
