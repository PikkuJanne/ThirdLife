# TL-0317 Hyper-V qualification

**Date** 2026-11-25  
**Active-machine live source base** `61df5c2eae27d4e1d128965f169b90fe15dfe218`

## Historical host admission checkpoint

The owner reported the dedicated Windows 11 VM running on the active Codex
machine, with the expected local account signed in and the clean checkpoint
available. That report supersedes the earlier deferral. Codex's medium-integrity
host token received permission denied from `Get-VM` in the initial 2.731 s host
diagnostic at `634b49c`. Its token had neither the Administrator nor the Hyper-V
Administrators role enabled. This prevented binding the VM ID, reading the
checkpoint or attempting guest authentication. The access check ran on the host
only. It recorded no guest observations.

In the live continuation, the authorized host token had Hyper-V Administrators
enabled and successfully read the running Generation 2 VM and its configured
clean checkpoint. PowerShell Direct authenticated with the existing DPAPI
credential and admitted the signed-in Basic console account on Windows 11
build 26300. Guest policy reported `EnableLUA=1`,
`ConsentPromptBehaviorAdmin=5` and `PromptOnSecureDesktop=1`.
[PowerShell Direct requirements](https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/powershell-direct)

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

Reuse the archive from a previously prepared committed checkout when its
source tree and reviewed artifact digests match the verified integration
source. Run from that archive's exact payload-source checkout. Prepare a
fresh archive from a clean commit when the staged guest driver or fixtures
change. The
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

Use the authorized Hyper-V shell on the Windows host under the Windows account
that exported the guest credential. The runner loads the approved local
CLIXML file under `%LOCALAPPDATA%\ThirdLife\Credentials` when called without
a credential. Windows DPAPI binds recovery to the exporting account and
computer. For a sequence in one shell, import the `PSCredential` once and pass
the same object to each case. Keep the credential file, password and VM ID out
of evidence. [Microsoft CLIXML credential guidance](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/import-clixml#example-2-import-a-secure-credential-object)

Discover the configured VM by its profile name and pass the discovered GUID
to `Inspect`. The runner verifies that identity and the clean checkpoint,
connects through PowerShell Direct, and checks the guest Windows identity,
admin token, native x64 architecture and expected account's existing Basic
console session. It reads the UAC policy values as context for prompt observations.

```powershell
$profile = Get-Content -Raw -LiteralPath .\eng\thirdlife-hyperv-profile.json | ConvertFrom-Json
$vm = Get-VM -Name $profile.vm -ErrorAction Stop
$credentialPath = Join-Path $env:LOCALAPPDATA 'ThirdLife\Credentials\guest.credential.xml'
$guest = Import-Clixml -LiteralPath $credentialPath -ErrorAction Stop
if ($guest -isnot [PSCredential] -or $guest.UserName -ine "$($profile.computer)\$($profile.account)") {
    throw 'The credential must be a PSCredential for the configured guest account.'
}
$admission = .\eng\run-thirdlife-hyperv.ps1 -Scenario Inspect -Credential $guest -ExpectedVMId $vm.Id
```

If the secure credential needs replacement, obtain it through one attended
local `Get-Credential` prompt for the configured guest account and keep the
result in memory. The runner also provides `-PromptForCredential` for a single
attended invocation.

Run each selected scenario with `$payloadSha256` set to the exact value printed
by the trusted preparation. The runner keeps the authenticated connection for
the transfer, guest commands and result collection within each scenario. If the
session closes after staging, the runner reconnects once for owned cleanup. The
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

Open Basic VMConnect before the consent cases. Dismiss the Enhanced Session
display configuration dialog if it appears so the existing Basic session can
attach. On the live host the WMI thumbnail was black before Basic attached.
The [console helper](../../../eng/thirdlife-hyperv-console.ps1) captures the
named VM by its admitted ID and validates both raw and length-prefixed RGB565.
Visually confirm the current secure-desktop `ThirdLife.Broker.exe` Yes/No
prompt. Pass `-ObservedBrokerConsentPrompt -ObservedFrameSha256` with its
digest and either `-Decline` for Escape or `-Accept -ObservedYesX <x>
-ObservedYesY <y>` for one click on the visible Yes button. If positioning
changes the frame, capture and verify it again before retrying. Record the
automated input separately from the client outcome and broker process state.
Delete the temporary PNG after use. [Hyper-V display capture](https://learn.microsoft.com/en-us/windows/win32/hyperv_v2/getvirtualsystemthumbnailimage-msvm-virtualsystemmanagementservice) · [Virtual keyboard](https://learn.microsoft.com/en-us/windows/win32/hyperv_v2/typekey-msvm-keyboard) · [Synthetic mouse](https://learn.microsoft.com/en-us/windows/win32/hyperv_v2/msvm-syntheticmouse)

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
| Full, `eng/verify.ps1 -Tier Full -Restore` | Passed in 351.353 s in the isolated active-machine worktree. The runtime test sources at `029107f` passed 303 Python governance and 2,025 .NET tests, locked restore, formatting and the warnings-as-errors Release build. | Default Full excludes the interactive and distinct-account guest cases. |
| `eng/run-thirdlife-hyperv.ps1 -Prepare` and private-runtime smoke | Built locked Release fixtures and staged the reviewed broker from `029107f`. Host bundle SHA-256 `d4a8222003ed306fc3d51c2cc9908ed146f764fb77d9a2d8ed06f6004b470091`. A clean extraction of the SDK and staged UI assembly ran the deterministic decline-mapping test in 11.701 s wall time. | Uses the pinned private Windows Desktop runtime. The guest transfer, installed candidate and UAC dialog remain unobserved. |
| Hyper-V distinct-account admission, physical-host negative case | Refused at the guest identity check. The count of owned test account names was unchanged. | Exercises the negative guard before any account action. |
| Console helper parser and synthetic CIM fixture | Passed in PowerShell 7 and Windows PowerShell 5.1. The fixture decoded a known RGB565 pixel and verified rejection of a changed frame, missing observation and the wrong VM. The matching frame issued one simulated Escape. | Hyper-V and secure-desktop calls were mocked. The Windows PowerShell run used the exact script text in a ScriptBlock under existing execution policy. |

## Live guest observations

The initial live cases used the prepared PR #41 archive at `5534e55`.
Its source tree matched the integrated base exactly. Payload SHA-256 was
`e5cc0382d1e7742136e9d5ff226873bcbc6d33f110090ae3299a56f63f9c82d3`.
The pinned SDK and the reviewed Native AOT broker hashes matched before
transfer and the protected broker was verified by the guest provisioning
procedure in each reported case. Durations include transfer, staging,
execution and cleanup. User Account Control displayed
`ThirdLife.Broker.exe` as a local executable from an unknown publisher.

| Guest case | Targeted result and duration | Observation and limit |
| --- | --- | --- |
| `Verify` | Passed, 99.174 s | Reviewed candidate and metadata verified in protected Program Files. The unelevated harness could read but could not write either protected file and opened the production launch guard. |
| `DistinctAccount`, initial run | Failed, 83.392 s | Protected deployment and run cleanup passed. The original runner discarded the assertion detail. A scoped post-run account query found the fixture account absent. |
| `DistinctAccount`, diagnostic run at `b5ec550` | Failed, 81.522 s | `ProbeStartRejected`, test and scheduled-task exit `1`. The process-based fixture produced no verified pipe-denial result. Protected deployment and cleanup passed. The run admitted the VM through the default DPAPI credential path. |
| `DistinctAccount`, native-error diagnostic at `8ecfe22` | Failed, 84.126 s | Same failure classification and exit `1`. No native startup code was reported, so the exact failing Windows call remains undetermined. Run cleanup passed. |
| `Accept` | Passed, 159.661 s | After visually confirming the live prompt, Codex delivered one virtual mouse Yes click at 17:38:48 UTC. The unelevated client returned `Completed` with one matching `Applied` no-op and the broker exited. |
| `Decline` | Passed, 133.534 s | After visually confirming the live prompt, Codex delivered one virtual Escape at 17:41:02 UTC. The unelevated client returned `Declined` with zero terminal results and the runner found no surviving broker. |
| `Interrupt` | Passed, 111.558 s | Codex observed the prompt and clicked Yes at 17:42:55 UTC. The authenticated, owned broker reached the pre-dispatch rendezvous. Scoped termination returned exit `-1`, followed by `Unavailable` and zero terminal results in the unelevated client. |
| `Timeout` | Passed, 124.995 s | Codex observed the prompt and clicked Yes at 17:44:30 UTC. The broker authenticated, closed the empty session at the ticket deadline and exited `3`. The unelevated harness recorded `BrokerDeadlineElapsed` with zero terminal results. |
| `Disconnect` | Passed, 102.782 s | Codex observed the prompt and clicked Yes at 17:46:42 UTC. After authenticated hello, closing the client pipe caused broker exit `3`. The unelevated harness recorded `ClientDisconnected` and zero terminal results. |
| `RetainObservation`, initial run | Failed, 114.340 s | Codex observed the prompt and clicked Yes at 17:48:37 UTC. The harness recorded only the consent request, and cleanup passed. Source review showed the independent process wrapper could request broader rights when reopening the elevated process. |
| `RetainObservation`, corrected run at `8ecfe22` | Passed, 112.211 s | Codex observed the prompt and clicked Yes at 17:55:42 UTC. The unelevated client returned `RequiresReview` and retained one exact `Applied` result from before the injected receive interruption. The test observed exit `0` using query and synchronize rights retained from the verified launch handle. This exercises client-side result retention. |

Ordinary broker-dependent application dispatch remains unavailable while
TL-0317 is open.
