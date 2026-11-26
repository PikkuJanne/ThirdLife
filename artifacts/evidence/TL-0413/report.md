# TL-0413 — TRACE native application

## Source and authorization

- The owner approved the TRACE v1.0 interface replacement for ThirdLife Setup Core. The build bundle ZIP SHA-256 was `208a66318530583f65c64d8e4e2e97e69273b96560b22ad3c16819dcdfb642b9`. All 30 payload hashes matched.
- The design source `d61c49e9bb4cc8f5b18c4ac3576a713060cb3f77` is an ancestor of fetched `main` at `4954fe7a9ef08e89357245d0fc07d62ec2129bc7`. Implementation started at the fetched M4-gated main in a separate worktree. Original untracked material and prior worktrees were preserved.
- The verified scope is the native presentation and existing service entry points. The production package route still depends on the exact action approval and applicable Windows consent. Device acceptance and recipient-facing features remain governed by their own implementation and release gates.

## Baseline

On 2026-11-26, `eng/verify.ps1 -Tier Quick` passed in 144.338 s on the active Windows 11 Pro 26H2 build 26300.9974, native x64, .NET SDK 10.0.400. Core 126, Inventory 927, UI 180. The run exceeded the 120 s target before TRACE changes. Its dominant test project was UI at 63 s. The run used structural bundle validation.

## Checked workflow

Jobs opens the existing local store lazily. Intake shows the saved record and lets the operator resolve an interrupted gate. Inspect shows a provider finding and its source, collection time and exact evidence reference, or a selected manual procedure and saved outcome. Decide shows the real candidate evaluation, its evidence and linked repairs. Prepare displays the reviewed profile, eight impact areas, exact approval and package operation. Verify reads the attempt journal, distinguishes installer results from independent verification, and exposes scoped retry, installed-state checks and restart review. Handover generates and opens the restricted saved workshop report.

The jobs list continues to use create and open by code. A recent-jobs query is deferred because the existing store has no bounded discovery route for approved metadata. The isolated no-op qualification remains available to engineering fixtures. Its historic journal can be read without presenting normal dispatch controls.

## Verification

The tested source candidate is `0c0b22b989592540523e16c696fa888fcfcf1d7d` in [PR #67](https://github.com/PikkuJanne/ThirdLife/pull/67), based on `4954fe7a9ef08e89357245d0fc07d62ec2129bc7`. Verification ran on 2026-11-26 on the active Windows 11 Pro 26H2 build 26300.9974, native x64, under the pinned toolchain.

| Tier | Procedure | Observed result |
|---|---|---|
| Targeted | `eng/verify.ps1 -Tier Targeted -TestProject tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj` | Passed in 110.773 s during iteration. 126 Core, 927 Inventory and 150 UI tests. |
| Quick | `eng/verify.ps1 -Tier Quick` | Passed in 113.642 s after correcting the findings text encoding and clamping initial window dimensions to the usable desktop. |
| Focused | Release UI tests filtered to deterministic `PlanReviewTests` and `AuditAccessibilityTests` | 14 passed in 11 s with the final job-entry progress and unavailable-profile bindings. |
| Native visual QA | Owned M1 fixture at current 240 DPI, with narrow and wide native Inspect layouts | Verified wrapping, left stage selector, selected finding and provenance panel. A rerun of the keyboard journey with an additional visual capture passed in 9.612 s on the committed source. |
| Accessibility | Release `ShellAccessibilityTests`, `WheelScrollingTests` and `BatteryInspectionTests` | 11 focused native component checks passed. Coverage includes 320 DIPs, simulated 2× layout with long text, the dark/light transition, system-color resource resolution, modal focus return and nested scrolling. |

The first Targeted run surfaced a lost readiness qualifier. The copy now explicitly states that assessment leaves readiness unknown. Visual inspection caught corrupted separators, which were corrected and covered by the reopened fixture assertion. The desktop driver now waits for controls to appear after a stage transition and asserts live regions from outside the app.

Native and component checks establish keyboard and UI Automation behavior under the stated conditions. System high-contrast resources were exercised with an isolated palette fixture. Windows high-contrast switching, perceptual screen-reader output, other device classes and physical hardware checks are outside the observed scope. No new live MSI installation, Windows consent, hardware intervention or reboot was used to verify the redesign. Prior package and consent evidence retains its exact scope in [TL-0411](../../gates/M4-package-vertical-slice.md) and [TL-0412](../TL-0412/report.md).

## Clean integration verification

All checks below ran on the detached, clean active-machine worktree at `0c0b22b989592540523e16c696fa888fcfcf1d7d`.

| Tier | Procedure | Result |
|---|---|---|
| Full | `eng/verify.ps1 -Tier Full -Restore` | Passed in 371.694 s. All 304 governance regressions and 2,138 .NET tests passed with 0 skips. Locked restore, formatting and warnings-as-errors Release build passed. |
| Active-machine Targeted | `pwsh -NoProfile -File eng/run-m1-audit-journey.ps1 -VerifyAccessibility` | Passed in 7.884 s at 240 DPI (250%). The external keyboard driver created the job, saved intake, ran inspection, recorded a manual result, reopened the same job and opened the persisted report. Three UI Automation live announcements and fractional scrolling passed. SQLite evidence and original saved report bytes agreed. The fixture exited normally. |
| Active-machine Targeted | `pwsh -NoProfile -File eng/run-ui-smoke.ps1` | Passed in 1.304 s. The ordinary unelevated apphost opened TRACE, exposed stage selection and closed normally. Peak working set was 174,145,536 bytes during this startup observation. |
| Extended | Release UI test filter `FullyQualifiedName~KilledNoOpApplicationReopensTheSameJobAndReconcilesWithoutDispatch\|FullyQualifiedName~KilledPackageApplicationReopens\|FullyQualifiedName~TerminatedOwnedPackageBackendLeavesTheReopenedAttemptInReview` | 4 passed in 14 s. Owned fixture process loss and backend termination retained the correct journal and review state. |
| Supplementary | GitHub Windows check on the exact implementation commit | Passed. The local checks above provide the runtime and integration evidence. |

The verified Release artifact is under `src/ThirdLife.UI/bin/Release/net10.0-windows/`. The apphost SHA-256 is `448060c5172b22e93f754c1507a25f09123c3e21ad8b5668e05fa157ff780670`. The UI assembly SHA-256 is `51e1b9932ec67ce14e6ee3306db5b5501a21c87632c2f6b5719f79cb0c41eb9e`. The apphost uses the installed .NET 10 Windows Desktop runtime. The worktree remains available for reruns and local launch.

TL-0501 is the next engineering-ready M5 task. Its first checkpoint validates the action graph and reconstructs per-action status from an exact saved journal.
