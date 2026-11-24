# TL-0113 — Short shell check

**Procedure revision:** 2  
**Execution state:** Pending human observation. Every applicable case is currently `Not run`.  
**Scope:** Review-only audit shell on the active Codex machine  
**Participation estimate:** 5–8 minutes, including any settings changes or restoration that need your attention.

## Your short check

### 1. Take one keyboard journey with Narrator

Start at Intake in the prepared ThirdLife window. Put the mouse aside. Move with Tab or
Shift+Tab and activate with Enter or Space. With Narrator, Insert+Tab reads the current item.
Insert+Space toggles scan mode. Turn scan mode off when using Page Down and Page Up in
ThirdLife. [Microsoft's Narrator reading commands](https://support.microsoft.com/en-us/accessibility/windows/narrator/chapter-4-reading-text)

- Check for visible focus on Intake and a normal launch without a UAC prompt. Tab to Inspect
  and activate it. Shift+Tab back to Intake and activate it. Listen for the changed stage
  and compare the requirements.
- Shift+Tab to **Skip to stage review** and activate it. Tab through **Requirements**,
  **Evidence** and **Plan**. Expand and collapse **How to read evidence**. Read **Activity**,
  **Battery safety** and the readiness status.
- Activate **Return to workflow stages** and check that focus returns to Intake. Read all six
  stage names and the unavailable-stage explanations. Confirm that later stages cannot be
  activated. Shift+Tab to **Workflow stages** and use Page Down and Page Up to reach its
  explanations. Use Narrator's scan mode with Up or Down to read non-focusable text.
- Close with Alt+F4, switch to the prepared File Explorer window with the app selected,
  and press Enter to reopen. Check that the shell still says no job is open and shows no
  evidence or plan. Note any lost focus, keyboard trap, confusing speech or missing or
  repeated announcement.

### 2. Check 200% scaling and a smaller display

In Windows Settings, open **System > Display**. Set **Scale** to **200%** at the current
resolution. Switch back to ThirdLife and check that the stage explanations, warning and
lower controls are readable and reachable. Then set **Display resolution** to
**1920 × 1200**. Keep the preview only if you can still use the screen. Windows may
adjust the scale automatically for this mode. Switch back to ThirdLife and check the
same areas once with keyboard scrolling. Report the two observations separately.

### 3. Check contrast

In Settings, open **Accessibility > Contrast themes**, select **Aquatic**, then **Apply**.
Switch to ThirdLife and change stage once. Check focus, the current stage, the warning
and the unavailable-stage explanations. Codex restores and verifies the original Windows
and Narrator settings.

## Reply with four short answers

1. **Keyboard** — OK or problem. Where?
2. **Speech** — Clear or problem. What was missing or confusing?
3. **Display** — Readable and reachable or problem at 200% and at reduced resolution.
4. **Contrast** — Clear or problem. Where?

Say **not checked** for any unfinished check. One sentence per problem is enough. Stop
if you encounter an unexpected prompt or cannot safely continue.

## Codex preparation and recording

The decision is whether the shell supports its keyboard journey, communicates clearly
through Narrator and remains usable with actual Windows display settings, so that TL-0113
can be completed. Prepare and rehearse the exact starting point, normal launch, keyboard
reopen path and supported settings before presenting the short check. The reviewer
supplies observations. Codex records metadata, timing, case outcomes, restoration and
any follow-up. Repeat only a failed or changed interaction. Preserve the original
procedure and pending record at [TL-0113 procedure revision 1](https://github.com/PikkuJanne/ThirdLife/blob/964863ebfc458a63ac559479a40450b652d2d787/artifacts/evidence/TL-0113/human-walkthrough.md).

### Build and environment

- **Source:** `518744b751c531680e24a99b964c3b05a9b7c434` on
  `codex/tl-0113-accessible-shell`. The Release binaries matched the hashes below during
  amendment preflight. Tracked application, test and build-configuration files matched
  that source. Repeat the affected verification and bind the new artifact if these inputs
  change.
- **Release apphost SHA-256:**
  `cb2d204146ee7dbb3b2bbffa83495646fdc1832ba859f4b9ea4545618c6955a8`
- **UI assembly SHA-256:**
  `eab61e2df46c8ef41f845a358f0db5347dae8afc2864742be243e386b4046861`
- **Recorded automated rehearsal:** [Direct-host Release smoke](host-smoke.json) observed
  startup, Inspect navigation and normal cleanup. [Automated results](runs.json) retain
  their original commits, commands and limitations. Codex also observed launch with
  Enter from a prepared Explorer selection, initial Intake focus, Tab/Enter to Inspect
  with the changed review/status, and Alt+F4 and Enter back to empty Intake. Narrator
  startup and exit with Insert+Escape were verified by process state. Windows speech and
  actual display usability require the observations above.
- **Preflight environment:** `REF-CODEX-001` revision `2026-08-29.1`. Direct active-machine
  standard-user interactive Windows 11 Pro x64, version `10.0.26300`. Built-in Windows
  Narrator `10.0.26100.8972`, inactive at preflight. No approved NVDA installation was
  found by checking standard installation, service and command locations.
- **Display rehearsal and restoration point:** 3840×2400 at 59 Hz, 250% Windows scaling,
  contrast off. Verified 200% at the native resolution. The supported 1920×1200 preview
  selected 100% and reverted on timeout. Aquatic enabled contrast and None restored it.
  The native resolution, 250% and contrast off were verified after setup rehearsal.
  Treat the scale and reduced modes as separate observations. After the human session,
  verify restoration of the original resolution, scale, theme and Narrator state.
- **Session specifics:** Launch starts from the prepared File Explorer selection and the
  open shell. The shell interaction uses compiled empty-state text. Reviewer role,
  timestamps with timezone, durations, actual observations and post-session restoration
  verification are pending. Record only sanitized metadata and bounded observations.
  Exclude machine names, usernames, personal paths, screenshots and speech recordings.

### Case mapping

Keep result and evidence class independent. Record `Pass`, `Fail`, `Not available` or
`Not run` for each case with timing, settings, the reviewer's actual observation and any
limitation. Actual human observations use `Human confirmed` where observed. One reply
can support multiple rows only if the completed actions cover each row. Keep failed
observations and link focused reruns. The project-owner M1 gate decision is recorded
separately at TL-0116.

| Case | Observation needed | Result | Evidence class |
| --- | --- | --- | --- |
| A11Y-001 keyboard journey | Reach each relevant region, page, close and reopen without a mouse. | Not run | Not available |
| A11Y-002 focus and order | Visible Intake focus, logical order, working skip and return, no trap. | Not run | Not available |
| A11Y-003 Narrator semantics | Meaningful stage, role, expansion, disabled state, evidence and warning speech. | Not run | Not available |
| A11Y-004 navigation announcements | Changed stage is intelligible with neither silence nor needless repetition. | Not run | Not available |
| A11Y-004 live operation progress | This shell exposes no live operation, cancellation or recovery. Activity fixtures are separate. | Not run | Not available |
| A11Y-005 200% Windows scaling | Essential controls and text remain usable at actual 200% scale. | Not run | Not available |
| A11Y-006 supported reduced resolution | Logical keyboard scrolling reaches essential controls and text. | Not run | Not available |
| A11Y-007 high contrast | Focus, state, warning and unavailable-stage explanations remain perceivable. | Not run | Not available |
| Conditional NVDA | Repeat only if an approved installation is available. | Not available | Not available |

The resulting claims apply to the recorded build, active machine and settings. TL-0116
carries forward valid unchanged observations and checks the new or changed integrated
audit interactions.
