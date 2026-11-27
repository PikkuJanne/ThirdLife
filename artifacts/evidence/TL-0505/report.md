# TL-0505 maintenance phase coordination

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
| Extended evidence reused | TL-0309 authenticated checkpoint fault/replay and TL-0408 package interruption and verification cover the unchanged action-specific recovery paths. [TL-0309](../TL-0309/report.md) Ãƒâ€šÃ‚Â· [TL-0408](../TL-0408/report.md) |

The initial Targeted attempts identified C# whitespace and a cancellation-token
analyzer failure in the conflict reread. The reread now explicitly completes
its check after a dispatch conflict.

## Scope remaining

The storage tests use controlled kernel-uptime readings, update observations and
isolated jobs. The WPF test uses a controlled provider. The checkpoint observed
no actual machine restart, servicing operation or installation.

TL-0505 remains in progress. The next implementation is the exact full-profile
phase sequence bound to the reviewed actions, including durable post-package
checks, final restart checkpoint, and handoff to independent final verification.
Exercise shutdown and resume at each of those additional boundaries. The native
Windows Update receipts, installed-revision observation and applicable restart
evidence retain their separate qualification requirements at TL-0511.
