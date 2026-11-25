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
