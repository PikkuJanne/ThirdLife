# ThirdLife Setup Core — Current status

**Snapshot date** 2026-11-25  
**Bundle baseline** 0.8.0  
**Runtime authority** Active Codex machine only. Remote CI is supplementary.  
**Active outcome** One verified machine-wide package transaction

The [development contract reset](docs/amendments/2026-11-25-development-contract-reset.md)
establishes the product contract and separates implementation dependencies, execution
prerequisites and exposure decisions. `TASKS.yaml` is the current work record.

The unelevated application has a reviewed Notepad++ x64 machine-wide path through
Windows Installer. Read-only adapter checks confirmed the initial absence and verified the
signed vendor installer. Deterministic application tests exercise approval expiry, dispatch,
independent verification, SQLite reopen and reporting. The real launched transaction
remains pending under TL-0412.
[Package evidence](artifacts/evidence/TL-0412/report.md)

Clean Full passed on candidate `100aeb8`. The same build passed unelevated application
startup and read-only vendor package resolution. TL-0401 through TL-0404 are complete
at their engineering scope.

PR #36 retains the broker implementation and is being reconciled with the integrated reset
under TL-0316. The installed Microsoft x64 tools and pinned .NET inputs produced the
Native AOT broker. TL-0317 tracks protected deployment and live privilege qualification.
The distinct-account guest remains deferred. [Broker evidence](artifacts/evidence/TL-0311/report.md)

After action-specific authorization, run the ordinary launched app on the admitted active
Windows 11 host, verify exact machine registration, then reopen the job and verify its
report. The task remains incomplete until that evidence is recorded.
