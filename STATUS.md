# ThirdLife Setup Core - Current status

**Snapshot date** 2026-11-27  
**Bundle baseline** 0.8.0  
**Runtime authority** Active Codex machine only. Remote CI is supplementary.

**Permanent guest capacity** The existing `ThirdLife-W11` only, throughout the
project, pilot, release and maintenance. Reuse authorized guest states sequentially.
No additional guest, Windows Sandbox or alternate Windows-baseline request is needed.

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
skips. TL-0503 checks current platform, storage and power before package
download and dispatch. A late resource deferral survives reopening in Verify
and the workshop report.
Live multi-action qualification and exact approval remain prerequisites at the
M5 technical gate, TL-0511.
[Scheduling evidence](artifacts/evidence/TL-0501/report.md)
[Action outcome evidence](artifacts/evidence/TL-0502/report.md)
[Preflight evidence](artifacts/evidence/TL-0503/report.md)

TL-0504 engineering is complete. It uses a typed broker request, independent phase authorization, an exact
servicing policy, measured storage checks, durable receipts and installed-state
verification. The existing guest offered one revision with an unsupported handler.
Prepare saved and reopened the reason with zero approvals and zero dispatches.
No live download or installation occurred. TL-0511 requires an eligible revision
on ThirdLife-W11, reviewed recovery and space, exact authorization, native receipts,
an independent installed-state check and a rescan after any required restart.
[Update verification](artifacts/evidence/TL-0504/single-guest-inventory.md)

TL-0505 binds the full maintenance schedule to the reviewed plan. Prepare, Verify
and the workshop report show where the job stopped. On `ThirdLife-W11`, the same
job and approval survived two orderly restarts and reached the independent
verification handoff with controlled package and update results.
[Maintenance evidence](artifacts/evidence/TL-0505/report.md)

TL-0507 is complete at engineering scope. Verify runs an independent
post-maintenance pass and reopens the result with exact installed versions,
profile capabilities and unresolved machine requirements. The controlled
application test detected a version downgrade under the same approval.
The native host scan returned four offers outside the servicing scope.
Whole-device acceptance remains open.
[Profile verification evidence](artifacts/evidence/TL-0507/report.md)

TL-0509 saves an authenticated cold-boot checkpoint and requires fresh profile
checks and the approved launch probe on the next confirmed full startup.
`ThirdLife-W11` powered off and started again. Verify reopened the same job and
approval, saved the post-boot result with controlled adapters and reopened it
in the workshop report. Physical host power-cycle evidence remains unobserved.
[Cold-boot evidence](artifacts/evidence/TL-0509/report.md)

TL-0510 passed 40 host matrix cases and three controlled candidate cases on
`ThirdLife-W11`. The guest reopened the exact job, approval and report. Live
reference verification is held pending trusted system-disk health and
aggregate restart clearance plus a qualified exact live profile. TL-0511
retains live update and multi-action qualification.
[M5 matrix](artifacts/e2e/M5-full-profile.md)

TL-0601 adds the workshop checklist to TRACE Handover. It saves attributable
confirmations, retains earlier results, and requires a recheck after a relevant
job or boot change. Bounded artifact detection and assisted and sealed handover
are the next implementation tracks.
[Finalization evidence](artifacts/evidence/TL-0601/report.md)

TL-0604 generates the restricted technical workshop record from saved
provenance, repairs, approval, preparation, verification and current or
earlier finalization. The record keeps whole-device handover open.
[Workshop record evidence](artifacts/evidence/TL-0604/report.md)

Pilot adoption and exposure remain at TL-0611. Standalone release and Team B
exit remain at TL-0710. [Decision and graph review](docs/reviews/2026-11-27-single-guest-continuation.md)
