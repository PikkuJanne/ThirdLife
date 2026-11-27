# TL-0703 guarded rollback journal after commit

The production job-store connection keeps SQLite's PERSIST rollback mode
and applies a zero byte limit to the journal after a commit. It continues
to use the registered file and Windows identity guards. Existing migration,
integrity, ACL, size and foreign-key checks are in the same path. A
preflight which refuses the database or an orphan journal retains its
existing fail-closed behavior.

A production-store synthetic integration test observed 41,552 retained
bytes after a completed write on the previous source. The corrected
Release source `6af7c2709eabc21a85222ed99907c30898291694` observed zero
bytes after writes through the same public store operations. The Windows
file identity remained the same before and after the writes and after
reopening the store. The job's evidence and later archive transition
reopened correctly. Seventeen focused tests passed on the active Windows
host, including forced process termination, rollback, commit, first-run
publication and preservation of an unpaired journal.

An additional native fixture used the host `winsqlite3.dll` engine
3.53.4 with synthetic markers. Both configurations rolled back an
uncommitted deletion and preserved another row. A committed delete with
the default PERSIST limit left 12,824 bytes including the earlier page
in the journal. The zero-limit variant retained the journal identity with
a length of zero. The product change applies to guarded job-store commits.
Job removal, idle-file cleanup, forensic erasure and recovery-copy
publication are outside this measured scope.

Governed Targeted passed at the same source on 27 November in 244.444 s.
It ran 1,449 Quick .NET tests, eight work-selection checks and 94
Persistence cases. The focused selection covered the normal job-store
entry points, Windows path validation, interruption, migration and
corruption protections. Clean detached Full verification passed in
500.907 s against the same source. It rebuilt all projects with zero
warnings, ran 322 repository tests and 2,691 .NET tests with zero
skips, and included all 236 persistence tests under the default
deterministic filter.

The next migration-recovery probe used the same Windows SQLite engine
and a synthetic 96-row source. The copy completed in 28 calls of up to
four pages while a competing writer received a busy result, rolled
back and retried after the read snapshot. A prematurely finished
backup left an empty destination schema. The completed staged copy
reopened with the original 96 rows, the expected version and clean
integrity and foreign-key checks. An earlier fixture with different
connection settings and a retained refused write produced a disk I/O
error. The rerun aligned connection settings and rolled back the
refused write. The remaining production work is protected staging,
an attributable named operation, recovery and expiration handling.
[SQLite backup API](https://www.sqlite.org/c3ref/backup_finish.html)

