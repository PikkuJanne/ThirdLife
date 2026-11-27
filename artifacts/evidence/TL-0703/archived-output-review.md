# TL-0703 archived report and guide copy review

In **Jobs**, **Check saved report and guide copies** reads receipts in the
same protected SQLite snapshot as the archived job and its latest checkpoint.
Jobs counts distinct recorded copies whose exact generated name, length and
SHA-256 match the registered local stores. Changed, missing, contradictory
or unavailable copies are reported as unverified. Verification uses read-only
file access with a total read ceiling of 128 MiB and an 8 MiB ceiling per file.
It does not create a missing artifact root. Earlier copies without associated
receipts may remain. Published exports and backup samples have separate scopes.

| Verification | Observed result |
| --- | --- |
| Governed Targeted on 27 November 2026 | 1,449 Quick .NET, eight work selection, eight focused WPF and five focused SQLite checks passed on the active Windows host in 191.029 s. Quick executes before the focused checks. |
| Source and focused report store checks | Release compilation against `585879a817c58eec90c862ccd52e7b678ac80259` passed with zero warnings. Sixteen report/guide tests passed, including protected store refusal cases. |
| Real WPF entry point | A new window opened the same archived synthetic job. Jobs identified one valid report and zero unverified copies. A one-byte same-length change moved that copy into the unverified count. The status has a polite UI Automation announcement. Closing the job disabled the action. |
| SQLite scope | The active job and a different job's checkpoint were refused. The selected archive returned its own report and guide receipts. A restore and rearchive at the same timestamp invalidated its previous checkpoint. |

The first build caught indexable collection analyzer violations. Direct
bounded checkpoint access cleared the warning-as-error build. The reviewed
copy check makes no data deletion, external export or recovery claim.

