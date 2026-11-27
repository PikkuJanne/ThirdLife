# TL-0505 maintenance phase coordination

## Initial cross-phase checkpoint

On 2026-11-27 the cross-phase checkpoint was tested on the active Windows 11 Pro
26H2 x64 host, build 26300.9974, with .NET SDK 10.0.400 and Python 3.14.7.
The final technical commit is `9f2a27a590dc5073cd9e6459aff4ea3a34cf7a42`,
based on `967ae8dad190441f7877f72defa10950e69870a1`.

Prepare explains the order of updates, restarts, packages, checks and final
verification before approval. Prepare and Verify show the reason for a pause and
how to resume. Opening either view reads the saved reason. An operator can leave
the job at that boundary and explicitly rescan or reconcile it later.

Each package start, including a retry, reads the update journal in the SQLite
write transaction that claims dispatch. An unresolved handoff, an unfinished
servicing phase, a missing installed-revision check, or a pending update restart
holds the package start. After a restart requirement, the existing update
workflow requires both lower observed kernel uptime and a successful scan showing
the restart flag clear. A fresh scan that still offers only restricted revisions
leaves independent package work available when there is no servicing recovery
outstanding. Each route retains its own approval, policy, resource and
verification checks.

Before starting an update phase or handing it to the broker, the same job must
have settled its outstanding package attempts. A started, applied or
review-required package holds servicing even if the job acquired a newer intake.
The read-only scan stays available. A competing package or update can save its
result and reconcile independently of the hold.

## Verification

| Check | Result |
| --- | --- |
| Baseline `dotnet test tests/ThirdLife.Actions.Tests -c Release --filter FullyQualifiedName~RebootCheckpointTests -p:RestoreLockedMode=true` | 21 passed, 87 ms test duration, on the base commit. |
| Persistence component filter `FullyQualifiedName~MaintenancePauses\|FullyQualifiedName~UnresolvedPackageFromEarlierIntake` | Four cases passed in 3 s. An approved package survived 0, 1 and 2 modeled Windows restarts and database reopens. The store rejected a clear flag on unchanged uptime and retained the exact approval. An unresolved package under an earlier intake stopped an approved update before channel preparation. |
| UI component filter `FullyQualifiedName~RestartPausesPackagePhase` | One WPF/SQLite case passed in 3 s. An explicit scan persisted the restart requirement. The reopened job showed the same keyboard-focusable, polite-live reason in Prepare and Verify. The provider received one scan in total. |
| Targeted (includes Quick) | At `1c61d0466e7d2d711cd6575bf899511322691f6c`, `./eng/verify.ps1 -Tier Targeted -TestProject tests/ThirdLife.Persistence.Tests/ThirdLife.Persistence.Tests.csproj -Filter '(FullyQualifiedName~WindowsUpdateWorkflowTests\|FullyQualifiedName~RebootCheckpointStoreTests)&Category!=Extended&Category!=ActiveMachine'` passed in 241.401 s. Governance, formatting, the warnings-as-errors build, 141 Core, 956 Inventory, 183 UI and 55 Persistence cases passed. |
| Contradictory inventory regression | On the final update-history change, all 40 cases of `dotnet test tests/ThirdLife.Persistence.Tests/ThirdLife.Persistence.Tests.csproj -c Release --no-restore -p:TreatWarningsAsErrors=true --filter FullyQualifiedName~WindowsUpdateWorkflowTests` passed in 18 s test time. An installed revision reoffered by a later successful inventory held packages through interruption, reopen and a failed scan. The hold cleared when a newer successful inventory omitted it. |
| Extended evidence reused | TL-0309 authenticated checkpoint fault/replay and TL-0408 package interruption and verification cover the unchanged action-specific recovery paths. [TL-0309](../TL-0309/report.md) and [TL-0408](../TL-0408/report.md) |

The initial Targeted attempts identified C# whitespace and a cancellation-token
analyzer failure in the conflict reread. The reread now explicitly completes
its check after a dispatch conflict.

### Scope at the initial checkpoint

The storage tests use controlled kernel-uptime readings, update observations and
isolated jobs. The WPF test uses a controlled provider. The checkpoint observed
no actual machine restart, servicing operation or installation.

At that checkpoint TL-0505 remained in progress. The next implementation was the exact full-profile
phase sequence bound to the reviewed actions, including durable post-package
checks, final restart checkpoint, and handoff to independent final verification.
Exercise shutdown and resume at each of those additional boundaries. The native
Windows Update receipts, installed-revision observation and applicable restart
evidence retain their separate qualification requirements at TL-0511.

The combined technical candidate `91c0153bd03228a45fd15f3e585a4eb27c01d72c`
passed 59 Windows Update workflow, reboot store and interrupted journal migration
cases in 28 s with the Release build. The `dotnet test` filter selected
`WindowsUpdateWorkflowTests`, `RebootCheckpointStoreTests` and
`InterruptedJournalMigrationRecoversAndPreservesExistingJob`.

## Full maintenance schedule

Prepare offers the full schedule when reviewing the supported package profile.
Before approval it displays Windows Update review, required restarts, the exact
package actions, post-package checks, the final restart and independent
verification. The reviewed phase list, evidence revision and action graph are
bound to the plan digest. An update still requires its own exact revision,
policy, resources and authorization. Package-only plans retain their earlier
encoding and approval behavior.

Schema 12 saves the first boundary with the approval and appends subsequent
boundaries in SQLite. Closing the job leaves the phase in place. Reopening
reads the latest boundary. An explicit continuation uses a fresh update scan,
the current intake and assessment, and the exact package journal. Both sides of
the final restart also require a new installed-state observation for each
verified target. A mismatch or unavailable result survives reopening.
Prepare, Verify and the workshop report distinguish the saved maintenance
phase from whole-device acceptance.

Windows supplies a local boot sequence independently of elapsed uptime. A
later boot can therefore be recognized even when the operator returns late.
The earlier encoding remains readable, including authenticated package
checkpoints with only an uptime observation. The coordinator allows three
pre-final restarts and a fourth for the final restart. A further eligible update
goes back to update review and the existing three-pass servicing limit.
Conflicting, stale or unavailable evidence holds continuation. An assessment
change after package verification preserves the earlier result for a new
readiness review.

The full-schedule tests allow zero, one or two servicing restarts before
packages. With no subsequent boot, the job stays at its final restart
checkpoint. The package-only path can finish its action without a restart
when Windows and the installer both report that none is required.

### Ordinary Windows restarts on the existing guest

On 27 November 2026, `GuestMaintenanceResumesTheSameApprovedJobAcrossTwoWindowsRestarts`
ran in three separate processes on the existing Windows 11 Pro 26H2 x64
guest, build 26300.9974. Each
process opened the same isolated SQLite store. The test used the production
Windows boot reader, a fixture operator and controlled update and package
observations. The host separately checked the VM identity and quiescence
before each orderly restart under the standing grant. Windows recorded one
planned restart event for each request and the kernel boot time changed twice.
[Standing guest restart authority](../../e2e/M4-first-write-slice.md#standing-guest-restart-authorization)

| Stage | Observed result |
| --- | --- |
| Before the first restart | The saved schedule rejected a clear restart flag on the same boot. One case passed in 1.681 s. |
| After the first restart | Windows reconnected with a later boot in 45.0 s. The same job, plan, impact and approval reopened. One modeled package attempt reached verified, the post-package check saved the final restart checkpoint, and another same-boot continuation was refused. One case passed in 1.676 s. |
| After the second restart | Windows reconnected with a later boot in 51.6 s. Fresh controlled checks handed the saved job to independent verification. One case passed in 1.001 s. The host confirmed the original binding and removed the guest test store, binaries and staging. |

The named case ran with the pinned .NET SDK 10.0.400 using `dotnet vstest
ThirdLife.Persistence.Tests.dll` and its exact method filter. It uses the same
opaque `THIRDLIFE_MAINTENANCE_RUN` (32 hexadecimal digits) and one
`THIRDLIFE_MAINTENANCE_STAGE` per invocation, in the order `prepare`,
`packages`, `verify`. The test itself only writes fixture records. Restarts
are performed separately by the authorized guest operator or harness.
The host retained the run-specific binding privately.

The SHA-256 digests of the tested Release assemblies were
`25828a0b88e4aa5283314072136e9a42fbc4999c542bb2c46bbbd1c4a2fcaeda`
for Actions, `ee2b9dc46db168e7766104fc8418acafb4a0f8f849a29d775734b62ae0dd3d4a`
for Persistence and `a8bd236aad9b3a3b26a71cd40f860fcfd612f8182ba0972729db8a9647a178fb`
for the test assembly. The guest payload SHA-256 was
`0f908bb32e948ccc47f79b013971cd6a5656064c11e66ec43d3d3b09d7a00c0f`.
The guest exercised ordinary warm restarts. Native servicing receipts and
the serviced revision remain TL-0511 qualifications. TL-0509 owns cold-boot
coverage and TL-0507 owns the independent profile verification pass.

### Active-machine regression results

The technical checkpoint is `0761995441c2e68492d7534d48e26c2ca83d947b`
on base `7d2282f11023aa9e6fb42f9ee7ff80eba8913bd2`. Tests ran on the
Windows 11 Pro 26H2 x64 host, build 26300.9974, with .NET SDK 10.0.400 and
Python 3.14.7. The guest Actions and Persistence assemblies matched the local
Release binaries used for the Targeted run. Subsequent test changes added
boundary shutdown and concurrent reassessment cases.

| Check | Result on 27 November 2026 |
| --- | --- |
| Baseline on the base commit | Three existing `MaintenancePausesApprovedPackageAtEachUpdateRestartAcrossReopen` cases passed in 1 s. |
| Targeted including Quick | `eng/verify.ps1 -Tier Targeted -TestProject tests/ThirdLife.Persistence.Tests/ThirdLife.Persistence.Tests.csproj -Filter '(FullyQualifiedName~RebootCheckpointStoreTests\|FullyQualifiedName~WindowsUpdateWorkflowTests\|FullyQualifiedName~InterruptedJournalMigration)&Category!=Extended&Category!=ActiveMachine'` passed in 205.032 s. The eight governance regressions, validators, formatting, warnings-as-errors build and 1,380 .NET cases passed (142 Core, 977 Inventory, 192 UI and 69 Persistence). |
| Boundary interruptions and safety checks | The final focused Persistence selection `FullyQualifiedName~FullMaintenance\|FullyQualifiedName~StampedPackageReboot\|FullyQualifiedName~PackageRebootRequiresAuthenticatedProofOfANewerBoot\|FullyQualifiedName~ThreeExactPassesRequireFreshReview` passed 12 cases in 12 s. Each boundary was reopened after a modeled shutdown. Before/after-commit faults, missing or reversed boot sequences, expired or stale scans, package mismatches, concurrent reassessment, repeat servicing and schema 11 migration were exercised. |
| WPF interruption, Extended | `dotnet test tests/ThirdLife.UI.Tests -c Release --no-restore --no-build --filter FullyQualifiedName~KilledNoOpApplicationReopensTheSameJobAndReconcilesWithoutDispatch` passed both cases in 10 s. An interrupted synthetic application resumed its existing job without replay in both scheduling modes. |
| Native shell and UI Automation | From the clean technical checkout, `eng/run-ui-smoke.ps1` opened the Release WPF shell with a standard user token, navigated to Inspect and observed its status in 2.412 s. The process exited normally. The maintenance-specific WPF tests separately exercised the keyboard continuation control, focusable status, polite announcements and generated workshop report. |

The first clean Full run took 471.544 s. Its 310 governance tests passed.
Seven Actions cases failed during fixture setup because the fixture
reconstructed the plan subject using an obsolete constructor. All other
assemblies passed. Commit `f8a5da7bb37bbe61631ba4e18dd4ed0488eb8c2a`
updated the comparison fixture to reuse the reviewed subject, schedule and
maintenance flag. All 24 `PlanDiffTests` passed in 374 ms, followed by all
254 deterministic Actions tests in 406 ms. The product and guest test sources
are identical at the two commits.

A fresh detached checkout of `f8a5da7bb37bbe61631ba4e18dd4ed0488eb8c2a`
passed `eng/verify.ps1 -Tier Full -Restore` in 412.863 s. All 310 governance
tests and 2,471 deterministic .NET tests across 13 assemblies passed with
zero skips. Locked restore, formatting and the warnings-as-errors Release
build passed. The checkout remained clean.

The final standalone Quick run passed all 1,311 .NET cases in 138.755 s,
18.755 s above the 120 s target. An earlier Quick run alongside Full took
188.368 s with the same selection. On the standalone run the Release build
fell from 17.20 s to 8.37 s and the 192-case WPF suite from 112 s to 73 s.
The WPF suite accounted for more than half of the final run.
