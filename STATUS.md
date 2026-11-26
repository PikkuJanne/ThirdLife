# ThirdLife Setup Core — Current status

**Snapshot date** 2026-11-26  
**Bundle baseline** 0.8.0  
**Runtime authority** Active Codex machine only. Remote CI is supplementary.  
**Verified outcomes** One machine-wide package transaction and the reviewed no-op broker

The [development contract reset](docs/amendments/2026-11-25-development-contract-reset.md)
establishes the product contract and separates implementation dependencies, execution
prerequisites and exposure decisions. `TASKS.yaml` is the current work record.

TL-0412 completed. The ordinary unelevated application installed the reviewed Notepad++
x64 vendor MSI machine-wide. Independent Windows Installer registration verified the
exact product, version and scope. The reopened job, durable journal and saved workshop
report agree. [Package evidence](artifacts/evidence/TL-0412/report.md)

TL-0316 reconciled the broker checkpoint from PR #36 with the reset. The installed
Microsoft x64 tools and pinned .NET inputs built the Native AOT broker. Protected
provisioning verified, and one broker rehearsal authenticated the client, completed
its no-op and exited through the Windows elevation route. [Broker evidence](artifacts/evidence/TL-0311/report.md)

TL-0317 completed the reviewed no-op broker qualification and cleared the TL-0311
implementation prerequisite. All eight named cases passed in the Windows 11
Hyper-V guest using PowerShell Direct and the local DPAPI credential. Evidence
includes actual Windows consent and decline, distinct-account denial, recovery,
and cleanup. Ordinary broker-dependent dispatch requires live application
qualification under TL-0318 and the TL-0314 privilege decision.
[Guest runner and evidence](artifacts/evidence/TL-0317/report.md)

TL-0312 completed the focused broker security checks for the reviewed no-op scope.
Forbidden request fields and failed peer reauthentication were rejected before
dispatch. The unelevated launch and bounded parser checks passed, and clean Full
passed on `e734b5f`. Windows consent, distinct-account denial and teardown retain
their TL-0317 provenance. [Broker security evidence](artifacts/evidence/TL-0312/report.md)

TL-0313 is complete at implementation scope on `main`. The guest preview reopened the
exact no-op plan, and the WPF process-kill scenario recovered the durable job.
TL-0318 holds the live guest cases for an attended window with exact WPF
approval and Windows consent for each plan. M3 technical preparation can proceed.
[Orchestration evidence](artifacts/evidence/TL-0313/report.md)

TL-0405 verified journal-correlated package progress and the saved-job recovery
paths. Download cancellation or connection loss permits a fresh reviewed
approval. Installer timeout, reported cancellation and process termination
leave uncertain attempts in review with a read-only registration check. A
streamed connection fault also verified partial installer cleanup.
[Package interruption evidence](artifacts/evidence/TL-0405/report.md)

TL-0406 independently confirmed exact machine-wide Notepad++ registration and
launched the reviewed installed executable in isolation on the active machine.
The probe cleaned up its process and temporary files. WPF fixtures retained
distinct applied and verified results through job reopen and report generation.
[Package verification evidence](artifacts/evidence/TL-0406/report.md)

TL-0407 records typed package failure codes in the app, durable journal and
workshop report. Protected logs and support omit raw backend details.
[Package diagnostics evidence](artifacts/evidence/TL-0407/report.md)

TL-0408 completed package recovery implementation. Reopening the saved job
continues independent verification after an applied result and rechecks
registration for ambiguous attempts. The protected restart checkpoint permits
verification after confirmed boot evidence. TL-0409 can implement bounded
idempotency and retry. TL-0410 retains native installer interruption and actual
Windows restart qualification.
[Package recovery evidence](artifacts/evidence/TL-0408/report.md)
