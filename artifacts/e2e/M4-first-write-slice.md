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

## 26 November 2026 preview runner correction

PR #53 integrated the original harness as `3d273b5`. The pinned profile's plan
describes an exact installed-package check and a short launch without documents.
The old driver expected identity-only text and stopped before returning a valid
plan digest. The corrected driver
checks exact displayed lines for the reviewed route and validates the digest
header and the saved intake date.

A Windows PowerShell 5.1 probe ran the `Read-Plan` function extracted from both
script versions against controlled UI Automation values using the production
verification text and pinned catalogue. The original rejected the expected text.
The corrected function returned the digest for CRLF and LF and refused a longer
version, missing launch verification and an overlong digest. This probe exercised
the reader without creating a guest job.

Read-only preflight at `3d273b5` passed again in 4.822 s on the admitted dedicated
guest. Windows Installer reported the reviewed package absent and no active
installer. The optional source check returned the expected length. Host queries
found one virtual disk and no pass-through disk or mounted DVD in both the running
VM configuration and the reviewed checkpoint. That describes the attachments at
inspection. At that checkpoint the live intake still needed the operator's
attributable confirmation of lawful ownership and no donor storage.

`eng/run-m4-package-desktop.ps1 -Inspect` passed with the corrected driver and a
locked Release build from `3d273b5`. The wrapper took 57.001 s including
preflight, transfer and cleanup. The unelevated application opened the audit
workspace, showed the closed intake gate and exited normally. A separate
PowerShell Direct check took 2.514 s and found no ThirdLife application or
installer process, runner task, staging directory or job-store directory.

Continuation artifact SHA-256 digests

| Artifact | SHA-256 |
| --- | --- |
| Release apphost | `60768deea5b96f46efc5395af150c677204c99f7bc9255f7f2858cdc4d56094b` |
| Release UI assembly | `d0de8fe3909c537ffe3aedc9ad986d58a2ef697a2d33265a38ff518385f8604a` |
| Corrected driver | `eaaf2a987bd8f072919ee348fcaba825995ab6ca9d5eb9d76f03dc65126c1275` |
| Staged bundle | `3d754b78904d38849bb863216ba422ce41a5dcc4ff1fe9a47dec4999c049b45d` |

The new worktree's first Targeted invocation stopped before testing because
PyYAML was absent. Installing the pinned requirements in its local virtual
environment resolved that. The first test selection took 188.568 s and included
four guest-only `PlanReviewTests` by name. They refused the host as designed.
The deterministic rerun passed in 144.860 s with 1,233 Quick .NET tests,
57 focused WPF cases and eight governance regressions. It excluded
`ActiveMachine`, `Extended` and `SandboxOnly` selections.

~~~powershell
.\eng\verify.ps1 -Tier Targeted -TestProject tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj -Filter '(FullyQualifiedName~PackageSetupTests|FullyQualifiedName~RebootRecoveryTests|FullyQualifiedName~PlanReviewTests)&Category!=ActiveMachine&Category!=Extended&Category!=SandboxOnly'
~~~

## 26 November 2026 live guest preview

The operator confirmed lawful ownership and no donor storage for the dedicated
guest and its reviewed clean checkpoint. The guest intake uses the date of that
confirmation. A generated local operator code links it to the protected
attestation. Codex operated the unelevated UI on the operator's behalf.

The initial `-Preview` created a durable job and stopped before recording intake.
The wrapper exited after 48.900 s and cleaned its stage. A read-only guest check
confirmed that the job store existed and there were no app, scheduled task or
stage remnants. Subsequent runs reopened that job with the original attestation.
The first resume isolated the intake save in 52.811 s. A 66.388 s diagnostic
identified the invalid date message. The ISO text input and calendar trials
confirmed a difference between the automation process culture and the guest's
date picker. The picker displayed the selected date in British format. The
final runner selects the exact day from the calendar, reads it back as a date
and compares the saved record with the original code and date before assessment.
A partial resume attestation was refused before guest admission.

`eng/run-m4-package-desktop.ps1 -ResumePreview` passed in 46.597 s on the same
Windows 11 Pro 26H2 native x64 guest, including staging and cleanup. The live
assessment let the unelevated application display the signed Notepad++
8.9.9.1 x64 machine-wide plan. The package was absent. Installation and handover
were disabled before approval. The wrapper cleaned the stage and left the job
available. The exact job, target binding and plan digest are retained in the
protected local record for approval. The live package operation, Windows
consent, interruption, restart and restoration remain unobserved.

| Live preview artifact | SHA-256 |
| --- | --- |
| Release apphost at `20247fb` | `143289c265e36180be76d605366f59c848081a498daa30d54dad85bde5ca5763` |
| Release UI assembly at `20247fb` | `f74ee32bb94746fde7e9b2777aca91b11a6f2c6cefdcb6098684b85c40edf476` |
| Guest preview driver | `99d6d90b50796d923a918797209f8607f0a10b8ac76ece34fa85279755222ad1` |
| Staged preview bundle | `5148670d752647051118774d0598552ee65ca0f192f1ae0292b055e0be42edb9` |

The final driver reopened the same job without re-entering intake and displayed
the identical plan digest in 49.015 s including staging and cleanup. Release
app artifacts matched the earlier run. The driver SHA-256 was
`a37ef874724c3e4e499aab84db3a27bf4873ad385607b5564e2ef5a07a89424a` and bundle
SHA-256 was `96563b7fab4e976687eaba3a89ec0cd05cec34d64a1def54d23a1349f56f44ac`.
An independent read-only SQLite count and guest cleanup inspection took 4.251 s.
The job had one intake record and 164 observations. It had zero approvals or
action entries. There were no app or installer processes, runner tasks or
staging directories. The exact saved job remains available for approval.

Quick with locked restore passed in the clean host worktree in 134.357 s. It
included the initial Release build and ran alongside guest verification. The
final Quick run reused locked dependencies and passed in 110.096 s with 1,233
.NET tests, eight governance regressions and a warnings-as-errors build. The
native guest checks above establish the separate UI Automation observations.

## 26 November 2026 scoped guest dispatch

The operator approved one installation against the exact displayed plan and
authorized an orderly restart of the same guest after an installer result.
The authorization and target binding are in the protected local record.
The preflight re-observed package absence and the expected source length. An
independent journal read found zero approvals and action entries before launch.

The `-Install` runner reopened the saved job using the reviewed Release
artifacts from `20247fb`, compared the displayed digest, saved approval through
the ordinary unelevated application and invoked installation once. A read-only
SQLite query verified exactly one approval and one dispatch intent for the same
plan, impact, action and approval. A PowerShell Direct query matched the owned
Windows Installer client, its approved machine-scope arguments and the exact
staged MSI hash. It also found a Windows consent process in the application
session. The host thumbnail was black, so this evidence establishes the
consent-process observation. The operator was asked to inspect the Basic
console and make the Windows choice there.

The first process monitor looked for `/i`. The corrected query used the
production `/package` switch and checked the MSI path, digest, executable and
all expected installer arguments.

The desktop wrapper returned after 639.265 s without a verified installer
result and retained the guest stage. App observation elapsed 613.852 s. A
separate read-only journal check confirmed `RequiresReview` with `TimedOut`
for the single correlated dispatch. It found no applied or verified result
and no restart checkpoint. Independent MSI observation still reported package
absence and unchanged boot time. One console installer client, one consent
process, the unelevated app, runner task and stage remained. The runner guard
also preserves the app when it sees a review-required status before its own
observation deadline. Resolve the existing Windows consent and observe the
client exit before same-job reconciliation or a restart. The native installer
interruption and checkpoint restoration retain their own authorization
requirements.

The guest driver SHA-256 was
`0aaf83a02585128982c24ad33fdbe608fe9398ab4cbcdddfec9180580bc7dd9a` and bundle
SHA-256 was `7ba7c9e8d305fff8542679aee2b64cf952d61132874d0476c0c80d7e0f3498cb`.
Quick with locked restore passed on the active host in 145.491 s with 1,233
.NET tests, eight governance regressions and a zero-warning Release build. The
build took 28.39 s and the UI test selection took 57 s during the concurrent
guest run. PowerShell parsing and native scheduler settings checks passed.
Targeted verification passed in 144.452 s with Quick and 46 focused
deterministic package and recovery cases. A malformed Install plan digest was
refused by parameter binding before guest admission.
Quick passed again in 102.991 s after tightening the app-retention guard.

## 26 November 2026 consent follow-through and recovery

The operator reported choosing Yes in the guest for the existing Windows
Installer prompt. PowerShell Direct observed no remaining consent process or
console installer client. Windows Installer events 1040 and 1042 matched the
bound MSI path and recorded the transaction's start and end. The available
product events and the client-exit audit supplied no installation return code.
The independent Windows Installer query found the exact machine package absent.
The durable journal retained one approval, one dispatch intent and a
`RequiresReview` result with `TimedOut` on that same attempt.

The package backend had removed its own temporary installer artifact. A
one-shot limited interactive task closed the retained WPF process normally
after matching its stage, executable digest, PID, start time and session.
The cleanup inspected the registered session-zero Windows Installer service
separately from console clients and left the service alone. It removed the
exact completed runner task and stage in 12.190 s.

Before restarting, `eng/run-m4-package-desktop.ps1 -Reopen -ExpectedRecovery
RequiresReview` passed in 86.727 s using the original reviewed Release
artifacts from `20247fb`. The ordinary app rechecked package registration,
kept the job in review, and generated a workshop report that agreed with the
saved plan and journal. Installation verification and handover remained
unavailable. A read-only check after reopening took 4.197 s. It found an
unchanged journal, zero app, consent and console installer processes and zero
runner stages. The reopened bundle SHA-256 was
`0789091e554e1aff90ef369f27931d45a3fda6252aea37032072c3a96b2c3ec7`.

The first `Restart-Computer` request produced no shutdown event or boot change
within 180 s. The alternate Windows restart checked that the guest was idle,
that the earlier boot was still current and that no prior shutdown had been
initiated. The bound guest then ran `shutdown.exe /r /t 0` without forced app
closure. PowerShell Direct observed changed Windows boot time after 51.897 s.
The System log had one planned guest restart initiation in that interval.

After startup, the native MSI probe ran by PowerShell Direct and again found
the exact package absent. The read-only journal fingerprint matched the
pre-restart journal. The single approval, dispatch and timed-out review result
survived without another action entry. No app, console installer, consent or
runner stage remained. A bounded 3.430 s read of the protected report store
found one matching pre-restart report. It retained the exact plan, machine
scope and timeout, with no verification result. The new Basic console was
signed out. Post-restart WPF report observation depends on the existing guest
account signing in.

Quick with locked restore passed in 159.474 s in the isolated host worktree
while the guest checks were running. The Release build took 31.90 s and the
Quick WPF selection took 61 s. The warm Quick repeat passed in 115.046 s with
1,233 .NET tests and eight governance regressions. The bundle and repository
validators passed again after the evidence update.

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
   entry point on the Basic console. With attributable confirmation of lawful
   ownership and no donor storage, use `-Preview` with the actual verification
   date and operator code. It creates one guest job, saves the intake, runs the
   live assessment and reads the fully resolved package plan without approving
   it. Use `-ResumePreview -Job` to continue the existing job. Supply both
   original attestation parameters to complete interrupted intake or assessment.
3. Obtain operator authority for the displayed plan digest and the exact
   target and action, then approve and dispatch in the ordinary application.
   `-Install` reopens the exact job, compares the digest before and after saving
   approval and invokes the ordinary install control once. Supervise Windows
   consent in the bound Basic console and retain uncertain run state for review.
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
