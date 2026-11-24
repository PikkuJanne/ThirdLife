# Accessible six-stage shell — TL-0113

## Scope and workflow

The shell presents **Intake → Inspect → Decide → Prepare → Verify → Handover**.
Intake and Inspect are available for reviewing the audit workflow. Each view separates
requirements, evidence and plan information. Starting the application or changing views
does not create a job, record sanitization, collect device data, evaluate policy or approve
a plan.

An empty view says that no job or evidence is open and that no plan exists. Missing
evidence stays unknown. Navigation is presentation state; entering Inspect does not grant
assessment access or satisfy a sanitization requirement. Decide, Prepare, Verify and
Handover remain visibly disabled. Their criteria and reasons remain readable without
activating a disabled control.

The battery warning remains explicit: **Battery reports cannot detect swelling. Swelling
requires human inspection.** Its meaning must remain available to keyboard and screen-reader
users and at enlarged text sizes.

The production shell has no demonstration run that could be mistaken for real evidence.
Automated fixtures exercise presentation states separately. A fixture completion establishes
only the tested UI behavior; it is not an assessment, policy pass or handover decision.

## Startup and lifecycle

The application requests `asInvoker` execution. `StartupSafety` checks the current Windows
identity and permits only a confirmed standard-user session; elevated or unknown access
shows a fixed recovery message and exits before constructing the shell. It does not
request UAC, relaunch another process to obtain a different token, start a broker or create
a privileged service. The refusal directs the operator to close the elevated launch and
start normally. This is necessary because `asInvoker` alone can inherit an elevated parent
token.

`ShellViewModel` keeps navigation, status and activity presentation separate. Its typed
activity states are Idle, Running, Cancelling, Cancelled, Completed, Failed and RequiresReview.
Progress counts range from zero to 16 work units; they describe work, not device health.
Cancellation requested and confirmed cancellation have different text. Completed work still
states that device readiness has not been verified. The shell starts with "No operation is
running" and exposes no operation runner, cancellation action or retry action. Tests supply
activity states directly; doing so does not start or stop work, record evidence or approve
any outcome.

Future live composition must call the existing domain and service contracts. In particular,
`JobService` and `SanitizationGate` own assessment access, and `AssessmentOrchestrator` owns
collection and durable partial outcomes. The shell must not reproduce their policy or treat
a view change as a durable transition. See [assessment orchestration](../inventory/assessment-orchestration.md).

## Accessibility behavior

- Use standard WPF controls with meaningful names, roles, selection and disabled states.
- Initial focus is on Intake. Intake and Inspect retain the focus when activated; "Skip
  to stage review" focuses the selected review and scrolls it into view. "Return to workflow
  stages" restores focus to the selected stage button. There are no custom shortcut keys.
- Keep primary navigation and readable content in a logical keyboard order with visible
  focus. The named Workflow stages group is itself a tab stop and describes PageDown and
  PageUp scrolling, so keyboard users can expose every disabled stage's readable criteria.
  Those criteria remain outside the disabled control's interaction path.
- Announce navigation and changed operation status through an accessible status region.
  Text distinguishes progress, cancellation requested, cancelled, completed and failed;
  repeated unchanged status must not produce needless announcements.
- Keep requirements, evidence and plan headings explicit. Status and blockers use words,
  so color is never the only source of meaning.
- "How to read evidence" is a standard Expander defining the four evidence classes. The
  Requirements, Evidence, Plan, Activity and Battery safety groups have accessible names
  and descriptions and can receive keyboard focus.
- Use system colors and the system message font family. Text wraps;
  scrolling provides a path to content at 200% scaling, large text and reduced resolution.
- Preserve focus and context during status changes. Future operation controls must make
  cancellation and recovery keyboard reachable when those actions become available.

The layout uses two columns when its available width is at least 720 device-independent
units and stacks the review below the workflow at narrower widths. Both arrangements use
the same vertically scrollable content, without horizontal scrolling. Status and Activity
are polite live regions. The window coalesces changed messages on its dispatcher, waits
for bindings to settle, then raises `LiveRegionChanged`; identical model state causes no
new notification. Closing detaches the model listener and aborts a pending announcement.

The task's automated checks inspect compiled WPF controls, automation peers, focus,
announcement behavior and bounded layout fixtures. A component scale transform and long
string fixture are useful deterministic checks; they do not substitute for a human review
with Windows scaling and assistive technology.

The source-bound [short shell check](../../artifacts/evidence/TL-0113/human-walkthrough.md)
uses one keyboard/Narrator journey and short actual display-mode checks. Codex prepares
and rehearses the build, settings and route, then maps the reviewer's observations to
independent `A11Y-001` through `A11Y-007` case results. The reviewer passed the applicable
shell portions on 24 November 2026. Valid unchanged results carry forward to
TL-0116 with their original provenance. Failed or changed interactions get focused retests.

## Privacy and resources

This shell displays bounded compiled workflow text and current in-memory presentation
state. It creates no job store, attachment, diagnostic log, cache, report or support export.
It reads no personal content and initiates no provider collection, network request,
telemetry or system-setting change. Closing the review shell loses only its temporary
navigation/presentation state; there is no durable job to resume through this surface.

The shell requires no GPU capability and introduces no polling, permanent worker, indexing
or unbounded history. Test progress is driven by bounded synthetic fixtures. Accessibility
meaning, focus and cancellation are retained in reduced-size layouts. Measurements and
same-machine tests establish only their recorded environment and workload; they do not
establish a minimum specification or certify other hardware.

## Verification and M1 continuity

Run the shared verifier with the UI project for this task's Targeted checks:

```powershell
powershell -File eng/verify.ps1 -Tier Targeted `
  -TestProject tests/ThirdLife.UI.Tests/ThirdLife.UI.Tests.csproj
```

Targeted includes Quick. Use the governed same-machine environment if host policy blocks
test loading, and record that limitation. Do not weaken security controls. Human results
must identify the exact source and tested executable; pending fields in the walkthrough
are not completed evidence.

The small reusable `eng/run-ui-smoke.ps1` observer uses installed Windows UI Automation
to start the Release apphost, invoke Inspect, observe its truthful changed status and close
only its owned process. It reports artifact hashes, token class, elapsed/CPU time, peak
working set and cleanup without recording paths or window contents. This direct-host path
is useful when Application Control rejects the xUnit assembly while permitting the UI.
It does not bypass policy or replace the component suite, event-listener test or human
screen-reader review. The Sandbox runs with a different token, so the standard-user
apphost observation remains separate from its component tests.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/run-ui-smoke.ps1
```

The TL-0113 shell provides review-only navigation. TL-0114 owns the live audit UI to
create/open/reopen jobs, record sanitization, start/cancel read-only assessment with
durable outcomes, and capture guided manual results using the existing services.
TL-0115 owns deterministic workshop report generation and opening it from the operator
UI. TL-0116 owns the integrated journey rehearsal, human review of new or changed
interactions, and evidence of the project-owner M1 gate decision.

There is no data migration, sibling integration or verified release-interface addition.
