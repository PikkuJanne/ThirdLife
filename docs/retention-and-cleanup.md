# Retention and cleanup

Production retention operates on the named records below. Automatic expiry and
the remaining time-based cleanup defaults are tracked separately.
[Approved privacy guidance](privacy/privacy-model.md)

| Record | Current behavior |
| --- | --- |
| Job, approvals, journal, policy snapshots, verification and finalization | Archive retains the job and its history for review and later restoration. A separate Jobs action permanently removes the approved archived job once its recorded local files are reconciled. It retains policy snapshots that other jobs reference. Automatic job expiry remains pending. |
| Local workshop report and recipient guide copies | The two protected folders each have a limit of 64 files, 64 MiB total and 8 MiB per artifact. Destinations already in use are preserved. The job store records generated file ownership before publication. Separate approved job-data deletion checks exact recorded copies and their corresponding staging names. Unrecorded copies remain for workshop review. |
| Sanitized logs | The store enforces age and the consuming subsystem's byte ceiling during writes and supported cleanup. It handles owned temporary and transaction files with schema, root and ownership checks. The package sink uses 14 days and 1 MiB. |
| Battery and package staging | Operation cleanup is limited to verified source-owned files. The battery workspace reconciles its ownership marker. Package cleanup covers the exact staged installer or the files prepared for independent launch verification. Only the operation-owned empty directory is eligible for nonrecursive removal. Failures remain reportable. |
| Harmless backup restore samples | The protected original folder accepts up to 16 samples and preserves earlier files. Jobs reads at most 4 KiB per recorded sample. The explicit job-data choice covers the exact generated original and any matching publication stage. Windows-recovered copies and external backups follow their destinations' retention. |
| SQLite migration and recovery | Migrations use a strict ledger and guarded transactions. Job opening detects unexpected schema or corruption and preserves the source on failure. Initialization stages and the SQLite rollback journal are implementation files. Normal guarded commits empty the rollback journal and retain its file identity for recovery. Existing idle journals are handled when SQLite processes the next write or recovery. A separate user-managed recovery-copy workflow and its retention timer remain pending. |
| Support and configuration archives | Exact ZIPs are limited to 256 KiB. They publish beneath an explicitly chosen local folder. Support publication stores an attributed receipt in the job. Recovery of an unconfirmed publication verifies the stored digest against the existing copy. Copies follow the destination's retention policy and remain there after ordinary application removal. |
| Exported or printed workshop and recipient copies | The owner of each destination or physical copy is responsible for access and retention. Workshop files remain restricted for the workshop's retention period. |

The privacy limits exclude retained raw provider or installer streams, secrets
and sibling-private content. Archive, application removal and job-data deletion
are separate operations. [Root inventory](data-locations.md)
[Uninstall and data preservation](uninstall-and-data-preservation.md)
