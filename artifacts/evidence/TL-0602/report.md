# TL-0602 workshop artifact detection

## Detector checkpoint

The Windows detector reads fixed metadata for temporary battery-report files, protected workshop report copies and ThirdLife installer staging directories. It checks only exact local account and SMB-share names supplied for one scan. A fixed set of Windows settings covers the local Remote Desktop setting and the two Remote Assistance policies. The result contains only an enumerated source and state. A scoped absence leaves the wider workshop check open. [Local account lookup](https://learn.microsoft.com/en-us/windows/win32/api/lmaccess/nf-lmaccess-netusergetinfo) [Local share lookup](https://learn.microsoft.com/en-us/windows/win32/api/lmshare/nf-lmshare-netsharegetinfo) [Remote Assistance policy](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-remoteassistance)

Directory inspection stays on a fixed local volume, pins at most 32 path components, checks up to 512 direct entries per source, and uses a five-second scan budget. It verifies matched entry type and path without reading file contents. The named target limits are 16 distinct accounts and 16 distinct shares. An unconfigured scope, access denial, unsafe entry, timeout, or exhausted budget remains explicit. Tests preserve seeded decoys and application artifacts.

| Check | Result |
|---|---|
| `dotnet test tests/ThirdLife.Inventory.Tests/ThirdLife.Inventory.Tests.csproj --configuration Release --no-restore --filter '(FullyQualifiedName~WorkshopArtifactDetectorTests\|FullyQualifiedName~InventoryReadOnlyBoundaryTests\|FullyQualifiedName~BatteryReportWorkspaceTests\|FullyQualifiedName~SecurityBaselineProviderTests)&Category!=ActiveMachine'` | 88 passed in 0.703 s. Native file metadata ran against disposable local fixture directories. Account/share and unavailable source states were controlled. |
| `eng/verify.ps1 -Tier Targeted -TestProject tests/ThirdLife.Inventory.Tests/ThirdLife.Inventory.Tests.csproj` with the same explicit filter | Passed in 198.966 s on 2026-11-27 using Windows 11 Pro x64 build 26300, .NET 10.0.400 and pinned Python. Quick ran 1,368 tests. The 88 selected Inventory tests passed again in 0.617 s. The WPF selection occupied 117 s within this run. |

The detector verification used base `7615a339a873613e87827c0530d2448b039f4ce6` with source tree `9fea40cdabd65565d8043a496233b3ab4041c978`. The first governed run caught two new read-only native lookups and a duplicate buffer release missing from the exact import inventory. The second run passed after registering the lookups and reusing the existing buffer release. The isolated detector checkpoint covers fixture behavior and guarded Windows file metadata. Handover wiring, persistence, and native account/share observation require their own evidence.

## Handover and persistence candidate

Handover lets an operator supply a short list of exact workshop account and local share names for a single scan. It clears the input fields before the call and saves the job-scoped findings, detector revision, evidence origin and UTC time. An exact allowing sanitization decision gates the read and is checked again in the scan write transaction. The scan uses unelevated, read-only sources. Cleanup and operator confirmation use their own authorized procedures. The normal UI displays explicit availability and scoped findings with manual review guidance.

Each saved scan refreshes the context of the five affected checklist rules and preserves earlier confirmations. A temporary battery report or package staging indication stays open through an unavailable rescan and requires a scoped absence from the same detector revision before the associated checklist can clear. A different supplied account or share list cannot clear a previously saved indication. Report copies and remote-access settings require attributable workshop review. The wider manual check remains separate from scoped absence.

A Release warnings-as-errors solution build passed. The focused `FinalizationChecklistTests` selection passed 12 tests in 0.144 s. `FinalizationArtifactStoreTests|FinalizationStoreTests` passed five tests in 3 s, covering version 15 migration, reopen, retained residue, stale review, distinct jobs, an unsanitized job, archive state and mixed evidence origins. `AuditArtifactScanTests|AuditFinalizationTests` passed two WPF tests in 9 s, including input discard, UI Automation name and status, and reopening the same saved scan. These ran on the active Windows host against candidate tree `d425b53d135c18f2bdbd79811d4bd345a4a2b4ea` with a staging copy of TL-0603 migration 016, SHA-256 `49b03190fa2cbc2ec931270064386e3a6c412d9215814dabd34e524a8e76c459`. Exact integrated migrations still need the clean-checkout gate.

The active-host `ActiveMachineUsesNativeReadOnlySourcesWithSyntheticAbsentNames` scenario passed in 0.112 s. It observed no indication for two synthetic exact account/share targets, temporary battery reports, package staging or the local Remote Desktop setting. It detected files matching the report pattern in the registered workshop report folder. Both Remote Assistance policy readings were unavailable. Positive native account and share cases remain unobserved. The Handover and database tests use controlled adapter outcomes. Actual handover and release decisions remain at their existing gates.

## Combined candidate

The exact TL-0603 source `f39667f1a94bdeb6871fa758476e368fe7d47ca3` supplied tracked migration 016 with the same SHA-256. The Release solution build passed in 14.44 s. Combined finalization and recipient-handover selections passed 22 Core tests in 0.169 s and seven Persistence tests in 4 s. The first combined WPF run found a deferred Handover refresh could disable the scan control between test readiness and invocation. The scan now requires a completed checklist read for the visible stage and current job version. The rebuilt `AuditArtifactScanTests|AuditFinalizationTests|AuditRecipientHandoverTests` selection passed all three WPF cases in 9 s.

`eng/verify.ps1 -Tier Full -Restore` ran from a clean active-host checkout of `c1f48bfddd003ca678ddbf0df76f03f709417ec6`. It passed 310 Python governance tests in 620.129 s, bundle and repository validation, locked restore, formatting, and the warnings-as-errors build. The .NET test result was 2,587 passed and one failure after 914.412 s of total verification. The failing `WheelOverReportMovesItsReadingPaneWithinOnePulse` fixture forced the preview control visible without an opened report. A real Handover state refresh restored the hidden preview, leaving the fixture without an inner scroll viewer.

## Workshop record integration

The candidate incorporates main `515a6d6fbcc4b34828256c3bbdace4e59e6d7d07`. The technical workshop record now includes saved scan origin and detector revision, typed source categories and states, and current or earlier context. Outstanding indications remain visible next to their blocking checklist rules. Report publication rechecks the saved scan sequence and rejects evidence from a different type of live or synthetic workspace. Exact local scope names stay in the original scan input only.

The wheel fixture now generates and opens an actual report before exercising the preview. It retains the original offset and outer-pane assertions. The combined Release build passed in 13.83 s. The complete Reports project passed 40 tests in 0.580 s. The WPF selection `AuditArtifactScanTests|WorkshopFinalizationReportTests|WheelScrollingTests` passed seven tests in 9 s. It exercised reopening the same saved scan, generating its workshop record, confirming the entered names were absent from the preview and saved bytes, and scrolling the real preview pane. Bundle and repository checks passed.

The governed `eng/verify.ps1 -Tier Targeted` report and WPF selection passed in 271.033 s at source tree `08b0b722f02c94c667d534403142844b2f27e8b5`. Quick passed 1,394 tests. Targeted then passed 30 report checks and seven WPF checks. The Quick WPF selection took 149 s. The run used the active Windows host with the same pinned SDK and Python environment as the detector checkpoint.

The next candidate incorporates reviewed main `244e51dd4e26a10179331877fe5a898fb39f8f4b`, including the system-disk provider, integrated recipient mode and keyboard focus repair. Migration 016 retained its exact SHA-256 and 017 follows it. Bundle and repository validation passed. The Release build passed in 13.99 s. Focused merged checks passed 25 Inventory tests in 0.906 s, four Persistence tests in 3 s, and eight WPF tests in 12 s. The read-only native inventory checks covered both the system-disk typed boundary and the two exact local artifact lookups.

## Clean migration verification

The corrected candidate `0c53597f68a188eb14747c14a7c5600061955dbc` passed
`eng/verify.ps1 -Tier Full -Restore` on the active Windows 11 host in a
separate clean detached worktree. Full took 502.462 s. All 310 governance
tests passed in 332.690 s. All 2,611 deterministic .NET tests across 13
assemblies passed with zero skips. The locked Release build, formatting,
both repository validators and the report-wheel regression passed.

The Full filter retains separate scopes for native and Extended tests.
The exact-name and fixed-path proof remains bounded by the source results
recorded above. The detector leaves cleanup and whole-device handover to
their owning workflows.

## Main integration candidate

The candidate against reviewed main `c06e9e145669a36651550e3ab5bd734ee94f7284`
passed `eng/verify.ps1 -Tier Targeted` in 227.458 s on the active Windows 11
host. The Release build and repository validators passed. All 1,409 Quick
.NET tests and 25 selected report and persistence tests passed with zero
skips. No additional native cleanup or dispatch took place.
