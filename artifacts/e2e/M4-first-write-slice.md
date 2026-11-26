# M4 first write-capable slice

## 26 November 2026 checkpoint

The application candidate was built from `d61c49e9bb4cc8f5b18c4ac3576a713060cb3f77`
on the unelevated active Windows 11 Pro 26H2 native x64 machine, build 26300.9974,
using .NET SDK 10.0.400 and locked restore. TL-0412 supplies the existing approved
physical-host package transaction and its reopened workshop report. That approval
names the physical host. [TL-0412 evidence](../evidence/TL-0412/report.md)

The dedicated Generation 2 Hyper-V guest passed the existing Windows 11 client,
native x64 and Basic console admission checks through PowerShell Direct. The
prepared standard clean checkpoint matched its expected ID. The guest ran Windows
11 Pro 26H2 build 26300.9974 and had 102.5 GiB available. Windows Installer reported
no exact or related registration for the reviewed Notepad++ product. No installer
client was present. The source returned the reviewed MSI length of 7,933,952 bytes
to the remoting identity. The production application remains responsible for
checking the bytes, trust, source and approval through its ordinary entry point.
Shared .NET Desktop 10 was absent. The desktop harness stages the runtime subset
of the existing SHA-512-pinned SDK archive for that run.

The unelevated desktop observation launched the ordinary application, opened the
audit workspace and confirmed the closed intake gate before a job existed.
It exited normally in 7.738 s and the harness removed the staging and scheduled
task. The staged bundle had SHA-256
`2580b38738a656c26d36a8fef7f12abef64bc09978bb16cd283bb2d57fb1c1f1`.
The desktop driver had SHA-256
`e11c3f09d667987cb4313e01d90786ef00de6bbab0a8813342faf556f4f4c0b8`.
The first staging attempt disposed its PowerShell Direct connection too early.
An early desktop attempt left the inspection process open. Exact process and
stage cleanup succeeded before repeating the run with bounded diagnostics and a
longer startup window.
The final read-only cleanup check found zero app processes, installer clients,
runner tasks and runner stages, and no ThirdLife job-store directory.

Reviewed Release SHA-256 digests

- Apphost `c7f0ca78769637af0413bb3258bd1c9ec40aaf60bafdeb8d0895b5e56c4a638f`
- UI assembly `b2abb417940ced1122cec44344c7dbdb96f17752c3a224640b6396f51ff12009`

The baseline Targeted check included Quick and passed in 175.114 s, including
1,233 Quick .NET tests, 52 focused WPF cases, eight governance regressions, locked
restore and the warnings-as-errors Release build. Run it from the committed
checkout with the pinned Python dependencies. The checkpoint Quick rerun passed
in 109.079 s. Bundle and repository validation were repeated after the task
record changed. The validator used its structural checks because the optional
JSON Schema validator was absent.

~~~powershell
.\eng\verify.ps1 -Tier Targeted -Restore -TestProject tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj -Filter 'FullyQualifiedName~PackageSetupTests|FullyQualifiedName~RebootRecoveryTests'
.\eng\run-m4-package-fixtures.ps1
~~~

| Selection | Tier | Elapsed | Outcome |
| --- | --- | --- | --- |
| FI-002 transfer and WPF recovery | Targeted | 1.897 s and 4.029 s | Partial staging was cleaned. The saved plan and approval remained available after connection loss. |
| FI-006 authorization decline | Targeted | 4.109 s | The modeled decline prevented the apply outcome from being treated as verified. |
| FI-008 installer success with missing or wrong registration | Targeted | 3.170 s | Independent verification withheld success. |
| Saved workshop report | Targeted | 6.142 s | Reopen preserved the approved package journal and the report matched. |
| FI-004 application loss during and after apply | Extended | 5.922 s and 5.125 s | Reopen retained the ambiguous attempt in review and verified the durably applied attempt without dispatch. |
| FI-005 owned backend loss | Extended | 3.801 s | Reopen re-observed the interrupted attempt and preserved review. |
| Modeled Windows boot | Targeted | 4.737 s | Same-boot recovery waited. Changed boot evidence allowed verification of the saved attempt. |
| FI-012 checkpoint commit faults | Extended | 2.732 s | Verification and token consumption recovered together. |

All ten fixture selections passed. They use isolated SQLite, owned fixture processes
and bounded adapters. They cover simulated consent, interrupted download and restart
logic. The recipient view at this stage is a test-only
[draft](../evidence/TL-0410/recipient-draft.md). TL-0605 owns the product recipient
guide. Technical WPF checks do not establish perceptual assistive-technology behavior.

## Native procedure

Use the exact VM and clean checkpoint IDs from the reviewed profile. Retain the
bound IDs, job reference and authorization in the protected local record. Keep
workshop output and raw guest evidence local. Publish only bounded pass/fail outcomes,
public artifact digests, durations and limitations.

1. Run `eng/run-m4-package-preflight.ps1` with the reviewed VM and checkpoint IDs.
   Use `-CheckPackageSource` to collect the optional pinned-source HEAD result.
   An egress, platform, checkpoint, package state or trust mismatch blocks the
   affected native case.
2. Use `eng/run-m4-package-desktop.ps1 -Inspect` with the same IDs and reviewed
   Release apphost and UI digests. This exercises the normal unelevated WPF
   entry point on the Basic console. With attributable no-donor-storage
   confirmation, use `-Preview` with the actual attestation date and operator
   code. It creates one guest job, saves the intake, runs the live assessment
   and reads the fully resolved package plan without approving it. Use
   `-ResumePreview -Job` to continue the existing job if needed.
3. Obtain operator authority for the displayed plan digest and the exact
   target and action, then approve and dispatch in the ordinary application.
   The pinned route is Notepad++ 8.9.9.1, source
   `notepad-plus-plus-releases`, x64 vendor MSI, machine scope, SHA-256
   `e3b0607a092ce2604fbdac027db2c94915995e8e568ebd226f8c5493c2f61a80`.
   Windows owns any secure-desktop choice. A separate live decline case needs
   its own applicable authorization and an actual Windows prompt.
4. Run the [native interruption and restart procedure](../../docs/testing/failure-injection.md#tl-0408-native-package-route-and-windows-restart).
   The installer-client interruption is a separate transaction. Require durable
   dispatch intent, the exact local app-owned process, candidate path, expected
   installer arguments, correlation and confirmed phase. Use a verified process
   handle within the bounded window. Leave shared installer services alone.
   If attribution or timing is unavailable, record it and leave the client alone.
   The orderly guest restart follows a completed installer result and authority
   for that guest restart. A successful install without 3010 can still verify
   continuity after an actual warm restart.
5. Run the same-job `-Reopen` case with its captured job reference, reviewed plan
   digest and expected state (`Verified` or `RequiresReview`). Compare the
   reopened report, journal and fresh exact machine-scope MSI observation.
   Check that no second dispatch occurred and capture the remaining recovery and
   process state. Restore only the bound disposable guest to its reviewed clean
   checkpoint after scope-bound authorization. Re-admit it and check package,
   job, process and staging absence. Retain a guest recovery record if restoration
   is unavailable.

The isolated guest installation, native client interruption, actual Windows
restart, operator Windows consent and checkpoint restoration require separate
evidence. The M4 write-capable release decision remains with TL-0411.
