# TL-0603 assisted and sealed handover

TRACE Handover stores the selected mode for the current workshop job.
Assisted handover records typed guidance choices for six setup areas after
the operator confirms that the recipient is present and chose the result.
Sealed handover displays every recipient-specific area as pending. The
workshop can continue the machine-level checklist without creating a
personal account, applying preferences, or handling recovery material.

Migration 16 stores the mode and assisted guidance history in the restricted
job store. Records contain the job, sequence, mode, optional setup area and
choice, presence confirmation, workshop operator code and UTC time. The UI
accepts no recipient name, private preference value, credential, backup
destination or recovery key. Saved guidance describes a choice and never
authorizes or verifies a device change.

A mode transition starts a new guidance view and invalidates earlier
finalization confirmations. Earlier records remain auditable. A guidance
choice in the same assisted mode leaves checklist freshness unchanged.
Existing schema 15 confirmations retain their validity until an actual
mode selection or another relevant evidence change. Archived jobs are
read-only. A change of Windows boot holds the finalization checklist and
keeps the saved handover mode and choices.

## Verification

The implementation base is the TL-0601 checklist checkpoint `be60699`.
Checks ran on the active Windows 11 Pro 26H2 native x64 host with .NET SDK
10.0.400 on 27 November 2026.

| Check | Observed result |
| --- | --- |
| Focused Core, `FullyQualifiedName~RecipientHandover` | 10 passed. Checked assisted and sealed modes, presence, mode changes, historical choices and typed projection. |
| Focused Persistence, `FullyQualifiedName~RecipientHandoverStoreTests\|FullyQualifiedName~FinalizationStoreTests\|FullyQualifiedName~FullMaintenanceReopensAtEveryBoundaryAndRequiresFreshStateAfterFinalRestart` | 10 passed in 16 s of test time. Checked migration, unchanged records before a mode selection, changed-generation recheck, invalid writes and reopen. |
| Focused WPF, `FullyQualifiedName~AuditRecipientHandoverTests\|FullyQualifiedName~AuditFinalizationTests` | Both cases passed in 11 s of test time. Through normal Handover controls, the test rejected absent recipient presence, saved an assisted backup-guidance choice, changed the mode, held the earlier checklist check, and reopened a sealed job with six pending choices. |

The WPF test used isolated SQLite and modeled the recipient acknowledgement.
No actual recipient choice, account, backup, recovery material, accessibility
setting or operating-system setup was applied. TL-0605 owns the recipient
guide and TL-0607 owns the complete handover decision.

The locked Targeted run passed in 409.475 s, with 1,599 .NET cases and
eight work-selection regressions. Clean Full at `f39667f1a94bdeb6871fa758476e368fe7d47ca3`
passed in 887.559 s with 310 governance regressions and 2,571 deterministic
.NET cases across 13 assemblies. The governance suite took 475.607 s and
the WPF suite took 236 s while other engineering checks used the active host.

The report integration base is main `515a6d6fbcc4b34828256c3bbdace4e59e6d7d07`.
The existing workshop artifact remains stored when a mode or guidance
choice changes. The visible preview is cleared so the next report uses the
current checklist. The combined focused WPF selection
`AuditRecipientHandoverTests | AuditFinalizationTests | WorkshopFinalizationReportTests`
passed all three cases in 7 s, including the modeled change from an existing
workshop report to a required checklist recheck.

The report-integration Quick passed in 288.222 s with 1,379 .NET cases
and eight work-selection regressions. The 120 s target was exceeded
while other branch checks ran. WPF took 126 s.

With the system-disk provider from main `4eb5f4f`, the repository
validators and ten focused application tests for handover mode, the
workshop record and disk policy projection passed. Test time was 10 s.
