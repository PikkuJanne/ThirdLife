# TL-0502 — Essential and optional action outcomes

## Outcome

Prepare uses the capability classification in the exact reviewed profile and approval. The saved plan parser now exposes the existing essentiality, capability, reason and evidence fields. Existing plan and impact formats remain readable with their original bytes. Changing a reviewed classification changes the plan digest and needs fresh approval.

A whole-plan decline records an attributable skip for every planned action in one SQLite transaction. An operator can also skip an individual approved scheduled action before dispatch. The decision is bound to the saved plan, action and current approval. An existing dispatch intent, unreconciled execution, changed approval or invalid intake blocks the corresponding write. An unchecked capability during initial profile selection is outside the proposed action list.

Verify and the restricted workshop report explain the saved action states and their effect on preparation disposition. Essential skips and essential failures require review. Settled optional omissions allow independent work to proceed, while an explicit dependency still requires verification of its prerequisite. Ambiguous outcomes hold readiness. Reopening restores attribution and action history. Reassessment can make a plan historical while retaining its earlier skip and failure records and the original assessment policy disposition. Whole-device acceptance continues to require its separate checks and finalization.

The governed exception tests exercise a reviewed policy rule for an exact, settled essential action outcome. The rule uses `preparation.action.<capability_id>` with the `prepared_action_outcomes` activation, and the record matches the job, policy digest, journal evidence, authorization and timestamp. A named exception affects readiness only. The effective package policy has no such authorization. Enabling production exception authoring requires a reviewed effective rule and durable workshop-restricted recording and retrieval.

## Verification

The verified base was `dd06172b87d3577def1a408e9b147f79f0342422` from `origin/main`. Verification used Windows 11 Pro 26H2 build 26300.9974, native x64, .NET SDK 10.0.400, Python 3.14.7 and the pinned dependencies. GitHub SSH rejected the available key. An authenticated HTTPS fetch confirmed the same named repository, upstream and base.

The detached baseline ran `eng/verify.ps1 -Tier Quick -Restore`. All 1,204 selected .NET cases passed in 145.762 s. The cold locked restore and build ran alongside implementation iteration. The WPF cases took 56 s. The first setup attempt found the clean checkout lacked PyYAML, so verification used the pinned local environment. The first feature formatting run found LF line endings in new source. The subsequent run used repository-required CRLF.

The tested implementation is commit `82220b3ce48fe69e4e67adeaecbafff3004a9378`.

| Tier | Procedure | Result |
|---|---|---|
| Targeted | `eng/verify.ps1 -Tier Targeted` with the Actions, Persistence, Reports, Packages and Policy projects after locked restore | Passed in 176.333 s. Quick passed 1,207 tests, including 154 WPF cases. Targeted passed 237 Actions, 156 SQLite, 26 Reports, 45 Packages and 188 Policy cases. |
| Extended | Release UI test filters `KilledNoOpApplicationReopensTheSameJobAndReconcilesWithoutDispatch`, `KilledPackageApplicationReopens` and `TerminatedOwnedPackageBackendLeavesTheReopenedAttemptInReview` | Five passed in 20.872 s. Owned-process interruptions exercised the legacy and scheduled no-op paths and the scheduled package intent, apply and backend-loss paths. |
| Full | `eng/verify.ps1 -Tier Full -Restore` in a fresh detached worktree | Passed in 373.704 s. All 304 governance tests and 2,209 deterministic .NET tests passed with zero skips. The default Full selection excludes ActiveMachine, Extended and SandboxOnly cases. |

The WPF scheduled no-op fixture recorded separate optional and essential skips through the accessible Prepare buttons. The backend and verifier received only the remaining actions. The reviewed machine-wide package profile used an injected package adapter to check an attributable essential skip with zero install calls. On reopen, Verify and the workshop report displayed the skip and its readiness effect. Reassessment retained the historical outcome. SQLite tests also exercised an interrupted atomic decline write, idempotent retry and stale/started-action rejection.

The standalone `eng/run-ui-smoke.ps1` check first reported failure after 2.117 s and the owned process exited normally. A diagnostic repeat added a named failure phase and identified the first Inspect control lookup after the native window handle appeared. The same diagnostic passed on the baseline in 2.178 s. A further diagnostic polled for the enabled UI Automation control within a bounded startup window. The clean TL-0502 artifact opened Inspect in 1.049 s and closed normally in 1.231 s, with a peak working set of 173,125,632 bytes. This confirmed a race in the observer. The repository script now waits for the enabled control and passed in 1.309 s on the branch build. The observation covers startup and stage navigation.

The clean Release apphost SHA-256 was `2915ee20add818c4d57ebec3647e2411b303ed363598e57981022ed148f6d7bd`. The clean UI assembly SHA-256 was `a6bf99e2f92d54d8a7e714330b652cf966dc6bb4e4ce7cca32f67012c86e7da2`.

The evidence and observer checkpoint passed `eng/verify.ps1 -Tier Quick` in 115.341 s with 1,207 selected .NET cases. The governed bundle digests matched the updated task and status records.

## Continuation

TL-0503 can implement preflight disk, power and resource checks next. Live multi-action qualification remains at TL-0511 with at least two qualified live actions, restart and recovery proof, exact operator approval and applicable Windows consent. This task exercised skip and failure behavior with deterministic WPF, SQLite, broker and package fixtures on the active machine.
