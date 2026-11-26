# TRACE operator shell

The native WPF window opens at **Jobs**. Create a job to enter Intake, or open
one by its workshop code to see the saved workflow. The current job code is
selectable. **Close job** returns to Jobs and keeps the saved records available.
Startup still admits Windows 11 client on native x64 in a standard-user session.

## Workflow

- **Intake** records the external result and shows the saved evidence and
  current assessment gate. When the record saved without a gate decision,
  **Check saved sanitization record** completes that decision.
- **Inspect** shows the read-only assessment, selectable findings and evidence
  detail. **Hardware checks** contains compiled procedures and manual capture.
  The battery warning is available with battery evidence and hardware checks.
- **Decide** shows the saved policy disposition, controlling reasons and
  policy evidence. **Repair and retest** records completed work and links a
  read-only assessment to that record.
- **Prepare** reviews the resolved operation and captures approval for the exact
  plan. Package execution requires its separate explicit action and any Windows
  consent required by the reviewed route.
- **Verify** presents the saved operation result and its independent checks.
  Review, retry and restart recovery follow the saved state and the availability
  of each operation.
- **Handover** shows outstanding device requirements and the restricted workshop
  report. The report opens the saved artifact through the existing integrity check.

Stage selection changes the visible workspace. The job services determine which
operations are available and own all durable changes. The product contract
defines those boundaries. [Product contract](../product-contract.md)

## Keyboard, layout and appearance

The workflow rail uses native list selection. Editable inputs have persistent
labels, and controls expose their names, selection and command availability
through WPF automation peers. Stage changes and scoped operation updates use
polite live regions. Unavailable operations show their reason in readable text.

At narrower widths, stage navigation wraps above the working area and selected
evidence reflows below its finding list. **Back to findings** restores focus
to the selected finding. The main viewport scrolls vertically, and bounded
evidence and report previews have their own scrolling where needed.

**Appearance** switches between the Dark and Light preferences. Windows high
contrast takes precedence and uses dynamic Windows colors. The app uses the
system message font and distinct focus and selection treatments. Outcome text
continues to convey reasons and limitations in each appearance.

## Verification and prior evidence

The TL-0413 evidence records the tested build, fixture scope and active-machine
observations. Run the targeted UI checks and the independently invokable native
keyboard and UI Automation journey from a verified Release checkout.

```powershell
pwsh -NoProfile -File eng/verify.ps1 -Tier Targeted -TestProject tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj
pwsh -NoProfile -File eng/run-m1-audit-journey.ps1 -VerifyAccessibility
```

For the previous interface, TL-0113 recorded observed keyboard and Narrator
behavior and display-mode checks on 24 November 2026. TL-0116 verified the
earlier integrated audit route. Keep those results with their original tested
surfaces and builds. [TL-0113 observations](../../artifacts/evidence/TL-0113/human-walkthrough.md)
