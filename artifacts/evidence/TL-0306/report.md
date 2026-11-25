# TL-0306 plan review and approval UI

**Date** 2026-11-25  
**Base** `be79922469a2295269afc12e29feaf44e2722fba`, PR #30  
**Environment** REF-CODEX-001 rev 2026-08-29.1, Windows 11 x64, pinned .NET SDK 10.0.400 and Python 3.14.7

## Contract

| Criterion | Evidence |
| --- | --- |
| Normal entry point | The Prepare button focuses plan review in the loaded audit workspace when the current assessment and intake gate allow review. Selecting a profile changes selection only. |
| Action selection | Optional checkboxes invalidate the displayed proposal when changed. Skipping any essential action displays unmet expected readiness and disables approval until the action is included again. |
| Review details | The native Details disclosure shows exact source, package ID, publisher, version, scope, reasons, privilege, disk estimates, downloads, network, restart, rollback, verification, and deferred recipient choices. |
| Exact approval | Approval reloads the current job and assessment and re-resolves the plan. It records the exact displayed plan and impact snapshot only when their plan digest still matches. Decline records the displayed snapshot without authorizing it. |
| Keyboard and scale | The five loaded-window tests use WPF automation and keyboard focus, and include Details and decision access in a 200% layout transform and constrained viewport. Status uses the polite live region. |
| No action dispatch | The UI references no broker assembly or launcher. The full route is synthetic no-op review. The real job store is read to recheck the assessment. Tests verify selection, preview, and approval leave saved job and assessment records intact. |

The ordinary saved intake contract uses `m1-intake-v1`. The original planner
expected an organization policy reference on the separate intake record. The
resolver recognizes the independent intake contract for synthetic review and still
checks the actual latest gate and matching evidence. Saved records need no
migration or re-attestation. Package proposals retain their exact policy-version binding.

Review decisions are retained for the open review session. Durable approval
storage and dispatch are owned by later tasks. The five UI tests use an
explicit ready synthetic policy fixture for approval and the ordinary bundled
candidate for the blocked-policy case. The 200% check is a WPF layout
constraint on this machine. It does not change system DPI or claim perceptual
screen-reader results.

## Verification

- The UI baseline passed 82 tests in the original checkout before edits.
- The first compile flagged implicit locale in display text construction.
  The presentation now uses explicit current culture for user-facing values.
- `dotnet test tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PlanReviewTests`
  passed all five loaded-window tests in 9.416 s.
- The review initially opened the plan details collapsed. Details now open
  with each newly displayed preview and remain collapsible by keyboard.
  The same five focused tests passed after the visible-details change in
  9.433 s.
- The first-party UI references trigger Full. Force-evaluate and locked
  solution restores passed in 1.599 s and 1.640 s. Exactly two lock files
  changed. All third-party records and the approved licence matrix are intact.
- The UI and Actions Targeted selection passed 1,185 tests in 64.225 s.
  Pushed source checkpoint `5517e07` matches the clean Full snapshot tree.
- The named active-machine `ShellProcessSmokeTests` case passed and closed
  its owned unelevated apphost. UI Automation observed the live navigation
  announcement. Elapsed time was 1,140 ms, peak working set 212,832,256
  bytes, and CPU time 1,156.250 ms. This measures startup and navigation on
  the reference machine. The UI assembly SHA-256 was
  `d06dab160d168ff20871bea04cbb12ba01e57e7d4e29d2fafe5d512a1071c38a`.
- `.\eng\verify.ps1 -Tier Full -Restore` passed 287 governance tests and
  1,748 .NET tests in 323.95 s on the clean same-machine worktree, including
  locked restore and a Release build with zero warnings. Its source tree
  `f1818707fac36743a4651ab26a4079fe3e35b82b` matches pushed checkpoint
  `5517e07f2e5b16016a0db8bdb5615eedcb3994d0`. Extended was outside the
  task's triggers.
