# TL-0509 final cold-boot verification

## Application workflow

Verify saves a checkpoint after the final maintenance restart and its boundary
checks. SQLite migration 14 adds an append-only request bound to the job,
maintenance revision, plan and impact digests, approval, package journal and
servicing history. Windows user-scoped protection authenticates the request against
the guarded database file. The operator reopens the existing job after shutdown and
power-on. A request can qualify on the immediately following Windows session within
24 hours. Saving a checkpoint requires no approval change, package operation,
elevation or application-initiated shutdown.

The read-only Windows adapter checks the local System log for a correlated clean
shutdown action, stop, start, previous boot and full-start evidence. It reads a fixed
set of numeric values and timestamps with a 32-event limit and a one-second enumeration
budget. A process reopen, Windows restart, hybrid startup, missing or contradictory
evidence, and an incompatible boot sequence leave post-boot verification unavailable.
Windows exposes Fast Startup and full shutdown as distinct transitions.
[Windows power states](https://learn.microsoft.com/en-us/windows/win32/power/system-power-states) ·
[Windows shutdown command](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/shutdown)

The post-boot command checks the current request before collecting a fresh Windows
Update scan, independent inventory and policy findings, exact package registration
and the approved bounded launch probe. A verified registration and a failed launch
remain separate recorded outcomes. The store rechecks the current job, journal,
approval, boot and time window when it saves the result. The existing package launch
route is limited to the reviewed package and the already-approved artifact identity.
Verify announces progress and errors through keyboard-focusable status controls.
Reopening the job and generating its restricted workshop report shows the associated
sessions, verification times, checks, versions, launch results and blockers.

The guarded store keeps up to 64 cold-boot requests per job and up to the existing
64 profile verification records. The request stays below 8 KiB and the existing
verification payload limit stays at 64 KiB. Local session counters, UTC times and a
bounded observation type are the only new projected Windows data. Event messages,
computer and account identifiers, paths and authentication data stay out of reports.
Existing profile results retain their original evidence and remain readable as
pre-cold-boot results.

## Verification scope

Checks use the active Windows 11 Pro 26H2 native x64 host, build 26300.9974, .NET
SDK 10.0.400 and Python 3.14.7. Base `891936e20eebacf5ca59248299b5420e018c8c7e`
passed the two existing full-maintenance WPF continuation cases in six seconds of
test time. The test command was
`dotnet test tests/ThirdLife.UI.Tests -c Release --filter FullyQualifiedName~FullMaintenanceReviewResumesThroughFinalRestart`.

| Check | Observed result |
| --- | --- |
| Inventory deterministic tests | 1,010 passed in five seconds of test time. The native adapter also classified the host's existing Windows restart as a restart at boot 33 and returned no evidence for a request in the still-running session. The read-only observation took 23.304 ms. |
| UI and report | Both focused WPF cases and both focused reports passed. The WPF cases exercised UI Automation, polite status, reopen within the same boot, a modeled warm Windows restart, and a modeled orderly next session with a saved successful or failed launch probe. A new scan and probe ran once per post-boot pass. |
| Cold-boot store | Four focused persistence cases passed in three seconds of test time. They cover missing, ambiguous, hybrid, restart, stale and mismatched evidence, changed job and review, copied-store authentication, a failed launch and migration of a saved preboot result from schema 13 to 14. |
| Existing guest cold start | All five Extended stages passed on `ThirdLife-W11`. The guest reopened the same checkpoint in boot 8 and held it pending. Hyper-V observed the guest power off and start in 53.466 s. Boot 9 admitted a fresh scan and launch probe, retained the approved package version, and reopened the same job, approval and workshop record. Package and inventory results came from controlled inputs. [Guest procedure and evidence](guest.md) |

The first combined Release build found a name collision between an Extended test
helper and a WPF type. Renaming the helper resolved the test build. The first Quick
format check found import ordering and line-wrapping differences in the new code.
The corrected gate is recorded with the final source.

The product's applicable physical-device checks and finalization remain required
for handover under D-033. The physical host power cycle remains unobserved and
the stable release matrix retains its active-machine cold-boot and continuity
requirement under TL-0707. Pilot and stable exposure follow their own gates.
