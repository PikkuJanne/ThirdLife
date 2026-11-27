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
| `NetworkInterruption` | Stop a controlled installer stream after staging 128 KiB and verify removal of the partial file and its owned directory. Reopen the WPF job with its saved binding and retry guidance. |
| `UiKill` | Terminate owned WPF clients after dispatch intent and after a saved applied outcome. Reopen and reconcile the exact job without another dispatch. |
| `BrokerKill` | Retain authenticated partial results when the broker transport ends. Terminate an owned package fixture process and reopen the ambiguous attempt for review. |
| `LowFreeSpace` | Inject zero bytes available on the system and staging volumes before acquisition. Separately inject critically low battery after acquisition, prevent dispatch and reopen the saved deferral. Package preflight reserves 1 GiB on the system volume and 128 MiB in staging before overhead and payload. |
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

## Verification history

The starting point was `29e432f3de4b459566997fade95a0fdb111767d7`.
The two baseline WPF maintenance continuation cases passed in 10 s of test
time in a clean detached worktree. The first format check exposed Windows
line-ending differences, and the initial focused resource run exposed a
fixture dispatcher issue. The resource rerun uses the ordinary Close job
and Open job controls on the window dispatcher.

The original offline assertion looked for persisted scan feedback in the
transient maintenance phase banner. After reopen the phase is still Windows
Update, and the saved network reason is visible in the Windows Update
result bound in Prepare and Verify. The corrected case checks both fields,
the exact approval and the subsequent scan recovery. The first clean Full
attempt passed 310 governance regressions and stopped at the testing guide's
stale manifest digest. The refreshed digest passed the bundle and repository
validators.

Targeted, including Quick, passed in 167.620 s with 1,358 test executions
and zero skips, plus eight work-contract regressions. It selected the
exact offline case and two existing maintenance continuation cases beyond
Quick. The standalone iteration Quick took 157.615 s against the 120 s
target with locked restore, formatting and 85 s of WPF test time. Targeted
completed within its 600 s target.

```powershell
./eng/verify.ps1 -Tier Targeted -TestProject tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj `
    -Filter 'FullyQualifiedName~OfflineMaintenancePreservesTheApprovedJobAndResumesAfterReopen|FullyQualifiedName~FullMaintenanceReviewResumesThroughFinalRestart'
```

On 27 November 2026 the clean checkpoint
`7431f58fdfaa1053fece0fa32e98e3a72046fdcf` passed
`eng/verify.ps1 -Tier Full -Restore` in 408.203 s. It passed 310 governance
cases and 2,543 deterministic .NET cases across 13 assemblies with zero skips.
Locked restore, formatting and the warnings-as-errors Release build passed.
The host ran Windows 11 Pro 26H2 build 26300.9974, native x64, .NET SDK
10.0.400, Python 3.14.7 and PowerShell 7.6.9.
[Reference-machine profile](../../docs/testing/reference-machine-profile.md)

## Active-machine matrix results

All 17 named scenarios passed at the same clean checkpoint. The 26 selections
executed 40 cases in 94.102 s of summed selection time. The receipt records
each filter, result, assembly digest and time. [Matrix receipt](../evidence/TL-0510/matrix.json)

The measured workload is `m5-inventory-recovery-v1`, SHA-256
`36217f57510f23145cba1aad962d3eed674f367df680e6b97223ef549fa24bec`.
Both cases used software rendering, one worker and a 640 × 420 DIP window.
The test sampled owned temporary and rollback-journal files every 10 ms.
Memory is process peak working set including the isolated test host.

| Measurement | Normal priority | Below normal, delay 350 ms |
| --- | ---: | ---: |
| Load fixture window | 690.45 ms | 743.54 ms |
| Complete inventory | 527.83 ms | 440.38 ms |
| Navigate while busy | 59.14 ms | 61.79 ms |
| Cancel to durable outcome | 166.13 ms | 554.04 ms |
| Reopen saved job | 184.25 ms | 137.23 ms |
| Whole fixture elapsed | 2,767.99 ms | 2,999.71 ms |
| CPU time | 2,593.75 ms | 2,421.88 ms |
| Peak working set | 170,971,136 bytes | 170,266,624 bytes |
| Sampled temporary peak | 254,512 bytes | 254,512 bytes |
| SQLite growth | 385,024 bytes | 360,448 bytes |
| Workshop report | 1,329 bytes | 1,329 bytes |

The storage sample can miss shorter write peaks. The owned fixture directories
were removed and the test host priority was restored. The interrupted package
transfer separately observed and removed its 128 KiB staged payload. Native
Windows Update cache growth remains unmeasured. The samples provide reference
measurements and carry no approved performance budget.

The ordinary compiled application ran unelevated with default WPF rendering.
UI Automation reached Inspect after 1,193 ms. The process closed normally
after 1,356 ms, with 175,214,592 bytes peak working set and 1,031.25 ms CPU.
[Startup receipt](../evidence/TL-0510/startup.json)

## Single-guest candidate results

The first guest invocation closed its PowerShell Direct runspace before
staging. A read-only check on the same guest found zero M5 staging
directories. The corrected runner retains the connection engine through
cleanup and keeps the existing per-operation time bounds.

The corrected checkpoint `84e47d5fe6b697e14cb9f1463f61ed8b30fce2be`
rebuilt Release with zero warnings and zero errors. The existing
`ThirdLife-W11` ran Windows 11 Pro 26H2, build 26300.9974, native x64.
The three cases passed in 91.149 s including staging and verified cleanup.
The guest test command took 33.489 s. The run used the established
administrative PowerShell Direct connection for private staging and a
controlled workspace and journal for execution. The separate host
application startup observation used a standard-user token.
[Guest receipt](../evidence/TL-0510/guest.json)

Job Seeker reopened the exact plan, approval and same four independent
fixture verification outcomes in the restricted workshop report. Basic
preserved its three essential outcomes when the optional video-call
action failed. Job Seeker held preparation for the same failure, with
the three independent outcomes intact. Each case created a fresh
isolated SQLite store. The host archive, guest staging, SDK and fixture data
were removed. The existing running guest remained on the same checkpoint
state with no power transition.

| Pinned candidate input | SHA-256 |
| --- | --- |
| Community Laptop candidate catalog | `60880321e29f0db5deb77c85bd10d1c5926ca1b068d930445695580891c1bdce` |
| Basic profile | `d80c1c029067ebc3f172011cf66a551eff50c7661c6daf8874aced23324130e2` |
| Job Seeker profile | `928e0a6a5072a480d416861fc1051afd5efd2fc32b40459cad4aa4c3b113eb05` |

Continuity Quick passed 1,355 cases and eight work-contract regressions
in 148.760 s. WPF accounted for 83 s. Quick exceeded its 120 s target
by 28.760 s. The follow-up adjusts the auxiliary PowerShell connection
lifetime and evidence while preserving the Full-tested .NET source.
