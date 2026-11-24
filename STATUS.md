# ThirdLife Setup Core — Current Handoff Status

**Snapshot date:** 2026-11-24  
**Bundle baseline:** 0.5.0  
**Runtime authority:** active Codex machine only; remote CI is supplementary  
**Current milestone:** M1 — Audit-only vertical slice  
**Current task:** TL-0113 — Build the accessible six-stage WPF shell; review

## Continuity

TL-0112 PR #13 is merged into main `d4ec836719746c659f12ca851f9890d62c76bfa7`.
The prior branch has no unique commits outside main. TL-0113 began from updated main on
`codex/tl-0113-accessible-shell`; its complete dependency chain is done. SSH origin is
unchanged; fetch uses the process-scoped HTTPS bridge. Prior branches and the unrelated
untracked Word reference remain untouched.
Draft PR #14 continues TL-0113 and the owner-authorized
[minimal human testing amendment](docs/amendments/2026-11-24-minimal-human-testing.md).
Fetched refs and PR ancestry matched the supplied head/base snapshot. TL-0113 remains in
review on this line pending the required human shell observations.

## Outcome and evidence

The six-stage review shell now separates requirements, evidence and plan. Intake/Inspect
navigation creates no evidence, policy pass or approval; later stages retain readable criteria.
Startup requires a standard-user token. No database, provider, network or broker is opened.

The amendment's active-host Targeted run including Quick passed Core 83, Inventory 901 and
UI 43 with no failures/skips in 101.017s. UI ran once. The complete governance suite
passed 278 tests in 321.827s. A live no-match test returned exit 1. Full/Extended were
not triggered. [Amendment verification](artifacts/evidence/TL-0113/amendment-verification.md)
records setup and retries. Prior Sandbox and apphost smoke evidence remains bound
to verified source `518744b751c531680e24a99b964c3b05a9b7c434`.

The [short shell check](artifacts/evidence/TL-0113/human-walkthrough.md) binds that unchanged
Release apphost and assembly. Keyboard start/reopen and supported settings were rehearsed.
Human keyboard, Narrator, scaling, reduced resolution and contrast results remain pending.

## Next action

Collect the four replies from the prepared shell check, record the actual case outcomes
and verify restoration. Resolve any findings and retest affected interactions. Merge PR #14
once TL-0113 is complete.
Continue from updated main through TL-0114 live audit UI, TL-0115 workshop report and
TL-0116 integrated review and owner gate decision. End the assignment at evidenced M1.
