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
