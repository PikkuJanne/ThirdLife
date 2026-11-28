# TL-0703 archived original File History samples

The **Check saved copies and original samples** action in **Jobs**
reads the selected archived job and its latest checkpoint in a single
protected SQLite snapshot. The snapshot includes the job's local report
and guide receipts and each distinct original File History sample's
saved preparation record. Preparation records cover the interval
where an original may have been published before its outcome was saved.

The original-sample verifier derives the internal filename from the
recorded sample ID. It opens only that named file in the registered
original folder and checks its protected file handle, length and
SHA-256. A missing or unsafe original remains unverified. Each original
read is bounded to 4 KiB and the job history to 128 audit records.
Jobs announces separate counts for the report and guide copies and
the original samples. The operator's recovered copies, external exports
and unrecorded files retain their separate ownership and review.

On 28 November 2026, the active Windows 11 Pro native x64 host
ran these focused Release selections with locked dependencies.

| Selection | Result |
| --- | --- |
| `FileHistoryRestoreProbeTests` | 5 passed in 478 ms. Exact bytes matched, and missing roots, exclusive locks and broadened permissions were refused. |
| `HandoverReviewStoreTests` | 6 passed in 2 s. Job selection, active jobs and stale checkpoints were checked. Preparation and outcome records produced one attributed original per sample. |
| `AuditJobArchiveTests` and `AuditReportTests` | 9 passed in 10 s. The real Jobs action had keyboard focus and a polite UI Automation status. A locked recovered copy left the original check usable. A same-length original change became unverified. The recovered and unrecorded fixture bytes were preserved. |

A concurrent baseline test host held the default WPF build output.
The focused rerun built and ran in an isolated artifacts directory.
The earlier integrated baseline separately passed 1,449 Quick .NET
tests and 78 selected Persistence, WPF and Reports tests in 279.809 s
at `98269a4c59e29cb9447d6eb458b6048e85b37cfc`.

Governed Targeted verification passed in 217.742 seconds on the
changed code. It ran all 1,450 Quick .NET tests, eight work-selection
checks, and 61 Persistence, nine WPF and 15 Reports tests with zero
skips. Release compilation returned zero warnings.

Clean detached Full verification with locked restore passed in
454.336 seconds at `2555c566c492aacb04f0882d37c32f57f7fd24ee`,
based on `98269a4c59e29cb9447d6eb458b6048e85b37cfc`.
It passed 322 repository checks and 2,693 .NET tests with zero
skips or build warnings. The default deterministic filter applies.

Quick on the final branch with the evidence updates passed 1,450
tests in 165.735 seconds against the 120-second target. The WPF
selection took 96 seconds and the Release build 11.69 seconds. The
earlier integrated Quick audit also exceeded the target at 178.889
seconds, including 104 seconds in WPF. The test selections remained
in place. [Earlier Quick audit](../TL-0709/private-candidate-boundary.md)

The verified action reads synthetic original samples. Separate job-data
deletion, native File History restoration and the stable installer
lifecycle still require their respective implementation or qualification.

## Separate deletion prerequisite

A 0.017-second native Windows probe on 28 November opened a disposable
synthetic file with `GENERIC_READ | DELETE`, `FILE_SHARE_READ` and
`FILE_FLAG_OPEN_REPARSE_POINT`. A conflicting writer caused a sharing
violation at acquisition. After acquisition, new write and delete
opens returned sharing violations. The fixture checked the expected
SHA-256 on the held handle, marked that handle for deletion and
confirmed removal after closing it. An adjacent synthetic decoy
retained its exact bytes.

The probe isolates Windows handle sharing and disposition. The
production workflow still needs a durable approval, a protected
root and file identity check, and split-state recovery between
SQLite records and files. The next implementation adds ownership
records before publishing local workshop reports and guides.
