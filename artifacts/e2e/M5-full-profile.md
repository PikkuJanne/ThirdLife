# M5 full-profile and failure-injection matrix

## Invocation

On the active Windows 11 x64 machine, prepare the pinned Python environment
in a clean checkout as described in [Testing](../../TESTING.md), then run
the published Full tier before the named scenario. Run the fixture matrix
from an unelevated PowerShell 7.2 or later session. Its JSON lists exact test
filters, Release assembly hashes, elapsed time, counts and cleanup. Use the
same case name to repeat a failure before expanding the test set.

```powershell
./eng/verify.ps1 -Tier Full -Restore
./eng/run-m5-matrix.ps1 -Scenario NetworkInterruption
./eng/run-m5-matrix.ps1 -Scenario SoftwareSingleWorker
./eng/run-ui-smoke.ps1
```

The guest companion takes the locally built Release output from the clean
checkpoint and verifies both transferred archives and the test assembly.
Use the existing PowerShell Direct credential and SHA-512-pinned private SDK
already used by the ThirdLife-W11 qualification runner.

```powershell
./eng/run-m5-guest-candidate.ps1 -Scenario Candidate,OptionalFailure,EssentialFailure
```

The three cases in the guest run execute sequentially in one test class, with
a fresh job store and fixtures per case. The runner also accepts each name
individually. The existing protected Hyper-V runner provisions a broker and
supports consent-bound operations. This guest companion executes only the
controlled profile test class, needs no broker installation or Windows consent
and uses no guest checkpoint or power operation. Codex owns both M5 drivers.
Replace them when a common scenario runner can preserve their exact selection,
payload checks, evidence and cleanup.

## Scenario and claim boundary

| Scenario | Entry point and assertion |
| --- | --- |
| `Candidate` | Job Seeker resolves four version-bound capabilities into four scheduled fixture no-ops. Approval is saved before any dispatch. Each action has an independent controlled postcondition. Reopen shows the same plan, approval, outcomes and workshop report. Recipient choices remain deferred and whole-device readiness stays held. |
| `OptionalFailure`, `EssentialFailure` | Inject the same video-call action failure in Basic and Job Seeker. The other three essential actions retain independent verification. Basic preserves preparation with the optional outcome visible, and Job Seeker holds preparation. |
| `NetworkInterruption` | Stop an owned installer stream after a partial write and validate cleanup. Reopen the WPF job with its saved binding and retry guidance. |
| `UiKill` | Terminate owned WPF clients after dispatch intent and after a saved applied outcome. Reopen and reconcile the exact job without another dispatch. |
| `BrokerKill` | Retain authenticated partial results when the broker transport ends. Terminate an owned package fixture process and reopen the ambiguous attempt for review. |
| `LowFreeSpace` | Inject low capacity before acquisition and a late decrease after acquisition. Prevent dispatch and reopen the saved deferral. |
| `ChangedMetadata` | Exercise material changes in package/digest binding, acquisition admission and WPF approval recheck. Require fresh review before dispatch. |
| `WrongClock` | Inject clock rollback and expiry at checkpoint validation. Independently confirm that an existing correlated sent receipt can still be verified. |
| `BrokenSource` | Inject disconnected, incomplete and invalid source responses. Verify bounded reasons and exclusion of backend text. |
| `SuccessWithoutInstall` | Backend success with missing or mismatched registration leaves independent verification held. |
| `LaunchCrash` | An early-exit launch result after exact registration remains unverified on reopen and in the workshop report. The outcome is injected at the reviewed probe boundary. |
| `MultiRestart` | Model three exact Windows Update passes with separate review, bounded arrivals, changed boot evidence, rescan and the full package-to-final-verification workflow. |
| `Offline` | Disconnect the configured update adapter. Reopen the same saved plan and approval with a visible hold and no install. After service recovery, continue to the package boundary under the same approval. |
| `SoftwareSingleWorker` | Force WPF software rendering and the default inventory worker count of one. Cancel a running provider, reopen the saved interruption, complete a second pass and read the workshop report. |
| `SlowDestination` | Use software rendering, one inventory worker and below-normal process priority. Hold the first SQLite provider append for 350 ms after cancellation, then finish its atomic commit, reopen and rerun. |
| `Accessibility` | Use the real WPF suite through Prepare, Verify and Handover with an isolated system-color dictionary, a 640 × 420 DIP window and simulated 2× layout. Check Tab/Space, live region names/settings, automation patterns, cancellation, retry and report reopen. |

The resource test uses 16 controlled providers with 16 facts each. It measures
startup to the loaded fixture shell, inventory, reopen, busy-stage response,
CPU, peak working set, sampled temporary bytes, database growth and rendered
report size. The owned workspace is removed and the test host priority is
restored. A separate ordinary product apphost observation measures startup
with the default renderer. The resource measurements apply to this workload
and test host. Windows Update cache size requires a native servicing run.

These constraints are regression-finding tools. All physical execution is on
the active Codex machine and its sole existing guest. Hardware claims are
limited to the recorded environment. Window layout and color controls use
internal fixtures. Actual Windows DPI, reduced display resolution, system
theme changes and audible or perceptual assistive-technology behavior have
separate validation scope.

## Live readiness prerequisites

The candidate profile and package catalog are synthetic. The live workshop
route supports one reviewed package. The reference guest remains unqualified
for whole-device readiness. Fresh system storage health requires trusted
mapping of the Windows system volume to its relevant physical disk evidence.
Aggregate restart clearance requires positive evidence wider than a clear
Windows Update flag. Ownership, guided device checks, finalization and
currently applicable policy findings retain their own evidence requirements.
[Profile verification evidence](../evidence/TL-0507/report.md)

The guest's recorded native update assessment found one unsupported-handler
revision and zero selectable revisions. Reopen native servicing when an
eligible exact revision exists with verified recovery, space, approval and
route authority. TL-0511 retains the approved live multi-action schedule,
independent action verification and the actual servicing, restart and rescan
requirements. TL-0508 retains candidate adoption. TL-0611 retains pilot
exposure. [Native scan evidence](../evidence/TL-0504/single-guest-inventory.md)

## Evidence reuse

- [TL-0317](../evidence/TL-0317/report.md) records actual authorized termination of a reviewed protected no-op broker in the sole guest, before dispatch. Its 111.558 s run returned zero terminal results and cleaned up the exact owned process. The current controlled broker case covers post-dispatch ambiguity without extending that native observation.
- [TL-0505](../evidence/TL-0505/report.md) records two orderly Windows restarts in that guest under the same saved binding, with controlled package and update results through the final verification handoff.
- [TL-0509](../evidence/TL-0509/guest.md) records actual guest power off and startup, fresh controlled verification and reopening the same saved job, approval and report. The resulting profile stayed blocked. The guest observation does not establish an active-host physical cold boot.
- [TL-0412](../evidence/TL-0412/report.md) records the reviewed native MSI transaction with independent registration and the saved job/report. It applies to that exact package and transaction.

The matrix adds tests and runners. It makes no change to the production
providers, qualification or package/restart trust rules used by those
observations.
