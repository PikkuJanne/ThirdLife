# M1 — One complete audit journey

**Status:** Exact-candidate rehearsal passed and the isolated review window is ready. Human observations and the M1 decision are pending.  
**Target:** About 10 minutes of participation after preparation.  
**Scope:** Actual WPF and SQLite workflow on the active Codex machine with synthetic intake, providers and manual unavailable/failure examples.

## Your short journey

Start in **ThirdLife Setup Core - M1 audit review (synthetic fixture)**, with Narrator
running and Windows scaling at 200%. Use Tab or Shift+Tab to move and Enter or Space
to activate. Insert+Tab reads the focused item. Insert+Space toggles Narrator scan
mode for reading text outside form controls. Use this sample job to review how the
app presents its workflow and limits.

1. **Create a job.** Use **Go to audit controls**, then **Create job**. Check that the
   job state and next action make sense when spoken and on screen.
2. **Save the sample intake.** The window supplies `m1-fixture-operator`,
   **Sanitized and verified**, **Clear**, the date 1 October 2026 and
   `m1-fixture-media`. Confirm **Use this synthetic intake record**, then choose
   **Save external result**. Check that the fixture limit and saved gate outcome
   are clear.
3. **Run the assessment.** Choose **Start assessment**. Listen for the status and
   open **Assessment evidence**. The synthetic examples include **fixture-keyboard**
   with a collected value, **fixture-unavailable** and **fixture-failed**.
   Check that unknown or failed evidence is clear and completion gives no readiness
   approval.
4. **Save one manual result.** In **Manual hardware checks**, use the preselected
   **Keyboard** check. Choose **Not available**, then **Equipment missing**. Use the
   sample note `No donor keyboard is available in this synthetic fixture.` if the
   note box is empty. Activate **Save manual result**. Check that the guidance and
   saved result explain the limitation.
5. **Reopen the same job.** Choose **Close job**, then **Open job** using the job ID
   left in the form. Check that the assessment and manual result are still visible
   and the next action is clear.
6. **Generate and read the report.** Use **Go to workshop report**, then
   **Generate workshop report** and **Open workshop report**. Check that the report
   belongs to the same job and that the findings, unresolved items and next step are
   consistent with what you reviewed.

Check that the changed job, manual and report controls are readable, focusable and
reachable at the starting 200% scale. At the end, briefly recheck those controls in
the prepared **1920 × 1200** display mode and the **Aquatic** contrast theme. Use
Windows Settings **System → Display** and **Accessibility → Contrast themes**.
Keep a resolution preview only if the screen remains usable. The scale may change
with resolution. Apply the contrast theme and check the controls in the ThirdLife
window once more. Codex restores and verifies the starting settings. Close the
review window when finished so its isolated fixture can be cleaned up.

## Reply with four short answers

1. **Navigation and next action** — reachable by keyboard and clear when spoken, or where a problem occurred.
2. **Findings and limits** — understandable, or what was unclear about the fixture, unavailable example or failure example.
3. **Saved job** — reopened as expected, or what changed or disappeared.
4. **Report** — useful and consistent, or what was missing or wrong.

Say **not checked** for unfinished steps. Mention any clipping, inaccessible focus
or contrast problem. Briefly say whether each display sample worked at **200%**,
**1920 × 1200** and in **Aquatic**.

**Owner decision** — Approve M1 / fix before M1 / decision pending.

**Scope acknowledgement** — I understand these observations cover only this machine
and the tested synthetic audit scenario / I need clarification.

## Codex-only preparation and record

The unresolved question is whether an operator can understand and complete the
integrated audit safely using the changed controls, keyboard, Narrator and display
settings. Rehearse the full route and confirm the exact build, synthetic separation
and protected workspace before presenting it. Codex supplies the open review
window, provides the prefilled sample fields, prepares settings, handles timing,
records only attributable responses, verifies cleanup and restores the environment.
Carry forward matching TL-0113 shell observations and retest a changed or failed
interaction only.

To open a fresh review window after expiry, Codex uses the verified Release build of
the recorded source and invokes only
`ThirdLife.UI.Tests.M1OperatorReviewTests.OpenPreparedFixtureForOperatorReview`
with `dotnet test`, `--no-build --no-restore`, `RunConfiguration.TreatNoTestsAsError=true`
and process-local `THIRDLIFE_M1_OPERATOR_REVIEW=1`. The launcher checks its token,
opens an isolated store and waits up to 30 minutes for the window to close.

| Evidence field | Recorded value |
| --- | --- |
| Tested commit | `da746d306623f860ce64cd1143738087486886ca` |
| Standard-user apphost SHA-256 | `8c3c9f5fed5e8c27b343626073f7f319be0b1fccc0b06f06e59a0ddfdb7e7824` |
| Review launcher and production assembly SHA-256 | `ThirdLife.UI.Tests.dll` `3c1bc6f7059f3f6a0c4a34c3e0ca93687c2bff4619638df69e3d951a1bf30886`, `ThirdLife.UI.dll` `f5cac0f7d32a6d4dffb5fdc4b40d806a69c82b30492d2212697611c89b7f6379`, `ThirdLife.Reports.dll` `7d8500be732862bf8a44a104df0027b3c4aeb1bcd1ed8758953a3d9a4a20db1d` |
| Context, reference profile and standard-user token | Active Windows 11 Pro x64 build 26300, `REF-CODEX-001` revision `2026-08-29.1`. Exact-candidate clean worktree, SDK 10.0.400, Release. Independent main apphost smoke and named fixture rehearsal both verified the standard-user token. |
| Candidate fixture digest and labelled available/unavailable/failure examples | `M1AuditFixture.cs` SHA-256 `92128eeae83336d1d99d12a1ca85dde8c116c241d526bdb40d58543a0b3a3c0c`. Explicitly synthetic intake and three example providers with collected, unavailable and failed outcomes. No donor keyboard sample. |
| Launch receipt | Exact Release launcher started at 17:22:41+01:00 on 24 November 2026. The active window was observed at 17:23:05+01:00 with Intake selected, no job open and the audit controls link available. |
| Actual Narrator and display settings before, during and after | Restoration baseline verified at 17:16:13+01:00 on 24 November 2026. 3840×2400 at 59 Hz, 250% scale, contrast off and Narrator off. Starting review verified at 17:22:29+01:00 at the same resolution and refresh rate, 200% scale, contrast off and Narrator on. Settings is open to System → Display. Human display observations and restoration readings are pending. |
| Operator role, review start/end, duration and exact response | Pending |
| Replacement or focused retest evidence | Pending if required |

| Observation | Coverage in this review | Result |
| --- | --- | --- |
| A11Y-001 and A11Y-002 | New controls and restored focus through the keyboard-only audit and report route. | Pending |
| A11Y-003 and A11Y-004 | Narrator semantics and status on the new gate, assessment, manual and report controls. | Pending |
| A11Y-005 | Actual 200% scale on new controls. | Pending |
| A11Y-006 | Reachability at the supported reduced resolution. | Pending |
| A11Y-007 | Focus, state and meaning in the actual contrast theme. | Pending |
| A11Y-008 and A11Y-010 | Plain next steps, one unavailable manual result, labelled unavailable and failure examples, and usable reopen/report interpretation. | Pending |
| A11Y-009 | Close/reopen context only. This samples recovery navigation. Automated cancellation and process interruption are separate. | Pending |
| Scenario-limit acknowledgement | Operator's explicit response. | Pending |
| M1 gate decision | Project owner's explicit response. | Pending |

See the [gate record](../../gates/M1-audit-only.md) for objective evidence and
[original shell observations](../TL-0113/human-walkthrough.md) for unchanged scope.
