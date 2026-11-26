# ThirdLife Setup Core - Current status

**Snapshot date** 2026-11-26  
**Bundle baseline** 0.8.0  
**Runtime authority** Active Codex machine only. Remote CI is supplementary.

TRACE is the default unelevated native interface on `main`. Jobs and the six stages
expose saved intake, findings and manual checks, candidate policy, exact
package review and approval, independent verification and recovery, and the
restricted workshop report. The verified application source and test limits
are recorded with TL-0413. [TRACE verification](artifacts/evidence/TL-0413/report.md)

M4 is complete for the reviewed machine-wide vendor MSI. TL-0412 recorded an
actual installation through the application, independent Windows Installer
verification and matching saved job, journal and report. Each new transaction
still requires exact action approval and applicable Windows consent.
[M4 gate](artifacts/gates/M4-package-vertical-slice.md)
[Live package evidence](artifacts/evidence/TL-0412/report.md)

TL-0317 qualified the no-op broker in the same-machine Windows 11 guest.
TL-0313 completed application orchestration at implementation scope.
Ordinary broker-dependent dispatch still requires TL-0318 live application
qualification and the TL-0314 privilege decision.
[Guest qualification](artifacts/evidence/TL-0317/report.md)
[Application orchestration](artifacts/evidence/TL-0313/report.md)

TL-0501 and TL-0502 cover approval-bound scheduling and attributable action
skips. Verify and the workshop report show the saved preparation disposition
after reopen. Essential omissions require review. TL-0503 is next for disk,
power and resource preflight. Live multi-action qualification and exact
approval remain prerequisites at the M5 technical gate, TL-0511.
[Scheduling evidence](artifacts/evidence/TL-0501/report.md)
[Action outcome evidence](artifacts/evidence/TL-0502/report.md)
