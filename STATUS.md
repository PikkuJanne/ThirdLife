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
its no-op and exited through the Windows elevation route. TL-0317 remains open for
live decline, elevated interruption recovery and the owner-deferred distinct-account
Hyper-V test. Broker-dependent actions remain unavailable in the ordinary application.
[Broker evidence](artifacts/evidence/TL-0311/report.md)

Clean Full passed on the reconciled runtime source `a5fcef3`. The next independent
package task is TL-0405, covering progress, cancellation, timeout and network loss.
