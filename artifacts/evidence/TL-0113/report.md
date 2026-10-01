# TL-0113 — Accessible six-stage WPF shell

**Date:** 2026-10-01  
**Branch:** `codex/tl-0113-accessible-shell`  
**Base:** `d4ec836719746c659f12ca851f9890d62c76bfa7`  
**Status:** review — required human keyboard and screen-reader evidence pending  
**Runtime authority:** active Codex machine; remote CI is supplementary

## Outcome and scope

The shell exposes Intake and Inspect for reviewing requirements, evidence and plan regions.
Decide, Prepare, Verify and Handover stay disabled with readable criteria. Unknown evidence,
missing jobs and absent plans remain explicit. The battery safety warning is preserved.
Opening a view never creates evidence, permits assessment or approves a plan.

This is the shell contract from TL-0113. No job/store/provider/network/broker is opened.
The complete durable operator audit flow still needs composition and verification before
TL-0116; this shell does not satisfy that M1 gate by itself. See
[`docs/ui/accessible-shell.md`](../../../docs/ui/accessible-shell.md).

## Continuity

PR #13 merged TL-0112 before this task began. The prior head `f7ace816a6e1d08da24a2e9e43c33a026477f357`
is an ancestor of main and has no unique unmerged commit. Local main fast-forwarded to the
base above, the complete dependency chain was parsed as done, and this task began on a new
normal branch. The SSH origin is unchanged; synchronization uses the process-scoped HTTPS
bridge. No branch was deleted, history rewritten or sibling repository accessed.
The existing untracked Word reference remains untouched.

## Acceptance mapping

| Criterion | Implementation and observed evidence |
| --- | --- |
| Unelevated startup | `asInvoker`/`uiAccess=false` manifest; current-token administrator check before constructing the main window; unknown/elevated cases refuse with fixed recovery guidance. Manifest and fail-closed decision tests pass. Direct-host Release apphost/UIA observation passes under a standard-user token. No elevation was requested for a negative live test. |
| Keyboard reachability and focus | Native controls, explicit initial Intake focus, focusable named regions, skip/return actions and keyboard paging. Loaded-window tests verify the exact tab cycle, both stage return targets and continuous visibility of disabled-stage criteria while paging. |
| Textual status | Empty/unknown evidence, unavailable stages, activity, cancellation, failure and review have explicit text. Native system colors preserve contrast-theme participation. |
| Accessible names and progress | Standard automation peers expose names, help, roles, disabled/current states and read-only progress. Polite live regions coalesce changes after bindings settle. Deterministic activity fixtures exercise unknown/determinate progress, cancellation requested/confirmed, completed, failed and review; completion never claims device readiness. |
| 200% and reduced viewport | Software-rendered loaded-window fixtures use a 480×320 DIP window, a 2× content transform and expanded long text. They verify reflow, no horizontal scroll, bottom reachability and focus return. Actual Windows scaling/contrast and human AT results remain pending. |
| Disabled stages retain criteria | Four unavailable buttons reject UIA invocation. Their separate criteria remain visible, named and accessible through keyboard paging. |

## Verification

Exact commands, durations, failed attempts, environment and result digests are in
[`runs.json`](runs.json). Source/workload hashes are in [`inputs.json`](inputs.json).

- Pre-edit direct-host Release baseline: UI **3 passed**, zero failures/skips, **7.476s**.
- Focused model tests: **24 passed**, **5.018s**. Initial startup/manifest checks: **7 passed**.
- Corrected transient Sandbox Targeted, including Quick: Core **83**, Inventory **901**,
  UI **43**, all passed without failures/skips. UI is selected again by Targeted and passes
  the same 43 cases. Shared tier **144.926s**; whole guest **201.735s**.
- Full Python governance regression, classified Targeted: **275 passed**, **349.394s**;
  focused runner/validator regressions **78 passed**, **160.298s**.
- Direct-host UIA smoke observes standard-user startup, Inspect navigation and changed
  textual status, then normal close. It uses installed Windows UI Automation because
  Application Control blocks the rebuilt xUnit assembly; it does not change that policy.
- Committed-source Sandbox verification and the final source-bound host smoke follow the
  implementation checkpoint. Their durable result files will be linked here at handoff.
- Full: not triggered. This is the initial scoped shell under the explicit Targeted task
  contract, with no major refactor, migration, dependency/engine change or release gate.
- Extended: not triggered. Small software-rendering/scaling fixtures run in Targeted;
  there is no broad resource, interruption, hardware or endurance matrix.
- Remote CI: not run as task evidence; any automatic GitHub result is supplementary only.

## Defects and limitations

1. Loaded-window checks found `ProgressBar.Value` defaulting to TwoWay against a read-only
   property, which crashed `Window.Show`. The binding now explicitly uses OneWay. The
   direct-host apphost smoke and all loaded-window/activity regressions pass after correction.
2. The first Sandbox run stopped at a whitespace error in the new STA helper. Full-solution
   format and verify-no-changes corrected/verified it before the related Targeted rerun.
3. Host Application Control `0x800711C7` rejected `ThirdLife.UI.Tests.dll` before discovery
   in Debug and Release. This is a blocked run, even when the test runner exits zero after
   reporting no matching tests. Host security settings are unchanged. Sandbox component
   results are authoritative on this physical machine.
4. The separate xUnit `ActiveMachine` event-listener test remains unexecuted because of that
   loader restriction. The host observer verifies the changed UIA status, not delivery of
   speech or an event callback. Human Narrator results remain required. Sandbox component
   tests do not prove standard-user apphost startup because its guest token differs.
5. Human keyboard, Narrator, actual 200% display scale, reduced-resolution and high-contrast
   results are **Not run** in [`human-walkthrough.md`](human-walkthrough.md). NVDA availability
   and approval remain unverified. No result or sign-off is inferred from automation.

## Environment, resources and data boundary

Reference hardware: `REF-CODEX-001`, revision `2026-08-29.1`. This task observes the current
host as Windows `10.0.26300.0`, SDK `10.0.400`, PowerShell `7.6.5`; the older reference profile
recorded build 26200 and PowerShell 7.6.4. Sandbox uses the same physical machine, 4 GiB memory,
read-only inputs, pinned tools, locked packages, audit enabled and networking only for governed
NuGet audit. Source/package bindings, bounded output and cleanup remain enforced.

The shell retains six fixed stages and one bounded activity projection (0–16 work units).
It has no polling, operation worker, history collection or application-managed cache/disk
growth. Loaded shell tests explicitly select software rendering. The direct-host observation
records default WPF rendering, elapsed time, CPU time and peak working set; this includes
startup/UIA overhead and establishes no resource budget or minimum hardware specification.
OS/runtime cache behavior is not measured. No other hardware or assistive technology is certified.

No data migration, schema, catalogue, provider contract, sibling behavior or release-interface
entry changes. No recipient/donor data, token, raw exception, window transcript or personal
path is retained. `eng/run-ui-smoke.ps1` is a small reusable observer, not a copied Sandbox
runner: the existing guest cannot supply the required direct-host standard-user observation.

## Human handoff and next task

Complete the source-bound [human walkthrough](human-walkthrough.md). The hybrid task stays
`review` until the required results exist. Publish the normal draft PR; merge only after the
task's evidence is complete. TL-0114 depends on TL-0113 and is not ready. No next product task
was started. Live task evidence is a compact reference; detailed results remain here.
