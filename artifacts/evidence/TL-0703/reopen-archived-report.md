# TL-0703 reopen the recorded workshop report

TRACE Handover can open the exact protected workshop report from a
reopened job that has a saved publication receipt. The store reads the
latest workshop report receipt within that job's bounded publication
history. The report reader checks its kind, job identity, generated
file name, length and SHA-256 through the registered protected folder.
Opening the copy creates no report and leaves the job and its handover
decisions intact. The UI identifies the copy as evidence from the time
it was generated.

Release WPF verification on the active host used a protected SQLite
job, an isolated report folder and a provider fixture that observes
nothing. The test generated and recorded a report, archived the job,
closed the first workspace, and reopened the same job in a second
workspace. The Handover button opened the recorded file and matched
its original bytes. When the fixture changed one byte without changing
length, the same button reported that the file had changed and
kept the preview empty. Closing the job cleared both its file name
and open capability. Ten focused report, archive and handover checks
passed in 10 s.

The product change is 0201da70c450be2b8dfbfabf727251a5fe66c2e4. It reuses the existing publication
history with no migration. A job with an earlier report that has no
recorded publication receipt still needs output generation through
TRACE. Scoped job-data deletion remains separate.
