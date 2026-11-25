# ThirdLife Setup Core — Current status

**Snapshot date** 2026-11-25  
**Bundle baseline** 0.8.0  
**Runtime authority** Active Codex machine only. Remote CI is supplementary.  
**Verified outcome** One machine-wide package transaction

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

TL-0317 now targets the running Hyper-V guest reported by the owner. The current Codex
token was denied at host `Get-VM`. Resume with an authorized host shell and secure local
PowerShell Direct authentication. Guest provisioning, distinct-account denial, actual
decline and interruption recovery remain unobserved. Broker-dependent actions remain
unavailable in the ordinary application. [Guest runner and evidence](artifacts/evidence/TL-0317/report.md)

Clean Full passed on the reconciled runtime source `a5fcef3`. The next independent
package task is TL-0405, covering progress, cancellation, timeout and network loss.
