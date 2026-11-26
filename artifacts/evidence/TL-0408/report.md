# TL-0408 package recovery

Date 2026-11-26. Base `8fc5c56cfd8910f94e187423a1c6689aa9d41290`.
The active machine ran Windows 11 Pro 26H2 build 26300.9974 on native x64.
Application and test operations ran unelevated.

## Application outcome

Opening the saved job in ThirdLife re-observes unresolved package work. A dispatch
intent without a durable installer result, a timeout or an ambiguous attempt gets a
fresh registration check and directs the operator to review. Exact registration
alone gives no durable claim about that attempt. The saved plan, approval and
correlation stay available. Recovery offers no install or rollback action.

A durable applied result for the reviewed package can continue the exact independent
registration and launch checks. A verified result appends to the original attempt
and appears in the reopened job and workshop report. Missing or conflicting
registration, unavailable observation, cancellation and failed launch verification
leave a typed review result. The live region and the cancel control expose the check
and its disposition. Reopening again preserves the single committed result.

For a restart-required installer result, the protected SQLite transaction saves
the backend outcome and attempts to seal a user- and store-bound checkpoint. The
authenticated payload binds the exact job, plan, approval, action, entry, correlation
and pre-restart Windows kernel interrupt-time sample. A later smaller sample proves
that Windows started again. The same-boot path checks registration and leaves
verification pending. An expired, stale, missing or unusable checkpoint leaves
the attempt in review. The store checks again before recording the independent
result and token consumption in the same transaction.

Normal recovery is confined to the saved single x64 machine package and the pinned
catalog and profile that disclosed the launch check. Archived jobs and a separate
evidence origin are read-only. A nonblocking per-attempt verification lease prevents
simultaneous live and resumed probes. The store compares the exact prior entry and
current gate inside the write transaction. No new SQLite migration, execution route,
or dependency is required. Protected boot proof and token material stay in the job
store.

A guest or workstation can reboot and run for longer than the short uptime first
sampled after installation. In that case the uptime comparison is inconclusive and
the job stays pending. Prompt reopen after Windows Restart gives the comparison its
best opportunity. This proof uses Windows kernel interrupt time, which includes
sleep and hibernation and is independent of wall-clock adjustments. The checkpoint
still expires after 24 hours. [Microsoft Interrupt Time](https://learn.microsoft.com/windows/win32/sysinfo/interrupt-time).
[Microsoft real-time API set](https://learn.microsoft.com/en-us/uwp/win32-and-com/win32-apis#apis-from-api-ms-win-core-realtime-l1-1-1dll).

## Verification

The production candidate is `7519a5ff71f371a42181dcf9d8c86e2b5c262e38`.
The branch is `codex/tl-0408-package-resume` and the pull request is
[PR #51](https://github.com/PikkuJanne/ThirdLife/pull/51).

| Check | Observed result |
| --- | --- |
| Clean baseline Targeted for UI with `FullyQualifiedName~PackageSetupTests\|FullyQualifiedName~RebootRecoveryTests` | Passed in 153.113 s. Both default Quick and the 31 filtered WPF tests passed. |
| Candidate `eng/verify.ps1 -Tier Targeted -Restore` for Actions, Packages, Persistence, Reports and UI | Passed in 158.829 s. Quick, eight governance checks, 1,612 .NET tests and the warnings-as-errors Release build passed. |
| Production boot API and same-boot checkpoint | The corrected system API set returned increasing positive interrupt-time samples. The normal WPF RestartRequired test sealed a checkpoint using the production source and observed that immediate reopening could not verify it. |
| Clean `eng/verify.ps1 -Tier Full -Restore` | Passed in 356.195 s on committed source. 304 governance regressions, 2,150 .NET tests, locked restore and a warnings-as-errors Release build passed. |
| Extended FI-004 during fixture apply | 1/1 passed in 6.059 s wall time. The same job reopened with fresh registration and an ambiguous saved attempt. |
| Extended FI-004 after durable apply during the fixture launch check | 1/1 passed in 5.127 s wall time. The same job recovered the original attempt to independently verified. |
| Extended FI-005 isolated backend process loss | 1/1 passed in 3.773 s wall time. The owned fixture backend exited while the application remained active. Fresh registration and review preserved the original attempt. |
| Extended FI-012 package checkpoint commit interruption | 2/2 passed in 2.562 s wall time. Aborting before commit preserved the pending token. A fault after commit preserved the recorded result and consumed token. |

The WPF fixtures checked the saved history, keyboard access, the polite live
region and focus after cancellation. These technical checks have no perceptual
screen-reader or physical hardware coverage. The independent Extended selectors
are in the [failure-injection register](../../../docs/testing/failure-injection.md#tl-0408-package-interruption-and-restart).

SHA-256 digests from the clean Release build

- `ThirdLife.UI.dll` `2794980603a1bd4da3b1f53d253f2433693300073946c25c7a97f64fe103ddf0`
- `ThirdLife.Persistence.dll` `55357a5e24bb8c00e9f5e734c2169d4d6bc1dd5fc8d0b3e99bfea6bad6b5b612`
- `ThirdLife.UI.Tests.dll` `20134e34ea97df25989ca0834004d370caebfc2b7ed627aab1e0e4b8871106ed`

The production commit also passed the [supplementary GitHub Quick check](https://github.com/PikkuJanne/ThirdLife/actions/runs/45775446166/job/139757883867).

The early package fixture run exposed a missing native entry point on the active
host. The fixed implementation uses the documented real-time API set and keeps
recovery pending if the API is unavailable. The old fixture regressions passed
after rebinding. One exploratory governance run overlapped a WPF build and raced
the creation and deletion of a generated project file. Authoritative Full uses a
clean checkout with governance and the build run in sequence.

## Native qualification

The Windows Sandbox CLI rejected the dedicated configuration at startup with
`0x80070780`. The direct Windows Sandbox launcher opened a blank session and
produced no guest admission marker within 120 seconds. No package fixture ran
in the guest and no Windows restart occurred. The owned session was closed.
Automatic command review blocked recursive cleanup of the isolated Sandbox
staging directory, whose contents are local test build files, a fixture draft and
the copied runtime. It remains under
`%TEMP%\ThirdLife-TL0408-Sandbox-4ddfea8aaa4a41e7a05c6493393fc6bd`.

The modeled package backend and launch checks use a disposable job. They do not
supply new native Windows Installer authority or a native 3010 observation. The
earlier live exact installation and the isolated executable check retain their
TL-0412 and TL-0406 provenance. The interrupted launch fixture checks the owned
fixture process and job-store recovery. Native probe workspace cleanup after abrupt
UI termination was unobserved.

The M4 end-to-end qualification needs an isolated Windows 11 native x64 guest
prepared for the exact reviewed artifact. Each install needs attributable approval
and any applicable Windows consent. Native interruption needs a safely attributable
owned installer client and a documented boundary. Restart-required qualification
needs a supported reviewed run that actually reports 3010 and a scoped Windows
restart of its disposable guest. TL-0410 retains the native execution work.
See the [failure-injection register](../../../docs/testing/failure-injection.md#tl-0408-native-package-route-and-windows-restart) for the procedure.
