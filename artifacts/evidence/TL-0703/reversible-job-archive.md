# TL-0703 archive and restore from Jobs

TRACE exposes the existing reversible archive operation in **Jobs**
when a workshop job is open. The action applies to the displayed job
ID. Archived records stay available for review and become read-only
for job work. Opening the same archived job returns the saved state.
**Restore archived job** resumes use of those records and appends the
existing restore checkpoint.

The operation reuses `JobService` and the protected SQLite archive
transaction. It creates no additional data root and retains the
original job and sanitization record. Changing archive state closes
prior report previews and clears temporary handover reviews before
loading the current saved job. Application removal and eventual
explicit job-data deletion have separate behavior and remain under
the lifecycle requirements.

| Verification on 27 November 2026 | Result |
| --- | --- |
| Focused Release UI selection | 12 passed in 6 s. The added test opened Jobs, invoked archive, verified UI Automation action names, closed and reopened the same archived job, and invoked restore. It reopened SQLite and verified both lifecycle checkpoints and the unchanged original sanitization evidence. |
| Governed Targeted with locked restore | 248.312 s. 1,448 Quick, 36 Persistence, and 16 WPF tests passed. Selected checks covered guarded evidence writes, normal job workflows, archive and restore, Handover and the read-only File History review. |
| Clean detached Full with locked restore | 534.079 s. 319 repository checks and 2,688 .NET tests passed with zero skips. The code and tests come from `a8edc835da69e1cac6356bee87dbc6fae567f8d8`. |

All tests used isolated records on the active Windows 11 native x64
host. The native backup selection was read-only. Removed-file or
installed MSI data-preservation outcomes were outside these runs.
[Retention contract](../../../docs/retention-and-cleanup.md)
