# Retention and cleanup

Production retention operates on the named records below. The policy's remaining
aged retention and deletion defaults are implementation requirements for later
lifecycle work. [Approved privacy guidance](privacy/privacy-model.md)

| Record | Current behavior |
| --- | --- |
| Job, approvals, journal, policy snapshots, verification and finalization | In **Jobs**, archive the open job to keep its history available in read-only state. Reopen it by code and choose **Restore archived job** to resume work. Automatic job expiry and an explicit job-data deletion choice remain pending. |
| Local workshop report and recipient guide copies | The two protected folders each have a limit of 64 files, 64 MiB total and 8 MiB per artifact. Destinations that already exist are preserved. Staged writes only clean their owned temporary file. A full folder needs review of retained copies. Jobs can check the exact recorded report and guide copies of an archived job in the registered roots. Earlier unrecorded copies are outside that check. |
| Sanitized logs | The store enforces age and the consuming subsystem's byte ceiling during writes and supported cleanup. It handles owned temporary and transaction files with schema, root and ownership checks. The package sink uses 14 days and 1 MiB. |
| Battery and package staging | Operation cleanup is limited to verified source-owned files. The battery workspace reconciles its prior ownership marker. The package runtime removes the verified installer and attempts nonrecursive removal of its own empty directory. Failures remain reportable. There is no shared sweep of unrelated folders. |
| Harmless backup restore samples | The protected original folder accepts up to 16 samples and preserves earlier files. Jobs uses the archived job's saved preparation records to check exact original samples, including preparations interrupted before an outcome was recorded. Each check reads at most 4 KiB per file. Files stay in the registered folders after Handover closes. Windows-recovered copies and external backups follow their destinations' retention. |
| SQLite migration and recovery | Migrations use a strict ledger and guarded transactions. Job opening detects unexpected schema or corruption and preserves the source on failure. Initialization stages and the SQLite rollback journal are implementation files. Normal guarded commits empty the rollback journal and retain its file identity for recovery. Existing idle journals are handled when SQLite processes the next write or recovery. A separate user-managed recovery-copy workflow and its retention timer remain pending. |
| Support and configuration archives | Exact ZIPs are limited to 256 KiB. They publish beneath an explicitly chosen local folder. Support publication stores an attributed receipt in the job. Recovery of an unconfirmed publication verifies the stored digest against the existing copy. Copies follow the destination's retention policy and remain there after ordinary application removal. |
| Exported or printed workshop and recipient copies | The owner of each destination or physical copy is responsible for access and retention. Workshop files remain restricted for the workshop's retention period. |

The privacy limits exclude retained raw provider or installer streams, secrets
and sibling-private content. Any later job-data deletion must be explicit and
operate only on verified ThirdLife-owned roots and exactly attributed artifacts.
Archive, application removal and job-data deletion are separate operations.
[Root inventory](data-locations.md)
