# TL-0317 Hyper-V qualification

**Date** 2026-11-25  
**Active-machine source base** `634b49cb29a49aca19dd1d060ce5d85e3ec27411`  
**Task state** Blocked at Hyper-V host admission

The owner reported the dedicated Windows 11 VM running on the active Codex
machine, with the expected local account signed in and the clean checkpoint
available. That report supersedes the earlier deferral. Codex's medium-integrity
host token received permission denied from `Get-VM` in the initial 2.731 s host
diagnostic. Its token has neither the Administrator nor the Hyper-V
Administrators role enabled. This prevented binding the VM ID, reading the
checkpoint or attempting guest authentication. The access check ran on the host
only. Guest state and all guest scenario results remain unobserved.

Continue from one authorized local Administrator PowerShell session. Open the
PowerShell Direct session with the expected guest account credential entered
through the attended local prompt if it is unavailable in that shell. Keep the
credential in memory for the selected cases. This opening condition preserves
the host authorization controls and Windows UAC. Routine VM preparation and
testing are already authorized. [PowerShell Direct requirements](https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/powershell-direct)

## Reusable inputs

PR #36 is integrated through `4f6cd73`, an ancestor of the current base. The
native broker, protocol, UI launcher, reviewed provisioner and pinned build
inputs match the reconciled checkpoint `a5fcef3`. On the host the installed
native executable is 2,889,728 bytes and SHA-256
`ea3d534d885b46b396e5aa8c31b81666c4fc8d5195a27e86cca464fc90bb3861`.
The checked-in provisioner matches its reviewed SHA-256
`cd8c8d67d943872b9206fc14379df428406785d10a4b7b1122e9cf248c6a90da`.
The host publication, protected install, successful elevated no-op and two
inconclusive decline attempts retain their original evidence.
[TL-0311 host evidence](../TL-0311/report.md)

Both prior decline rehearsals used the production `runas` path and completed
an authenticated no-op. The rehearsal selection only set the expected client
outcome. It did not record what Windows displayed or how Windows granted
elevation. The guest procedure correlates a visual prompt observation with
the selected action, returned state and owned broker process state.

## Runner procedure

Prepare a committed clean checkout using the current repository SDK. The
[host runner](../../../eng/run-thirdlife-hyperv.ps1) checks the reviewed executable
and provisioner digests, builds the test assemblies and fixtures with locked
inputs, and returns `TestSource` and `PayloadSha256`. Its default cache and run
results are under the host user's local application data, outside the checkout.
The [Hyper-V profile](../../../eng/thirdlife-hyperv-profile.json) pins the x64
SDK 10.0.400 archive, which includes the Windows Desktop runtime and VSTest
needed by the staged assemblies. The guest uses a private SDK extraction.
[Microsoft SDK release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)

```powershell
.\eng\run-thirdlife-hyperv.ps1 -Prepare
```

From the authorized host shell, supply an existing secure in-memory guest
credential, or enter it once using the local `Get-Credential` prompt as shown.
`Inspect` checks the configured running Generation 2 VM and clean checkpoint,
connects through PowerShell Direct by VM ID, and checks the guest Windows
identity, admin token, native x64 architecture and expected account's existing
Basic console session. It reads the UAC policy values as diagnostic context
for prompt observations. The runner's `-PromptForCredential` is also available
for one attended invocation. Keep the returned VM ID local.

```powershell
$profile = Get-Content -Raw -LiteralPath .\eng\thirdlife-hyperv-profile.json | ConvertFrom-Json
$guest = Get-Credential -UserName "$($profile.computer)\$($profile.account)"
$admission = .\eng\run-thirdlife-hyperv.ps1 -Scenario Inspect -Credential $guest
```

Run each selected scenario with `$payloadSha256` set to the exact value printed
by the trusted preparation. The runner keeps the authenticated connection for
the transfer, guest commands and result collection within each scenario. The
guest driver checks transferred digests, uses the reviewed protected provisioner
and calls `VerifyOnly`. It schedules the UI test in the signed-in account's
interactive session with the limited token. The distinct-account test uses the
specified administrative token for its temporary local account and a separate
standard-account probe. The test validates the protected request and both the
denial and initiating-account control connection through the production pipe.

```powershell
.\eng\run-thirdlife-hyperv.ps1 -Scenario Verify -Credential $guest `
    -ExpectedVMId $admission.VMId -PayloadSha256 $payloadSha256
```

Run `DistinctAccount`, `Accept`, `Decline`, `Interrupt`, `Timeout`, `Disconnect`
and `RetainObservation` with the same binding. Start each consent case with the
Basic console ready for an observer. A guest consent-process observation
synchronizes the observer with the attempted launch. Verify the actual prompt
for the reviewed broker visually before acting, then correlate the choice with
the client result. Microsoft describes Enhanced VMConnect as Remote Desktop.
Use the Basic console already bound by admission. [VMConnect session modes](https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/enhanced-session-mode)

For `Decline`, the [console helper](../../../eng/thirdlife-hyperv-console.ps1)
can capture the named VM's live display using the same admitted ID. Once Codex
has visually confirmed the current broker Yes/No prompt, call `-Decline
-ObservedBrokerConsentPrompt -ObservedFrameSha256` with the returned digest.
The helper rechecks the live frame and sends only Escape if it still matches.
Record this as automated input delivery and check the client result and broker
process separately. Delete the temporary PNG after use and retain sanitized
prompt observations in the evidence. [Hyper-V display capture](https://learn.microsoft.com/en-us/windows/win32/hyperv_v2/getvirtualsystemthumbnailimage-msvm-virtualsystemmanagementservice) · [Virtual keyboard](https://learn.microsoft.com/en-us/windows/win32/hyperv_v2/typekey-msvm-keyboard)

The host runner emits scenario-level local JSON with source and artifact
digests, bounded observations and the cleanup result. Inspect each result,
including the scheduled task, temporary account and run-directory cleanup as
applicable, and whether the installed candidate was retained for follow-on
runs. Keep raw host or guest logs, local identifiers, credentials and images
out of committed evidence. Protect the clean baseline and export needed
evidence before any justified rollback.

## Verification at the host admission checkpoint

| Tier or check | Observation | Limit |
| --- | --- | --- |
| Quick baseline, `eng/verify.ps1 -Tier Quick -Restore` | Passed in 115.724 s on the integrated base. Core 126, Inventory 927 and UI 140 .NET cases. | Deterministic baseline before the Hyper-V runner. |
| Focused provisioner, `tools.tests.test_broker_deployment` | Six checks passed on the active Windows host. | Local validator and provisioning fixtures. |
| Focused UI broker client and installed launcher tests | Thirty checks passed on the active Windows host. | Deterministic result mapping and launch guards. |
| Targeted, Broker, Broker Security and UI projects | Passed in 81.295 s on the active Windows host. Included Quick and 81 selected broker tests. | Deterministic suite. Named guest cases remain separate. |
| Hyper-V distinct-account admission, physical-host negative case | Refused at the guest identity check. The count of owned test account names was unchanged. | Exercises the negative guard before any account action. |
| Console helper parser and synthetic CIM fixture | Passed in PowerShell 7 and Windows PowerShell 5.1. The fixture decoded a known RGB565 pixel and verified rejection of a changed frame, missing observation and the wrong VM. The matching frame issued one simulated Escape. | Hyper-V and secure-desktop calls were mocked. The Windows PowerShell run used the exact script text in a ScriptBlock under existing execution policy. |

| Guest case | Current result | Required observation |
| --- | --- | --- |
| `Verify` | Unobserved | Reviewed candidate and metadata verified in protected Program Files. Unelevated writes denied and the production launch guard opened. |
| `DistinctAccount` | Unobserved | Temporary standard account denied, initiating account admitted, owned account and test resources removed. |
| `Accept` | Unobserved | Guest Windows consent attributed to the reviewed prompt, then one matching applied no-op and clean broker exit. |
| `Decline` | Unobserved | Actual Windows decline attributed to the reviewed prompt, then `Declined`, no terminal action and no remaining broker. |
| `Interrupt` | Unobserved | Owned elevated broker stopped at the pre-dispatch rendezvous, with `Unavailable`, no terminal action and verified exit. |
| `Timeout` | Unobserved | Protected broker authenticates then exits at its deadline while awaiting a scoped no-op batch. |
| `Disconnect` | Unobserved | Scope-bound authenticated client closes the connection and the broker exits without dispatch. |
| `RetainObservation` | Unobserved | The production client preserves the first accepted terminal observation and returns `RequiresReview` when its receive loop is interrupted. |

Ordinary broker-dependent application dispatch remains unavailable while
TL-0317 is open.
