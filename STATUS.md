# ThirdLife Setup Core — Current status

**Snapshot date** 2026-11-25  
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
and cleanup. Ordinary broker-dependent dispatch remains gated by application
orchestration in TL-0313 and the applicable TL-0314 privilege decision.
[Guest runner and evidence](artifacts/evidence/TL-0317/report.md)

TL-0312 completed the focused broker security checks for the reviewed no-op scope.
Forbidden request fields and failed peer reauthentication were rejected before
dispatch. The unelevated launch and bounded parser checks passed, and clean Full
passed on `e734b5f`. Windows consent, distinct-account denial and teardown retain
their TL-0317 provenance. [Broker security evidence](artifacts/evidence/TL-0312/report.md)

The next broker task is TL-0313, connecting approval, journal, consent, broker result
and recovery. The next independent package task is TL-0405, covering progress,
cancellation, timeout and network loss.
