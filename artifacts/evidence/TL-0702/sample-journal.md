# TL-0702 harmless sample continuity

TRACE saves an attributed, exact sample intent before writing the local
file. Migration 021 records the generated sample identifier, digest,
length, review sequence, operator code, timestamp and limited outcome.
The store retains earlier results and declines stale or foreign scope.
An interrupted intent reopens as unconfirmed. A verified separate-file
match still leaves backup pending. Windows account information, backup
volume identity, recipient paths and recovery material stay outside the
sample journal.

The controlled migration fixture opens a job created with schema 020,
retains its assisted handover, stages a sample with schema 021 and
reopens the protected intent. It rejects an approval from a different
review and a further copy outcome after switching to sealed handover.
The WPF fixture exercises the current review, protected sample creation,
unavailable copy, matching copy and reopen. Reopening displays the saved
history while current sample check controls stay unavailable until a
new review. No actual Windows backup or restore ran.
[Sample staging](handover-sample.md)

On 27 November 2026 the clean detached active Windows 11 x64 checkout
at `93557a2d4b11ad8557e33e65621db0df2c227f48` passed
`eng/verify.ps1 -Tier Full -Restore` in 553.982 s. It ran 319
repository checks and 2,685 .NET tests with no skips. The focused
Targeted selection passed 1,446 Quick and 41 persistence tests
including the schema-020 migration, interrupted intent and stale-write
cases. Its first native WPF run exposed a test-driver issue when opening
the job outside the UI event dispatcher. The corrected active-machine
test uses Close and Open, waits for the job's navigation, and returns
to Handover. The corrected selection passed all four WPF and native read-only tests in four seconds and verified the visible restored history
and absence of restored authorization. The production code is identical
to the clean Full source. The recipient's actual Windows backup setup and
restore attribution remain separate qualifications.
