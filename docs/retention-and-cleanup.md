# Retention and cleanup

Production retention operates on the named records below. The policy's remaining
aged retention and deletion defaults are implementation requirements for later
lifecycle work. [Approved privacy guidance](privacy/privacy-model.md)

| Record | Current behavior |
| --- | --- |
| Job, approvals, journal, policy snapshots, verification and finalization | Archive is reversible and makes the job read-only while archived. History stays associated with the job. There is no automatic 180-day job expiry or explicit job-data deletion command in the current application. |
| Local workshop report and recipient guide copies | The two protected folders each have a limit of 64 files, 64 MiB total and 8 MiB per artifact. Destinations that already exist are preserved. Staged writes only clean their owned temporary file. A full folder needs review of retained copies. |
| Sanitized logs | The store enforces age and the consuming subsystem's byte ceiling during writes and supported cleanup. It handles owned temporary and transaction files with schema, root and ownership checks. The package sink uses 14 days and 1 MiB. |
| Battery and package staging | Operation cleanup is limited to verified source-owned files. The battery workspace reconciles its prior ownership marker. The package runtime removes the verified installer and attempts nonrecursive removal of its own empty directory. Failures remain reportable. There is no shared sweep of unrelated folders. |
| Harmless backup restore samples | The protected original folder accepts up to 16 samples without overwriting earlier files. The exact sample check reads at most 4 KiB per file. Files stay in the registered folders after the handover review closes. The job retains the attributed preparation and copy results, including unconfirmed preparation. A new review starts a fresh Windows scope. |
| SQLite migration and recovery | Migrations use a strict ledger and guarded transactions. Job opening detects unexpected schema or corruption and preserves the source on failure. Initialization stages and the SQLite rollback journal are implementation files. A separate user-managed recovery-copy workflow and its retention timer remain pending. |
| Support and configuration archives | Exact ZIPs are limited to 256 KiB. They publish beneath an explicitly chosen local folder. Support publication stores an attributed receipt in the job. Recovery of an unconfirmed publication verifies the stored digest against the existing copy. Copies follow the destination's retention policy and remain there after ordinary application removal. |
| Exported or printed workshop and recipient copies | The owner of each destination or physical copy is responsible for access and retention. Workshop files remain restricted for the workshop's retention period. |

The privacy limits exclude retained raw provider or installer streams, secrets
and sibling-private content. Any later job-data deletion must be explicit and
operate only on verified ThirdLife-owned roots and exactly attributed artifacts.
Archive, application removal and job-data deletion are separate operations.
[Root inventory](data-locations.md)
