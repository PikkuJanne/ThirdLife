# TL-0313 — No-op broker orchestration

**Date** 2026-11-25  
**Base** `8ed89d5a77022f85e3ef5b09961c7612c7b01604`  
**Tested source** `a6149608be1c97daa4954853bc7d76d11aeb0001`  
**Environment** Unelevated Windows 11 Pro 26H2 build 26300.9974 on native x64, .NET SDK 10.0.400, Python 3.14.7

## Saved approval and execution

The WPF plan review saves the exact plan and impact hashes with an operator-coded
approval. SQLite commits the approval and its action entries together. A broker
attempt requires the current approval and commits every action's started intent
in one transaction. The transport checks the approval and current plan again after
Windows consent and before sending the request. The request preserves the one-use
correlation and the original two-minute dispatch window.

The WPF and SQLite fixture showed that a declined consent result records that no
action was sent. The job takes an explicit fresh approval before retrying the same
plan. Cancellation before dispatch has the same recovery path. A correlated
successful broker result records `Applied`. A missing or ambiguously cancelled result
requires review, and any returned results that passed correlation checks remain
available. Only separate postcondition evidence can establish `Verified`.

Reopening the job showed its committed plan digest and per-action status. An
unfinished start stays pending until the dispatch window expires. Recovery closes
an expired attempt for review and leaves the job open without redispatching.
Technical accessibility checks exercised control names, keyboard focus, the live
status region and the read-only history field. Journal and UI records use bounded
identifiers and typed status values. They carry neither IPC nonces nor raw output.

## Active-machine verification

| Command or review | Tier | Result and elapsed | Scope and limit |
|---|---|---|---|
| `dotnet test tests/ThirdLife.Actions.Tests/ThirdLife.Actions.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~JournalTests` | Targeted baseline | 35 passed, 9.422 s command | Base checkout `8ed89d5`. Deterministic journal baseline. |
| `eng/verify.ps1 -Tier Targeted -Restore -TestProject` with Actions, Persistence, Broker, Broker.Security and UI test projects | Targeted, including Quick | 7 work-contract and 1,586 .NET checks passed, 139.442 s | WPF and isolated SQLite with a fixture broker. Assertions cover wrong or stale approval, approval recheck before send, transaction faults, duplicate start, no-dispatch decline, retry, cancellation, limited progress, partial results and reopen. The first formatting run stopped at working-file line endings. Restoring the repository's CRLF convention cleared the failure. |
| `eng/verify.ps1 -Tier Full -Restore` | Full | 303 governance and 2,050 .NET checks passed, 341.723 s | Clean same-machine checkout. Default Full excludes ActiveMachine, Extended and SandboxOnly. |
| UI `BrokerBatchClientTests` with `Category=ActiveMachine` | Targeted | 3 passed, 2.108 s command | Adapter and client use the actual unelevated Windows identity and an injected launcher. Decline and pending consent are simulated, with no real elevation request. |
| `eng/run-thirdlife-hyperv.ps1 -Scenario Inspect` | Targeted preflight | Passed, 3.143 s command | The existing dedicated guest and clean checkpoint admitted Windows build 26300, the Basic console session and DPAPI-backed authentication. `EnableLUA=1`, `ConsentPromptBehaviorAdmin=5` and `PromptOnSecureDesktop=1`. Read-only admission. |

The evidence update passed Quick in 86.686 s and the complete Python governance
regression with 303 tests in 251.743 s.

## Scope of protected-broker evidence

The earlier [TL-0317 guest qualification](../TL-0317/report.md) retains its actual
secure-desktop consent and decline, protected Native AOT no-op, interruption and
teardown observations. The broker executable, protocol and provisioner are
unchanged by `a614960`. The new orchestration, pre-dispatch recheck, durable job
continuity and WPF state were exercised with fixtures. Actual UAC and process kills
through the integrated application route remain unobserved. The fixture policy
admits the test plan. Admission on live hardware still follows the bundled policy.
The automation checks cover technical accessibility in the normal WPF surface.

The installed Native AOT image matched the reviewed SHA-256
`ea3d534d885b46b396e5aa8c31b81666c4fc8d5195a27e86cca464fc90bb3861`.
`eng/run-thirdlife-hyperv.ps1 -Prepare` built the fixture assemblies with locked
inputs from clean source `a614960` and prepared local payload SHA-256
`4efacbacc89c3d39933c6d61255fef4452b8ee3c2a8a91cae3489cac623eaf12` in a
task-specific cache. The guest runner still selects TL-0317's transport-level
scenarios. Integrated application verification needs a dedicated case that
composes the protected adapter with the WPF fixture and its durable job.

Ordinary startup keeps the no-op dispatch backend unavailable. The admitted
same-machine Windows 11 guest is available for controlled qualification. Bind the
integrated application route, pinned Native AOT image and exact saved no-op batch
before obtaining attributable operator approval and actual Windows consent.
Ordinary dispatch additionally requires TL-0314's
candidate-scoped privilege decision. A successful no-op reply remains `Applied`
until independent postcondition evidence exists. `Applied` and `RequiresReview`
continue to prevent another execution approval in the affected job until resolved.

## Controlled application qualification

The TL-0313 cases use the dedicated Windows 11 Hyper-V guest and the pinned
Native AOT broker from TL-0317. The guest runner verifies the retained protected
installation and stages the checked-in fixture under its existing protected
test root. The interactive test runs at medium integrity and uses the normal
WPF approval and execution controls, the production broker adapter and a
disposable SQLite job. Synthetic intake and policy data resolve the Basic profile's
three essential capabilities, document editing, PDF reading and web browsing, to
compiled no-ops. The batch installs no packages and changes no settings.

Prepare the archive from the clean, committed candidate checkout. Use that same
checkout and work directory for execution. Retain the VM identity and payload
digest returned by the admitted guest and the preparation locally.

```powershell
$work = Join-Path $env:LOCALAPPDATA 'ThirdLife\HyperV-TL0313-Application'
$bundle = .\eng\run-thirdlife-hyperv.ps1 -Prepare -WorkDirectory $work
$guest = .\eng\run-thirdlife-hyperv.ps1 -Scenario Inspect -WorkDirectory $work
$binding = @{
    ExpectedVMId = $guest.VMId
    PayloadSha256 = $bundle.PayloadSha256
    WorkDirectory = $work
}
.\eng\run-thirdlife-hyperv.ps1 -Scenario ApplicationPreview @binding
```

`ApplicationPreview` loads the displayed plan and verifies its identity and
reopen behavior without saving approval or requesting elevation. For the live
cases, start `ApplicationAccept`, `ApplicationDecline` or `ApplicationCancel`
with the same runner and binding in an attended Basic VMConnect session. Each run creates its own job and
reports the plan and impact hashes. The operator reviews the displayed plan,
enters their operator code, saves approval and chooses **Run approved no-op**.
After the runner reports the consent process, the operator makes the Windows consent choice for the displayed
`ThirdLife.Broker.exe` prompt. The saved approval belongs to that exact run.
Keep the guest result local and record the visible Windows choice and the
observed journal and reopen outcomes with their respective provenance.

Application acceptance expects one correlated `Applied` result per action. Decline
expects pre-dispatch authorization failures, a reopened job and an available
fresh approval. Cancellation uses Windows **Yes** to reach the authenticated
pre-dispatch pause, where the fixture invokes the WPF Cancel control. The broker
reply carries no independent postcondition verification.
The ordinary product startup remains gated by qualification and the applicable
TL-0314 privilege decision.

### Follow-on verification on 2026-11-25

The follow-on worktree started at the merged PR #44 head `388a75d`. The initial
PlanReview and BrokerBatchClient test selection passed 25 tests in 31.690 s on
the unelevated Windows 11 x64 host. Source `da3b01b` adds the bounded guest cases
and preserves the TL-0317 executable, protocol, provisioning and ordinary
startup composition.

| Command or review | Tier | Result and elapsed | Scope and limit |
| --- | --- | --- | --- |
| `eng/verify.ps1 -Tier Targeted -Restore -TestProject` with UI, Actions, Broker, Broker.SecurityTests and Persistence | Targeted including Quick | Passed 7 governed checks and 1,587 .NET tests in 149.902 s | Follow-on source with locked SDK 10.0.400 and Python 3.14.7. The default deterministic filter excludes the interactive guest cases. |
| `eng/verify.ps1 -Tier Full -Restore` | Full | Passed 303 governance and 2,051 .NET tests in 453.300 s | Clean same-machine worktree at `da3b01b`, with locked restore, formatting and a warning-free Release build. Default filters exclude interactive guest cases and the independently checked Extended interruption scenario. |
| PowerShell parsing and synthetic application trace cases | Focused | Passed on PowerShell 7.6.9 and Windows PowerShell 5.1 | Exercises matching approval digests, typed observations and rejection of invalid or unapproved traces. The host missing-VM-ID guard was invoked in PowerShell 7. |
| `eng/run-thirdlife-hyperv.ps1 -Prepare` | Qualification preparation | Passed in 28.771 s | Committed source `da3b01bbc4a51dddfc521683d2ae827fcec059d3`, payload SHA-256 `3b80ab2d5fcddfb82fc5b70dfbd1fe3dbc8a1f8ecbf042edeb2f650e0c956987`. The archived Native AOT broker and SDK matched the existing pins. |
| `eng/run-thirdlife-hyperv.ps1 -Scenario ApplicationPreview` | Targeted active-machine guest | Passed in 113.876 s | The limited-token WPF review displayed the exact three-action plan. SQLite reopened the same plan and impact with zero approvals and zero journal entries. Protected broker verification and owned guest cleanup passed. No consent process or broker dispatch was observed. |

The approval-free preview binds only its own synthetic job. Each live case
creates a fresh exact plan, requires its own operator approval in WPF and uses
the operator's separate Windows consent choice. Preview readiness provides no
privilege authorization for ordinary startup.

The `ApplicationAccept` attempt used the same source and payload. The guest
displayed its saved plan and requested operator input. The run timed out after
260 s including stage and cleanup, with `CandidateReady` as its only observation.
The run produced no confirmed approval, consent process or dispatch result.
Protected-broker verification and owned guest cleanup passed. Reopen the live
work with a newly staged plan during an attended operator window. The previous
run's plan and approval window have expired.

### Actual WPF client interruption and recovery

The [Extended scenario](../../../TESTING.md#6-test-manifest-and-reusable-runners)
opened an owned, unelevated WPF fixture process. The UI saved the synthetic
operator approval and the actual coordinator committed each action's durable
intent. The backend held at that boundary without a broker connection or send
path. The parent read the held job independently in SQLite. After the parent's
acknowledgment, the exact WPF client terminated itself and the parent verified
the client exit and the expected nonzero test wrapper exit.

A new WPF workspace reopened that database and displayed the same plan and
started entries. Advancing the fixture clock to the dispatch-window deadline
and using **Check recovery** added one correlated `RequiresReview` entry per
action. A second reopen added no entries and the reopen backend saw zero calls.
The run's temporary directory was removed. Timing at the deadline is controlled
fixture evidence. TL-0317 retains the actual protected broker termination and
teardown observation in the separate transport case.

The focused Extended invocation passed in 13.558 s. The integration worktree
then ran Targeted with Quick and the named Extended test. All 7 governed
checks and 1,198 .NET tests passed in 142.916 s. The test source file
`PlanReviewTests.BrokerInterruption.cs` had SHA-256
`5ab20dfefac892af7191fc67790ddf2d9f2fb1a3b24dbac1352d54d9ad72ed38`.
