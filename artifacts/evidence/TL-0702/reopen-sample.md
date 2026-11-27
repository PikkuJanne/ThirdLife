# TL-0702 reopen the exact harmless sample

After closing a workshop job, TRACE retains the typed sample history.
For the current assisted handover, the operator can review the current
eligible destination, confirm the exact local scope with the recipient
and enter their workshop operator code. **Use saved harmless sample**
reconstructs the recorded sample and rechecks the protected original's
length and SHA-256. It leaves the original and recovered folders
untouched. It declines stale scope, an unconfirmed intent, a missing
original or a changed original.

Preparation remains attributed to the original operator. The resumed
copy result is attributed to the operator who reconfirmed the current
review. The protected store retains both matching results if different
operators performed them. An exact repeat by the same operator remains
idempotent. Earlier handovers and sealed handovers cannot use the
previous review. A matching separate-file check leaves backup pending.

TRACE explains how the recipient can include the exact harmless
sample folder in a File History library and restore to the separate
folder. It leaves Windows configuration and restore with the
recipient. [Windows File History instructions](https://support.microsoft.com/en-us/windows/experience/backup-recovery/backup-and-restore-with-file-history)

On 27 November 2026 the native Windows 11 x64 host ran
`eng/verify.ps1 -Tier Targeted -Restore` at
`c3284b46dfc94c2b30b2f129a08476b5d7e1d0b0`. The run passed 1,447
Quick and 38 selected tests in 249.915 s. The selections covered
File History core review, protected sample storage, the SQLite sample
journal, workshop reports, Backup readiness and the exact read-only
native drive checks. Seven named keyboard, focus and Handover cases
passed separately in 11 s of test time on the same source.

The WPF test closed and reopened the job, rejected a missing
confirmation or operator code, reconstructed the original sample,
then verified a separate fixture copy without creating a duplicate.
It retained the first and second operators on their respective copy
results. The current workshop report reflected the saved result with
sample identity fields omitted. Guarded reader tests covered replacement
or mismatch. Windows File History configuration, a native backup and
an attributable native restoration were outside these runs.
[Sample journal](sample-journal.md) · [Workshop report](../TL-0604/backup-sample-record.md)

The same source passed a separate clean detached
`eng/verify.ps1 -Tier Full -Restore` on the active host in 557.861 s.
All 319 governance and 2,687 .NET tests passed with zero skips.
The active-machine and named Extended cases were run separately.
Final integration adds evidence and documentation over this source.
