# TL-0501 - Dependency and phase scheduling checkpoint

## Base and scope

Work began after TRACE integration on `d207610b0594a9138510b7fd1cb5d341e892fe1b`, fetched and verified as `main`. TL-0404 and TL-0309 are complete. This checkpoint validates an action schedule against a bounded plan snapshot and projects progress from the saved journal. It leaves the existing package and broker routes unchanged.

On 2026-11-26, the deterministic Actions suite passed 181 tests in 442 ms and the focused SQLite action-journal suite passed 9 in 3 s. The baseline reused the clean TRACE build at `0c0b22b989592540523e16c696fa888fcfcf1d7d` after confirming the corresponding tracked source and tests match the merge revision. Environment was the active Windows 11 Pro 26H2 build 26300.9974 native x64 machine. Both runs used Release with `--no-build --no-restore`. The persistence filter selected `ActionJournalStoreTests`.

## Implemented behavior

`ActionSchedule` validates a bounded immutable graph against the action set
of an exact `JournalPlanSnapshot`. It rejects invalid action identities,
dependencies, phases and cycles. Eligible ties follow the reviewed plan's
action order. Dependencies and earlier phases require independent verification
before the projection offers the next action for review.

The projection requires the same job, plan digest and impact digest as the
saved journal. It retains per-action state, checkpoint and result, withholds
the review candidate while an operation requires reconciliation, and keeps
earlier verified actions visible after an independent failure. Historical
plans remain readable and provide no next review candidate.

## Verification

The Release verification ran on the active Windows 11 Pro 26H2 build 26300.9974 native x64 machine with .NET SDK 10.0.400. Tested code commit `ff3ac4622ff9d6731a272f99c24c60191e639f88`.

| Tier | Procedure | Result |
|---|---|---|
| Targeted | `eng/verify.ps1 -Tier Targeted -Restore`, Actions and Persistence projects, filter `FullyQualifiedName~ActionScheduleTests\|FullyQualifiedName~ActionJournalStoreTests` | Passed in 121.959 s. Locked restore, formatting, warnings-as-errors build, 1,203 Quick tests and 31 targeted tests passed. |
| Targeted | `dotnet test tests/ThirdLife.Actions.Tests/ThirdLife.Actions.Tests.csproj -c Release --no-restore`, deterministic selection | 202 passed in 340 ms on committed source. |
| Targeted | `dotnet test tests/ThirdLife.Persistence.Tests/ThirdLife.Persistence.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ActionJournalStoreTests` | 10 passed in 3 s on committed source. The test writes through the ordinary bounded journal API, closes and reopens SQLite, checks exact plan, impact and approval, and verifies the hold and progress after interruption. |

The close-and-reopen test supplies the same fixture graph at each reopen.
The saved journal supplies the action outcomes and existing approval.
Production dispatch and schema versions remain unchanged.

## Approval-bound execution checkpoint

The continuation started from `4656e86f9188c870658d709c7e6a50a87e655562`, the verified main merge of PR #68. The baseline passed 74 Actions tests in 319 ms and 10 SQLite action-journal tests in 2 s. It used the unchanged compiled source from the first checkpoint in Release with `--no-build --no-restore`.

The scheduled plan format writes the validated phases, dependencies and deterministic order into the exact plan digest. The impact digest already includes that plan digest. A changed graph requires approval against the changed snapshot. SQLite restores the graph entirely from the saved bytes. Existing versions retain their original bytes, approvals and historical interpretation. The format uses the existing bounded SQLite snapshot fields and requires no database migration.

A scheduled synthetic no-op plan uses the same durable broker approval and dispatch-intent path, with a single eligible action per transaction. SQLite checks the current sanitization gate and approval, graph position, any job-wide recovery hold and remaining journal capacity before committing the intent. The coordinator revalidates the current proposal before the intent and immediately before the backend sends the typed request. It waits for a separate postcondition observation and the matching saved verification before releasing dependent actions and later phases. A known failure leaves the affected branch blocked and retains earlier verification. Decline before dispatch holds remaining work for a fresh approval. A reopened job can observe a saved applied action without sending it again. An interrupted start stays held for reconciliation.

In the WPF fixture, rebuild the proposal for the same saved job to retrieve the matching approval and progress. The first run left one applied action pending verification. The reopened run verified that action, sent the remaining steps one at a time and completed with one saved approval and one dispatch intent per action. Plan details expose phases and dependencies, and the completed view keeps the saved approval with execution disabled. The fixture backend and verifier supplied deterministic observations.

### Continuation verification

On 2026-11-26 the active Windows 11 Pro 26H2 native x64 machine (build 26300.9974) ran the following checks against implementation commit `9e00a1b929cc7b810e787acf86d280d145056433` with .NET SDK 10.0.400 and Python 3.14.7.

| Tier | Procedure | Result |
|---|---|---|
| Targeted | `eng/verify.ps1 -Tier Targeted` with Actions, Persistence and UI projects, filtered to the deterministic ActionSchedule, ExecutionCoordinator, PlanDiff, ActionJournalStore and PlanReview suites | Passed in 128.103 s after locked restore. Formatting, warnings-as-errors Release build and all 1,204 Quick cases passed. Targeted selections passed 69 Actions, 13 SQLite and 12 WPF cases. The SQLite cases include competing independent connections, out-of-order intent, stale approval, decline, reapproval and exact restore. |
| Extended | Release WPF test filter `FullyQualifiedName~KilledNoOpApplicationReopensTheSameJobAndReconcilesWithoutDispatch` | Both versions passed in 10.152 s with zero skips. The owned WPF test process stopped at its committed intent before broker authorization. After reopening, the scheduled path retained one interrupted action awaiting review and kept all later actions approved. The legacy path retained its whole-batch history. Both reopened without dispatch. |
| Full | `eng/verify.ps1 -Tier Full -Restore` in a fresh detached worktree | Passed in 404.156 s. All 304 governance and 2,174 .NET tests passed with zero skips. The default Full selection retains its documented ActiveMachine, Extended and SandboxOnly exclusions. |
| Active-machine Targeted | `pwsh -NoProfile -File eng/run-ui-smoke.ps1` using the clean Release build | Passed in 2.623 s. The unelevated app opened TRACE, UI Automation selected Inspect, and the owned app exited normally. This startup sample reached 174,919,680 bytes peak working set. |

The clean Release apphost SHA-256 was `1ac66817ff009be05af90ad387bedd5429e79540ff5607ba181406ba9c4e2487`. The UI assembly SHA-256 was `c7bc12bb70711433c9bebc5991f7f9ecc5c7fb3021635ef8dcba4e88e5af0886`. The evidence worktree also passed all 304 governance regressions in 298.916 s after adding the execution prerequisites.

## Handoff from the approval-bound checkpoint

Live multi-action dispatch requires a second distinct action with a qualified supported backend, plus schedule-aware dispatch, independent verification and restart/reconciliation for each action in the real candidate. The current package adapter is qualified for the one pinned machine-wide vendor MSI. The scheduled coordinator is scoped to synthetic no-ops with a configured verifier. Ordinary privileged broker use retains the TL-0318 application qualification and TL-0314 privilege prerequisites. Reopen live execution when a real sequence and its per-action adapters are qualified. Record the operator's exact plan approval and applicable Windows consent before any live dispatch.

## Scheduled package execution

The package continuation started at main merge `38161d462549491b25d4574da669e3eb93f8f699`. The previous committed Release build passed the focused Actions and journal baseline (45 and 13 cases) and all 43 deterministic WPF package cases. The active machine was Windows 11 Pro 26H2 build 26300.9974, native x64.

The ordinary Prepare workflow binds the reviewed machine-wide package to its saved schedule. It writes the operator's exact approval and per-action entries atomically. The serial executor reloads that approval and schedule before selecting each action. SQLite admits one correlated intent after checking the current sanitization gate, fresh approval, dependencies, phase, per-target attempt budget, job-wide recovery state and space for the outcome and any restart proof. Windows Installer remains the supported mutation boundary. The application re-resolves the exact plan before entering that boundary. Existing package identity checks, secure-desktop authority and independent installed-state and launch verification still apply.

A started, applied or review-required step holds the schedule until recovery. Recovery binds to the exact action, approval and correlation. A restart-pending result needs the matching boot proof and package verification before releasing the next phase. A definite failure blocks its dependents while preserving verified siblings. A Windows consent decline or changed dispatch authority stops further dispatch under that approval. Retry limits and cooldowns follow the exact target across plan versions. Existing plan bytes and saved history remain readable. A current review of a prior-version retry records fresh approval for the new schedule, links to the prior attempt and carries forward its attempt count.

The ordinary WPF package journey uses the pinned Notepad++ 8.9.9.1 x64 adapter. The multi-package tests use distinct prepared fixture identities through the same serial executor and real SQLite path, with an injected package outcome and boot source. The separately qualified pinned Windows Installer transaction remains attributable to TL-0412. The existing application exposes one qualified live package. Other package shapes require an exact qualified adapter before dispatch.

### Package scheduler verification

On 2026-11-26 the active Windows 11 Pro 26H2 native x64 machine (build 26300.9974) tested implementation `5c466d91a747d100d629b4b8053925c2d62089e8` using .NET SDK 10.0.400 and Python 3.14.7.

| Tier | Procedure | Result |
|---|---|---|
| Targeted | `eng/verify.ps1 -Tier Targeted` for Actions, Persistence, UI and Packages after locked restore | Passed in 161.354 s. All 1,204 Quick cases passed. The targeted selections passed 102 Actions, 25 SQLite, 43 WPF package and 41 package adapter cases. The WPF install route saved the bound plan and approval, waited for the package outcome, and independently verified and reported the same saved attempt. |
| Extended | Release UI test filters `KilledNoOpApplicationReopensTheSameJobAndReconcilesWithoutDispatch`, `KilledPackageApplicationReopens` and `TerminatedOwnedPackageBackendLeavesTheReopenedAttemptInReview` | Five passed in 26.657 s. The package process-loss cases reopened the scheduled job at the saved intent or applied checkpoint. The other cases covered frozen backend and legacy and scheduled no-op recovery. The fixture app and SQLite used the same controlled clock after the freshness check surfaced their original clock mismatch. |
| Full | `eng/verify.ps1 -Tier Full -Restore` in a fresh detached worktree | Passed in 379.143 s, with 304 governance and 2,181 .NET tests and zero skips. The default Full exclusions apply. |
| Active-machine Targeted | `pwsh -NoProfile -File eng/run-ui-smoke.ps1` from the clean Release worktree | Passed in 2.407 s. The unelevated app opened and selected Inspect through UI Automation before closing normally. Peak working set during this startup observation was 173,395,968 bytes. |
| Active-machine readiness | `eng/run-m4-package-preflight.ps1` on the exact bound guest and reviewed checkpoint | Passed in 3.88 s. The Windows Installer executable was available, the exact package registration was absent, and no installer process was observed. This read-only run did not create a job or dispatch a package. |
| Active-machine guest desktop | `eng/run-m4-package-desktop.ps1 -Inspect` on the exact bound guest | The current artifact timed out at launch (instrumented repeat 71.102 s). The previous verified UI artifact from `9e00a1b929cc7b810e787acf86d280d145056433` timed out waiting for its application window in 74.513 s in the same session. Both inspected only startup and cleaned their stages. The package workflow was reached through the host WPF and SQLite fixtures. |

The clean Release apphost SHA-256 was `927cf5cf6e3e67fa1f86b2668906cf97d023f69db9c4051521b3764217994286`. The UI assembly SHA-256 was `03bfe1e6e0da898d45a8525a619dc58f3ac72cd20b65ebf1cf61c5943c58b767`.

The TL-0501 code acceptance covers dependency order, rejection of cycles and phase inversions, single-action admission, independent failure, and durable recovery. The live multi-action requirement added in PR #69 remains explicit under TL-0511, the M5 technical gate that exercises the complete profile. It requires at least two distinct live actions, a qualified route for each, per-action verification and restart/reconciliation, exact plan approval and applicable Windows consent. This records which task supplies the live evidence and keeps the pilot and release gates in force.
