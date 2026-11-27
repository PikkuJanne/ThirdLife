# Retention and cleanup

Production retention operates on the named records below. The policy's remaining
aged retention and deletion defaults are implementation requirements for later
lifecycle work. [Approved privacy guidance](privacy/privacy-model.md)

| Record | Current behavior |
| --- | --- |
| Job, approvals, journal, policy snapshots, verification and finalization | Archive is reversible and makes the job read-only while archived. History stays associated with the job. There is no automatic 180-day job expiry or explicit job-data deletion command in the current application. |
| Workshop report copies | The store enforces up to 64 files, 64 MiB total and 8 MiB per artifact. Destinations that already exist are preserved. Staged writes only clean their owned temporary file. A full folder needs review of retained copies. |
| Sanitized logs | The store enforces age and the consuming subsystem's byte ceiling during writes and supported cleanup. It handles owned temporary and transaction files with schema, root and ownership checks. The package sink uses 14 days and 1 MiB. |
| Battery and package staging | Operation cleanup is limited to verified source-owned files. The battery workspace reconciles its prior ownership marker. The package runtime removes the verified installer and attempts nonrecursive removal of its own empty directory. Failures remain reportable. There is no shared sweep of unrelated folders. |
| SQLite migration and recovery | Migrations use a strict ledger and guarded transactions. Job opening detects unexpected schema or corruption and preserves the source on failure. Initialization stages and the SQLite rollback journal are implementation files. A separate user-managed recovery-copy workflow and its retention timer remain pending. |
| Exported files | Copies in external destinations follow that destination's retention policy. Workshop files remain restricted and preserved for the workshop's retention period. Support exports require the separately reviewed support projection and explicit recipient handling. |

The privacy limits exclude retained raw provider or installer streams, secrets
and sibling-private content. Any later job-data deletion must be explicit and
operate only on verified ThirdLife-owned roots and exactly attributed artifacts.
Archive, application removal and job-data deletion are separate operations.
[Root inventory](data-locations.md)
